using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the enhanced Global Grep window.
    /// Supports multi-location, structured criteria, profiles, and scheduling.
    /// </summary>
    public partial class GlobalGrepViewModel : ViewModelBase
    {
        private readonly IGlobalGrepService _grepService;
        private readonly ISearchLocationService _locationService;
        private readonly ISearchConfigService _configService;
        private readonly ISearchSchedulerService _schedulerService;
        private readonly IWindowsTaskSchedulerService _taskSchedulerService;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private readonly IWindowOwnerProvider _windowOwner;
        private CancellationTokenSource? _cancellationTokenSource;
        private SearchReportParams? _lastSearchParams;

        // Streaming results: background threads enqueue, timer flushes to UI
        private readonly ConcurrentQueue<GrepResult> _resultQueue = new ConcurrentQueue<GrepResult>();
        private System.Threading.Timer? _flushTimer;

        /// <summary>
        /// Raised when a scheduled "Run Now" wants to close the window.
        /// The string arg is the schedule name.
        /// </summary>
        public event Action<string>? RequestCloseForScheduledRun;

        /// <summary>
        /// Raised when a scheduled "Run Now" background search completes.
        /// Args: schedule name, HTML report path (null if no report).
        /// </summary>
        public event Action<string, string?>? ScheduledRunCompleted;

        // Kept for backward compat (in-memory loaded session search)
        private IEnumerable<LogSessionData> LoadedSessions { get; }

        #region Constructor

        public GlobalGrepViewModel(
            IEnumerable<LogSessionData> loadedSessions,
            IGlobalGrepService grepService,
            ISearchLocationService locationService,
            ISearchConfigService configService,
            ISearchSchedulerService schedulerService,
            IWindowsTaskSchedulerService taskSchedulerService,
            IDialogService dialogService,
            IViewFactory viewFactory,
            IDispatcher dispatcher,
            IWindowOwnerProvider windowOwner)
        {
            _grepService = grepService;
            _locationService = locationService;
            _configService = configService;
            _schedulerService = schedulerService;
            _taskSchedulerService = taskSchedulerService;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowOwner = windowOwner;
            LoadedSessions = loadedSessions;
            Results = new ObservableRangeCollection<GrepResult>();

            // Load locations
            Locations = new ObservableCollection<SearchLocation>(_locationService.Locations);

            // Initialize condition groups with one empty group
            ConditionGroups = new ObservableCollection<ConditionGroupVM>();
            ConditionGroups.Add(new ConditionGroupVM());

            // Load saved profiles
            RefreshSavedProfiles();

            // Load schedules
            Schedules = new ObservableCollection<ScheduledSearch>(_schedulerService.Schedules);
            _schedulerService.Start();

            // Sync schedules with Windows Task Scheduler (background)
            Task.Run(() =>
            {
                try { _taskSchedulerService.SyncAll(_schedulerService.Schedules); }
                catch (Exception ex) { AppLogger.Error("[Scheduler] Failed to sync with Windows Task Scheduler", ex); }
            });

            // Defaults
            SearchPLC = true;
            SearchAPP = true;
            UseRegex = false;

            // Combo box sources
            FieldOptions = Enum.GetValues(typeof(SearchField)).Cast<SearchField>().ToList();
            OperatorOptions = Enum.GetValues(typeof(SearchOperator)).Cast<SearchOperator>().ToList();
            ConditionOperatorOptions = Enum.GetValues(typeof(ConditionOperator)).Cast<ConditionOperator>().ToList();
            GroupOperatorOptions = Enum.GetValues(typeof(LogicalGroupOperator)).Cast<LogicalGroupOperator>().ToList();
            SelectedGroupOperator = LogicalGroupOperator.And;

            // Commands
            SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(), _ => !IsSearching);
            CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);
            ClearResultsCommand = new RelayCommand(_ => ClearResults());
            FindFirstOccurrenceCommand = new RelayCommand(_ => FindFirstOccurrence(), _ => Results.Any());
            OpenAllFilesCommand = new RelayCommand(_ => { });

            // Location commands
            AddLocationCommand = new RelayCommand(_ => AddLocation());
            EditLocationCommand = new RelayCommand(_ => EditLocation(), _ => SelectedLocation != null);
            RemoveLocationCommand = new RelayCommand(_ => RemoveLocation(), _ => SelectedLocation != null);
            TestLocationCommand = new RelayCommand(async _ => await TestLocationAsync(), _ => SelectedLocation != null);

            // Condition commands
            AddGroupCommand = new RelayCommand(_ => ConditionGroups.Add(new ConditionGroupVM()));
            RemoveGroupCommand = new RelayCommand(g => { if (g is ConditionGroupVM gvm) ConditionGroups.Remove(gvm); }, _ => ConditionGroups.Count > 1);
            AddConditionCommand = new RelayCommand(g => { if (g is ConditionGroupVM gvm) gvm.Conditions.Add(new ConditionVM()); });
            RemoveConditionCommand = new RelayCommand(c =>
            {
                if (c is ConditionVM cvm)
                    foreach (var grp in ConditionGroups)
                        grp.Conditions.Remove(cvm);
            });

            // Config commands
            SaveConfigCommand = new RelayCommand(_ => SaveConfig());
            LoadConfigCommand = new RelayCommand(_ => LoadConfig());

            // Profile commands
            LoadProfileCommand = new RelayCommand(_ => LoadSelectedProfile(), _ => SelectedProfile != null);
            DeleteProfileCommand = new RelayCommand(_ => DeleteSelectedProfile(), _ => SelectedProfile != null);
            RenameProfileCommand = new RelayCommand(_ => RenameSelectedProfile(), _ => SelectedProfile != null);
            ImportProfileCommand = new RelayCommand(_ => ImportProfile());

            // Export commands
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Results.Any());
            ExportJsonCommand = new RelayCommand(_ => ExportJson(), _ => Results.Any());
            ExportReportCommand = new RelayCommand(_ => ExportReport(), _ => Results.Any());

            // Schedule commands
            AddScheduleCommand = new RelayCommand(_ => AddSchedule());
            EditScheduleCommand = new RelayCommand(_ => EditSchedule(), _ => SelectedSchedule != null);
            RemoveScheduleCommand = new RelayCommand(_ => RemoveSchedule(), _ => SelectedSchedule != null);
            RunScheduleNowCommand = new RelayCommand(async _ => await RunScheduleNowAsync(), _ => SelectedSchedule != null);
        }

        #endregion

        #region Properties

        // --- Results ---
        private ObservableRangeCollection<GrepResult> _results;
        public ObservableRangeCollection<GrepResult> Results
        {
            get => _results;
            set { _results = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResultCount)); }
        }
        public int ResultCount => Results?.Count ?? 0;

        // --- Quick search ---
        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set { if (_searchQuery != value) { _searchQuery = value; OnPropertyChanged(); } }
        }

        private SearchField _selectedQuickSearchField = SearchField.Any;
        public SearchField SelectedQuickSearchField
        {
            get => _selectedQuickSearchField;
            set { if (_selectedQuickSearchField != value) { _selectedQuickSearchField = value; OnPropertyChanged(); } }
        }

        private bool _useRegex;
        public bool UseRegex
        {
            get => _useRegex;
            set { if (_useRegex != value) { _useRegex = value; OnPropertyChanged(); } }
        }

        // --- Log type filters ---
        private bool _searchPLC;
        public bool SearchPLC
        {
            get => _searchPLC;
            set { if (_searchPLC != value) { _searchPLC = value; OnPropertyChanged(); } }
        }

        private bool _searchAPP;
        public bool SearchAPP
        {
            get => _searchAPP;
            set { if (_searchAPP != value) { _searchAPP = value; OnPropertyChanged(); } }
        }

        // --- Time filters ---
        private DateTime? _fileTimeFrom;
        public DateTime? FileTimeFrom
        {
            get => _fileTimeFrom;
            set { _fileTimeFrom = value; OnPropertyChanged(); }
        }

        private DateTime? _fileTimeTo;
        public DateTime? FileTimeTo
        {
            get => _fileTimeTo;
            set { _fileTimeTo = value; OnPropertyChanged(); }
        }

        private DateTime? _resultTimeFrom;
        public DateTime? ResultTimeFrom
        {
            get => _resultTimeFrom;
            set { _resultTimeFrom = value; OnPropertyChanged(); }
        }

        private DateTime? _resultTimeTo;
        public DateTime? ResultTimeTo
        {
            get => _resultTimeTo;
            set { _resultTimeTo = value; OnPropertyChanged(); }
        }

        // --- Status ---
        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set { if (_isSearching != value) { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotSearching)); CommandManager.InvalidateRequerySuggested(); } }
        }
        public bool IsNotSearching => !IsSearching;

        private string? _statusMessage;
        public string? StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        private string? _searchDuration;
        public string? SearchDuration
        {
            get => _searchDuration;
            set { if (_searchDuration != value) { _searchDuration = value; OnPropertyChanged(); } }
        }

        private int _progressCurrent;
        public int ProgressCurrent
        {
            get => _progressCurrent;
            set { if (_progressCurrent != value) { _progressCurrent = value; OnPropertyChanged(); } }
        }

        private int _progressTotal;
        public int ProgressTotal
        {
            get => _progressTotal;
            set { if (_progressTotal != value) { _progressTotal = value; OnPropertyChanged(); } }
        }

        private GrepResult? _selectedResult;
        public GrepResult? SelectedResult
        {
            get => _selectedResult;
            set { if (_selectedResult != value) { _selectedResult = value; OnPropertyChanged(); } }
        }

        // --- Locations ---
        public ObservableCollection<SearchLocation> Locations { get; }

        private SearchLocation? _selectedLocation;
        public SearchLocation? SelectedLocation
        {
            get => _selectedLocation;
            set { if (_selectedLocation != value) { _selectedLocation = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        }

        // --- Condition groups ---
        public ObservableCollection<ConditionGroupVM> ConditionGroups { get; }

        /// <summary>
        /// Controls the Advanced Conditions expander. True when any conditions have values.
        /// </summary>
        private bool _hasConditions;
        public bool HasConditions
        {
            get => _hasConditions;
            set { if (_hasConditions != value) { _hasConditions = value; OnPropertyChanged(); } }
        }

        private LogicalGroupOperator _selectedGroupOperator;
        public LogicalGroupOperator SelectedGroupOperator
        {
            get => _selectedGroupOperator;
            set { _selectedGroupOperator = value; OnPropertyChanged(); }
        }

        // Combo box sources
        public List<SearchField> FieldOptions { get; }
        public List<SearchOperator> OperatorOptions { get; }
        public List<ConditionOperator> ConditionOperatorOptions { get; }
        public List<LogicalGroupOperator> GroupOperatorOptions { get; }

        // --- Saved profiles ---
        private ObservableCollection<string> _savedProfiles;
        public ObservableCollection<string> SavedProfiles
        {
            get => _savedProfiles;
            set { _savedProfiles = value; OnPropertyChanged(); }
        }

        private string? _selectedProfile;
        public string? SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); UpdateProfilePreview(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string? _profilePreview;
        public string? ProfilePreview
        {
            get => _profilePreview;
            set { _profilePreview = value; OnPropertyChanged(); }
        }

        // --- Schedules ---
        public ObservableCollection<ScheduledSearch> Schedules { get; }

        private ScheduledSearch? _selectedSchedule;
        public ScheduledSearch? SelectedSchedule
        {
            get => _selectedSchedule;
            set { if (_selectedSchedule != value) { _selectedSchedule = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        }

        // --- Backward compat ---
        private string? _externalPath;
        public string? ExternalPath
        {
            get => _externalPath;
            set { if (_externalPath != value) { _externalPath = value; OnPropertyChanged(); } }
        }

        #endregion

        #region Commands

        public ICommand SearchCommand { get; }
        public ICommand CancelSearchCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand FindFirstOccurrenceCommand { get; }
        public ICommand OpenAllFilesCommand { get; }

        public ICommand AddLocationCommand { get; }
        public ICommand EditLocationCommand { get; }
        public ICommand RemoveLocationCommand { get; }
        public ICommand TestLocationCommand { get; }

        public ICommand AddGroupCommand { get; }
        public ICommand RemoveGroupCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }

        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }

        public ICommand LoadProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand RenameProfileCommand { get; }
        public ICommand ImportProfileCommand { get; }

        public ICommand ExportCsvCommand { get; }
        public ICommand ExportJsonCommand { get; }
        public ICommand ExportReportCommand { get; }

        public ICommand AddScheduleCommand { get; }
        public ICommand EditScheduleCommand { get; }
        public ICommand RemoveScheduleCommand { get; }
        public ICommand RunScheduleNowCommand { get; }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _schedulerService?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Enums

        public enum SearchModeType
        {
            LoadedSessions,
            ExternalFiles
        }

        public SearchModeType SearchMode { get; set; } = SearchModeType.LoadedSessions;

        #endregion
    }

    #region Condition ViewModels

    /// <summary>
    /// ViewModel wrapper for a <see cref="SearchConditionGroup"/> (observable for UI binding).
    /// </summary>
    public class ConditionGroupVM : INotifyPropertyChanged
    {
        private ConditionOperator _operator = ConditionOperator.And;
        public ConditionOperator Operator
        {
            get => _operator;
            set { _operator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operator))); }
        }

        public ObservableCollection<ConditionVM> Conditions { get; } = new ObservableCollection<ConditionVM>();

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// ViewModel wrapper for a single <see cref="SearchCondition"/> (observable for UI binding).
    /// </summary>
    public class ConditionVM : INotifyPropertyChanged
    {
        private SearchField _field = SearchField.Any;
        public SearchField Field
        {
            get => _field;
            set { _field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Field))); }
        }

        private SearchOperator _operator = SearchOperator.Contains;
        public SearchOperator Operator
        {
            get => _operator;
            set { _operator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operator))); }
        }

        private string? _value;
        public string? Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }

        private bool _negate;
        public bool Negate
        {
            get => _negate;
            set { _negate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Negate))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    #endregion
}
