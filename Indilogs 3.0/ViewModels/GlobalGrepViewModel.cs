using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the enhanced Global Grep window.
    /// Supports multi-location, structured criteria, profiles, and scheduling.
    /// </summary>
    public class GlobalGrepViewModel : ViewModelBase
    {
        private readonly GlobalGrepService _grepService;
        private readonly SearchLocationService _locationService;
        private readonly SearchConfigService _configService;
        private readonly SearchSchedulerService _schedulerService;
        private CancellationTokenSource _cancellationTokenSource;
        private SearchReportParams _lastSearchParams;

        // Streaming results: background threads enqueue, timer flushes to UI
        private readonly ConcurrentQueue<GrepResult> _resultQueue = new ConcurrentQueue<GrepResult>();
        private System.Threading.Timer _flushTimer;
        private int _streamedResultCount;

        /// <summary>
        /// Raised when a scheduled "Run Now" wants to close the window.
        /// The string arg is the schedule name.
        /// </summary>
        public event Action<string> RequestCloseForScheduledRun;

        /// <summary>
        /// Raised when a scheduled "Run Now" background search completes.
        /// Args: schedule name, HTML report path (null if no report).
        /// </summary>
        public event Action<string, string> ScheduledRunCompleted;

        // Kept for backward compat (in-memory loaded session search)
        private IEnumerable<LogSessionData> LoadedSessions { get; }

        #region Constructor

        public GlobalGrepViewModel(IEnumerable<LogSessionData> loadedSessions)
        {
            _grepService = new GlobalGrepService();
            _locationService = new SearchLocationService();
            _configService = new SearchConfigService();
            _schedulerService = new SearchSchedulerService(_grepService, _locationService);
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
        private string _searchQuery;
        public string SearchQuery
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

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        private string _searchDuration;
        public string SearchDuration
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

        private GrepResult _selectedResult;
        public GrepResult SelectedResult
        {
            get => _selectedResult;
            set { if (_selectedResult != value) { _selectedResult = value; OnPropertyChanged(); } }
        }

        // --- Locations ---
        public ObservableCollection<SearchLocation> Locations { get; }

        private SearchLocation _selectedLocation;
        public SearchLocation SelectedLocation
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

        private string _selectedProfile;
        public string SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); UpdateProfilePreview(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _profilePreview;
        public string ProfilePreview
        {
            get => _profilePreview;
            set { _profilePreview = value; OnPropertyChanged(); }
        }

        // --- Schedules ---
        public ObservableCollection<ScheduledSearch> Schedules { get; }

        private ScheduledSearch _selectedSchedule;
        public ScheduledSearch SelectedSchedule
        {
            get => _selectedSchedule;
            set { if (_selectedSchedule != value) { _selectedSchedule = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        }

        // --- Backward compat ---
        private string _externalPath;
        public string ExternalPath
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

        #region Search Execution

        /// <summary>
        /// Flushes queued results from background threads to the UI ObservableCollection.
        /// Called by a timer every 150ms during search.
        /// </summary>
        private void FlushResultsToUI(object state)
        {
            if (_resultQueue.IsEmpty) return;

            var batch = new List<GrepResult>();
            while (_resultQueue.TryDequeue(out var result))
                batch.Add(result);

            if (batch.Count == 0) return;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Results.AddRange(batch);
                    OnPropertyChanged(nameof(ResultCount));
                    StatusMessage = $"Searching... {Results.Count:N0} result(s) found so far";
                });
            }
            catch (TaskCanceledException) { }
        }

        private async Task ExecuteSearchAsync()
        {
            if (IsSearching) return;
            IsSearching = true;
            Results.Clear();
            OnPropertyChanged(nameof(ResultCount));
            StatusMessage = "Preparing search...";
            SearchDuration = "";
            _streamedResultCount = 0;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Start flush timer — pushes queued results to UI every 150ms
            _flushTimer = new System.Threading.Timer(FlushResultsToUI, null, 150, 150);

            try
            {
                var progress = new Progress<(int current, int total, string status)>(p =>
                {
                    ProgressCurrent = p.current;
                    ProgressTotal = p.total;
                    // Don't overwrite "X results found so far" with location progress
                });

                var activeLocations = Locations.Where(l => l.IsActive).ToList();

                bool hasSearchText = !string.IsNullOrWhiteSpace(SearchQuery);
                bool hasConditions = ConditionGroups.Any(g => g.Conditions.Any(c => !string.IsNullOrWhiteSpace(c.Value)));
                bool hasLocations = activeLocations.Any();
                bool hasLoadedSessions = LoadedSessions?.Any() == true;

                string searchPattern = hasSearchText ? SearchQuery : "(no quick-search text)";
                string conditionsSummary = hasConditions ? BuildCriteriaSummary() : "(no structured conditions)";

                AppLogger.Info($"[Grep] ════════════════════════════════════════════════════");
                AppLogger.Info($"[Grep] SEARCH STARTED at {DateTime.Now:HH:mm:ss.fff}");
                AppLogger.Info($"[Grep] Pattern: \"{searchPattern}\"");
                AppLogger.Info($"[Grep] Field: {SelectedQuickSearchField}, Regex: {UseRegex}, PLC: {SearchPLC}, APP: {SearchAPP}");
                if (hasConditions)
                    AppLogger.Info($"[Grep] Conditions: {conditionsSummary}");
                AppLogger.Info($"[Grep] Locations configured: {activeLocations.Count}, Loaded sessions: {(LoadedSessions as System.Collections.ICollection)?.Count ?? 0}");

                if (!hasSearchText && !hasConditions)
                {
                    StatusMessage = "Please enter a search query or add search conditions.";
                    AppLogger.Warn("[Grep] SEARCH ABORTED — no search text and no conditions provided");
                    return;
                }

                SearchCriteria criteria;
                if (hasConditions)
                    criteria = BuildCriteria();
                else
                    criteria = BuildQuickSearchCriteria();

                // Streaming callback: enqueue results for batched UI flush
                Action<GrepResult> onResult = result =>
                {
                    _resultQueue.Enqueue(result);
                };

                if (hasLocations)
                {
                    AppLogger.Info($"[Grep] MODE: Multi-location search across {activeLocations.Count} location(s)");
                    foreach (var loc in activeLocations)
                        AppLogger.Info($"[Grep]   Location: \"{loc.Name}\" — {loc.BasePath}");
                    await _grepService.SearchMultiLocationAsync(
                        criteria, activeLocations, progress, _cancellationTokenSource.Token, onResult);
                }
                else if (hasLoadedSessions)
                {
                    var sessionsList = LoadedSessions.ToList();
                    AppLogger.Info($"[Grep] MODE: In-memory loaded sessions ({sessionsList.Count} session(s))");
                    foreach (var s in sessionsList)
                        AppLogger.Info($"[Grep]   Session: \"{s.FileName}\" — PLC:{s.Logs?.Count ?? 0} entries, APP:{s.AppDevLogs?.Count ?? 0} entries");
                    await _grepService.SearchLoadedSessionsWithCriteriaAsync(
                        LoadedSessions, criteria, progress, _cancellationTokenSource.Token, onResult);
                }
                else
                {
                    StatusMessage = "No search target available. Add a search location or load a log file in the main window first.";
                    AppLogger.Warn("[Grep] SEARCH ABORTED — no locations configured and no loaded sessions available");
                    return;
                }

                // Final flush — push any remaining queued results
                _flushTimer?.Dispose();
                _flushTimer = null;
                FlushResultsToUI(null);

                sw.Stop();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(ResultCount));
                    SearchDuration = $"({sw.ElapsedMilliseconds:N0}ms)";

                    if (Results.Count > 0)
                    {
                        StatusMessage = $"Search complete. Found {Results.Count:N0} result(s).";
                        AppLogger.Info($"[Grep] SEARCH COMPLETE — {Results.Count:N0} result(s) found in {sw.ElapsedMilliseconds:N0}ms");
                    }
                    else
                    {
                        StatusMessage = $"Search complete. No results found for \"{searchPattern}\".";
                        AppLogger.Warn($"[Grep] SEARCH COMPLETE — 0 results for \"{searchPattern}\" in {sw.ElapsedMilliseconds:N0}ms");
                    }

                    var locationNames = hasLocations
                        ? activeLocations.Select(l => l.Name).ToList()
                        : LoadedSessions?.Select(s => Path.GetFileName(s.FileName) ?? "Loaded session").ToList()
                          ?? new List<string>();
                    _lastSearchParams = new SearchReportParams
                    {
                        LocationNames = locationNames,
                        QueryText = SearchQuery,
                        CriteriaSummary = BuildCriteriaSummary(),
                        SearchDuration = $"{sw.ElapsedMilliseconds:N0}ms",
                        LogTypes = (SearchPLC && SearchAPP) ? "PLC + APP" : SearchPLC ? "PLC" : SearchAPP ? "APP" : "None",
                        FileTimeRange = FormatTimeRange(FileTimeFrom, FileTimeTo),
                        ResultTimeRange = FormatTimeRange(ResultTimeFrom, ResultTimeTo)
                    };
                });
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
                AppLogger.Info($"[Grep] SEARCH CANCELLED by user after {sw.ElapsedMilliseconds:N0}ms");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
                AppLogger.Error($"[Grep] SEARCH FAILED after {sw.ElapsedMilliseconds:N0}ms", ex);
            }
            finally
            {
                // Ensure timer is stopped
                _flushTimer?.Dispose();
                _flushTimer = null;
                // Final flush in case of cancellation/error
                FlushResultsToUI(null);
                IsSearching = false;
                AppLogger.Info($"[Grep] ════════════════════════════════════════════════════");
            }
        }

        private SearchCriteria BuildCriteria()
        {
            var criteria = new SearchCriteria
            {
                SearchPLC = SearchPLC,
                SearchAPP = SearchAPP,
                GroupOperator = SelectedGroupOperator,
                Groups = ConditionGroups.Select(g => new SearchConditionGroup
                {
                    Operator = g.Operator,
                    Conditions = g.Conditions
                        .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                        .Select(c => new SearchCondition
                        {
                            Field = c.Field,
                            Operator = c.Operator,
                            Value = c.Value,
                            Negate = c.Negate
                        }).ToList()
                }).Where(g => g.Conditions.Count > 0).ToList()
            };

            if (FileTimeFrom.HasValue || FileTimeTo.HasValue)
                criteria.FileTimeFilter = new TimeRangeFilter { From = FileTimeFrom, To = FileTimeTo };

            if (ResultTimeFrom.HasValue || ResultTimeTo.HasValue)
                criteria.ResultTimeFilter = new TimeRangeFilter { From = ResultTimeFrom, To = ResultTimeTo };

            return criteria;
        }

        private SearchCriteria BuildQuickSearchCriteria()
        {
            var criteria = new SearchCriteria
            {
                SearchPLC = SearchPLC,
                SearchAPP = SearchAPP,
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition
                            {
                                Field = SelectedQuickSearchField,
                                Operator = UseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                                Value = SearchQuery
                            }
                        }
                    }
                }
            };

            if (FileTimeFrom.HasValue || FileTimeTo.HasValue)
                criteria.FileTimeFilter = new TimeRangeFilter { From = FileTimeFrom, To = FileTimeTo };
            if (ResultTimeFrom.HasValue || ResultTimeTo.HasValue)
                criteria.ResultTimeFilter = new TimeRangeFilter { From = ResultTimeFrom, To = ResultTimeTo };

            return criteria;
        }

        private void CancelSearch()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling search...";
        }

        private void ClearResults()
        {
            Results.Clear();
            OnPropertyChanged(nameof(ResultCount));
            StatusMessage = "Results cleared.";
            SearchDuration = "";
            SelectedResult = null;
        }

        private void FindFirstOccurrence()
        {
            var first = Results.Where(r => r.Timestamp.HasValue).OrderBy(r => r.Timestamp.Value).FirstOrDefault();
            if (first != null)
            {
                SelectedResult = first;
                StatusMessage = $"First occurrence: {first.TimestampDisplay} in {first.SessionName}";
            }
        }

        public List<(string FilePath, string SessionName)> GetUniqueFiles()
        {
            return Results
                .Where(r => !string.IsNullOrWhiteSpace(r.FilePath) && !string.IsNullOrWhiteSpace(r.SessionName))
                .Select(r => (r.FilePath, r.SessionName))
                .Distinct()
                .OrderBy(f => f.SessionName)
                .ToList();
        }

        #endregion

        #region Location Management

        private void AddLocation()
        {
            var result = ShowLocationDialog("Add Search Location", "", "", "");
            if (result == null) return;

            var loc = new SearchLocation { Name = result.Value.name, Address = result.Value.address, BasePath = result.Value.path };
            _locationService.Add(loc);
            Locations.Add(loc);
        }

        private void EditLocation()
        {
            if (SelectedLocation == null) return;
            var result = ShowLocationDialog("Edit Search Location", SelectedLocation.Name, SelectedLocation.Address, SelectedLocation.BasePath);
            if (result == null) return;

            SelectedLocation.Name = result.Value.name;
            SelectedLocation.Address = result.Value.address;
            SelectedLocation.BasePath = result.Value.path;
            _locationService.Update(SelectedLocation);
        }

        private void RemoveLocation()
        {
            if (SelectedLocation == null) return;
            if (MessageBox.Show($"Remove location '{SelectedLocation.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _locationService.Remove(SelectedLocation.Id);
                Locations.Remove(SelectedLocation);
            }
        }

        private async Task TestLocationAsync()
        {
            if (SelectedLocation == null) return;
            StatusMessage = $"Testing connectivity to {SelectedLocation.Name}...";
            var status = await _locationService.TestConnectivityAsync(SelectedLocation);
            StatusMessage = $"{SelectedLocation.Name}: {status}";
        }

        private (string name, string address, string path)? ShowLocationDialog(string title, string name, string address, string path)
        {
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var textSecondary = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");
            var primaryColor = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor");

            var dialog = new Window
            {
                Title = title,
                Width = 480,
                Height = 340,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = bgDark
            };

            var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

            // --- Name field ---
            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Friendly Name",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameHint = new System.Windows.Controls.TextBlock
            {
                Text = "A short label for this location (e.g. \"Simulator 1\")",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameBox = new System.Windows.Controls.TextBox
            {
                Text = name ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };

            // --- Address field ---
            var addrLabel = new System.Windows.Controls.TextBlock
            {
                Text = "IP Address / Hostname",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addrHint = new System.Windows.Controls.TextBlock
            {
                Text = "Machine IP or hostname (e.g. \"192.168.1.10\" or \"localhost\")",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addrBox = new System.Windows.Controls.TextBox
            {
                Text = address ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };

            // --- Path field with Browse ---
            var pathLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Log Folder Path",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var pathHint = new System.Windows.Controls.TextBlock
            {
                Text = "Folder containing log files (local, mapped drive, or UNC path)",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var pathRow = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var browseBtn = new System.Windows.Controls.Button
            {
                Content = "Browse...",
                Width = 75,
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(6, 0, 0, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(browseBtn, System.Windows.Controls.Dock.Right);
            var pathBox = new System.Windows.Controls.TextBox
            {
                Text = path ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush
            };
            browseBtn.Click += (s, e) =>
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Select the folder containing log files";
                    if (!string.IsNullOrWhiteSpace(pathBox.Text) && Directory.Exists(pathBox.Text))
                        fbd.SelectedPath = pathBox.Text;
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        pathBox.Text = fbd.SelectedPath;
                }
            };
            pathRow.Children.Add(browseBtn);
            pathRow.Children.Add(pathBox);

            // --- Buttons ---
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Padding = new Thickness(6, 6, 6, 6),
                Background = primaryColor,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(6, 6, 6, 6),
                Background = bgCard,
                Foreground = textPrimary,
                IsCancel = true
            };

            (string name, string address, string path)? result = null;
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    MessageBox.Show("Please enter a name.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = (nameBox.Text.Trim(), addrBox.Text.Trim(), pathBox.Text.Trim());
                dialog.Close();
            };
            cancelBtn.Click += (s, e) => dialog.Close();

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);

            root.Children.Add(nameLabel);
            root.Children.Add(nameHint);
            root.Children.Add(nameBox);
            root.Children.Add(addrLabel);
            root.Children.Add(addrHint);
            root.Children.Add(addrBox);
            root.Children.Add(pathLabel);
            root.Children.Add(pathHint);
            root.Children.Add(pathRow);
            root.Children.Add(btnPanel);

            dialog.Content = root;
            dialog.ShowDialog();
            return result;
        }

        private string PromptInput(string title, string prompt, string defaultValue)
        {
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");

            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = bgDark
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            var label = new System.Windows.Controls.TextBlock { Text = prompt, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 5) };
            var textBox = new System.Windows.Controls.TextBox { Text = defaultValue ?? "", Padding = new Thickness(6, 4, 6, 4), Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush };
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 5, 0), IsDefault = true };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 70, IsCancel = true };
            string result = null;
            okBtn.Click += (s, e) => { result = textBox.Text; dialog.Close(); };
            cancelBtn.Click += (s, e) => dialog.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;
            dialog.ShowDialog();
            return result;
        }

        #endregion

        #region Config / Profile Management

        private void SaveConfig()
        {
            var name = PromptInput("Save Profile", "Profile name:", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            var profile = new SearchProfile
            {
                Name = name,
                Locations = Locations.ToList(),
                Criteria = BuildCriteria()
            };
            _configService.SaveProfile(profile);
            RefreshSavedProfiles();
            StatusMessage = $"Profile '{name}' saved.";
        }

        private void LoadConfig()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Search Profile (*.json)|*.json",
                Title = "Load Search Profile"
            };
            if (dlg.ShowDialog() != true) return;
            var profile = _configService.LoadProfileFromFile(dlg.FileName);
            if (profile != null) ApplyProfile(profile);
        }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile == null) return;
            var profile = _configService.LoadProfile(SelectedProfile);
            if (profile != null) ApplyProfile(profile);
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedProfile == null) return;
            if (MessageBox.Show($"Delete profile '{SelectedProfile}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _configService.DeleteProfile(SelectedProfile);
            RefreshSavedProfiles();
        }

        private void RenameSelectedProfile()
        {
            if (SelectedProfile == null) return;
            var newName = PromptInput("Rename Profile", "New name:", SelectedProfile);
            if (string.IsNullOrWhiteSpace(newName)) return;
            _configService.RenameProfile(SelectedProfile, newName);
            RefreshSavedProfiles();
        }

        private void ImportProfile()
        {
            var dlg = new OpenFileDialog { Filter = "Search Profile (*.json)|*.json", Title = "Import Profile" };
            if (dlg.ShowDialog() != true) return;
            _configService.ImportProfile(dlg.FileName);
            RefreshSavedProfiles();
            StatusMessage = "Profile imported.";
        }

        private void ApplyProfile(SearchProfile profile)
        {
            // Apply locations
            Locations.Clear();
            foreach (var loc in profile.Locations)
                Locations.Add(loc);
            _locationService.Save();

            // Apply criteria
            var c = profile.Criteria;
            if (c != null)
            {
                SearchPLC = c.SearchPLC;
                SearchAPP = c.SearchAPP;
                SelectedGroupOperator = c.GroupOperator;
                FileTimeFrom = c.FileTimeFilter?.From;
                FileTimeTo = c.FileTimeFilter?.To;
                ResultTimeFrom = c.ResultTimeFilter?.From;
                ResultTimeTo = c.ResultTimeFilter?.To;

                ConditionGroups.Clear();
                if (c.Groups != null && c.Groups.Count > 0)
                {
                    foreach (var g in c.Groups)
                    {
                        var gvm = new ConditionGroupVM { Operator = g.Operator };
                        foreach (var cond in g.Conditions)
                            gvm.Conditions.Add(new ConditionVM
                            {
                                Field = cond.Field,
                                Operator = cond.Operator,
                                Value = cond.Value,
                                Negate = cond.Negate
                            });
                        ConditionGroups.Add(gvm);
                    }
                }
                else
                {
                    ConditionGroups.Add(new ConditionGroupVM());
                }
            }

            StatusMessage = $"Profile '{profile.Name}' loaded.";
        }

        private void RefreshSavedProfiles()
        {
            SavedProfiles = new ObservableCollection<string>(_configService.ListProfiles());
        }

        private void UpdateProfilePreview()
        {
            if (string.IsNullOrEmpty(SelectedProfile))
            {
                ProfilePreview = "";
                return;
            }
            var profile = _configService.LoadProfile(SelectedProfile);
            if (profile == null) { ProfilePreview = "Profile not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Name: {profile.Name}");
            sb.AppendLine($"Locations: {profile.Locations?.Count ?? 0}");
            if (profile.Locations != null)
                foreach (var loc in profile.Locations)
                    sb.AppendLine($"  - {loc.Name} ({loc.Address}) → {loc.BasePath}");
            if (profile.Criteria != null)
            {
                sb.AppendLine($"PLC: {profile.Criteria.SearchPLC}, APP: {profile.Criteria.SearchAPP}");
                sb.AppendLine($"Groups: {profile.Criteria.Groups?.Count ?? 0} (operator: {profile.Criteria.GroupOperator})");
                if (profile.Criteria.Groups != null)
                    foreach (var g in profile.Criteria.Groups)
                    {
                        sb.AppendLine($"  Group ({g.Operator}):");
                        foreach (var c in g.Conditions)
                            sb.AppendLine($"    {(c.Negate ? "NOT " : "")}{c.Field} {c.Operator} \"{c.Value}\"");
                    }
            }
            ProfilePreview = sb.ToString();
        }

        #endregion

        #region Export

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"grep_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dlg.ShowDialog() != true) return;
            using (var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("Timestamp,Location,FilePath,LineNumber,LogType,Thread,Logger,Method,Data,Message");
                foreach (var r in Results)
                {
                    var e = r.ReferencedLogEntry;
                    writer.WriteLine($"\"{r.TimestampDisplay}\",\"{Esc(r.LocationName)}\",\"{Esc(r.FilePath)}\",{r.LineNumber},\"{Esc(r.LogType)}\",\"{Esc(e?.ThreadName)}\",\"{Esc(e?.Logger)}\",\"{Esc(e?.Method)}\",\"{Esc(e?.Data)}\",\"{Esc(e?.Message)}\"");
                }
            }
            StatusMessage = $"Exported {Results.Count:N0} results to CSV.";
        }

        private void ExportJson()
        {
            var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = $"grep_results_{DateTime.Now:yyyyMMdd_HHmmss}.json" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(Results.ToList(), Formatting.Indented));
            StatusMessage = $"Exported {Results.Count:N0} results to JSON.";
        }

        private static string Esc(string s) => s?.Replace("\"", "\"\"") ?? "";

        private void ExportReport()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "HTML Report (*.html)|*.html",
                FileName = $"search_report_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };
            if (dlg.ShowDialog() != true) return;

            var reportParams = _lastSearchParams ?? new SearchReportParams
            {
                LocationNames = Locations.Where(l => l.IsActive).Select(l => l.Name).ToList(),
                QueryText = SearchQuery,
                CriteriaSummary = BuildCriteriaSummary(),
                SearchDuration = SearchDuration,
                LogTypes = (SearchPLC && SearchAPP) ? "PLC + APP" : SearchPLC ? "PLC" : SearchAPP ? "APP" : "None"
            };

            SearchReportService.GenerateHtmlReport(dlg.FileName, reportParams, Results.ToList());
            StatusMessage = $"Report saved to {Path.GetFileName(dlg.FileName)}.";

            try { System.Diagnostics.Process.Start(dlg.FileName); }
            catch { /* browser not available */ }
        }

        private string BuildCriteriaSummary()
        {
            var parts = new List<string>();
            foreach (var group in ConditionGroups)
            {
                var conditions = group.Conditions
                    .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                    .Select(c => $"{(c.Negate ? "NOT " : "")}{c.Field} {c.Operator} \"{c.Value}\"");
                if (conditions.Any())
                    parts.Add($"({string.Join($" {group.Operator} ", conditions)})");
            }
            return parts.Count > 0 ? string.Join($" {SelectedGroupOperator} ", parts) : "";
        }

        private static string FormatTimeRange(DateTime? from, DateTime? to)
        {
            if (!from.HasValue && !to.HasValue) return null;
            string f = from?.ToString("yyyy-MM-dd") ?? "...";
            string t = to?.ToString("yyyy-MM-dd") ?? "...";
            return $"{f} to {t}";
        }

        #endregion

        #region Schedule Management

        /// <summary>
        /// Builds a combined SearchCriteria from ALL current UI settings:
        /// quick search text (Section 2), structured conditions (Section 3),
        /// PLC/APP toggles, and time filters.
        /// </summary>
        private SearchCriteria BuildFullCriteria()
        {
            // Start with structured conditions + PLC/APP + time filters
            var criteria = BuildCriteria();

            // If there's also quick search text, add it as an additional group
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var quickGroup = new SearchConditionGroup
                {
                    Operator = ConditionOperator.Or,
                    Conditions = new List<SearchCondition>
                    {
                        new SearchCondition
                        {
                            Field = SelectedQuickSearchField,
                            Operator = UseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                            Value = SearchQuery
                        }
                    }
                };
                criteria.Groups.Insert(0, quickGroup);
            }

            return criteria;
        }

        private void AddSchedule()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true },
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(8, 0, 0),
                OutputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "IndiLogs_GrepResults")
            };

            if (!ShowScheduleDialog("New Scheduled Search", schedule)) return;

            _schedulerService.AddSchedule(schedule);
            Schedules.Add(schedule);
            StatusMessage = $"Schedule '{schedule.Name}' added.";
        }

        private void EditSchedule()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;

            if (!ShowScheduleDialog("Edit Scheduled Search", schedule)) return;

            _schedulerService.UpdateSchedule(schedule);
            var idx = Schedules.IndexOf(schedule);
            if (idx >= 0)
            {
                Schedules.RemoveAt(idx);
                Schedules.Insert(idx, schedule);
                SelectedSchedule = schedule;
            }
            StatusMessage = $"Schedule '{schedule.Name}' updated.";
        }

        /// <summary>
        /// Self-contained schedule editor dialog. Configures EVERYTHING:
        /// name, timing, search criteria, locations, time filters, output.
        /// </summary>
        private bool ShowScheduleDialog(string title, ScheduledSearch schedule)
        {
            // ═══ THEME RESOURCES ═══
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var bgPanel = (System.Windows.Media.Brush)Application.Current.FindResource("BgPanel");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var textSecondary = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");
            var primaryColor = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor");
            System.Windows.Media.Brush dangerColor = null;
            try { dangerColor = (System.Windows.Media.Brush)Application.Current.FindResource("DangerColor"); } catch { }
            dangerColor = dangerColor ?? System.Windows.Media.Brushes.Red;

            bool dialogResult = false;

            // ═══ PARSE EXISTING CRITERIA ═══
            bool isSimpleMode = true;
            string existingSearchText = "";
            SearchField existingField = SearchField.Any;
            bool existingRegex = false;
            var existingConditions = new List<SearchCondition>();
            ConditionOperator existingCondOp = ConditionOperator.And;
            bool existingPLC = schedule.Criteria?.SearchPLC ?? true;
            bool existingAPP = schedule.Criteria?.SearchAPP ?? true;
            var existingLocIds = new HashSet<Guid>(schedule.Criteria?.LocationIds ?? new List<Guid>());
            DateTime? existingFileFrom = schedule.Criteria?.FileTimeFilter?.From;
            DateTime? existingFileTo = schedule.Criteria?.FileTimeFilter?.To;
            DateTime? existingResFrom = schedule.Criteria?.ResultTimeFilter?.From;
            DateTime? existingResTo = schedule.Criteria?.ResultTimeFilter?.To;

            if (schedule.Criteria?.Groups != null && schedule.Criteria.Groups.Count > 0)
            {
                var allConds = schedule.Criteria.Groups.SelectMany(g => g.Conditions).ToList();
                if (allConds.Count == 1 && schedule.Criteria.Groups.Count == 1)
                {
                    existingField = allConds[0].Field;
                    existingSearchText = allConds[0].Value ?? "";
                    existingRegex = allConds[0].Operator == SearchOperator.Regex;
                }
                else if (allConds.Count > 0)
                {
                    isSimpleMode = false;
                    existingCondOp = schedule.Criteria.Groups[0].Operator;
                    existingConditions.AddRange(allConds);
                }
            }

            // ═══ DIALOG WINDOW ═══
            var dialog = new Window
            {
                Title = title,
                Width = 700,
                Height = 780,
                MaxHeight = SystemParameters.WorkArea.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = bgDark
            };

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Padding = new Thickness(18)
            };
            var root = new System.Windows.Controls.StackPanel();

            // ═══ HELPERS ═══
            Func<string, System.Windows.Controls.TextBlock> makeLabel = text =>
                new System.Windows.Controls.TextBlock
                {
                    Text = text, Foreground = textPrimary,
                    FontWeight = FontWeights.SemiBold, FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 3)
                };

            Func<string, System.Windows.Controls.TextBox> makeTextBox = val =>
                new System.Windows.Controls.TextBox
                {
                    Text = val ?? "", Padding = new Thickness(6, 4, 6, 4),
                    Background = bgCard, Foreground = textPrimary,
                    BorderBrush = borderBrush, FontSize = 12
                };

            Func<string, System.Windows.Controls.StackPanel, System.Windows.Controls.Border> makeSection = (header, content) =>
            {
                content.Children.Insert(0, new System.Windows.Controls.TextBlock
                {
                    Text = header, FontWeight = FontWeights.Bold,
                    Foreground = primaryColor, FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                return new System.Windows.Controls.Border
                {
                    Background = bgPanel, BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10),
                    Child = content
                };
            };

            // ════════════════════════════════════════════════════════
            // SECTION 1: SCHEDULE DETAILS
            // ════════════════════════════════════════════════════════
            var sec1 = new System.Windows.Controls.StackPanel();
            sec1.Children.Add(makeLabel("Schedule Name"));
            var nameBox = makeTextBox(schedule.Name);
            sec1.Children.Add(nameBox);
            var enabledCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Enabled", Foreground = textPrimary,
                IsChecked = schedule.IsEnabled, FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            };
            sec1.Children.Add(enabledCheck);
            root.Children.Add(makeSection("Schedule Details", sec1));

            // ════════════════════════════════════════════════════════
            // SECTION 2: WHEN TO RUN
            // ════════════════════════════════════════════════════════
            var sec2 = new System.Windows.Controls.StackPanel();

            sec2.Children.Add(makeLabel("Schedule Type"));
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                Background = bgCard, Foreground = textPrimary,
                BorderBrush = borderBrush, FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3)
            };
            typeCombo.Items.Add("Once (specific date & time)");
            typeCombo.Items.Add("Daily");
            typeCombo.Items.Add("Weekly");
            typeCombo.Items.Add("Interval (repeat every N minutes)");
            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once: typeCombo.SelectedIndex = 0; break;
                case ScheduleType.Daily: typeCombo.SelectedIndex = 1; break;
                case ScheduleType.Weekly: typeCombo.SelectedIndex = 2; break;
                case ScheduleType.Interval: typeCombo.SelectedIndex = 3; break;
            }
            sec2.Children.Add(typeCombo);

            // Run Date (Once only)
            var dateLabel = makeLabel("Run Date");
            sec2.Children.Add(dateLabel);
            var datePicker = new System.Windows.Controls.DatePicker
            {
                SelectedDate = schedule.RunDate, FontSize = 12
            };
            sec2.Children.Add(datePicker);

            // Run Time (HH:mm)
            var timeLabel = makeLabel("Run Time (HH:mm)");
            sec2.Children.Add(timeLabel);
            var timePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var hourBox = new System.Windows.Controls.TextBox
            {
                Text = schedule.RunTime.Hours.ToString("00"),
                Width = 50, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 14
            };
            var colonLbl = new System.Windows.Controls.TextBlock
            {
                Text = ":", Foreground = textPrimary, FontSize = 16,
                FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            var minuteBox = new System.Windows.Controls.TextBox
            {
                Text = schedule.RunTime.Minutes.ToString("00"),
                Width = 50, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 14
            };
            timePanel.Children.Add(hourBox);
            timePanel.Children.Add(colonLbl);
            timePanel.Children.Add(minuteBox);
            timePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "(24-hour format)", Foreground = textSecondary,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            sec2.Children.Add(timePanel);

            // Days of Week (Weekly only)
            var daysLabel = makeLabel("Run on Days");
            sec2.Children.Add(daysLabel);
            var daysPanel = new System.Windows.Controls.WrapPanel();
            var dayCheckBoxes = new Dictionary<DayOfWeek, System.Windows.Controls.CheckBox>();
            var dayNames = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
            var shortDayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int di = 0; di < dayNames.Length; di++)
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = shortDayNames[di], Foreground = textPrimary,
                    IsChecked = schedule.RunDays?.Contains(dayNames[di]) == true,
                    Margin = new Thickness(0, 0, 10, 0), FontSize = 12
                };
                dayCheckBoxes[dayNames[di]] = cb;
                daysPanel.Children.Add(cb);
            }
            sec2.Children.Add(daysPanel);

            // Repeat Interval (Interval only)
            var intervalLabel = makeLabel("Repeat Every (minutes)");
            sec2.Children.Add(intervalLabel);
            var intervalBox = makeTextBox(schedule.RepeatIntervalMinutes.ToString());
            intervalBox.Width = 80;
            intervalBox.HorizontalAlignment = HorizontalAlignment.Left;
            sec2.Children.Add(intervalBox);

            // Visibility toggling
            Action updateWhenVisibility = () =>
            {
                int idx = typeCombo.SelectedIndex;
                dateLabel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                datePicker.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                bool showTime = idx <= 2;
                timeLabel.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
                timePanel.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
                daysLabel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
                daysPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
                intervalLabel.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
                intervalBox.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            };
            typeCombo.SelectionChanged += (s, e) => updateWhenVisibility();
            updateWhenVisibility();

            root.Children.Add(makeSection("When to Run", sec2));

            // ════════════════════════════════════════════════════════
            // SECTION 3: WHAT TO SEARCH
            // ════════════════════════════════════════════════════════
            var sec3 = new System.Windows.Controls.StackPanel();

            // Search mode toggle
            var modePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var simpleRadio = new System.Windows.Controls.RadioButton
            {
                Content = "Simple Search", Foreground = textPrimary,
                IsChecked = isSimpleMode, FontSize = 12, Margin = new Thickness(0, 0, 20, 0)
            };
            var advancedRadio = new System.Windows.Controls.RadioButton
            {
                Content = "Advanced Conditions", Foreground = textPrimary,
                IsChecked = !isSimpleMode, FontSize = 12
            };
            modePanel.Children.Add(simpleRadio);
            modePanel.Children.Add(advancedRadio);
            sec3.Children.Add(modePanel);

            // --- Simple Search Panel ---
            var simplePanel = new System.Windows.Controls.StackPanel
            {
                Visibility = isSimpleMode ? Visibility.Visible : Visibility.Collapsed
            };
            var simpleFldRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            simpleFldRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Search in:", Foreground = textSecondary, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            var simpleFieldCombo = new System.Windows.Controls.ComboBox
            {
                Width = 110, FontSize = 11, Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush
            };
            foreach (var f in Enum.GetValues(typeof(SearchField)).Cast<SearchField>())
                simpleFieldCombo.Items.Add(f);
            simpleFieldCombo.SelectedItem = existingField;
            simpleFldRow.Children.Add(simpleFieldCombo);
            simplePanel.Children.Add(simpleFldRow);

            simplePanel.Children.Add(makeLabel("Search text"));
            var simpleTextBox = makeTextBox(existingSearchText);
            simplePanel.Children.Add(simpleTextBox);

            var simpleRegexCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Use Regular Expression", Foreground = textPrimary,
                IsChecked = existingRegex, FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
            };
            simplePanel.Children.Add(simpleRegexCheck);
            sec3.Children.Add(simplePanel);

            // --- Advanced Conditions Panel ---
            var advancedPanel = new System.Windows.Controls.StackPanel
            {
                Visibility = isSimpleMode ? Visibility.Collapsed : Visibility.Visible
            };
            var advOpRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            advOpRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Combine conditions with:", Foreground = textSecondary,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var advOperatorCombo = new System.Windows.Controls.ComboBox
            {
                Width = 70, FontSize = 11, Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush
            };
            foreach (var op in Enum.GetValues(typeof(ConditionOperator)).Cast<ConditionOperator>())
                advOperatorCombo.Items.Add(op);
            advOperatorCombo.SelectedItem = existingCondOp;
            advOpRow.Children.Add(advOperatorCombo);
            advancedPanel.Children.Add(advOpRow);

            // Dynamic condition rows
            var condPanel = new System.Windows.Controls.StackPanel();
            var condFields = new List<System.Windows.Controls.ComboBox>();
            var condOps = new List<System.Windows.Controls.ComboBox>();
            var condValues = new List<System.Windows.Controls.TextBox>();
            var condNegates = new List<System.Windows.Controls.CheckBox>();
            var condRows = new List<System.Windows.Controls.Grid>();

            Action addConditionRow = null;
            addConditionRow = () =>
            {
                var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

                var fieldCb = new System.Windows.Controls.ComboBox
                {
                    FontSize = 11, Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };
                foreach (var f in Enum.GetValues(typeof(SearchField)).Cast<SearchField>())
                    fieldCb.Items.Add(f);
                fieldCb.SelectedItem = SearchField.Any;

                var opCb = new System.Windows.Controls.ComboBox
                {
                    FontSize = 11, Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };
                foreach (var op in Enum.GetValues(typeof(SearchOperator)).Cast<SearchOperator>())
                    opCb.Items.Add(op);
                opCb.SelectedItem = SearchOperator.Contains;

                var valBox = new System.Windows.Controls.TextBox
                {
                    FontSize = 11, Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };

                var negCheck = new System.Windows.Controls.CheckBox
                {
                    Content = "Excl", Foreground = textPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0), FontSize = 10
                };

                var removeBtn = new System.Windows.Controls.Button
                {
                    Content = "X", Width = 22, Padding = new Thickness(0, 2, 0, 2),
                    Background = bgCard, Foreground = dangerColor,
                    BorderBrush = borderBrush, FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                System.Windows.Controls.Grid.SetColumn(fieldCb, 0);
                System.Windows.Controls.Grid.SetColumn(opCb, 1);
                System.Windows.Controls.Grid.SetColumn(valBox, 2);
                System.Windows.Controls.Grid.SetColumn(negCheck, 3);
                System.Windows.Controls.Grid.SetColumn(removeBtn, 4);

                row.Children.Add(fieldCb);
                row.Children.Add(opCb);
                row.Children.Add(valBox);
                row.Children.Add(negCheck);
                row.Children.Add(removeBtn);

                condRows.Add(row);
                condFields.Add(fieldCb);
                condOps.Add(opCb);
                condValues.Add(valBox);
                condNegates.Add(negCheck);
                condPanel.Children.Add(row);

                removeBtn.Click += (s, e) =>
                {
                    int ri = condRows.IndexOf(row);
                    if (ri >= 0)
                    {
                        condRows.RemoveAt(ri);
                        condFields.RemoveAt(ri);
                        condOps.RemoveAt(ri);
                        condValues.RemoveAt(ri);
                        condNegates.RemoveAt(ri);
                        condPanel.Children.Remove(row);
                    }
                };
            };

            // Pre-populate existing conditions
            if (existingConditions.Count > 0)
            {
                foreach (var c in existingConditions)
                {
                    addConditionRow();
                    condFields[condFields.Count - 1].SelectedItem = c.Field;
                    condOps[condOps.Count - 1].SelectedItem = c.Operator;
                    condValues[condValues.Count - 1].Text = c.Value ?? "";
                    condNegates[condNegates.Count - 1].IsChecked = c.Negate;
                }
            }
            else if (!isSimpleMode)
            {
                addConditionRow();
            }

            advancedPanel.Children.Add(condPanel);

            var addCondBtn = new System.Windows.Controls.Button
            {
                Content = "+ Add Condition", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            addCondBtn.Click += (s, e) => addConditionRow();
            advancedPanel.Children.Add(addCondBtn);
            sec3.Children.Add(advancedPanel);

            // Toggle simple/advanced
            simpleRadio.Checked += (s, e) =>
            {
                simplePanel.Visibility = Visibility.Visible;
                advancedPanel.Visibility = Visibility.Collapsed;
            };
            advancedRadio.Checked += (s, e) =>
            {
                simplePanel.Visibility = Visibility.Collapsed;
                advancedPanel.Visibility = Visibility.Visible;
                if (condRows.Count == 0) addConditionRow();
            };

            // Separator + PLC/APP
            sec3.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 8, 0, 4), Background = borderBrush
            });
            sec3.Children.Add(makeLabel("Log types to scan"));
            var logTypeRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var plcCheck = new System.Windows.Controls.CheckBox
            {
                Content = "PLC logs", Foreground = textPrimary,
                IsChecked = existingPLC, FontSize = 12, Margin = new Thickness(0, 0, 20, 0)
            };
            var appCheck = new System.Windows.Controls.CheckBox
            {
                Content = "APP logs", Foreground = textPrimary,
                IsChecked = existingAPP, FontSize = 12
            };
            logTypeRow.Children.Add(plcCheck);
            logTypeRow.Children.Add(appCheck);
            sec3.Children.Add(logTypeRow);

            root.Children.Add(makeSection("What to Search", sec3));

            // ════════════════════════════════════════════════════════
            // SECTION 4: WHERE TO SEARCH
            // ════════════════════════════════════════════════════════
            var sec4 = new System.Windows.Controls.StackPanel();
            var locationChecks = new Dictionary<Guid, System.Windows.Controls.CheckBox>();
            var locListPanel = new System.Windows.Controls.StackPanel();
            var locScroll = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 140,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = locListPanel
            };
            var noLocLabel = new System.Windows.Controls.TextBlock
            {
                Text = "No locations configured. Click '+ Add Location' below.",
                Foreground = textSecondary, FontSize = 11, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            };

            // Rebuilds the location checkbox list from _locationService
            Action rebuildLocationList = () =>
            {
                locationChecks.Clear();
                locListPanel.Children.Clear();
                foreach (var loc in _locationService.Locations)
                {
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = $"{loc.Name}  ({loc.Address} \u2014 {loc.BasePath})",
                        Foreground = textPrimary, FontSize = 11,
                        IsChecked = existingLocIds.Count == 0 || existingLocIds.Contains(loc.Id),
                        Margin = new Thickness(0, 0, 0, 3),
                        Tag = loc.Id
                    };
                    locationChecks[loc.Id] = cb;
                    locListPanel.Children.Add(cb);
                }
                noLocLabel.Visibility = _locationService.Locations.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
                locScroll.Visibility = _locationService.Locations.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
            };

            sec4.Children.Add(noLocLabel);
            sec4.Children.Add(locScroll);
            rebuildLocationList();

            // Location management buttons
            var locBtnRow = new System.Windows.Controls.WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 0)
            };

            var addLocBtn = new System.Windows.Controls.Button
            {
                Content = "+ Add Location", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            addLocBtn.Click += (s, e) =>
            {
                var result = ShowLocationDialog("Add Search Location", "", "", "");
                if (result == null) return;
                var newLoc = new SearchLocation
                {
                    Name = result.Value.name,
                    Address = result.Value.address,
                    BasePath = result.Value.path
                };
                _locationService.Add(newLoc);
                Locations.Add(newLoc);
                // After adding, mark new location as selected in this schedule
                existingLocIds.Add(newLoc.Id);
                rebuildLocationList();
            };

            var editLocBtn = new System.Windows.Controls.Button
            {
                Content = "Edit", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            editLocBtn.Click += (s, e) =>
            {
                // Find the first checked location to edit
                var checkedId = locationChecks.FirstOrDefault(kv => kv.Value.IsChecked == true).Key;
                if (checkedId == Guid.Empty)
                {
                    MessageBox.Show("Check a location to edit it.", "Edit Location", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var loc = _locationService.Locations.FirstOrDefault(l => l.Id == checkedId);
                if (loc == null) return;
                var result = ShowLocationDialog("Edit Search Location", loc.Name, loc.Address, loc.BasePath);
                if (result == null) return;
                loc.Name = result.Value.name;
                loc.Address = result.Value.address;
                loc.BasePath = result.Value.path;
                _locationService.Update(loc);
                // Also update the main window's Locations collection
                var mainLoc = Locations.FirstOrDefault(l => l.Id == loc.Id);
                if (mainLoc != null) { mainLoc.Name = loc.Name; mainLoc.Address = loc.Address; mainLoc.BasePath = loc.BasePath; }
                rebuildLocationList();
            };

            var removeLocBtn = new System.Windows.Controls.Button
            {
                Content = "Remove", Background = bgCard,
                Foreground = dangerColor, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            removeLocBtn.Click += (s, e) =>
            {
                var checkedIds = locationChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (checkedIds.Count == 0)
                {
                    MessageBox.Show("Check the location(s) you want to remove.", "Remove Location", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (MessageBox.Show($"Remove {checkedIds.Count} selected location(s)?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                foreach (var id in checkedIds)
                {
                    _locationService.Remove(id);
                    var mainLoc = Locations.FirstOrDefault(l => l.Id == id);
                    if (mainLoc != null) Locations.Remove(mainLoc);
                    existingLocIds.Remove(id);
                }
                rebuildLocationList();
            };

            var selAllBtn = new System.Windows.Controls.Button
            {
                Content = "Select All", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            selAllBtn.Click += (s, e) => { foreach (var kv in locationChecks) kv.Value.IsChecked = true; };

            var selNoneBtn = new System.Windows.Controls.Button
            {
                Content = "Select None", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            selNoneBtn.Click += (s, e) => { foreach (var kv in locationChecks) kv.Value.IsChecked = false; };

            locBtnRow.Children.Add(addLocBtn);
            locBtnRow.Children.Add(editLocBtn);
            locBtnRow.Children.Add(removeLocBtn);
            locBtnRow.Children.Add(new System.Windows.Controls.Separator
            {
                Width = 1, Margin = new Thickness(4, 2, 4, 2),
                Background = borderBrush
            });
            locBtnRow.Children.Add(selAllBtn);
            locBtnRow.Children.Add(selNoneBtn);
            sec4.Children.Add(locBtnRow);

            root.Children.Add(makeSection("Where to Search", sec4));

            // ════════════════════════════════════════════════════════
            // SECTION 5: TIME FILTERS (optional)
            // ════════════════════════════════════════════════════════
            var sec5 = new System.Windows.Controls.StackPanel();

            sec5.Children.Add(makeLabel("File date range (skip files outside this range)"));
            var ffGrid = new System.Windows.Controls.Grid();
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(50) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ffFromLbl = new System.Windows.Controls.TextBlock { Text = "From:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var ffFromPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingFileFrom, FontSize = 11 };
            var ffToLbl = new System.Windows.Controls.TextBlock { Text = "To:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var ffToPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingFileTo, FontSize = 11 };
            System.Windows.Controls.Grid.SetColumn(ffFromLbl, 0); System.Windows.Controls.Grid.SetColumn(ffFromPicker, 1);
            System.Windows.Controls.Grid.SetColumn(ffToLbl, 2); System.Windows.Controls.Grid.SetColumn(ffToPicker, 3);
            ffGrid.Children.Add(ffFromLbl); ffGrid.Children.Add(ffFromPicker);
            ffGrid.Children.Add(ffToLbl); ffGrid.Children.Add(ffToPicker);
            sec5.Children.Add(ffGrid);

            sec5.Children.Add(makeLabel("Result timestamp filter"));
            var rfGrid = new System.Windows.Controls.Grid();
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(50) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rfFromLbl = new System.Windows.Controls.TextBlock { Text = "From:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var rfFromPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingResFrom, FontSize = 11 };
            var rfToLbl = new System.Windows.Controls.TextBlock { Text = "To:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var rfToPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingResTo, FontSize = 11 };
            System.Windows.Controls.Grid.SetColumn(rfFromLbl, 0); System.Windows.Controls.Grid.SetColumn(rfFromPicker, 1);
            System.Windows.Controls.Grid.SetColumn(rfToLbl, 2); System.Windows.Controls.Grid.SetColumn(rfToPicker, 3);
            rfGrid.Children.Add(rfFromLbl); rfGrid.Children.Add(rfFromPicker);
            rfGrid.Children.Add(rfToLbl); rfGrid.Children.Add(rfToPicker);
            sec5.Children.Add(rfGrid);

            root.Children.Add(makeSection("Time Filters (optional)", sec5));

            // ════════════════════════════════════════════════════════
            // SECTION 6: OUTPUT
            // ════════════════════════════════════════════════════════
            var sec6 = new System.Windows.Controls.StackPanel();
            sec6.Children.Add(makeLabel("Output Directory"));
            var dirDock = new System.Windows.Controls.DockPanel();
            var browseBtn = new System.Windows.Controls.Button
            {
                Content = "Browse...", Width = 70,
                Padding = new Thickness(4, 3, 4, 3), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Controls.DockPanel.SetDock(browseBtn, System.Windows.Controls.Dock.Right);
            var dirBox = makeTextBox(schedule.OutputDirectory);
            browseBtn.Click += (s, e) =>
            {
                var folderDlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    SelectedPath = dirBox.Text,
                    Description = "Select output directory for search results"
                };
                if (folderDlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    dirBox.Text = folderDlg.SelectedPath;
            };
            dirDock.Children.Add(browseBtn);
            dirDock.Children.Add(dirBox);
            sec6.Children.Add(dirDock);
            root.Children.Add(makeSection("Output", sec6));

            // ════════════════════════════════════════════════════════
            // SAVE / CANCEL BUTTONS
            // ════════════════════════════════════════════════════════
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "Save", Width = 90, IsDefault = true,
                Padding = new Thickness(6, 8, 6, 8),
                Background = primaryColor, Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold, FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 90, IsCancel = true,
                Padding = new Thickness(6, 8, 6, 8),
                Background = bgCard, Foreground = textPrimary, FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            okBtn.Click += (s, e) =>
            {
                // ═══ VALIDATION ═══
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    MessageBox.Show("Schedule name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int typeIdx = typeCombo.SelectedIndex;
                int hours = 0, minutes = 0;
                bool needsTime = typeIdx <= 2;
                if (needsTime)
                {
                    if (!int.TryParse(hourBox.Text, out hours) || hours < 0 || hours > 23)
                    {
                        MessageBox.Show("Hours must be 0\u201323.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!int.TryParse(minuteBox.Text, out minutes) || minutes < 0 || minutes > 59)
                    {
                        MessageBox.Show("Minutes must be 0\u201359.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (typeIdx == 0 && datePicker.SelectedDate == null)
                {
                    MessageBox.Show("Please select a date for 'Once' schedule.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (typeIdx == 2 && !dayCheckBoxes.Any(kv => kv.Value.IsChecked == true))
                {
                    MessageBox.Show("Select at least one day for weekly schedule.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int intervalMins = 60;
                if (typeIdx == 3)
                {
                    if (!int.TryParse(intervalBox.Text, out intervalMins) || intervalMins < 1)
                    {
                        MessageBox.Show("Interval must be at least 1 minute.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                bool isSimple = simpleRadio.IsChecked == true;
                if (isSimple && string.IsNullOrWhiteSpace(simpleTextBox.Text))
                {
                    MessageBox.Show("Please enter search text.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!isSimple && condValues.All(v => string.IsNullOrWhiteSpace(v.Text)))
                {
                    MessageBox.Show("Add at least one search condition with a value.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (plcCheck.IsChecked != true && appCheck.IsChecked != true)
                {
                    MessageBox.Show("Select at least PLC or APP log type.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (locationChecks.Count > 0 && !locationChecks.Any(kv => kv.Value.IsChecked == true))
                {
                    MessageBox.Show("Select at least one search location.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // ═══ BUILD SEARCH CRITERIA ═══
                var criteria = new SearchCriteria
                {
                    SearchPLC = plcCheck.IsChecked == true,
                    SearchAPP = appCheck.IsChecked == true,
                    LocationIds = locationChecks
                        .Where(kv => kv.Value.IsChecked == true)
                        .Select(kv => kv.Key).ToList()
                };

                if (ffFromPicker.SelectedDate.HasValue || ffToPicker.SelectedDate.HasValue)
                    criteria.FileTimeFilter = new TimeRangeFilter { From = ffFromPicker.SelectedDate, To = ffToPicker.SelectedDate };
                if (rfFromPicker.SelectedDate.HasValue || rfToPicker.SelectedDate.HasValue)
                    criteria.ResultTimeFilter = new TimeRangeFilter { From = rfFromPicker.SelectedDate, To = rfToPicker.SelectedDate };

                if (isSimple)
                {
                    criteria.Groups.Add(new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition
                            {
                                Field = (SearchField)simpleFieldCombo.SelectedItem,
                                Operator = simpleRegexCheck.IsChecked == true ? SearchOperator.Regex : SearchOperator.Contains,
                                Value = simpleTextBox.Text.Trim()
                            }
                        }
                    });
                }
                else
                {
                    var group = new SearchConditionGroup
                    {
                        Operator = (ConditionOperator)advOperatorCombo.SelectedItem
                    };
                    for (int ci = 0; ci < condValues.Count; ci++)
                    {
                        if (string.IsNullOrWhiteSpace(condValues[ci].Text)) continue;
                        group.Conditions.Add(new SearchCondition
                        {
                            Field = (SearchField)condFields[ci].SelectedItem,
                            Operator = (SearchOperator)condOps[ci].SelectedItem,
                            Value = condValues[ci].Text.Trim(),
                            Negate = condNegates[ci].IsChecked == true
                        });
                    }
                    if (group.Conditions.Count > 0)
                        criteria.Groups.Add(group);
                }

                // ═══ APPLY TO SCHEDULE ═══
                schedule.Name = nameBox.Text.Trim();
                schedule.IsEnabled = enabledCheck.IsChecked == true;
                switch (typeIdx)
                {
                    case 0: schedule.ScheduleType = ScheduleType.Once; break;
                    case 1: schedule.ScheduleType = ScheduleType.Daily; break;
                    case 2: schedule.ScheduleType = ScheduleType.Weekly; break;
                    case 3: schedule.ScheduleType = ScheduleType.Interval; break;
                }
                schedule.RunDate = typeIdx == 0 ? datePicker.SelectedDate : null;
                schedule.RunTime = new TimeSpan(hours, minutes, 0);
                schedule.RunDays = dayCheckBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                schedule.RepeatIntervalMinutes = intervalMins;
                schedule.OutputDirectory = dirBox.Text.Trim();
                schedule.Criteria = criteria;

                dialogResult = true;
                dialog.Close();
            };

            cancelBtn.Click += (s, e) => dialog.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            root.Children.Add(btnPanel);

            scroll.Content = root;
            dialog.Content = scroll;
            dialog.ShowDialog();
            return dialogResult;
        }

        private void RemoveSchedule()
        {
            if (SelectedSchedule == null) return;
            if (MessageBox.Show($"Remove schedule '{SelectedSchedule.Name}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _schedulerService.RemoveSchedule(SelectedSchedule.Id);
            Schedules.Remove(SelectedSchedule);
        }

        private async Task RunScheduleNowAsync()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;
            var scheduleName = schedule.Name;

            // Calculate wait time until the scheduled RunTime
            var now = DateTime.Now;
            DateTime targetTime;
            if (schedule.ScheduleType == ScheduleType.Once && schedule.RunDate.HasValue)
            {
                targetTime = schedule.RunDate.Value.Date.Add(schedule.RunTime);
            }
            else
            {
                targetTime = now.Date.Add(schedule.RunTime);
                if (targetTime <= now && schedule.ScheduleType != ScheduleType.Interval)
                    targetTime = targetTime.AddDays(1);
            }

            TimeSpan waitDuration = targetTime - now;

            // For Interval type, calculate next interval run
            if (schedule.ScheduleType == ScheduleType.Interval)
            {
                if (schedule.LastRunTime.HasValue)
                {
                    var nextRun = schedule.LastRunTime.Value.AddMinutes(schedule.RepeatIntervalMinutes);
                    if (nextRun > now)
                        waitDuration = nextRun - now;
                    else
                        waitDuration = TimeSpan.Zero; // Overdue — run immediately
                }
                else
                {
                    waitDuration = TimeSpan.Zero; // Never run — run immediately
                }
            }

            // Close the window — search will run at the scheduled time
            string timeMsg = waitDuration.TotalSeconds < 5
                ? "starting now"
                : waitDuration.TotalMinutes < 1
                    ? $"starting in {waitDuration.Seconds}s"
                    : waitDuration.TotalHours < 1
                        ? $"starting at {targetTime:HH:mm} (in {waitDuration.TotalMinutes:F0} min)"
                        : $"starting at {targetTime:HH:mm} (in {waitDuration.TotalHours:F1} hours)";

            RequestCloseForScheduledRun?.Invoke(scheduleName);
            AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' — {timeMsg}");

            try
            {
                // Wait until the scheduled time
                if (waitDuration.TotalSeconds >= 1)
                {
                    AppLogger.Info($"[Grep] Waiting until {targetTime:yyyy-MM-dd HH:mm:ss} to run '{scheduleName}'...");
                    await Task.Delay(waitDuration, _cancellationTokenSource?.Token ?? CancellationToken.None);
                }

                AppLogger.Info($"[Grep] Running scheduled search '{scheduleName}' now...");
                var reportPath = await _schedulerService.RunNowAsync(schedule);
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' completed: {schedule.LastRunStatus}");

                // Notify to reopen window and open report
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, reportPath);
                });
            }
            catch (OperationCanceledException)
            {
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' wait cancelled");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Grep] Scheduled search '{scheduleName}' failed", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
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

        #region Enums (kept for backward compat)

        public enum SearchModeType
        {
            LoadedSessions,
            ExternalFiles
        }

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

        public event PropertyChangedEventHandler PropertyChanged;
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

        private string _value;
        public string Value
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

        public event PropertyChangedEventHandler PropertyChanged;
    }

    #endregion
}
