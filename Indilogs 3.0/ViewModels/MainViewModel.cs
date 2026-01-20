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

        public bool IsSearchPanelVisible
        {
            get => FilterVM?.IsSearchPanelVisible ?? false;
            set { if (FilterVM != null) FilterVM.IsSearchPanelVisible = value; }
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
        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };


        public event Action<LogEntry> RequestScrollToLog;

        /// <summary>
        /// Public method to trigger scroll to log event from child ViewModels
        /// </summary>
        public void ScrollToLog(LogEntry log)
        {
            RequestScrollToLog?.Invoke(log);
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
                if (SelectedTabIndex == 2)
                {
                    if (FilterVM != null && FilterVM.IsAppFilterActive != value)
                    {
                        FilterVM.IsAppFilterActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (FilterVM != null && FilterVM.IsMainFilterActive != value)
                    {
                        FilterVM.IsMainFilterActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(value);
                    }
                }
            }
        }

        public bool IsFilterOutActive
        {
            get => SelectedTabIndex == 2 ? IsAppFilterOutActive : IsMainFilterOutActive;
            set
            {
                if (SelectedTabIndex == 2)
                {
                    if (FilterVM != null && FilterVM.IsAppFilterOutActive != value)
                    {
                        FilterVM.IsAppFilterOutActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (FilterVM != null && FilterVM.IsMainFilterOutActive != value)
                    {
                        FilterVM.IsMainFilterOutActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(FilterVM.IsMainFilterActive);
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

        public ICommand AddAnnotationCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }
        public ICommand SaveCaseCommand { get; }
        public ICommand LoadCaseCommand { get; }

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
            OpenIndigoInvadersCommand = new RelayCommand(OpenIndigoInvaders);

            _allLogsCache = SessionVM.AllLogsCache;
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            ToggleExplorerMenuCommand = new RelayCommand(o => IsExplorerMenuOpen = !IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => IsConfigMenuOpen = !IsConfigMenuOpen);
            ToggleTimeSyncCommand = new RelayCommand(o => IsTimeSyncEnabled = !IsTimeSyncEnabled);
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
            ToggleFilterView(IsFilterActive);
        }

        private void InitializeVisualMode()
        {
            var logsToUse = SessionVM.AllLogsCache ?? Logs;
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
            return statesList.OrderByDescending(s => s.StartTime).ToList();
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
            CaseVM?.ClearMarkedLogs();
            FilterVM.IsMainFilterActive = false; FilterVM.IsAppFilterActive = false;
            FilterVM.IsMainFilterOutActive = false; FilterVM.IsAppFilterOutActive = false;
            FilterVM.IsAppTimeFocusActive = false; FilterVM.LastFilteredAppCache = null;
            FilterVM.IsTimeFocusActive = false;
            SessionVM.AllLogsCache?.Clear();
            if (FilterVM.LastFilteredCache != null) FilterVM.LastFilteredCache.Clear();
            FilterVM.NegativeFilters.Clear();
            FilterVM.ActiveThreadFilters.Clear();
            Logs = new List<LogEntry>(); FilteredLogs?.Clear(); AppDevLogsFiltered?.Clear();
            LoggerTreeRoot?.Clear(); Events.Clear(); Screenshots.Clear();
            LoadedFiles.Clear(); CurrentProgress = 0; SetupInfo = ""; PressConfig = ""; ScreenshotZoom = 400;
            IsFilterOutActive = false; LoadedSessions.Clear(); SelectedSession = null;
            SessionVM.AllAppLogsCache = null;
            ResetTreeFilters();
            ConfigVM.ClearConfigurationFiles();
            VisualTimelineVM?.Clear();
            IsVisualMode = false;
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
                if (_statesWindow != null && _statesWindow.IsVisible) { _statesWindow.Activate(); return; }

                _statesWindow = new StatesWindow(SelectedSession.CachedStates, this);
                _statesWindow.Owner = Application.Current.MainWindow;
                _statesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                _statesWindow.Show();
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
            FilterVM.LastFilteredCache.Clear();
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
                                VisualTimelineVM.LoadData(SessionVM.AllLogsCache.ToList(), Events);
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
        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                FilterVM.TreeShowOnlyLogger = null;
                FilterVM.TreeShowOnlyPrefix = null;
                FilterVM.TreeHiddenLoggers.Remove(node.FullPath);
                node.IsHidden = false;
                FilterVM.IsAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                FilterVM.TreeShowOnlyLogger = null;
                FilterVM.TreeShowOnlyPrefix = null;
                FilterVM.TreeHiddenLoggers.Add(node.FullPath);
                node.IsHidden = true;
                FilterVM.IsAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                FilterVM.TreeShowOnlyLogger = node.FullPath;
                FilterVM.IsAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                FilterVM.TreeShowOnlyPrefix = node.FullPath;
                FilterVM.IsAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                FilterVM.TreeShowOnlyLogger = null;
                FilterVM.TreeShowOnlyPrefix = null;
                FilterVM.TreeHiddenPrefixes.Add(node.FullPath);
                node.IsHidden = true;
                FilterVM.IsAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowAll(object obj)
        {
            ResetTreeFilters();
            FilterVM.IsAppFilterActive = false;
            OnPropertyChanged(nameof(IsFilterActive));
            ToggleFilterView(false);
        }
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
                new LogDetailsWindow(log).Show();
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
                _exportConfigWindow.Activate();
                _exportConfigWindow.Focus();
                return;
            }

            _exportConfigWindow = new ExportConfigurationWindow();
            var viewModel = new ExportConfigurationViewModel(SelectedSession, _csvService);
            _exportConfigWindow.DataContext = viewModel;
            _exportConfigWindow.Owner = Application.Current.MainWindow;

            _exportConfigWindow.Closed += (s, e) => _exportConfigWindow = null;
            _exportConfigWindow.Show();
        }
        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results);
            _analysisWindow.Owner = Application.Current.MainWindow;
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            _analysisWindow.Show();
        }
        private void OpenSnakeGame(object obj)
        {
            var snakeWindow = new IndiLogs_3._0.Views.SnakeWindow();
            snakeWindow.Owner = Application.Current.MainWindow;
            snakeWindow.ShowDialog();
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
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(18, 18, 18)));
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(30, 30, 36)));
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(37, 37, 45)));
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(45, 45, 54)));

                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Colors.White));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(176, 176, 176)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(51, 51, 51)));

                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 229, 255)));
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
            if (Application.Current.MainWindow != null && Application.Current.MainWindow != win)
                win.Owner = Application.Current.MainWindow;
            if (obj is FrameworkElement button)
            {
                Point buttonPosition = button.PointToScreen(new Point(0, 0));
                win.Left = buttonPosition.X;
                win.Top = buttonPosition.Y + button.ActualHeight;
            }
            else
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            win.Show();
        }
        private void OpenFontsWindow(object obj) { new FontsWindow { DataContext = this }.ShowDialog(); }
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void NotifyPropertyChanged(string propertyName) => OnPropertyChanged(propertyName);
    }
}