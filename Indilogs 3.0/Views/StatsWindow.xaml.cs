using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Views
{
    public partial class StatsWindow : Window
    {
        private readonly List<LogEntry> _logs;
        private List<ErrorStat> _errorStats;
        private List<LoadStat> _threadStats;
        private List<LoadStat> _loggerStats;
        private List<GapInfo> _gaps;

        public StatsWindow(IEnumerable<LogEntry> logs)
        {
            InitializeComponent();
            _logs = logs?.ToList() ?? new List<LogEntry>();

            System.Diagnostics.Debug.WriteLine($"[STATS] StatsWindow initialized with {_logs.Count} logs");

            Loaded += (s, e) => CalculateStatistics();
        }

        private void CalculateStatistics()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Starting statistics calculation...");

            if (_logs == null || !_logs.Any())
            {
                SummaryText.Text = "No logs available for analysis.";
                System.Diagnostics.Debug.WriteLine("[STATS] No logs available");
                return;
            }

            var orderedLogs = _logs.OrderBy(l => l.Date).ToList();
            var timeSpan = orderedLogs.Last().Date - orderedLogs.First().Date;

            SummaryText.Text = $"Analyzed {_logs.Count:N0} log entries spanning {timeSpan.TotalMinutes:F1} minutes ({orderedLogs.First().Date:HH:mm:ss} - {orderedLogs.Last().Date:HH:mm:ss})";

            // Calculate all statistics
            CalculateErrorHistogram();
            CalculateThreadDistribution();
            CalculateLoggerDistribution();
            CalculateGapAnalysis(orderedLogs);

            System.Diagnostics.Debug.WriteLine("[STATS] Statistics calculation completed");
        }

        private void CalculateErrorHistogram()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Calculating error histogram...");

            var errors = _logs.Where(l =>
                string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "Fatal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Level, "FATAL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[STATS] Found {errors.Count} error logs");

            if (!errors.Any())
            {
                ErrorCountText.Text = "(No errors found)";
                _errorStats = new List<ErrorStat>();
                ErrorHistogram.ItemsSource = _errorStats;
                return;
            }

            // Group by message (truncate for grouping)
            var grouped = errors
                .GroupBy(e => TruncateMessage(e.Message, 100))
                .Select(g => new { Message = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToList();

            int maxCount = grouped.Max(g => g.Count);

            _errorStats = grouped.Select(g => new ErrorStat
            {
                Message = g.Message,
                Count = g.Count,
                BarWidth = maxCount > 0 ? (double)g.Count / maxCount * 300 : 0
            }).ToList();

            ErrorCountText.Text = $"(Total: {errors.Count:N0} errors)";
            ErrorHistogram.ItemsSource = _errorStats;

            System.Diagnostics.Debug.WriteLine($"[STATS] Error histogram: {_errorStats.Count} unique error types");
        }

        private void CalculateThreadDistribution()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Calculating thread distribution...");

            var grouped = _logs
                .Where(l => !string.IsNullOrEmpty(l.ThreadName))
                .GroupBy(l => l.ThreadName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToList();

            if (!grouped.Any())
            {
                ThreadCountText.Text = "(No thread data)";
                _threadStats = new List<LoadStat>();
                ThreadHistogram.ItemsSource = _threadStats;
                return;
            }

            int maxCount = grouped.Max(g => g.Count);
            int totalLogs = _logs.Count;

            _threadStats = grouped.Select(g => new LoadStat
            {
                Name = g.Name,
                Count = g.Count,
                Percentage = (double)g.Count / totalLogs * 100,
                DisplayText = $"{g.Count:N0} ({(double)g.Count / totalLogs * 100:F1}%)",
                BarWidth = maxCount > 0 ? (double)g.Count / maxCount * 200 : 0
            }).ToList();

            ThreadCountText.Text = $"(Top 10 of {_logs.Select(l => l.ThreadName).Distinct().Count()} threads)";
            ThreadHistogram.ItemsSource = _threadStats;

            System.Diagnostics.Debug.WriteLine($"[STATS] Thread distribution: {_threadStats.Count} threads shown");
        }

        private void CalculateLoggerDistribution()
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Calculating logger distribution...");

            var grouped = _logs
                .Where(l => !string.IsNullOrEmpty(l.Logger))
                .GroupBy(l => l.Logger)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(15)
                .ToList();

            if (!grouped.Any())
            {
                LoggerCountText.Text = "(No logger data)";
                _loggerStats = new List<LoadStat>();
                LoggerHistogram.ItemsSource = _loggerStats;
                return;
            }

            int totalLogs = _logs.Count;

            _loggerStats = grouped.Select(g => new LoadStat
            {
                Name = GetShortLoggerName(g.Name),
                FullName = g.Name,
                Count = g.Count,
                Percentage = (double)g.Count / totalLogs * 100,
                DisplayText = $"{g.Count:N0} ({(double)g.Count / totalLogs * 100:F1}%)"
            }).ToList();

            LoggerCountText.Text = $"(Top 15 of {_logs.Select(l => l.Logger).Where(l => !string.IsNullOrEmpty(l)).Distinct().Count()} loggers)";
            LoggerHistogram.ItemsSource = _loggerStats;

            System.Diagnostics.Debug.WriteLine($"[STATS] Logger distribution: {_loggerStats.Count} loggers shown");
        }

        private void CalculateGapAnalysis(List<LogEntry> orderedLogs)
        {
            System.Diagnostics.Debug.WriteLine("[STATS] Calculating gap analysis...");

            _gaps = new List<GapInfo>();
            const double gapThresholdSeconds = 2.0;

            for (int i = 1; i < orderedLogs.Count; i++)
            {
                var gap = orderedLogs[i].Date - orderedLogs[i - 1].Date;
                if (gap.TotalSeconds >= gapThresholdSeconds)
                {
                    _gaps.Add(new GapInfo
                    {
                        Index = _gaps.Count + 1,
                        StartTime = orderedLogs[i - 1].Date,
                        EndTime = orderedLogs[i].Date,
                        Duration = gap,
                        DurationText = FormatDuration(gap),
                        LastMessageBeforeGap = TruncateMessage(orderedLogs[i - 1].Message, 100),
                        LastLogBeforeGap = orderedLogs[i - 1]
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"[STATS] Found {_gaps.Count} gaps >= {gapThresholdSeconds}s");

            if (_gaps.Any())
            {
                GapSummaryText.Text = $"Found {_gaps.Count} time gap(s) of 2+ seconds. Total gap time: {FormatDuration(TimeSpan.FromSeconds(_gaps.Sum(g => g.Duration.TotalSeconds)))}";
                GapDataGrid.ItemsSource = _gaps;
                GapDataGrid.Visibility = Visibility.Visible;
                NoGapsMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                GapSummaryText.Text = "No significant time gaps detected.";
                GapDataGrid.Visibility = Visibility.Collapsed;
                NoGapsMessage.Visibility = Visibility.Visible;
            }
        }

        private string TruncateMessage(string message, int maxLength)
        {
            if (string.IsNullOrEmpty(message)) return "(empty)";
            if (message.Length <= maxLength) return message;
            return message.Substring(0, maxLength) + "...";
        }

        private string GetShortLoggerName(string logger)
        {
            if (string.IsNullOrEmpty(logger)) return logger;
            var parts = logger.Split('.');
            if (parts.Length <= 2) return logger;
            return string.Join(".", parts.Skip(parts.Length - 2));
        }

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1)
                return $"{ts.TotalMinutes:F1} min";
            return $"{ts.TotalSeconds:F1} sec";
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== LOG STATISTICS REPORT ===");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Total Logs: {_logs.Count:N0}");
                sb.AppendLine();

                // Error Histogram
                sb.AppendLine("--- TOP 10 ERRORS ---");
                if (_errorStats != null && _errorStats.Any())
                {
                    foreach (var e2 in _errorStats)
                    {
                        sb.AppendLine($"  [{e2.Count}] {e2.Message}");
                    }
                }
                else
                {
                    sb.AppendLine("  No errors found.");
                }
                sb.AppendLine();

                // Thread Distribution
                sb.AppendLine("--- THREAD DISTRIBUTION ---");
                if (_threadStats != null && _threadStats.Any())
                {
                    foreach (var t in _threadStats)
                    {
                        sb.AppendLine($"  {t.Name}: {t.DisplayText}");
                    }
                }
                sb.AppendLine();

                // Logger Distribution
                sb.AppendLine("--- LOGGER DISTRIBUTION ---");
                if (_loggerStats != null && _loggerStats.Any())
                {
                    foreach (var l in _loggerStats)
                    {
                        sb.AppendLine($"  {l.FullName ?? l.Name}: {l.DisplayText}");
                    }
                }
                sb.AppendLine();

                // Gap Analysis
                sb.AppendLine("--- GAP ANALYSIS (>= 2 seconds) ---");
                if (_gaps != null && _gaps.Any())
                {
                    sb.AppendLine($"  Total gaps: {_gaps.Count}");
                    sb.AppendLine();
                    foreach (var g in _gaps)
                    {
                        sb.AppendLine($"  Gap #{g.Index}:");
                        sb.AppendLine($"    Start: {g.StartTime:yyyy-MM-dd HH:mm:ss.fff}");
                        sb.AppendLine($"    End: {g.EndTime:yyyy-MM-dd HH:mm:ss.fff}");
                        sb.AppendLine($"    Duration: {g.DurationText}");
                        sb.AppendLine($"    Last message: {g.LastMessageBeforeGap}");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("  No significant gaps found.");
                }

                // Save to file
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"LogStats_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, sb.ToString());
                    MessageBox.Show($"Report exported to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting report: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    // Helper classes for data binding
    public class ErrorStat
    {
        public string Message { get; set; }
        public int Count { get; set; }
        public double BarWidth { get; set; }
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
}
