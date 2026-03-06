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
    public static partial class LogStatisticsService
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
                var match = AppConstants.FileTimestampRegex().Match(fileName);
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
