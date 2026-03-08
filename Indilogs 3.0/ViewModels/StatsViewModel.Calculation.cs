using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class StatsViewModel
    {
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

            List<ErrorStat>? plcErrorStats = null, appThreadErrorStats = null, appMethodErrorStats = null;
            List<LoadStat>? plcThreadStats = null, appThreadStats = null, appMethodStats = null;
            List<GapInfo>? plcGaps = null, appGaps = null;

            // Chart data built on background
            List<(string Name, int Count, List<LogEntry> Logs)>? barChartData = null;
            List<(string State, int Count, List<LogEntry> Logs)>? pieChartData = null;
            int[]? timelineBuckets = null;
            List<LogEntry>[]? timelineBucketLogs = null;
            DateTime timelineFirstTime = default;
            double timelineBucketSize = 0;
            int timelineBucketCount = 0;
            List<StateEntry>? timelineStateEntries = null;
            List<(string Logger, int Count)>? loggerData = null;
            List<(string State, int Count)>? stateData = null;

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
                        appGapSummary = $"Found {appGaps!.Count} gap(s) >= 2s. Total: {FormatDuration(TimeSpan.FromSeconds(appGaps.Sum(g => g.Duration.TotalSeconds)))}";
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

        private static readonly System.Collections.Frozen.FrozenSet<string> _errorLevels = AppConstants.ErrorLevels;

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
    }
}
