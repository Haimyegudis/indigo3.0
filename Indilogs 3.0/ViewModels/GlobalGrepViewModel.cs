using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IEmailNotificationService _emailService;
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
            IEmailNotificationService emailService,
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
            _emailService = emailService;
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
}
