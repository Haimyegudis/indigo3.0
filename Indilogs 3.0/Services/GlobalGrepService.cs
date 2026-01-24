using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
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
                    cancellationToken.ThrowIfCancellationRequested();
                    var session = sessionsList[sessionIndex];
                    string sessionName = Path.GetFileName(session.FileName) ?? $"Session {sessionIndex + 1}";

                    if (session.Logs != null)
                        results.AddRange(SearchLogCollection(session.Logs, matchPredicate, searchMessage, searchException, searchMethod, searchData, session.FilePath, "PLC", sessionName, sessionIndex, cancellationToken));

                    if (session.AppDevLogs != null)
                        results.AddRange(SearchLogCollection(session.AppDevLogs, matchPredicate, searchMessage, searchException, searchMethod, searchData, session.FilePath, "APP", sessionName, sessionIndex, cancellationToken));

                    progress?.Report((sessionIndex + 1, totalSessions, $"Searching: {sessionName}"));
                }
            }, cancellationToken);

            return results;
        }

        public async Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)> progress, CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();
            if (string.IsNullOrWhiteSpace(path)) return results;

            Regex regex = useRegex ? new Regex(searchQuery, RegexOptions.IgnoreCase | RegexOptions.Compiled) : null;
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
                    cancellationToken.ThrowIfCancellationRequested();
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
                        // DEBUG: Log first 3 matches
                        if (results.Count < 3)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GREP] Match #{results.Count + 1} ({logType}):");
                            System.Diagnostics.Debug.WriteLine($"  Level={entry.Level}, Thread={entry.ThreadName}, Logger={entry.Logger}");
                            System.Diagnostics.Debug.WriteLine($"  Message={entry.Message?.Substring(0, Math.Min(50, entry.Message?.Length ?? 0))}...");
                            System.Diagnostics.Debug.WriteLine($"  Method={entry.Method}, Pattern={entry.Pattern}, Data={entry.Data}");
                        }

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
                System.Diagnostics.Debug.WriteLine($"[GREP] Error reading {fileName}: {ex.Message}");
            }
        }


        private List<GrepResult> SearchLogCollection(IEnumerable<LogEntry> logs, Func<string, bool> predicate, bool msg, bool exc, bool meth, bool data, string path, string type, string name, int idx, CancellationToken ct)
        {
            var res = new List<GrepResult>();
            int matchCount = 0;
            foreach (var log in logs)
            {
                ct.ThrowIfCancellationRequested();

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
                    matchCount++;
                    if (matchCount <= 3) // Only debug first 3 matches
                    {
                        System.Diagnostics.Debug.WriteLine($"[GREP] In-Memory Match #{matchCount} ({type}):");
                        System.Diagnostics.Debug.WriteLine($"  Level={log.Level}, Thread={log.ThreadName}, Logger={log.Logger}");
                        System.Diagnostics.Debug.WriteLine($"  Message={log.Message?.Substring(0, Math.Min(50, log.Message?.Length ?? 0))}...");
                        System.Diagnostics.Debug.WriteLine($"  Method={log.Method}, Pattern={log.Pattern}, Data={log.Data}");
                    }
                    res.Add(new GrepResult { Timestamp = log.Date, FilePath = path, LogType = type, PreviewText = log.Message, SessionName = name, ReferencedLogEntry = log, SessionIndex = idx, LineNumber = -1 });
                }
            }
            return res;
        }

        private bool IsLineMatch(string line, string query, Regex regex, bool useRegex)
        {
            if (useRegex && regex != null) return regex.IsMatch(line);
            // תיקון: שימוש בשם פרמטר נכון query במקום searchQuery
            if (QueryParserService.HasBooleanOperators(query: query)) return EvaluateQueryOnText(line, _queryParser.Parse(query, out _));
            return line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Func<string, bool> CreateMatchPredicate(string q, bool useReg)
        {
            if (useReg) { try { var r = new Regex(q, RegexOptions.IgnoreCase); return t => !string.IsNullOrEmpty(t) && r.IsMatch(t); } catch { } }
            if (QueryParserService.HasBooleanOperators(query: q)) { var node = _queryParser.Parse(q, out _); return t => !string.IsNullOrEmpty(t) && EvaluateQueryOnText(t, node); }
            return t => !string.IsNullOrEmpty(t) && t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool EvaluateQueryOnText(string text, FilterNode node)
        {
            if (node == null || string.IsNullOrEmpty(text)) return false;
            // תיקון: שימוש ב-node.Type (Enum) במקום node.NodeType (String)
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
                var entries = archive.Entries.Where(e => IsLogFile(e.FullName, plc, app)).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    prog?.Report((i, entries.Count, $"Scanning: {entries[i].Name}"));
                    using (var s = entries[i].Open())
                    {
                        SearchStream(s, zipPath, entries[i].Name, DetermineLogType(entries[i].FullName), q, r, u, res, ct);
                    }
                }
            }
        }

        private void SearchDirectory(string path, string q, Regex r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)> prog, CancellationToken ct)
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(f => IsLogFile(f, plc, app)).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
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
            bool isPlc = lp.Contains("enginegroupa.file") || lp.Contains("plc") || lp.Contains("main");
            bool isApp = lp.Contains("appdev") || lp.Contains("press.host.app") || lp.Contains("app.log");
            return (plc && isPlc) || (app && isApp);
        }

        private string DetermineLogType(string p) => p.ToLowerInvariant().Contains("app") ? "APP" : "PLC";
    }
}