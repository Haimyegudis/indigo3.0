using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndiLogs_3._0.ViewModels
{
    public partial class StatsViewModel
    {
        private List<(string Name, int Count, List<LogEntry> Logs)> BuildLoggerBarChartData(
            List<LogEntry> plcErrors, List<LogEntry> appErrors)
        {
            var combinedCounts = new List<(string Name, int Count, List<LogEntry> Logs)>();

            if (plcErrors.Count > 0)
            {
                var plcGroups = new Dictionary<string, List<LogEntry>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < plcErrors.Count; i++)
                {
                    string key = plcErrors[i].ThreadName ?? "Unknown";
                    if (!plcGroups.TryGetValue(key, out var list))
                    {
                        list = new List<LogEntry>();
                        plcGroups[key] = list;
                    }
                    list.Add(plcErrors[i]);
                }
                foreach (var kvp in plcGroups)
                    combinedCounts.Add(($"[PLC] {kvp.Key}", kvp.Value.Count, kvp.Value));
            }
            if (appErrors.Count > 0)
            {
                var appGroups = new Dictionary<string, List<LogEntry>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < appErrors.Count; i++)
                {
                    string key = appErrors[i].Logger ?? "Unknown";
                    if (!appGroups.TryGetValue(key, out var list))
                    {
                        list = new List<LogEntry>();
                        appGroups[key] = list;
                    }
                    list.Add(appErrors[i]);
                }
                foreach (var kvp in appGroups)
                    combinedCounts.Add(($"[APP] {GetShortLoggerName(kvp.Key)}", kvp.Value.Count, kvp.Value));
            }

            combinedCounts.Sort((a, b) => b.Count.CompareTo(a.Count));
            return combinedCounts.Count > 10 ? combinedCounts.GetRange(0, 10) : combinedCounts;
        }

        private List<(string State, int Count, List<LogEntry> Logs)>? BuildStatePieChartData(
            List<LogEntry> plcLogs, List<StateEntry>? stateEntries)
        {
            var plcErrors = GetErrorLogs(plcLogs);
            if (plcErrors.Count == 0 || stateEntries == null || stateEntries.Count == 0) return null;

            var errorsByState = new Dictionary<string, List<LogEntry>>();
            foreach (var error in plcErrors)
            {
                int lo = 0, hi = stateEntries.Count - 1;
                StateEntry? foundState = null;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var s = stateEntries[mid];
                    if (error.Date < s.StartTime) hi = mid - 1;
                    else if (s.EndTime.HasValue && error.Date > s.EndTime.Value) lo = mid + 1;
                    else { foundState = s; break; }
                }
                if (foundState != null && !string.IsNullOrWhiteSpace(foundState.StateName))
                {
                    if (!errorsByState.TryGetValue(foundState.StateName, out var list))
                    {
                        list = new List<LogEntry>();
                        errorsByState[foundState.StateName] = list;
                    }
                    list.Add(error);
                }
            }

            var pieList = new List<(string State, int Count, List<LogEntry> Logs)>();
            foreach (var kvp in errorsByState)
                pieList.Add((kvp.Key, kvp.Value.Count, kvp.Value));
            pieList.Sort((a, b) => b.Count.CompareTo(a.Count));
            return pieList.Count > 10 ? pieList.GetRange(0, 10) : pieList;
        }

        public struct TimelineData
        {
            public int[] Buckets;
            public List<LogEntry>[] BucketLogs;
            public DateTime FirstTime;
            public double BucketSize;
            public int BucketCount;
        }

        private static TimelineData BuildErrorTimelineChartData(List<LogEntry> errorLogs)
        {
            var result = new TimelineData();
            if (errorLogs.Count == 0) return result;

            errorLogs.Sort((a, b) => a.Date.CompareTo(b.Date));
            result.FirstTime = errorLogs[0].Date;
            var lastTime = errorLogs[errorLogs.Count - 1].Date;
            var totalDuration = lastTime - result.FirstTime;

            if (totalDuration.TotalMinutes < 2) result.BucketCount = 60;
            else if (totalDuration.TotalMinutes < 30) result.BucketCount = 100;
            else result.BucketCount = 120;

            result.BucketSize = totalDuration.TotalSeconds / result.BucketCount;
            result.Buckets = new int[result.BucketCount];
            result.BucketLogs = new List<LogEntry>[result.BucketCount];
            for (int i = 0; i < result.BucketCount; i++)
                result.BucketLogs[i] = new List<LogEntry>();

            for (int i = 0; i < errorLogs.Count; i++)
            {
                int idx = (int)((errorLogs[i].Date - result.FirstTime).TotalSeconds / result.BucketSize);
                if (idx >= result.BucketCount) idx = result.BucketCount - 1;
                if (idx < 0) idx = 0;
                result.Buckets[idx]++;
                result.BucketLogs[idx].Add(errorLogs[i]);
            }

            return result;
        }

        private static List<StateEntry> CalculateStateEntries(List<LogEntry> plcLogs)
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
                {
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

                    if (i < transitionLogs.Count - 1)
                        entry.EndTime = transitionLogs[i + 1].Date;
                    else
                        entry.EndTime = logEndLimit;

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
                    var match = AppConstants.S4StateRegex().Match(l.Message);
                    if (match.Success && match.Groups[2].Value.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                    {
                        enterLogs.Add((l, match.Groups[1].Value.ToUpperInvariant()));
                    }
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

                if (i < enterLogs.Count - 1)
                    entry.EndTime = enterLogs[i + 1].Log.Date;
                else
                    entry.EndTime = logEndLimit;

                statesList.Add(entry);
            }

            return statesList;
        }

        private static void AppendGapSection(StringBuilder sb, List<GapInfo>? gaps)
        {
            sb.AppendLine("--- GAP ANALYSIS (>= 2s) ---");
            if (gaps != null && gaps.Any())
            {
                foreach (var g in gaps)
                {
                    sb.AppendLine($"  #{g.Index} | {g.DurationText} | Start: {g.StartTime:HH:mm:ss.ffffff} | End: {g.EndTime:HH:mm:ss.ffffff}");
                    sb.AppendLine($"      Last Log: {g.LastMessageBeforeGap}");
                }
            }
            else
            {
                sb.AppendLine("  No significant gaps.");
            }
            sb.AppendLine();
        }
    }
}
