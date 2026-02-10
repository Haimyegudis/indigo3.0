using DocumentFormat.OpenXml.Spreadsheet;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private List<ErrorStat> _appLoggerErrorStats;
        private List<ErrorStat> _appThreadErrorStats;
        private List<LoadStat> _appThreadStats;
        private List<LoadStat> _appLoggerStats;
        private List<GapInfo> _appGaps;

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

        // Color palette
        private static readonly SKColor[] ChartColors = new[]
        {
            SKColor.Parse("#E74C3C"), SKColor.Parse("#F1C40F"), SKColor.Parse("#3498DB"),
            SKColor.Parse("#2ECC71"), SKColor.Parse("#9B59B6"), SKColor.Parse("#E67E22"),
            SKColor.Parse("#95A5A6"), SKColor.Parse("#16A085"), SKColor.Parse("#F39C12"),
            SKColor.Parse("#8E44AD")
        };

        // ??? ??: ????? ???? ??? ??? ?????? ??????. ?? ????? ?? ?????? ?-StatsWindow ???? ????? ????.
        public StatsWindow(IEnumerable<LogEntry> plcLogs, IEnumerable<LogEntry> appLogs, Action<string, string> applyFilterCallback = null, Action<LogEntry> navigateToLogCallback = null)
        {
            InitializeComponent();
            _plcLogs = plcLogs?.ToList() ?? new List<LogEntry>();
            _appLogs = appLogs?.ToList() ?? new List<LogEntry>();
            _applyFilterCallback = applyFilterCallback;
            _navigateToLogCallback = navigateToLogCallback;

            System.Diagnostics.Debug.WriteLine($"[STATS] Initialized with {_plcLogs.Count} PLC logs and {_appLogs.Count} APP logs");

            Loaded += (s, e) => CalculateStatistics();
        }

        private void CalculateStatistics()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Starting calculation...");

            int totalLogs = _plcLogs.Count + _appLogs.Count;
            if (totalLogs == 0)
            {
                SummaryText.Text = "No logs available for analysis.";
                return;
            }

            // Fast summary - avoid sorting all dates, just get min/max directly
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
            {
                var timeSpan = maxDate - minDate;
                SummaryText.Text = $"Analyzed {totalLogs:N0} logs spanning {timeSpan.TotalMinutes:F1} minutes";
            }

            CalculatePlcStatistics();
            CalculateAppStatistics();
            CalculateAdvancedAnalytics();
        }

        private void CalculatePlcStatistics()
        {
            if (_plcLogs.Count == 0) { PlcSummaryText.Text = "No PLC logs available."; return; }

            PlcSummaryText.Text = $"PLC Logs: {_plcLogs.Count:N0} entries";

            var errors = GetErrorLogs(_plcLogs);
            _plcErrorStats = CalculateErrorHistogram(errors, 10);
            PlcErrorHistogram.ItemsSource = _plcErrorStats;
            PlcErrorCountText.Text = errors.Count > 0 ? $"(Total: {errors.Count:N0})" : "(No errors)";

            _plcThreadStats = CalculateLoadDistribution(_plcLogs, l => l.ThreadName, 10);
            PlcThreadHistogram.ItemsSource = _plcThreadStats;
            PlcThreadCountText.Text = _plcThreadStats.Count > 0 ? "(Top 10)" : "";

            _plcGaps = FindGaps(_plcLogs);
            if (_plcGaps.Count > 0)
            {
                PlcGapSummaryText.Text = $"Found {_plcGaps.Count} gap(s) >= 2s. Total: {FormatDuration(TimeSpan.FromSeconds(_plcGaps.Sum(g => g.Duration.TotalSeconds)))}";
                PlcGapDataGrid.ItemsSource = _plcGaps;
                PlcGapDataGrid.Visibility = Visibility.Visible;
                PlcNoGapsMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlcGapSummaryText.Text = "No significant time gaps.";
                PlcGapDataGrid.Visibility = Visibility.Collapsed;
                PlcNoGapsMessage.Visibility = Visibility.Visible;
            }
        }

        private void CalculateAppStatistics()
        {
            if (_appLogs.Count == 0) { AppSummaryText.Text = "No APP logs available."; return; }

            AppSummaryText.Text = $"APP Logs: {_appLogs.Count:N0} entries";
            var errors = GetErrorLogs(_appLogs);

            _appLoggerErrorStats = CalculateErrorHistogram(errors, 10, l => l.Method ?? "Unknown");
            AppLoggerErrorHistogram.ItemsSource = _appLoggerErrorStats;
            AppLoggerErrorCountText.Text = errors.Count > 0 ? $"(Total: {errors.Count:N0})" : "(No errors)";

            _appThreadErrorStats = CalculateErrorHistogram(errors, 10, l => GetShortLoggerName(l.Logger));
            AppThreadErrorHistogram.ItemsSource = _appThreadErrorStats;
            AppThreadErrorCountText.Text = errors.Count > 0 ? $"(Top 10 loggers)" : "";

            _appThreadStats = CalculateLoadDistribution(_appLogs, l => GetShortLoggerName(l.Logger), 10, l => l.Logger);
            AppThreadHistogram.ItemsSource = _appThreadStats;
            AppThreadCountText.Text = "(Top 10)";

            _appLoggerStats = CalculateLoadDistribution(_appLogs, l => l.Method ?? "Unknown", 15);
            AppLoggerHistogram.ItemsSource = _appLoggerStats;
            AppLoggerCountText.Text = "(Top 15)";

            _appGaps = FindGaps(_appLogs);
            if (_appGaps.Count > 0)
            {
                AppGapSummaryText.Text = $"Found {_appGaps.Count} gap(s) >= 2s. Total: {FormatDuration(TimeSpan.FromSeconds(_appGaps.Sum(g => g.Duration.TotalSeconds)))}";
                AppGapDataGrid.ItemsSource = _appGaps;
                AppGapDataGrid.Visibility = Visibility.Visible;
                AppNoGapsMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                AppGapSummaryText.Text = "No significant time gaps.";
                AppGapDataGrid.Visibility = Visibility.Collapsed;
                AppNoGapsMessage.Visibility = Visibility.Visible;
            }
        }

        // ==========================================
        //  HELPERS
        // ==========================================

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

            // Get top N using partial sort
            var topItems = counts.OrderByDescending(kvp => kvp.Value).Take(take).ToList();
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

            var topItems = counts.OrderByDescending(kvp => kvp.Value).Take(take).ToList();
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

        private string GetShortLoggerName(string logger)
        {
            if (string.IsNullOrEmpty(logger)) return "Unknown";
            var parts = logger.Split('.');
            if (parts.Length <= 2) return logger;
            return string.Join(".", parts.Skip(parts.Length - 2));
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
                AppendSection(sb, "ERRORS BY LOGGER", _appLoggerErrorStats, s => $"{s.Name} ({s.Count} errors)");
                AppendSection(sb, "ERRORS BY THREAD", _appThreadErrorStats, s => $"{s.Name} ({s.Count} errors)");
                AppendSection(sb, "THREAD LOAD", _appThreadStats, s => $"{s.Name}: {s.DisplayText}");
                AppendSection(sb, "LOGGER LOAD", _appLoggerStats, s => $"{s.FullName}: {s.DisplayText}");
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
                    sb.AppendLine($"  #{g.Index} | {g.DurationText} | Start: {g.StartTime:HH:mm:ss.fff} | End: {g.EndTime:HH:mm:ss.fff}");
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
        private void CalculateAdvancedAnalytics()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Calculating advanced analytics...");

            // Get all error logs (PLC + APP)
            var allErrors = GetErrorLogs(_plcLogs.Concat(_appLogs).ToList());

            if (!allErrors.Any())
            {
                AnalyticsSummaryText.Text = "No error logs available for advanced analytics.";
                return;
            }

            AnalyticsSummaryText.Text = $"Advanced Analytics - Total Errors: {allErrors.Count:N0}";

            // 1. Top 10 Errors (PLC by Thread, APP by Logger)
            CreateLoggerBarChart(GetErrorLogs(_plcLogs), GetErrorLogs(_appLogs.Any() ? _appLogs : new List<LogEntry>()));

            // 2. Errors by STATE (Pie Chart)
            CreateStatePieChart(_plcLogs);

            // 3. Error Density Timeline (Line Chart)
            CreateErrorTimelineChart(allErrors);
        }

        private void CreateLoggerBarChart(List<LogEntry> plcErrors, List<LogEntry> appErrors)
        {
            // PLC: group by Thread (since there's only one logger: E1.PLC)
            // APP: group by Logger
            var combinedCounts = new List<(string Name, int Count, List<LogEntry> Logs)>();

            if (plcErrors.Any())
            {
                var plcThreadGroups = plcErrors.GroupBy(l => l.ThreadName ?? "Unknown")
                    .Select(g => (Name: $"[PLC] {g.Key}", Count: g.Count(), Logs: g.ToList())).ToList();
                combinedCounts.AddRange(plcThreadGroups);
            }
            if (appErrors.Any())
            {
                var appLoggerGroups = appErrors.GroupBy(l => l.Logger ?? "Unknown")
                    .Select(g => (Name: $"[APP] {GetShortLoggerName(g.Key)}", Count: g.Count(), Logs: g.ToList())).ToList();
                combinedCounts.AddRange(appLoggerGroups);
            }

            _barChartData = combinedCounts.OrderByDescending(x => x.Count).Take(10).ToList();

            if (!_barChartData.Any())
            {
                LoggerChartCountText.Text = "(No data)";
                return;
            }

            _loggerData = _barChartData.Select(x => (x.Name, x.Count)).ToList();
            LoggerChartCountText.Text = $"({_barChartData.Sum(x => x.Count):N0} errors) - Click bar to navigate";

            BarChartCanvas.InvalidateVisual();
        }

        private void BarChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColor.Parse("#2D2D30"));

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

            using (var labelPaint = new SKPaint { Color = SKColor.Parse("#C8C8C8"), TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            using (var valuePaint = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
            {
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
                    var barColor = isHovered ? SKColor.Parse("#FF6B5A") : SKColor.Parse("#E74C3C");
                    using (var barPaint = new SKPaint { IsAntialias = true })
                    {
                        barPaint.Shader = SKShader.CreateLinearGradient(
                            new SKPoint(barRect.Left, barRect.Top), new SKPoint(barRect.Right, barRect.Top),
                            new[] { barColor, barColor.WithAlpha(180) }, null, SKShaderTileMode.Clamp);
                        canvas.DrawRoundRect(barRect, 4, 4, barPaint);
                    }

                    // Hover highlight border
                    if (isHovered)
                    {
                        using (var borderPaint = new SKPaint { Color = SKColors.White.WithAlpha(120), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true })
                            canvas.DrawRoundRect(barRect, 4, 4, borderPaint);
                    }

                    // Label
                    string label = item.Name.Length > 22 ? item.Name.Substring(0, 19) + "..." : item.Name;
                    canvas.DrawText(label, 5, y + barHeight / 2 + 4, labelPaint);

                    // Value
                    string valueText = item.Count.ToString("N0");
                    float tw = valuePaint.MeasureText(valueText);
                    canvas.DrawText(valueText, leftMargin + barW + 6, y + barHeight / 2 + 4, valuePaint);
                }
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

        private void CreateStatePieChart(List<LogEntry> plcLogs)
        {
            var plcErrors = GetErrorLogs(plcLogs);
            if (!plcErrors.Any()) { StateChartCountText.Text = "(No PLC errors)"; return; }

            var stateEntries = CalculateStateEntries(plcLogs);
            if (!stateEntries.Any()) { StateChartCountText.Text = "(No state transitions found)"; return; }

            // Map errors to states using binary search on sorted state entries O(n log m)
            var errorsByState = new Dictionary<string, List<LogEntry>>();
            foreach (var error in plcErrors)
            {
                // Binary search: find the state interval containing this error's date
                int lo = 0, hi = stateEntries.Count - 1;
                StateEntry foundState = null;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var s = stateEntries[mid];
                    if (error.Date < s.StartTime)
                        hi = mid - 1;
                    else if (s.EndTime.HasValue && error.Date > s.EndTime.Value)
                        lo = mid + 1;
                    else
                    {
                        foundState = s;
                        break;
                    }
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

            _pieChartData = errorsByState
                .Select(kvp => (State: kvp.Key, Count: kvp.Value.Count, Logs: kvp.Value))
                .OrderByDescending(x => x.Count).Take(10).ToList();

            if (!_pieChartData.Any()) { StateChartCountText.Text = "(No errors matched to states)"; return; }

            _stateData = _pieChartData.Select(x => (x.State, x.Count)).ToList();
            StateChartCountText.Text = $"({_pieChartData.Sum(x => x.Count):N0} errors with state info) - Click to navigate";

            PieChartCanvas.InvalidateVisual();
        }

        private void PieChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColor.Parse("#2D2D30"));

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
                using (var slicePaint = new SKPaint { Color = isHovered ? color.WithAlpha(255) : color.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Fill })
                using (var strokePaint = new SKPaint { Color = SKColor.Parse("#2D2D30"), StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke })
                {
                    var path = new SKPath();
                    path.MoveTo(cx + exX, cy + exY);
                    path.ArcTo(new SKRect(cx - radius + exX, cy - radius + exY, cx + radius + exX, cy + radius + exY), startAngle, sweep, false);
                    path.Close();
                    canvas.DrawPath(path, slicePaint);
                    canvas.DrawPath(path, strokePaint);
                }

                // Percentage label inside slice
                if (sweep > 18)
                {
                    float labelR = radius * 0.65f;
                    float lx = cx + exX + (float)(labelR * Math.Cos(midAngle * Math.PI / 180));
                    float ly = cy + exY + (float)(labelR * Math.Sin(midAngle * Math.PI / 180));
                    using (var textPaint = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
                        canvas.DrawText($"{(float)item.Count / total * 100:F0}%", lx, ly + 4, textPaint);
                }

                startAngle += sweep;
            }

            // Legend
            float legendX = chartAreaW + 10;
            float legendY = 15;
            using (var legendTextPaint = new SKPaint { Color = SKColor.Parse("#C8C8C8"), TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            using (var legendCountPaint = new SKPaint { Color = SKColor.Parse("#999999"), TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                for (int i = 0; i < _pieChartData.Count; i++)
                {
                    var item = _pieChartData[i];
                    var color = ChartColors[i % ChartColors.Length];
                    bool isHov = i == _hoveredPieIndex;

                    using (var dotPaint = new SKPaint { Color = isHov ? color : color.WithAlpha(200), IsAntialias = true })
                        canvas.DrawCircle(legendX + 6, legendY + 6, 6, dotPaint);

                    string name = item.State.Length > 16 ? item.State.Substring(0, 13) + "..." : item.State;
                    legendTextPaint.Color = isHov ? SKColors.White : SKColor.Parse("#C8C8C8");
                    canvas.DrawText(name, legendX + 18, legendY + 11, legendTextPaint);
                    canvas.DrawText($"({item.Count})", legendX + 18, legendY + 24, legendCountPaint);
                    legendY += 30;
                }
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

        private void CreateErrorTimelineChart(List<LogEntry> errorLogs)
        {
            if (!errorLogs.Any()) { TimelineChartInfo.Text = "(No errors)"; return; }

            var sortedErrors = errorLogs.OrderBy(l => l.Date).ToList();
            _timelineFirstTime = sortedErrors.First().Date;
            var lastTime = sortedErrors.Last().Date;
            var totalDuration = lastTime - _timelineFirstTime;

            if (totalDuration.TotalMinutes < 2) _timelineBucketCount = 60;
            else if (totalDuration.TotalMinutes < 30) _timelineBucketCount = 100;
            else _timelineBucketCount = 120;

            _timelineBucketSize = totalDuration.TotalSeconds / _timelineBucketCount;
            var bucketSizeDisplay = _timelineBucketSize < 60 ? $"{_timelineBucketSize:F1}s" : $"{_timelineBucketSize / 60:F1}min";
            TimelineChartInfo.Text = $"({sortedErrors.Count} errors over {totalDuration.TotalMinutes:F1} min, resolution: {bucketSizeDisplay})";

            _timelineBuckets = new int[_timelineBucketCount];
            _timelineBucketLogs = new List<LogEntry>[_timelineBucketCount];
            for (int i = 0; i < _timelineBucketCount; i++)
                _timelineBucketLogs[i] = new List<LogEntry>();

            foreach (var log in sortedErrors)
            {
                int idx = (int)((log.Date - _timelineFirstTime).TotalSeconds / _timelineBucketSize);
                if (idx >= _timelineBucketCount) idx = _timelineBucketCount - 1;
                if (idx < 0) idx = 0;
                _timelineBuckets[idx]++;
                _timelineBucketLogs[idx].Add(log);
            }

            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColor.Parse("#2D2D30"));

            if (_timelineBuckets == null || _timelineBucketCount == 0) return;

            float w = info.Width, h = info.Height;
            float leftM = 55, rightM = 15, topM = 15, bottomM = 35;
            float chartW = w - leftM - rightM;
            float chartH = h - topM - bottomM;
            int maxVal = _timelineBuckets.Max();
            if (maxVal == 0) maxVal = 1;

            _hoveredTimelineBucket = -1;

            // Grid lines
            using (var gridPaint = new SKPaint { Color = SKColor.Parse("#FFFFFF").WithAlpha(25), StrokeWidth = 1, IsAntialias = false })
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    canvas.DrawLine(leftM, y, w - rightM, y, gridPaint);
                }
            }

            // Area fill + line
            float stepW = chartW / _timelineBucketCount;
            var linePath = new SKPath();
            var areaPath = new SKPath();

            areaPath.MoveTo(leftM, topM + chartH);
            for (int i = 0; i < _timelineBucketCount; i++)
            {
                float x = leftM + i * stepW + stepW / 2;
                float valH = (float)_timelineBuckets[i] / maxVal * chartH;
                float y = topM + chartH - valH;

                if (i == 0) linePath.MoveTo(x, y); else linePath.LineTo(x, y);
                areaPath.LineTo(x, y);

                // Check hover
                if (_timelineMouse.X >= leftM + i * stepW && _timelineMouse.X < leftM + (i + 1) * stepW &&
                    _timelineMouse.Y >= topM && _timelineMouse.Y <= topM + chartH)
                {
                    _hoveredTimelineBucket = i;
                }
            }
            areaPath.LineTo(leftM + (_timelineBucketCount - 1) * stepW + stepW / 2, topM + chartH);
            areaPath.Close();

            // Gradient fill
            using (var fillPaint = new SKPaint { IsAntialias = true })
            {
                fillPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, topM), new SKPoint(0, topM + chartH),
                    new[] { SKColor.Parse("#3498DB").WithAlpha(120), SKColor.Parse("#3498DB").WithAlpha(15) },
                    null, SKShaderTileMode.Clamp);
                canvas.DrawPath(areaPath, fillPaint);
            }

            // Line
            using (var linePaint = new SKPaint { Color = SKColor.Parse("#3498DB"), StrokeWidth = 2.5f, IsAntialias = true, Style = SKPaintStyle.Stroke })
                canvas.DrawPath(linePath, linePaint);

            // Data points (only if few enough)
            if (_timelineBucketCount <= 60)
            {
                using (var dotPaint = new SKPaint { Color = SKColor.Parse("#3498DB"), IsAntialias = true })
                {
                    for (int i = 0; i < _timelineBucketCount; i++)
                    {
                        if (_timelineBuckets[i] > 0)
                        {
                            float x = leftM + i * stepW + stepW / 2;
                            float valH = (float)_timelineBuckets[i] / maxVal * chartH;
                            float y = topM + chartH - valH;
                            canvas.DrawCircle(x, y, i == _hoveredTimelineBucket ? 5 : 3, dotPaint);
                        }
                    }
                }
            }

            // Hover vertical line + highlight
            if (_hoveredTimelineBucket >= 0)
            {
                float hx = leftM + _hoveredTimelineBucket * stepW + stepW / 2;
                using (var hoverLinePaint = new SKPaint { Color = SKColors.White.WithAlpha(80), StrokeWidth = 1, IsAntialias = false,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0) })
                    canvas.DrawLine(hx, topM, hx, topM + chartH, hoverLinePaint);

                // Highlight bar region
                float hx1 = leftM + _hoveredTimelineBucket * stepW;
                float hx2 = hx1 + stepW;
                using (var highlightPaint = new SKPaint { Color = SKColors.White.WithAlpha(20) })
                    canvas.DrawRect(hx1, topM, stepW, chartH, highlightPaint);
            }

            // Y-axis labels
            using (var yLabelPaint = new SKPaint { Color = SKColor.Parse("#999999"), TextSize = 10, IsAntialias = true, TextAlign = SKTextAlign.Right, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    int val = (int)(maxVal * (1.0 - (double)i / gridLines));
                    canvas.DrawText(val.ToString(), leftM - 6, y + 4, yLabelPaint);
                }
            }

            // X-axis labels
            using (var xLabelPaint = new SKPaint { Color = SKColor.Parse("#999999"), TextSize = 10, IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                int labelCount = Math.Min(8, _timelineBucketCount);
                int labelStep = Math.Max(1, _timelineBucketCount / labelCount);
                for (int i = 0; i < _timelineBucketCount; i += labelStep)
                {
                    float x = leftM + i * stepW + stepW / 2;
                    var time = _timelineFirstTime.AddSeconds(i * _timelineBucketSize);
                    canvas.DrawText(time.ToString("HH:mm:ss"), x, topM + chartH + 18, xLabelPaint);
                }
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

            using (var measurePaint = new SKPaint { TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                float maxW = 0;
                foreach (var line in lines)
                {
                    float lw = measurePaint.MeasureText(line);
                    if (lw > maxW) maxW = lw;
                }
                float boxW = maxW + padding * 2;

                // Clamp to canvas bounds
                if (x + boxW > canvasW - 5) x = canvasW - boxW - 5;
                if (y + boxH > canvasH - 5) y = canvasH - boxH - 5;
                if (x < 5) x = 5;
                if (y < 5) y = 5;

                var rect = new SKRect(x, y, x + boxW, y + boxH);

                // Shadow
                using (var shadowPaint = new SKPaint { Color = SKColors.Black.WithAlpha(100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4), IsAntialias = true })
                    canvas.DrawRoundRect(rect.Left + 2, rect.Top + 2, rect.Width, rect.Height, 6, 6, shadowPaint);

                // Background
                using (var bgPaint = new SKPaint { Color = SKColor.Parse("#1E3A5F").WithAlpha(245), IsAntialias = true })
                    canvas.DrawRoundRect(rect, 6, 6, bgPaint);

                // Border
                using (var borderPaint = new SKPaint { Color = SKColor.Parse("#3498DB").WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true })
                    canvas.DrawRoundRect(rect, 6, 6, borderPaint);

                // Text
                using (var textPaint = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        canvas.DrawText(lines[i], x + padding, y + padding + (i + 1) * lineH - 3, textPaint);
                    }
                }
            }
        }

        // Calculate state entries using same logic as StateFailureAnalyzer
        private List<StateEntry> CalculateStateEntries(List<LogEntry> plcLogs)
        {
            var statesList = new List<StateEntry>();
            // Logs are already sorted by Date from the loading phase - no need to sort again

            // Find PlcMngr transitions without LINQ
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

            if (transitionLogs.Count == 0) return statesList;

            DateTime logEndLimit = plcLogs[plcLogs.Count - 1].Date;

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

                // Set EndTime
                if (i < transitionLogs.Count - 1)
                    entry.EndTime = transitionLogs[i + 1].Date;
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

    public class GapInfo
    {
        public int Index { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string DurationText { get; set; }
        public string LastMessageBeforeGap { get; set; }
        public LogEntry LastLogBeforeGap { get; set; }
    }

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