using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class StatsViewModel
    {
        // ==========================================
        //  MAIN CALCULATION — called by View on Loaded
        // ==========================================

        /// <summary>
        /// Runs all heavy statistics on a background thread, then updates properties
        /// (which raise PropertyChanged) so the View can bind/react.
        /// Returns an action the View should run on the Dispatcher to push data to named UI elements.
        /// </summary>
        public async Task CalculateStatisticsAsync()
        {
            var statsSw = System.Diagnostics.Stopwatch.StartNew();
            int totalLogs = _plcLogs.Count + _appLogs.Count;
            if (totalLogs == 0)
            {
                SummaryText = "No logs available for analysis.";
                IsLoading = false;
                return;
            }

            // Local variables to hold results computed on the background thread
            string summaryText = "";
            string plcSummary = "", plcErrorCount = "", plcThreadCount = "", plcGapSummary = "";
            string appSummary = "", appErrorCount = "", appThreadCount = "";
            string appMethodErrorCount = "", appMethodCount = "";
            string appGapSummary = "";
            string analyticsSummary = "", loggerChartCount = "", stateChartCount = "", timelineInfo = "";
            bool plcHasGaps = false, appHasGaps = false, plcHasLogs = false, appHasLogs = false;

            List<ErrorStat> plcErrorStats = null, appThreadErrorStats = null, appMethodErrorStats = null;
            List<LoadStat> plcThreadStats = null, appThreadStats = null, appMethodStats = null;
            List<GapInfo> plcGaps = null, appGaps = null;

            // Chart data built on background
            List<(string Name, int Count, List<LogEntry> Logs)> barChartData = null;
            List<(string State, int Count, List<LogEntry> Logs)> pieChartData = null;
            int[] timelineBuckets = null;
            List<LogEntry>[] timelineBucketLogs = null;
            DateTime timelineFirstTime = default;
            double timelineBucketSize = 0;
            int timelineBucketCount = 0;
            List<StateEntry> timelineStateEntries = null;
            List<(string Logger, int Count)> loggerData = null;
            List<(string State, int Count)> stateData = null;

            await Task.Run(() =>
            {
                // Fast summary
                DateTime minDate = DateTime.MaxValue, maxDate = DateTime.MinValue;
                for (int i = 0; i < _plcLogs.Count; i++)
                {
                    if (_plcLogs[i].Date < minDate) minDate = _plcLogs[i].Date;
                    if (_plcLogs[i].Date > maxDate) maxDate = _plcLogs[i].Date;
                }
                for (int i = 0; i < _appLogs.Count; i++)
                {
                    if (_appLogs[i].Date < minDate) minDate = _appLogs[i].Date;
                    if (_appLogs[i].Date > maxDate) maxDate = _appLogs[i].Date;
                }
                if (minDate < DateTime.MaxValue)
                    summaryText = $"Analyzed {totalLogs:N0} logs spanning {(maxDate - minDate).TotalMinutes:F1} minutes";

                // Cache error logs once
                var plcErrors = GetErrorLogs(_plcLogs);
                var appErrors = GetErrorLogs(_appLogs);

                // PLC Statistics
                plcHasLogs = _plcLogs.Count > 0;
                if (plcHasLogs)
                {
                    plcSummary = $"PLC Logs: {_plcLogs.Count:N0} entries";
                    plcErrorStats = CalculateErrorHistogram(plcErrors, 10);
                    plcErrorCount = plcErrors.Count > 0 ? $"(Total: {plcErrors.Count:N0})" : "(No errors)";
                    plcThreadStats = CalculateLoadDistribution(_plcLogs, l => l.ThreadName, 10);
                    plcThreadCount = plcThreadStats.Count > 0 ? "(Top 10)" : "";
                    plcGaps = FindGaps(_plcLogs);
                    plcHasGaps = plcGaps.Count > 0;
                    if (plcHasGaps)
                        plcGapSummary = $"Found {plcGaps.Count} gap(s) >= 2s. Total: {FormatDuration(TimeSpan.FromSeconds(plcGaps.Sum(g => g.Duration.TotalSeconds)))}";
                    else
                        plcGapSummary = "No significant time gaps.";
                }

                // APP Statistics
                appHasLogs = _appLogs.Count > 0;
                if (appHasLogs)
                {
                    appSummary = $"APP Logs: {_appLogs.Count:N0} entries";
                    appThreadErrorStats = CalculateErrorHistogram(appErrors, 10, l => GetShortLoggerName(l.Logger));
                    appErrorCount = appErrors.Count > 0 ? $"(Total: {appErrors.Count:N0})" : "(No errors)";
                    appThreadStats = CalculateLoadDistribution(_appLogs, l => GetShortLoggerName(l.Logger), 15, l => l.Logger);
                    appThreadCount = "(Top 15)";
                    if (!_hasBinaryAppLogs)
                    {
                        appMethodErrorStats = CalculateErrorHistogram(appErrors, 10, l => l.Method ?? "(unknown)");
                        appMethodErrorCount = appErrors.Count > 0 ? $"(Total: {appErrors.Count:N0})" : "(No errors)";
                        appMethodStats = CalculateLoadDistribution(_appLogs, l => l.Method ?? "(unknown)", 15);
                        appMethodCount = "(Top 15)";
                    }
                    appGaps = FindGaps(_appLogs);
                    appHasGaps = appGaps != null && appGaps.Count > 0;
                    if (appHasGaps)
                        appGapSummary = $"Found {appGaps.Count} gap(s) >= 2s. Total: {FormatDuration(TimeSpan.FromSeconds(appGaps.Sum(g => g.Duration.TotalSeconds)))}";
                    else
                        appGapSummary = "No significant time gaps.";
                }

                // Advanced Analytics
                var allErrors = new List<LogEntry>(plcErrors.Count + appErrors.Count);
                allErrors.AddRange(plcErrors);
                allErrors.AddRange(appErrors);

                if (allErrors.Count > 0)
                {
                    analyticsSummary = $"Advanced Analytics - Total Errors: {allErrors.Count:N0}";
                    barChartData = BuildLoggerBarChartData(plcErrors, appErrors);
                    timelineStateEntries = CalculateStateEntries(_plcLogs);
                    pieChartData = BuildStatePieChartData(_plcLogs, timelineStateEntries);
                    var timelineResult = BuildErrorTimelineChartData(allErrors);
                    timelineBuckets = timelineResult.Buckets;
                    timelineBucketLogs = timelineResult.BucketLogs;
                    timelineFirstTime = timelineResult.FirstTime;
                    timelineBucketSize = timelineResult.BucketSize;
                    timelineBucketCount = timelineResult.BucketCount;

                    loggerChartCount = barChartData != null && barChartData.Count > 0
                        ? $"({barChartData.Sum(x => x.Count):N0} errors) - Click bar to navigate" : "(No data)";
                    stateChartCount = pieChartData != null && pieChartData.Count > 0
                        ? $"({pieChartData.Sum(x => x.Count):N0} errors with state info) - Click to navigate" : "(No state transitions found)";
                    if (timelineBuckets != null)
                    {
                        var bucketDisp = timelineBucketSize < 60 ? $"{timelineBucketSize:F1}s" : $"{timelineBucketSize / 60:F1}min";
                        timelineInfo = $"({allErrors.Count} errors, resolution: {bucketDisp})";
                    }

                    if (barChartData != null && barChartData.Count > 0)
                        loggerData = barChartData.Select(x => (x.Name, x.Count)).ToList();
                    if (pieChartData != null && pieChartData.Count > 0)
                        stateData = pieChartData.Select(x => (x.State, x.Count)).ToList();
                }
                else
                {
                    analyticsSummary = "No error logs available for advanced analytics.";
                }
            });

            // Push results into properties (fires PropertyChanged on UI thread)
            if (!string.IsNullOrEmpty(summaryText)) SummaryText = summaryText;

            // PLC
            PlcHasLogs = plcHasLogs;
            if (plcHasLogs)
            {
                PlcSummaryText = plcSummary;
                PlcErrorStats = plcErrorStats;
                PlcErrorCountText = plcErrorCount;
                PlcThreadStats = plcThreadStats;
                PlcThreadCountText = plcThreadCount;
                PlcGaps = plcGaps;
                PlcHasGaps = plcHasGaps;
                PlcGapSummaryText = plcGapSummary;
            }
            else
            {
                PlcSummaryText = "No PLC logs available.";
            }

            // APP
            AppHasLogs = appHasLogs;
            if (appHasLogs)
            {
                AppSummaryText = appSummary;
                AppThreadErrorStats = appThreadErrorStats;
                AppThreadErrorCountText = appErrorCount;
                AppThreadStats = appThreadStats;
                AppThreadCountText = appThreadCount;
                if (!_hasBinaryAppLogs && appMethodErrorStats != null)
                {
                    AppMethodErrorStats = appMethodErrorStats;
                    AppMethodErrorCountText = appMethodErrorCount;
                    AppMethodStats = appMethodStats;
                    AppMethodCountText = appMethodCount;
                }
                AppGaps = appGaps;
                AppHasGaps = appHasGaps;
                AppGapSummaryText = appGapSummary;
            }
            else
            {
                AppSummaryText = "No APP logs available.";
            }

            // Advanced Analytics
            AnalyticsSummaryText = analyticsSummary;
            LoggerChartCountText = loggerChartCount;
            StateChartCountText = stateChartCount;
            TimelineChartInfoText = timelineInfo;
            BarChartData = barChartData;
            PieChartData = pieChartData;
            TimelineBuckets = timelineBuckets;
            TimelineBucketLogs = timelineBucketLogs;
            TimelineFirstTime = timelineFirstTime;
            TimelineBucketSize = timelineBucketSize;
            TimelineBucketCount = timelineBucketCount;
            TimelineStateEntries = timelineStateEntries;
            TimelineZoomStart = 0;
            TimelineZoomEnd = timelineBucketCount;
            LoggerData = loggerData;
            StateData = stateData;

            IsLoading = false;
            AppLogger.Info($"[Stats] Statistics calculated for {totalLogs:N0} entries — {statsSw.ElapsedMilliseconds}ms");
        }

        // ==========================================
        //  HELPERS — static / instance
        // ==========================================

        private static readonly HashSet<string> _errorLevels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Error", "Fatal" };

        private static List<LogEntry> GetErrorLogs(List<LogEntry> source)
        {
            var result = new List<LogEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Level != null && _errorLevels.Contains(source[i].Level))
                    result.Add(source[i]);
            }
            return result;
        }

        // O(n) TopN selection
        private static List<KeyValuePair<string, int>> TopN(Dictionary<string, int> dict, int n)
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

        private static List<ErrorStat> CalculateErrorHistogram(List<LogEntry> errors, int take, Func<LogEntry, string>? keySelector = null)
        {
            if (errors.Count == 0) return new List<ErrorStat>();

            keySelector ??= l => TruncateMessage(l.Message, 100);

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
            double barScale = keySelector == null ? 300.0 : 200.0;

            var result = new List<ErrorStat>(topItems.Count);
            foreach (var kvp in topItems)
            {
                result.Add(new ErrorStat
                {
                    Name = kvp.Key,
                    Message = kvp.Key,
                    Count = kvp.Value,
                    DisplayText = kvp.Value.ToString("N0"),
                    BarWidth = maxCount > 0 ? (double)kvp.Value / maxCount * barScale : 0
                });
            }
            return result;
        }

        private static List<LoadStat> CalculateLoadDistribution(List<LogEntry> logs, Func<LogEntry, string> keySelector, int take, Func<LogEntry, string>? fullNameSelector = null)
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

        private static List<GapInfo> FindGaps(List<LogEntry> logs)
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

        internal static string TruncateMessage(string message, int maxLength)
        {
            if (string.IsNullOrEmpty(message)) return "(empty)";
            if (message.Length <= maxLength) return message;
            return message.Substring(0, maxLength) + "...";
        }

        private readonly Dictionary<string, string> _shortLoggerCache = new Dictionary<string, string>();

        private string GetShortLoggerName(string logger)
        {
            if (string.IsNullOrEmpty(logger)) return "Unknown";
            if (_shortLoggerCache.TryGetValue(logger, out var cached)) return cached;
            var parts = logger.Split('.');
            string result = parts.Length <= 2 ? logger : string.Join(".", parts, parts.Length - 2, 2);
            _shortLoggerCache[logger] = result;
            return result;
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1) return $"{ts.TotalMinutes:F1} min";
            return $"{ts.TotalSeconds:F1} sec";
        }

        // ==========================================
        //  CHART DATA BUILDERS
        // ==========================================

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
                StateEntry foundState = null;
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

        // Return type for timeline data
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

        // ==========================================
        //  STATE DETECTION
        // ==========================================

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
                    var match = AppConstants.S4StateRegex.Match(l.Message);
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
