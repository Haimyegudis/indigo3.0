using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Handles external tools (JIRA, Kibana, Outlook), analysis/export windows,
    /// theme management, settings, fonts, and utility windows.
    /// Extracted from MainViewModel to reduce its responsibility.
    /// </summary>
    public class ToolsViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly CsvExportService _csvService;
        private readonly IWindowManager _windowManager;

        // Windows Instances
        private StatesWindow _statesWindow;
        private AnalysisReportWindow _analysisWindow;
        private ExportConfigurationWindow _exportConfigWindow;

        // --- Commands ---
        public ICommand OpenJiraCommand { get; }
        public ICommand OpenKibanaCommand { get; }
        public ICommand OpenOutlookCommand { get; }
        public ICommand ExportParsedDataCommand { get; }
        public ICommand RunAnalysisCommand { get; }
        public ICommand OpenStatesWindowCommand { get; }
        public ICommand OpenGlobalGrepCommand { get; }
        public ICommand OpenStripeAnalysisCommand { get; }
        public ICommand OpenComparisonCommand { get; }
        public ICommand OpenSnakeGameCommand { get; }
        public ICommand OpenIndigoInvadersCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ToggleBoldCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenFontsWindowCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ViewLogDetailsCommand { get; }

        public ToolsViewModel(MainViewModel parent, CsvExportService csvService)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _csvService = csvService ?? throw new ArgumentNullException(nameof(csvService));
            _windowManager = Bootstrapper.Resolve<IWindowManager>();

            // External tools
            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);

            // Windows
            ExportParsedDataCommand = new RelayCommand(ExportParsedData);
            RunAnalysisCommand = new RelayCommand(RunAnalysis);
            OpenStatesWindowCommand = new RelayCommand(OpenStatesWindow);
            OpenGlobalGrepCommand = new RelayCommand(o => OpenGlobalGrepWindow());
            OpenStripeAnalysisCommand = new RelayCommand(o => OpenStripeAnalysisWindow());
            OpenComparisonCommand = new RelayCommand(o => OpenComparisonWindow(),
                o => _parent.SessionVM?.AllLogsCache?.Count > 0 || _parent.SessionVM?.AllAppLogsCache?.Count > 0);
            OpenSnakeGameCommand = new RelayCommand(OpenSnakeGame);
            OpenIndigoInvadersCommand = new RelayCommand(OpenIndigoInvaders);

            // Theme & UI
            ToggleThemeCommand = new RelayCommand(o => _parent.IsDarkMode = !_parent.IsDarkMode);
            ToggleBoldCommand = new RelayCommand(o => _parent.IsBold = !_parent.IsBold);
            OpenSettingsCommand = new RelayCommand(OpenSettingsWindow);
            OpenHelpCommand = new RelayCommand(o => _windowManager.OpenWindow(new HelpWindow()));
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            ViewLogDetailsCommand = new RelayCommand(ViewLogDetails);

            // Zoom
            ZoomInCommand = new RelayCommand(o =>
            {
                if (_parent.SelectedTabIndex == 4) _parent.ScreenshotZoom = Math.Min(5000, _parent.ScreenshotZoom + 100);
                else _parent.GridFontSize = Math.Min(30, _parent.GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (_parent.SelectedTabIndex == 4) _parent.ScreenshotZoom = Math.Max(100, _parent.ScreenshotZoom - 100);
                else _parent.GridFontSize = Math.Max(8, _parent.GridFontSize - 1);
            });
        }

        #region External Tools

        public void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"[ToolsVM] OpenUrl failed: {ex.Message}"); }
        }

        private void OpenOutlook(object obj)
        {
            try { Process.Start("outlook.exe", "/c ipm.note"); }
            catch { OpenUrl("mailto:"); }
        }

        private void OpenKibana(object obj) { /* Not yet implemented */ }

        #endregion

        #region Export & Analysis Windows

        private void ExportParsedData(object obj)
        {
            if (_parent.SelectedSession == null || _parent.SelectedSession.Logs == null || !_parent.SelectedSession.Logs.Any())
            {
                MessageBox.Show("No logs loaded.", "Info");
                return;
            }

            if (_exportConfigWindow != null && _exportConfigWindow.IsLoaded)
            {
                _windowManager.ActivateWindow(_exportConfigWindow);
                return;
            }

            _exportConfigWindow = new ExportConfigurationWindow();
            var viewModel = new ExportConfigurationViewModel(_parent.SelectedSession, _csvService);
            _exportConfigWindow.DataContext = viewModel;
            _exportConfigWindow.Closed += (s, e) => _exportConfigWindow = null;
            _windowManager.OpenWindow(_exportConfigWindow);
        }

        private void RunAnalysis(object obj)
        {
            if (_parent.SelectedSession == null) { MessageBox.Show("No logs loaded."); return; }
            if (_parent.IsAnalysisRunning) { MessageBox.Show("Analysis is already running..."); return; }

            var session = _parent.SelectedSession;
            if (session.CachedAnalysis != null && session.CachedAnalysis.Count > 0)
            {
                OpenAnalysisWindow(session.CachedAnalysis);
            }
            else
            {
                _parent.StatusMessage = "Starting analysis...";
                _parent.StartBackgroundAnalysis(session);
            }
        }

        public void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results);
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            _windowManager.OpenWindow(_analysisWindow);
        }

        private void OpenStatesWindow(object obj)
        {
            if (_parent.IsAnalysisRunning)
            {
                MessageBox.Show("Still analyzing data in background...\nPlease wait until the process finishes.",
                                "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_parent.SelectedSession == null) { MessageBox.Show("No logs loaded."); return; }

            if (_parent.SelectedSession.CachedStates != null && _parent.SelectedSession.CachedStates.Count > 0)
            {
                if (_statesWindow != null && _statesWindow.IsVisible) { _windowManager.ActivateWindow(_statesWindow); return; }

                _statesWindow = new StatesWindow(_parent.SelectedSession.CachedStates, _parent);
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                _windowManager.OpenWindow(_statesWindow);
            }
            else
            {
                MessageBox.Show("No states detected in this session.");
            }
        }

        private void OpenGlobalGrepWindow()
        {
            var sessions = _parent.LoadedSessions ?? new ObservableCollection<LogSessionData>();
            var viewModel = new GlobalGrepViewModel(sessions);

            if (!sessions.Any())
                viewModel.SearchMode = GlobalGrepViewModel.SearchModeType.ExternalFiles;

            var window = new GlobalGrepWindow(viewModel, _parent.NavigateToGrepResult, _parent.LoadMultipleFiles);
            _windowManager.OpenWindow(window);
        }

        private void OpenComparisonWindow()
        {
            var comparisonWindow = _windowManager.GetOrCreate<ComparisonWindow>(
                () => new ComparisonWindow(new LogComparisonViewModel(
                    _parent.SessionVM.AllLogsCache,
                    _parent.SessionVM.AllAppLogsCache,
                    _parent
                )),
                Application.Current.MainWindow
            );
        }

        private async void OpenStripeAnalysisWindow()
        {
            var logs = _parent.FilterVM?.AppDevLogsFiltered?.ToList();

            if (logs == null || !logs.Any())
            {
                MessageBox.Show(
                    "No APP logs loaded.\n\nPlease load a session with APP logs first, or switch to the APP tab.",
                    "Stripe Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasStripeData = logs.Any(l =>
                (!string.IsNullOrEmpty(l.Data) && l.Data.Contains("stripeDescriptor")) ||
                (!string.IsNullOrEmpty(l.Message) && l.Message.Contains("stripeDescriptor")));

            if (!hasStripeData)
            {
                MessageBox.Show(
                    "No stripe data found in APP logs.\n\n" +
                    "This feature requires logs containing stripeDescriptor JSON data.",
                    "Stripe Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new StripeAnalysisWindow();
            _windowManager.OpenWindow(window);

            _ = window.LoadFromLogs(logs);
        }

        #endregion

        #region Theme & Settings

        public void ApplyTheme(bool isDark)
        {
            var dict = Application.Current.Resources;
            if (isDark)
            {
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(10, 18, 30)));
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(15, 25, 40)));
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(20, 35, 55)));
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(30, 50, 75)));
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(220, 230, 240)));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(140, 160, 180)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(40, 60, 85)));
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 200, 220)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(245, 0, 87)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Colors.White));
            }
            else
            {
                var lightGradient = new LinearGradientBrush();
                lightGradient.StartPoint = new Point(0, 0);
                lightGradient.EndPoint = new Point(1, 1);
                lightGradient.GradientStops.Add(new GradientStop(Color.FromRgb(240, 242, 245), 0.0));
                lightGradient.GradientStops.Add(new GradientStop(Color.FromRgb(200, 204, 210), 1.0));

                UpdateResource(dict, "BgDark", lightGradient);
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(243, 244, 246)));
                UpdateResource(dict, "BgCard", new SolidColorBrush(Colors.White));
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(230, 230, 230)));
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(31, 41, 55)));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(107, 114, 128)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(209, 213, 219)));
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 120, 215)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(220, 0, 80)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Colors.Black));
            }
        }

        private void UpdateResource(ResourceDictionary dict, string key, object value)
        {
            if (dict.Contains(key)) dict.Remove(key);
            dict.Add(key, value);
        }

        public void UpdateContentFont(string fontName)
        {
            if (!string.IsNullOrEmpty(fontName) && Application.Current != null)
                UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName));
        }

        public void UpdateContentFontWeight(bool isBold)
        {
            if (Application.Current != null)
                UpdateResource(Application.Current.Resources, "ContentFontWeight",
                    isBold ? FontWeights.Bold : FontWeights.Normal);
        }

        private void OpenSettingsWindow(object obj)
        {
            var win = new SettingsWindow { DataContext = _parent };
            win.WindowStartupLocation = WindowStartupLocation.Manual;

            if (obj is FrameworkElement button)
            {
                var source = PresentationSource.FromVisual(button);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                Point buttonPosition = button.PointToScreen(new Point(0, 0));
                double buttonHeight = button.ActualHeight * dpiScale;

                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)buttonPosition.X, (int)buttonPosition.Y));
                var workingArea = screen.WorkingArea;

                double left = buttonPosition.X / dpiScale;
                double top = (buttonPosition.Y + buttonHeight + 5) / dpiScale;

                if (left + win.Width > workingArea.Right / dpiScale)
                    left = workingArea.Right / dpiScale - win.Width - 10;

                double estimatedHeight = 350;
                if (top + estimatedHeight > workingArea.Bottom / dpiScale)
                    top = buttonPosition.Y / dpiScale - estimatedHeight - 5;

                win.Left = left;
                win.Top = top;
                win.Show();
                win.Activate();
                win.Focus();
            }
            else
            {
                _windowManager.OpenWindow(win);
            }
        }

        private void OpenFontsWindow(object obj) => _windowManager.ShowDialog(new FontsWindow { DataContext = _parent });

        private void ViewLogDetails(object obj)
        {
            if (obj is LogEntry log)
            {
                var detailsWindow = new LogDetailsWindow(log);
                _windowManager.OpenWindow(detailsWindow);
            }
        }

        #endregion

        #region Fun / Easter Eggs

        private void OpenSnakeGame(object obj)
        {
            var snakeWindow = new SnakeWindow();
            _windowManager.ShowDialog(snakeWindow);
        }

        private void OpenIndigoInvaders(object obj)
        {
            var window = new IndigoInvadersWindow();
            _windowManager.OpenWindow(window);
        }

        #endregion
    }
}
