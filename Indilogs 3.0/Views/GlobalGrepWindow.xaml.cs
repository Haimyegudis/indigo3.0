using System;
using System.IO;
using System.Windows;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    public partial class GlobalGrepWindow : Window
    {
        private readonly GlobalGrepViewModel _viewModel;
        private readonly Action<GrepResult> _navigationCallback;
        private readonly Action<System.Collections.Generic.List<(string FilePath, string SessionName)>> _openAllFilesCallback;
        private bool _closingForScheduledRun;

        public GlobalGrepWindow(GlobalGrepViewModel viewModel, Action<GrepResult> navigationCallback, Action<System.Collections.Generic.List<(string FilePath, string SessionName)>> openAllFilesCallback = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _navigationCallback = navigationCallback;
            _openAllFilesCallback = openAllFilesCallback;
            DataContext = _viewModel;

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedResult))
                    JumpButton.IsEnabled = _viewModel.SelectedResult != null;
            };
            JumpButton.IsEnabled = false;

            // Subscribe to scheduled run events
            _viewModel.RequestCloseForScheduledRun += OnRequestCloseForScheduledRun;
            _viewModel.ScheduledRunCompleted += OnScheduledRunCompleted;
        }

        private void OnRequestCloseForScheduledRun(string scheduleName)
        {
            _closingForScheduledRun = true;
            Hide();
            AppLogger.Info($"[Grep] Window hidden — running scheduled search '{scheduleName}' in background");
        }

        private void OnScheduledRunCompleted(string scheduleName, string reportPath)
        {
            _closingForScheduledRun = false;

            // Reopen the window
            Show();
            Activate();

            _viewModel.StatusMessage = $"Schedule '{scheduleName}' completed: {_viewModel.SelectedSchedule?.LastRunStatus ?? "done"}";

            // Open the HTML report in the browser
            if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = reportPath,
                        UseShellExecute = true
                    });
                    AppLogger.Info($"[Grep] Opened report: {reportPath}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Grep] Failed to open report: {ex.Message}");
                    MessageBox.Show($"Search complete but could not open report:\n{reportPath}\n\n{ex.Message}",
                        "Report", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (string.IsNullOrEmpty(reportPath))
            {
                MessageBox.Show($"Scheduled search '{scheduleName}' completed but no report was generated.\nCheck that an output directory is configured.",
                    "Schedule Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => JumpToSelectedLog();
        private void JumpToLog_Click(object sender, RoutedEventArgs e) => JumpToSelectedLog();
        private void OpenAllFiles_Click(object sender, RoutedEventArgs e) => OpenAllFiles();

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "HOW TO USE GLOBAL LOG SEARCH\n" +
                "════════════════════════════════════\n\n" +

                "1. SEARCH LOCATIONS\n" +
                "Add machines or simulators you want to search.\n" +
                "Click '+ Add Location' and enter:\n" +
                "  - A friendly name (e.g. \"Simulator 1\")\n" +
                "  - The IP address or hostname\n" +
                "  - The network path to the log folder (UNC or mapped drive)\n" +
                "Use 'Test Connection' to verify the machine is reachable.\n" +
                "Check/uncheck locations to include or exclude them from search.\n\n" +

                "2. SEARCH QUERY\n" +
                "Choose which field to search in (Any, Message, Method, Thread, etc.)\n" +
                "Then type text to search for. Supports:\n" +
                "  - Simple text: error timeout\n" +
                "  - Boolean operators: error AND timeout\n" +
                "  - Quoted phrases: \"connection lost\"\n" +
                "  - Regular expressions (enable the checkbox): .*error.*\\d{3}\n\n" +

                "3. ADVANCED CONDITIONS (optional)\n" +
                "Search specific log fields with precise matching:\n" +
                "  - Pick a field (Message, Logger, Thread, etc.)\n" +
                "  - Pick an operator (Contains, Equals, StartsWith, Regex)\n" +
                "  - Type a value to match\n" +
                "  - Check 'Exclude' to invert (find lines that do NOT match)\n" +
                "Multiple conditions in a group can be combined with AND/OR/NOR.\n" +
                "You can add multiple groups for complex searches.\n\n" +

                "4. FILE DATE RANGE (optional)\n" +
                "If a folder has many log files, use this to only scan\n" +
                "files from a specific date range (speeds up the search).\n" +
                "The date is extracted from the filename or file modification date.\n\n" +
                "The 'Result timestamp' filter further narrows results to only show\n" +
                "log entries whose internal timestamp falls within the given range.\n\n" +

                "5. SEARCH PROFILES\n" +
                "Save your current settings (locations, query, conditions) as a\n" +
                "reusable profile. Load it later to repeat the same search.\n\n" +

                "6. SCHEDULED SEARCHES\n" +
                "Set up automatic searches that run at specific times.\n" +
                "Results are saved to a folder as CSV and JSON files.\n\n" +

                "RESULTS\n" +
                "Double-click a result to jump to that log entry in the main window.\n" +
                "Click a file name to open its location in File Explorer.\n" +
                "The 'Matched In' column shows which field matched your search.\n\n" +
                "EXPORT\n" +
                "  - Export CSV / JSON: save raw results data.\n" +
                "  - Export Report: generate an HTML report with search parameters,\n" +
                "    summary statistics, and a full results table. Opens in browser.",
                "Global Log Search — Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void JumpToSelectedLog()
        {
            if (_viewModel.SelectedResult == null) return;
            try
            {
                _navigationCallback?.Invoke(_viewModel.SelectedResult);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Log navigation failed", ex);
                MessageBox.Show($"Navigation Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FileLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock tb && tb.DataContext is GrepResult result)
            {
                if (!string.IsNullOrWhiteSpace(result.FilePath))
                {
                    try
                    {
                        if (File.Exists(result.FilePath))
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{result.FilePath}\"");
                        else if (Directory.Exists(Path.GetDirectoryName(result.FilePath)))
                            System.Diagnostics.Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(result.FilePath)}\"");
                        else
                            MessageBox.Show($"Path not found: {result.FilePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Open file location failed", ex);
                    }
                }
            }
        }

        private void OpenAllFiles()
        {
            var uniqueFiles = _viewModel.GetUniqueFiles();
            if (uniqueFiles == null || uniqueFiles.Count == 0)
            {
                MessageBox.Show("No files to open.", "Open All Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                _openAllFilesCallback?.Invoke(uniqueFiles);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Opening multiple files failed", ex);
                MessageBox.Show($"Error opening files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
