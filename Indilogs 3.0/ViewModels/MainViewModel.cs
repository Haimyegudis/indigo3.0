using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Views;
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

    
        // --- Properties & Fields ---
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
        public ICommand ToggleVisualModeCommand { get; }
        private FileSystemWatcher _fileWatcher = null;
        private bool _isBackgroundLoadingActive = false;
        private bool _isRefreshActive = false;
        private DateTime _lastFileCheckTime = DateTime.MinValue;
        private long _lastFileSize = 0;
        private long _lastStreamPosition = 0;  // ← NEW: Save stream position!
        private const int MIN_REFRESH_INTERVAL_MS = 5000;
        public ObservableCollection<LogEntry> MarkedAppLogs { get; set; }
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;
        private readonly CsvExportService _csvService;
        private bool _isPollingActive = false;
        // Windows Instances
        private StatesWindow _statesWindow;
        private AnalysisReportWindow _analysisWindow;
        private MarkedLogsWindow _markedMainLogsWindow;
        private MarkedLogsWindow _markedAppLogsWindow;
        private MarkedLogsWindow _combinedMarkedWindow;
        private bool _isAnalysisRunning;
        private ExportConfigurationWindow _exportConfigWindow = null;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set { _isAnalysisRunning = value; OnPropertyChanged(); }
        }
        // State Flags
        private bool _isMainFilterActive;
        private bool _isAppFilterActive;
        private bool _isMainFilterOutActive;
        private bool _isAppFilterOutActive;
        private bool _isAppTimeFocusActive;
        private bool _isTimeFocusActive = false;

        // Timers
        private DispatcherTimer _searchDebounceTimer;

        // Caches
        private ObservableRangeCollection<LogEntry> _liveLogsCollection;
        private IList<LogEntry> _allLogsCache;
        private IList<LogEntry> _allAppLogsCache;
        private IList<LogEntry> _lastFilteredCache = new List<LogEntry>();
        private List<LogEntry> _lastFilteredAppCache;

        // Filters
        private List<string> _negativeFilters = new List<string>();
        private List<string> _activeThreadFilters = new List<string>();
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();
        private FilterNode _savedFilterRoot = null;

        private FilterNode _mainFilterRoot = null;
        private FilterNode _appFilterRoot = null;
        private List<ColoringCondition> _mainColoringRules = new List<ColoringCondition>();
        private List<ColoringCondition> _appColoringRules = new List<ColoringCondition>();

        // Tree Filter State
        private LoggerNode _selectedTreeItem;
        public LoggerNode SelectedTreeItem
        {
            get => _selectedTreeItem;
            set { _selectedTreeItem = value; OnPropertyChanged(); }
        }

        private HashSet<string> _treeHiddenLoggers = new HashSet<string>();
        private HashSet<string> _treeHiddenPrefixes = new HashSet<string>();
        private string _treeShowOnlyLogger = null;
        private string _treeShowOnlyPrefix = null;
        private string _liveFilePath;
        // Live Monitoring - Load last 2 minutes initially, then update cache every 5 seconds
        private const int UI_UPDATE_BATCH_SIZE = 500; // Show 500 logs at a time
        private const int POLLING_READ_BYTES = 5 * 1024 * 1024; // Read 5MB on each poll to ensure complete logs
        private CancellationTokenSource _liveCts;
        private CustomLiveLogReader _customReader;
        private int _lastParsedLogCount = 0; // Track number of logs we've already shown
        private const int POLLING_INTERVAL_MS = 5000; // Check every 5 seconds
        private const int INITIAL_LOAD_MINUTES = 2; // Load last 2 minutes initially
        private readonly object _collectionLock = new object();
        // Collections
        private IEnumerable<LogEntry> _logs;
        public IEnumerable<LogEntry> Logs
        {
            get => _logs;
            set { _logs = value; OnPropertyChanged(); }
        }
        public ObservableRangeCollection<LogEntry> FilteredLogs { get; set; }

        private ObservableRangeCollection<LogEntry> _appDevLogsFiltered;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered
        {
            get => _appDevLogsFiltered;
            set { _appDevLogsFiltered = value; OnPropertyChanged(); }
        }

        public ObservableCollection<LoggerNode> LoggerTreeRoot { get; set; }
        public ObservableCollection<EventEntry> Events { get; set; }
        public ObservableCollection<BitmapImage> Screenshots { get; set; }
        public ObservableCollection<string> LoadedFiles { get; set; }
        public ObservableCollection<LogSessionData> LoadedSessions { get; set; }
        public ObservableCollection<SavedConfiguration> SavedConfigs { get; set; }
        public ObservableCollection<LogEntry> MarkedLogs { get; set; }
        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };

        // --- NEW CONFIG & DB PROPERTIES ---
        public ObservableCollection<string> ConfigurationFiles { get; set; } = new ObservableCollection<string>();
        private Dictionary<string, string> _configFilesPathMap = new Dictionary<string, string>();

        private string _selectedConfigFile;
        public string SelectedConfigFile
        {
            get => _selectedConfigFile;
            set
            {
                if (_selectedConfigFile != value)
                {
                    _selectedConfigFile = value;
                    OnPropertyChanged();
                    LoadSelectedFileContent();
                }
            }
        }

        private string _configFileContent;
        public string ConfigFileContent
        {
            get => _configFileContent;
            set { _configFileContent = value; OnPropertyChanged(); }
        }

        // Tree view for SQLite databases
        private ObservableCollection<DbTreeNode> _dbTreeNodes = new ObservableCollection<DbTreeNode>();
        public ObservableCollection<DbTreeNode> DbTreeNodes
        {
            get => _dbTreeNodes;
            set { _dbTreeNodes = value; OnPropertyChanged(); }
        }

        private bool _isDbFileSelected;
        public bool IsDbFileSelected
        {
            get => _isDbFileSelected;
            set { _isDbFileSelected = value; OnPropertyChanged(); }
        }

        // Search in config tab
        private string _configSearchText = "";
        public string ConfigSearchText
        {
            get => _configSearchText;
            set
            {
                if (_configSearchText != value)
                {
                    _configSearchText = value;
                    OnPropertyChanged();
                    FilterConfigContent();
                }
            }
        }

        private ObservableCollection<DbTreeNode> _allDbTreeNodes = new ObservableCollection<DbTreeNode>();
        // ----------------------------------

        public event Action<LogEntry> RequestScrollToLog;

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
                }
            }
        }

        // PLC tab is index 0 (PLC LOGS) or 1 (PLC FILTERED)
        public bool IsPLCTabSelected => _selectedTabIndex == 0 || _selectedTabIndex == 1;

        private LogSessionData _selectedSession;
        public LogSessionData SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession != value)
                {
                    _selectedSession = value;
                    OnPropertyChanged();
                    SwitchToSession(_selectedSession);

                }
            }
        }
        private bool _isMarkedLogsCombined;
        public bool IsMarkedLogsCombined
        {
            get => _isMarkedLogsCombined;
            set
            {
                if (_isMarkedLogsCombined != value)
                {
                    _isMarkedLogsCombined = value;
                    OnPropertyChanged();
                    CloseAllMarkedWindows();
                }
            }
        }

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

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private double _currentProgress;
        public double CurrentProgress
        {
            get => _currentProgress;
            set { _currentProgress = value; OnPropertyChanged(); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private LogEntry _selectedLog;
        public LogEntry SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); }
        }

        private SavedConfiguration _selectedConfig;
        public SavedConfiguration SelectedConfig
        {
            get => _selectedConfig;
            set { _selectedConfig = value; OnPropertyChanged(); }
        }

        private bool _isSearchPanelVisible;
        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            set
            {
                _isSearchPanelVisible = value;
                OnPropertyChanged();
                if (!value) SearchText = string.Empty;
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ValidateSearchSyntax();
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
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

            // Only validate if it has boolean operators
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
            get => SelectedTabIndex == 2 ? _isAppFilterActive : _isMainFilterActive;
            set
            {
                if (SelectedTabIndex == 2)
                {
                    if (_isAppFilterActive != value)
                    {
                        _isAppFilterActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (_isMainFilterActive != value)
                    {
                        _isMainFilterActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(_isMainFilterActive);
                    }
                }
            }
        }

        public bool IsFilterOutActive
        {
            get => SelectedTabIndex == 2 ? _isAppFilterOutActive : _isMainFilterOutActive;
            set
            {
                if (SelectedTabIndex == 2)
                {
                    if (_isAppFilterOutActive != value)
                    {
                        _isAppFilterOutActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (_isMainFilterOutActive != value)
                    {
                        _isMainFilterOutActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(_isMainFilterActive);
                    }
                }
            }
        }

        private bool _isLiveMode;
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set { _isLiveMode = value; OnPropertyChanged(); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPaused)); }
        }
        public bool IsPaused => !IsRunning;

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

        private bool _isExplorerMenuOpen;
        public bool IsExplorerMenuOpen
        {
            get => _isExplorerMenuOpen;
            set { _isExplorerMenuOpen = value; OnPropertyChanged(); }
        }

        private bool _isConfigMenuOpen;
        public bool IsConfigMenuOpen
        {
            get => _isConfigMenuOpen;
            set { _isConfigMenuOpen = value; OnPropertyChanged(); }
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

        public MainViewModel()
        {
            _csvService = new CsvExportService();
            _logService = new LogFileService();
            _coloringService = new LogColoringService();

            ToggleVisualModeCommand = new RelayCommand(o => IsVisualMode = !IsVisualMode);

            TreeShowThisCommand = new RelayCommand(ExecuteTreeShowThis);
            TreeHideThisCommand = new RelayCommand(ExecuteTreeHideThis);
            TreeShowOnlyThisCommand = new RelayCommand(ExecuteTreeShowOnlyThis);
            TreeShowWithChildrenCommand = new RelayCommand(ExecuteTreeShowWithChildren);
            TreeHideWithChildrenCommand = new RelayCommand(ExecuteTreeHideWithChildren);
            TreeShowAllCommand = new RelayCommand(ExecuteTreeShowAll);
            OpenIndigoInvadersCommand = new RelayCommand(OpenIndigoInvaders);

            _allLogsCache = new List<LogEntry>();
            Logs = new List<LogEntry>();
            LoadedSessions = new ObservableCollection<LogSessionData>();
            FilteredLogs = new ObservableRangeCollection<LogEntry>();
            AppDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            LoggerTreeRoot = new ObservableCollection<LoggerNode>();
            Events = new ObservableCollection<EventEntry>();
            Screenshots = new ObservableCollection<BitmapImage>();
            LoadedFiles = new ObservableCollection<string>();
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            MarkedAppLogs = new ObservableCollection<LogEntry>();
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            ToggleExplorerMenuCommand = new RelayCommand(o => IsExplorerMenuOpen = !IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => IsConfigMenuOpen = !IsConfigMenuOpen);

            LoadCommand = new RelayCommand(LoadFile);
            ClearCommand = new RelayCommand(o => { ClearLogs(o); IsExplorerMenuOpen = false; });
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

            ToggleSearchCommand = new RelayCommand(o => { IsSearchPanelVisible = !IsSearchPanelVisible; });
            CloseSearchCommand = new RelayCommand(o => { IsSearchPanelVisible = false; SearchText = ""; });

            OpenFilterWindowCommand = new RelayCommand(OpenFilterWindow);
            OpenColoringWindowCommand = new RelayCommand(OpenColoringWindow);

            SaveConfigCommand = new RelayCommand(o => { SaveConfiguration(o); IsConfigMenuOpen = false; });
            LoadConfigCommand = new RelayCommand(o => { LoadConfigurationFromFile(o); IsConfigMenuOpen = false; });
            RemoveConfigCommand = new RelayCommand(o => { RemoveConfiguration(o); IsConfigMenuOpen = false; }, o => SelectedConfig != null);
            ApplyConfigCommand = new RelayCommand(ApplyConfiguration);

            FilterOutCommand = new RelayCommand(FilterOut);
            FilterOutThreadCommand = new RelayCommand(FilterOutThread);
            OpenThreadFilterCommand = new RelayCommand(OpenThreadFilter);
            FilterContextCommand = new RelayCommand(FilterContext);
            UndoFilterOutCommand = new RelayCommand(UndoFilterOut);

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

            LivePlayCommand = new RelayCommand(LivePlay);
            LivePauseCommand = new RelayCommand(LivePause);
            LiveClearCommand = new RelayCommand(LiveClear);

            _isDarkMode = Properties.Settings.Default.IsDarkMode;
            ApplyTheme(_isDarkMode);
            LoadSavedConfigurations();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ToggleFilterView(IsFilterActive);
        }

        private void InitializeVisualMode()
        {
            var logsToUse = _allLogsCache ?? Logs;
            if (VisualTimelineVM != null)
            {
                VisualTimelineVM.LoadData(logsToUse.ToList(), Events);
            }
        }
        private async void ProcessFiles(string[] filePaths)
        {
            // Check if this is a live log file (active file being written to)
            if (filePaths.Length == 1 && File.Exists(filePaths[0]))
            {
                var filePath = filePaths[0];
                var fileName = Path.GetFileName(filePath);
                var ext = Path.GetExtension(filePath).ToLower();

                // Detect live log files: .log or .file extension, or specific patterns like "no-sn.engineGroupA.file"
                if (ext == ".log" || ext == ".file" ||
                    fileName.IndexOf("engineGroup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fileName.IndexOf("no-sn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Check if file is actively being written (might grow)
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // If we can open with ReadWrite sharing, it's likely a live file
                            // Start live monitoring instead of loading as static file
                            StartLiveMonitoring(filePath);
                            return;
                        }
                    }
                    catch
                    {
                        // If file is locked or not accessible, treat as static file
                    }
                }
            }

            IsBusy = true;
            StatusMessage = "Processing files...";

            try
            {
                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                var newSession = await _logService.LoadSessionAsync(filePaths, progress);

                newSession.FileName = System.IO.Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1) newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                StatusMessage = "Applying Colors...";
                await _coloringService.ApplyDefaultColorsAsync(newSession.Logs, false);
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    await _coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true);

                LoadedSessions.Add(newSession);
                SelectedSession = newSession;

                // Configuration files are loaded from ZIP in SwitchToSession() via SelectedSession setter
                // No need to call LoadConfigurationFiles here - it would clear ZIP-loaded files

                CurrentProgress = 100;
                StatusMessage = "Logs Loaded. Running Analysis in Background...";
                IsBusy = false;

                StartBackgroundAnalysis(newSession);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false;
            }
        }

        // --- NEW CONFIG METHODS ---
        private void LoadConfigurationFiles(string rootPath)
        {
            ConfigurationFiles.Clear();
            _configFilesPathMap.Clear();
            ConfigFileContent = string.Empty;

            try
            {
                string configDir = Path.Combine(rootPath, "Configuration");
                // Fallback check if rootPath IS the config dir or similar structure
                if (!Directory.Exists(configDir))
                {
                    // Check if we are inside a subfolder and need to go up
                    var parent = Directory.GetParent(rootPath);
                    if (parent != null)
                    {
                        var siblingConfig = Path.Combine(parent.FullName, "Configuration");
                        if (Directory.Exists(siblingConfig)) configDir = siblingConfig;
                    }
                }

                if (Directory.Exists(configDir))
                {
                    var files = Directory.GetFiles(configDir, "*.*")
                                         .Where(s => s.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".db", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        ConfigurationFiles.Add(fileName);
                        _configFilesPathMap[fileName] = file;
                    }

                    if (ConfigurationFiles.Count > 0)
                        SelectedConfigFile = ConfigurationFiles[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading config files: {ex.Message}";
            }
        }

        private void LoadSelectedFileContent()
        {
            ConfigSearchText = ""; // Reset search when changing files

            if (string.IsNullOrEmpty(SelectedConfigFile) || SelectedSession == null)
            {
                ConfigFileContent = "";
                IsDbFileSelected = false;
                DbTreeNodes.Clear();
                return;
            }

            try
            {
                // Check if this is a SQLite database file
                if (SelectedConfigFile.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    IsDbFileSelected = true;
                    ConfigFileContent = ""; // Clear text content for DB files

                    if (SelectedSession.DatabaseFiles != null &&
                        SelectedSession.DatabaseFiles.ContainsKey(SelectedConfigFile))
                    {
                        // Load DB async to prevent UI freeze
                        _ = LoadSqliteToTreeAsync(SelectedSession.DatabaseFiles[SelectedConfigFile]);
                    }
                    else
                    {
                        DbTreeNodes.Clear();
                    }
                    return;
                }

                // For non-DB files, clear tree and show text
                IsDbFileSelected = false;
                DbTreeNodes.Clear();

                // Handle JSON/text configuration files
                if (SelectedSession.ConfigurationFiles == null ||
                    !SelectedSession.ConfigurationFiles.ContainsKey(SelectedConfigFile))
                {
                    ConfigFileContent = "";
                    return;
                }

                string content = SelectedSession.ConfigurationFiles[SelectedConfigFile];

                // Try to format JSON for better readability
                try
                {
                    if (SelectedConfigFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        content.TrimStart().StartsWith("{") ||
                        content.TrimStart().StartsWith("["))
                    {
                        dynamic parsedJson = JsonConvert.DeserializeObject(content);
                        ConfigFileContent = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                    }
                    else
                    {
                        ConfigFileContent = content;
                    }
                }
                catch
                {
                    ConfigFileContent = content;
                }
            }
            catch (Exception ex)
            {
                ConfigFileContent = $"Error displaying file content: {ex.Message}";
            }
        }

        private string LoadSqliteContent(byte[] dbBytes)
        {
            var sb = new System.Text.StringBuilder();
            string tempDbPath = null;

            try
            {
                // Write DB bytes to a temporary file (SQLite needs a file path)
                tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, dbBytes);

                using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Read Only=True;"))
                {
                    connection.Open();

                    // Get all table names
                    var tables = new List<string>();
                    using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }

                    sb.AppendLine($"=== SQLite Database: {tables.Count} tables ===");
                    sb.AppendLine();

                    foreach (var tableName in tables)
                    {
                        sb.AppendLine($"━━━ TABLE: {tableName} ━━━");

                        // Get row count
                        using (var countCmd = new SQLiteCommand($"SELECT COUNT(*) FROM [{tableName}]", connection))
                        {
                            var count = countCmd.ExecuteScalar();
                            sb.AppendLine($"Rows: {count}");
                        }

                        // Get column info and data
                        using (var cmd = new SQLiteCommand($"SELECT * FROM [{tableName}] LIMIT 100", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            // Get column names
                            var columns = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                columns.Add(reader.GetName(i));
                            }
                            sb.AppendLine($"Columns: {string.Join(", ", columns)}");
                            sb.AppendLine();

                            // Display data
                            int rowNum = 0;
                            while (reader.Read() && rowNum < 100)
                            {
                                sb.AppendLine($"--- Row {++rowNum} ---");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
                                    // Truncate very long values
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
                // Clean up temp file
                if (tempDbPath != null && File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch { }
                }
            }

            return sb.ToString();
        }

        private async Task LoadSqliteToTreeAsync(byte[] dbBytes)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DbTreeNodes.Clear();
                _allDbTreeNodes.Clear();
            });

            DbTreeNode tablesRoot = null;
            string tempDbPath = null;

            try
            {
                // Do all DB work on background thread
                tablesRoot = await Task.Run(() =>
                {
                    tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                    File.WriteAllBytes(tempDbPath, dbBytes);

                    var root = new DbTreeNode
                    {
                        NodeType = "Root",
                        IsExpanded = true
                    };

                    using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Read Only=True;"))
                    {
                        connection.Open();

                        // Get all tables with their CREATE statements
                        var tablesInfo = new List<(string name, string sql)>();
                        using (var cmd = new SQLiteCommand("SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string name = reader.GetString(0);
                                string sql = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                tablesInfo.Add((name, sql));
                            }
                        }

                        root.Name = $"Tables ({tablesInfo.Count})";

                        foreach (var (tableName, tableSql) in tablesInfo)
                        {
                            // Table node with schema
                            var tableNode = new DbTreeNode
                            {
                                Name = tableName,
                                Schema = tableSql,
                                NodeType = "Table",
                                IsExpanded = false
                            };

                            // Get column info using PRAGMA
                            using (var cmd = new SQLiteCommand($"PRAGMA table_info([{tableName}])", connection))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    // cid, name, type, notnull, dflt_value, pk
                                    string colName = reader.GetString(1);
                                    string colType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                    bool notNull = reader.GetInt32(3) == 1;
                                    bool isPk = reader.GetInt32(5) == 1;

                                    // Build schema description
                                    string schemaDesc = $"\"{colName}\" {colType}";
                                    if (notNull) schemaDesc += " NOT NULL";
                                    if (isPk) schemaDesc += " PRIMARY KEY";

                                    var columnNode = new DbTreeNode
                                    {
                                        Name = colName,
                                        Type = colType,
                                        Schema = schemaDesc,
                                        NodeType = "Column"
                                    };

                                    tableNode.Children.Add(columnNode);
                                }
                            }

                            root.Children.Add(tableNode);
                        }
                    }

                    // Cleanup temp file
                    if (tempDbPath != null && File.Exists(tempDbPath))
                    {
                        try { File.Delete(tempDbPath); } catch { }
                    }

                    return root;
                });

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DbTreeNodes.Add(tablesRoot);
                    _allDbTreeNodes.Add(tablesRoot);
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DbTreeNodes.Add(new DbTreeNode { Name = $"Error: {ex.Message}", NodeType = "Error" });
                });
            }
        }

        private void FilterConfigContent()
        {
            if (string.IsNullOrWhiteSpace(ConfigSearchText))
            {
                // No filter - show all nodes
                foreach (var node in DbTreeNodes)
                {
                    SetNodeVisibility(node, true);
                }
                return;
            }

            string searchLower = ConfigSearchText.ToLower();

            foreach (var tableNode in DbTreeNodes)
            {
                bool tableHasMatch = FilterTreeNode(tableNode, searchLower);
                tableNode.IsVisible = tableHasMatch;
            }
        }

        private bool FilterTreeNode(DbTreeNode node, string searchLower)
        {
            bool selfMatches = (node.Name?.ToLower().Contains(searchLower) == true) ||
                               (node.Type?.ToLower().Contains(searchLower) == true) ||
                               (node.Schema?.ToLower().Contains(searchLower) == true);

            bool anyChildMatches = false;
            foreach (var child in node.Children)
            {
                bool childMatches = FilterTreeNode(child, searchLower);
                if (childMatches) anyChildMatches = true;
            }

            bool isVisible = selfMatches || anyChildMatches;
            node.IsVisible = isVisible;

            if (isVisible && node.Children.Count > 0)
            {
                node.IsExpanded = true;
            }

            return isVisible;
        }

        private void SetNodeVisibility(DbTreeNode node, bool visible)
        {
            node.IsVisible = visible;
            foreach (var child in node.Children)
            {
                SetNodeVisibility(child, visible);
            }
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

        private void StartBackgroundAnalysis(LogSessionData session)
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
                    if (_allLogsCache != null) _allLogsCache.Clear();
                    FilteredLogs.Clear();
                    SelectedLog = null;
                }
            });

            if (IsLiveMode)
            {
                // DON'T reset _lastParsedLogCount - keep tracking where we are in file
                IsRunning = true;
                StatusMessage = "Cleared. Monitoring continues...";
                Debug.WriteLine("LiveClear: Cleared UI, monitoring continues");
            }
            else
            {
                _lastParsedLogCount = 0;
                StatusMessage = "Logs cleared.";
            }
        }

        private void ClearLogs(object obj)
        {
            MarkedLogs.Clear(); MarkedAppLogs.Clear();
            _isMainFilterActive = false; _isAppFilterActive = false;
            _isMainFilterOutActive = false; _isAppFilterOutActive = false;
            _isAppTimeFocusActive = false; _lastFilteredAppCache = null;
            _isTimeFocusActive = false;
            if (_allLogsCache != null) _allLogsCache.Clear();
            _lastFilteredCache.Clear(); _negativeFilters.Clear(); _activeThreadFilters.Clear();
            Logs = new List<LogEntry>(); FilteredLogs.Clear(); AppDevLogsFiltered.Clear();
            LoggerTreeRoot.Clear(); Events.Clear(); Screenshots.Clear();
            LoadedFiles.Clear(); CurrentProgress = 0; SetupInfo = ""; PressConfig = ""; ScreenshotZoom = 400;
            IsFilterOutActive = false; LoadedSessions.Clear(); SelectedSession = null;
            _allAppLogsCache = null;
            ResetTreeFilters();

            // --- CLEAR NEW PROPERTIES ---
            ConfigurationFiles.Clear();
            ConfigFileContent = "";
            _configFilesPathMap.Clear();
            // ----------------------------

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

            _isTimeFocusActive = false;
            _isAppTimeFocusActive = false;
            _lastFilteredCache.Clear();
            _lastFilteredAppCache = null;
            _savedFilterRoot = null;
            SearchText = string.Empty;
            _isMainFilterActive = false;
            _isAppFilterActive = false;
            _isMainFilterOutActive = false;
            _isAppFilterOutActive = false;
            _activeThreadFilters.Clear();
            _negativeFilters.Clear();
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

                    if (_allLogsCache != null)
                    {
                        var timeSlice = _allLogsCache.Where(l => l.Date >= start && l.Date <= end).OrderByDescending(l => l.Date).ToList();
                        var smartFiltered = timeSlice.Where(l => IsDefaultLog(l)).ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _lastFilteredCache = timeSlice;
                            _savedFilterRoot = null;
                            _isTimeFocusActive = true;
                            _isMainFilterActive = true;
                            SelectedTabIndex = 0;
                            UpdateMainLogsFilter(true);
                            if (FilteredLogs != null)
                            {
                                FilteredLogs.ReplaceAll(smartFiltered);
                                if (FilteredLogs.Count > 0) SelectedLog = FilteredLogs[0];
                            }
                            OnPropertyChanged(nameof(IsFilterActive));
                            StatusMessage = $"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";

                            if (IsVisualMode && VisualTimelineVM != null)
                            {
                                VisualTimelineVM.LoadData(_allLogsCache, Events);
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
            if (_allAppLogsCache == null || !_allAppLogsCache.Any()) return;
            IsBusy = true;
            StatusMessage = "Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = _allAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppDevLogsFiltered.ReplaceAll(errors);
                    IsBusy = false;
                    StatusMessage = $"Showing {errors.Count} Errors";
                    _isAppFilterActive = true;
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
        private void SwitchToSession(LogSessionData session)
        {
            _isMainFilterActive = false;
            _isAppFilterActive = false;
            _isMainFilterOutActive = false;
            _isAppFilterOutActive = false;
            if (session == null) return;
            IsBusy = true;

            _allLogsCache = session.Logs;
            Logs = session.Logs;

            ConfigurationFiles.Clear();
            // Add JSON/text configuration files
            if (session.ConfigurationFiles != null)
            {
                foreach (var kvp in session.ConfigurationFiles)
                {
                    ConfigurationFiles.Add(kvp.Key);
                }
            }
            // Add SQLite database files
            if (session.DatabaseFiles != null)
            {
                foreach (var kvp in session.DatabaseFiles)
                {
                    ConfigurationFiles.Add(kvp.Key);
                }
            }

            if (ConfigurationFiles.Count > 0)
                SelectedConfigFile = ConfigurationFiles[0];
            else
                SelectedConfigFile = null;

            OnPropertyChanged(nameof(ConfigurationFiles));

            var defaultFilteredLogs = session.Logs.Where(l => IsDefaultLog(l)).ToList();
            FilteredLogs.ReplaceAll(defaultFilteredLogs);
            if (FilteredLogs.Count > 0) SelectedLog = FilteredLogs[0];

            Events = new ObservableCollection<EventEntry>(session.Events); OnPropertyChanged(nameof(Events));
            Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots); OnPropertyChanged(nameof(Screenshots));
            MarkedLogs = session.MarkedLogs; OnPropertyChanged(nameof(MarkedLogs));
            SetupInfo = session.SetupInfo;
            PressConfig = session.PressConfiguration;

            if (!string.IsNullOrEmpty(session.VersionsInfo))
                WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            _allAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();
            BuildLoggerTree(_allAppLogsCache);

            SearchText = "";
            IsFilterActive = false;
            IsFilterOutActive = false;
            _isTimeFocusActive = false;
            _isAppTimeFocusActive = false;

            _negativeFilters.Clear();
            _activeThreadFilters.Clear();

            _mainFilterRoot = null;
            _appFilterRoot = null;
            _lastFilteredAppCache = null;
            _lastFilteredCache.Clear();

            ResetTreeFilters();

            ApplyAppLogsFilter();
            IsBusy = false;
        }
        private void BuildLoggerTree(IEnumerable<LogEntry> logs)
        {
            LoggerTreeRoot.Clear();
            if (logs == null || !logs.Any()) return;

            int totalCount = logs.Count();
            var rootNode = new LoggerNode { Name = "All Loggers", FullPath = "", IsExpanded = true, Count = totalCount };

            var loggerGroups = logs.GroupBy(l => l.Logger)
                                   .Select(g => new { Name = g.Key, Count = g.Count() })
                                   .ToList();

            foreach (var group in loggerGroups)
            {
                if (string.IsNullOrEmpty(group.Name)) continue;
                var parts = group.Name.Split('.');
                AddNodeRecursive(rootNode, parts, 0, "", group.Count);
            }

            foreach (var child in rootNode.Children)
            {
                LoggerTreeRoot.Add(child);
            }
        }
        private void AddNodeRecursive(LoggerNode parent, string[] parts, int index, string currentPath, int count)
        {
            if (index >= parts.Length) return;

            string part = parts[index];
            string newPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";

            var child = parent.Children.FirstOrDefault(c => c.Name == part);
            if (child == null)
            {
                child = new LoggerNode { Name = part, FullPath = newPath };
                int insertIdx = 0;
                while (insertIdx < parent.Children.Count && string.Compare(parent.Children[insertIdx].Name, part) < 0)
                    insertIdx++;
                parent.Children.Insert(insertIdx, child);
            }

            child.Count += count;
            AddNodeRecursive(child, parts, index + 1, newPath, count);
        }
        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenLoggers.Remove(node.FullPath);
                node.IsHidden = false;
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenLoggers.Add(node.FullPath);
                node.IsHidden = true;
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                _treeShowOnlyLogger = node.FullPath;
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                _treeShowOnlyPrefix = node.FullPath;
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenPrefixes.Add(node.FullPath);
                node.IsHidden = true;
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }
        private void ExecuteTreeShowAll(object obj)
        {
            ResetTreeFilters();
            _isAppFilterActive = false;
            OnPropertyChanged(nameof(IsFilterActive));
            ToggleFilterView(false);
        }
        private void ResetTreeFilters()
        {
            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;
            foreach (var node in LoggerTreeRoot) ResetVisualHiddenState(node);
        }
        private void ResetVisualHiddenState(LoggerNode node)
        {
            node.IsHidden = false;
            foreach (var child in node.Children) ResetVisualHiddenState(child);
        }
        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log) new LogDetailsWindow(log).Show();
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
        private void ToggleFilterView(bool show)
        {
            UpdateMainLogsFilter(show);
            ApplyAppLogsFilter();
        }
        private void UpdateMainLogsFilter(bool show)
        {
            // --- תיקון קריטי ל-Live Monitoring ---
            // אם אנחנו במצב Live, אסור להחליף את Logs ברשימה סטטית!
            // אנחנו חייבים להישאר מחוברים ל-_liveLogsCollection שמתעדכן ברקע.
            if (IsLiveMode)
            {
                if (Logs != _liveLogsCollection)
                {
                    Logs = _liveLogsCollection;
                }
                // במצב Live, הפילטור קורה רק בטאב ה-Filtered (FilteredLogs),
                // הטאב הראשי (Logs) תמיד מציג את הכל (Raw Data).
                return;
            }
            // --------------------------------------

            bool isActive = _isMainFilterActive;
            IEnumerable<LogEntry> currentLogs;
            bool hasSearchText = !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2;

            if (isActive || hasSearchText)
            {
                if ((_mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0) || _isTimeFocusActive)
                    currentLogs = _lastFilteredCache ?? new List<LogEntry>();
                else
                    currentLogs = _allLogsCache;

                if (_activeThreadFilters.Any())
                    currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));

                if (hasSearchText)
                {
                    // Smart Boolean Search: Check if query has special operators
                    if (QueryParserService.HasBooleanOperators(SearchText))
                    {
                        var parser = new QueryParserService();
                        var filterTree = parser.Parse(SearchText, out string errorMessage);

                        if (filterTree != null)
                        {
                            // Use smart filtering with parsed tree
                            currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, filterTree));
                        }
                        else
                        {
                            // Parsing failed - show error in status (optional) and fall back to simple search
                            // You can optionally show the error: StatusMessage = $"Search syntax error: {errorMessage}";
                            currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                    }
                    else
                    {
                        // Simple search for maximum performance
                        currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
            }
            else
            {
                currentLogs = _allLogsCache;
            }

            if (_isMainFilterOutActive && _negativeFilters.Any())
            {
                currentLogs = currentLogs.Where(l =>
                {
                    foreach (var f in _negativeFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            if (l.ThreadName != null && l.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                        else
                        {
                            if (l.Message != null && l.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                    }
                    return true;
                });
            }
            Logs = currentLogs.ToList();
        }
        private void ApplyAppLogsFilter()
        {
            if (_allAppLogsCache == null) return;
            if (!_isAppFilterActive && string.IsNullOrWhiteSpace(SearchText))
            {
                AppDevLogsFiltered.ReplaceAll(_allAppLogsCache);
                return;
            }
            var source = _allAppLogsCache;
            if (_isAppFilterActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }
            var query = source.AsParallel().AsOrdered();
            if (_isAppFilterActive && !_isAppTimeFocusActive && _appFilterRoot != null && _appFilterRoot.Children.Count > 0)
            {
                query = query.Where(l => EvaluateFilterNode(l, _appFilterRoot));
            }
            if (_isAppFilterActive)
            {
                if (_treeShowOnlyLogger != null)
                {
                    query = query.Where(l => l.Logger == _treeShowOnlyLogger);
                }
                else if (_treeShowOnlyPrefix != null)
                {
                    query = query.Where(l => l.Logger != null && (l.Logger == _treeShowOnlyPrefix || l.Logger.StartsWith(_treeShowOnlyPrefix + ".")));
                }
                else
                {
                    if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                    {
                        query = query.Where(l =>
                        {
                            if (l.Logger == null) return true;
                            if (_treeHiddenLoggers.Contains(l.Logger)) return false;
                            foreach (var prefix in _treeHiddenPrefixes)
                            {
                                if (l.Logger == prefix || l.Logger.StartsWith(prefix + ".")) return false;
                            }
                            return true;
                        });
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText;

                // Smart Boolean Search for App Logs
                if (QueryParserService.HasBooleanOperators(SearchText))
                {
                    var parser = new QueryParserService();
                    var filterTree = parser.Parse(SearchText, out string errorMessage);

                    if (filterTree != null)
                    {
                        // Use smart filtering with parsed tree
                        query = query.Where(l => EvaluateFilterNode(l, filterTree));
                    }
                    else
                    {
                        // Parsing failed - fall back to simple search
                        query = query.Where(l =>
                        {
                            if (l.Message == null) return false;

                            if (l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;

                            int dataIndex = l.Message.IndexOf("DATA:", StringComparison.OrdinalIgnoreCase);
                            if (dataIndex >= 0)
                            {
                                string dataSection = l.Message.Substring(dataIndex + 5);
                                if (dataSection.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }

                            return false;
                        });
                    }
                }
                else
                {
                    // Simple search for maximum performance
                    query = query.Where(l =>
                    {
                        if (l.Message == null) return false;

                        if (l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;

                        int dataIndex = l.Message.IndexOf("DATA:", StringComparison.OrdinalIgnoreCase);
                        if (dataIndex >= 0)
                        {
                            string dataSection = l.Message.Substring(dataIndex + 5);
                            if (dataSection.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;
                        }

                        return false;
                    });
                }
            }
            if (_isAppFilterOutActive && _negativeFilters.Any())
            {
                var negFilters = _negativeFilters.ToList();
                query = query.Where(l =>
                {
                    foreach (var f in negFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            if (l.ThreadName != null && l.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                        else
                        {
                            if (l.Message != null && l.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                    }
                    return true;
                });
            }
            var resultList = query.ToList();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppDevLogsFiltered.ReplaceAll(resultList);
            });
        }
        // Indilogs 3.0/ViewModels/MainViewModel.cs

        private void StartLiveMonitoring(string path)
        {
            // ניקוי
            StopLiveMonitoring();
            ClearLogs(null);

            // הגדרות UI
            LoadedFiles.Add(Path.GetFileName(path));
            _liveFilePath = path;
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            _allLogsCache = _liveLogsCollection;
            Logs = _liveLogsCollection;

            IsLiveMode = true;
            IsRunning = true;
            WindowTitle = "IndiLogs 3.0 - LIVE MONITORING (Custom)";

            // אתחול המנגנון החדש
            _liveCts = new CancellationTokenSource();
            _customReader = new CustomLiveLogReader();

            // רישום לאירועים
            _customReader.OnStatusChanged += (msg) =>
            {
                Application.Current.Dispatcher.Invoke(() => StatusMessage = msg);
            };

            // בתוך StartLiveMonitoring, החלף את הרישום ל-OnLogsReceived:

            _customReader.OnLogsReceived += (newLogs) =>
            {
                Task.Run(async () =>
                {
                    // 1. החלת צבעים (דיפולטיים + מותאמים אישית אם יש)
                    await _coloringService.ApplyDefaultColorsAsync(newLogs, false);

                    // אם יש חוקי צביעה מותאמים אישית פעילים
                    if (_mainColoringRules != null && _mainColoringRules.Any())
                    {
                        await _coloringService.ApplyCustomColoringAsync(newLogs, _mainColoringRules);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        lock (_collectionLock)
                        {
                            foreach (var log in newLogs) // שומרים על סדר כרונולוגי
                            {
                                // א. הוספה תמיד לטאב הראשי (PLC) ולזיכרון
                                _liveLogsCollection.Insert(0, log);

                                // ב. הוספה לטאב המסונן (PLC FILTERED) רק אם עומד בתנאים!
                                if (ShouldShowInFilteredView(log))
                                {
                                    FilteredLogs.Insert(0, log);
                                }
                            }

                            StatusMessage = $"Live: {FilteredLogs.Count:N0} filtered / {_liveLogsCollection.Count:N0} total";
                        }
                    });
                });
            };

            // הפעלה ברקע
            Task.Run(() => _customReader.StartMonitoring(path, _liveCts.Token));
        }
        private bool ShouldShowInFilteredView(LogEntry log)
        {
            // 1. בדיקת Negative Filters (Filter Out) - תמיד פעיל אם מוגדר
            if (_isMainFilterOutActive && _negativeFilters.Any())
            {
                foreach (var f in _negativeFilters)
                {
                    if (f.StartsWith("THREAD:"))
                    {
                        if (log.ThreadName != null && log.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                    else
                    {
                        if (log.Message != null && log.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                }
            }

            // 2. האם יש פילטרים פעילים? (חיפוש, עצים, Threads)
            bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            bool hasActiveFilter = _isMainFilterActive || hasSearch || _activeThreadFilters.Any();

            // 3. אם אין שום פילטר פעיל -> השתמש בלוגיקה הדיפולטית (Default Colors/Filter)
            // זה מה שגורם לטאב "PLC Filtered" להיראות כמו בטעינה רגילה
            if (!hasActiveFilter)
            {
                return IsDefaultLog(log);
            }

            // 4. בדיקת פילטרים פעילים

            // Thread Filter
            if (_activeThreadFilters.Any())
            {
                if (!_activeThreadFilters.Contains(log.ThreadName)) return false;
            }

            // Search Text
            if (hasSearch)
            {
                if (log.Message == null || log.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Advanced Tree / Condition Filter
            if (_mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0)
            {
                if (!EvaluateFilterNode(log, _mainFilterRoot)) return false;
            }

            return true;
        }
        private void StartFileWatcher(string filePath)
        {
            try
            {
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = false;
                    _fileWatcher.Dispose();
                    _fileWatcher = null;
                }

                string directory = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileName(filePath);

                Debug.WriteLine($">>> Starting FileSystemWatcher for: {fileName}");

                _fileWatcher = new FileSystemWatcher(directory, fileName);
                _fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.EnableRaisingEvents = true;

                Debug.WriteLine($">>> ✅ FileSystemWatcher started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ FileSystemWatcher error: {ex.Message}");
                Debug.WriteLine($"   No live monitoring - FileSystemWatcher failed");
            }
        }
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var now = DateTime.Now;

            // חזק יותר: 5 שניות מינימום בין refreshים
            if ((now - _lastFileCheckTime).TotalMilliseconds < MIN_REFRESH_INTERVAL_MS)
            {
                Debug.WriteLine($"[{now:HH:mm:ss.fff}] 🔇 File change IGNORED (throttled)");
                return;
            }

            _lastFileCheckTime = now;
            Debug.WriteLine($"[{now:HH:mm:ss.fff}] 📢 File changed - triggering refresh");

            Task.Run(async () =>
            {
                try
                {
                    await RefreshLogs();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ OnFileChanged error: {ex.Message}");
                }
            });
        }

        private async Task RefreshLogsOptimized()
        {
            if (_isRefreshActive) return;

            _isRefreshActive = true;
            Debug.WriteLine($">>> RefreshLogsOptimized STARTED");

            try
            {
                await Task.Delay(100); // Wait for write to complete

                long currentFileSize;
                try
                {
                    var fileInfo = new FileInfo(_liveFilePath);
                    currentFileSize = fileInfo.Length;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  ❌ Cannot access file: {ex.Message}");
                    return;
                }

                const long MIN_GROWTH = 5120; // 5KB
                long growth = currentFileSize - _lastFileSize;

                if (growth < MIN_GROWTH)
                {
                    Debug.WriteLine($"  ℹ️ File grew by only {growth:N0} bytes - skipping");
                    return;
                }

                _lastFileSize = currentFileSize;
                Debug.WriteLine($"  File grew by {growth:N0} bytes");

                // ================================================================
                // STRATEGY 1: Try to seek to last position (FAST!)
                // ================================================================
                Debug.WriteLine($"  🎯 Attempting OPTIMIZED read from position {_lastStreamPosition:N0}...");

                bool optimizedSuccess = false;
                List<LogEntry> newLogs = null;

                try
                {
                    newLogs = await Task.Run(() =>
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // Try seeking to last known position
                            if (_lastStreamPosition > 0 && _lastStreamPosition < fs.Length)
                            {
                                Debug.WriteLine($"    Seeking to position {_lastStreamPosition:N0}...");
                                fs.Seek(_lastStreamPosition, SeekOrigin.Begin);

                                Debug.WriteLine($"    Creating reader from seeked position...");
                                var result = _logService.ParseLogStream(fs);

                                if (result.AllLogs != null && result.AllLogs.Count > 0)
                                {
                                    Debug.WriteLine($"    ✅ SUCCESS! Parsed {result.AllLogs.Count:N0} new logs from seeked position!");

                                    // Update position
                                    _lastStreamPosition = fs.Position;

                                    optimizedSuccess = true;
                                    return result.AllLogs;
                                }
                                else
                                {
                                    Debug.WriteLine($"    ⚠️ No logs from seeked position, falling back...");
                                }
                            }

                            return null;
                        }
                    });
                }
                catch (Exception seekEx)
                {
                    Debug.WriteLine($"    ❌ Seek strategy failed: {seekEx.Message}");
                }

                // ================================================================
                // STRATEGY 2: Fallback - parse entire file (SLOW)
                // ================================================================
                if (!optimizedSuccess || newLogs == null)
                {
                    Debug.WriteLine($"  ⚙️ Falling back to full file parse...");

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    newLogs = await Task.Run(() =>
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var ms = new MemoryStream())
                        {
                            fs.CopyTo(ms);
                            ms.Position = 0;

                            var result = _logService.ParseLogStream(ms);
                            var allLogs = result.AllLogs;

                            if (allLogs != null && allLogs.Count > _lastParsedLogCount)
                            {
                                var deltaLogs = allLogs.Skip(_lastParsedLogCount).ToList();
                                _lastParsedLogCount = allLogs.Count;
                                _lastStreamPosition = fs.Position; // Update position

                                return deltaLogs;
                            }

                            return null;
                        }
                    });

                    sw.Stop();
                    Debug.WriteLine($"  Full parse took {sw.ElapsedMilliseconds:N0}ms");
                }

                // ================================================================
                // Add new logs to UI
                // ================================================================
                if (newLogs != null && newLogs.Count > 0)
                {
                    await _coloringService.ApplyDefaultColorsAsync(newLogs, false);
                    if (_savedColoringRules != null && _savedColoringRules.Count > 0)
                        await _coloringService.ApplyCustomColoringAsync(newLogs, _savedColoringRules);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        lock (_collectionLock)
                        {
                            foreach (var log in newLogs.OrderByDescending(l => l.Date))
                            {
                                _liveLogsCollection.Insert(0, log);
                                FilteredLogs.Insert(0, log);
                            }

                            if (SelectedLog == null && FilteredLogs.Count > 0)
                                SelectedLog = FilteredLogs[0];

                            StatusMessage = $"Live: {FilteredLogs.Count:N0} shown (+{newLogs.Count} new) | {_liveLogsCollection.Count:N0} total";
                        }
                    });

                    string method = optimizedSuccess ? "OPTIMIZED SEEK" : "full parse";
                    Debug.WriteLine($"  ✅ Added {newLogs.Count:N0} new logs via {method}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ RefreshLogsOptimized error: {ex.Message}");
            }
            finally
            {
                _isRefreshActive = false;
                Debug.WriteLine($">>> RefreshLogsOptimized FINISHED");
            }
        }




        private void StopLiveMonitoring()
        {
            _liveCts?.Cancel();
            _liveCts = null;
            _customReader = null;

            IsLiveMode = false;
            IsRunning = false;
            StatusMessage = "Live monitoring stopped.";
        }





        private async Task PollingLoop(CancellationToken token)
        {
            Debug.WriteLine($">>> PollingLoop STARTED");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsRunning && !string.IsNullOrEmpty(_liveFilePath) && File.Exists(_liveFilePath))
                    {
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Polling trigger...");
                        await RefreshLogs();
                    }
                    else
                    {
                        if (!IsRunning)
                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Polling skipped (paused)");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ PollingLoop error: {ex.Message}");
                }

                try
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Waiting {POLLING_INTERVAL_MS}ms...");
                    await Task.Delay(POLLING_INTERVAL_MS, token);
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($">>> PollingLoop CANCELLED");
                    break;
                }
            }

            Debug.WriteLine($">>> PollingLoop EXITED");
        }





        // Indilogs 3.0/ViewModels/MainViewModel.cs

        private async Task RefreshLogs()
        {
            if (_isRefreshActive) return;
            _isRefreshActive = true;

            try
            {
                // בדיקה מהירה אם הקובץ באמת גדל
                long currentLength = new FileInfo(_liveFilePath).Length;
                if (currentLength <= _lastStreamPosition)
                {
                    _isRefreshActive = false;
                    return;
                }

                await Task.Run(() =>
                {
                    using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        // 1. קפיצה למיקום האחרון הידוע
                        fs.Seek(_lastStreamPosition, SeekOrigin.Begin);

                        // 2. קריאת הדלתא (הלוגים החדשים בלבד)
                        var newLogs = _logService.ParseLogStreamPartial(fs);

                        // 3. עדכון המיקום לפעם הבאה
                        _lastStreamPosition = fs.Position;
                        _lastFileSize = fs.Length;

                        if (newLogs.Count > 0)
                        {
                            // צביעה
                            _coloringService.ApplyDefaultColorsAsync(newLogs, false).Wait();

                            // עדכון UI
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                lock (_collectionLock)
                                {
                                    foreach (var log in newLogs.OrderByDescending(l => l.Date))
                                    {
                                        FilteredLogs.Insert(0, log);       // הוספה לראש התצוגה
                                        _liveLogsCollection.Insert(0, log); // הוספה לראש ה-Cache
                                    }
                                    StatusMessage = $"Live: {FilteredLogs.Count:N0} logs (+{newLogs.Count} new)";
                                }
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshLogs Error: {ex.Message}");
            }
            finally
            {
                _isRefreshActive = false;
            }
        }

        // ============================================================================
        // STEP 4: REPLACE StopLiveMonitoring() METHOD (around line 1601)
        // ============================================================================


        private void LivePlay(object obj)
        {
            Debug.WriteLine($"LivePlay called - IsLiveMode: {IsLiveMode}, _liveFilePath: {_liveFilePath}");
            IsRunning = true;
            StatusMessage = "Live monitoring active.";
        }
        private void LivePause(object obj)
        {
            Debug.WriteLine($"LivePause called");
            IsRunning = false;
            StatusMessage = "Live monitoring paused.";
        }
        private void LoadFile(object obj)
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true) ProcessFiles(dialog.FileNames);
        }
        private void FilterOut(object p)
        {
            if (SelectedLog == null) return;
            var w = new FilterOutWindow(SelectedLog.Message);
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.TextToRemove))
            {
                _negativeFilters.Add(w.TextToRemove);
                IsFilterOutActive = true;
                ToggleFilterView(IsFilterActive);
            }
        }
        private void FilterOutThread(object obj)
        {
            if (SelectedLog == null || string.IsNullOrEmpty(SelectedLog.ThreadName)) return;
            var win = new FilterOutWindow(SelectedLog.ThreadName);
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.TextToRemove))
            {
                string filterKey = "THREAD:" + win.TextToRemove;
                if (!_negativeFilters.Contains(filterKey))
                {
                    _negativeFilters.Add(filterKey);
                    IsFilterOutActive = true;
                    ToggleFilterView(IsFilterActive);
                }
            }
        }
        private void OpenThreadFilter(object obj)
        {
            if (_allLogsCache == null || !_allLogsCache.Any()) return;
            var threads = _allLogsCache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            var win = new ThreadFilterWindow(threads);
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear) { _activeThreadFilters.Clear(); if (_savedFilterRoot == null) IsFilterActive = false; }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any()) { _activeThreadFilters = win.SelectedThreads; IsFilterActive = true; }
                ToggleFilterView(IsFilterActive);
            }
        }
        private async void OpenFilterWindow(object obj)
        {
            var win = new FilterWindow();
            bool isAppTab = SelectedTabIndex == 2;
            var currentRoot = isAppTab ? _appFilterRoot : _mainFilterRoot;

            if (currentRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(currentRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                IsBusy = true;
                await Task.Run(() =>
                {
                    if (isAppTab) _appFilterRoot = newRoot;
                    else
                    {
                        _mainFilterRoot = newRoot;
                        if (hasAdvanced)
                        {
                            // Thread-safe enumeration: create a copy of the collection before filtering
                            List<LogEntry> cacheCopy;
                            lock (_collectionLock)
                            {
                                cacheCopy = _allLogsCache.ToList();
                            }
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, _mainFilterRoot)).ToList();
                            _lastFilteredCache = res;
                        }
                        else _lastFilteredCache.Clear();
                    }
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isAppTab) { _isAppFilterActive = hasAdvanced; ApplyAppLogsFilter(); }
                    else { _isMainFilterActive = hasAdvanced || _activeThreadFilters.Any(); UpdateMainLogsFilter(_isMainFilterActive); }
                    OnPropertyChanged(nameof(IsFilterActive));
                    IsBusy = false;
                });
            }
        }
        private bool EvaluateFilterNode(LogEntry log, FilterNode node)
        {
            if (node == null) return true;
            if (node.Type == NodeType.Condition)
            {
                string val = "";
                switch (node.Field)
                {
                    case "Level": val = log.Level; break;
                    case "ThreadName": val = log.ThreadName; break;
                    case "Logger": val = log.Logger; break;
                    case "ProcessName": val = log.ProcessName; break;
                    case "Method": val = log.Method; break;
                    default: val = log.Message; break;
                }
                if (string.IsNullOrEmpty(val)) return false;
                string op = node.Operator;
                string criteria = node.Value;
                if (op == "Equals") return val.Equals(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Begins With") return val.StartsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Ends With") return val.EndsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Regex") { try { return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase); } catch { return false; } }
                return val.IndexOf(criteria, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else
            {
                if (node.Children == null || node.Children.Count == 0) return true;
                string op = node.LogicalOperator;
                bool isBaseOr = op.Contains("OR");
                bool baseResult;
                if (isBaseOr)
                {
                    baseResult = false;
                    foreach (var child in node.Children) { if (EvaluateFilterNode(log, child)) { baseResult = true; break; } }
                }
                else
                {
                    baseResult = true;
                    foreach (var child in node.Children) { if (!EvaluateFilterNode(log, child)) { baseResult = false; break; } }
                }
                if (op.StartsWith("NOT")) return !baseResult;
                return baseResult;
            }
        }
        private void FilterContext(object obj)
        {
            if (SelectedLog == null) return;
            IsBusy = true;
            double multiplier = SelectedTimeUnit == "Minutes" ? 60 : 1;
            double rangeInSeconds = ContextSeconds * multiplier;
            DateTime targetTime = SelectedLog.Date;
            DateTime startTime = targetTime.AddSeconds(-rangeInSeconds);
            DateTime endTime = targetTime.AddSeconds(rangeInSeconds);
            bool isAppTab = SelectedTabIndex == 2;

            Task.Run(() =>
            {
                if (isAppTab)
                {
                    if (_allAppLogsCache != null)
                    {
                        var contextLogs = _allAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() => { _lastFilteredAppCache = contextLogs; _isAppTimeFocusActive = true; _appFilterRoot = null; IsFilterActive = true; ToggleFilterView(true); StatusMessage = $"APP Focus Time: {contextLogs.Count} logs shown"; IsBusy = false; });
                    }
                }
                else
                {
                    if (_allLogsCache != null)
                    {
                        var contextLogs = _allLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() => { _lastFilteredCache = contextLogs; _savedFilterRoot = null; _isTimeFocusActive = true; IsFilterActive = true; ToggleFilterView(true); StatusMessage = $"Focus Time: +/- {rangeInSeconds}s | {contextLogs.Count} logs shown"; IsBusy = false; });
                    }
                }
            });
        }
        private void UndoFilterOut(object parameter) { }
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
        private void LoadSavedConfigurations()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
            if (Directory.Exists(path))
                foreach (var f in Directory.GetFiles(path, "*.json")) { try { var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(f)); c.FilePath = f; SavedConfigs.Add(c); } catch { } }
        }
        private async void ApplyConfiguration(object parameter)
        {
            if (parameter is SavedConfiguration c)
            {
                IsBusy = true;
                StatusMessage = $"Loading config: {c.Name} (Overriding current state)...";

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SearchText = "";
                    IsSearchPanelVisible = false;
                    _negativeFilters.Clear();
                    _activeThreadFilters.Clear();
                    _isTimeFocusActive = false;
                    _isAppTimeFocusActive = false;
                    ResetTreeFilters();
                    _lastFilteredCache.Clear();
                    _lastFilteredAppCache = null;
                    _isMainFilterActive = false;
                    _isAppFilterActive = false;
                    _isMainFilterOutActive = false;
                    _isAppFilterOutActive = false;
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                });

                await Task.Run(async () =>
                {
                    _mainColoringRules = c.MainColoringRules ?? new List<ColoringCondition>();
                    if (_allLogsCache != null)
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_allLogsCache, false);
                        if (_mainColoringRules.Any())
                            await _coloringService.ApplyCustomColoringAsync(_allLogsCache, _mainColoringRules);
                    }

                    _appColoringRules = c.AppColoringRules ?? new List<ColoringCondition>();
                    if (_allAppLogsCache != null)
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_allAppLogsCache, true);
                        if (_appColoringRules.Any())
                            await _coloringService.ApplyCustomColoringAsync(_allAppLogsCache, _appColoringRules);
                    }
                });

                _mainFilterRoot = c.MainFilterRoot;
                if (_mainFilterRoot != null && _allLogsCache != null)
                {
                    var res = await Task.Run(() => _allLogsCache.Where(l => EvaluateFilterNode(l, _mainFilterRoot)).ToList());
                    _lastFilteredCache = res;
                }

                _appFilterRoot = c.AppFilterRoot;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_appFilterRoot != null && _appFilterRoot.Children.Count > 0)
                        _isAppFilterActive = true;

                    if (_mainFilterRoot != null && _mainFilterRoot.Children.Count > 0)
                        _isMainFilterActive = true;

                    if (Logs != null) foreach (var log in Logs) log.OnPropertyChanged("RowBackground");
                    if (AppDevLogsFiltered != null) foreach (var log in AppDevLogsFiltered) log.OnPropertyChanged("RowBackground");

                    UpdateMainLogsFilter(_isMainFilterActive);
                    ApplyAppLogsFilter();

                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                });

                IsBusy = false;
                StatusMessage = "Configuration loaded successfully.";
            }
        }
        private void RemoveConfiguration(object parameter)
        {
            var configToDelete = SelectedConfig;
            if (configToDelete != null && MessageBox.Show($"Delete '{configToDelete.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (File.Exists(configToDelete.FilePath)) File.Delete(configToDelete.FilePath);
                SavedConfigs.Remove(configToDelete);
            }
        }
        private void SaveConfiguration(object obj)
        {
            var existingNames = SavedConfigs.Select(c => c.Name).ToList();
            var dlg = new SaveConfigWindow(existingNames);
            if (dlg.ShowDialog() == true)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
                Directory.CreateDirectory(dir);
                var cfg = new SavedConfiguration { Name = dlg.ConfigName, CreatedDate = DateTime.Now, FilePath = Path.Combine(dir, dlg.ConfigName + ".json"), MainColoringRules = _mainColoringRules ?? new List<ColoringCondition>(), MainFilterRoot = _mainFilterRoot, AppColoringRules = _appColoringRules ?? new List<ColoringCondition>(), AppFilterRoot = _appFilterRoot };
                File.WriteAllText(cfg.FilePath, JsonConvert.SerializeObject(cfg)); SavedConfigs.Add(cfg);
            }
        }
        private void LoadConfigurationFromFile(object obj)
        {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true) { try { var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(dlg.FileName)); c.FilePath = dlg.FileName; SavedConfigs.Add(c); } catch { } }
        }
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
        private async void OpenColoringWindow(object obj)
        {
            try
            {
                var win = new ColoringWindow();
                bool isAppTab = SelectedTabIndex == 2;
                var currentRulesSource = isAppTab ? _appColoringRules : _mainColoringRules;
                var rulesCopy = currentRulesSource.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    IsBusy = true;
                    StatusMessage = isAppTab ? "Applying APP Colors..." : "Applying Main Colors...";
                    await Task.Run(async () =>
                    {
                        if (isAppTab) { _appColoringRules = newRules; if (_allAppLogsCache != null) { await _coloringService.ApplyDefaultColorsAsync(_allAppLogsCache, true); await _coloringService.ApplyCustomColoringAsync(_allAppLogsCache, _appColoringRules); } }
                        else { _mainColoringRules = newRules; if (_allLogsCache != null) { await _coloringService.ApplyDefaultColorsAsync(_allLogsCache, false); await _coloringService.ApplyCustomColoringAsync(_allLogsCache, _mainColoringRules); } }
                    });
                    Application.Current.Dispatcher.Invoke(() => { if (isAppTab) { if (AppDevLogsFiltered != null) foreach (var log in AppDevLogsFiltered) log.OnPropertyChanged("RowBackground"); } else { if (Logs != null) foreach (var log in Logs) log.OnPropertyChanged("RowBackground"); } });
                    IsBusy = false; StatusMessage = "Colors Updated.";
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); IsBusy = false; }
        }
        private void MarkRow(object obj)
        {
            if (SelectedLog != null)
            {
                SelectedLog.IsMarked = !SelectedLog.IsMarked;
                bool isAppTab = SelectedTabIndex == 2;
                var targetList = isAppTab ? MarkedAppLogs : MarkedLogs;
                if (SelectedLog.IsMarked) { targetList.Add(SelectedLog); var sorted = targetList.OrderByDescending(x => x.Date).ToList(); targetList.Clear(); foreach (var l in sorted) targetList.Add(l); }
                else { targetList.Remove(SelectedLog); }
            }
        }
        private void GoToNextMarked(object obj)
        {
            if (!Logs.Any()) return;
            var list = Logs.ToList();
            int current = SelectedLog != null ? list.IndexOf(SelectedLog) : -1;
            var next = list.Skip(current + 1).FirstOrDefault(l => l.IsMarked) ?? list.FirstOrDefault(l => l.IsMarked);
            if (next != null) { SelectedLog = next; RequestScrollToLog?.Invoke(next); }
        }
        private void GoToPrevMarked(object obj)
        {
            if (!Logs.Any()) return;
            var list = Logs.ToList();
            int current = SelectedLog != null ? list.IndexOf(SelectedLog) : list.Count;
            var prev = list.Take(current).LastOrDefault(l => l.IsMarked) ?? list.LastOrDefault(l => l.IsMarked);
            if (prev != null) { SelectedLog = prev; RequestScrollToLog?.Invoke(prev); }
        }
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
        private void OpenMarkedLogsWindow(object obj)
        {
            if (IsMarkedLogsCombined)
            {
                if (_combinedMarkedWindow != null && _combinedMarkedWindow.IsVisible)
                {
                    _combinedMarkedWindow.Activate();
                    return;
                }

                var combinedList = new List<LogEntry>();
                if (MarkedLogs != null) combinedList.AddRange(MarkedLogs);
                if (MarkedAppLogs != null) combinedList.AddRange(MarkedAppLogs);

                var sortedList = combinedList.OrderByDescending(x => x.Date).ToList();
                var collectionToShow = new ObservableCollection<LogEntry>(sortedList);

                _combinedMarkedWindow = new MarkedLogsWindow(collectionToShow, "Marked Lines (Combined - Main & App)");
                _combinedMarkedWindow.DataContext = this;
                _combinedMarkedWindow.Owner = Application.Current.MainWindow;
                _combinedMarkedWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _combinedMarkedWindow.Closed += (s, e) => _combinedMarkedWindow = null;
                _combinedMarkedWindow.Show();
            }
            else
            {
                bool isAppTab = SelectedTabIndex == 2;

                if (isAppTab)
                {
                    if (_markedAppLogsWindow != null && _markedAppLogsWindow.IsVisible)
                    {
                        _markedAppLogsWindow.Activate();
                        return;
                    }
                    _markedAppLogsWindow = new MarkedLogsWindow(MarkedAppLogs, "Marked Lines (APP)");
                    _markedAppLogsWindow.DataContext = this;
                    _markedAppLogsWindow.Owner = Application.Current.MainWindow;
                    _markedAppLogsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    _markedAppLogsWindow.Closed += (s, e) => _markedAppLogsWindow = null;
                    _markedAppLogsWindow.Show();
                }
                else
                {
                    if (_markedMainLogsWindow != null && _markedMainLogsWindow.IsVisible)
                    {
                        _markedMainLogsWindow.Activate();
                        return;
                    }
                    _markedMainLogsWindow = new MarkedLogsWindow(MarkedLogs, "Marked Lines (LOGS)");
                    _markedMainLogsWindow.DataContext = this;
                    _markedMainLogsWindow.Owner = Application.Current.MainWindow;
                    _markedMainLogsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    _markedMainLogsWindow.Closed += (s, e) => _markedMainLogsWindow = null;
                    _markedMainLogsWindow.Show();
                }
            }
        }
        private void CloseAllMarkedWindows()
        {
            if (_combinedMarkedWindow != null) { _combinedMarkedWindow.Close(); _combinedMarkedWindow = null; }
            if (_markedMainLogsWindow != null) { _markedMainLogsWindow.Close(); _markedMainLogsWindow = null; }
            if (_markedAppLogsWindow != null) { _markedAppLogsWindow.Close(); _markedAppLogsWindow = null; }
        }
        private bool IsDefaultLog(LogEntry l)
        {
            if (string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Message != null && l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Events", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Logger != null && l.Logger.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        private void OpenKibana(object obj) { }
        public void OnFilesDropped(string[] files) { if (files != null && files.Length > 0) ProcessFiles(files); }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
} 