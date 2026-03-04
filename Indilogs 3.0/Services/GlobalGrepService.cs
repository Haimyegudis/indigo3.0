using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using Indigo.Infra.ICL.Core.Logging;

namespace IndiLogs_3._0.Services
{
    public class GlobalGrepService
    {
        private readonly QueryParserService _queryParser;

        public GlobalGrepService()
        {
            _queryParser = new QueryParserService();
        }

        public async Task<List<GrepResult>> SearchLoadedSessionsAsync(
            IEnumerable<LogSessionData> loadedSessions,
            string searchQuery,
            bool useRegex,
            bool searchMessage,
            bool searchException,
            bool searchMethod,
            bool searchData,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();
            var sessionsList = loadedSessions.ToList();
            int totalSessions = sessionsList.Count;

            Func<string, bool> matchPredicate = CreateMatchPredicate(searchQuery, useRegex);

            await Task.Run(() =>
            {
                for (int sessionIndex = 0; sessionIndex < sessionsList.Count; sessionIndex++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var session = sessionsList[sessionIndex];
                    string sessionName = Path.GetFileName(session.FileName) ?? $"Session {sessionIndex + 1}";

                    if (session.Logs != null)
                        results.AddRange(SearchLogCollection(session.Logs, matchPredicate, searchMessage, searchException, searchMethod, searchData, session.FilePath, "PLC", sessionName, sessionIndex, cancellationToken));

                    if (session.AppDevLogs != null)
                        results.AddRange(SearchLogCollection(session.AppDevLogs, matchPredicate, searchMessage, searchException, searchMethod, searchData, session.FilePath, "APP", sessionName, sessionIndex, cancellationToken));

                    progress?.Report((sessionIndex + 1, totalSessions, $"Searching: {sessionName}"));
                }
            });

            return results;
        }

        /// <summary>
        /// Searches in-memory loaded sessions using structured <see cref="SearchCriteria"/>.
        /// When <paramref name="onResult"/> is provided, each match is streamed immediately to the caller.
        /// </summary>
        public async Task<List<GrepResult>> SearchLoadedSessionsWithCriteriaAsync(
            IEnumerable<LogSessionData> loadedSessions,
            SearchCriteria criteria,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken,
            Action<GrepResult>? onResult = null)
        {
            var sessionsList = loadedSessions.ToList();
            int totalSessions = sessionsList.Count;
            int totalMatches = 0;

            // If no streaming callback, collect results
            var collectList = onResult == null ? new List<GrepResult>() : null;
            Action<GrepResult> effectiveCallback = onResult ?? (r => collectList!.Add(r));

            AppLogger.Info($"[Grep] SearchLoadedSessionsWithCriteria: {totalSessions} session(s), PLC={criteria.SearchPLC}, APP={criteria.SearchAPP}");

            await Task.Run(() =>
            {
                for (int sessionIndex = 0; sessionIndex < sessionsList.Count; sessionIndex++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var session = sessionsList[sessionIndex];
                    string sessionName = Path.GetFileName(session.FileName) ?? $"Session {sessionIndex + 1}";

                    progress?.Report((sessionIndex + 1, totalSessions, $"Searching loaded: {sessionName}"));

                    if (criteria.SearchPLC && session.Logs != null)
                    {
                        int matchCount = 0;
                        for (int i = 0; i < session.Logs.Count; i++)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            var entry = session.Logs[i];

                            if (criteria.ResultTimeFilter != null)
                            {
                                if (criteria.ResultTimeFilter.From.HasValue && entry.Date < criteria.ResultTimeFilter.From.Value) continue;
                                if (criteria.ResultTimeFilter.To.HasValue && entry.Date > criteria.ResultTimeFilter.To.Value) continue;
                            }

                            if (EvaluateCriteria(entry, criteria))
                            {
                                matchCount++;
                                effectiveCallback(new GrepResult
                                {
                                    Timestamp = entry.Date,
                                    FilePath = session.FilePath,
                                    LineNumber = i + 1,
                                    LogType = "PLC",
                                    PreviewText = entry.Message,
                                    SessionName = sessionName,
                                    ReferencedLogEntry = entry,
                                    SessionIndex = sessionIndex,
                                    MatchedField = DetermineMatchedFields(entry, criteria)
                                });
                            }
                        }
                        totalMatches += matchCount;
                        AppLogger.Info($"[Grep] PLC logs in '{sessionName}': {session.Logs.Count} entries, {matchCount} matches");
                    }

                    if (criteria.SearchAPP && session.AppDevLogs != null)
                    {
                        int matchCount = 0;
                        for (int i = 0; i < session.AppDevLogs.Count; i++)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            var entry = session.AppDevLogs[i];

                            if (criteria.ResultTimeFilter != null)
                            {
                                if (criteria.ResultTimeFilter.From.HasValue && entry.Date < criteria.ResultTimeFilter.From.Value) continue;
                                if (criteria.ResultTimeFilter.To.HasValue && entry.Date > criteria.ResultTimeFilter.To.Value) continue;
                            }

                            if (EvaluateCriteria(entry, criteria))
                            {
                                matchCount++;
                                effectiveCallback(new GrepResult
                                {
                                    Timestamp = entry.Date,
                                    FilePath = session.FilePath,
                                    LineNumber = i + 1,
                                    LogType = "APP",
                                    PreviewText = entry.Message,
                                    SessionName = sessionName,
                                    ReferencedLogEntry = entry,
                                    SessionIndex = sessionIndex,
                                    MatchedField = DetermineMatchedFields(entry, criteria)
                                });
                            }
                        }
                        totalMatches += matchCount;
                        AppLogger.Info($"[Grep] APP logs in '{sessionName}': {session.AppDevLogs.Count} entries, {matchCount} matches");
                    }
                }
            });

