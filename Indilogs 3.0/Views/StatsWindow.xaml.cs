using DocumentFormat.OpenXml.Spreadsheet;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class StatsWindow : Window
    {
        private readonly List<LogEntry> _plcLogs;
        private readonly List<LogEntry> _appLogs;

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

        // ??? ??: ????? ???? ??? ??? ?????? ??????. ?? ????? ?? ?????? ?-StatsWindow ???? ????? ????.
        public StatsWindow(IEnumerable<LogEntry> plcLogs, IEnumerable<LogEntry> appLogs)
        {
            InitializeComponent();
            _plcLogs = plcLogs?.ToList() ?? new List<LogEntry>();
            _appLogs = appLogs?.ToList() ?? new List<LogEntry>();

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
        public string Message { get; set; }   // ????? ?????? (???? ????????? ??????)
        public int Count { get; set; }
        public string DisplayText { get; set; }
        public double BarWidth { get; set; }
    }
    // Helper models (same as before but ensured they have necessary props)
   
}