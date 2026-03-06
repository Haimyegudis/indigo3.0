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
    public partial class ComparisonPaneViewModel : ViewModelBase
    {

        #region Fields

        private readonly IList<LogEntry> _allPlcLogs;
        private readonly IList<LogEntry> _allAppLogs;
        private SourceType _selectedSourceType = SourceType.AllPLC;
        private string? _selectedFilter;
        private string? _searchText;
        private LogEntry? _selectedLog;
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
        public string? SelectedFilter
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
        public string? SearchText
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
        public LogEntry? SelectedLog
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

    }
}
