using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Computes log statistics (error histograms, load distributions, gap analysis, state errors).
    /// Algorithms extracted from StatsWindow for reuse in scheduled/on-demand scans.
    /// </summary>
    public static class LogStatisticsService
    {
        private static readonly HashSet<string> ErrorLevels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Error", "Fatal" };



        // ====================================================================
        //  Main entry point
        // ====================================================================

        /// <summary>
        /// Computes all statistics from pre-loaded PLC and APP log lists.
        /// </summary>
        public static LogStatisticsResult ComputeStatistics(
            List<LogEntry> plcLogs, List<LogEntry> appLogs, bool hasBinaryAppLogs = false)
        {
            var result = new LogStatisticsResult
            {
                TotalPlcLogs = plcLogs.Count,
                TotalAppLogs = appLogs.Count,
                HasBinaryAppLogs = hasBinaryAppLogs
            };

            // Time span
            DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
            for (int i = 0; i < plcLogs.Count; i++)
            {
                if (plcLogs[i].Date < min) min = plcLogs[i].Date;
                if (plcLogs[i].Date > max) max = plcLogs[i].Date;
            }
            for (int i = 0; i < appLogs.Count; i++)
            {
                if (appLogs[i].Date < min) min = appLogs[i].Date;
                if (appLogs[i].Date > max) max = appLogs[i].Date;
            }
            if (min < DateTime.MaxValue)
            {
                result.EarliestTimestamp = min;
                result.LatestTimestamp = max;
            }

            // Error logs (cached)
            var plcErrors = GetErrorLogs(plcLogs);
            var appErrors = GetErrorLogs(appLogs);
            result.TotalPlcErrors = plcErrors.Count;
            result.TotalAppErrors = appErrors.Count;

            // PLC statistics
            if (plcLogs.Count > 0)
            {
                result.PlcTopErrors = CalculateErrorHistogram(plcErrors, 10);
                result.PlcThreadLoad = CalculateLoadDistribution(plcLogs, l => l.ThreadName, 10);
                result.PlcGaps = FindGaps(plcLogs);
            }

            // APP statistics
            if (appLogs.Count > 0)
            {
                result.AppLoggerErrors = CalculateErrorHistogram(appErrors, 10, l => GetShortLoggerName(l.Logger));
                result.AppLoggerLoad = CalculateLoadDistribution(appLogs, l => GetShortLoggerName(l.Logger), 15, l => l.Logger);

                if (!hasBinaryAppLogs)
                {
                    result.AppMethodErrors = CalculateErrorHistogram(appErrors, 10, l => l.Method ?? "(unknown)");
                    result.AppMethodLoad = CalculateLoadDistribution(appLogs, l => l.Method ?? "(unknown)", 15);
                }

                result.AppGaps = FindGaps(appLogs);
            }

            // Advanced analytics
            var allErrors = new List<LogEntry>(plcErrors.Count + appErrors.Count);
            allErrors.AddRange(plcErrors);
            allErrors.AddRange(appErrors);

            if (allErrors.Count > 0)
            {
                // Errors by source (PLC by thread, APP by logger)
                result.ErrorsBySource = BuildErrorsBySource(plcErrors, appErrors);

                // State entries + errors by state
                result.StateEntries = CalculateStateEntries(plcLogs);
                if (result.StateEntries.Count > 0)
                    result.ErrorsByState = MapErrorsToStates(plcErrors, result.StateEntries);
            }

            return result;
        }

        // ====================================================================
        //  File scanning — parse all logs from files for statistics
        // ====================================================================

        /// <summary>
        /// Parses all log entries from files at the given locations.
        /// Uses parallel file processing (4 concurrent) with thread-safe collections.
        /// Applies time filters from criteria but does NOT evaluate search conditions.
        /// Returns separate PLC and APP log lists sorted by date, plus whether binary APP logs were detected.
        /// </summary>
        public static (List<LogEntry> plcLogs, List<LogEntry> appLogs, bool hasBinaryAppLogs)
            ParseLogsFromLocations(
                IReadOnlyList<SearchLocation> locations,
                SearchCriteria criteria,
                IProgress<(int current, int total, string status)>? progress,
                CancellationToken ct)
        {
            var plcBag = new ConcurrentBag<LogEntry>();
            var appBag = new ConcurrentBag<LogEntry>();
            int hasBinaryFlag = 0; // 0 = false, 1 = true (for Interlocked)
            int completedLocations = 0;
            int totalLocations = locations.Count;
            int totalFiles = 0;
            int completedFiles = 0;

            foreach (var location in locations)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(location.BasePath)) continue;

                progress?.Report((completedLocations, totalLocations, $"Scanning: {location.Name}..."));
                AppLogger.Info($"[Stats] Scanning location \"{location.Name}\" at \"{location.BasePath}\"");

                try
                {
                    var files = Directory.GetFiles(location.BasePath, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsLogFile(f, criteria.SearchPLC, criteria.SearchAPP))
                        .ToList();

                    // Statistics intentionally does NOT apply the file time filter.
                    // File time filter is a performance optimisation for search; statistics
                    // should always scan all available files so the user gets a complete picture.
                    // Individual log entries are still filtered by ResultTimeFilter below.

                    totalFiles += files.Count;
                    AppLogger.Info($"[Stats] Location \"{location.Name}\": {files.Count} file(s) to scan (no file-time filter for statistics)");

                    // Process files in parallel (up to 4 concurrent)
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 4,
                        CancellationToken = CancellationToken.None
                    };

                    Parallel.ForEach(files, parallelOptions, (file, loopState) =>
                    {
                        if (ct.IsCancellationRequested) { loopState.Break(); return; }

                        string logType = DetermineLogType(file);
                        if (IsNumericAppFileName(Path.GetFileName(file).ToLowerInvariant()))
                            Interlocked.Exchange(ref hasBinaryFlag, 1);

                        try
                        {
                            var fileSw = System.Diagnostics.Stopwatch.StartNew();
                            int parsed = 0;

                            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                parsed = ParseZipFile(file, criteria, plcBag, appBag, ref hasBinaryFlag, ct);
                            }
                            else
                            {
                                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    parsed = ParseStream(fs, logType, criteria, plcBag, appBag, ct);
                                }
                            }

                            int done = Interlocked.Increment(ref completedFiles);
                            if (parsed > 0)
                                AppLogger.Info($"[Stats] File \"{Path.GetFileName(file)}\": {parsed:N0} entries in {fileSw.ElapsedMilliseconds}ms ({done}/{totalFiles})");
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            Interlocked.Increment(ref completedFiles);
                            AppLogger.Warn($"[Stats] Error reading '{Path.GetFileName(file)}': {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Stats] Cannot enumerate files at '{location.BasePath}': {ex.Message}");
                }

                completedLocations++;
                progress?.Report((completedLocations, totalLocations, $"Completed: {location.Name}"));
            }

            // Convert to sorted lists (needed for gap analysis and state detection)
            var plcLogs = plcBag.ToList();
            plcLogs.Sort((a, b) => a.Date.CompareTo(b.Date));
            var appLogs = appBag.ToList();
            appLogs.Sort((a, b) => a.Date.CompareTo(b.Date));
            bool hasBinary = hasBinaryFlag == 1;

            AppLogger.Info($"[Stats] Total parsed: {plcLogs.Count:N0} PLC + {appLogs.Count:N0} APP entries, binary={hasBinary}");
            return (plcLogs, appLogs, hasBinary);
        }

        private static int ParseZipFile(string zipPath, SearchCriteria criteria,
            ConcurrentBag<LogEntry> plcBag, ConcurrentBag<LogEntry> appBag, ref int hasBinaryFlag, CancellationToken ct)
        {
            int totalParsed = 0;
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                        string entryLower = entry.FullName.ToLowerInvariant();

                        // Nested ZIP
                        if (entryLower.EndsWith(".zip"))
                        {
                            try
                            {
                                using (var entryStream = entry.Open())
                                using (var ms = new MemoryStream())
                                {
                                    entryStream.CopyTo(ms);
                                    ms.Position = 0;
                                    using (var innerArchive = new ZipArchive(ms, ZipArchiveMode.Read))
                                    {
                                        foreach (var innerEntry in innerArchive.Entries)
                                        {
                                            if (ct.IsCancellationRequested) break;
                                            if (string.IsNullOrWhiteSpace(innerEntry.Name)) continue;
                                            if (!IsLogEntry(innerEntry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                                            string innerLogType = DetermineLogType(innerEntry.FullName);
                                            if (IsNumericAppFileName(innerEntry.Name.ToLowerInvariant()))
                                                Interlocked.Exchange(ref hasBinaryFlag, 1);

                                            using (var s = innerEntry.Open())
                                                totalParsed += ParseStream(s, innerLogType, criteria, plcBag, appBag, ct);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            {
                                AppLogger.Warn($"[Stats] Error in nested ZIP '{entry.Name}': {ex.Message}");
                            }
                            continue;
                        }

                        if (!IsLogEntry(entry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                        string logType = DetermineLogType(entry.FullName);
                        if (IsNumericAppFileName(entry.Name.ToLowerInvariant()))
                            Interlocked.Exchange(ref hasBinaryFlag, 1);

                        using (var s = entry.Open())
                            totalParsed += ParseStream(s, logType, criteria, plcBag, appBag, ct);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Stats] Error opening ZIP '{zipPath}': {ex.Message}");
            }
            return totalParsed;
        }

        /// <summary>
        /// Parses a single log stream and adds entries to thread-safe collections.
        /// Returns the number of entries parsed.
        /// </summary>
        private static int ParseStream(Stream stream, string logType, SearchCriteria criteria,
            ConcurrentBag<LogEntry> plcBag, ConcurrentBag<LogEntry> appBag, CancellationToken ct)
        {
            int count = 0;
            Stream seekableStream = stream;
            MemoryStream? memoryStream = null;
            if (!stream.CanSeek)
            {
                memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                seekableStream = memoryStream;
            }

            try
            {
                var logReader = new IndigoLogsReader(seekableStream);
                var targetBag = logType == "APP" ? appBag : plcBag;

                while (logReader.MoveToNext())
                {
                    if (ct.IsCancellationRequested) break;

                    var currentLog = logReader.Current;
                    if (currentLog == null) continue;

                    var entry = new LogEntry
                    {
                        Date = currentLog.Time,
                        Level = currentLog.Level?.ToString() ?? "INFO",
                        ThreadName = currentLog.ThreadName ?? "",
                        Logger = currentLog.LoggerName ?? "",
                        Message = currentLog.Message ?? ""
                    };
                    LogParserService.ParseLogEntry(entry);

                    // Apply result time filter
                    if (criteria.ResultTimeFilter != null)
                    {
                        if (criteria.ResultTimeFilter.From.HasValue && entry.Date < criteria.ResultTimeFilter.From.Value) continue;
                        if (criteria.ResultTimeFilter.To.HasValue && entry.Date > criteria.ResultTimeFilter.To.Value) continue;
                    }

                    targetBag.Add(entry);
                    count++;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Stats] Error parsing stream: {ex.Message}");
            }
            finally
            {
                memoryStream?.Dispose();
            }
            return count;
        }

        // ====================================================================
        //  Statistics computation (extracted from StatsWindow.xaml.cs)
        // ====================================================================

        /// <summary>O(n) TopN selection for Dictionary — avoids O(n log n) full sort.</summary>
        internal static List<KeyValuePair<string, int>> TopN(Dictionary<string, int> dict, int n)
        {
            var result = new List<KeyValuePair<string, int>>(Math.Min(n, dict.Count));
            foreach (var kvp in dict)
            {
                if (result.Count < n)
                {
                    result.Add(kvp);
                    if (result.Count == n)
                        result.Sort((a, b) => b.Value.CompareTo(a.Value));
                }
                else if (kvp.Value > result[n - 1].Value)
                {
                    result[n - 1] = kvp;
                    for (int i = n - 2; i >= 0 && result[i].Value < result[i + 1].Value; i--)
                    {
                        var tmp = result[i]; result[i] = result[i + 1]; result[i + 1] = tmp;
                    }
                }
            }
            if (result.Count > 0 && result.Count < n)
                result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }

        internal static List<LogEntry> GetErrorLogs(List<LogEntry> source)
        {
            var result = new List<LogEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Level != null && ErrorLevels.Contains(source[i].Level))
                    result.Add(source[i]);
            }
            return result;
        }

        internal static List<ErrorStat> CalculateErrorHistogram(List<LogEntry> errors, int take, Func<LogEntry, string>? keySelector = null)
        {
            if (errors.Count == 0) return new List<ErrorStat>();

            keySelector = keySelector ?? (l => TruncateMessage(l.Message, 100));

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < errors.Count; i++)
            {
                string key = keySelector(errors[i]);
                if (counts.TryGetValue(key, out int c))
                    counts[key] = c + 1;
                else
                    counts[key] = 1;
            }

            var topItems = TopN(counts, take);
            if (topItems.Count == 0) return new List<ErrorStat>();

            int maxCount = topItems[0].Value;

            var result = new List<ErrorStat>(topItems.Count);
            foreach (var kvp in topItems)
            {
                result.Add(new ErrorStat
                {
                    Name = kvp.Key,
                    Message = kvp.Key,
                    Count = kvp.Value,
                    DisplayText = kvp.Value.ToString("N0"),
                    BarWidth = maxCount > 0 ? (double)kvp.Value / maxCount * 200 : 0
                });
            }
            return result;
        }

        internal static List<LoadStat> CalculateLoadDistribution(List<LogEntry> logs, Func<LogEntry, string> keySelector, int take, Func<LogEntry, string>? fullNameSelector = null)
        {
            var counts = new Dictionary<string, int>();
            var firstLog = new Dictionary<string, LogEntry>();
            for (int i = 0; i < logs.Count; i++)
            {
                string key = keySelector(logs[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (counts.TryGetValue(key, out int c))
                    counts[key] = c + 1;
                else
                {
                    counts[key] = 1;
                    firstLog[key] = logs[i];
                }
            }

            if (counts.Count == 0) return new List<LoadStat>();

            var topItems = TopN(counts, take);
            int maxCount = topItems[0].Value;
            int total = logs.Count;

            var result = new List<LoadStat>(topItems.Count);
            foreach (var kvp in topItems)
            {
                double pct = (double)kvp.Value / total * 100;
                result.Add(new LoadStat
                {
                    Name = kvp.Key,
                    FullName = fullNameSelector != null ? fullNameSelector(firstLog[kvp.Key]) : kvp.Key,
                    Count = kvp.Value,
                    Percentage = pct,
                    DisplayText = $"{kvp.Value:N0} ({pct:F1}%)",
                    BarWidth = maxCount > 0 ? (double)kvp.Value / maxCount * 200 : 0
                });
            }
            return result;
        }

        internal static List<GapInfo> FindGaps(List<LogEntry> logs)
        {
            var gaps = new List<GapInfo>();
            if (logs == null || logs.Count < 2) return gaps;

            const double threshold = 2.0;
            for (int i = 1; i < logs.Count; i++)
            {
                var diff = logs[i].Date - logs[i - 1].Date;
                if (diff.TotalSeconds >= threshold)
                {
                    gaps.Add(new GapInfo
                    {
                        Index = gaps.Count + 1,
                        StartTime = logs[i - 1].Date,
                        EndTime = logs[i].Date,
                        Duration = diff,
                        DurationText = FormatDuration(diff),
                        LastMessageBeforeGap = TruncateMessage(logs[i - 1].Message, 100),
                        LastLogBeforeGap = logs[i - 1]
                    });
                }
            }
            return gaps;
        }

        /// <summary>
        /// Detects state entries from PLC logs. Supports S6 (PlcMngr transitions) and S4-5 (STATE_XXX regex).
        /// </summary>
        internal static List<StateEntry> CalculateStateEntries(List<LogEntry> plcLogs)
        {
            var statesList = new List<StateEntry>();
            if (plcLogs.Count == 0) return statesList;

            DateTime logEndLimit = plcLogs[plcLogs.Count - 1].Date;

            // S6: PlcMngr transitions (Manager thread + "PlcMngr:" + "->")
            var transitionLogs = new List<LogEntry>();
            for (int i = 0; i < plcLogs.Count; i++)
            {
                var l = plcLogs[i];
                if (l.ThreadName != null && l.Message != null &&
                    l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                    l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                    l.Message.Contains("->"))
                {
                    transitionLogs.Add(l);
                }
            }

            if (transitionLogs.Count > 0)
            {
                // S6 path — add initial "from" state
                var firstLog = transitionLogs[0];
                var firstParts = firstLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (firstParts.Length >= 2)
                {
                    string initialState = firstParts[0].Replace("PlcMngr:", "").Trim();
                    if (!string.IsNullOrWhiteSpace(initialState))
                    {
                        statesList.Add(new StateEntry
                        {
                            StateName = initialState,
                            TransitionTitle = $"(initial) {initialState}",
                            StartTime = plcLogs[0].Date,
                            EndTime = firstLog.Date,
                            LogReference = firstLog
                        });
                    }
                }

                for (int i = 0; i < transitionLogs.Count; i++)
                {
                    var currentLog = transitionLogs[i];
                    var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    string fromStateRaw = parts[0].Replace("PlcMngr:", "").Trim();
                    string toStateRaw = parts[1].Trim();

                    var entry = new StateEntry
                    {
                        StateName = toStateRaw,
                        TransitionTitle = $"{fromStateRaw} -> {toStateRaw}",
                        StartTime = currentLog.Date,
                        LogReference = currentLog
                    };

                    entry.EndTime = i < transitionLogs.Count - 1
                        ? transitionLogs[i + 1].Date
                        : logEndLimit;

                    statesList.Add(entry);
                }
                return statesList;
            }

            // S4-5 fallback: "==== STATE_XXX - Enter ======"
            var enterLogs = new List<(LogEntry Log, string StateName)>();
            for (int i = 0; i < plcLogs.Count; i++)
            {
                var l = plcLogs[i];
                if (l.Message != null && l.Message.Contains("==== STATE"))
                {
                    var match = AppConstants.S4StateRegex.Match(l.Message);
                    if (match.Success && match.Groups[2].Value.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                        enterLogs.Add((l, match.Groups[1].Value.ToUpperInvariant()));
                }
            }

            if (enterLogs.Count == 0) return statesList;

            for (int i = 0; i < enterLogs.Count; i++)
            {
                var (currentLog, stateName) = enterLogs[i];
                string prevState = i > 0 ? enterLogs[i - 1].StateName : "?";

                var entry = new StateEntry
                {
                    StateName = stateName,
                    TransitionTitle = $"{prevState} -> {stateName}",
                    StartTime = currentLog.Date,
                    LogReference = currentLog
                };

                entry.EndTime = i < enterLogs.Count - 1
                    ? enterLogs[i + 1].Log.Date
                    : logEndLimit;

                statesList.Add(entry);
            }

            return statesList;
        }

        /// <summary>
        /// Maps errors to state intervals using binary search.
        /// </summary>
        internal static List<StatCount> MapErrorsToStates(List<LogEntry> errors, List<StateEntry> stateEntries)
        {
            if (errors.Count == 0 || stateEntries == null || stateEntries.Count == 0)
                return new List<StatCount>();

            var errorsByState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var error in errors)
            {
                int lo = 0, hi = stateEntries.Count - 1;
                string? foundState = null;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var s = stateEntries[mid];
                    if (error.Date < s.StartTime) hi = mid - 1;
                    else if (s.EndTime.HasValue && error.Date > s.EndTime.Value) lo = mid + 1;
                    else { foundState = s.StateName; break; }
                }

                if (foundState != null && !string.IsNullOrWhiteSpace(foundState))
                {
                    if (errorsByState.TryGetValue(foundState, out int c))
                        errorsByState[foundState] = c + 1;
                    else
                        errorsByState[foundState] = 1;
                }
            }

            var result = new List<StatCount>();
            foreach (var kvp in errorsByState)
                result.Add(new StatCount { Name = kvp.Key, Count = kvp.Value });
            result.Sort((a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        private static List<StatCount> BuildErrorsBySource(List<LogEntry> plcErrors, List<LogEntry> appErrors)
        {
            var combined = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // PLC errors by thread
            for (int i = 0; i < plcErrors.Count; i++)
            {
                string key = $"[PLC] {plcErrors[i].ThreadName ?? "Unknown"}";
                if (combined.TryGetValue(key, out int c)) combined[key] = c + 1;
                else combined[key] = 1;
            }

            // APP errors by logger
            for (int i = 0; i < appErrors.Count; i++)
            {
                string key = $"[APP] {GetShortLoggerName(appErrors[i].Logger)}";
                if (combined.TryGetValue(key, out int c)) combined[key] = c + 1;
                else combined[key] = 1;
            }

            var topItems = TopN(combined, 10);
            var result = new List<StatCount>();
            foreach (var kvp in topItems)
                result.Add(new StatCount { Name = kvp.Key, Count = kvp.Value });
            return result;
        }

        // ====================================================================
        //  Helper methods
        // ====================================================================

        internal static string TruncateMessage(string message, int maxLength)
        {
            if (string.IsNullOrEmpty(message)) return "(empty)";
            if (message.Length <= maxLength) return message;
            return message.Substring(0, maxLength) + "...";
        }

        private static readonly Dictionary<string, string> _shortLoggerCache = new Dictionary<string, string>();

        internal static string GetShortLoggerName(string logger)
        {
            if (string.IsNullOrEmpty(logger)) return "Unknown";
            lock (_shortLoggerCache)
            {
                if (_shortLoggerCache.TryGetValue(logger, out var cached)) return cached;
                var parts = logger.Split('.');
                string result = parts.Length <= 2 ? logger : string.Join(".", parts, parts.Length - 2, 2);
                _shortLoggerCache[logger] = result;
                return result;
            }
        }

        internal static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1) return $"{ts.TotalMinutes:F1} min";
            return $"{ts.TotalSeconds:F1} sec";
        }

        // ====================================================================
        //  File classification (copied from GlobalGrepService for independence)
        // ====================================================================

        private static bool IsLogFile(string p, bool plc, bool app)
        {
            string lp = p.ToLowerInvariant();
            if (lp.EndsWith(".zip")) return true;
            return IsSearchableLogFile(lp, plc, app);
        }

        private static bool IsLogEntry(string entryName, bool plc, bool app)
        {
            string lp = entryName.ToLowerInvariant();
            if (lp.EndsWith(".zip")) return false;
            return IsSearchableLogFile(lp, plc, app);
        }

        private static bool IsSearchableLogFile(string lp, bool plc, bool app)
        {
            string fileName = lp;
            int lastSlash = lp.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash >= 0) fileName = lp.Substring(lastSlash + 1);

            bool isPLC = fileName.Contains("enginegroupa.file") ||
                         fileName.Contains("enginegroupb.file") ||
                         fileName.EndsWith(".file.log") ||
                         (fileName.Contains("no-sn") && fileName.Contains("file"));

            bool isAPP = fileName.Contains("appdev") || fileName.Contains("press.host.app");
            if (!isAPP) isAPP = IsNumericAppFileName(fileName);

            return (plc && isPLC) || (app && isAPP);
        }

        private static bool IsNumericAppFileName(string lowerFileName)
        {
            if (lowerFileName.Contains("enginegroup")) return false;
            int dotFileIdx = lowerFileName.IndexOf(".file");
            if (dotFileIdx <= 0) return false;
            string prefix = lowerFileName.Substring(0, dotFileIdx);
            return prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]);
        }

        private static string DetermineLogType(string p)
        {
            string lp = p.ToLowerInvariant();
            string fileName = Path.GetFileName(lp);
            if (fileName.Contains("appdev") || fileName.Contains("press.host.app")) return "APP";
            if (IsNumericAppFileName(fileName)) return "APP";
            return "PLC";
        }


        private static List<string> FilterFilesByTimeRange(List<string> files, TimeRangeFilter filter)
        {
            if (filter == null || (!filter.From.HasValue && !filter.To.HasValue))
                return files;

            return files.Where(f =>
            {
                DateTime fileTime;
                string fileName = Path.GetFileNameWithoutExtension(f);
                var match = AppConstants.FileTimestampRegex.Match(fileName);
                if (match.Success)
                {
                    fileTime = new DateTime(
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value),
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value));
                }
                else
                {
                    try { fileTime = File.GetLastWriteTime(f); }
                    catch (Exception ex) { AppLogger.Warn($"[Stats] Cannot read file time for {f}: {ex.Message}"); return true; }
                }

                if (filter.From.HasValue && fileTime < filter.From.Value) return false;
                if (filter.To.HasValue && fileTime > filter.To.Value) return false;
                return true;
            }).ToList();
        }
    }
}