            if (cancellationToken.IsCancellationRequested)
                AppLogger.Info($"[Grep] SearchLoadedSessionsWithCriteria cancelled — {totalMatches} results found before cancel");
            else
                AppLogger.Info($"[Grep] SearchLoadedSessionsWithCriteria complete: {totalMatches} total matches");
            return collectList ?? new List<GrepResult>();
        }

        public async Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)> progress, CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();
            if (string.IsNullOrWhiteSpace(path)) return results;

            Regex regex = useRegex ? new Regex(searchQuery, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)) : null;
            bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() => {
                if (isZip) SearchZipFile(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
                else if (Directory.Exists(path)) SearchDirectory(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
            }, cancellationToken);

            return results.OrderBy(r => r.Timestamp).ToList();
        }

        private void SearchStream(Stream stream, string filePath, string fileName, string logType, string searchQuery, Regex regex, bool useRegex, List<GrepResult> results, CancellationToken cancellationToken)
        {
            int lineNumber = 0;

            try
            {
                // IndigoLogsReader requires a seekable stream, so copy to MemoryStream if needed
                Stream seekableStream = stream;
                MemoryStream memoryStream = null;

                if (!stream.CanSeek)
                {
                    memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    seekableStream = memoryStream;
                }

                try
                {
                    // Use IndigoLogsReader for proper parsing
                    var logReader = new IndigoLogsReader(seekableStream);

                    while (logReader.MoveToNext())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    lineNumber++;

                    var currentLog = logReader.Current;
                    if (currentLog == null) continue;

                    // Convert IndigoLog to LogEntry
                    var entry = new LogEntry
                    {
                        Date = currentLog.Time,
                        Level = currentLog.Level?.ToString() ?? "INFO",
                        ThreadName = currentLog.ThreadName ?? "",
                        Logger = currentLog.LoggerName ?? "",
                        Message = currentLog.Message ?? ""
                    };

                    // Parse Pattern, Data, Method, Exception from Message
                    LogParserService.ParseLogEntry(entry);

                    // Check if this log matches the search query
                    bool isMatch = false;
                    if (useRegex && regex != null)
                    {
                        isMatch = (!string.IsNullOrEmpty(entry.Message) && regex.IsMatch(entry.Message)) ||
                                  (!string.IsNullOrEmpty(entry.Exception) && regex.IsMatch(entry.Exception)) ||
                                  (!string.IsNullOrEmpty(entry.Method) && regex.IsMatch(entry.Method)) ||
                                  (!string.IsNullOrEmpty(entry.Data) && regex.IsMatch(entry.Data));
                    }
                    else
                    {
                        isMatch = (!string.IsNullOrEmpty(entry.Message) && entry.Message.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Exception) && entry.Exception.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Method) && entry.Method.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Data) && entry.Data.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    if (isMatch)
                    {
                        results.Add(new GrepResult
                        {
                            Timestamp = entry.Date,
                            FilePath = filePath,
                            LineNumber = lineNumber,
                            LogType = logType,
                            PreviewText = entry.Message,
                            SessionName = fileName,
                            ReferencedLogEntry = entry,
                            SessionIndex = -1
                        });
                    }
                }
                }

                finally
                {
                    memoryStream?.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Searching ZIP entry failed", ex);
            }
        }


        private List<GrepResult> SearchLogCollection(IEnumerable<LogEntry> logs, Func<string, bool> predicate, bool msg, bool exc, bool meth, bool data, string path, string type, string name, int idx, CancellationToken ct)
        {
            var res = new List<GrepResult>();
            foreach (var log in logs)
            {
                if (ct.IsCancellationRequested) break;

                // Ensure all fields are parsed (Pattern, Data, Exception) if not already
                if (string.IsNullOrEmpty(log.Pattern) && !string.IsNullOrEmpty(log.Message))
                {
                    LogParserService.ParseLogEntry(log);
                }

                bool isMatch = (msg && !string.IsNullOrEmpty(log.Message) && predicate(log.Message)) ||
                               (exc && !string.IsNullOrEmpty(log.Exception) && predicate(log.Exception)) ||
                               (meth && !string.IsNullOrEmpty(log.Method) && predicate(log.Method)) ||
                               (data && !string.IsNullOrEmpty(log.Data) && predicate(log.Data));
                if (isMatch)
                {
                    res.Add(new GrepResult { Timestamp = log.Date, FilePath = path, LogType = type, PreviewText = log.Message, SessionName = name, ReferencedLogEntry = log, SessionIndex = idx, LineNumber = -1 });
                }
            }
            return res;
        }

        private bool IsLineMatch(string line, string query, Regex regex, bool useRegex)
        {
            if (useRegex && regex != null) return regex.IsMatch(line);
            // Fix: use correct parameter name query instead of searchQuery
            if (QueryParserService.HasBooleanOperators(query: query)) return EvaluateQueryOnText(line, _queryParser.Parse(query, out _));
            return line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Func<string, bool> CreateMatchPredicate(string q, bool useReg)
        {
            if (useReg) { try { var r = new Regex(q, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); return t => !string.IsNullOrEmpty(t) && r.IsMatch(t); } catch (Exception ex) { AppLogger.Warn($"Invalid regex pattern '{q}': {ex.Message}"); } }
            if (QueryParserService.HasBooleanOperators(query: q)) { var node = _queryParser.Parse(q, out _); return t => !string.IsNullOrEmpty(t) && EvaluateQueryOnText(t, node); }
            return t => !string.IsNullOrEmpty(t) && t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool EvaluateQueryOnText(string text, FilterNode node)
        {
            if (node == null || string.IsNullOrEmpty(text)) return false;
            // Fix: use node.Type (Enum) instead of node.NodeType (String)
            if (node.Type == NodeType.Condition)
            {
                bool match = text.IndexOf(node.Value ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
                return (node.LogicalOperator?.Contains("NOT") == true) ? !match : match;
            }
            if (node.Children == null) return false;
            var results = node.Children.Select(c => EvaluateQueryOnText(text, c));
            bool res = (node.LogicalOperator?.Contains("OR") == true) ? results.Any(r => r) : results.All(r => r);
            return (node.LogicalOperator?.Contains("NOT") == true) ? !res : res;
        }

        private void SearchZipFile(string zipPath, string q, Regex r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)> prog, CancellationToken ct)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var allEntries = archive.Entries.ToList();
                for (int i = 0; i < allEntries.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    var entry = allEntries[i];
                    if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                    string entryLower = entry.FullName.ToLowerInvariant();

                    // Nested ZIP — extract to memory and recurse
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
                                    var innerEntries = innerArchive.Entries
                                        .Where(e => IsLogEntry(e.FullName, plc, app)).ToList();
                                    foreach (var innerEntry in innerEntries)
                                    {
                                        if (ct.IsCancellationRequested) break;
                                        prog?.Report((i, allEntries.Count, $"Scanning: {entry.Name}/{innerEntry.Name}"));
                                        using (var s = innerEntry.Open())
                                        {
                                            SearchStream(s, zipPath, $"{entry.Name}/{innerEntry.Name}",
                                                DetermineLogType(innerEntry.FullName), q, r, u, res, ct);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            AppLogger.Warn($"[Grep] Error reading nested ZIP '{entry.Name}': {ex.Message}");
                        }
                        continue;
                    }

                    if (!IsLogEntry(entry.FullName, plc, app)) continue;

                    prog?.Report((i, allEntries.Count, $"Scanning: {entry.Name}"));
                    using (var s = entry.Open())
                    {
                        SearchStream(s, zipPath, entry.Name, DetermineLogType(entry.FullName), q, r, u, res, ct);
                    }
                }
            }
        }

        private void SearchDirectory(string path, string q, Regex r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)> prog, CancellationToken ct)
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(f => IsLogFile(f, plc, app)).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                prog?.Report((i, files.Count, $"Scanning: {Path.GetFileName(files[i])}"));
                using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    SearchStream(fs, files[i], Path.GetFileName(files[i]), DetermineLogType(files[i]), q, r, u, res, ct);
                }
            }
        }

        private bool IsLogFile(string p, bool plc, bool app)
        {
            string lp = p.ToLowerInvariant();
            // Always include ZIP files — they'll be opened and entries filtered individually
            if (lp.EndsWith(".zip")) return true;
            return IsSearchableLogFile(lp, plc, app);
        }

        /// <summary>
        /// Checks if an entry inside a ZIP is a searchable log file (not a ZIP itself — those are handled separately).
        /// </summary>
        private bool IsLogEntry(string entryName, bool plc, bool app)
        {
            string lp = entryName.ToLowerInvariant();
            if (lp.EndsWith(".zip")) return false; // nested ZIPs handled separately
            return IsSearchableLogFile(lp, plc, app);
        }

        /// <summary>
        /// Determines if a file is a searchable PLC or APP log.
        /// Uses the same classification rules as LogFileService:
        ///
        /// PLC files:
        ///   - engineGroupA.file (+ numbered: .file.log.49)
        ///   - engineGroupB.file
        ///   - ends with .file.log
        ///   - contains "no-sn" AND contains "file"
        ///
        /// APP files (S6 text):
        ///   - contains "appdev" or "press.host.app"
        /// APP files (S4-5 binary):
        ///   - numeric prefix before .file (e.g. 50300001.file, 50300001.file.log.8865)
        ///   - NOT containing "enginegroup"
        ///
        /// Everything else (terminal logs, CSV, DB, screenshots, configs) is skipped.
        /// </summary>
        private bool IsSearchableLogFile(string lp, bool plc, bool app)
        {
            string fileName = lp;
            int lastSlash = lp.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash >= 0) fileName = lp.Substring(lastSlash + 1);

            // --- PLC patterns (from LogFileService) ---
            bool isPLC = fileName.Contains("enginegroupa.file") ||
                         fileName.Contains("enginegroupb.file") ||
                         fileName.EndsWith(".file.log") ||
                         (fileName.Contains("no-sn") && fileName.Contains("file"));

            // --- APP patterns (from LogFileService) ---
            // S6 text APP dev logs: contains "appdev" or "press.host.app"
            bool isAPP = fileName.Contains("appdev") || fileName.Contains("press.host.app");
            // S4-5 binary APP logs: numeric prefix before .file, not engineGroup
            if (!isAPP)
                isAPP = IsNumericAppFileName(fileName);

            return (plc && isPLC) || (app && isAPP);
        }

        /// <summary>
        /// Checks if a filename looks like a numeric APP binary log (e.g. "50300001.file.log.123").
        /// Same logic as LogFileService.IsNumericAppFile.
        /// </summary>
        private static bool IsNumericAppFileName(string lowerFileName)
        {
            if (lowerFileName.Contains("enginegroup")) return false;
            int dotFileIdx = lowerFileName.IndexOf(".file");
            if (dotFileIdx <= 0) return false;
            string prefix = lowerFileName.Substring(0, dotFileIdx);
            return prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]);
        }

        private string DetermineLogType(string p)
        {
            string lp = p.ToLowerInvariant();
            string fileName = Path.GetFileName(lp);
            // APP: text dev logs
            if (fileName.Contains("appdev") || fileName.Contains("press.host.app"))
                return "APP";
            // APP: binary logs (S4-5)
            if (IsNumericAppFileName(fileName))
                return "APP";
            // Everything else is PLC
            return "PLC";
        }

        // ====================================================================
        //  Multi-location + criteria-based search
        // ====================================================================

        /// <summary>
        /// Searches multiple locations in parallel using structured <see cref="SearchCriteria"/>.
        /// When <paramref name="onResult"/> is provided, each match is streamed immediately to the caller
        /// (for real-time UI updates). When null, results are collected and returned as a list (batch mode).
        /// </summary>
        public async Task<List<GrepResult>> SearchMultiLocationAsync(
            SearchCriteria criteria,
            IReadOnlyList<SearchLocation> locations,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken,
            Action<GrepResult>? onResult = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var activeLocations = locations.Where(l => l.IsActive).ToList();

            if (criteria.LocationIds != null && criteria.LocationIds.Count > 0)
                activeLocations = activeLocations.Where(l => criteria.LocationIds.Contains(l.Id)).ToList();

            int totalLocations = activeLocations.Count;
            int completedLocations = 0;
            int totalMatches = 0;

            // If no streaming callback, collect results into a thread-safe bag
            var collectBag = onResult == null ? new ConcurrentBag<GrepResult>() : null;
            Action<GrepResult> effectiveCallback = onResult ?? (r => collectBag.Add(r));

            AppLogger.Info($"[Grep] SearchMultiLocation: {totalLocations} active location(s), PLC={criteria.SearchPLC}, APP={criteria.SearchAPP}");

            var tasks = activeLocations.Select(location => Task.Run(() =>
            {
                var locSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    progress?.Report((completedLocations, totalLocations, $"Searching: {location.Name}..."));
                    AppLogger.Info($"[Grep] Searching location \"{location.Name}\" at \"{location.BasePath}\"...");

                    // Wrap callback to tag results with location info
                    Action<GrepResult> locationCallback = r =>
                    {
                        r.LocationName = location.Name;
                        r.LocationAddress = location.Address ?? location.BasePath;
                        Interlocked.Increment(ref totalMatches);
                        effectiveCallback(r);
                    };

                    int matchCount = SearchLocationFiles(location, criteria, cancellationToken, locationCallback);
                    AppLogger.Info($"[Grep] Location \"{location.Name}\" done — {matchCount} result(s) in {locSw.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException)
                {
                    AppLogger.Info($"[Grep] Location \"{location.Name}\" search cancelled");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Grep] Failed to search location '{location.Name}': {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref completedLocations);
                    progress?.Report((completedLocations, totalLocations, $"Completed: {location.Name}"));
                }
            }));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var resultList = collectBag?.OrderBy(r => r.Timestamp).ToList() ?? new List<GrepResult>();
            int count = onResult != null ? totalMatches : resultList.Count;

            if (cancellationToken.IsCancellationRequested)
                AppLogger.Info($"[Grep] SearchMultiLocation cancelled — {count:N0} partial results — {sw.ElapsedMilliseconds}ms");
            else
                AppLogger.Info($"[Grep] SearchMultiLocation complete: {count:N0} results from {activeLocations.Count} locations — {sw.ElapsedMilliseconds}ms");
            return resultList;
        }

        /// <summary>
        /// Searches all matching files in a single location using the given criteria.
        /// Files are processed in parallel for better performance.
        /// Each match is streamed via the <paramref name="onResult"/> callback.
        /// Returns total match count.
        /// </summary>
        private int SearchLocationFiles(SearchLocation location, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            if (string.IsNullOrWhiteSpace(location.BasePath))
            {
                AppLogger.Warn($"[Grep] Location \"{location.Name}\" has empty BasePath — skipping");
                return 0;
            }

            // Gather files
            List<string> files;
            try
            {
                files = Directory.GetFiles(location.BasePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsLogFile(f, criteria.SearchPLC, criteria.SearchAPP))
                    .ToList();
                AppLogger.Info($"[Grep] Location \"{location.Name}\": found {files.Count} file(s) to search");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Grep] Cannot enumerate files at '{location.BasePath}': {ex.Message}");
                return 0;
            }

            // Filter files by time range
            if (criteria.FileTimeFilter != null)
            {
                int beforeFilter = files.Count;
                files = FilterFilesByTimeRange(files, criteria.FileTimeFilter);
                AppLogger.Info($"[Grep] Time filter applied: {beforeFilter} → {files.Count} file(s)");

                // Diagnostic: if all files were filtered out, log the date range of what was available
                if (files.Count == 0 && beforeFilter > 0)
                {
                    try
                    {
                        var dates = new List<DateTime>();
                        foreach (var f in Directory.GetFiles(location.BasePath, "*.*", SearchOption.AllDirectories).Take(20))
                        {
                            try { dates.Add(File.GetLastWriteTime(f)); } catch (Exception) { /* file access error during diagnostics */ }
                        }
                        if (dates.Count > 0)
                        {
                            dates.Sort();
                            AppLogger.Warn($"[Grep] All {beforeFilter} files filtered out! Filter: From={criteria.FileTimeFilter.From:yyyy-MM-dd HH:mm}. " +
                                $"Sample file dates: oldest={dates.First():yyyy-MM-dd HH:mm}, newest={dates.Last():yyyy-MM-dd HH:mm}");
                        }
                    }
                    catch (Exception ex) { AppLogger.Warn($"Diagnostic file date sampling failed: {ex.Message}"); }
                }
            }

            if (files.Count == 0)
            {
                AppLogger.Info($"[Grep] Location \"{location.Name}\": no files to search after filtering");
                return 0;
            }

            // Search files in parallel (up to 4 concurrent) for better throughput
            int totalMatches = 0;
            int filesSearched = 0;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = CancellationToken.None // We check ct manually to avoid exceptions
            };

            Parallel.ForEach(files, parallelOptions, (file, loopState) =>
            {
                if (ct.IsCancellationRequested) { loopState.Break(); return; }
                Interlocked.Increment(ref filesSearched);
                try
                {
                    int fileMatches;
                    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        fileMatches = SearchZipWithCriteria(file, criteria, ct, onResult);
                    }
                    else
                    {
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fileMatches = SearchStreamWithCriteria(fs, file, Path.GetFileName(file),
                                DetermineLogType(file), criteria, ct, onResult);
                        }
                    }
                    if (fileMatches > 0)
                    {
                        Interlocked.Add(ref totalMatches, fileMatches);
                        AppLogger.Info($"[Grep] File \"{Path.GetFileName(file)}\": {fileMatches} match(es)");
                    }
                }
                catch (OperationCanceledException)
                {
                    AppLogger.Info($"[Grep] Search cancelled while reading '{Path.GetFileName(file)}'");
                    loopState.Break();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Grep] Error reading '{file}': {ex.Message}");
                }
            });

            AppLogger.Info($"[Grep] Location \"{location.Name}\": searched {filesSearched}/{files.Count} file(s), {totalMatches} total match(es)");
            return totalMatches;
        }

        private int SearchZipWithCriteria(string zipPath, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            int totalMatches = 0;
            AppLogger.Info($"[Grep] Opening ZIP: \"{Path.GetFileName(zipPath)}\"");
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entries = archive.Entries.ToList();
                    AppLogger.Info($"[Grep] ZIP \"{Path.GetFileName(zipPath)}\": {entries.Count} entries");

                    foreach (var entry in entries)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                        string entryLower = entry.FullName.ToLowerInvariant();

                        // Nested ZIP — extract to memory and recurse
                        if (entryLower.EndsWith(".zip"))
                        {
                            AppLogger.Info($"[Grep] Nested ZIP found: \"{entry.Name}\" — extracting...");
                            try
                            {
                                using (var entryStream = entry.Open())
                                using (var ms = new MemoryStream())
                                {
                                    entryStream.CopyTo(ms);
                                    ms.Position = 0;
                                    using (var innerArchive = new ZipArchive(ms, ZipArchiveMode.Read))
                                    {
                                        var innerEntries = innerArchive.Entries.ToList();
                                        int matchable = innerEntries.Count(e => !string.IsNullOrWhiteSpace(e.Name) && IsLogEntry(e.FullName, criteria.SearchPLC, criteria.SearchAPP));
                                        AppLogger.Info($"[Grep] Nested ZIP \"{entry.Name}\": {innerEntries.Count} entries, {matchable} searchable log(s)");

                                        foreach (var innerEntry in innerEntries)
                                        {
                                            if (ct.IsCancellationRequested) break;
                                            if (string.IsNullOrWhiteSpace(innerEntry.Name)) continue;
                                            if (!IsLogEntry(innerEntry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                                            using (var s = innerEntry.Open())
                                            {
                                                totalMatches += SearchStreamWithCriteria(s,
                                                    zipPath, $"{entry.Name}/{innerEntry.Name}",
                                                    DetermineLogType(innerEntry.FullName), criteria, ct, onResult);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            {
                                AppLogger.Warn($"[Grep] Error reading nested ZIP '{entry.Name}' in '{zipPath}': {ex.Message}");
                            }
                            continue;
                        }

                        // Regular log file entry
                        if (!IsLogEntry(entry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                        using (var s = entry.Open())
                        {
                            totalMatches += SearchStreamWithCriteria(s, zipPath, entry.Name,
                                DetermineLogType(entry.FullName), criteria, ct, onResult);
                        }
                    }
                }
                AppLogger.Info($"[Grep] ZIP \"{Path.GetFileName(zipPath)}\" done — {totalMatches} result(s)");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Grep] Error opening ZIP '{zipPath}': {ex.Message}");
            }
            return totalMatches;
        }

        /// <summary>
        /// Parses a log stream and evaluates criteria. Each match is streamed via <paramref name="onResult"/>.
        /// Returns total match count.
        /// </summary>
        private int SearchStreamWithCriteria(Stream stream, string filePath, string fileName,
            string logType, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            int matchCount = 0;
            int lineNumber = 0;

            try
            {
                Stream seekableStream = stream;
                MemoryStream memoryStream = null;
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
                    while (logReader.MoveToNext())
                    {
                        if (ct.IsCancellationRequested) break;
                        lineNumber++;

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

                        // Evaluate structured criteria
                        if (EvaluateCriteria(entry, criteria))
                        {
                            matchCount++;
                            onResult(new GrepResult
                            {
                                Timestamp = entry.Date,
                                FilePath = filePath,
                                LineNumber = lineNumber,
                                LogType = logType,
                                PreviewText = entry.Message,
                                SessionName = fileName,
                                ReferencedLogEntry = entry,
                                SessionIndex = -1,
                                MatchedField = DetermineMatchedFields(entry, criteria)
                            });
                        }
                    }
                }
                finally
                {
                    memoryStream?.Dispose();
                }

                if (ct.IsCancellationRequested)
                    AppLogger.Info($"[Grep] Stream \"{fileName}\" ({logType}): cancelled after {lineNumber} entries, {matchCount} match(es) so far");
                else if (lineNumber > 0 && matchCount > 0)
                    AppLogger.Info($"[Grep] Stream \"{fileName}\" ({logType}): parsed {lineNumber} entries, {matchCount} match(es)");
                else if (lineNumber == 0)
                    AppLogger.Warn($"[Grep] Stream \"{fileName}\" ({logType}): 0 entries parsed (empty or unrecognized format)");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Grep] Error parsing stream '{fileName}': {ex.Message}");
            }

            return matchCount;
        }

        // ====================================================================
        //  File time filtering
        // ====================================================================

        // Matches timestamps like 2024-01-15T14-30-00 or 2024-01-15_14-30-00 in filenames
        private static readonly Regex _fileTimestampRegex = new Regex(
            @"(\d{4})-(\d{2})-(\d{2})[T_](\d{2})-(\d{2})-(\d{2})",
            RegexOptions.Compiled);

        /// <summary>
        /// Filters files by time range using filename timestamp pattern or file modification date as fallback.
        /// </summary>
        public List<string> FilterFilesByTimeRange(List<string> files, TimeRangeFilter filter)
        {
            if (filter == null || (!filter.From.HasValue && !filter.To.HasValue))
                return files;

            return files.Where(f =>
            {
                DateTime fileTime;

                // Try to parse timestamp from filename first
                string fileName = Path.GetFileNameWithoutExtension(f);
                var match = _fileTimestampRegex.Match(fileName);
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
                    // Fallback to file modification date
                    try { fileTime = File.GetLastWriteTime(f); }
                    catch (Exception) { return true; } // Include file if we can't determine its time
                }

                if (filter.From.HasValue && fileTime < filter.From.Value) return false;
                if (filter.To.HasValue && fileTime > filter.To.Value) return false;
                return true;
            }).ToList();
        }

        // ====================================================================
        //  Criteria evaluation (multi-field, groups, logical operators)
        // ====================================================================

        /// <summary>
        /// Evaluates whether a log entry matches the top-level search criteria.
        /// Groups are combined with the criteria's GroupOperator (AND/OR).
        /// </summary>
        public bool EvaluateCriteria(LogEntry entry, SearchCriteria criteria)
        {
            if (criteria.Groups == null || criteria.Groups.Count == 0) return true;

            if (criteria.GroupOperator == LogicalGroupOperator.And)
                return criteria.Groups.All(g => EvaluateGroup(entry, g));
            else
                return criteria.Groups.Any(g => EvaluateGroup(entry, g));
        }

        /// <summary>
        /// Evaluates a group of conditions with the group's operator (AND/OR/NOR).
        /// </summary>
        public bool EvaluateGroup(LogEntry entry, SearchConditionGroup group)
        {
            if (group.Conditions == null || group.Conditions.Count == 0) return true;

            bool result;
            switch (group.Operator)
            {
                case ConditionOperator.And:
                    result = group.Conditions.All(c => EvaluateCondition(entry, c));
                    break;
                case ConditionOperator.Or:
                    result = group.Conditions.Any(c => EvaluateCondition(entry, c));
                    break;
                case ConditionOperator.Nor:
                    result = !group.Conditions.Any(c => EvaluateCondition(entry, c));
                    break;
                default:
                    result = group.Conditions.All(c => EvaluateCondition(entry, c));
                    break;
            }
            return result;
        }

        /// <summary>
        /// Evaluates a single condition against a log entry field, with optional negation.
        /// </summary>
        public bool EvaluateCondition(LogEntry entry, SearchCondition condition)
        {
            // Get all field values to check
            var fieldsToCheck = GetFieldValues(entry, condition.Field);
            bool match = fieldsToCheck.Any(text => MatchText(text, condition.Value, condition.Operator));
            return condition.Negate ? !match : match;
        }

        private List<string> GetFieldValues(LogEntry entry, SearchField field)
        {
            var values = new List<string>();
            switch (field)
            {
                case SearchField.Message:   if (entry.Message != null) values.Add(entry.Message); break;
                case SearchField.Level:     if (entry.Level != null) values.Add(entry.Level); break;
                case SearchField.ThreadName: if (entry.ThreadName != null) values.Add(entry.ThreadName); break;
                case SearchField.Logger:    if (entry.Logger != null) values.Add(entry.Logger); break;
                case SearchField.Method:    if (entry.Method != null) values.Add(entry.Method); break;
                case SearchField.Data:      if (entry.Data != null) values.Add(entry.Data); break;
                case SearchField.Exception: if (entry.Exception != null) values.Add(entry.Exception); break;
                case SearchField.Any:
                    if (entry.Message != null) values.Add(entry.Message);
                    if (entry.Level != null) values.Add(entry.Level);
                    if (entry.ThreadName != null) values.Add(entry.ThreadName);
                    if (entry.Logger != null) values.Add(entry.Logger);
                    if (entry.Method != null) values.Add(entry.Method);
                    if (entry.Data != null) values.Add(entry.Data);
                    if (entry.Exception != null) values.Add(entry.Exception);
                    break;
            }
            return values;
        }

        private bool MatchText(string text, string value, SearchOperator op)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (string.IsNullOrEmpty(value)) return false;

            switch (op)
            {
                case SearchOperator.Contains:
                    return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
                case SearchOperator.Equals:
                    return string.Equals(text, value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.StartsWith:
                    return text.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.EndsWith:
                    return text.EndsWith(value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.Regex:
                    try { return Regex.IsMatch(text, value, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
                    catch (Exception) { return false; }
                default:
                    return false;
            }
        }

        /// <summary>
        /// After a match is confirmed, determines which field(s) actually matched.
        /// Returns a comma-separated string like "Message" or "Message, Exception".
        /// </summary>
        public string DetermineMatchedFields(LogEntry entry, SearchCriteria criteria)
        {
            if (criteria.Groups == null || criteria.Groups.Count == 0) return "";

            var matchedFields = new HashSet<string>();
            var allFields = new[] { SearchField.Message, SearchField.Level, SearchField.ThreadName,
                                    SearchField.Logger, SearchField.Method, SearchField.Data, SearchField.Exception };

            foreach (var group in criteria.Groups)
            {
                if (group.Conditions == null) continue;
                foreach (var condition in group.Conditions)
                {
                    if (string.IsNullOrWhiteSpace(condition.Value)) continue;

                    if (condition.Field == SearchField.Any)
                    {
                        // Check each individual field to see which ones actually matched
                        foreach (var field in allFields)
                        {
                            var values = GetFieldValues(entry, field);
                            if (values.Any(v => MatchText(v, condition.Value, condition.Operator)))
                                matchedFields.Add(field.ToString());
                        }
                    }
                    else
                    {
                        var values = GetFieldValues(entry, condition.Field);
                        if (values.Any(v => MatchText(v, condition.Value, condition.Operator)))
                            matchedFields.Add(condition.Field.ToString());
                    }
                }
            }

            return string.Join(", ", matchedFields);
        }
    }
}