using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Service for performing global grep searches across loaded sessions or external files.
    /// Supports both in-memory searches (fast) and file-based searches (comprehensive).
    /// </summary>
    public class GlobalGrepService
    {
        private readonly QueryParserService _queryParser;

        public GlobalGrepService()
        {
            _queryParser = new QueryParserService();
        }

        #region In-Memory Search (Loaded Sessions)

        /// <summary>
        /// Searches all loaded sessions for matches.
        /// This is fast because data is already in memory.
        /// </summary>
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
            int processedSessions = 0;

            // Prepare search predicate
            Func<string, bool> matchPredicate;
            if (useRegex)
            {
                try
                {
                    var regex = new Regex(searchQuery, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    matchPredicate = text => !string.IsNullOrEmpty(text) && regex.IsMatch(text);
                }
                catch (ArgumentException)
                {
                    // Invalid regex - fall back to plain text search
                    matchPredicate = text => !string.IsNullOrEmpty(text) &&
                                            text.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            else
            {
                // Check if query has boolean operators
                if (_queryParser.HasBooleanOperators(searchQuery))
                {
                    var filterNode = _queryParser.Parse(searchQuery);
                    matchPredicate = text => !string.IsNullOrEmpty(text) &&
                                            EvaluateQueryOnText(text, filterNode);
                }
                else
                {
                    // Simple contains search
                    matchPredicate = text => !string.IsNullOrEmpty(text) &&
                                            text.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            await Task.Run(() =>
            {
                for (int sessionIndex = 0; sessionIndex < sessionsList.Count; sessionIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var session = sessionsList[sessionIndex];
                    string sessionName = Path.GetFileName(session.FileName) ?? $"Session {sessionIndex + 1}";

                    // Search PLC logs
                    if (session.Logs != null)
                    {
                        foreach (var log in session.Logs)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (CheckLogEntryMatch(log, matchPredicate, searchMessage, searchException, searchMethod, searchData,
                                                   out string matchedField))
                            {
                                results.Add(new GrepResult
                                {
                                    Timestamp = log.Date,
                                    FilePath = session.FilePath,
                                    LogType = "PLC",
                                    PreviewText = TruncateText(log.Message, 500),
                                    SessionName = sessionName,
                                    ReferencedLogEntry = log,
                                    SessionIndex = sessionIndex,
                                    MatchedField = matchedField,
                                    LineNumber = -1 // Not applicable for in-memory logs
                                });
                            }
                        }
                    }

                    // Search APP logs
                    if (session.AppDevLogs != null)
                    {
                        foreach (var log in session.AppDevLogs)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (CheckLogEntryMatch(log, matchPredicate, searchMessage, searchException, searchMethod, searchData,
                                                   out string matchedField))
                            {
                                results.Add(new GrepResult
                                {
                                    Timestamp = log.Date,
                                    FilePath = session.FilePath,
                                    LogType = "APP",
                                    PreviewText = TruncateText(log.Message, 500),
                                    SessionName = sessionName,
                                    ReferencedLogEntry = log,
                                    SessionIndex = sessionIndex,
                                    MatchedField = matchedField,
                                    LineNumber = -1
                                });
                            }
                        }
                    }

                    processedSessions++;
                    progress?.Report((processedSessions, totalSessions, $"Searching session: {sessionName}"));
                }
            }, cancellationToken);

            return results;
        }

        /// <summary>
        /// Checks if a LogEntry matches the search criteria
        /// </summary>
        private bool CheckLogEntryMatch(
            LogEntry log,
            Func<string, bool> matchPredicate,
            bool searchMessage,
            bool searchException,
            bool searchMethod,
            bool searchData,
            out string matchedField)
        {
            matchedField = null;

            if (searchMessage && matchPredicate(log.Message))
            {
                matchedField = "Message";
                return true;
            }

            if (searchException && matchPredicate(log.Exception))
            {
                matchedField = "Exception";
                return true;
            }

            if (searchMethod && matchPredicate(log.Method))
            {
                matchedField = "Method";
                return true;
            }

            if (searchData && matchPredicate(log.Data))
            {
                matchedField = "Data";
                return true;
            }

            return false;
        }

        #endregion

        #region File-Based Search (External Files)

        /// <summary>
        /// Searches external files (ZIP or directory) without loading them into memory.
        /// This is slower but can search files that haven't been opened yet.
        /// </summary>
        public async Task<List<GrepResult>> SearchExternalFilesAsync(
            string path,
            string searchQuery,
            bool useRegex,
            bool searchPLC,
            bool searchAPP,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
            {
                return results;
            }

            // Prepare regex
            Regex regex = null;
            if (useRegex)
            {
                try
                {
                    regex = new Regex(searchQuery, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    // Invalid regex - will fall back to contains search
                }
            }

            bool isZip = File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                if (isZip)
                {
                    SearchZipFile(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
                }
                else if (Directory.Exists(path))
                {
                    SearchDirectory(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
                }
            }, cancellationToken);

            return results;
        }

        /// <summary>
        /// Searches all log files within a ZIP archive
        /// </summary>
        private void SearchZipFile(
            string zipPath,
            string searchQuery,
            Regex regex,
            bool useRegex,
            bool searchPLC,
            bool searchAPP,
            List<GrepResult> results,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.Where(e => IsLogFile(e.FullName, searchPLC, searchAPP)).ToList();
                int totalEntries = entries.Count;
                int processedEntries = 0;

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string logType = DetermineLogType(entry.FullName);
                    progress?.Report((processedEntries, totalEntries, $"Scanning: {entry.FullName}"));

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        SearchStream(reader, zipPath, entry.FullName, logType, searchQuery, regex, useRegex, results, cancellationToken);
                    }

                    processedEntries++;
                }
            }
        }

        /// <summary>
        /// Searches all log files within a directory (recursively)
        /// </summary>
        private void SearchDirectory(
            string directoryPath,
            string searchQuery,
            Regex regex,
            bool useRegex,
            bool searchPLC,
            bool searchAPP,
            List<GrepResult> results,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => IsLogFile(f, searchPLC, searchAPP))
                                 .ToList();

            int totalFiles = files.Count;
            int processedFiles = 0;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string logType = DetermineLogType(filePath);
                progress?.Report((processedFiles, totalFiles, $"Scanning: {Path.GetFileName(filePath)}"));

                using (var reader = new StreamReader(filePath))
                {
                    SearchStream(reader, filePath, Path.GetFileName(filePath), logType, searchQuery, regex, useRegex, results, cancellationToken);
                }

                processedFiles++;
            }
        }

        /// <summary>
        /// Searches a text stream line by line for matches
        /// </summary>
        private void SearchStream(
            StreamReader reader,
            string filePath,
            string fileName,
            string logType,
            string searchQuery,
            Regex regex,
            bool useRegex,
            List<GrepResult> results,
            CancellationToken cancellationToken)
        {
            int lineNumber = 0;
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;

                bool isMatch = false;

                if (useRegex && regex != null)
                {
                    isMatch = regex.IsMatch(line);
                }
                else if (useRegex && regex == null)
                {
                    // Invalid regex - fall back to contains
                    isMatch = line.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    // Boolean query support
                    if (_queryParser.HasBooleanOperators(searchQuery))
                    {
                        var filterNode = _queryParser.Parse(searchQuery);
                        isMatch = EvaluateQueryOnText(line, filterNode);
                    }
                    else
                    {
                        isMatch = line.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }

                if (isMatch)
                {
                    results.Add(new GrepResult
                    {
                        Timestamp = TryExtractTimestamp(line),
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        LogType = logType,
                        PreviewText = TruncateText(line, 500),
                        SessionName = fileName,
                        SessionIndex = -1, // Not applicable for external files
                        MatchedField = "RawLine"
                    });
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Determines if a file is a log file based on its path
        /// </summary>
        private bool IsLogFile(string path, bool includePLC, bool includeAPP)
        {
            string lowerPath = path.ToLowerInvariant();

            bool isPLC = lowerPath.Contains("enginegroupa.file") ||
                         lowerPath.Contains("plc") ||
                         lowerPath.Contains("main");

            bool isAPP = lowerPath.Contains("appdev") ||
                         lowerPath.Contains("press.host.app") ||
                         lowerPath.Contains("app.log");

            return (includePLC && isPLC) || (includeAPP && isAPP);
        }

        /// <summary>
        /// Determines log type (PLC or APP) based on file path
        /// </summary>
        private string DetermineLogType(string path)
        {
            string lowerPath = path.ToLowerInvariant();

            if (lowerPath.Contains("appdev") || lowerPath.Contains("press.host.app") || lowerPath.Contains("app.log"))
                return "APP";

            return "PLC";
        }

        /// <summary>
        /// Attempts to extract a timestamp from a raw log line
        /// </summary>
        private DateTime? TryExtractTimestamp(string line)
        {
            // Try common timestamp formats
            // Format 1: yyyy-MM-dd HH:mm:ss.fff
            var match = Regex.Match(line, @"\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}");
            if (match.Success && DateTime.TryParse(match.Value, out var dt1))
                return dt1;

            // Format 2: yyyy-MM-dd HH:mm:ss
            match = Regex.Match(line, @"\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}");
            if (match.Success && DateTime.TryParse(match.Value, out var dt2))
                return dt2;

            return null;
        }

        /// <summary>
        /// Truncates text to a maximum length for UI display
        /// </summary>
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Evaluates a boolean query (FilterNode) against a text string.
        /// This is a simplified version that works on raw text instead of LogEntry objects.
        /// </summary>
        private bool EvaluateQueryOnText(string text, FilterNode node)
        {
            if (node == null || string.IsNullOrEmpty(text))
                return false;

            if (node.NodeType == "Condition")
            {
                // For text-based search, we only support "contains" style matching
                string value = node.Value ?? string.Empty;
                bool isNegated = node.LogicalOperator?.Contains("NOT") == true;

                bool matches = text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
                return isNegated ? !matches : matches;
            }
            else if (node.NodeType == "Group" && node.Children != null)
            {
                bool isOr = node.LogicalOperator?.Contains("OR") == true;
                bool isNegated = node.LogicalOperator?.Contains("NOT") == true;

                var childResults = node.Children.Select(child => EvaluateQueryOnText(text, child)).ToList();

                bool result;
                if (isOr)
                {
                    result = childResults.Any(r => r);
                }
                else // AND
                {
                    result = childResults.All(r => r);
                }

                return isNegated ? !result : result;
            }

            return false;
        }

        #endregion
    }
}
