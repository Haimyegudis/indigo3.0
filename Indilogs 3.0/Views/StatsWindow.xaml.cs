using DocumentFormat.OpenXml.Spreadsheet;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.Wpf;
using System.Windows.Media;

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

        // ??? ??: ????? ???? ??? ??? ?????? ??????. ?? ????? ?? ?????? ?-StatsWindow ???? ????? ????.
        public StatsWindow(IEnumerable<LogEntry> plcLogs, IEnumerable<LogEntry> appLogs, Action<string, string> applyFilterCallback = null)
        {
            InitializeComponent();
            _plcLogs = plcLogs?.ToList() ?? new List<LogEntry>();
            _appLogs = appLogs?.ToList() ?? new List<LogEntry>();
            _applyFilterCallback = applyFilterCallback;

            System.Diagnostics.Debug.WriteLine($"[STATS] Initialized with {_plcLogs.Count} PLC logs and {_appLogs.Count} APP logs");

            Loaded += (s, e) => CalculateStatistics();
        }

        private void CalculateStatistics()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Starting calculation...");

            // ????? ????? ?????
            int totalLogs = _plcLogs.Count + _appLogs.Count;
            if (totalLogs == 0)
            {
                SummaryText.Text = "No logs available for analysis.";
                return;
            }

            // ????? ???? ????? ????
            var allDates = _plcLogs.Concat(_appLogs).Select(l => l.Date).OrderBy(d => d).ToList();
            if (allDates.Any())
            {
                var timeSpan = allDates.Last() - allDates.First();
                SummaryText.Text = $"Analyzed {totalLogs:N0} logs spanning {timeSpan.TotalMinutes:F1} minutes";
            }

            // ??????? ??? ??? ?????
            CalculatePlcStatistics();
            CalculateAppStatistics();
            CalculateAdvancedAnalytics();
        }

        // ==========================================
        //  PLC LOGS ANALYSIS
        // ==========================================
        private void CalculatePlcStatistics()
        {
            if (!_plcLogs.Any())
            {
                PlcSummaryText.Text = "No PLC logs available.";
                return;
            }

            PlcSummaryText.Text = $"PLC Logs: {_plcLogs.Count:N0} entries";

            // 1. PLC Errors
            var errors = GetErrorLogs(_plcLogs);
            _plcErrorStats = CalculateErrorHistogram(errors, 10);
            PlcErrorHistogram.ItemsSource = _plcErrorStats;
            PlcErrorCountText.Text = errors.Any() ? $"(Total: {errors.Count:N0})" : "(No errors)";

            // 2. PLC Thread Load
            _plcThreadStats = CalculateLoadDistribution(_plcLogs, l => l.ThreadName, 10);
            PlcThreadHistogram.ItemsSource = _plcThreadStats;
            PlcThreadCountText.Text = _plcThreadStats.Any() ? "(Top 10)" : "";

            // 3. PLC Gaps
            _plcGaps = FindGaps(_plcLogs);
            if (_plcGaps.Any())
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

        // ==========================================
        //  APP LOGS ANALYSIS
        // ==========================================
        private void CalculateAppStatistics()
        {
            if (!_appLogs.Any())
            {
                AppSummaryText.Text = "No APP logs available.";
                return;
            }

            AppSummaryText.Text = $"APP Logs: {_appLogs.Count:N0} entries";

            var errors = GetErrorLogs(_appLogs);

            // 1. APP Errors by Logger (???? ??????)
            _appLoggerErrorStats = CalculateErrorHistogram(errors, 10, l => GetShortLoggerName(l.Logger));
            AppLoggerErrorHistogram.ItemsSource = _appLoggerErrorStats;
            AppLoggerErrorCountText.Text = errors.Any() ? $"(Total: {errors.Count:N0})" : "(No errors)";

            // 2. APP Errors by Thread (???? ??????)
            _appThreadErrorStats = CalculateErrorHistogram(errors, 10, l => l.ThreadName);
            AppThreadErrorHistogram.ItemsSource = _appThreadErrorStats;
            AppThreadErrorCountText.Text = errors.Any() ? $"(Top 10 threads)" : "";

            // 3. APP Thread Load (General)
            _appThreadStats = CalculateLoadDistribution(_appLogs, l => l.ThreadName, 10);
            AppThreadHistogram.ItemsSource = _appThreadStats;
            AppThreadCountText.Text = "(Top 10)";

            // 4. APP Logger Load (General)
            _appLoggerStats = CalculateLoadDistribution(_appLogs, l => GetShortLoggerName(l.Logger), 15, l => l.Logger);
            AppLoggerHistogram.ItemsSource = _appLoggerStats;
            AppLoggerCountText.Text = "(Top 15)";

            // 5. APP Gaps
            _appGaps = FindGaps(_appLogs);
            if (_appGaps.Any())
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

        private List<LogEntry> GetErrorLogs(List<LogEntry> source)
        {
            return source.Where(l =>
                string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "Fatal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "FATAL", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Generic Error Histogram Calculator (By Message or Custom Key)
        private List<ErrorStat> CalculateErrorHistogram(List<LogEntry> errors, int take, Func<LogEntry, string> keySelector = null)
        {
            if (!errors.Any()) return new List<ErrorStat>();

            // Default key is the Message
            keySelector = keySelector ?? (l => TruncateMessage(l.Message, 100));

            var grouped = errors
                .GroupBy(keySelector)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(take)
                .ToList();

            int maxCount = grouped.Max(g => g.Count);

            return grouped.Select(g => new ErrorStat
            {
                Name = g.Key,    // For Logger/Thread names
                Message = g.Key, // For error messages
                Count = g.Count,
                DisplayText = $"{g.Count:N0}",
                BarWidth = maxCount > 0 ? (double)g.Count / maxCount * (keySelector == null ? 300 : 200) : 0
            }).ToList();
        }

        // Generic Load Distribution Calculator
        private List<LoadStat> CalculateLoadDistribution(List<LogEntry> logs, Func<LogEntry, string> keySelector, int take, Func<LogEntry, string> fullNameSelector = null)
        {
            var grouped = logs
                .Where(l => !string.IsNullOrEmpty(keySelector(l)))
                .GroupBy(keySelector)
                .Select(g => new { Name = g.Key, Count = g.Count(), FullName = fullNameSelector != null ? fullNameSelector(g.First()) : g.Key })
                .OrderByDescending(g => g.Count)
                .Take(take)
                .ToList();

            if (!grouped.Any()) return new List<LoadStat>();

            int maxCount = grouped.Max(g => g.Count);
            int total = logs.Count;

            return grouped.Select(g => new LoadStat
            {
                Name = g.Name,
                FullName = g.FullName,
                Count = g.Count,
                Percentage = (double)g.Count / total * 100,
                DisplayText = $"{g.Count:N0} ({(double)g.Count / total * 100:F1}%)",
                BarWidth = maxCount > 0 ? (double)g.Count / maxCount * 200 : 0
            }).ToList();
        }

        private List<GapInfo> FindGaps(List<LogEntry> logs)
        {
            var gaps = new List<GapInfo>();
            if (logs == null || logs.Count < 2) return gaps;

            var ordered = logs.OrderBy(l => l.Date).ToList();
            const double threshold = 2.0;

            for (int i = 1; i < ordered.Count; i++)
            {
                var diff = ordered[i].Date - ordered[i - 1].Date;
                if (diff.TotalSeconds >= threshold)
                {
                    gaps.Add(new GapInfo
                    {
                        Index = gaps.Count + 1,
                        StartTime = ordered[i - 1].Date,
                        EndTime = ordered[i].Date,
                        Duration = diff,
                        DurationText = FormatDuration(diff),
                        LastMessageBeforeGap = TruncateMessage(ordered[i - 1].Message, 100),
                        LastLogBeforeGap = ordered[i - 1]
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

            // 1. Top 10 Loggers by Error Count (Bar Chart)
            CreateLoggerBarChart(allErrors);

            // 2. Errors by STATE (Pie Chart)
            CreateStatePieChart(_plcLogs);

            // 3. Error Density Timeline (Line Chart)
            CreateErrorTimelineChart(allErrors);
        }

        private void CreateLoggerBarChart(List<LogEntry> errorLogs)
        {
            // For PLC logs, group by Thread instead of Logger
            // For APP logs, group by Logger
            var plcErrors = errorLogs.Where(l => l.Logger?.Contains("E1.PLC") == true || string.IsNullOrEmpty(l.Logger)).ToList();
            var appErrors = errorLogs.Where(l => l.Logger?.Contains("E1.PLC") != true && !string.IsNullOrEmpty(l.Logger)).ToList();

            List<(string Name, int Count)> combinedCounts = new List<(string, int)>();

            // Add PLC errors by Thread
            if (plcErrors.Any())
            {
                var plcThreadCounts = plcErrors
                    .GroupBy(l => l.ThreadName ?? "Unknown")
                    .Select(g => (Name: $"[PLC] {g.Key}", Count: g.Count()))
                    .ToList();
                combinedCounts.AddRange(plcThreadCounts);
            }

            // Add APP errors by Logger
            if (appErrors.Any())
            {
                var appLoggerCounts = appErrors
                    .GroupBy(l => l.Logger ?? "Unknown")
                    .Select(g => (Name: GetShortLoggerName(g.Key), Count: g.Count()))
                    .ToList();
                combinedCounts.AddRange(appLoggerCounts);
            }

            var topCounts = combinedCounts
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            if (!topCounts.Any())
            {
                LoggerChartCountText.Text = "(No data)";
                return;
            }

            // Store data for drill-down
            _loggerData = topCounts.Select(x => (x.Name, x.Count)).ToList();

            LoggerChartCountText.Text = $"({topCounts.Sum(x => x.Count):N0} errors) - Click bar to filter";

            // Create OxyPlot model
            var plotModel = new PlotModel
            {
                Title = "",
                Background = OxyColors.Transparent,
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 60)
            };

            // Create bar series
            var barSeries = new BarSeries
            {
                FillColor = OxyColor.FromRgb(231, 76, 60), // Danger color
                StrokeThickness = 0,
                LabelPlacement = LabelPlacement.Inside,
                LabelFormatString = "{0}"
            };

            // Add data (reversed for display)
            var reversedData = topCounts.AsEnumerable().Reverse().ToList();
            foreach (var item in reversedData)
            {
                barSeries.Items.Add(new BarItem { Value = item.Count });
            }

            // Configure axes
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColor.FromRgb(200, 200, 200),
                TicklineColor = OxyColors.Transparent,
                FontSize = 11
            };

            foreach (var item in reversedData)
            {
                var displayName = item.Name.Length > 25 ? item.Name.Substring(0, 22) + "..." : item.Name;
                categoryAxis.Labels.Add(displayName);
            }

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                TextColor = OxyColor.FromRgb(150, 150, 150),
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255),
                MinorGridlineStyle = LineStyle.None,
                FontSize = 10
            };

            plotModel.Axes.Add(categoryAxis);
            plotModel.Axes.Add(valueAxis);
            plotModel.Series.Add(barSeries);

            // Create PlotView and add to host
            var plotView = new OxyPlot.Wpf.PlotView
            {
                Model = plotModel,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48))
            };

            // Add click handler for drill-down
            plotView.MouseDown += (s, e) =>
            {
                try
                {
                    var position = new ScreenPoint(e.GetPosition(plotView).X, e.GetPosition(plotView).Y);
                    var hitResult = barSeries.GetNearestPoint(position, false);

                    if (hitResult != null)
                    {
                        int index = (int)hitResult.Index;
                        if (index >= 0 && index < reversedData.Count)
                        {
                            var selectedLogger = reversedData[index].Name;
                            ApplyLoggerFilter(selectedLogger);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CHART CLICK] Error: {ex.Message}");
                }
            };

            LoggerBarChartHost.Children.Clear();
            LoggerBarChartHost.Children.Add(plotView);
        }

        private void CreateStatePieChart(List<LogEntry> plcLogs)
        {
            // Get error logs from PLC
            var plcErrors = GetErrorLogs(plcLogs);

            if (!plcErrors.Any())
            {
                StateChartCountText.Text = "(No PLC errors)";
                return;
            }

            // Build state entries using the same logic as StateFailureAnalyzer
            var stateEntries = CalculateStateEntries(plcLogs);

            if (!stateEntries.Any())
            {
                StateChartCountText.Text = "(No state transitions found)";
                return;
            }

            // Map each error to its state based on timestamp
            var errorsByState = new Dictionary<string, int>();

            foreach (var error in plcErrors)
            {
                // Find the state this error occurred in (state that started before error and hasn't ended yet)
                var state = stateEntries
                    .Where(s => s.StartTime <= error.Date &&
                               (s.EndTime == null || error.Date <= s.EndTime.Value))
                    .FirstOrDefault();

                if (state != null && !string.IsNullOrWhiteSpace(state.StateName))
                {
                    string stateName = state.StateName;
                    if (!errorsByState.ContainsKey(stateName))
                        errorsByState[stateName] = 0;
                    errorsByState[stateName]++;
                }
            }

            var stateCounts = errorsByState
                .Select(kvp => new { State = kvp.Key, Count = kvp.Value })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            if (!stateCounts.Any())
            {
                StateChartCountText.Text = "(No errors matched to states)";
                return;
            }

            // Store state data for drill-down
            _stateData = stateCounts.Select(x => (x.State, x.Count)).ToList();

            StateChartCountText.Text = $"({stateCounts.Sum(x => x.Count):N0} errors with state info) - Click slice to filter";

            // Create OxyPlot model
            var plotModel = new PlotModel
            {
                Title = "",
                Background = OxyColors.Transparent
            };

            var pieSeries = new PieSeries
            {
                StrokeThickness = 2.0,
                InsideLabelPosition = 0.5,
                AngleSpan = 360,
                StartAngle = 0,
                InsideLabelColor = OxyColors.White,
                InsideLabelFormat = "{1}: {2:0}",
                FontSize = 11
            };

            // Color palette
            var colors = new[]
            {
                OxyColor.FromRgb(231, 76, 60),   // Red
                OxyColor.FromRgb(241, 196, 15),  // Yellow
                OxyColor.FromRgb(52, 152, 219),  // Blue
                OxyColor.FromRgb(46, 204, 113),  // Green
                OxyColor.FromRgb(155, 89, 182),  // Purple
                OxyColor.FromRgb(230, 126, 34),  // Orange
                OxyColor.FromRgb(149, 165, 166), // Gray
                OxyColor.FromRgb(22, 160, 133),  // Teal
                OxyColor.FromRgb(243, 156, 18),  // Light Orange
                OxyColor.FromRgb(142, 68, 173)   // Dark Purple
            };

            for (int i = 0; i < stateCounts.Count; i++)
            {
                var item = stateCounts[i];
                pieSeries.Slices.Add(new PieSlice(item.State, item.Count)
                {
                    IsExploded = false,
                    Fill = colors[i % colors.Length]
                });
            }

            plotModel.Series.Add(pieSeries);

            // Create PlotView and add to host
            var plotView = new OxyPlot.Wpf.PlotView
            {
                Model = plotModel,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48))
            };

            // Add click handler for drill-down
            plotView.MouseDown += (s, e) =>
            {
                try
                {
                    var position = new ScreenPoint(e.GetPosition(plotView).X, e.GetPosition(plotView).Y);
                    var hitResult = pieSeries.GetNearestPoint(position, false);

                    if (hitResult != null)
                    {
                        int index = (int)hitResult.Index;
                        if (index >= 0 && index < stateCounts.Count)
                        {
                            var selectedState = stateCounts[index].State;
                            ApplyStateFilter(selectedState);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CHART CLICK] Error: {ex.Message}");
                }
            };

            StatePieChartHost.Children.Clear();
            StatePieChartHost.Children.Add(plotView);
        }

        private void CreateErrorTimelineChart(List<LogEntry> errorLogs)
        {
            if (!errorLogs.Any())
            {
                TimelineChartInfo.Text = "(No errors)";
                return;
            }

            var sortedErrors = errorLogs.OrderBy(l => l.Date).ToList();
            var firstTime = sortedErrors.First().Date;
            var lastTime = sortedErrors.Last().Date;
            var totalDuration = lastTime - firstTime;

            // Determine bucket size based on duration
            int bucketCount;
            string timeUnit;
            if (totalDuration.TotalMinutes < 2)
            {
                bucketCount = 60; // 60 buckets for short duration
                timeUnit = "seconds";
            }
            else if (totalDuration.TotalMinutes < 30)
            {
                bucketCount = 100; // More detail for medium duration
                timeUnit = "time";
            }
            else
            {
                bucketCount = 120; // High detail for long duration
                timeUnit = "time";
            }

            var bucketSize = totalDuration.TotalSeconds / bucketCount;
            var bucketSizeDisplay = bucketSize < 60 ? $"{bucketSize:F1}s" : $"{bucketSize / 60:F1}min";

            TimelineChartInfo.Text = $"({sortedErrors.Count} errors over {totalDuration.TotalMinutes:F1} min, resolution: {bucketSizeDisplay})";

            var buckets = new int[bucketCount];

            foreach (var log in sortedErrors)
            {
                var elapsedSeconds = (log.Date - firstTime).TotalSeconds;
                int bucketIndex = (int)(elapsedSeconds / bucketSize);
                if (bucketIndex >= bucketCount) bucketIndex = bucketCount - 1;
                if (bucketIndex < 0) bucketIndex = 0;
                buckets[bucketIndex]++;
            }

            // Create OxyPlot model
            var plotModel = new PlotModel
            {
                Title = "",
                Background = OxyColors.Transparent,
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 60)
            };

            var lineSeries = new LineSeries
            {
                Color = OxyColor.FromRgb(52, 152, 219), // Primary color
                StrokeThickness = 2,
                MarkerType = OxyPlot.MarkerType.Circle,
                MarkerSize = 3,
                MarkerFill = OxyColor.FromRgb(52, 152, 219)
            };

            for (int i = 0; i < bucketCount; i++)
            {
                var bucketTime = firstTime.AddSeconds(i * bucketSize);
                lineSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(bucketTime), buckets[i]));
            }

            var dateAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                TextColor = OxyColor.FromRgb(150, 150, 150),
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255),
                FontSize = 10
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Error Count",
                TitleColor = OxyColor.FromRgb(200, 200, 200),
                TextColor = OxyColor.FromRgb(150, 150, 150),
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255),
                MinorGridlineStyle = LineStyle.None,
                FontSize = 10
            };

            plotModel.Axes.Add(dateAxis);
            plotModel.Axes.Add(valueAxis);
            plotModel.Series.Add(lineSeries);

            // Create PlotView and add to host
            var plotView = new OxyPlot.Wpf.PlotView
            {
                Model = plotModel,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48))
            };

            ErrorTimelineChartHost.Children.Clear();
            ErrorTimelineChartHost.Children.Add(plotView);
        }

        // Calculate state entries using same logic as StateFailureAnalyzer
        private List<StateEntry> CalculateStateEntries(List<LogEntry> plcLogs)
        {
            var statesList = new List<StateEntry>();
            var sortedLogs = plcLogs.OrderBy(l => l.Date).ToList();

            // Find PlcMngr transitions - use "Manager" thread (as in StateFailureAnalyzer)
            var transitionLogs = sortedLogs
                .Where(l => l.ThreadName != null &&
                           l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                           l.Message != null &&
                           l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                           l.Message.Contains("->"))
                .ToList();

            if (transitionLogs.Count == 0) return statesList;

            DateTime logEndLimit = sortedLogs.Last().Date;

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