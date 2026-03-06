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
using IndiLogs_3._0.Services.Interfaces;
using Indigo.Infra.ICL.Core.Logging;

namespace IndiLogs_3._0.Services
{
    public partial class GlobalGrepService : IGlobalGrepService
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
            IProgress<(int current, int total, string status)>? progress,
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
            }, cancellationToken).ConfigureAwait(false);

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
            }).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                AppLogger.Info($"[Grep] SearchLoadedSessionsWithCriteria cancelled — {totalMatches} results found before cancel");
            else
                AppLogger.Info($"[Grep] SearchLoadedSessionsWithCriteria complete: {totalMatches} total matches");
            return collectList ?? new List<GrepResult>();
        }

        public async Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();
            if (string.IsNullOrWhiteSpace(path)) return results;

            Regex? regex = useRegex ? new Regex(searchQuery, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)) : null;
            bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() => {
                if (isZip) SearchZipFile(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
                else if (Directory.Exists(path)) SearchDirectory(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);

            return results.OrderBy(r => r.Timestamp).ToList();
        }

        private void SearchStream(Stream stream, string filePath, string fileName, string logType, string searchQuery, Regex? regex, bool useRegex, List<GrepResult> results, CancellationToken cancellationToken)
        {
            int lineNumber = 0;

            try
            {
                // IndigoLogsReader requires a seekable stream, so copy to MemoryStream if needed
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

        private bool IsLineMatch(string line, string query, Regex? regex, bool useRegex)
        {
            if (useRegex && regex != null) return regex.IsMatch(line);
            // Fix: use correct parameter name query instead of searchQuery
            if (QueryParserService.HasBooleanOperators(query: query)) return EvaluateQueryOnText(line, _queryParser.Parse(query, out _));
            return line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Func<string, bool> CreateMatchPredicate(string q, bool useReg)
        {
            if (useReg) { try { var r = new Regex(q, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)); return t => !string.IsNullOrEmpty(t) && r.IsMatch(t); } catch (Exception ex) { AppLogger.Warn($"Invalid regex pattern '{q}': {ex.Message}"); } }
            if (QueryParserService.HasBooleanOperators(query: q)) { var node = _queryParser.Parse(q, out _); return t => !string.IsNullOrEmpty(t) && EvaluateQueryOnText(t, node); }
            return t => !string.IsNullOrEmpty(t) && t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool EvaluateQueryOnText(string text, FilterNode? node)
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

        private void SearchZipFile(string zipPath, string q, Regex? r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)>? prog, CancellationToken ct)
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

        private void SearchDirectory(string path, string q, Regex? r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)>? prog, CancellationToken ct)
        {
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(f => IsLogFile(f, plc, app)).ToList();
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

    }
}