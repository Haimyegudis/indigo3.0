using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    }
}