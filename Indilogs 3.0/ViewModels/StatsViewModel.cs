using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the StatsWindow — holds all statistics computation, data aggregation,
    /// chart data building, state detection, and export logic.
    /// The View (StatsWindow.xaml.cs) only handles UI event wiring and SkiaSharp rendering.
    /// </summary>
    public partial class StatsViewModel : ViewModelBase
    {
        // ==========================================
        //  INPUT DATA
        // ==========================================
        private readonly List<LogEntry> _plcLogs;
        private readonly List<LogEntry> _appLogs;
        private readonly Action<string, string>? _applyFilterCallback;
        private readonly Action<LogEntry>? _navigateToLogCallback;
        private readonly bool _hasBinaryAppLogs;
        private readonly bool _isDarkMode;

        // ==========================================
        //  COMPUTED STATS — bound / read by the View
        // ==========================================

        // PLC
        private List<ErrorStat>? _plcErrorStats;
        public List<ErrorStat>? PlcErrorStats { get => _plcErrorStats; private set => SetField(ref _plcErrorStats, value); }

        private List<LoadStat>? _plcThreadStats;
        public List<LoadStat>? PlcThreadStats { get => _plcThreadStats; private set => SetField(ref _plcThreadStats, value); }

        private List<GapInfo>? _plcGaps;
        public List<GapInfo>? PlcGaps { get => _plcGaps; private set => SetField(ref _plcGaps, value); }

        // APP
        private List<ErrorStat>? _appThreadErrorStats;
        public List<ErrorStat>? AppThreadErrorStats { get => _appThreadErrorStats; private set => SetField(ref _appThreadErrorStats, value); }

        private List<LoadStat>? _appThreadStats;
        public List<LoadStat>? AppThreadStats { get => _appThreadStats; private set => SetField(ref _appThreadStats, value); }

        private List<ErrorStat>? _appMethodErrorStats;
        public List<ErrorStat>? AppMethodErrorStats { get => _appMethodErrorStats; private set => SetField(ref _appMethodErrorStats, value); }

        private List<LoadStat>? _appMethodStats;
        public List<LoadStat>? AppMethodStats { get => _appMethodStats; private set => SetField(ref _appMethodStats, value); }

        private List<GapInfo>? _appGaps;
        public List<GapInfo>? AppGaps { get => _appGaps; private set => SetField(ref _appGaps, value); }

        // Summary strings
        private string _summaryText = "Analyzing logs...";
        public string SummaryText { get => _summaryText; private set => SetField(ref _summaryText, value); }

        private string _plcSummaryText = "";
        public string PlcSummaryText { get => _plcSummaryText; private set => SetField(ref _plcSummaryText, value); }

        private string _plcErrorCountText = "";
        public string PlcErrorCountText { get => _plcErrorCountText; private set => SetField(ref _plcErrorCountText, value); }

        private string _plcThreadCountText = "";
        public string PlcThreadCountText { get => _plcThreadCountText; private set => SetField(ref _plcThreadCountText, value); }

        private string _plcGapSummaryText = "";
        public string PlcGapSummaryText { get => _plcGapSummaryText; private set => SetField(ref _plcGapSummaryText, value); }

        private string _appSummaryText = "";
        public string AppSummaryText { get => _appSummaryText; private set => SetField(ref _appSummaryText, value); }

        private string _appThreadErrorCountText = "";
        public string AppThreadErrorCountText { get => _appThreadErrorCountText; private set => SetField(ref _appThreadErrorCountText, value); }

        private string _appThreadCountText = "";
        public string AppThreadCountText { get => _appThreadCountText; private set => SetField(ref _appThreadCountText, value); }

        private string _appMethodErrorCountText = "";
        public string AppMethodErrorCountText { get => _appMethodErrorCountText; private set => SetField(ref _appMethodErrorCountText, value); }

        private string _appMethodCountText = "";
        public string AppMethodCountText { get => _appMethodCountText; private set => SetField(ref _appMethodCountText, value); }

        private string _appGapSummaryText = "";
        public string AppGapSummaryText { get => _appGapSummaryText; private set => SetField(ref _appGapSummaryText, value); }

        private string _analyticsSummaryText = "";
        public string AnalyticsSummaryText { get => _analyticsSummaryText; private set => SetField(ref _analyticsSummaryText, value); }

        private string _loggerChartCountText = "";
        public string LoggerChartCountText { get => _loggerChartCountText; private set => SetField(ref _loggerChartCountText, value); }

        private string _stateChartCountText = "";
        public string StateChartCountText { get => _stateChartCountText; private set => SetField(ref _stateChartCountText, value); }

        private string _timelineChartInfoText = "";
        public string TimelineChartInfoText { get => _timelineChartInfoText; private set => SetField(ref _timelineChartInfoText, value); }

        // Visibility flags
        private bool _plcHasLogs;
        public bool PlcHasLogs { get => _plcHasLogs; private set => SetField(ref _plcHasLogs, value); }

        private bool _plcHasGaps;
        public bool PlcHasGaps { get => _plcHasGaps; private set => SetField(ref _plcHasGaps, value); }

        private bool _appHasLogs;
        public bool AppHasLogs { get => _appHasLogs; private set => SetField(ref _appHasLogs, value); }

        private bool _appHasGaps;
        public bool AppHasGaps { get => _appHasGaps; private set => SetField(ref _appHasGaps, value); }

        private bool _isLoading = true;
        public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }

        // ==========================================
        //  CHART DATA — consumed by SkiaSharp rendering in the View
        // ==========================================
        public List<(string Name, int Count, List<LogEntry> Logs)>? BarChartData { get; private set; }
        public List<(string State, int Count, List<LogEntry> Logs)>? PieChartData { get; private set; }
        public int[]? TimelineBuckets { get; private set; }
        public List<LogEntry>[]? TimelineBucketLogs { get; private set; }
        public DateTime TimelineFirstTime { get; private set; }
        public double TimelineBucketSize { get; private set; }
        public int TimelineBucketCount { get; private set; }
        public List<StateEntry>? TimelineStateEntries { get; private set; }

        // Timeline zoom state — mutable from the View
        public int TimelineZoomStart { get; set; }
        public int TimelineZoomEnd { get; set; }

        // Advanced Analytics drill-down data
        public List<(string Logger, int Count)>? LoggerData { get; private set; }
        public List<(string State, int Count)>? StateData { get; private set; }

        // ==========================================
        //  PUBLIC CONFIG
        // ==========================================
        public bool HasBinaryAppLogs => _hasBinaryAppLogs;
        public bool IsDarkMode => _isDarkMode;

        // ==========================================
        //  CONSTRUCTOR
        // ==========================================
        public StatsViewModel(
            IEnumerable<LogEntry>? plcLogs,
            IEnumerable<LogEntry>? appLogs,
            Action<string, string>? applyFilterCallback,
            Action<LogEntry>? navigateToLogCallback,
            bool isDarkMode,
            bool hasBinaryAppLogs)
        {
            _plcLogs = plcLogs?.ToList() ?? new List<LogEntry>();
            _appLogs = appLogs?.ToList() ?? new List<LogEntry>();
            _applyFilterCallback = applyFilterCallback;
            _navigateToLogCallback = navigateToLogCallback;
            _isDarkMode = isDarkMode;
            _hasBinaryAppLogs = hasBinaryAppLogs;
        }

        // ==========================================
        //  DRILL-DOWN / NAVIGATION
        // ==========================================

        /// <summary>Navigate to the first log in the given bar chart item.</summary>
        public void NavigateBarChartItem(int index)
        {
            if (BarChartData == null || index < 0 || index >= BarChartData.Count) return;
            var item = BarChartData[index];
            if (_navigateToLogCallback != null && item.Logs.Any())
                _navigateToLogCallback(item.Logs.First());
            else
                ApplyLoggerFilter(item.Name);
        }

        /// <summary>Navigate to the first log in the given pie chart item.</summary>
        public void NavigatePieChartItem(int index)
        {
            if (PieChartData == null || index < 0 || index >= PieChartData.Count) return;
            var item = PieChartData[index];
            if (_navigateToLogCallback != null && item.Logs.Any())
                _navigateToLogCallback(item.Logs.First());
            else
                ApplyStateFilter(item.State);
        }

        /// <summary>Navigate to the first log in the given timeline bucket.</summary>
        public void NavigateTimelineBucket(int bucketIndex)
        {
            if (TimelineBucketLogs == null || bucketIndex < 0 || bucketIndex >= TimelineBucketCount) return;
            var logs = TimelineBucketLogs[bucketIndex];
            if (_navigateToLogCallback != null && logs.Any())
                _navigateToLogCallback(logs.First());
        }

        /// <summary>
        /// Returns true if applying the filter requires closing the window (i.e. user confirmed).
        /// The View should call this and close itself if true is returned.
        /// showConfirmation is a delegate the View provides to show a MessageBox.
        /// </summary>
        public bool TryApplyLoggerFilter(string logger, Func<string, string, bool> showConfirmation)
        {
            if (_applyFilterCallback == null) return false;
            if (showConfirmation($"Filter logs to show only Logger:\n\n{logger}\n\nThis will close the statistics window and apply the filter.", "Apply Logger Filter"))
            {
                _applyFilterCallback("Logger", logger);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if applying the filter requires closing the window (i.e. user confirmed).
        /// </summary>
        public bool TryApplyStateFilter(string state, Func<string, string, bool> showConfirmation)
        {
            if (_applyFilterCallback == null) return false;
            if (showConfirmation($"Filter logs to show only STATE:\n\n{state}\n\nThis will close the statistics window and apply the filter.", "Apply State Filter"))
            {
                _applyFilterCallback("State", state);
                return true;
            }
            return false;
        }

        // ==========================================
        //  EXPORT
        // ==========================================

        /// <summary>Builds the full export report string.</summary>
        public string BuildExportReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== LOG STATISTICS REPORT ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"PLC Logs: {_plcLogs.Count:N0}");
            sb.AppendLine($"APP Logs: {_appLogs.Count:N0}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            // PLC
            sb.AppendLine(">>> PLC LOGS STATISTICS <<<");
            AppendSection(sb, "TOP 10 COMMON ERRORS", _plcErrorStats, s => $"[{s.Count}] {s.Message}");
            AppendSection(sb, "THREAD LOAD", _plcThreadStats, s => $"{s.Name}: {s.DisplayText}");
            AppendGapSection(sb, _plcGaps);
            sb.AppendLine();

            // APP
            sb.AppendLine(">>> APP LOGS STATISTICS <<<");
            AppendSection(sb, "ERRORS BY LOGGER", _appThreadErrorStats, s => $"{s.Name} ({s.Count} errors)");
            if (!_hasBinaryAppLogs)
                AppendSection(sb, "ERRORS BY METHOD", _appMethodErrorStats, s => $"{s.Name} ({s.Count} errors)");
            AppendSection(sb, "LOGGER LOAD", _appThreadStats, s => $"{s.Name}: {s.DisplayText}");
            if (!_hasBinaryAppLogs)
                AppendSection(sb, "METHOD LOAD", _appMethodStats, s => $"{s.Name}: {s.DisplayText}");
            AppendGapSection(sb, _appGaps);

            return sb.ToString();
        }

        // ==========================================
        //  TIMELINE ZOOM
        // ==========================================

        public void ZoomTimeline(int mouseDelta, bool isShift, int hoveredBucket)
        {
            if (TimelineBuckets == null || TimelineBucketCount == 0) return;

            int visibleCount = TimelineZoomEnd - TimelineZoomStart;

            if (isShift)
            {
                // Pan
                int panAmount = Math.Max(1, visibleCount / 10);
                if (mouseDelta > 0)
                {
                    TimelineZoomStart = Math.Max(0, TimelineZoomStart - panAmount);
                    TimelineZoomEnd = TimelineZoomStart + visibleCount;
                }
                else
                {
                    TimelineZoomEnd = Math.Min(TimelineBucketCount, TimelineZoomEnd + panAmount);
                    TimelineZoomStart = TimelineZoomEnd - visibleCount;
                }
            }
            else
            {
                // Zoom
                int center = (TimelineZoomStart + TimelineZoomEnd) / 2;
                if (hoveredBucket >= TimelineZoomStart && hoveredBucket < TimelineZoomEnd)
                    center = hoveredBucket;

                int minVisible = Math.Max(20, TimelineBucketCount / 5);
                int newVisible;
                if (mouseDelta > 0)
                    newVisible = Math.Max(minVisible, (int)(visibleCount * 0.90));
                else
                    newVisible = Math.Min(TimelineBucketCount, (int)(visibleCount * 1.12));

                TimelineZoomStart = Math.Max(0, center - newVisible / 2);
                TimelineZoomEnd = TimelineZoomStart + newVisible;
                if (TimelineZoomEnd > TimelineBucketCount)
                {
                    TimelineZoomEnd = TimelineBucketCount;
                    TimelineZoomStart = Math.Max(0, TimelineZoomEnd - newVisible);
                }
            }
        }

        // ==========================================
        //  EXPORT HELPERS
        // ==========================================

        private static void AppendSection<T>(StringBuilder sb, string title, List<T>? items, Func<T, string> formatter)
        {
            sb.AppendLine($"--- {title} ---");
            if (items != null && items.Any())
                foreach (var item in items) sb.AppendLine("  " + formatter(item));
            else
                sb.AppendLine("  (No data)");
            sb.AppendLine();
        }

        // ==========================================
        //  PRIVATE FILTER HELPERS (fallback when no navigateCallback)
        // ==========================================

        private void ApplyLoggerFilter(string logger)
        {
            // Fallback: the View should intercept these via TryApply* methods.
            // This path only triggers when there is no navigate callback.
            _applyFilterCallback?.Invoke("Logger", logger);
        }

        private void ApplyStateFilter(string state)
        {
            _applyFilterCallback?.Invoke("State", state);
        }
    }
}
