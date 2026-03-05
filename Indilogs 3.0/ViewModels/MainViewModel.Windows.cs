using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // ── Window management methods ──

        private void OpenStatesWindow(object obj)
        {
            if (IsAnalysisRunning)
            {
                _dialogService.ShowInfo("Still analyzing data in background...\nPlease wait until the process finishes.", "Processing");
                return;
            }

            if (SessionVM.SelectedSession == null) { _dialogService.ShowInfo("No logs loaded."); return; }

            if (SessionVM.SelectedSession.CachedStates != null && SessionVM.SelectedSession.CachedStates.Count > 0)
            {
                if (_statesWindow != null && _statesWindow.IsVisible) { _windowManager.ActivateWindow(_statesWindow); return; }

                _statesWindow = _viewFactory.Create<StatesWindow>(SessionVM.SelectedSession.CachedStates, this);
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                _windowManager.OpenWindow(_statesWindow);
            }
            else
            {
                _dialogService.ShowInfo("No states detected in this session.");
            }
        }

        private void RunAnalysis(object obj)
        {
            if (SessionVM.SelectedSession == null)
            {
                _dialogService.ShowInfo("No logs loaded.");
                return;
            }

            if (IsAnalysisRunning)
            {
                _dialogService.ShowInfo("Analysis is still running in the background.\nPlease wait a moment and try again.", "Processing");
                return;
            }

            // S4-5: go directly to Statistics (no Failures analysis available)
            if (HasBinaryAppLogs)
            {
                ShowStatisticsAnalysis();
                return;
            }

            // S6: Show analysis menu with Failures / Statistics choices
            var menuWindow = _viewFactory.Create<Views.AnalysisMenuWindow>();
            menuWindow.Owner = _windowOwner.GetOwner();

            if (menuWindow.ShowDialog() == true)
            {
                switch (menuWindow.SelectedChoice)
                {
                    case Views.AnalysisMenuWindow.AnalysisChoice.Failures:
                        ShowFailuresAnalysis();
                        break;
                    case Views.AnalysisMenuWindow.AnalysisChoice.Statistics:
                        ShowStatisticsAnalysis();
                        break;
                }
            }
        }

        private void ShowFailuresAnalysis()
        {
            if (_analysisWindow != null && _analysisWindow.IsVisible)
            {
                _analysisWindow.Activate();
                return;
            }

            if (SessionVM.SelectedSession.CachedAnalysis != null && SessionVM.SelectedSession.CachedAnalysis.Any())
            {
                OpenAnalysisWindow(SessionVM.SelectedSession.CachedAnalysis);
            }
            else
            {
                _dialogService.ShowInfo("Great news! No critical state failures were detected in this session.", "Analysis Result");
            }
        }

        private void ShowStatisticsAnalysis()
        {
            var plcLogs = SessionVM?.AllLogsCache;
            var appLogs = SessionVM?.AllAppLogsCache;

            // Check if there is any data to display
            bool hasPlc = plcLogs != null && plcLogs.Any();
            bool hasApp = appLogs != null && appLogs.Any();

            if (!hasPlc && !hasApp)
            {
                _dialogService.ShowWarning("No logs available for analysis.", "No Data");
                return;
            }

            // Create the window with both parameters and a callback for filtering
            var statsWindow = _viewFactory.Create<Views.StatsWindow>(plcLogs, appLogs, (Action<string, string>)ApplyChartDrillDownFilter, (Action<LogEntry>)NavigateToLogFromStats, IsDarkMode, HasBinaryAppLogs);
            statsWindow.Title = "Log Statistics Dashboard";
            _windowManager.OpenWindow(statsWindow);
        }

        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = _viewFactory.Create<AnalysisReportWindow>(results, (Action<LogEntry>)(log => JumpToLog(log)));
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            _windowManager.OpenWindow(_analysisWindow);
        }

        private void OpenSettingsWindow(object obj)
        {
            var win = _viewFactory.Create<SettingsWindow>();
            win.DataContext = this;
            win.WindowStartupLocation = WindowStartupLocation.Manual;

            if (obj is FrameworkElement button)
            {
                // Get DPI scale factor for accurate positioning
                var source = PresentationSource.FromVisual(button);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Position below the button, aligned to its left edge
                Point buttonPosition = button.PointToScreen(new Point(0, 0));
                double buttonHeight = button.ActualHeight * dpiScale;

                // Get screen bounds to ensure window stays on screen
                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)buttonPosition.X, (int)buttonPosition.Y));
                var workingArea = screen.WorkingArea;

                // Position below the button
                double left = buttonPosition.X / dpiScale;
                double top = (buttonPosition.Y + buttonHeight + 5) / dpiScale;

                // Ensure window doesn't go off the right edge
                if (left + win.Width > workingArea.Right / dpiScale)
                {
                    left = workingArea.Right / dpiScale - win.Width - 10;
                }

                // Ensure window doesn't go off the bottom - if so, show above button
                double estimatedHeight = 350;
                if (top + estimatedHeight > workingArea.Bottom / dpiScale)
                {
                    top = buttonPosition.Y / dpiScale - estimatedHeight - 5;
                }

                win.Left = left;
                win.Top = top;

                // Show the window directly instead of using WindowManager
                // to preserve our manual positioning
                win.Show();
                win.Activate();
                win.Focus();
            }
            else
            {
                // Fallback: use WindowManager for centering
                _windowManager.OpenWindow(win);
            }
        }

        private void OpenFontsWindow(object obj) { var w = _viewFactory.Create<FontsWindow>(); w.DataContext = this; _windowManager.ShowDialog(w); }

        private void OpenMarkedLogsWindow(object obj) => CaseVM?.OpenMarkedWindowCommand.Execute(obj);

        private void OpenGlobalGrepWindow()
        {
            // Create an empty collection if no sessions are loaded, to allow the window to open
            var sessions = SessionVM?.LoadedSessions ?? new ObservableCollection<LogSessionData>();

            var viewModel = new GlobalGrepViewModel(
                sessions,
                Bootstrapper.Resolve<IGlobalGrepService>(),
                Bootstrapper.Resolve<ISearchLocationService>(),
                Bootstrapper.Resolve<ISearchConfigService>(),
                Bootstrapper.Resolve<ISearchSchedulerService>(),
                Bootstrapper.Resolve<IWindowsTaskSchedulerService>(),
                Bootstrapper.Resolve<IEmailNotificationService>(),
                _dialogService, _viewFactory, _dispatcher, _windowOwner);

            var window = _viewFactory.Create<GlobalGrepWindow>(viewModel, (Action<GrepResult>)NavigateToGrepResult, (Action<List<(string FilePath, string SessionName)>>)LoadMultipleFiles);
            _windowManager.OpenWindow(window);
        }

        private void OpenComparisonWindow()
        {
            var comparisonWindow = _windowManager.GetOrCreate<Views.ComparisonWindow>(
                () => _viewFactory.Create<Views.ComparisonWindow>(new LogComparisonViewModel(
                    SessionVM.AllLogsCache,
                    SessionVM.AllAppLogsCache,
                    this,
                    _windowManager
                )),
                _windowOwner.GetOwner()
            );
        }

        private async Task OpenStripeAnalysisWindow()
        {
            try
            {
            var logs = FilterVM?.AppDevLogsFiltered?.ToList();

            if (logs == null || !logs.Any())
            {
                _dialogService.ShowInfo(
                    "No APP logs loaded.\n\nPlease load a session with APP logs first, or switch to the APP tab.",
                    "Stripe Analysis");
                return;
            }

            // Quick pre-check: do we have any stripe data?
            bool hasStripeData = logs.Any(l =>
                (!string.IsNullOrEmpty(l.Data) && l.Data.Contains("stripeDescriptor")) ||
                (!string.IsNullOrEmpty(l.Message) && l.Message.Contains("stripeDescriptor")));

            if (!hasStripeData)
            {
                _dialogService.ShowInfo(
                    "No stripe data found in APP logs.\n\n" +
                    "This feature requires logs containing stripeDescriptor JSON data.",
                    "Stripe Analysis");
                return;
            }

            var window = _viewFactory.Create<StripeAnalysisWindow>();
            _windowManager.OpenWindow(window);

            // Load data asynchronously after window is shown
            await Task.Run(() => { }).ContinueWith(_ =>
            {
                _ = window.LoadFromLogs(logs);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex) { AppLogger.Error("OpenStripeAnalysisWindow failed", ex); }
        }

        private void OpenSnakeGame(object obj)
        {
            var snakeWindow = _viewFactory.Create<IndiLogs_3._0.Views.SnakeWindow>();
            _windowManager.ShowDialog(snakeWindow);
        }

        private void OpenIndigoInvaders(object obj)
        {
            var invadersWindow = _viewFactory.Create<IndiLogs_3._0.Views.IndigoInvadersWindow>();
            invadersWindow.Owner = _windowOwner.GetOwner();
            invadersWindow.ShowDialog();
        }

        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log)
            {
                _windowManager.OpenWindow(_viewFactory.Create<LogDetailsWindow>(log));
            }
        }

        /// <summary>
        /// Opens a URL in the default browser with scheme validation.
        /// Shared utility for opening URLs in the default browser.
        /// </summary>
        internal static void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "mailto"))
                {
                    AppLogger.Warn($"OpenUrl blocked unsafe or invalid URI: '{url}'");
                    return;
                }
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { AppLogger.Error($"OpenUrl failed for '{url}'", ex); }
        }

        private void OpenOutlook(object obj) { try { Process.Start(new ProcessStartInfo("outlook.exe", "/c ipm.note") { UseShellExecute = true }); } catch (Exception ex) { AppLogger.Warn($"Outlook launch failed: {ex.Message}"); OpenUrl("mailto:"); } }

        /// <summary>
        /// Opens Kibana in the default browser using the machine name from the Readme file.
        /// </summary>
        private void OpenKibana(object obj)
        {
            string machineName = string.Empty;

            // Extract machine name from Readme content (line: "Machine name: MANUAL-FP-4")
            if (!string.IsNullOrEmpty(PressConfig))
            {
                foreach (var line in PressConfig.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("Machine name:", StringComparison.OrdinalIgnoreCase))
                    {
                        machineName = line.Substring("Machine name:".Length).Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(machineName))
            {
                OpenUrl("http://localhost/");
                return;
            }

            string nextParam = Uri.EscapeDataString($"/{machineName}/app/home");
            string url = $"http://localhost/{machineName}/login?next={nextParam}#/";
            OpenUrl(url);
        }
    }
}
