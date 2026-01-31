using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// ViewModel for a single pane in the comparison window.
    /// Manages source selection, filtering, and search for one side of the comparison.
    /// </summary>
    public class ComparisonPaneViewModel : INotifyPropertyChanged
    {
        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Fields

        private readonly IList<LogEntry> _allPlcLogs;
        private readonly IList<LogEntry> _allAppLogs;
        private SourceType _selectedSourceType = SourceType.AllPLC;
        private string _selectedFilter;
        private string _searchText;
        private LogEntry _selectedLog;
        private int _topVisibleIndex;

        #endregion

        #region Constructor

        public ComparisonPaneViewModel(IList<LogEntry> allPlcLogs, IList<LogEntry> allAppLogs)
        {
            _allPlcLogs = allPlcLogs ?? new List<LogEntry>();
            _allAppLogs = allAppLogs ?? new List<LogEntry>();

            FilteredLogs = new ObservableRangeCollection<LogEntry>();
            AvailableFilters = new ObservableCollection<string>();
            SourceTypes = new ObservableCollection<SourceType>(
                Enum.GetValues(typeof(SourceType)).Cast<SourceType>());

            // Initialize with all PLC logs
            ApplySourceFilter();
        }

        #endregion

        #region Source Type Enum

        public enum SourceType
        {
            AllPLC,
            AllAPP,
            ByThread,           // Thread from PLC logs
            ByThreadFromApp,    // Thread from APP logs
            ByLogger,           // Logger from APP logs
            ByLoggerFromPLC,    // Logger from PLC logs
            ByMethod,           // Method from APP logs
            ByMethodFromPLC,    // Method from PLC logs
            ByPattern           // Pattern from PLC logs
        }

        #endregion

        #region Properties

        /// <summary>
        /// Available source types for the dropdown.
        /// </summary>
        public ObservableCollection<SourceType> SourceTypes { get; }

        /// <summary>
        /// The currently selected source type.
        /// </summary>
        public SourceType SelectedSourceType
        {
            get => _selectedSourceType;
            set
            {
                if (_selectedSourceType != value)
                {
                    _selectedSourceType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowFilterDropdown));
                    UpdateAvailableFilters();
                    ApplySourceFilter();
                }
            }
        }

        /// <summary>
        /// Whether to show the secondary filter dropdown (for Thread/Logger/Method/Pattern selection).
        /// </summary>
        public bool ShowFilterDropdown => _selectedSourceType == SourceType.ByThread ||
                                           _selectedSourceType == SourceType.ByThreadFromApp ||
                                           _selectedSourceType == SourceType.ByLogger ||
                                           _selectedSourceType == SourceType.ByLoggerFromPLC ||
                                           _selectedSourceType == SourceType.ByMethod ||
                                           _selectedSourceType == SourceType.ByMethodFromPLC ||
                                           _selectedSourceType == SourceType.ByPattern;

        /// <summary>
        /// Available filter values (thread names, logger names, etc.).
        /// </summary>
        public ObservableCollection<string> AvailableFilters { get; }

        /// <summary>
        /// The currently selected filter value.
        /// </summary>
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter != value)
                {
                    _selectedFilter = value;
                    OnPropertyChanged();
                    ApplySourceFilter();
                }
            }
        }

        /// <summary>
        /// Search text for filtering within the current source.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplySearchFilter();
                }
            }
        }

        /// <summary>
        /// The filtered logs to display.
        /// </summary>
        public ObservableRangeCollection<LogEntry> FilteredLogs { get; }

        /// <summary>
        /// The currently selected log entry.
        /// </summary>
        public LogEntry SelectedLog
        {
            get => _selectedLog;
            set
            {
                if (_selectedLog != value)
                {
                    _selectedLog = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The index of the top visible row (for scroll sync).
        /// </summary>
        public int TopVisibleIndex
        {
            get => _topVisibleIndex;
            set
            {
                if (_topVisibleIndex != value)
                {
                    _topVisibleIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the source logs based on the current source type.
        /// </summary>
        private IList<LogEntry> SourceLogs
        {
            get
            {
                switch (_selectedSourceType)
                {
                    case SourceType.AllAPP:
                    case SourceType.ByThreadFromApp:
                    case SourceType.ByLogger:
                    case SourceType.ByMethod:
                        return _allAppLogs;
                    case SourceType.AllPLC:
                    case SourceType.ByThread:
                    case SourceType.ByLoggerFromPLC:
                    case SourceType.ByMethodFromPLC:
                    case SourceType.ByPattern:
                    default:
                        return _allPlcLogs;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Updates the available filters based on the selected source type.
        /// </summary>
        private void UpdateAvailableFilters()
        {
            AvailableFilters.Clear();
            _selectedFilter = null;

            switch (_selectedSourceType)
            {
                case SourceType.ByThread:
                    var plcThreads = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.ThreadName))
                        .Select(l => l.ThreadName)
                        .Distinct()
                        .OrderBy(t => t);
                    foreach (var t in plcThreads)
                        AvailableFilters.Add(t);
                    break;

                case SourceType.ByThreadFromApp:
                    var appThreads = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.ThreadName))
                        .Select(l => l.ThreadName)
                        .Distinct()
                        .OrderBy(t => t);
                    foreach (var t in appThreads)
                        AvailableFilters.Add(t);
                    break;

                case SourceType.ByLogger:
                    var appLoggers = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.Logger))
                        .Select(l => l.Logger)
                        .Distinct()
                        .OrderBy(l => l);
                    foreach (var l in appLoggers)
                        AvailableFilters.Add(l);
                    break;

                case SourceType.ByLoggerFromPLC:
                    var plcLoggers = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Logger))
                        .Select(l => l.Logger)
                        .Distinct()
                        .OrderBy(l => l);
                    foreach (var l in plcLoggers)
                        AvailableFilters.Add(l);
                    break;

                case SourceType.ByMethod:
                    var appMethods = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.Method))
                        .Select(l => l.Method)
                        .Distinct()
                        .OrderBy(m => m);
                    foreach (var m in appMethods)
                        AvailableFilters.Add(m);
                    break;

                case SourceType.ByMethodFromPLC:
                    var plcMethods = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Method))
                        .Select(l => l.Method)
                        .Distinct()
                        .OrderBy(m => m);
                    foreach (var m in plcMethods)
                        AvailableFilters.Add(m);
                    break;

                case SourceType.ByPattern:
                    var patterns = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Pattern))
                        .Select(l => l.Pattern)
                        .Distinct()
                        .OrderBy(p => p);
                    foreach (var p in patterns)
                        AvailableFilters.Add(p);
                    break;
            }

            // Select first filter if available
            if (AvailableFilters.Count > 0)
            {
                SelectedFilter = AvailableFilters[0];
            }

            OnPropertyChanged(nameof(AvailableFilters));
        }

        /// <summary>
        /// Applies the current source type and filter to get the base log list.
        /// </summary>
        public void ApplySourceFilter()
        {
            IEnumerable<LogEntry> result;

            switch (_selectedSourceType)
            {
                case SourceType.AllPLC:
                    result = _allPlcLogs;
                    break;

                case SourceType.AllAPP:
                    result = _allAppLogs;
                    break;

                case SourceType.ByThread:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.ThreadName == _selectedFilter);
                    break;

                case SourceType.ByThreadFromApp:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.ThreadName == _selectedFilter);
                    break;

                case SourceType.ByLogger:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.Logger == _selectedFilter);
                    break;

                case SourceType.ByLoggerFromPLC:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Logger == _selectedFilter);
                    break;

                case SourceType.ByMethod:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.Method == _selectedFilter);
                    break;

                case SourceType.ByMethodFromPLC:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Method == _selectedFilter);
                    break;

                case SourceType.ByPattern:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Pattern == _selectedFilter);
                    break;

                default:
                    result = _allPlcLogs;
                    break;
            }

            // Apply search filter if present
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var searchLower = _searchText.ToLowerInvariant();
                result = result.Where(l =>
                    (l.Message?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.Logger?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.ThreadName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.Method?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            FilteredLogs.ReplaceAll(result.OrderBy(l => l.Date));
            OnPropertyChanged(nameof(FilteredLogs));
        }

        /// <summary>
        /// Applies only the search filter (called when search text changes).
        /// </summary>
        private void ApplySearchFilter()
        {
            ApplySourceFilter();
        }

        /// <summary>
        /// Performs binary search to find the log entry nearest to the target time.
        /// Returns the index in FilteredLogs.
        /// </summary>
        public int BinarySearchNearest(DateTime targetTime)
        {
            if (FilteredLogs == null || FilteredLogs.Count == 0)
                return -1;

            int left = 0;
            int right = FilteredLogs.Count - 1;

            // Edge cases
            if (targetTime <= FilteredLogs[0].Date)
                return 0;
            if (targetTime >= FilteredLogs[right].Date)
                return right;

            // Binary search
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = FilteredLogs[mid].Date;

                if (midTime == targetTime)
                    return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            // Find nearest between left and right
            if (left >= FilteredLogs.Count)
                return right;

            DateTime leftTime = FilteredLogs[left].Date;
            DateTime rightTime = FilteredLogs[right].Date;

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff <= rightDiff ? left : right;
        }

        /// <summary>
        /// Gets the log entry at the specified index, or null if out of range.
        /// </summary>
        public LogEntry GetLogAtIndex(int index)
        {
            if (index >= 0 && index < FilteredLogs.Count)
                return FilteredLogs[index];
            return null;
        }

        #endregion

        #region INotifyPropertyChanged

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
