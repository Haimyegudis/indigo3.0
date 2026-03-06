using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Properties

        // --- Results ---
        private ObservableRangeCollection<GrepResult> _results = new();
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
        private ObservableCollection<string> _savedProfiles = new();
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
    }
}
