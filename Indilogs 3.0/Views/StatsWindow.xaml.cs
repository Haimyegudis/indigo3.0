using DocumentFormat.OpenXml.Spreadsheet;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace IndiLogs_3._0.Views
{
    public partial class StatsWindow : Window
    {
        private readonly List<LogEntry> _plcLogs;
        private readonly List<LogEntry> _appLogs;
        private readonly Action<string, string> _applyFilterCallback;

        // PLC Stats Data
        private List<ErrorStat> _plcErrorStats;
        private List<LoadStat> _plcThreadStats;
        private List<GapInfo> _plcGaps;

        // APP Stats Data
        private List<ErrorStat> _appThreadErrorStats;
        private List<LoadStat> _appThreadStats;
        private List<ErrorStat> _appMethodErrorStats;
        private List<LoadStat> _appMethodStats;
        private List<GapInfo> _appGaps;

        // Configuration
        private readonly bool _hasBinaryAppLogs;

        // Advanced Analytics - store logger and state data for drill-down
        private List<(string Logger, int Count)> _loggerData;
        private List<(string State, int Count)> _stateData;

        // SkiaSharp chart data
        private List<(string Name, int Count, List<LogEntry> Logs)> _barChartData;
        private List<(string State, int Count, List<LogEntry> Logs)> _pieChartData;
        private int[] _timelineBuckets;
        private List<LogEntry>[] _timelineBucketLogs;
        private DateTime _timelineFirstTime;
        private double _timelineBucketSize;
        private int _timelineBucketCount;
        private List<StateEntry> _timelineStateEntries; // State intervals for background coloring

        // Hover state for charts
        private SKPoint _barChartMouse = new SKPoint(-1, -1);
        private SKPoint _pieChartMouse = new SKPoint(-1, -1);
        private SKPoint _timelineMouse = new SKPoint(-1, -1);
        private int _hoveredBarIndex = -1;
        private int _hoveredPieIndex = -1;
        private int _hoveredTimelineBucket = -1;

        // Hit regions for click detection
        private List<SKRect> _barHitRegions = new List<SKRect>();
        private List<(float startAngle, float sweepAngle)> _pieHitAngles = new List<(float, float)>();
        private float _pieChartCenterX, _pieChartCenterY, _pieChartRadius;

        // Navigate to log callback
        private readonly Action<LogEntry> _navigateToLogCallback;

        // Theme awareness
        private readonly bool _isDarkMode;
        private SKColor _chartBg;
        private SKColor _chartText;
        private SKColor _chartTextDim;
        private SKColor _chartGrid;
        private SKColor _tooltipBg;
        private SKColor _tooltipBorder;

        // Timeline zoom
        private int _timelineZoomStart;
        private int _timelineZoomEnd;

        // Cached SKTypeface instances (native resources — allocate once)
        private static readonly SKTypeface s_segoeUI = SKTypeface.FromFamilyName("Segoe UI");
        private static readonly SKTypeface s_segoeUIBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

        // Cached SKColors for values used in render loops
        private static readonly SKColor s_barNormal = SKColor.Parse("#E74C3C");
        private static readonly SKColor s_barHover = SKColor.Parse("#FF6B5A");
        private static readonly SKColor s_accentDark = SKColor.Parse("#3498DB");
        private static readonly SKColor s_accentLight = SKColor.Parse("#2196F3");

        // Cached theme colors (avoid SKColor.Parse per theme switch)
        private static readonly SKColor s_darkBg = SKColor.Parse("#2D2D30");
        private static readonly SKColor s_darkTextDim = SKColor.Parse("#C8C8C8");
        private static readonly SKColor s_darkGrid = SKColors.White.WithAlpha(25);
        private static readonly SKColor s_darkTooltipBg = SKColor.Parse("#1E3A5F").WithAlpha(245);
        private static readonly SKColor s_darkTooltipBorder = SKColor.Parse("#3498DB").WithAlpha(100);
        private static readonly SKColor s_lightBg = SKColor.Parse("#FAFAFA");
        private static readonly SKColor s_lightText = SKColor.Parse("#333333");
        private static readonly SKColor s_lightTextDim = SKColor.Parse("#666666");
        private static readonly SKColor s_lightGrid = SKColors.Black.WithAlpha(20);
        private static readonly SKColor s_lightTooltipBg = SKColors.White.WithAlpha(240);
        private static readonly SKColor s_lightTooltipBorder = SKColor.Parse("#2196F3").WithAlpha(120);

        // Color palette
        private static readonly SKColor[] ChartColors = new[]
        {
            SKColor.Parse("#E74C3C"), SKColor.Parse("#F1C40F"), SKColor.Parse("#3498DB"),
            SKColor.Parse("#2ECC71"), SKColor.Parse("#9B59B6"), SKColor.Parse("#E67E22"),
            SKColor.Parse("#95A5A6"), SKColor.Parse("#16A085"), SKColor.Parse("#F39C12"),
            SKColor.Parse("#8E44AD")
        };

        // Cached SKPaint instances — created once, reused per frame (properties updated as needed)
        private readonly SKPaint _cachedTextPaint11 = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = s_segoeUI };
        private readonly SKPaint _cachedTextPaint11Bold = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = s_segoeUIBold };
        private readonly SKPaint _cachedTextPaint10 = new SKPaint { TextSize = 10, IsAntialias = true, Typeface = s_segoeUI };
        private readonly SKPaint _cachedTextPaint9 = new SKPaint { TextSize = 9, IsAntialias = true, Typeface = s_segoeUI };
        private readonly SKPaint _cachedFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _cachedStrokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _cachedGridPaint = new SKPaint { StrokeWidth = 1, IsAntialias = false };
        private readonly SKPaint _cachedTooltipBgPaint = new SKPaint { IsAntialias = true };
        private readonly SKPaint _cachedTooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

        public StatsWindow(IEnumerable<LogEntry> plcLogs, IEnumerable<LogEntry> appLogs, Action<string, string> applyFilterCallback = null, Action<LogEntry> navigateToLogCallback = null, bool isDarkMode = true, bool hasBinaryAppLogs = false)
        {
            InitializeComponent();
            _plcLogs = plcLogs?.ToList() ?? new List<LogEntry>();
            _appLogs = appLogs?.ToList() ?? new List<LogEntry>();
            _applyFilterCallback = applyFilterCallback;
            _navigateToLogCallback = navigateToLogCallback;
            _isDarkMode = isDarkMode;
            _hasBinaryAppLogs = hasBinaryAppLogs;
            ApplyChartTheme();

            // In S4-5, hide Method panels
            if (_hasBinaryAppLogs)
            {
                AppMethodErrorPanel.Visibility = Visibility.Collapsed;
                AppMethodErrorColumn.Width = new GridLength(0);
                AppMethodErrorSpacer.Width = new GridLength(0);
                AppMethodLoadPanel.Visibility = Visibility.Collapsed;
                AppMethodLoadColumn.Width = new GridLength(0);
                AppMethodLoadSpacer.Width = new GridLength(0);
            }

            Loaded += async (s, e) => await CalculateStatisticsAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cachedTextPaint11?.Dispose();
            _cachedTextPaint11Bold?.Dispose();
            _cachedTextPaint10?.Dispose();
            _cachedTextPaint9?.Dispose();
            _cachedFillPaint?.Dispose();
            _cachedStrokePaint?.Dispose();
            _cachedGridPaint?.Dispose();
            _cachedTooltipBgPaint?.Dispose();
            _cachedTooltipBorderPaint?.Dispose();
        }

        private void ApplyChartTheme()
        {
            if (_isDarkMode)
            {
                _chartBg = s_darkBg;
                _chartText = SKColors.White;
                _chartTextDim = s_darkTextDim;
                _chartGrid = s_darkGrid;
                _tooltipBg = s_darkTooltipBg;
                _tooltipBorder = s_darkTooltipBorder;
            }
            else
            {
                _chartBg = s_lightBg;
                _chartText = s_lightText;
                _chartTextDim = s_lightTextDim;
                _chartGrid = s_lightGrid;
                _tooltipBg = s_lightTooltipBg;
                _tooltipBorder = s_lightTooltipBorder;
            }
        }

        private async Task CalculateStatisticsAsync()
        {
            int totalLogs = _plcLogs.Count + _appLogs.Count;
            if (totalLogs == 0)
            {
                SummaryText.Text = "No logs available for analysis.";
                LoadingOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // --- All heavy calculations on background thread ---
            // Local variables to hold results
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

                // --- Cache error logs once ---
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

                // Advanced Analytics — use cached error logs (no re-filter!)
                var allErrors = new List<LogEntry>(plcErrors.Count + appErrors.Count);
                allErrors.AddRange(plcErrors);
                allErrors.AddRange(appErrors);

                if (allErrors.Count > 0)
                {
                    analyticsSummary = $"Advanced Analytics - Total Errors: {allErrors.Count:N0}";
                    CreateLoggerBarChartData(plcErrors, appErrors);
                    _timelineStateEntries = CalculateStateEntries(_plcLogs);
                    CreateStatePieChartData(_plcLogs, _timelineStateEntries);
                    CreateErrorTimelineChartData(allErrors);

                    loggerChartCount = _barChartData != null && _barChartData.Count > 0 ?
                        $"({_barChartData.Sum(x => x.Count):N0} errors) - Click bar to navigate" : "(No data)";
                    stateChartCount = _pieChartData != null && _pieChartData.Count > 0 ?
                        $"({_pieChartData.Sum(x => x.Count):N0} errors with state info) - Click to navigate" : "(No state transitions found)";
                    if (_timelineBuckets != null)
                    {
                        var bucketDisp = _timelineBucketSize < 60 ? $"{_timelineBucketSize:F1}s" : $"{_timelineBucketSize / 60:F1}min";
                        timelineInfo = $"({allErrors.Count} errors, resolution: {bucketDisp})";
                    }
                }
                else
                {
                    analyticsSummary = "No error logs available for advanced analytics.";
                }
            });

            // --- Update UI on main thread ---
            if (!string.IsNullOrEmpty(summaryText)) SummaryText.Text = summaryText;

            // PLC tab
            if (plcHasLogs)
            {
                PlcSummaryText.Text = plcSummary;
                _plcErrorStats = plcErrorStats;
                PlcErrorHistogram.ItemsSource = _plcErrorStats;
                PlcErrorCountText.Text = plcErrorCount;
                _plcThreadStats = plcThreadStats;
                PlcThreadHistogram.ItemsSource = _plcThreadStats;
                PlcThreadCountText.Text = plcThreadCount;
                _plcGaps = plcGaps;
                if (plcHasGaps)
                {
                    PlcGapSummaryText.Text = plcGapSummary;
                    PlcGapDataGrid.ItemsSource = _plcGaps;
                    PlcGapDataGrid.Visibility = Visibility.Visible;
                    PlcNoGapsMessage.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PlcGapSummaryText.Text = plcGapSummary;
                    PlcGapDataGrid.Visibility = Visibility.Collapsed;
                    PlcNoGapsMessage.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PlcSummaryText.Text = "No PLC logs available.";
            }

            // APP tab
            if (appHasLogs)
            {
                AppSummaryText.Text = appSummary;
                _appThreadErrorStats = appThreadErrorStats;
                AppThreadErrorHistogram.ItemsSource = _appThreadErrorStats;
                AppThreadErrorCountText.Text = appErrorCount;
                _appThreadStats = appThreadStats;
                AppThreadHistogram.ItemsSource = _appThreadStats;
                AppThreadCountText.Text = appThreadCount;
                if (!_hasBinaryAppLogs && appMethodErrorStats != null)
                {
                    _appMethodErrorStats = appMethodErrorStats;
                    AppMethodErrorHistogram.ItemsSource = _appMethodErrorStats;
                    AppMethodErrorCountText.Text = appMethodErrorCount;
                    _appMethodStats = appMethodStats;
                    AppMethodHistogram.ItemsSource = _appMethodStats;
                    AppMethodCountText.Text = appMethodCount;
                }
                _appGaps = appGaps;
                if (appHasGaps)
                {
                    AppGapSummaryText.Text = appGapSummary;
                    AppGapDataGrid.ItemsSource = _appGaps;
                    AppGapDataGrid.Visibility = Visibility.Visible;
                    AppNoGapsMessage.Visibility = Visibility.Collapsed;
                }
                else
                {
                    AppGapSummaryText.Text = appGapSummary;
                    AppGapDataGrid.Visibility = Visibility.Collapsed;
                    AppNoGapsMessage.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AppSummaryText.Text = "No APP logs available.";
            }

            // Advanced Analytics tab
            AnalyticsSummaryText.Text = analyticsSummary;
            LoggerChartCountText.Text = loggerChartCount;
            StateChartCountText.Text = stateChartCount;
            if (_barChartData != null && _barChartData.Any())
                _loggerData = _barChartData.Select(x => (x.Name, x.Count)).ToList();
            BarChartCanvas.InvalidateVisual();
            PieChartCanvas.InvalidateVisual();
            if (_timelineBuckets != null)
                TimelineChartCanvas.InvalidateVisual();
            if (!string.IsNullOrEmpty(timelineInfo)) TimelineChartInfo.Text = timelineInfo;

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // Old CalculatePlcStatistics/CalculateAppStatistics/CalculateAdvancedAnalytics removed —
        // all logic now in CalculateStatisticsAsync above

        // ==========================================
        //  HELPERS
        // ==========================================

        // O(n) TopN selection for Dictionary — avoids O(n log n) full sort
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

        private static readonly HashSet<string> _errorLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Error", "Fatal" };

        private List<LogEntry> GetErrorLogs(List<LogEntry> source)
        {
            var result = new List<LogEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Level != null && _errorLevels.Contains(source[i].Level))
                    result.Add(source[i]);
            }
            return result;
        }

        // Generic Error Histogram Calculator (By Message or Custom Key)
        private List<ErrorStat> CalculateErrorHistogram(List<LogEntry> errors, int take, Func<LogEntry, string> keySelector = null)
        {
            if (errors.Count == 0) return new List<ErrorStat>();

            keySelector = keySelector ?? (l => TruncateMessage(l.Message, 100));

            // Use Dictionary for O(1) grouping instead of LINQ GroupBy
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < errors.Count; i++)
            {
                string key = keySelector(errors[i]);
                if (counts.TryGetValue(key, out int c))
                    counts[key] = c + 1;
                else
                    counts[key] = 1;
            }

            // Get top N using O(n) selection instead of O(n log n) full sort
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

        // Generic Load Distribution Calculator
        private List<LoadStat> CalculateLoadDistribution(List<LogEntry> logs, Func<LogEntry, string> keySelector, int take, Func<LogEntry, string> fullNameSelector = null)
        {
            // Use Dictionary for O(1) grouping
            var counts = new Dictionary<string, int>();
            var firstLog = new Dictionary<string, LogEntry>(); // for fullName lookup
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

        private List<GapInfo> FindGaps(List<LogEntry> logs)
        {
            var gaps = new List<GapInfo>();
            if (logs == null || logs.Count < 2) return gaps;

            // Logs are already sorted by Date from the loading phase - no need to sort again
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

        private string TruncateMessage(string message, int maxLength)
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

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1) return $"{ts.TotalMinutes:F1} min";
            return $"{ts.TotalSeconds:F1} sec";
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== LOG STATISTICS REPORT ===");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"PLC Logs: {_plcLogs.Count:N0}");
                sb.AppendLine($"APP Logs: {_appLogs.Count:N0}");
                sb.AppendLine(new string('=', 50));
                sb.AppendLine();

                // --- PLC SECTION ---
                sb.AppendLine(">>> PLC LOGS STATISTICS <<<");
                AppendSection(sb, "TOP 10 COMMON ERRORS", _plcErrorStats, s => $"[{s.Count}] {s.Message}");
                AppendSection(sb, "THREAD LOAD", _plcThreadStats, s => $"{s.Name}: {s.DisplayText}");
                AppendGapSection(sb, _plcGaps);
                sb.AppendLine();

                // --- APP SECTION ---
                sb.AppendLine(">>> APP LOGS STATISTICS <<<");
                AppendSection(sb, "ERRORS BY LOGGER", _appThreadErrorStats, s => $"{s.Name} ({s.Count} errors)");
                if (!_hasBinaryAppLogs)
                    AppendSection(sb, "ERRORS BY METHOD", _appMethodErrorStats, s => $"{s.Name} ({s.Count} errors)");
                AppendSection(sb, "LOGGER LOAD", _appThreadStats, s => $"{s.Name}: {s.DisplayText}");
                if (!_hasBinaryAppLogs)
                    AppendSection(sb, "METHOD LOAD", _appMethodStats, s => $"{s.Name}: {s.DisplayText}");
                AppendGapSection(sb, _appGaps);

                // Save
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"LogStats_Full_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, sb.ToString());
                    MessageBox.Show($"Report exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendSection<T>(StringBuilder sb, string title, List<T> items, Func<T, string> formatter)
        {
            sb.AppendLine($"--- {title} ---");
            if (items != null && items.Any())
                foreach (var item in items) sb.AppendLine("  " + formatter(item));
            else
                sb.AppendLine("  (No data)");
            sb.AppendLine();
        }

        private void AppendGapSection(StringBuilder sb, List<GapInfo> gaps)
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

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ==========================================
        //  ADVANCED ANALYTICS
        // ==========================================
        // Old CalculateAdvancedAnalytics removed — logic now in CalculateStatisticsAsync

        // Data-only chart builders (no UI calls — safe for background thread)
        private void CreateLoggerBarChartData(List<LogEntry> plcErrors, List<LogEntry> appErrors)
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

            // Sort and take top 10 in-place
            combinedCounts.Sort((a, b) => b.Count.CompareTo(a.Count));
            _barChartData = combinedCounts.Count > 10 ? combinedCounts.GetRange(0, 10) : combinedCounts;
        }

        private void BarChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            if (_barChartData == null || !_barChartData.Any()) return;

            float w = info.Width, h = info.Height;
            float leftMargin = 160, rightMargin = 50, topMargin = 10, bottomMargin = 10;
            float chartW = w - leftMargin - rightMargin;
            float chartH = h - topMargin - bottomMargin;
            int count = _barChartData.Count;
            float barHeight = Math.Min(28, (chartH - (count - 1) * 4) / count);
            float gap = 4;
            int maxCount = _barChartData.Max(x => x.Count);

            _barHitRegions.Clear();
            _hoveredBarIndex = -1;

            _cachedTextPaint11.Color = _chartTextDim;
            _cachedTextPaint11.TextAlign = SKTextAlign.Left;
            _cachedTextPaint11Bold.Color = _chartText;
            _cachedTextPaint11Bold.TextAlign = SKTextAlign.Left;

            for (int i = 0; i < count; i++)
            {
                var item = _barChartData[i];
                float y = topMargin + i * (barHeight + gap);
                float barW = maxCount > 0 ? (float)item.Count / maxCount * chartW : 0;
                var barRect = new SKRect(leftMargin, y, leftMargin + barW, y + barHeight);

                _barHitRegions.Add(new SKRect(0, y, w, y + barHeight));

                // Check hover
                bool isHovered = _barChartMouse.Y >= y && _barChartMouse.Y <= y + barHeight && _barChartMouse.X >= 0;
                if (isHovered) _hoveredBarIndex = i;

                // Bar gradient
                var barColor = isHovered ? s_barHover : s_barNormal;
                var shader = SKShader.CreateLinearGradient(
                    new SKPoint(barRect.Left, barRect.Top), new SKPoint(barRect.Right, barRect.Top),
                    new[] { barColor, barColor.WithAlpha(180) }, null, SKShaderTileMode.Clamp);
                _cachedFillPaint.Shader = shader;
                canvas.DrawRoundRect(barRect, 4, 4, _cachedFillPaint);
                shader.Dispose();
                _cachedFillPaint.Shader = null;

                // Hover highlight border
                if (isHovered)
                {
                    _cachedStrokePaint.Color = SKColors.White.WithAlpha(120);
                    _cachedStrokePaint.StrokeWidth = 1.5f;
                    canvas.DrawRoundRect(barRect, 4, 4, _cachedStrokePaint);
                }

                // Label
                string label = item.Name.Length > 22 ? item.Name.Substring(0, 19) + "..." : item.Name;
                canvas.DrawText(label, 5, y + barHeight / 2 + 4, _cachedTextPaint11);

                // Value
                string valueText = item.Count.ToString("N0");
                canvas.DrawText(valueText, leftMargin + barW + 6, y + barHeight / 2 + 4, _cachedTextPaint11Bold);
            }

            // Draw hover tooltip
            if (_hoveredBarIndex >= 0 && _hoveredBarIndex < _barChartData.Count)
            {
                var item = _barChartData[_hoveredBarIndex];
                string tip = $"{item.Name}\n{item.Count:N0} errors — Click to navigate";
                DrawTooltip(canvas, tip, _barChartMouse.X + 15, _barChartMouse.Y - 10, w, h);
            }
        }

        private void BarChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(BarChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(BarChartCanvas).DpiScaleX;
            _barChartMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            BarChartCanvas.InvalidateVisual();
        }

        private void BarChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _barChartMouse = new SKPoint(-1, -1);
            _hoveredBarIndex = -1;
            BarChartCanvas.InvalidateVisual();
        }

        private void BarChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_hoveredBarIndex >= 0 && _hoveredBarIndex < _barChartData.Count)
            {
                var item = _barChartData[_hoveredBarIndex];
                if (_navigateToLogCallback != null && item.Logs.Any())
                {
                    _navigateToLogCallback(item.Logs.First());
                }
                else
                {
                    ApplyLoggerFilter(item.Name);
                }
            }
        }

        private void CreateStatePieChartData(List<LogEntry> plcLogs, List<StateEntry> stateEntries)
        {
            var plcErrors = GetErrorLogs(plcLogs);
            if (plcErrors.Count == 0 || stateEntries == null || stateEntries.Count == 0) return;

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
            _pieChartData = pieList.Count > 10 ? pieList.GetRange(0, 10) : pieList;

            if (_pieChartData.Count > 0)
                _stateData = _pieChartData.Select(x => (x.State, x.Count)).ToList();
        }

        private void PieChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            if (_pieChartData == null || !_pieChartData.Any()) return;

            float w = info.Width, h = info.Height;
            float legendWidth = w * 0.38f;
            float chartAreaW = w - legendWidth;
            float radius = Math.Min(chartAreaW, h) * 0.38f;
            float cx = chartAreaW / 2f;
            float cy = h / 2f;
            _pieChartCenterX = cx; _pieChartCenterY = cy; _pieChartRadius = radius;

            int total = _pieChartData.Sum(x => x.Count);
            _pieHitAngles.Clear();
            _hoveredPieIndex = -1;

            float startAngle = -90;
            for (int i = 0; i < _pieChartData.Count; i++)
            {
                var item = _pieChartData[i];
                float sweep = (float)item.Count / total * 360f;

                // Check hover
                bool isHovered = false;
                if (_pieChartMouse.X >= 0)
                {
                    float dx = _pieChartMouse.X - cx, dy = _pieChartMouse.Y - cy;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius)
                    {
                        float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
                        if (angle < -90) angle += 360;
                        float normalAngle = angle + 90;
                        if (normalAngle < 0) normalAngle += 360;
                        if (normalAngle >= 360) normalAngle -= 360;
                        float checkStart = startAngle + 90;
                        if (checkStart < 0) checkStart += 360;
                        float checkEnd = checkStart + sweep;
                        if (normalAngle >= checkStart && normalAngle < checkEnd)
                        {
                            isHovered = true;
                            _hoveredPieIndex = i;
                        }
                    }
                }

                _pieHitAngles.Add((startAngle, sweep));

                float explode = isHovered ? 6 : 0;
                float midAngle = startAngle + sweep / 2f;
                float exX = (float)(explode * Math.Cos(midAngle * Math.PI / 180));
                float exY = (float)(explode * Math.Sin(midAngle * Math.PI / 180));

                var color = ChartColors[i % ChartColors.Length];
                _cachedFillPaint.Color = isHovered ? color.WithAlpha(255) : color.WithAlpha(220);
                _cachedStrokePaint.Color = _chartBg;
                _cachedStrokePaint.StrokeWidth = 2;
                {
                    var path = new SKPath();
                    path.MoveTo(cx + exX, cy + exY);
                    path.ArcTo(new SKRect(cx - radius + exX, cy - radius + exY, cx + radius + exX, cy + radius + exY), startAngle, sweep, false);
                    path.Close();
                    canvas.DrawPath(path, _cachedFillPaint);
                    canvas.DrawPath(path, _cachedStrokePaint);
                    path.Dispose();
                }

                // Percentage label inside slice
                if (sweep > 18)
                {
                    float labelR = radius * 0.65f;
                    float lx = cx + exX + (float)(labelR * Math.Cos(midAngle * Math.PI / 180));
                    float ly = cy + exY + (float)(labelR * Math.Sin(midAngle * Math.PI / 180));
                    _cachedTextPaint11Bold.Color = SKColors.White;
                    _cachedTextPaint11Bold.TextAlign = SKTextAlign.Center;
                    canvas.DrawText($"{(float)item.Count / total * 100:F0}%", lx, ly + 4, _cachedTextPaint11Bold);
                }

                startAngle += sweep;
            }

            // Legend
            float legendX = chartAreaW + 10;
            float legendY = 15;
            _cachedTextPaint10.Color = _chartTextDim.WithAlpha(180);
            _cachedTextPaint10.TextAlign = SKTextAlign.Left;

            for (int i = 0; i < _pieChartData.Count; i++)
            {
                var item = _pieChartData[i];
                var color = ChartColors[i % ChartColors.Length];
                bool isHov = i == _hoveredPieIndex;

                _cachedFillPaint.Color = isHov ? color : color.WithAlpha(200);
                canvas.DrawCircle(legendX + 6, legendY + 6, 6, _cachedFillPaint);

                string name = item.State.Length > 16 ? item.State.Substring(0, 13) + "..." : item.State;
                _cachedTextPaint11.Color = isHov ? _chartText : _chartTextDim;
                _cachedTextPaint11.TextAlign = SKTextAlign.Left;
                canvas.DrawText(name, legendX + 18, legendY + 11, _cachedTextPaint11);
                canvas.DrawText($"({item.Count})", legendX + 18, legendY + 24, _cachedTextPaint10);
                legendY += 30;
            }

            // Tooltip
            if (_hoveredPieIndex >= 0 && _hoveredPieIndex < _pieChartData.Count)
            {
                var item = _pieChartData[_hoveredPieIndex];
                float pct = (float)item.Count / total * 100;
                string tip = $"{item.State}\n{item.Count:N0} errors ({pct:F1}%)\nClick to navigate";
                DrawTooltip(canvas, tip, _pieChartMouse.X + 15, _pieChartMouse.Y - 10, w, h);
            }
        }

        private void PieChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(PieChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(PieChartCanvas).DpiScaleX;
            _pieChartMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            PieChartCanvas.InvalidateVisual();
        }

        private void PieChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _pieChartMouse = new SKPoint(-1, -1);
            _hoveredPieIndex = -1;
            PieChartCanvas.InvalidateVisual();
        }

        private void PieChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_hoveredPieIndex >= 0 && _hoveredPieIndex < _pieChartData.Count)
            {
                var item = _pieChartData[_hoveredPieIndex];
                if (_navigateToLogCallback != null && item.Logs.Any())
                {
                    _navigateToLogCallback(item.Logs.First());
                }
                else
                {
                    ApplyStateFilter(item.State);
                }
            }
        }

        private void CreateErrorTimelineChartData(List<LogEntry> errorLogs)
        {
            if (errorLogs.Count == 0) return;

            // In-place sort instead of OrderBy().ToList()
            errorLogs.Sort((a, b) => a.Date.CompareTo(b.Date));
            _timelineFirstTime = errorLogs[0].Date;
            var lastTime = errorLogs[errorLogs.Count - 1].Date;
            var totalDuration = lastTime - _timelineFirstTime;

            if (totalDuration.TotalMinutes < 2) _timelineBucketCount = 60;
            else if (totalDuration.TotalMinutes < 30) _timelineBucketCount = 100;
            else _timelineBucketCount = 120;

            _timelineBucketSize = totalDuration.TotalSeconds / _timelineBucketCount;

            _timelineBuckets = new int[_timelineBucketCount];
            _timelineBucketLogs = new List<LogEntry>[_timelineBucketCount];
            for (int i = 0; i < _timelineBucketCount; i++)
                _timelineBucketLogs[i] = new List<LogEntry>();

            for (int i = 0; i < errorLogs.Count; i++)
            {
                int idx = (int)((errorLogs[i].Date - _timelineFirstTime).TotalSeconds / _timelineBucketSize);
                if (idx >= _timelineBucketCount) idx = _timelineBucketCount - 1;
                if (idx < 0) idx = 0;
                _timelineBuckets[idx]++;
                _timelineBucketLogs[idx].Add(errorLogs[i]);
            }

            _timelineZoomStart = 0;
            _timelineZoomEnd = _timelineBucketCount;
        }

        private void TimelineChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            if (_timelineBuckets == null || _timelineBucketCount == 0) return;

            // Apply zoom range
            int zStart = Math.Max(0, _timelineZoomStart);
            int zEnd = Math.Min(_timelineBucketCount, _timelineZoomEnd);
            int visibleCount = zEnd - zStart;
            if (visibleCount <= 0) visibleCount = _timelineBucketCount;

            float w = info.Width, h = info.Height;
            float leftM = 55, rightM = 15, topM = 15, bottomM = 35;
            float chartW = w - leftM - rightM;
            float chartH = h - topM - bottomM;
            int maxVal = 1;
            for (int i = zStart; i < zEnd; i++)
                if (_timelineBuckets[i] > maxVal) maxVal = _timelineBuckets[i];

            _hoveredTimelineBucket = -1;

            // State background coloring — draw colored vertical bands for each state interval (zoom-aware)
            if (_timelineStateEntries != null && _timelineStateEntries.Count > 0)
            {
                double zoomStartSec = zStart * _timelineBucketSize;
                double zoomEndSec = zEnd * _timelineBucketSize;
                var zoomStartTime = _timelineFirstTime.AddSeconds(zoomStartSec);
                var zoomEndTime = _timelineFirstTime.AddSeconds(zoomEndSec);
                double zoomTotalSec = zoomEndSec - zoomStartSec;
                if (zoomTotalSec <= 0) zoomTotalSec = 1;

                foreach (var state in _timelineStateEntries)
                {
                    if (state.EndTime == null) continue;
                    if (state.EndTime.Value < zoomStartTime || state.StartTime > zoomEndTime) continue;

                    // Calculate x positions relative to zoom window
                    double startSec = Math.Max(0, (state.StartTime - _timelineFirstTime).TotalSeconds - zoomStartSec);
                    double endSec = Math.Min(zoomTotalSec, (state.EndTime.Value - _timelineFirstTime).TotalSeconds - zoomStartSec);
                    float x1 = leftM + (float)(startSec / zoomTotalSec) * chartW;
                    float x2 = leftM + (float)(endSec / zoomTotalSec) * chartW;

                    // Get state color using the same coloring system as the Charts tab
                    int stateId = ChartStateConfig.GetId(state.StateName);
                    SKColor stateColor = ChartStateConfig.GetSolidColor(stateId).WithAlpha(35);

                    _cachedFillPaint.Color = stateColor;
                    canvas.DrawRect(x1, topM, x2 - x1, chartH, _cachedFillPaint);

                    // Draw state label if band is wide enough
                    float bandWidth = x2 - x1;
                    if (bandWidth > 40)
                    {
                        _cachedTextPaint9.Color = SKColors.Black;
                        _cachedTextPaint9.TextAlign = SKTextAlign.Center;
                        float labelX = x1 + bandWidth / 2;
                        canvas.DrawText(state.StateName, labelX, topM + 11, _cachedTextPaint9);
                    }
                }
            }

            // Grid lines
            _cachedGridPaint.Color = _chartGrid;
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    canvas.DrawLine(leftM, y, w - rightM, y, _cachedGridPaint);
                }
            }

            // Area fill + line (zoom-aware)
            float stepW = chartW / visibleCount;
            var linePath = new SKPath();
            var areaPath = new SKPath();
            var accentColor = _isDarkMode ? s_accentDark : s_accentLight;

            areaPath.MoveTo(leftM, topM + chartH);
            for (int vi = 0; vi < visibleCount; vi++)
            {
                int i = zStart + vi;
                float x = leftM + vi * stepW + stepW / 2;
                float valH = (float)_timelineBuckets[i] / maxVal * chartH;
                float y = topM + chartH - valH;

                if (vi == 0) linePath.MoveTo(x, y); else linePath.LineTo(x, y);
                areaPath.LineTo(x, y);

                // Check hover
                if (_timelineMouse.X >= leftM + vi * stepW && _timelineMouse.X < leftM + (vi + 1) * stepW &&
                    _timelineMouse.Y >= topM && _timelineMouse.Y <= topM + chartH)
                {
                    _hoveredTimelineBucket = i;
                }
            }
            areaPath.LineTo(leftM + (visibleCount - 1) * stepW + stepW / 2, topM + chartH);
            areaPath.Close();

            // Gradient fill
            {
                var shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, topM), new SKPoint(0, topM + chartH),
                    new[] { accentColor.WithAlpha(120), accentColor.WithAlpha(15) },
                    null, SKShaderTileMode.Clamp);
                _cachedFillPaint.Shader = shader;
                canvas.DrawPath(areaPath, _cachedFillPaint);
                shader.Dispose();
                _cachedFillPaint.Shader = null;
            }

            // Line
            _cachedStrokePaint.Color = accentColor;
            _cachedStrokePaint.StrokeWidth = 2.5f;
            canvas.DrawPath(linePath, _cachedStrokePaint);

            // Data points (only if few enough)
            if (visibleCount <= 60)
            {
                _cachedFillPaint.Color = accentColor;
                for (int vi = 0; vi < visibleCount; vi++)
                {
                    int i = zStart + vi;
                    if (_timelineBuckets[i] > 0)
                    {
                        float x = leftM + vi * stepW + stepW / 2;
                        float valH = (float)_timelineBuckets[i] / maxVal * chartH;
                        float y = topM + chartH - valH;
                        canvas.DrawCircle(x, y, i == _hoveredTimelineBucket ? 5 : 3, _cachedFillPaint);
                    }
                }
            }

            // Hover vertical line + highlight
            if (_hoveredTimelineBucket >= zStart && _hoveredTimelineBucket < zEnd)
            {
                int hvi = _hoveredTimelineBucket - zStart;
                float hx = leftM + hvi * stepW + stepW / 2;
                var hoverLineColor = _isDarkMode ? SKColors.White.WithAlpha(80) : SKColors.Black.WithAlpha(60);
                _cachedStrokePaint.Color = hoverLineColor;
                _cachedStrokePaint.StrokeWidth = 1;
                var dashEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
                _cachedStrokePaint.PathEffect = dashEffect;
                canvas.DrawLine(hx, topM, hx, topM + chartH, _cachedStrokePaint);
                _cachedStrokePaint.PathEffect = null;
                dashEffect.Dispose();

                // Highlight bar region
                float hx1 = leftM + hvi * stepW;
                var highlightColor = _isDarkMode ? SKColors.White.WithAlpha(20) : SKColors.Black.WithAlpha(15);
                _cachedFillPaint.Color = highlightColor;
                canvas.DrawRect(hx1, topM, stepW, chartH, _cachedFillPaint);
            }

            // Y-axis labels
            _cachedTextPaint10.Color = _chartTextDim;
            _cachedTextPaint10.TextAlign = SKTextAlign.Right;
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    int val = (int)(maxVal * (1.0 - (double)i / gridLines));
                    canvas.DrawText(val.ToString(), leftM - 6, y + 4, _cachedTextPaint10);
                }
            }

            // X-axis labels (zoom-aware)
            _cachedTextPaint10.TextAlign = SKTextAlign.Center;
            {
                int labelCount = Math.Min(8, visibleCount);
                int labelStep = Math.Max(1, visibleCount / labelCount);
                for (int vi = 0; vi < visibleCount; vi += labelStep)
                {
                    int i = zStart + vi;
                    float x = leftM + vi * stepW + stepW / 2;
                    var time = _timelineFirstTime.AddSeconds(i * _timelineBucketSize);
                    canvas.DrawText(time.ToString("HH:mm:ss"), x, topM + chartH + 18, _cachedTextPaint10);
                }
            }

            // Zoom hint
            if (visibleCount < _timelineBucketCount)
            {
                string zoomText = $"Zoom: {visibleCount}/{_timelineBucketCount} buckets  (Scroll to zoom, Shift+Scroll to pan)";
                _cachedTextPaint9.Color = _chartTextDim.WithAlpha(150);
                _cachedTextPaint9.TextAlign = SKTextAlign.Right;
                canvas.DrawText(zoomText, w - rightM, topM + chartH + 30, _cachedTextPaint9);
            }

            // Hover tooltip
            if (_hoveredTimelineBucket >= 0 && _hoveredTimelineBucket < _timelineBucketCount)
            {
                var bucketStart = _timelineFirstTime.AddSeconds(_hoveredTimelineBucket * _timelineBucketSize);
                var bucketEnd = bucketStart.AddSeconds(_timelineBucketSize);
                int count = _timelineBuckets[_hoveredTimelineBucket];
                var logs = _timelineBucketLogs[_hoveredTimelineBucket];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{bucketStart:HH:mm:ss} - {bucketEnd:HH:mm:ss}");

                // Find the active state at this time
                if (_timelineStateEntries != null)
                {
                    var midTime = bucketStart.AddSeconds(_timelineBucketSize / 2);
                    foreach (var st in _timelineStateEntries)
                    {
                        if (midTime >= st.StartTime && st.EndTime.HasValue && midTime <= st.EndTime.Value)
                        {
                            sb.AppendLine($"State: {st.StateName}");
                            break;
                        }
                    }
                }

                sb.AppendLine($"{count} error(s)");
                var topMsgs = logs.Take(3).Select(l => TruncateMessage(l.Message, 50));
                foreach (var msg in topMsgs) sb.AppendLine($"  {msg}");
                if (count > 3) sb.AppendLine($"  +{count - 3} more...");
                sb.Append("Click to navigate");

                DrawTooltip(canvas, sb.ToString().TrimEnd(), _timelineMouse.X + 15, _timelineMouse.Y - 10, w, h);
            }
        }

        private void TimelineChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(TimelineChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(TimelineChartCanvas).DpiScaleX;
            _timelineMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _timelineMouse = new SKPoint(-1, -1);
            _hoveredTimelineBucket = -1;
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_timelineBuckets == null || _timelineBucketCount == 0) return;

            int visibleCount = _timelineZoomEnd - _timelineZoomStart;
            bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (isShift)
            {
                // Pan: shift + scroll
                int panAmount = Math.Max(1, visibleCount / 10);
                if (e.Delta > 0)
                {
                    // Pan left
                    _timelineZoomStart = Math.Max(0, _timelineZoomStart - panAmount);
                    _timelineZoomEnd = _timelineZoomStart + visibleCount;
                }
                else
                {
                    // Pan right
                    _timelineZoomEnd = Math.Min(_timelineBucketCount, _timelineZoomEnd + panAmount);
                    _timelineZoomStart = _timelineZoomEnd - visibleCount;
                }
            }
            else
            {
                // Zoom: calculate center from mouse position
                int center = (_timelineZoomStart + _timelineZoomEnd) / 2;
                if (_hoveredTimelineBucket >= _timelineZoomStart && _hoveredTimelineBucket < _timelineZoomEnd)
                    center = _hoveredTimelineBucket;

                // Gentle zoom: 10% per scroll step, minimum 20% of total buckets
                int minVisible = Math.Max(20, _timelineBucketCount / 5);
                int newVisible;
                if (e.Delta > 0)
                    newVisible = Math.Max(minVisible, (int)(visibleCount * 0.90)); // Zoom in 10%
                else
                    newVisible = Math.Min(_timelineBucketCount, (int)(visibleCount * 1.12)); // Zoom out 12%

                _timelineZoomStart = Math.Max(0, center - newVisible / 2);
                _timelineZoomEnd = _timelineZoomStart + newVisible;
                if (_timelineZoomEnd > _timelineBucketCount)
                {
                    _timelineZoomEnd = _timelineBucketCount;
                    _timelineZoomStart = Math.Max(0, _timelineZoomEnd - newVisible);
                }
            }

            e.Handled = true;
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_hoveredTimelineBucket >= 0 && _hoveredTimelineBucket < _timelineBucketCount)
            {
                var logs = _timelineBucketLogs[_hoveredTimelineBucket];
                if (_navigateToLogCallback != null && logs.Any())
                {
                    _navigateToLogCallback(logs.First());
                }
            }
        }

        // ==========================================
        //  SHARED TOOLTIP RENDERER
        // ==========================================
        private void DrawTooltip(SKCanvas canvas, string text, float x, float y, float canvasW, float canvasH)
        {
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Split('\n');
            float padding = 8, lineH = 16;
            float boxH = lines.Length * lineH + padding * 2;

            // Use cached paint for measurement
            _cachedTextPaint11.TextAlign = SKTextAlign.Left;
            float maxW = 0;
            foreach (var line in lines)
            {
                float lw = _cachedTextPaint11.MeasureText(line);
                if (lw > maxW) maxW = lw;
            }
            float boxW = maxW + padding * 2;

            // Clamp to canvas bounds
            if (x + boxW > canvasW - 5) x = canvasW - boxW - 5;
            if (y + boxH > canvasH - 5) y = canvasH - boxH - 5;
            if (x < 5) x = 5;
            if (y < 5) y = 5;

            var rect = new SKRect(x, y, x + boxW, y + boxH);

            // Shadow (MaskFilter needs disposal, so use using here)
            using (var shadowPaint = new SKPaint { Color = SKColors.Black.WithAlpha(100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4), IsAntialias = true })
                canvas.DrawRoundRect(rect.Left + 2, rect.Top + 2, rect.Width, rect.Height, 6, 6, shadowPaint);

            // Background
            _cachedTooltipBgPaint.Color = _tooltipBg;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBgPaint);

            // Border
            _cachedTooltipBorderPaint.Color = _tooltipBorder;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBorderPaint);

            // Text
            _cachedTextPaint11.Color = _chartText;
            for (int i = 0; i < lines.Length; i++)
            {
                canvas.DrawText(lines[i], x + padding, y + padding + (i + 1) * lineH - 3, _cachedTextPaint11);
            }
        }

        /// <summary>Regex for S4-5 binary PLC state transitions: "==== STATE_XXX - Enter/Exit ======"</summary>
        private static readonly Regex _s4StateRegex =
            new Regex(@"STATE_(\w+)\s*-\s*(Enter|Exit)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // Calculate state entries — supports both S6 (Manager thread) and S4-5 (==== STATE) patterns
        private List<StateEntry> CalculateStateEntries(List<LogEntry> plcLogs)
        {
            var statesList = new List<StateEntry>();
            if (plcLogs.Count == 0) return statesList;

            // Logs are already sorted by Date from the loading phase - no need to sort again
            DateTime logEndLimit = plcLogs[plcLogs.Count - 1].Date;

            // ── S6: PlcMngr transitions (Manager thread + "PlcMngr:" + "->") ──
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
                // S6 path
                // Add the initial "from" state before the first transition
                // e.g., "PlcMngr: OFF -> MECH_INIT" means the system was in OFF before this log
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
                                StartTime = plcLogs[0].Date, // from the very first log
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

            // ── S4-5 fallback: "==== STATE_XXX - Enter ======" in PLC logs ──
            var enterLogs = new List<(LogEntry Log, string StateName)>();
            for (int i = 0; i < plcLogs.Count; i++)
            {
                var l = plcLogs[i];
                if (l.Message != null && l.Message.Contains("==== STATE"))
                {
                    var match = _s4StateRegex.Match(l.Message);
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

        private string ExtractStateName(LogEntry log)
        {
            // Try to extract state name from Pattern or Data field
            // Pattern example: "STATE_IDLE", "STATE_RUNNING", etc.
            if (!string.IsNullOrWhiteSpace(log.Pattern) && log.Pattern.Contains("STATE"))
                return log.Pattern;

            if (!string.IsNullOrWhiteSpace(log.Data) && log.Data.Contains("State"))
            {
                // Try to parse "State=XXX" or "CurrentState=XXX"
                var parts = log.Data.Split(new[] { '=', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Trim().EndsWith("State", StringComparison.OrdinalIgnoreCase))
                        return parts[i + 1].Trim();
                }
            }

            return "Unknown";
        }

        // ==========================================
        //  DRILL-DOWN FILTER HANDLERS
        // ==========================================
        private void ApplyLoggerFilter(string logger)
        {
            if (_applyFilterCallback == null)
            {
                MessageBox.Show($"Filter by Logger: {logger}\n\nNo filter callback configured.",
                    "Logger Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Filter logs to show only Logger:\n\n{logger}\n\nThis will close the statistics window and apply the filter.",
                "Apply Logger Filter", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
            {
                _applyFilterCallback("Logger", logger);
                Close();
            }
        }

        private void ApplyStateFilter(string state)
        {
            if (_applyFilterCallback == null)
            {
                MessageBox.Show($"Filter by STATE: {state}\n\nNo filter callback configured.",
                    "State Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Filter logs to show only STATE:\n\n{state}\n\nThis will close the statistics window and apply the filter.",
                "Apply State Filter", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
            {
                _applyFilterCallback("State", state);
                Close();
            }
        }
    }
    public class LoadStat
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string DisplayText { get; set; }
        public double BarWidth { get; set; }
    }

    // GapInfo moved to Models/GapInfo.cs

    // (ErrorStat ??? ???? ?????, ?? ???? ?????? ??? ????? ????? ???)
    public class ErrorStat
    {
        public string Name { get; set; }      // ?? ?-Logger ?? ?-Thread
        public string FullName { get; set; }  // Full name for tooltip
        public string Message { get; set; }   // ????? ?????? (???? ????????? ??????)
        public int Count { get; set; }
        public string DisplayText { get; set; }
        public double BarWidth { get; set; }
    }
    // Helper models (same as before but ensured they have necessary props)

}