using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Grep
{
    public static partial class LogStatisticsService
    {
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
    }
}
