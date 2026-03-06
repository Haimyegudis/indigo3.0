using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        // --- Active Flags ---
        private bool _isMainFilterActive;
        public bool IsMainFilterActive
        {
            get => _isMainFilterActive;
            set
            {
                _isMainFilterActive = value;
                OnPropertyChanged();
            }
        }

        private bool _isAppErrorFilterActive;
        public bool IsAppErrorFilterActive
        {
            get => _isAppErrorFilterActive;
            set { _isAppErrorFilterActive = value; OnPropertyChanged(); }
        }

        private bool _isAppFilterActive;
        public bool IsAppFilterActive
        {
            get => _isAppFilterActive;
            set
            {
                _isAppFilterActive = value;
                OnPropertyChanged();
            }
        }

        private bool _isMainFilterOutActive;
        public bool IsMainFilterOutActive
        {
            get => _isMainFilterOutActive;
            set
            {
                _isMainFilterOutActive = value;
                OnPropertyChanged();
            }
        }

        private bool _isAppFilterOutActive;
        public bool IsAppFilterOutActive
        {
            get => _isAppFilterOutActive;
            set
            {
                _isAppFilterOutActive = value;
                OnPropertyChanged();
            }
        }

        private bool _isTimeFocusActive = false;
        public bool IsTimeFocusActive
        {
            get => _isTimeFocusActive;
            set { _isTimeFocusActive = value; OnPropertyChanged(); }
        }

        private bool _isAppTimeFocusActive = false;
        public bool IsAppTimeFocusActive
        {
            get => _isAppTimeFocusActive;
            set { _isAppTimeFocusActive = value; OnPropertyChanged(); }
        }

        private DateTime? _globalTimeRangeStart = null;
        public DateTime? GlobalTimeRangeStart
        {
            get => _globalTimeRangeStart;
            set { _globalTimeRangeStart = value; OnPropertyChanged(); }
        }

        private DateTime? _globalTimeRangeEnd = null;
        public DateTime? GlobalTimeRangeEnd
        {
            get => _globalTimeRangeEnd;
            set { _globalTimeRangeEnd = value; OnPropertyChanged(); }
        }

        public bool IsGlobalTimeRangeActive => _globalTimeRangeStart.HasValue && _globalTimeRangeEnd.HasValue;

        // --- Specific Filters Lists ---
        private List<string> _negativeFilters = new List<string>();
        public List<string> NegativeFilters => _negativeFilters;

        private List<string> _appNegativeFilters = new List<string>();
        public List<string> AppNegativeFilters => _appNegativeFilters;

        // PLC thread filters
        private List<string> _activeThreadFilters = new List<string>();
        public List<string> ActiveThreadFilters => _activeThreadFilters;

        // APP thread filters (separate from PLC)
        private List<string> _appActiveThreadFilters = new List<string>();
        public List<string> AppActiveThreadFilters => _appActiveThreadFilters;

        // New Lists for independent column filtering
        private List<string> _activeLoggerFilters = new List<string>();
        public List<string> ActiveLoggerFilters => _activeLoggerFilters;

        private List<string> _activeMethodFilters = new List<string>();
        public List<string> ActiveMethodFilters => _activeMethodFilters;

        // --- HasStoredFilter properties ---
        // These indicate whether there's a filter definition stored (regardless of checkbox state)
        public bool HasMainStoredFilter
        {
            get
            {
                bool hasAdvanced = _mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0;
                bool hasThread = _activeThreadFilters.Any();
                bool hasTimeFocus = _isTimeFocusActive;
                bool result = hasAdvanced || hasThread || hasTimeFocus;
                return result;
            }
        }

        public bool HasAppStoredFilter =>
            (_appFilterRoot != null && _appFilterRoot.Children != null && _appFilterRoot.Children.Count > 0) ||
            _appActiveThreadFilters.Any() ||
            _activeLoggerFilters.Any() ||
            _activeMethodFilters.Any() ||
            _treeShowOnlyLogger != null ||
            _treeShowOnlyPrefix != null ||
            _treeHiddenLoggers.Count > 0 ||
            _treeHiddenPrefixes.Count > 0 ||
            _isAppTimeFocusActive;

        // HasStoredFilterOut - indicates whether there are negative filters stored
        public bool HasMainStoredFilterOut => _negativeFilters.Any();
        public bool HasAppStoredFilterOut => _appNegativeFilters.Any();

        // --- Caches ---
        private List<LogEntry>? _lastFilteredCache = null;
        public List<LogEntry>? LastFilteredCache
        {
            get => _lastFilteredCache;
            set { _lastFilteredCache = value; OnPropertyChanged(); }
        }

        private List<LogEntry>? _lastFilteredAppCache = null;
        public List<LogEntry>? LastFilteredAppCache
        {
            get => _lastFilteredAppCache;
            set { _lastFilteredAppCache = value; OnPropertyChanged(); }
        }

        // --- Tree Filter State (APP loggers) ---
        private HashSet<string> _treeHiddenLoggers = new HashSet<string>();
        public HashSet<string> TreeHiddenLoggers => _treeHiddenLoggers;

        private HashSet<string> _treeHiddenPrefixes = new HashSet<string>();
        public HashSet<string> TreeHiddenPrefixes => _treeHiddenPrefixes;

        private string? _treeShowOnlyLogger = null;
        public string? TreeShowOnlyLogger
        {
            get => _treeShowOnlyLogger;
            set { _treeShowOnlyLogger = value; OnPropertyChanged(); }
        }

        private string? _treeShowOnlyPrefix = null;
        public string? TreeShowOnlyPrefix
        {
            get => _treeShowOnlyPrefix;
            set { _treeShowOnlyPrefix = value; OnPropertyChanged(); }
        }

        // --- PLC Tree Filter State ---
        private HashSet<string> _plcTreeHiddenLoggers = new HashSet<string>();
        private HashSet<string> _plcTreeHiddenPrefixes = new HashSet<string>();
        private string? _plcTreeShowOnlyLogger = null;
        private string? _plcTreeShowOnlyPrefix = null;
        public bool IsPlcTreeFilterActive => _plcTreeShowOnlyLogger != null || _plcTreeShowOnlyPrefix != null ||
                                              _plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0;
    }
}
