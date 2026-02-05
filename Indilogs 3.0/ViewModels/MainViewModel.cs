using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Views;
using IndiLogs_3._0.ViewModels.Components;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Data.SQLite;

namespace IndiLogs_3._0.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // --- Child ViewModels (Composition Pattern) ---
        public LogSessionViewModel SessionVM { get; private set; }
        public FilterSearchViewModel FilterVM { get; private set; }
        public LiveMonitoringViewModel LiveVM { get; private set; }
        public CaseManagementViewModel CaseVM { get; private set; }
        public ConfigExplorerViewModel ConfigVM { get; private set; }
        public VisualTimelineViewModel VisualTimelineVM { get; set; } = new VisualTimelineViewModel();
        public ChartTabViewModel ChartVM { get; private set; }

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

        public ObservableCollection<LogEntry> MarkedAppLogs => CaseVM?.MarkedAppLogs;
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;
        private readonly CsvExportService _csvService;

        // Windows Instances
        private StatesWindow _statesWindow;
        private AnalysisReportWindow _analysisWindow;
        private bool _isAnalysisRunning;
        private ExportConfigurationWindow _exportConfigWindow = null;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set { _isAnalysisRunning = value; OnPropertyChanged(); }
        }

        // Caches
        private IList<LogEntry> _allLogsCache;
        private IList<LogEntry> _allAppLogsCache;

        // Coloring
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();

        public List<ColoringCondition> MainColoringRules
        {
            get => CaseVM?.MainColoringRules;
            set { if (CaseVM != null) CaseVM.MainColoringRules = value; }
        }
        public List<ColoringCondition> AppColoringRules
        {
            get => CaseVM?.AppColoringRules;
            set { if (CaseVM != null) CaseVM.AppColoringRules = value; }
        }

        // Case File & Annotations
        public Dictionary<LogEntry, LogAnnotation> LogAnnotations => CaseVM?.LogAnnotations;

        private const int UI_UPDATE_BATCH_SIZE = 500;
        private readonly object _collectionLock = new object();

        // Collections
        public IEnumerable<LogEntry> Logs
        {
            get => SessionVM?.Logs;
            set { if (SessionVM != null) SessionVM.Logs = value; OnPropertyChanged(); }
        }

        public ObservableRangeCollection<LogEntry> FilteredLogs => FilterVM?.FilteredLogs;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered => FilterVM?.AppDevLogsFiltered;
        public ObservableCollection<LoggerNode> LoggerTreeRoot => FilterVM?.LoggerTreeRoot;

        public IList<LogEntry> AllLogsCache => SessionVM?.AllLogsCache;
        public IList<LogEntry> AllAppLogsCache => SessionVM?.AllAppLogsCache;
        public ObservableCollection<EventEntry> Events => SessionVM?.Events;
        public ObservableCollection<BitmapImage> Screenshots => SessionVM?.Screenshots;
        public ObservableCollection<string> LoadedFiles => SessionVM?.LoadedFiles;
        public ObservableCollection<LogSessionData> LoadedSessions => SessionVM?.LoadedSessions;

        public LogSessionData SelectedSession
        {
            get => SessionVM?.SelectedSession;
            set { if (SessionVM != null) SessionVM.SelectedSession = value; }
        }

        public double CurrentProgress
        {
            get => SessionVM?.CurrentProgress ?? 0;
            set { if (SessionVM != null) SessionVM.CurrentProgress = value; }
        }

        public string StatusMessage
        {
            get => SessionVM?.StatusMessage;
            set { if (SessionVM != null) SessionVM.StatusMessage = value; }
        }

        public bool IsBusy
        {
            get => SessionVM?.IsBusy ?? false;
            set { if (SessionVM != null) SessionVM.IsBusy = value; }
        }

        // Search & Filter Properties
        public string SearchText
        {
            get => FilterVM?.SearchText;
            set { if (FilterVM != null) FilterVM.SearchText = value; }
        }

       

        public LoggerNode SelectedTreeItem => FilterVM?.SelectedTreeItem;
        public bool IsMainFilterActive => FilterVM?.IsMainFilterActive ?? false;
        public bool IsAppFilterActive => FilterVM?.IsAppFilterActive ?? false;
        public bool IsMainFilterOutActive => FilterVM?.IsMainFilterOutActive ?? false;
        public bool IsAppFilterOutActive => FilterVM?.IsAppFilterOutActive ?? false;
        public bool IsTimeFocusActive => FilterVM?.IsTimeFocusActive ?? false;
        public bool IsAppTimeFocusActive => FilterVM?.IsAppTimeFocusActive ?? false;

        public FilterNode MainFilterRoot
        {
            get => FilterVM?.MainFilterRoot;
            set { if (FilterVM != null) FilterVM.MainFilterRoot = value; }
        }
        public FilterNode AppFilterRoot
        {
            get => FilterVM?.AppFilterRoot;
            set { if (FilterVM != null) FilterVM.AppFilterRoot = value; }
        }
        public FilterNode SavedFilterRoot
        {
            get => FilterVM?.SavedFilterRoot;
            set { if (FilterVM != null) FilterVM.SavedFilterRoot = value; }
        }
        public List<string> NegativeFilters => FilterVM?.NegativeFilters;
        public List<string> ActiveThreadFilters => FilterVM?.ActiveThreadFilters;
        public List<LogEntry> LastFilteredCache
        {
            get => FilterVM?.LastFilteredCache;
            set { if (FilterVM != null) FilterVM.LastFilteredCache = value; }
        }
        public List<LogEntry> LastFilteredAppCache
        {
            get => FilterVM?.LastFilteredAppCache;
            set { if (FilterVM != null) FilterVM.LastFilteredAppCache = value; }
        }
        public HashSet<string> TreeHiddenLoggers => FilterVM?.TreeHiddenLoggers;
        public HashSet<string> TreeHiddenPrefixes => FilterVM?.TreeHiddenPrefixes;
        public string TreeShowOnlyLogger
        {
            get => FilterVM?.TreeShowOnlyLogger;
            set { if (FilterVM != null) FilterVM.TreeShowOnlyLogger = value; }
        }
        public string TreeShowOnlyPrefix
        {
            get => FilterVM?.TreeShowOnlyPrefix;
            set { if (FilterVM != null) FilterVM.TreeShowOnlyPrefix = value; }
        }
        public bool IsSearchPanelVisible
        {
            get => FilterVM?.IsSearchPanelVisible ?? false;
            set
            {
                if (FilterVM != null)
                    FilterVM.IsSearchPanelVisible = value;
                OnPropertyChanged();
            }
        }
        // Live Mode
        public bool IsLiveMode
        {
            get => LiveVM?.IsLiveMode ?? false;
            set { if (LiveVM != null) LiveVM.IsLiveMode = value; }
        }
        public bool IsRunning
        {
            get => LiveVM?.IsRunning ?? false;
            set { if (LiveVM != null) LiveVM.IsRunning = value; }
        }
        public bool IsPaused => LiveVM?.IsPaused ?? false;

        // Case Management
        public ObservableCollection<SavedConfiguration> SavedConfigs
        {
            get => CaseVM?.SavedConfigs;
            set { /* Read-only collection */ }
        }
        public ObservableCollection<LogEntry> MarkedLogs
        {
            get => CaseVM?.MarkedLogs;
            set { /* Read-only collection */ }
        }
        public SavedConfiguration SelectedConfig
        {
            get => CaseVM?.SelectedConfig;
            set { if (CaseVM != null) CaseVM.SelectedConfig = value; }
        }
        public bool IsMarkedLogsCombined
        {
            get => CaseVM?.IsMarkedLogsCombined ?? false;
            set { if (CaseVM != null) CaseVM.IsMarkedLogsCombined = value; }
        }
        public bool ShowAllAnnotations
        {
            get => CaseVM?.ShowAllAnnotations ?? false;
            set { if (CaseVM != null) CaseVM.ShowAllAnnotations = value; }
        }

        // Config Explorer
        public ObservableCollection<string> ConfigurationFiles => ConfigVM?.ConfigurationFiles;
        public string SelectedConfigFile
        {
            get => ConfigVM?.SelectedConfigFile;
            set { if (ConfigVM != null) ConfigVM.SelectedConfigFile = value; }
        }
        public string ConfigFileContent
        {
            get => ConfigVM?.ConfigFileContent;
            set { if (ConfigVM != null) ConfigVM.ConfigFileContent = value; }
        }
        public string FilteredConfigContent => ConfigVM?.FilteredConfigContent;
        public string ConfigSearchText
        {
            get => ConfigVM?.ConfigSearchText;
            set { if (ConfigVM != null) ConfigVM.ConfigSearchText = value; }
        }
        public ObservableCollection<DbTreeNode> DbTreeNodes => ConfigVM?.DbTreeNodes;
        public bool IsDbFileSelected
        {
            get => ConfigVM?.IsDbFileSelected ?? false;
            set { if (ConfigVM != null) ConfigVM.IsDbFileSelected = value; }
        }
        public bool IsExplorerMenuOpen
        {
            get => ConfigVM?.IsExplorerMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsExplorerMenuOpen = value; }
        }
        public bool IsConfigMenuOpen
        {
            get => ConfigVM?.IsConfigMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsConfigMenuOpen = value; }
        }
        public bool IsLoggersMenuOpen
        {
            get => ConfigVM?.IsLoggersMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsLoggersMenuOpen = value; }
        }

        // --- PANEL VISIBILITY ---
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

        public ICommand ToggleLeftPanelCommand { get; }
        public ICommand ToggleRightPanelCommand { get; }

        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };


        public event Action<LogEntry> RequestScrollToLog;
        public event Action<LogEntry, bool> RequestScrollToLogPreservePosition;
        public event Action<LogEntry> RequestSaveScrollPosition;

        /// <summary>
        /// Public method to trigger scroll to log event from child ViewModels
        /// </summary>
        public void ScrollToLog(LogEntry log)
        {
            RequestScrollToLog?.Invoke(log);
        }

        /// <summary>
        /// Scrolls to log while preserving its visual position on screen (used when filter changes)
        /// </summary>
        public void ScrollToLogPreservePosition(LogEntry log)
        {
            RequestScrollToLogPreservePosition?.Invoke(log, true);
        }

        /// <summary>
        /// Saves the current scroll position before filter changes (call BEFORE applying filters)
        /// </summary>
        public void SaveScrollPosition(LogEntry log)
        {
            RequestSaveScrollPosition?.Invoke(log);
        }

        // --- SELECTED TAB INDEX ---
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    if (_selectedTabIndex == 2) // APP Tab
                    {
                        LeftTabIndex = 1; // LOGGERS
                    }
                    else if (_selectedTabIndex == 0 || _selectedTabIndex == 1) // PLC Tabs
                    {
                        LeftTabIndex = 0; // EXPLORER
                    }

                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                    OnPropertyChanged(nameof(IsPLCTabSelected));
                    OnPropertyChanged(nameof(IsAppTabSelected));
                }
            }
        }

        public bool IsPLCTabSelected => _selectedTabIndex == 0 || _selectedTabIndex == 1;
        public bool IsAppTabSelected => _selectedTabIndex == 2;


        private int _leftTabIndex;
        public int LeftTabIndex
        {
            get => _leftTabIndex;
            set { _leftTabIndex = value; OnPropertyChanged(); }
        }

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        private string _setupInfo;
        public string SetupInfo
        {
            get => _setupInfo;
            set { _setupInfo = value; OnPropertyChanged(); }
        }

        private string _pressConfig;
        public string PressConfig
        {
            get => _pressConfig;
            set { _pressConfig = value; OnPropertyChanged(); }
        }

        private string _versionsInfo;
        public string VersionsInfo
        {
            get => _versionsInfo;
            set { _versionsInfo = value; OnPropertyChanged(); }
        }

        private LogEntry _selectedLog;
        public LogEntry SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); }
        }

        private bool _isSearchSyntaxValid = true;
        public bool IsSearchSyntaxValid
        {
            get => _isSearchSyntaxValid;
            set
            {
                if (_isSearchSyntaxValid != value)
                {
                    _isSearchSyntaxValid = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _searchSyntaxError;
        public string SearchSyntaxError
        {
            get => _searchSyntaxError;
            set
            {
                if (_searchSyntaxError != value)
                {
                    _searchSyntaxError = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ValidateSearchSyntax()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
                return;
            }

            if (QueryParserService.HasBooleanOperators(SearchText))
            {
                var parser = new QueryParserService();
                var result = parser.Parse(SearchText, out string errorMessage);

                if (result == null)
                {
                    IsSearchSyntaxValid = false;
                    SearchSyntaxError = errorMessage;
                }
                else
                {
                    IsSearchSyntaxValid = true;
                    SearchSyntaxError = null;
                }
            }
            else
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
            }
        }

        public bool IsFilterActive
        {
            get => SelectedTabIndex == 2 ? IsAppFilterActive : IsMainFilterActive;
            set
            {
                System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] value={value}, Tab={SelectedTabIndex}");

                // Save the currently selected log and its scroll position BEFORE changing filter state
                var savedSelectedLog = SelectedLog;
                if (savedSelectedLog != null)
                {
                    SaveScrollPosition(savedSelectedLog);
                }

                if (SelectedTabIndex == 2)
                {
                    System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] APP: Current={FilterVM?.IsAppFilterActive}, HasStored={FilterVM?.HasAppStoredFilter}");
                    if (FilterVM != null && FilterVM.IsAppFilterActive != value)
                    {
                        // Only toggle if there's a stored filter to show/hide
                        // If no stored filter and trying to activate, do nothing
                        if (value && !FilterVM.HasAppStoredFilter)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] APP: No stored filter, returning");
                            return;
                        }

                        FilterVM.IsAppFilterActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: Current={FilterVM?.IsMainFilterActive}, HasStored={FilterVM?.HasMainStoredFilter}");
                    System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: _mainFilterRoot={FilterVM?.MainFilterRoot != null}, ThreadFilters={FilterVM?.ActiveThreadFilters?.Count ?? 0}, TimeFocus={FilterVM?.IsTimeFocusActive}");

                    if (FilterVM != null && FilterVM.IsMainFilterActive != value)
                    {
                        // Only toggle if there's a stored filter to show/hide
                        // If no stored filter and trying to activate, do nothing
                        if (value && !FilterVM.HasMainStoredFilter)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: No stored filter, returning");
                            return;
                        }

                        System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: Setting IsMainFilterActive to {value}");
                        FilterVM.IsMainFilterActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(value);

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: Restoring selection to log at index {Logs?.ToList().IndexOf(savedSelectedLog) ?? -1}");
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                    System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: Dispatched scroll to log");
                                }));
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsFilterActive SET] MAIN: savedSelectedLog is null, not restoring");
                        }
                    }
                }
            }
        }

        public bool IsFilterOutActive
        {
            get => SelectedTabIndex == 2 ? IsAppFilterOutActive : IsMainFilterOutActive;
            set
            {
                // Save the currently selected log and its scroll position BEFORE changing filter state
                var savedSelectedLog = SelectedLog;
                if (savedSelectedLog != null)
                {
                    SaveScrollPosition(savedSelectedLog);
                }

                if (SelectedTabIndex == 2)
                {
                    if (FilterVM != null && FilterVM.IsAppFilterOutActive != value)
                    {
                        // Only toggle if there's a stored filter out to show/hide
                        if (value && !FilterVM.HasAppStoredFilterOut)
                            return;

                        FilterVM.IsAppFilterOutActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
                else
                {
                    if (FilterVM != null && FilterVM.IsMainFilterOutActive != value)
                    {
                        // Only toggle if there's a stored filter out to show/hide
                        if (value && !FilterVM.HasMainStoredFilterOut)
                            return;

                        FilterVM.IsMainFilterOutActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(FilterVM.IsMainFilterActive);

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
            }
        }


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


        // Time-Sync Scrolling
        private bool _isTimeSyncEnabled;
        public bool IsTimeSyncEnabled
        {
            get => _isTimeSyncEnabled;
            set
            {
                _isTimeSyncEnabled = value;
                OnPropertyChanged();
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"[TIME-SYNC] Feature is now: {(value ? "ENABLED ✓" : "DISABLED ✗")}");
                System.Diagnostics.Debug.WriteLine($"========================================");

                StatusMessage = value ? "🔗 Time-Sync ENABLED" : "⛓ Time-Sync DISABLED";
            }
        }

        private bool _isSyncScrolling = false;
        private int _timeSyncOffsetSeconds = 0;

        public int TimeSyncOffsetSeconds
        {
            get => _timeSyncOffsetSeconds;
            set { _timeSyncOffsetSeconds = value; OnPropertyChanged(); }
        }

        // --- Commands ---
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand MarkRowCommand { get; }
        public ICommand NextMarkedCommand { get; }
        public ICommand PrevMarkedCommand { get; }
        public ICommand JumpToLogCommand { get; }
        public ICommand OpenJiraCommand { get; }
        public ICommand OpenKibanaCommand { get; }
        public ICommand OpenOutlookCommand { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand RemoveConfigCommand { get; }
        public ICommand ApplyConfigCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand OpenLoggerFilterCommand { get; }
        public ICommand OpenMethodFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ViewLogDetailsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
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

        public MainViewModel()
        {
            _csvService = new CsvExportService();
            _logService = new LogFileService();
            _coloringService = new LogColoringService();
            _isTimeSyncEnabled = false;

            // Initialize child ViewModels
            SessionVM = new LogSessionViewModel(this, _logService, _coloringService);
            FilterVM = new FilterSearchViewModel(this, SessionVM);
            CaseVM = new CaseManagementViewModel(this, SessionVM, FilterVM);
            LiveVM = new LiveMonitoringViewModel(this, SessionVM, FilterVM, CaseVM, _logService, _coloringService);
            ConfigVM = new ConfigExplorerViewModel(this, SessionVM);
            ChartVM = new ChartTabViewModel(this);

            // Set dependencies
            SessionVM.SetDependencies(FilterVM, CaseVM, ConfigVM, LiveVM);

            // Subscriptions
            SessionVM.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SessionVM.Logs): OnPropertyChanged(nameof(Logs)); break;
                    case nameof(SessionVM.AllLogsCache): OnPropertyChanged(nameof(AllLogsCache)); break;
                    case nameof(SessionVM.AllAppLogsCache): OnPropertyChanged(nameof(AllAppLogsCache)); break;
                    case nameof(SessionVM.Events): OnPropertyChanged(nameof(Events)); break;
                    case nameof(SessionVM.Screenshots): OnPropertyChanged(nameof(Screenshots)); break;
                    case nameof(SessionVM.LoadedFiles): OnPropertyChanged(nameof(LoadedFiles)); break;
                    case nameof(SessionVM.LoadedSessions): OnPropertyChanged(nameof(LoadedSessions)); break;
                    case nameof(SessionVM.SelectedSession): OnPropertyChanged(nameof(SelectedSession)); break;
                    case nameof(SessionVM.CurrentProgress): OnPropertyChanged(nameof(CurrentProgress)); break;
                    case nameof(SessionVM.StatusMessage): OnPropertyChanged(nameof(StatusMessage)); break;
                    case nameof(SessionVM.IsBusy): OnPropertyChanged(nameof(IsBusy)); break;
                }
            };

            FilterVM.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(FilterVM.FilteredLogs): OnPropertyChanged(nameof(FilteredLogs)); break;
                    case nameof(FilterVM.AppDevLogsFiltered): OnPropertyChanged(nameof(AppDevLogsFiltered)); break;
                    case nameof(FilterVM.SearchText): OnPropertyChanged(nameof(SearchText)); break;
                    case nameof(FilterVM.IsSearchPanelVisible): OnPropertyChanged(nameof(IsSearchPanelVisible)); break;
                    case nameof(FilterVM.LoggerTreeRoot): OnPropertyChanged(nameof(LoggerTreeRoot)); break;
                    case nameof(FilterVM.SelectedTreeItem): OnPropertyChanged(nameof(SelectedTreeItem)); break;
                    case nameof(FilterVM.IsMainFilterActive): OnPropertyChanged(nameof(IsMainFilterActive)); break;
                    case nameof(FilterVM.IsAppFilterActive): OnPropertyChanged(nameof(IsAppFilterActive)); break;
                }
            };

            LiveVM.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(LiveVM.IsLiveMode): OnPropertyChanged(nameof(IsLiveMode)); break;
                    case nameof(LiveVM.IsRunning): OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(IsPaused)); break;
                    case nameof(LiveVM.IsPaused): OnPropertyChanged(nameof(IsPaused)); break;
                }
            };

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
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            ToggleExplorerMenuCommand = new RelayCommand(o => IsExplorerMenuOpen = !IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => IsConfigMenuOpen = !IsConfigMenuOpen);
            ToggleLoggersMenuCommand = new RelayCommand(o => IsLoggersMenuOpen = !IsLoggersMenuOpen);
            ToggleTimeSyncCommand = new RelayCommand(o => IsTimeSyncEnabled = !IsTimeSyncEnabled);
            ToggleLeftPanelCommand = new RelayCommand(o => IsLeftPanelVisible = !IsLeftPanelVisible);
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelVisible = !IsRightPanelVisible);
            BrowseTableCommand = ConfigVM.BrowseTableCommand;
            CopyTableNameCommand = new RelayCommand(CopyTableName);

            // --- UPDATED ANNOTATION COMMANDS ---
            ToggleAnnotationCommand = new RelayCommand(ToggleAnnotation);
            CloseAnnotationCommand = new RelayCommand(CloseAnnotation);
            ToggleAllAnnotationsCommand = new RelayCommand(ToggleAllAnnotations);

            LoadCommand = SessionVM.LoadCommand;
            ClearCommand = new RelayCommand(o => { SessionVM.ClearCommand.Execute(o); IsExplorerMenuOpen = false; });
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);
            FilterAppErrorsCommand = new RelayCommand(FilterAppErrors);
            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);

            OpenMarkedLogsWindowCommand = new RelayCommand(o => { OpenMarkedLogsWindow(o); IsExplorerMenuOpen = false; });
            OpenStatesWindowCommand = new RelayCommand(o => { OpenStatesWindow(o); IsExplorerMenuOpen = false; });
            ExportParsedDataCommand = new RelayCommand(o => { ExportParsedData(o); IsExplorerMenuOpen = false; });
            RunAnalysisCommand = new RelayCommand(o => { RunAnalysis(o); IsExplorerMenuOpen = false; });
            OpenGlobalGrepCommand = new RelayCommand(o => { OpenGlobalGrepWindow(); IsExplorerMenuOpen = false; });
            OpenStripeAnalysisCommand = new RelayCommand(o => { OpenStripeAnalysisWindow(); IsExplorerMenuOpen = false; });
            OpenComparisonCommand = new RelayCommand(o => { OpenComparisonWindow(); }, o => SessionVM.AllLogsCache?.Count > 0 || SessionVM.AllAppLogsCache?.Count > 0);

            ToggleSearchCommand = FilterVM.ToggleSearchCommand;
            CloseSearchCommand = FilterVM.CloseSearchCommand;
            OpenFilterWindowCommand = FilterVM.OpenFilterWindowCommand;
            OpenColoringWindowCommand = CaseVM.OpenColoringWindowCommand;

            SaveConfigCommand = new RelayCommand(o => { SaveConfiguration(o); IsConfigMenuOpen = false; });
            LoadConfigCommand = new RelayCommand(o => { LoadConfigurationFromFile(o); IsConfigMenuOpen = false; });
            RemoveConfigCommand = new RelayCommand(o => { RemoveConfiguration(o); IsConfigMenuOpen = false; }, o => SelectedConfig != null);
            ApplyConfigCommand = new RelayCommand(ApplyConfiguration);

            FilterOutCommand = FilterVM.FilterOutCommand;
            FilterOutThreadCommand = FilterVM.FilterOutThreadCommand;
            OpenThreadFilterCommand = FilterVM.OpenThreadFilterCommand;
            OpenLoggerFilterCommand = FilterVM.OpenLoggerFilterCommand;
            OpenMethodFilterCommand = FilterVM.OpenMethodFilterCommand;
            FilterContextCommand = FilterVM.FilterContextCommand;
            UndoFilterOutCommand = FilterVM.UndoFilterOutCommand;

            ResetTimeFocusCommand = new RelayCommand(ResetTimeFocus);

            ViewLogDetailsCommand = new RelayCommand(ViewLogDetails);
            ToggleThemeCommand = new RelayCommand(o => IsDarkMode = !IsDarkMode);
            ToggleBoldCommand = new RelayCommand(o => IsBold = !IsBold);
            OpenSettingsCommand = new RelayCommand(OpenSettingsWindow);
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            OpenSnakeGameCommand = new RelayCommand(OpenSnakeGame);

            FilterToStateCommand = new RelayCommand(FilterToState);

            ZoomInCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == 4) ScreenshotZoom = Math.Min(5000, ScreenshotZoom + 100);
                else GridFontSize = Math.Min(30, GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == 4) ScreenshotZoom = Math.Max(100, ScreenshotZoom - 100);
                else GridFontSize = Math.Max(8, GridFontSize - 1);
            });

            LivePlayCommand = LiveVM.LivePlayCommand;
            LivePauseCommand = LiveVM.LivePauseCommand;
            LiveClearCommand = LiveVM.LiveClearCommand;

            AddAnnotationCommand = new RelayCommand(AddAnnotation);
            DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation);
            SaveCaseCommand = new RelayCommand(SaveCase);
            LoadCaseCommand = new RelayCommand(LoadCase);

            _isDarkMode = Properties.Settings.Default.IsDarkMode;
            ApplyTheme(_isDarkMode);
            LoadSavedConfigurations();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            // Save the currently selected log and its scroll position BEFORE toggling filter
            var savedSelectedLog = SelectedLog;
            if (savedSelectedLog != null)
            {
                SaveScrollPosition(savedSelectedLog);
            }

            ToggleFilterView(IsFilterActive);

            // Restore the selected log and scroll to it, preserving visual position
            // Use Dispatcher to ensure UI has fully updated before scrolling
            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }));
            }
        }

        private void InitializeVisualMode()
        {
            // Use filtered logs if time range is active, otherwise use all logs
            var logsToUse = FilterVM.IsGlobalTimeRangeActive ? Logs : (SessionVM.AllLogsCache ?? Logs);
            if (VisualTimelineVM != null)
            {
                VisualTimelineVM.LoadData(logsToUse.ToList(), Events);
            }
        }

        public void ProcessFiles(string[] filePaths, Action<LogSessionData> onLoadComplete = null)
            => SessionVM?.ProcessFiles(filePaths, onLoadComplete);


        // --- NEW ANNOTATION LOGIC ---

        private void ToggleAnnotation(object parameter)
        {
            System.Diagnostics.Debug.WriteLine($"[TOGGLE] Called with: {parameter?.GetType().Name}");

            if (parameter is LogEntry log)
            {
                System.Diagnostics.Debug.WriteLine($"[TOGGLE] Before: HasAnnotation={log.HasAnnotation}, IsExpanded={log.IsAnnotationExpanded}");

                if (log.HasAnnotation)
                {
                    log.IsAnnotationExpanded = !log.IsAnnotationExpanded;
                    System.Diagnostics.Debug.WriteLine($"[TOGGLE] After: IsExpanded={log.IsAnnotationExpanded}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TOGGLE] Log has no annotation!");
                }
            }
        }
        private void ToggleAllAnnotations(object obj)
        {
            IEnumerable<LogEntry> targetList = null;

            if (SelectedTabIndex == 2) // APP Tab
            {
                targetList = SessionVM?.AllAppLogsCache;
            }
            else // PLC Tab
            {
                targetList = SessionVM?.AllLogsCache;
            }

            if (targetList == null || !targetList.Any()) return;

            // Get only logs with annotations
            var logsWithAnnotations = targetList.Where(l => l.HasAnnotation).ToList();
            if (!logsWithAnnotations.Any()) return;

            // Check if any is expanded to determine direction
            bool anyExpanded = logsWithAnnotations.Any(l => l.IsAnnotationExpanded);
            bool newState = !anyExpanded;

            // Update all annotations
            foreach (var log in logsWithAnnotations)
            {
                log.IsAnnotationExpanded = newState;
            }

            ShowAllAnnotations = newState;
            StatusMessage = newState ? "All annotations expanded" : "All annotations collapsed";
        }

        private void CloseAnnotation(object parameter) => CaseVM?.CloseAnnotationCommand.Execute(parameter);

        private string LoadSqliteContent(byte[] dbBytes)
        {
            var sb = new System.Text.StringBuilder();
            string tempDbPath = null;

            try
            {
                tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, dbBytes);

                using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Read Only=True;"))
                {
                    connection.Open();
                    var tables = new List<string>();
                    using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { tables.Add(reader.GetString(0)); }
                    }

                    sb.AppendLine($"=== SQLite Database: {tables.Count} tables ===");
                    sb.AppendLine();

                    foreach (var tableName in tables)
                    {
                        sb.AppendLine($"━━━ TABLE: {tableName} ━━━");
                        using (var countCmd = new SQLiteCommand($"SELECT COUNT(*) FROM [{tableName}]", connection))
                        {
                            var count = countCmd.ExecuteScalar();
                            sb.AppendLine($"Rows: {count}");
                        }
                        using (var cmd = new SQLiteCommand($"SELECT * FROM [{tableName}] LIMIT 100", connection))
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
                    try { File.Delete(tempDbPath); } catch { }
                }
            }
            return sb.ToString();
        }

        private List<StateEntry> CalculateStatesInternal(IEnumerable<LogEntry> logs)
        {
            var statesList = new List<StateEntry>();
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();

            var transitionLogs = sortedLogs.Where(l => l.ThreadName != null &&
                                                 l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message != null &&
                                                 l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message.Contains("->"))
                                     .ToList();

            if (transitionLogs.Count == 0) return statesList;

            var failureEvents = sortedLogs
                .Where(l => l.ThreadName == "Events" &&
                           l.Message != null &&
                           l.Message.Contains("Enqueue event PLC_FAILURE_STATE_CHANGE from Main_PLC"))
                .Select(l => l.Date)
                .ToHashSet();

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
                    LogReference = currentLog,
                    Status = "OK",
                    StatusColor = Brushes.LightGreen
                };

                if (i < transitionLogs.Count - 1)
                    entry.EndTime = transitionLogs[i + 1].Date;
                else
                    entry.EndTime = logEndLimit;

                bool hasFailureEvent = failureEvents.Any(eventTime =>
                    eventTime >= entry.StartTime && eventTime <= (entry.EndTime ?? logEndLimit));

                if (hasFailureEvent)
                {
                    entry.Status = "FAILED";
                    entry.StatusColor = Brushes.Red;
                }
                else if (entry.StateName.Equals("GET_READY", StringComparison.OrdinalIgnoreCase))
                {
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLogParts = transitionLogs[i + 1].Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextLogParts.Length >= 2 && !nextLogParts[1].Trim().Equals("DYNAMIC_READY", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = "FAILED";
                            entry.StatusColor = Brushes.Red;
                        }
                    }
                }
                else if (entry.StateName.Equals("MECH_INIT", StringComparison.OrdinalIgnoreCase))
                {
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLogParts = transitionLogs[i + 1].Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextLogParts.Length >= 2 && !nextLogParts[1].Trim().Equals("STANDBY", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = "FAILED";
                            entry.StatusColor = Brushes.Red;
                        }
                    }
                }

                statesList.Add(entry);
            }
            // מיון מהישן לחדש (חדשים למטה)
            return statesList.OrderBy(s => s.StartTime).ToList();
        }

        public void StartBackgroundAnalysis(LogSessionData session)
        {
            IsAnalysisRunning = true;

            Task.Run(() =>
            {
                try
                {
                    session.CachedStates = CalculateStatesInternal(session.Logs);
                    session.CachedAnalysis = new UniversalStateFailureAnalyzer().Analyze(session);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Background Analysis Error: {ex.Message}");
                }
                finally
                {
                    IsAnalysisRunning = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SelectedSession == session)
                            StatusMessage = "Background Analysis Complete.";
                    });
                }
            });
        }

        private void LiveClear(object obj)
        {
            Debug.WriteLine("LiveClear called");
            IsRunning = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_collectionLock)
                {
                    if (SessionVM.AllLogsCache != null) SessionVM.AllLogsCache.Clear();
                    FilteredLogs?.Clear();
                    SelectedLog = null;
                }
            });

            if (IsLiveMode)
            {
                IsRunning = true;
                StatusMessage = "Cleared. Monitoring continues...";
                Debug.WriteLine("LiveClear: Cleared UI, monitoring continues");
            }
            else
            {
                StatusMessage = "Logs cleared.";
            }
        }

        private void ClearLogs(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] ========== CLEAR ALL STARTED ==========");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");

            try
            {
                // Step 1: Clear SessionVM (this clears logs, events, screenshots)
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 1: Calling SessionVM.ClearCommand...");
                SessionVM?.ClearCommand.Execute(null);

                // Step 2: Clear case management
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 2: Clearing case management...");
                CaseVM?.ClearMarkedLogs();
                if (CaseVM?.LogAnnotations != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Clearing {CaseVM.LogAnnotations.Count} annotations");
                    CaseVM.LogAnnotations.Clear();
                }

                // Step 3: Clear filters
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 3: Resetting filters...");
                if (FilterVM != null)
                {
                    FilterVM.IsMainFilterActive = false;
                    FilterVM.IsAppFilterActive = false;
                    FilterVM.IsMainFilterOutActive = false;
                    FilterVM.IsAppFilterOutActive = false;
                    FilterVM.IsAppTimeFocusActive = false;
                    FilterVM.IsTimeFocusActive = false;

                    if (FilterVM.LastFilteredAppCache != null)
                        FilterVM.LastFilteredAppCache.Clear();
                    if (FilterVM.LastFilteredCache != null)
                        FilterVM.LastFilteredCache.Clear();
                    if (FilterVM.NegativeFilters != null)
                        FilterVM.NegativeFilters.Clear();
                    if (FilterVM.ActiveThreadFilters != null)
                        FilterVM.ActiveThreadFilters.Clear();
                    if (FilterVM.ActiveLoggerFilters != null)
                        FilterVM.ActiveLoggerFilters.Clear();
                    if (FilterVM.ActiveMethodFilters != null)
                        FilterVM.ActiveMethodFilters.Clear();

                    // Note: FilteredLogs and AppDevLogsFiltered are already cleared by SessionVM
                    // This is just a backup to ensure they are cleared
                    System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Backup: Checking FilteredLogs: {FilteredLogs?.Count ?? 0} items");
                    System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Backup: Checking AppDevLogsFiltered: {AppDevLogsFiltered?.Count ?? 0} items");

                    System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Clearing LoggerTreeRoot: {LoggerTreeRoot?.Count ?? 0} items");
                    if (LoggerTreeRoot != null)
                    {
                        LoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(LoggerTreeRoot));
                    }

                    FilterVM.SearchText = "";
                    FilterVM.IsSearchPanelVisible = false;
                }

                // Step 4: Clear main collections (backup - SessionVM should have done this)
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 4: Clearing main collections...");
                Logs = new List<LogEntry>();
                OnPropertyChanged(nameof(Logs));

                // Step 5: Clear configuration
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 5: Clearing configuration...");
                ConfigVM?.ClearConfigurationFiles();

                // Notify UI about cleared config properties
                OnPropertyChanged(nameof(ConfigurationFiles));
                OnPropertyChanged(nameof(DbTreeNodes));
                OnPropertyChanged(nameof(SelectedConfigFile));
                OnPropertyChanged(nameof(ConfigFileContent));
                OnPropertyChanged(nameof(FilteredConfigContent));
                OnPropertyChanged(nameof(ConfigSearchText));
                OnPropertyChanged(nameof(IsDbFileSelected));

                // ⚠️ THIS IS THE CRITICAL PART - Clear text info ⚠️
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 6: Clearing text info (CRITICAL)...");

                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Clearing SetupInfo (was: {SetupInfo?.Length ?? 0} chars)");
                SetupInfo = "";
                OnPropertyChanged(nameof(SetupInfo));

                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Clearing PressConfig (was: {PressConfig?.Length ?? 0} chars)");
                PressConfig = "";
                OnPropertyChanged(nameof(PressConfig));

                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Clearing VersionsInfo (was: {VersionsInfo?.Length ?? 0} chars)");
                VersionsInfo = "";
                OnPropertyChanged(nameof(VersionsInfo));

                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Resetting WindowTitle");
                WindowTitle = "IndiLogs 3.0";
                OnPropertyChanged(nameof(WindowTitle));

                // Step 7: Reset UI state
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 7: Resetting UI state...");
                CurrentProgress = 0;
                ScreenshotZoom = 400;
                SelectedSession = null;
                SelectedLog = null;
                IsFilterOutActive = false;
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(IsFilterOutActive));

                // Step 8: Reset tree filters
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 8: Resetting tree filters...");
                ResetTreeFilters();

                // Step 9: Reset visual mode
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 9: Resetting visual mode...");
                if (VisualTimelineVM != null)
                    VisualTimelineVM.Clear();
                IsVisualMode = false;

                // Step 10: Reset tabs
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] Step 10: Resetting tabs...");
                SelectedTabIndex = 0;
                OnPropertyChanged(nameof(SelectedTabIndex));
                LeftTabIndex = 0;
                OnPropertyChanged(nameof(LeftTabIndex));

                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine("[MAIN VM CLEAR] ========== CLEAR ALL COMPLETED ==========");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");

                StatusMessage = "All data cleared successfully";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] ❌ ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MAIN VM CLEAR] Stack trace: {ex.StackTrace}");
                StatusMessage = $"Clear failed: {ex.Message}";
            }
        }
        private void OpenStatesWindow(object obj)
        {
            if (IsAnalysisRunning)
            {
                MessageBox.Show("Still analyzing data in background...\nPlease wait until the process finishes.",
                                "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSession == null) { MessageBox.Show("No logs loaded."); return; }

            if (SelectedSession.CachedStates != null && SelectedSession.CachedStates.Count > 0)
            {
                if (_statesWindow != null && _statesWindow.IsVisible) { WindowManager.ActivateWindow(_statesWindow); return; }

                _statesWindow = new StatesWindow(SelectedSession.CachedStates, this);
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                WindowManager.OpenWindow(_statesWindow);
            }
            else
            {
                MessageBox.Show("No states detected in this session.");
            }
        }

        private void ResetTimeFocus(object obj)
        {
            if (VisualTimelineVM != null)
            {
                VisualTimelineVM.ViewScale = 1.0;
                VisualTimelineVM.ViewOffset = 0;
                VisualTimelineVM.SelectedState = null;
            }

            FilterVM.IsTimeFocusActive = false;
            FilterVM.IsAppTimeFocusActive = false;
            FilterVM.LastFilteredCache?.Clear();
            FilterVM.LastFilteredAppCache = null;
            FilterVM.SavedFilterRoot = null;
            SearchText = string.Empty;
            FilterVM.IsMainFilterActive = false;
            FilterVM.IsAppFilterActive = false;
            FilterVM.IsMainFilterOutActive = false;
            FilterVM.IsAppFilterOutActive = false;
            FilterVM.ActiveThreadFilters.Clear();
            FilterVM.NegativeFilters.Clear();
            ResetTreeFilters();

            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterOutActive));

            UpdateMainLogsFilter(false);
            ApplyAppLogsFilter();

            InitializeVisualMode();

            StatusMessage = "Filter reset. Showing all data.";
        }

        private void RunAnalysis(object obj)
        {
            if (SelectedSession == null)
            {
                MessageBox.Show("No logs loaded.");
                return;
            }

            if (IsAnalysisRunning)
            {
                MessageBox.Show("Analysis is still running in the background.\nPlease wait a moment and try again.",
                                "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show analysis menu
            var menuWindow = new Views.AnalysisMenuWindow();
            menuWindow.Owner = Application.Current.MainWindow;

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
            System.Diagnostics.Debug.WriteLine("[ANALYSIS] ShowFailuresAnalysis called");

            if (_analysisWindow != null && _analysisWindow.IsVisible)
            {
                _analysisWindow.Activate();
                return;
            }

            if (SelectedSession.CachedAnalysis != null && SelectedSession.CachedAnalysis.Any())
            {
                OpenAnalysisWindow(SelectedSession.CachedAnalysis);
            }
            else
            {
                MessageBox.Show("Great news! No critical state failures were detected in this session.",
                                "Analysis Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowStatisticsAnalysis()
        {
            System.Diagnostics.Debug.WriteLine("[ANALYSIS] ShowStatisticsAnalysis called");

            // שליפת שתי הרשימות
            var plcLogs = SessionVM?.AllLogsCache;
            var appLogs = SessionVM?.AllAppLogsCache;

            // בדיקה אם יש בכלל נתונים להציג
            bool hasPlc = plcLogs != null && plcLogs.Any();
            bool hasApp = appLogs != null && appLogs.Any();

            if (!hasPlc && !hasApp)
            {
                MessageBox.Show("No logs available for analysis.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // יצירת החלון עם שני הפרמטרים וקולבק לפילטור
            var statsWindow = new Views.StatsWindow(plcLogs, appLogs, ApplyChartDrillDownFilter);
            statsWindow.Title = "Log Statistics Dashboard";
            WindowManager.OpenWindow(statsWindow);
        }

        private void ApplyChartDrillDownFilter(string filterType, string filterValue)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MAIN VM] Applying chart drill-down filter: {filterType} = {filterValue}");

                if (filterType == "Logger")
                {
                    // Filter by Logger field - search for the logger name in the message
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = 0;

                    int logCount = Logs?.Count() ?? 0;
                    MessageBox.Show($"Filter applied: Logger = {filterValue}\n\nShowing {logCount} matching logs.",
                        "Logger Filter Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (filterType == "State")
                {
                    // Filter by STATE - search for the state name
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = 0;

                    int logCount = Logs?.Count() ?? 0;
                    MessageBox.Show($"Filter applied: STATE = {filterValue}\n\nShowing {logCount} matching logs.",
                        "State Filter Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying filter: {ex.Message}", "Filter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FilterToState(object obj)
        {
            if (obj is StateEntry state)
            {
                IsBusy = true;
                StatusMessage = $"Focusing state: {state.StateName}...";

                Task.Run(() =>
                {
                    DateTime start = state.StartTime;
                    DateTime end = state.EndTime ?? DateTime.MaxValue;

                    if (SessionVM.AllLogsCache != null)
                    {
                        var timeSlice = SessionVM.AllLogsCache.Where(l => l.Date >= start && l.Date <= end).OrderByDescending(l => l.Date).ToList();
                        var smartFiltered = timeSlice.Where(l => IsDefaultLog(l)).ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            FilterVM.LastFilteredCache = timeSlice;
                            FilterVM.SavedFilterRoot = null;
                            FilterVM.IsTimeFocusActive = true;
                            FilterVM.IsMainFilterActive = true;
                            SelectedTabIndex = 0;
                            UpdateMainLogsFilter(true);
                            if (FilterVM?.FilteredLogs != null)
                            {
                                FilterVM.FilteredLogs.ReplaceAll(smartFiltered);
                                if (FilterVM.FilteredLogs.Count > 0) SelectedLog = FilterVM.FilteredLogs[0];
                            }
                            OnPropertyChanged(nameof(IsFilterActive));
                            StatusMessage = $"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";

                            if (IsVisualMode && VisualTimelineVM != null)
                            {
                                // Use filtered logs if time range is active
                                var logsForVisual = FilterVM.IsGlobalTimeRangeActive ? Logs : SessionVM.AllLogsCache.ToList();
                                VisualTimelineVM.LoadData(logsForVisual, Events);
                                VisualTimelineVM.FocusOnState(state.StateName);
                            }

                            IsBusy = false;
                        });
                    }
                    else
                    {
                        IsBusy = false;
                    }
                });
            }
        }
        private void FilterAppErrors(object obj)
        {
            if (SessionVM.AllAppLogsCache == null || !SessionVM.AllAppLogsCache.Any()) return;
            IsBusy = true;
            StatusMessage = "Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = SessionVM.AllAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilterVM?.AppDevLogsFiltered?.ReplaceAll(errors);
                    IsBusy = false;
                    StatusMessage = $"Showing {errors.Count} Errors";
                    FilterVM.IsAppFilterActive = true;
                    OnPropertyChanged(nameof(IsFilterActive));
                });
            });
        }
        private void OpenIndigoInvaders(object obj)
        {
            var invadersWindow = new IndiLogs_3._0.Views.IndigoInvadersWindow();
            invadersWindow.Owner = Application.Current.MainWindow;
            invadersWindow.ShowDialog();
        }

        private void BuildLoggerTree(IEnumerable<LogEntry> logs) => FilterVM?.BuildLoggerTree(logs);
                   
        public void ResetTreeFilters()
        {
            FilterVM.TreeHiddenLoggers.Clear();
            FilterVM.TreeHiddenPrefixes.Clear();
            FilterVM.TreeShowOnlyLogger = null;
            FilterVM.TreeShowOnlyPrefix = null;
            foreach (var node in LoggerTreeRoot) ResetVisualHiddenState(node);
        }
        private void ResetVisualHiddenState(LoggerNode node)
        {
            node.IsHidden = false;
            foreach (var child in node.Children) ResetVisualHiddenState(child);
        }
        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log)
            {
                WindowManager.OpenWindow(new LogDetailsWindow(log));
            }
        }
        public async void SortAppLogs(string sortBy, bool ascending)
        {
            if (AppDevLogsFiltered == null || AppDevLogsFiltered.Count == 0) return;
            IsBusy = true;
            StatusMessage = "Sorting...";
            await Task.Run(() =>
            {
                List<LogEntry> sorted = null;
                var source = AppDevLogsFiltered.ToList();
                switch (sortBy)
                {
                    case "Time": sorted = ascending ? source.OrderBy(x => x.Date).ToList() : source.OrderByDescending(x => x.Date).ToList(); break;
                    case "Level": sorted = ascending ? source.OrderBy(x => x.Level).ToList() : source.OrderByDescending(x => x.Level).ToList(); break;
                    case "Logger": sorted = ascending ? source.OrderBy(x => x.Logger).ToList() : source.OrderByDescending(x => x.Logger).ToList(); break;
                    case "Thread": sorted = ascending ? source.OrderBy(x => x.ThreadName).ToList() : source.OrderByDescending(x => x.ThreadName).ToList(); break;
                    default: sorted = source; break;
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppDevLogsFiltered.ReplaceAll(sorted);
                    IsBusy = false;
                    StatusMessage = "Sorted.";
                });
            });
        }

        private void ToggleFilterView(bool show) => FilterVM?.ToggleFilterView(show);
        private void UpdateMainLogsFilter(bool show) => FilterVM?.ApplyMainLogsFilter();
        private void ApplyAppLogsFilter() => FilterVM?.ApplyAppLogsFilter();

        private void StartLiveMonitoring(string path) => LiveVM?.StartLiveMonitoring(path);
        private void StopLiveMonitoring() => LiveVM?.StopLiveMonitoring();

        private void LivePlay(object obj) => LiveVM?.LivePlayCommand.Execute(obj);
        private void LivePause(object obj) => LiveVM?.LivePauseCommand.Execute(obj);
        private void LoadFile(object obj)
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true) ProcessFiles(dialog.FileNames);
        }
        private async void OpenFilterWindow(object obj)
        {
            var win = new FilterWindow();
            bool isAppTab = SelectedTabIndex == 2;
            var currentRoot = isAppTab ? FilterVM.AppFilterRoot : FilterVM.MainFilterRoot;

            if (currentRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(currentRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                IsBusy = true;
                await Task.Run(() =>
                {
                    if (isAppTab) FilterVM.AppFilterRoot = newRoot;
                    else
                    {
                        FilterVM.MainFilterRoot = newRoot;
                        if (hasAdvanced)
                        {
                            List<LogEntry> cacheCopy;
                            lock (_collectionLock)
                            {
                                cacheCopy = _allLogsCache.ToList();
                            }
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, FilterVM.MainFilterRoot)).ToList();
                            FilterVM.LastFilteredCache = res;
                        }
                        else FilterVM.LastFilteredCache.Clear();
                    }
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isAppTab) { FilterVM.IsAppFilterActive = hasAdvanced; ApplyAppLogsFilter(); }
                    else { FilterVM.IsMainFilterActive = hasAdvanced || FilterVM.ActiveThreadFilters.Any(); UpdateMainLogsFilter(FilterVM.IsMainFilterActive); }
                    OnPropertyChanged(nameof(IsFilterActive));
                    IsBusy = false;
                });
            }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node) => FilterVM?.EvaluateFilterNode(log, node) ?? true;
        private async void ExportParsedData(object obj)
        {
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any())
            {
                MessageBox.Show("No logs loaded.", "Info");
                return;
            }

            if (_exportConfigWindow != null && _exportConfigWindow.IsLoaded)
            {
                WindowManager.ActivateWindow(_exportConfigWindow);
                return;
            }

            _exportConfigWindow = new ExportConfigurationWindow();
            var viewModel = new ExportConfigurationViewModel(SelectedSession, _csvService);
            _exportConfigWindow.DataContext = viewModel;
            _exportConfigWindow.Closed += (s, e) => _exportConfigWindow = null;
            WindowManager.OpenWindow(_exportConfigWindow);
        }
        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results);
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            WindowManager.OpenWindow(_analysisWindow);
        }
        private void OpenSnakeGame(object obj)
        {
            var snakeWindow = new IndiLogs_3._0.Views.SnakeWindow();
            WindowManager.ShowDialog(snakeWindow);
        }
        private void LoadSavedConfigurations() => CaseVM?.LoadSavedConfigs();
        private void ApplyConfiguration(object parameter) { if (parameter is SavedConfiguration c) CaseVM?.ApplyConfiguration(c); }
        private void RemoveConfiguration(object parameter) => CaseVM?.DeleteConfigCommand.Execute(parameter);
        private void SaveConfiguration(object obj) => CaseVM?.SaveConfigCommand.Execute(obj);
        private void LoadConfigurationFromFile(object obj) => CaseVM?.LoadConfigCommand.Execute(obj);
        private void ApplyTheme(bool isDark)
        {
            var dict = Application.Current.Resources;
            if (isDark)
            {
                // Dark mode colors - deep navy blue theme (like reference image)
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(10, 18, 30)));    // #0A121E - very deep navy
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(15, 25, 40)));   // #0F1928 - dark navy panel
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(20, 35, 55)));    // #142337 - navy card
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(30, 50, 75))); // #1E324B - lighter navy hover

                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(220, 230, 240))); // Soft white-blue
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(140, 160, 180))); // Muted blue-gray
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(40, 60, 85))); // #283C55 - subtle blue border

                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 200, 220)));  // Teal/Cyan
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
            if (dict.Contains(key))
                dict.Remove(key);
            dict.Add(key, value);
        }

        private void MarkRow(object obj) => CaseVM?.MarkLogCommand.Execute(obj);
        private void GoToNextMarked(object obj) => CaseVM?.GoToNextMarkedCommand.Execute(obj);
        private void GoToPrevMarked(object obj) => CaseVM?.GoToPrevMarkedCommand.Execute(obj);
        private void JumpToLog(object obj) { if (obj is LogEntry log) { SelectedLog = log; RequestScrollToLog?.Invoke(log); } }
        private void OpenSettingsWindow(object obj)
        {
            var win = new SettingsWindow { DataContext = this };
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
                WindowManager.OpenWindow(win);
            }
        }
        private void OpenFontsWindow(object obj) { WindowManager.ShowDialog(new FontsWindow { DataContext = this }); }
        private void UpdateContentFont(string fontName) { if (!string.IsNullOrEmpty(fontName) && Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName)); }
        private void UpdateContentFontWeight(bool isBold)
        {
            if (Application.Current != null)
            {
                UpdateResource(Application.Current.Resources, "ContentFontWeight",
                    isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);
            }
        }

        private void OpenMarkedLogsWindow(object obj) => CaseVM?.OpenMarkedWindowCommand.Execute(obj);

        private void OpenGlobalGrepWindow()
        {
            // יצירת אוסף ריק במידה ולא נטענו סשנים, כדי לאפשר לחלון להיפתח
            var sessions = LoadedSessions ?? new ObservableCollection<LogSessionData>();

            var viewModel = new GlobalGrepViewModel(sessions);

            // אם אין קבצים טעונים, נגדיר את ברירת המחדל לחיפוש חיצוני
            if (!sessions.Any())
            {
                viewModel.SearchMode = GlobalGrepViewModel.SearchModeType.ExternalFiles;
            }

            var window = new GlobalGrepWindow(viewModel, NavigateToGrepResult, LoadMultipleFiles);
            WindowManager.OpenWindow(window);
        }

        private void OpenComparisonWindow()
        {
            var comparisonWindow = WindowManager.GetOrCreate<Views.ComparisonWindow>(
                () => new Views.ComparisonWindow(new LogComparisonViewModel(
                    SessionVM.AllLogsCache,
                    SessionVM.AllAppLogsCache,
                    this
                )),
                Application.Current.MainWindow
            );
        }

        private async void OpenStripeAnalysisWindow()
        {
            var logs = FilterVM?.AppDevLogsFiltered?.ToList();

            if (logs == null || !logs.Any())
            {
                MessageBox.Show(
                    "No APP logs loaded.\n\nPlease load a session with APP logs first, or switch to the APP tab.",
                    "Stripe Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Quick pre-check: do we have any stripe data?
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
            WindowManager.OpenWindow(window);

            // Load data asynchronously after window is shown
            await Task.Run(() => { }).ContinueWith(_ =>
            {
                window.LoadFromLogs(logs);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void NavigateToGrepResult(GrepResult result)
        {
            if (result == null) return;

            // If we have a direct reference to the log entry (in-memory search)
            if (result.ReferencedLogEntry != null && result.SessionIndex >= 0)
            {
                // Navigate to the loaded session
                if (result.SessionIndex < LoadedSessions.Count)
                {
                    SelectedSession = LoadedSessions[result.SessionIndex];

                    // Switch to the appropriate tab (0 for PLC, 2 for APP)
                    SelectedTabIndex = (result.LogType == "APP") ? 2 : 0;

                    // Wait for UI to update, then scroll to the log entry
                    Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => RequestScrollToLog?.Invoke(result.ReferencedLogEntry)));
                }
                return;
            }

            // If we don't have a direct reference (external file search)
            if (string.IsNullOrEmpty(result.FilePath)) return;

            // Check if the file is already loaded
            var session = LoadedSessions.FirstOrDefault(s => s.FilePath == result.FilePath);

            if (session != null)
            {
                SelectedSession = session;
                JumpByTime(result, session);
            }
            else
            {
                // Load the file if not already loaded
                ProcessFiles(new[] { result.FilePath }, (loadedSession) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SelectedSession = loadedSession;
                        JumpByTime(result, loadedSession);
                    });
                });
            }
        }

        private void JumpByTime(GrepResult result, LogSessionData session)
        {
            // Switch to the appropriate tab (0 for PLC, 2 for APP)
            SelectedTabIndex = (result.LogType == "APP") ? 2 : 0;

            // Get the appropriate log collection
            var logs = (result.LogType == "APP") ? session.AppDevLogs : session.Logs;

            // Find the exact log entry by Timestamp and Message
            var target = logs?.FirstOrDefault(l =>
                l.Date == result.Timestamp &&
                l.Message == result.ReferencedLogEntry?.Message &&
                l.ThreadName == result.ReferencedLogEntry?.ThreadName)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp && l.Message == result.ReferencedLogEntry?.Message)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp);

            if (target != null)
            {
                // Wait for UI to update, then scroll to the log entry
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => RequestScrollToLog?.Invoke(target)));
            }
        }

        private void LoadMultipleFiles(List<(string FilePath, string SessionName)> fileList)
        {
            if (fileList == null || fileList.Count == 0) return;

            // Get list of already loaded files
            var loadedFilePaths = LoadedSessions.Select(s => s.FilePath).ToList();

            // Show file selection window
            var fileSelectionWindow = new Views.FileSelectionWindow(fileList, loadedFilePaths);
            fileSelectionWindow.Owner = Application.Current.MainWindow;

            if (fileSelectionWindow.ShowDialog() == true)
            {
                var filesToLoad = fileSelectionWindow.FilesToLoad;

                if (filesToLoad != null && filesToLoad.Count > 0)
                {
                    // Load all files using ProcessFiles
                    ProcessFiles(filesToLoad.ToArray(), null);

                    MessageBox.Show($"Loaded {filesToLoad.Count} file(s).", "Open All Files", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private bool IsDefaultLog(LogEntry l) => FilterVM?.IsDefaultLog(l) ?? false;
        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        private void OpenKibana(object obj) { }

        private void CopyTableName(object parameter)
        {
            if (parameter is DbTreeNode node && !string.IsNullOrEmpty(node.Name))
            {
                try
                {
                    Clipboard.SetText(node.Name);
                }
                catch { }
            }
        }
        public void OnFilesDropped(string[] files) { if (files != null && files.Length > 0) ProcessFiles(files); }

        public LogAnnotation GetAnnotation(LogEntry log) => CaseVM?.GetAnnotation(log);
        private void AddAnnotation(object parameter) => CaseVM?.AddAnnotationCommand.Execute(parameter);
        private void DeleteAnnotation(object parameter) => CaseVM?.DeleteAnnotationCommand.Execute(parameter);
        private void SaveCase(object parameter) => CaseVM?.SaveCaseCommand.Execute(parameter);
        private void LoadCase(object parameter) => CaseVM?.LoadCaseCommand.Execute(parameter);

        // ==================== TIME-SYNC SCROLLING METHODS ====================

        private int LinearSearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[LINEAR SEARCH] Collection is null or empty");
                return -1;
            }

            System.Diagnostics.Debug.WriteLine($"[LINEAR SEARCH] Searching {collection.Count} entries for {targetTime:HH:mm:ss.fff}");

            int nearestIndex = 0;
            TimeSpan minDiff = (collection[0].Date - targetTime).Duration();

            for (int i = 1; i < collection.Count; i++)
            {
                TimeSpan currentDiff = (collection[i].Date - targetTime).Duration();
                if (currentDiff < minDiff)
                {
                    minDiff = currentDiff;
                    nearestIndex = i;
                }
                if (minDiff.TotalMilliseconds < 1)
                    break;
            }

            System.Diagnostics.Debug.WriteLine($"[LINEAR SEARCH] Found nearest at index {nearestIndex}: {collection[nearestIndex].Date:HH:mm:ss.fff} (diff: {minDiff.TotalSeconds:F3}s)");

            return nearestIndex;
        }

        private int BinarySearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0) return -1;

            int left = 0;
            int right = collection.Count - 1;

            if (targetTime <= collection[0].Date) return 0;
            if (targetTime >= collection[right].Date) return right;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = collection[mid].Date;

                if (midTime == targetTime) return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            if (left >= collection.Count) return right;

            DateTime leftTime = collection[left].Date;
            DateTime rightTime = collection[right].Date;

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff < rightDiff ? left : right;
        }

        public void RequestSyncScroll(DateTime targetTime, string sourceGrid)
        {
            if (!IsTimeSyncEnabled || _isSyncScrolling) return;

            _isSyncScrolling = true;

            try
            {
                DateTime adjustedTime = targetTime.AddSeconds(TimeSyncOffsetSeconds);
                IList<LogEntry> targetCollection = null;
                string targetGrid = null;

                if (sourceGrid == "PLC" || sourceGrid == "PLCFiltered")
                {
                    if (AppDevLogsFiltered != null && AppDevLogsFiltered.Count > 0)
                    {
                        targetCollection = AppDevLogsFiltered;
                        targetGrid = "APP";
                    }
                }
                else if (sourceGrid == "APP")
                {
                    if (FilteredLogs != null && FilteredLogs.Count > 0 && SelectedTabIndex == 1)
                    {
                        targetCollection = FilteredLogs;
                        targetGrid = "PLCFiltered";
                    }
                    else if (Logs != null && (Logs as IList<LogEntry>)?.Count > 0)
                    {
                        targetCollection = Logs as IList<LogEntry>;
                        targetGrid = "PLC";
                    }
                }

                if (targetCollection == null || targetCollection.Count == 0) return;

                int nearestIndex = LinearSearchNearest(targetCollection, adjustedTime);

                if (nearestIndex >= 0)
                {
                    LogEntry nearestLog = targetCollection[nearestIndex];
                    TimeSpan timeDiff = (nearestLog.Date - adjustedTime).Duration();

                    if (timeDiff.TotalSeconds <= 60)
                    {
                        RequestScrollToLog?.Invoke(nearestLog);
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"🔗 Synced {sourceGrid} ↔ {targetGrid} (±{timeDiff.TotalSeconds:F1}s)";
                        });
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"⚠ No correlated logs within 60s (closest: {timeDiff.TotalSeconds:F0}s)";
                        });
                    }
                }
            }
            finally
            {
                _isSyncScrolling = false;
            }
        }

        /// <summary>
        /// Navigate to a log entry by time (called from Charts when user clicks on a point)
        /// </summary>
        public void NavigateToLogTime(DateTime time)
        {
            if (FilteredLogs == null || FilteredLogs.Count == 0) return;

            // Find the nearest log entry by time
            var nearestLog = FilteredLogs
                .OrderBy(l => Math.Abs((l.Date - time).TotalMilliseconds))
                .FirstOrDefault();

            if (nearestLog != null)
            {
                // Request the UI to scroll to this log
                RequestScrollToLog?.Invoke(nearestLog);
            }
        }

        /// <summary>
        /// Sync chart cursor when a log entry is selected (called from DataGrid selection)
        /// </summary>
        public void OnLogEntrySelected(LogEntry entry)
        {
            if (entry != null && ChartVM?.HasData == true)
            {
                ChartVM.SyncToLogTime(entry.Date);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void NotifyPropertyChanged(string propertyName) => OnPropertyChanged(propertyName);
    }
}