using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using IndiLogs_3._0.ViewModels.Components;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        // --- Child ViewModels (Composition Pattern) ---
        public LogSessionViewModel SessionVM { get; private set; }
        public FilterSearchViewModel FilterVM { get; private set; }
        public LiveMonitoringViewModel LiveVM { get; private set; }
        public CaseManagementViewModel CaseVM { get; private set; }
        public ConfigExplorerViewModel ConfigVM { get; private set; }
        public VisualTimelineViewModel VisualTimelineVM { get; set; } = new VisualTimelineViewModel();
        public ChartTabViewModel ChartVM { get; private set; }
        public CprAnalysisViewModel CprVM { get; private set; }
        public DifferentLogsViewModel DifferentLogsVM { get; private set; }
        public StepRecorderViewModel StepRecorderVM { get; private set; }

        private bool _isVisualMode;
        public bool IsVisualMode
        {
            get => _isVisualMode;
            set
            {
                _isVisualMode = value;
                OnPropertyChanged();
                if (value) InitializeVisualMode();
            }
        }

        public ICommand BrowseTableCommand { get; }
        public ICommand CopyTableNameCommand { get; }
        public ICommand ToggleAnnotationCommand { get; }
        public ICommand CloseAnnotationCommand { get; }
        public ICommand ToggleAllAnnotationsCommand { get; }
        public ICommand ToggleVisualModeCommand { get; }

        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly ICsvExportService _csvService;
        private readonly IDefaultConfigurationService _defaultConfigService;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        public IDefaultConfigurationService DefaultConfigService => _defaultConfigService;
        public ILogColoringService ColoringService => _coloringService;

        // Windows Instances
        private StatesWindow? _statesWindow;
        private AnalysisReportWindow? _analysisWindow;
        private bool _isAnalysisRunning;
        private ExportConfigurationWindow? _exportConfigWindow = null;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set { _isAnalysisRunning = value; OnPropertyChanged(); }
        }

        // Caches
        private IList<LogEntry>? _allLogsCache;

        // Coloring
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();

        private const int UI_UPDATE_BATCH_SIZE = AppConstants.UiUpdateBatchSize;
        private readonly object _collectionLock = new object();

        // Full-column DataView for EVENTS tab (all CSV columns as-is)
        private System.Data.DataView? _eventsDataView;
        public System.Data.DataView? EventsDataView
        {
            get => _eventsDataView;
            set { _eventsDataView = value; OnPropertyChanged(); }
        }

        public void LoadEventsDataView()
        {
            var session = SessionVM?.SelectedSession;
            if (session == null) return;

            // Events tab: display terminal CSV bytes
            var eventsCsvEntries = session.TerminalCsvBytes?
                .Where(kv => !Path.GetFileName(kv.Key).StartsWith("Io-", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (eventsCsvEntries == null || eventsCsvEntries.Count == 0)
            {
                EventsDataView = null;
                return;
            }

            var dt = new System.Data.DataTable("Events");
            bool headersDone = false;

            foreach (var kv in eventsCsvEntries)
            {
                string csvText = System.Text.Encoding.UTF8.GetString(kv.Value);
                using var reader = new StringReader(csvText);
                string headerLine = reader.ReadLine();
                if (headerLine == null) continue;

                if (!headersDone)
                {
                    var headers = SplitCsvLineHelper(headerLine);
                    foreach (var h in headers)
                        dt.Columns.Add(h.Trim(), typeof(string));
                    headersDone = true;
                }

                string dataLine;
                while ((dataLine = reader.ReadLine()) != null)
                {
                    var values = SplitCsvLineHelper(dataLine);
                    var row = dt.NewRow();
                    for (int i = 0; i < Math.Min(values.Count, dt.Columns.Count); i++)
                        row[i] = values[i].Trim();
                    dt.Rows.Add(row);
                }
            }
            EventsDataView = dt.DefaultView;
        }

        private static List<string> SplitCsvLineHelper(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        /// <summary>Exposes the plugin loader so child VMs can query loaded plugins.</summary>
        public Services.Interfaces.IPluginLoader? GetPluginLoader()
            => _logService?.GetPluginLoader();

        // ── UI properties ──

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        private IReadOnlyList<PluginColumnDef>? _currentPluginColumns;
        public IReadOnlyList<PluginColumnDef>? CurrentPluginColumns
        {
            get => _currentPluginColumns;
            set { _currentPluginColumns = value; OnPropertyChanged(); }
        }

        private string? _setupInfo;
        public string? SetupInfo
        {
            get => _setupInfo;
            set { _setupInfo = value; OnPropertyChanged(); }
        }

        private string? _pressConfig;
        public string? PressConfig
        {
            get => _pressConfig;
            set { _pressConfig = value; OnPropertyChanged(); }
        }

        private string? _versionsInfo;
        public string? VersionsInfo
        {
            get => _versionsInfo;
            set { _versionsInfo = value; OnPropertyChanged(); }
        }

        private LogEntry? _selectedLog;
        public LogEntry? SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedLog)); }
        }

        public bool HasSelectedLog => _selectedLog != null;

        // Log Details panel pin/auto-hide state
        private bool _isLogDetailsPinned = true;
        public bool IsLogDetailsPinned
        {
            get => _isLogDetailsPinned;
            set { _isLogDetailsPinned = value; OnPropertyChanged(); }
        }

        public ICommand ToggleLogDetailsPinCommand { get; private set; }

        // ── Font & appearance ──

        private string _selectedFont = "Segoe UI";
        public string SelectedFont
        {
            get => _selectedFont;
            set { if (_selectedFont != value) { _selectedFont = value; OnPropertyChanged(); UpdateContentFont(_selectedFont); } }
        }

        private bool _isBold;
        public bool IsBold
        {
            get => _isBold;
            set { if (_isBold != value) { _isBold = value; OnPropertyChanged(); UpdateContentFontWeight(value); } }
        }

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                _isDarkMode = value;
                ApplyTheme(value);
                OnPropertyChanged();
                Properties.Settings.Default.IsDarkMode = value;
                Properties.Settings.Default.Save();
            }
        }

        private double _gridFontSize = 12;
        public double GridFontSize
        {
            get => _gridFontSize;
            set { _gridFontSize = value; OnPropertyChanged(); }
        }

        private double _screenshotZoom = 400;
        public double ScreenshotZoom
        {
            get => _screenshotZoom;
            set { _screenshotZoom = value; OnPropertyChanged(); }
        }

        private int _contextSeconds = 10;
        public int ContextSeconds
        {
            get => _contextSeconds;
            set { if (_contextSeconds != value) { _contextSeconds = value; OnPropertyChanged(); } }
        }

        private string _selectedTimeUnit = "Seconds";
        public string SelectedTimeUnit
        {
            get => _selectedTimeUnit;
            set { _selectedTimeUnit = value; OnPropertyChanged(); }
        }

        // ── Panel visibility ──

        private bool _isLeftPanelVisible = true;
        public bool IsLeftPanelVisible
        {
            get => _isLeftPanelVisible;
            set { _isLeftPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isRightPanelVisible = true;
        public bool IsRightPanelVisible
        {
            get => _isRightPanelVisible;
            set { _isRightPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isBottomPanelVisible = true;
        public bool IsBottomPanelVisible
        {
            get => _isBottomPanelVisible;
            set { _isBottomPanelVisible = value; OnPropertyChanged(); }
        }

        public ICommand ToggleLeftPanelCommand { get; }
        public ICommand ToggleRightPanelCommand { get; }

        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };

        // ── Scroll requests ──

        public event Action<LogEntry>? RequestScrollToLog;
        public event Action<LogEntry, bool>? RequestScrollToLogPreservePosition;
        public event Action<LogEntry>? RequestSaveScrollPosition;
        public event Action<string>? RequestScrollToBottom;

        public void ScrollToLog(LogEntry log) => RequestScrollToLog?.Invoke(log);
        public void ScrollToLogPreservePosition(LogEntry log) => RequestScrollToLogPreservePosition?.Invoke(log, true);
        public void SaveScrollPosition(LogEntry log) => RequestSaveScrollPosition?.Invoke(log);
        public void ScrollTabToBottom(string tabName) => RequestScrollToBottom?.Invoke(tabName);

        // ── Filter computed properties (delegated, bindings from XAML) ──

        public bool HasRangeStart => FilterVM?.HasRangeStart ?? false;
        public List<Models.ActiveFilterItem> ActiveFilters => FilterVM?.GetActiveFilters() ?? new List<Models.ActiveFilterItem>();
        public bool HasActiveFilters => ActiveFilters.Count > 0;
        public ICommand ClearActiveFilterCommand { get; private set; }
        public ICommand AddBackComponentCommand { get; private set; }

        // --- Commands ---
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand RemoveSessionCommand { get; }
        public ICommand MarkRowCommand { get; }
        public ICommand NextMarkedCommand { get; }
        public ICommand PrevMarkedCommand { get; }
        public ICommand JumpToLogCommand { get; }
        public ICommand OpenJiraCommand { get; }
        public ICommand OpenKibanaCommand { get; }
        public ICommand OpenOutlookCommand { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand ShowFailuresCommand { get; private set; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand RemoveConfigCommand { get; }
        public ICommand ApplyConfigCommand { get; }
        public ICommand ShowConfigsFolderCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand OpenLoggerFilterCommand { get; }
        public ICommand OpenMethodFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand StartRangeCommand { get; }
        public ICommand EndRangeCommand { get; }
        public ICommand ClearRangeCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ViewLogDetailsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenPluginTesterCommand { get; }
        public ICommand ToggleBoldCommand { get; }
        public ICommand OpenFontsWindowCommand { get; }
        public ICommand OpenMarkedLogsWindowCommand { get; }
        public ICommand ExportParsedDataCommand { get; }
        public ICommand RunAnalysisCommand { get; }
        public ICommand FilterToStateCommand { get; }
        public ICommand OpenStatesWindowCommand { get; }
        public ICommand OpenSnakeGameCommand { get; }
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }
        public ICommand ToggleExplorerMenuCommand { get; }
        public ICommand ToggleConfigMenuCommand { get; }
        public ICommand ToggleLoggersMenuCommand { get; }
        public ICommand TreeShowThisCommand { get; }
        public ICommand TreeHideThisCommand { get; }
        public ICommand TreeShowOnlyThisCommand { get; }
        public ICommand TreeShowWithChildrenCommand { get; }
        public ICommand TreeHideWithChildrenCommand { get; }
        public ICommand TreeShowAllCommand { get; }
        public ICommand OpenIndigoInvadersCommand { get; }
        public ICommand FilterAppErrorsCommand { get; }
        public ICommand OpenVisualAnalysisCommand { get; }
        public ICommand ResetTimeFocusCommand { get; }
        public ICommand ToggleTimeSyncCommand { get; }
        public ICommand OpenTimeRangeFilterCommand { get; }

        public ICommand AddAnnotationCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }
        public ICommand SaveCaseCommand { get; }
        public ICommand LoadCaseCommand { get; }
        public ICommand OpenGlobalGrepCommand { get; }
        public ICommand OpenStripeAnalysisCommand { get; }
        public ICommand OpenComparisonCommand { get; }
        public ICommand SetAsDefaultCommand { get; }
        public ICommand ResetDefaultsCommand { get; }

        public MainViewModel(ILogFileService logService, ILogColoringService coloringService, ICsvExportService csvService, IDefaultConfigurationService defaultConfigService, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher)
        {
            _logService = logService;
            _coloringService = coloringService;
            _csvService = csvService;
            _defaultConfigService = defaultConfigService;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _isTimeSyncEnabled = false;

            // Initialize child ViewModels
            SessionVM = new LogSessionViewModel(this, _logService, _coloringService, _dialogService, _viewFactory, _dispatcher);
            FilterVM = new FilterSearchViewModel(this, SessionVM, _dialogService, _viewFactory, _dispatcher);
            CaseVM = new CaseManagementViewModel(this, SessionVM, FilterVM, _dialogService, _viewFactory, _dispatcher);
            LiveVM = new LiveMonitoringViewModel(this, SessionVM, FilterVM, CaseVM, _logService, _coloringService, _dispatcher);
            ConfigVM = new ConfigExplorerViewModel(this, SessionVM, _dialogService, _viewFactory, _dispatcher);
            ChartVM = new ChartTabViewModel(this);
            CprVM = new CprAnalysisViewModel(_dialogService);
            DifferentLogsVM = new DifferentLogsViewModel(_logService.GetPluginLoader(), _dialogService, _viewFactory);
            DifferentLogsVM.GetCurrentZipPath = () => SessionVM?.SelectedSession?.FilePath;
            StepRecorderVM = new StepRecorderViewModel(_dialogService);

            // Set dependencies
            SessionVM.SetDependencies(FilterVM, CaseVM, ConfigVM, LiveVM);

            // Subscriptions
            SessionVM.PropertyChanged += SessionVM_PropertyChanged;
            FilterVM.PropertyChanged += FilterVM_PropertyChanged;
            LiveVM.PropertyChanged += LiveVM_PropertyChanged;
            DifferentLogsVM.PropertyChanged += DifferentLogsVM_PropertyChanged;

            ToggleVisualModeCommand = new RelayCommand(o => IsVisualMode = !IsVisualMode);

            TreeShowThisCommand = FilterVM.TreeShowThisCommand;
            TreeHideThisCommand = FilterVM.TreeHideThisCommand;
            TreeShowOnlyThisCommand = FilterVM.TreeShowOnlyThisCommand;
            TreeShowWithChildrenCommand = FilterVM.TreeShowWithChildrenCommand;
            TreeHideWithChildrenCommand = FilterVM.TreeHideWithChildrenCommand;
            TreeShowAllCommand = FilterVM.TreeShowAllCommand;
            OpenTimeRangeFilterCommand = FilterVM.OpenTimeRangeFilterCommand;
            OpenIndigoInvadersCommand = new RelayCommand(OpenIndigoInvaders);

            _allLogsCache = SessionVM.AllLogsCache;
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            ToggleExplorerMenuCommand = new RelayCommand(o => ConfigVM.IsExplorerMenuOpen = !ConfigVM.IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => ConfigVM.IsConfigMenuOpen = !ConfigVM.IsConfigMenuOpen);
            ToggleLoggersMenuCommand = new RelayCommand(o => ConfigVM.IsLoggersMenuOpen = !ConfigVM.IsLoggersMenuOpen);
            ToggleTimeSyncCommand = new RelayCommand(o => IsTimeSyncEnabled = !IsTimeSyncEnabled);
            ToggleLeftPanelCommand = new RelayCommand(o => IsLeftPanelVisible = !IsLeftPanelVisible);
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelVisible = !IsRightPanelVisible);
            AddBackComponentCommand = new RelayCommand(o =>
            {
                if (o is string componentName)
                    _ = SessionVM.AddBackComponentAsync(componentName);
            });
            BrowseTableCommand = ConfigVM.BrowseTableCommand;
            CopyTableNameCommand = new RelayCommand(CopyTableName);

            // --- UPDATED ANNOTATION COMMANDS ---
            ToggleAnnotationCommand = new RelayCommand(ToggleAnnotation);
            CloseAnnotationCommand = new RelayCommand(CloseAnnotation);
            ToggleAllAnnotationsCommand = new RelayCommand(ToggleAllAnnotations);

            LoadCommand = SessionVM.LoadCommand;
            ClearCommand = new RelayCommand(o => { SessionVM.ClearCommand.Execute(o); ConfigVM.IsExplorerMenuOpen = false; });
            RemoveSessionCommand = SessionVM.RemoveSessionCommand;
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);
            FilterAppErrorsCommand = new RelayCommand(FilterAppErrors);
            OpenJiraCommand = new RelayCommand(o => OpenUrl(Services.AppSettingsService.JiraUrl));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);

            OpenMarkedLogsWindowCommand = new RelayCommand(o => { OpenMarkedLogsWindow(o); ConfigVM.IsExplorerMenuOpen = false; });
            OpenStatesWindowCommand = new RelayCommand(o => { OpenStatesWindow(o); ConfigVM.IsExplorerMenuOpen = false; });
            ExportParsedDataCommand = new RelayCommand(o => { _ = ExportParsedData(o); ConfigVM.IsExplorerMenuOpen = false; });
            RunAnalysisCommand = new RelayCommand(o => { RunAnalysis(o); ConfigVM.IsExplorerMenuOpen = false; });
            OpenGlobalGrepCommand = new RelayCommand(o => { OpenGlobalGrepWindow(); ConfigVM.IsExplorerMenuOpen = false; });
            OpenStripeAnalysisCommand = new RelayCommand(o => { _ = OpenStripeAnalysisWindow(); ConfigVM.IsExplorerMenuOpen = false; });
            OpenComparisonCommand = new RelayCommand(o => { OpenComparisonWindow(); }, o => SessionVM.AllLogsCache?.Count > 0 || SessionVM.AllAppLogsCache?.Count > 0);

            ToggleSearchCommand = FilterVM.ToggleSearchCommand;
            CloseSearchCommand = FilterVM.CloseSearchCommand;
            ShowFailuresCommand = new RelayCommand(_ => ShowFailuresAnalysis());
            OpenFilterWindowCommand = FilterVM.OpenFilterWindowCommand;
            OpenColoringWindowCommand = CaseVM.OpenColoringWindowCommand;

            SaveConfigCommand = new RelayCommand(o => { SaveConfiguration(o); ConfigVM.IsConfigMenuOpen = false; });
            LoadConfigCommand = new RelayCommand(o => { LoadConfigurationFromFile(o); ConfigVM.IsConfigMenuOpen = false; });
            RemoveConfigCommand = new RelayCommand(o => { RemoveConfiguration(o); ConfigVM.IsConfigMenuOpen = false; }, o => CaseVM?.SelectedConfig != null);
            ApplyConfigCommand = new RelayCommand(ApplyConfiguration);
            ShowConfigsFolderCommand = CaseVM.ShowConfigsFolderCommand;

            FilterOutCommand = FilterVM.FilterOutCommand;
            FilterOutThreadCommand = FilterVM.FilterOutThreadCommand;
            OpenThreadFilterCommand = FilterVM.OpenThreadFilterCommand;
            OpenLoggerFilterCommand = FilterVM.OpenLoggerFilterCommand;
            OpenMethodFilterCommand = FilterVM.OpenMethodFilterCommand;
            FilterContextCommand = FilterVM.FilterContextCommand;
            UndoFilterOutCommand = FilterVM.UndoFilterOutCommand;
            StartRangeCommand = FilterVM.StartRangeCommand;
            EndRangeCommand = FilterVM.EndRangeCommand;
            ClearRangeCommand = FilterVM.ClearRangeCommand;

            ResetTimeFocusCommand = new RelayCommand(ResetTimeFocus);

            ViewLogDetailsCommand = new RelayCommand(ViewLogDetails);
            ToggleThemeCommand = new RelayCommand(o => IsDarkMode = !IsDarkMode);
            ToggleBoldCommand = new RelayCommand(o => IsBold = !IsBold);
            OpenSettingsCommand = new RelayCommand(OpenSettingsWindow);
            OpenHelpCommand = new RelayCommand(o => WindowManager.OpenWindow(_viewFactory.Create<Views.HelpWindow>()));
            OpenPluginTesterCommand = new RelayCommand(o => WindowManager.GetOrCreate<Views.PluginTesterWindow>(() => _viewFactory.Create<Views.PluginTesterWindow>()));
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            OpenSnakeGameCommand = new RelayCommand(OpenSnakeGame);

            FilterToStateCommand = new RelayCommand(FilterToState);

            ZoomInCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Min(5000, ScreenshotZoom + 100);
                else GridFontSize = Math.Min(30, GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Max(100, ScreenshotZoom - 100);
                else GridFontSize = Math.Max(8, GridFontSize - 1);
            });

            LivePlayCommand = LiveVM.LivePlayCommand;
            LivePauseCommand = LiveVM.LivePauseCommand;
            LiveClearCommand = LiveVM.LiveClearCommand;

            ClearGlobalsSearchCommand    = new RelayCommand(o => GlobalsSearchText = "");
            ToggleGlobalsDiffsCommand    = new RelayCommand(o => GlobalsShowDiffsOnly = !GlobalsShowDiffsOnly);
            ClearSystabSearchCommand = new RelayCommand(o => { SystabSearchText = ""; SystabShowDiffsOnly = false; });
            ToggleLogDetailsPinCommand = new RelayCommand(o => IsLogDetailsPinned = !IsLogDetailsPinned);
            ClearActiveFilterCommand = new RelayCommand(o =>
            {
                if (o is string key)
                    FilterVM?.RemoveActiveFilter(key);
            });

            AddAnnotationCommand = new RelayCommand(AddAnnotation);
            DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation);
            SaveCaseCommand = new RelayCommand(SaveCase);
            LoadCaseCommand = new RelayCommand(LoadCase);

            SetAsDefaultCommand = new RelayCommand(SetCurrentAsDefault);
            ResetDefaultsCommand = new RelayCommand(ResetDefaults);

            _isDarkMode = Properties.Settings.Default.IsDarkMode;
            ApplyTheme(_isDarkMode);
            LoadSavedConfigurations();
            LoadUserDefaults();
        }

        // ── Visual mode ──

        private void InitializeVisualMode()
        {
            // Use filtered logs if time range is active, otherwise use all logs
            var logsToUse = FilterVM.IsGlobalTimeRangeActive ? SessionVM.Logs : (SessionVM.AllLogsCache ?? SessionVM.Logs);
            if (VisualTimelineVM != null)
            {
                // S4-5 (binary APP): skip Events on timeline
                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
                VisualTimelineVM.LoadData(logsToUse.ToList(), eventsToShow);
            }
        }

        // ── Annotation logic ──

        private void ToggleAnnotation(object parameter)
        {
            if (parameter is LogEntry log && log.HasAnnotation)
                log.IsAnnotationExpanded = !log.IsAnnotationExpanded;
        }

        private void ToggleAllAnnotations(object obj)
        {
            IEnumerable<LogEntry> targetList = null;

            if (SelectedTabIndex == AppConstants.TAB_APP)
                targetList = SessionVM?.AllAppLogsCache;
            else
                targetList = SessionVM?.AllLogsCache;

            if (targetList == null || !targetList.Any()) return;

            var logsWithAnnotations = targetList.Where(l => l.HasAnnotation).ToList();
            if (!logsWithAnnotations.Any()) return;

            bool anyExpanded = logsWithAnnotations.Any(l => l.IsAnnotationExpanded);
            bool newState = !anyExpanded;

            foreach (var log in logsWithAnnotations)
                log.IsAnnotationExpanded = newState;

            if (CaseVM != null) CaseVM.ShowAllAnnotations = newState;
            SessionVM.StatusMessage = newState ? "All annotations expanded" : "All annotations collapsed";
        }

        private void CloseAnnotation(object parameter) => CaseVM?.CloseAnnotationCommand.Execute(parameter);

        // ── SQLite content loading ──

        private string LoadSqliteContent(byte[] dbBytes)
        {
            var sb = new System.Text.StringBuilder();
            string tempDbPath = null;

            try
            {
                tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, dbBytes);

                using (var connection = new SqliteConnection($"Data Source={tempDbPath};Mode=ReadOnly"))
                {
                    connection.Open();
                    var tables = new List<string>();
                    using (var cmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { tables.Add(reader.GetString(0)); }
                    }

                    sb.AppendLine($"=== SQLite Database: {tables.Count} tables ===");
                    sb.AppendLine();

                    foreach (var tableName in tables)
                    {
                        sb.AppendLine($"━━━ TABLE: {tableName} ━━━");
                        using (var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM [{EscapeSqlBracketId(tableName)}]", connection))
                        {
                            var count = countCmd.ExecuteScalar();
                            sb.AppendLine($"Rows: {count}");
                        }
                        using (var cmd = new SqliteCommand($"SELECT * FROM [{EscapeSqlBracketId(tableName)}] LIMIT 100", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            var columns = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++) { columns.Add(reader.GetName(i)); }
                            sb.AppendLine($"Columns: {string.Join(", ", columns)}");
                            sb.AppendLine();

                            int rowNum = 0;
                            while (reader.Read() && rowNum < 100)
                            {
                                sb.AppendLine($"--- Row {++rowNum} ---");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
                                    if (value.Length > 500) value = value.Substring(0, 500) + "...";
                                    sb.AppendLine($"  {columns[i]}: {value}");
                                }
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error reading SQLite database: {ex.Message}");
            }
            finally
            {
                if (tempDbPath != null && File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch (Exception ex) { AppLogger.Error("Temp DB cleanup failed", ex); }
                }
            }
            return sb.ToString();
        }

        private static string EscapeSqlBracketId(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return identifier.Replace("]", "]]");
        }

        // ── Live monitoring ──

        private void LiveClear(object obj)
        {
            LiveVM.IsRunning = false;

            _dispatcher.Post(() =>
            {
                lock (_collectionLock)
                {
                    if (SessionVM.AllLogsCache != null) SessionVM.AllLogsCache.Clear();
                    FilterVM?.FilteredLogs?.Clear();
                    SelectedLog = null;
                }
            });

            if (LiveVM.IsLiveMode)
            {
                LiveVM.IsRunning = true;
                SessionVM.StatusMessage ="Cleared. Monitoring continues...";
            }
            else
            {
                SessionVM.StatusMessage ="Logs cleared.";
            }
        }

        private void ClearLogs(object obj)
        {
            try
            {
                SessionVM?.ClearCommand.Execute(null);

                CaseVM?.ClearMarkedLogs();
                CaseVM?.LogAnnotations?.Clear();

                if (FilterVM != null)
                {
                    FilterVM.IsMainFilterActive = false;
                    FilterVM.IsAppFilterActive = false;
                    FilterVM.IsMainFilterOutActive = false;
                    FilterVM.IsAppFilterOutActive = false;
                    FilterVM.IsAppTimeFocusActive = false;
                    FilterVM.IsTimeFocusActive = false;

                    FilterVM.LastFilteredAppCache?.Clear();
                    FilterVM.LastFilteredCache?.Clear();
                    FilterVM.NegativeFilters?.Clear();
                    FilterVM.ActiveThreadFilters?.Clear();
                    FilterVM.ActiveLoggerFilters?.Clear();
                    FilterVM.ActiveMethodFilters?.Clear();

                    if (FilterVM?.LoggerTreeRoot != null)
                    {
                        FilterVM.LoggerTreeRoot.Clear();
                    }
                    if (FilterVM?.PlcLoggerTreeRoot != null)
                    {
                        FilterVM.PlcLoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(ActiveLoggerTree));
                    }

                    FilterVM.SearchText = "";
                    FilterVM.IsSearchPanelVisible = false;
                }

                SessionVM.Logs = new List<LogEntry>();

                ConfigVM?.ClearConfigurationFiles();

                SetupInfo = "";
                OnPropertyChanged(nameof(SetupInfo));
                PressConfig = "";
                OnPropertyChanged(nameof(PressConfig));
                VersionsInfo = "";
                OnPropertyChanged(nameof(VersionsInfo));
                WindowTitle = "IndiLogs 3.0";
                OnPropertyChanged(nameof(WindowTitle));

                SessionVM.CurrentProgress = 0;
                ScreenshotZoom = 400;
                SessionVM.SelectedSession = null;
                SelectedLog = null;
                IsFilterOutActive = false;
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(IsFilterOutActive));

                ResetTreeFilters();

                if (VisualTimelineVM != null)
                    VisualTimelineVM.Clear();
                _isVisualMode = false;
                OnPropertyChanged(nameof(IsVisualMode));

                SelectedTabIndex = AppConstants.TAB_PLC;
                OnPropertyChanged(nameof(SelectedTabIndex));
                LeftTabIndex = 0;
                OnPropertyChanged(nameof(LeftTabIndex));

                SessionVM.StatusMessage ="All data cleared successfully";
            }
            catch (Exception ex)
            {
                SessionVM.StatusMessage =$"Clear failed: {ex.Message}";
            }
        }

        private void StartLiveMonitoring(string path) => LiveVM?.StartLiveMonitoring(path);
        private void StopLiveMonitoring() => LiveVM?.StopLiveMonitoring();
        private void LivePlay(object obj) => LiveVM?.LivePlayCommand.Execute(obj);
        private void LivePause(object obj) => LiveVM?.LivePauseCommand.Execute(obj);

        // ── Configuration management ──

        private void LoadSavedConfigurations()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs3.0", "Configs");
            CaseVM?.EnsureDefaultConfigsOnDisk(path);
        }

        private void LoadUserDefaults()
        {
            _defaultConfigService.Load();
            var defaults = _defaultConfigService.CurrentDefaults;
            if (defaults == null) return;

            if (defaults.HasCustomMainColoring && defaults.MainDefaultColoringRules != null)
                _coloringService.UserDefaultMainRules = defaults.MainDefaultColoringRules;
            if (defaults.HasCustomAppColoring && defaults.AppDefaultColoringRules != null)
                _coloringService.UserDefaultAppRules = defaults.AppDefaultColoringRules;
            if (defaults.HasCustomPlcFilter && defaults.PlcFilteredDefaultFilter != null)
                FilterVM.DefaultPlcFilter = defaults.PlcFilteredDefaultFilter;
        }

        private void SetCurrentAsDefault(object obj)
        {
            var config = new Models.DefaultConfiguration();

            if (FilterVM.DefaultPlcFilter != null)
            {
                config.PlcFilteredDefaultFilter = FilterVM.DefaultPlcFilter.DeepClone();
                config.HasCustomPlcFilter = true;
            }

            if (CaseVM?.MainColoringRules != null && CaseVM.MainColoringRules.Count > 0)
            {
                config.MainDefaultColoringRules = CaseVM.MainColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomMainColoring = true;
            }

            if (CaseVM?.AppColoringRules != null && CaseVM.AppColoringRules.Count > 0)
            {
                config.AppDefaultColoringRules = CaseVM.AppColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomAppColoring = true;
            }

            _defaultConfigService.Save(config);

            _coloringService.UserDefaultMainRules = config.MainDefaultColoringRules;
            _coloringService.UserDefaultAppRules = config.AppDefaultColoringRules;

            SessionVM.StatusMessage = "Current settings saved as defaults.";
        }

        private void ResetDefaults(object obj)
        {
            _defaultConfigService.Reset();
            _coloringService.UserDefaultMainRules = null;
            _coloringService.UserDefaultAppRules = null;
            FilterVM.DefaultPlcFilter = null;

            SessionVM.StatusMessage = "Defaults reset to factory settings.";
        }

        private void ApplyConfiguration(object parameter) { if (parameter is SavedConfiguration c) CaseVM?.ApplyConfiguration(c); }
        private void RemoveConfiguration(object parameter) => CaseVM?.DeleteConfigCommand.Execute(parameter);
        private void SaveConfiguration(object obj) => CaseVM?.SaveConfigCommand.Execute(obj);
        private void LoadConfigurationFromFile(object obj) => CaseVM?.LoadConfigCommand.Execute(obj);

        // ── Case management delegates ──

        private void MarkRow(object obj) => CaseVM?.MarkLogCommand.Execute(obj);
        private void GoToNextMarked(object obj) => CaseVM?.GoToNextMarkedCommand.Execute(obj);
        private void GoToPrevMarked(object obj) => CaseVM?.GoToPrevMarkedCommand.Execute(obj);
        private void JumpToLog(object obj) { if (obj is LogEntry log) { SelectedLog = log; RequestScrollToLog?.Invoke(log); } }

        public LogAnnotation GetAnnotation(LogEntry log) => CaseVM?.GetAnnotation(log);
        private void AddAnnotation(object parameter) => CaseVM?.AddAnnotationCommand.Execute(parameter);
        private void DeleteAnnotation(object parameter) => CaseVM?.DeleteAnnotationCommand.Execute(parameter);
        private void SaveCase(object parameter) => CaseVM?.SaveCaseCommand.Execute(parameter);
        private void LoadCase(object parameter) => CaseVM?.LoadCaseCommand.Execute(parameter);

        // ── Clipboard ──

        private void CopyTableName(object parameter)
        {
            if (parameter is DbTreeNode node && !string.IsNullOrEmpty(node.Name))
            {
                try
                {
                    System.Windows.Clipboard.SetText(node.Name);
                }
                catch (Exception ex) { AppLogger.Error("Clipboard copy failed", ex); }
            }
        }

        // ── Session changes ──

        private async Task OnSelectedSessionChangedAsync(LogSessionData session)
        {
            if (session != null && session.HasBinaryAppLogs &&
                !string.IsNullOrEmpty(session.FilePath) && File.Exists(session.FilePath))
                await StepRecorderVM.LoadFromZipAsync(session.FilePath);
            else
                StepRecorderVM.Clear();
        }

        // ── Child VM PropertyChanged handlers ──

        private void SessionVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SessionVM.SelectedSession):
                    OnPropertyChanged(nameof(PlcTabHeader));
                    OnPropertyChanged(nameof(HasBinaryAppLogs));
                    OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                    OnPropertyChanged(nameof(ReportsButtonText));
                    OnPropertyChanged(nameof(ShowMainTabs));
                    OnPropertyChanged(nameof(HasExternalFileOnly));
                    _ = OnSelectedSessionChangedAsync(SessionVM.SelectedSession);
                    break;
            }
        }

        private void FilterVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FilterVM.LoggerTreeRoot):
                case nameof(FilterVM.PlcLoggerTreeRoot):
                    OnPropertyChanged(nameof(ActiveLoggerTree));
                    break;
                case nameof(FilterVM.IsMainFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsAppFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsMainFilterOutActive):
                case nameof(FilterVM.IsAppFilterOutActive):
                    OnPropertyChanged(nameof(IsFilterOutActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.HasRangeStart):
                    OnPropertyChanged(nameof(HasRangeStart));
                    break;
            }
        }

        private void LiveVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            // LiveVM properties (IsLiveMode, IsRunning, IsPaused) bind directly via LiveVM.* in XAML
        }

        private void DifferentLogsVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DifferentLogsVM.HasFile))
            {
                OnPropertyChanged(nameof(ShowMainTabs));
                OnPropertyChanged(nameof(HasExternalFileOnly));
            }
        }

        // ── Dispose ──

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (SessionVM != null) SessionVM.PropertyChanged -= SessionVM_PropertyChanged;
                if (FilterVM != null) FilterVM.PropertyChanged -= FilterVM_PropertyChanged;
                if (LiveVM != null) LiveVM.PropertyChanged -= LiveVM_PropertyChanged;
                if (DifferentLogsVM != null) DifferentLogsVM.PropertyChanged -= DifferentLogsVM_PropertyChanged;
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
