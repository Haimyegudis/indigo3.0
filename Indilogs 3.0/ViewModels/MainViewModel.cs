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
        private readonly IWindowOwnerProvider _windowOwner;
        private readonly IWindowManager _windowManager;
        private readonly Services.GrepServiceBundle _grepBundle;
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

        /// <summary>Exposes the plugin loader so child VMs can query loaded plugins.</summary>
        public Services.Interfaces.IPluginLoader? GetPluginLoader()
            => _logService?.GetPluginLoader();

        public ICommand ToggleLogDetailsPinCommand { get; private set; }
        public ICommand ToggleLeftPanelCommand { get; }
        public ICommand ToggleRightPanelCommand { get; }
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
        public ICommand? OpenVisualAnalysisCommand { get; private set; }
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

        public MainViewModel(ILogFileService logService, ILogColoringService coloringService, ICsvExportService csvService, IDefaultConfigurationService defaultConfigService, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher, IWindowOwnerProvider windowOwner, IWindowManager windowManager, Services.GrepServiceBundle grepBundle)
        {
            _logService = logService;
            _coloringService = coloringService;
            _csvService = csvService;
            _defaultConfigService = defaultConfigService;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowOwner = windowOwner;
            _windowManager = windowManager;
            _grepBundle = grepBundle;
            _isTimeSyncEnabled = false;

            // Initialize child ViewModels
            SessionVM = new LogSessionViewModel(this, _logService, _coloringService, _dialogService, _viewFactory, _dispatcher, _windowOwner);
            FilterVM = new FilterSearchViewModel(this, SessionVM, _dialogService, _viewFactory, _dispatcher, _windowOwner);
            CaseVM = new CaseManagementViewModel(this, SessionVM, FilterVM, _dialogService, _viewFactory, _dispatcher, _windowManager);
            LiveVM = new LiveMonitoringViewModel(this, SessionVM, FilterVM, CaseVM, _logService, _coloringService, _dispatcher);
            ConfigVM = new ConfigExplorerViewModel(this, SessionVM, _dialogService, _viewFactory, _dispatcher, _windowOwner, _windowManager);
            ChartVM = new ChartTabViewModel(this);
            CprVM = new CprAnalysisViewModel(_dialogService);
            DifferentLogsVM = new DifferentLogsViewModel(_logService.GetPluginLoader(), _dialogService, _viewFactory, _windowOwner);
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
            if (Fonts.SystemFontFamilies != null)
                AvailableFonts = new ObservableCollection<string>(Fonts.SystemFontFamilies.OrderBy(f => f.Source).Select(f => f.Source));
            else
                AvailableFonts = new ObservableCollection<string>();

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
            OpenHelpCommand = new RelayCommand(o => _windowManager.OpenWindow(_viewFactory.Create<Views.HelpWindow>()));
            OpenPluginTesterCommand = new RelayCommand(o => _windowManager.GetOrCreate<Views.PluginTesterWindow>(() => _viewFactory.Create<Views.PluginTesterWindow>()));
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            OpenSnakeGameCommand = new RelayCommand(OpenSnakeGame);

            FilterToStateCommand = new RelayCommand(FilterToState);

            ZoomInCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Min(5.0, Math.Round(ScreenshotZoom + 0.1, 1));
                else GridFontSize = Math.Min(30, GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Max(0.2, Math.Round(ScreenshotZoom - 0.1, 1));
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
    }
}
