using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Debug helper list that tracks clearing operations for diagnostics.
    /// </summary>
    public class TrackedList<T> : List<T>, IList<T>
    {
        private readonly string _name;

        public TrackedList(string name) { _name = name; }

        public new void Clear()
        {
            base.Clear();
        }

        // Override ICollection<T>.Clear explicitly
        void ICollection<T>.Clear()
        {
            base.Clear();
        }
    }

    /// <summary>
    /// Manages log filtering, searching, and logger tree operations for PLC and APP log views.
    /// </summary>
    public class FilterSearchViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;

        /// <summary>
        /// User-configurable default PLC filter applied when no explicit filters are active.
        /// </summary>
        private FilterNode _defaultPlcFilter;
        public FilterNode DefaultPlcFilter
        {
            get => _defaultPlcFilter;
            set { _defaultPlcFilter = value; OnPropertyChanged(); }
        }

        // --- Search ---
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
                    OnSearchTextChanged();
                }
            }
        }

        private bool _isSearchPanelVisible;
        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            set
            {
                if (_isSearchPanelVisible != value)
                {
                    _isSearchPanelVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Filtered PLC/main log entries displayed in the FILTERED tab.
        /// </summary>
        private ObservableRangeCollection<LogEntry> _filteredLogs;
        public ObservableRangeCollection<LogEntry> FilteredLogs
        {
            get => _filteredLogs;
            set
            {
                _filteredLogs = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Filtered APP developer log entries displayed in the APP tab.
        /// </summary>
        private ObservableRangeCollection<LogEntry> _appDevLogsFiltered;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered
        {
            get => _appDevLogsFiltered;
            set
            {
                _appDevLogsFiltered = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Hierarchical tree of APP logger names for tree-based filtering.
        /// </summary>
        private ObservableCollection<LoggerNode> _loggerTreeRoot;
        public ObservableCollection<LoggerNode> LoggerTreeRoot
        {
            get => _loggerTreeRoot;
            set
            {
                _loggerTreeRoot = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Hierarchical tree of PLC logger names for tree-based filtering.
        /// </summary>
        private ObservableCollection<LoggerNode> _plcLoggerTreeRoot = new ObservableCollection<LoggerNode>();
        public ObservableCollection<LoggerNode> PlcLoggerTreeRoot
        {
            get => _plcLoggerTreeRoot;
            set
            {
                _plcLoggerTreeRoot = value;
                OnPropertyChanged();
            }
        }

        private LoggerNode _selectedTreeItem;
        public LoggerNode SelectedTreeItem
        {
            get => _selectedTreeItem;
            set
            {
                _selectedTreeItem = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Root node of the advanced filter tree for PLC/main logs.
        /// </summary>
        private FilterNode _mainFilterRoot;
        public FilterNode MainFilterRoot
        {
            get => _mainFilterRoot;
            set
            {
                _mainFilterRoot = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Root node of the advanced filter tree for APP logs.
        /// </summary>
        private FilterNode _appFilterRoot;
        public FilterNode AppFilterRoot
        {
            get => _appFilterRoot;
            set { _appFilterRoot = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Root node of a saved/persisted filter tree for restoring filter state.
        /// </summary>
        private FilterNode _savedFilterRoot;
        public FilterNode SavedFilterRoot
        {
            get => _savedFilterRoot;
            set { _savedFilterRoot = value; OnPropertyChanged(); }
        }

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
        private TrackedList<string> _activeThreadFilters = new TrackedList<string>("ActiveThreadFilters");
        public TrackedList<string> ActiveThreadFilters => _activeThreadFilters;

        // APP thread filters (separate from PLC)
        private TrackedList<string> _appActiveThreadFilters = new TrackedList<string>("AppActiveThreadFilters");
        public TrackedList<string> AppActiveThreadFilters => _appActiveThreadFilters;

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

        /// <summary>
        /// Returns a list of active filter descriptions for display in the right panel
        /// </summary>
        public List<ActiveFilterItem> GetActiveFilters()
        {
            var items = new List<ActiveFilterItem>();
            int tab = _parent?.SelectedTabIndex ?? 0;
            bool isAppTab = (tab == AppConstants.TAB_APP);
            bool isPLCTab = (tab == AppConstants.TAB_PLC || tab == AppConstants.TAB_PLC_FILTERED);

            if (isAppTab)
            {
                // === APP TAB FILTERS ===

                // Advanced filter (APP)
                if (_appFilterRoot != null && _appFilterRoot.Children?.Count > 0)
                {
                    int idx = 0;
                    CollectFilterNodeDescriptions(items, _appFilterRoot, "FILTER", "", "APP_FILTER", ref idx);
                }

                // Show Errors filter active
                if (_isAppErrorFilterActive)
                {
                    items.Add(new ActiveFilterItem { Category = "FILTER", Description = "Level Equals \"Error\"", IsActive = true, Key = "APP_ERROR_FILTER" });
                }

                // APP Time Focus
                if (_isAppTimeFocusActive)
                {
                    items.Add(new ActiveFilterItem { Category = "TIME RANGE", Description = "Time Focus active", IsActive = true, Key = "APP_TIME_FOCUS" });
                }

                // Thread filters (APP-specific) - one item per thread
                if (_appActiveThreadFilters.Any())
                {
                    foreach (var t in _appActiveThreadFilters)
                        items.Add(new ActiveFilterItem { Category = "THREAD", Description = $"Thread = \"{t}\"", IsActive = true, Key = $"APP_THREAD:{t}" });
                }

                // APP Filter Out (negative filters)
                if (_appNegativeFilters.Any())
                {
                    foreach (var nf in _appNegativeFilters)
                    {
                        string desc = nf.StartsWith("THREAD:") ? $"Thread: {nf.Substring(7)}" : $"Message: \"{nf}\"";
                        items.Add(new ActiveFilterItem { Category = "FILTER OUT", Description = desc, IsActive = true, Key = $"APP_NEGATIVE:{nf}" });
                    }
                }

                // Logger filters
                if (_activeLoggerFilters.Any())
                {
                    foreach (var l in _activeLoggerFilters)
                        items.Add(new ActiveFilterItem { Category = "LOGGER", Description = l, IsActive = true, Key = $"LOGGER:{l}" });
                }

                // Method filters
                if (_activeMethodFilters.Any())
                {
                    foreach (var m in _activeMethodFilters)
                        items.Add(new ActiveFilterItem { Category = "METHOD", Description = m, IsActive = true, Key = $"METHOD:{m}" });
                }

                // Tree filters (logger tree)
                if (_treeShowOnlyLogger != null)
                    items.Add(new ActiveFilterItem { Category = "LOGGER", Description = $"Show only: {_treeShowOnlyLogger}", IsActive = true, Key = "TREE_SHOW_ONLY_LOGGER" });
                if (_treeShowOnlyPrefix != null)
                    items.Add(new ActiveFilterItem { Category = "LOGGER", Description = $"Show prefix: {_treeShowOnlyPrefix}", IsActive = true, Key = "TREE_SHOW_ONLY_PREFIX" });
                if (_treeHiddenLoggers.Count > 0)
                {
                    foreach (var h in _treeHiddenLoggers)
                        items.Add(new ActiveFilterItem { Category = "FILTER OUT", Description = $"Logger: {h}", IsActive = true, Key = $"TREE_HIDE_LOGGER:{h}" });
                }
                if (_treeHiddenPrefixes.Count > 0)
                {
                    foreach (var h in _treeHiddenPrefixes)
                        items.Add(new ActiveFilterItem { Category = "FILTER OUT", Description = $"Prefix: {h}", IsActive = true, Key = $"TREE_HIDE_PREFIX:{h}" });
                }

            }
            else if (isPLCTab)
            {
                // === PLC TAB FILTERS ===

                // Advanced filter (Main)
                if (_mainFilterRoot != null && _mainFilterRoot.Children?.Count > 0)
                {
                    int idx = 0;
                    CollectFilterNodeDescriptions(items, _mainFilterRoot, "FILTER", "", "MAIN_FILTER", ref idx);
                }

                // Time Focus (Main)
                if (_isTimeFocusActive)
                {
                    items.Add(new ActiveFilterItem { Category = "TIME RANGE", Description = "Time Focus active", IsActive = true, Key = "MAIN_TIME_FOCUS" });
                }

                // Thread filters - one item per thread
                if (_activeThreadFilters.Any())
                {
                    foreach (var t in _activeThreadFilters)
                        items.Add(new ActiveFilterItem { Category = "THREAD", Description = $"Thread = \"{t}\"", IsActive = true, Key = $"MAIN_THREAD:{t}" });
                }

                // Filter Out (negative filters) - PLC only
                if (_negativeFilters.Any())
                {
                    foreach (var nf in _negativeFilters)
                    {
                        string desc = nf.StartsWith("THREAD:") ? $"Thread: {nf.Substring(7)}" : $"Message: \"{nf}\"";
                        items.Add(new ActiveFilterItem { Category = "FILTER OUT", Description = desc, IsActive = true, Key = $"NEGATIVE:{nf}" });
                    }
                }

                // PLC tree filters (logger tree)
                if (_plcTreeShowOnlyPrefix != null)
                    items.Add(new ActiveFilterItem { Category = "PLC LOGGER", Description = $"Show prefix: {_plcTreeShowOnlyPrefix}", IsActive = true, Key = "PLC_TREE_SHOW_ONLY_PREFIX" });
                if (_plcTreeShowOnlyLogger != null)
                    items.Add(new ActiveFilterItem { Category = "PLC LOGGER", Description = $"Show only: {_plcTreeShowOnlyLogger}", IsActive = true, Key = "PLC_TREE_SHOW_ONLY_LOGGER" });
                if (_plcTreeHiddenLoggers.Count > 0)
                    foreach (var h in _plcTreeHiddenLoggers)
                        items.Add(new ActiveFilterItem { Category = "PLC FILTER OUT", Description = $"Logger: {h}", IsActive = true, Key = $"PLC_TREE_HIDE_LOGGER:{h}" });
                if (_plcTreeHiddenPrefixes.Count > 0)
                    foreach (var h in _plcTreeHiddenPrefixes)
                        items.Add(new ActiveFilterItem { Category = "PLC FILTER OUT", Description = $"Prefix: {h}", IsActive = true, Key = $"PLC_TREE_HIDE_PREFIX:{h}" });
            }

            // === SHARED FILTERS (all log tabs) ===
            if (isPLCTab || isAppTab)
            {
                // Global Time Range
                if (IsGlobalTimeRangeActive)
                {
                    items.Add(new ActiveFilterItem { Category = "TIME RANGE", Description = $"{_globalTimeRangeStart:HH:mm:ss} → {_globalTimeRangeEnd:HH:mm:ss}", IsActive = true, Key = "GLOBAL_TIME_RANGE" });
                }

                // Search text
                if (!string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2)
                {
                    items.Add(new ActiveFilterItem { Category = "SEARCH", Description = $"\"{SearchText}\"", IsActive = true, Key = "SEARCH" });
                }

                // Range selection in progress
                if (_hasRangeStart && _rangeStartLog != null)
                {
                    items.Add(new ActiveFilterItem { Category = "RANGE", Description = $"Start: {_rangeStartLog.Date:HH:mm:ss.ffffff} (select End Range)", IsActive = true, Key = "RANGE" });
                }

                // === COLORING RULES ===
                var colorRules = isAppTab ? _parent?.CaseVM?.AppColoringRules : _parent?.CaseVM?.MainColoringRules;
                if (colorRules != null && colorRules.Count > 0)
                {
                    for (int i = 0; i < colorRules.Count; i++)
                    {
                        var rule = colorRules[i];
                        items.Add(new ActiveFilterItem { Category = "COLORING", Description = $"{rule.Field} {rule.Operator} \"{rule.Value}\"", IsActive = true, Key = $"COLORING:{i}", ColorBrush = new System.Windows.Media.SolidColorBrush(rule.Color) });
                    }
                }

                // Default coloring rules (only show if no session-specific rules)
                if (colorRules == null || colorRules.Count == 0)
                {
                    var defaultRules = isAppTab ? _parent?.ColoringService?.UserDefaultAppRules : _parent?.ColoringService?.UserDefaultMainRules;
                    if (defaultRules != null && defaultRules.Count > 0)
                    {
                        for (int i = 0; i < defaultRules.Count; i++)
                        {
                            var rule = defaultRules[i];
                            items.Add(new ActiveFilterItem { Category = "COLORING", Description = $"{rule.Field} {rule.Operator} \"{rule.Value}\" (default)", IsActive = true, Key = $"DEFAULT_COLORING:{i}", ColorBrush = new System.Windows.Media.SolidColorBrush(rule.Color) });
                        }
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// Collects all condition nodes into a flat list for display.
        /// conditionIndex is passed by ref to assign unique keys across recursive calls.
        /// </summary>
        private void CollectFilterNodeDescriptions(List<ActiveFilterItem> items, FilterNode node, string category, string prefix, string keyPrefix, ref int conditionIndex)
        {
            if (node == null) return;

            if (node.Type == NodeType.Condition)
            {
                int myIndex = conditionIndex++;

                // Skip ThreadName conditions - they are already shown via the THREAD category
                if (node.Field == "ThreadName" && (_activeThreadFilters.Any() || _appActiveThreadFilters.Any()))
                    return;

                string desc = $"{prefix}{node.Field} {node.Operator} \"{node.Value}\"";
                items.Add(new ActiveFilterItem { Category = category, Description = desc, IsActive = true, Key = $"{keyPrefix}:{myIndex}" });
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectFilterNodeDescriptions(items, child, category, prefix, keyPrefix, ref conditionIndex);
            }
        }

        /// <summary>
        /// Removes a specific condition node from the filter tree by its flattened index.
        /// The index matches the order assigned by CollectFilterNodeDescriptions.
        /// </summary>
        private void RemoveFilterConditionByIndex(FilterNode root, int targetIndex)
        {
            if (root == null) return;
            int current = 0;
            RemoveConditionRecursive(root, targetIndex, ref current);
        }

        private bool RemoveConditionRecursive(FilterNode parent, int targetIndex, ref int current)
        {
            if (parent == null || parent.Children == null) return false;

            for (int i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (child.Type == NodeType.Condition)
                {
                    if (current == targetIndex)
                    {
                        parent.Children.RemoveAt(i);
                        return true;
                    }
                    current++;
                }
                else if (child.Type == NodeType.Group)
                {
                    if (RemoveConditionRecursive(child, targetIndex, ref current))
                    {
                        // If the group is now empty, remove it too
                        if (child.Children.Count == 0)
                            parent.Children.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }

        // --- Caches ---
        private List<LogEntry> _lastFilteredCache = null;
        public List<LogEntry> LastFilteredCache
        {
            get => _lastFilteredCache;
            set { _lastFilteredCache = value; OnPropertyChanged(); }
        }

        private List<LogEntry> _lastFilteredAppCache = null;
        public List<LogEntry> LastFilteredAppCache
        {
            get => _lastFilteredAppCache;
            set { _lastFilteredAppCache = value; OnPropertyChanged(); }
        }

        // --- Tree Filter State (APP loggers) ---
        private HashSet<string> _treeHiddenLoggers = new HashSet<string>();
        public HashSet<string> TreeHiddenLoggers => _treeHiddenLoggers;

        private HashSet<string> _treeHiddenPrefixes = new HashSet<string>();
        public HashSet<string> TreeHiddenPrefixes => _treeHiddenPrefixes;

        private string _treeShowOnlyLogger = null;
        public string TreeShowOnlyLogger
        {
            get => _treeShowOnlyLogger;
            set { _treeShowOnlyLogger = value; OnPropertyChanged(); }
        }

        private string _treeShowOnlyPrefix = null;
        public string TreeShowOnlyPrefix
        {
            get => _treeShowOnlyPrefix;
            set { _treeShowOnlyPrefix = value; OnPropertyChanged(); }
        }

        // --- PLC Tree Filter State ---
        private HashSet<string> _plcTreeHiddenLoggers = new HashSet<string>();
        private HashSet<string> _plcTreeHiddenPrefixes = new HashSet<string>();
        private string _plcTreeShowOnlyLogger = null;
        private string _plcTreeShowOnlyPrefix = null;
        public bool IsPlcTreeFilterActive => _plcTreeShowOnlyLogger != null || _plcTreeShowOnlyPrefix != null ||
                                              _plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0;

        private DispatcherTimer _searchDebounceTimer;

        // --- Commands ---
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand OpenLoggerFilterCommand { get; }
        public ICommand OpenMethodFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand TreeShowThisCommand { get; }
        public ICommand TreeHideThisCommand { get; }
        public ICommand TreeShowOnlyThisCommand { get; }
        public ICommand TreeShowWithChildrenCommand { get; }
        public ICommand TreeHideWithChildrenCommand { get; }
        public ICommand TreeShowAllCommand { get; }
        public ICommand OpenTimeRangeFilterCommand { get; }
        public ICommand StartRangeCommand { get; }
        public ICommand EndRangeCommand { get; }
        public ICommand ClearRangeCommand { get; }

        // Range selection state
        private LogEntry _rangeStartLog = null;
        private bool _hasRangeStart = false;
        public bool HasRangeStart
        {
            get => _hasRangeStart;
            set { _hasRangeStart = value; OnPropertyChanged(); }
        }

        public FilterSearchViewModel(MainViewModel parent, LogSessionViewModel sessionVM)
        {
            _parent = parent;
            _sessionVM = sessionVM;

            _filteredLogs = new ObservableRangeCollection<LogEntry>();
            _appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            _loggerTreeRoot = new ObservableCollection<LoggerNode>();
            _plcLoggerTreeRoot = new ObservableCollection<LoggerNode>();

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            ToggleSearchCommand = new RelayCommand(o =>
            {
                // Force refresh by toggling if already true
                if (IsSearchPanelVisible)
                {
                    IsSearchPanelVisible = false;
                }
                IsSearchPanelVisible = true;
            });
            CloseSearchCommand = new RelayCommand(o =>
            {
                // Save the currently selected log and its scroll position BEFORE clearing search
                var savedSelectedLog = _parent.SelectedLog;
                if (savedSelectedLog != null)
                {
                    _parent.SaveScrollPosition(savedSelectedLog);
                }

                SearchText = "";  // Clear the search text
                IsSearchPanelVisible = false;
                // Refresh the logs to show all (without search filter)
                ApplyMainLogsFilter();
                ApplyAppLogsFilter();

                // Restore the selected log and scroll to it, preserving visual position
                // Use Dispatcher to ensure UI has fully updated before scrolling
                if (savedSelectedLog != null)
                {
                    var logToRestore = savedSelectedLog;
                    Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ContextIdle,
                        new Action(() =>
                        {
                            _parent.SelectedLog = logToRestore;
                            _parent.ScrollToLogPreservePosition(logToRestore);
                        }));
                }
            });
            OpenFilterWindowCommand = new RelayCommand(async o => await OpenFilterWindow(o));
            FilterOutCommand = new RelayCommand(FilterOut);
            FilterOutThreadCommand = new RelayCommand(FilterOutThread);

            // Fixed commands calling specific logic
            OpenThreadFilterCommand = new RelayCommand(OpenThreadFilter);
            OpenLoggerFilterCommand = new RelayCommand(OpenLoggerFilter);
            OpenMethodFilterCommand = new RelayCommand(OpenMethodFilter);

            FilterContextCommand = new RelayCommand(FilterContext);
            UndoFilterOutCommand = new RelayCommand(UndoFilterOut);
            TreeShowThisCommand = new RelayCommand(ExecuteTreeShowThis);
            TreeHideThisCommand = new RelayCommand(ExecuteTreeHideThis);
            TreeShowOnlyThisCommand = new RelayCommand(ExecuteTreeShowOnlyThis);
            TreeShowWithChildrenCommand = new RelayCommand(ExecuteTreeShowWithChildren);
            TreeHideWithChildrenCommand = new RelayCommand(ExecuteTreeHideWithChildren);
            TreeShowAllCommand = new RelayCommand(ExecuteTreeShowAll);
            OpenTimeRangeFilterCommand = new RelayCommand(OpenTimeRangeFilter);
            StartRangeCommand = new RelayCommand(StartRange);
            EndRangeCommand = new RelayCommand(EndRange, o => HasRangeStart);
            ClearRangeCommand = new RelayCommand(ClearRange);
        }

        private void OnSearchTextChanged()
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            // Save the currently selected log and its scroll position BEFORE applying search filter
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            ApplyMainLogsFilter();
            ApplyAppLogsFilter();

            // Restore the selected log and scroll to it, preserving visual position
            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        _parent.SelectedLog = logToRestore;
                        _parent.ScrollToLogPreservePosition(logToRestore);
                    }));
            }
        }

        /// <summary>
        /// Builds the hierarchical APP logger tree from the provided log entries.
        /// </summary>
        public void BuildLoggerTree(IEnumerable<LogEntry> logs)
        {
            if (logs == null || !logs.Any())
            {
                LoggerTreeRoot = new ObservableCollection<LoggerNode>();
                return;
            }

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

            LoggerTreeRoot = new ObservableCollection<LoggerNode>(rootNode.Children);
        }

        /// <summary>
        /// Builds the hierarchical PLC logger tree from the provided log entries.
        /// </summary>
        public void BuildPlcLoggerTree(IEnumerable<LogEntry> logs)
        {
            if (logs == null || !logs.Any())
            {
                PlcLoggerTreeRoot = new ObservableCollection<LoggerNode>();
                return;
            }

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

            PlcLoggerTreeRoot = new ObservableCollection<LoggerNode>(rootNode.Children);
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


        /// <summary>
        /// Applies all active filters (thread, logger, advanced, search, negative) to APP logs and updates AppDevLogsFiltered.
        /// </summary>
        public void ApplyAppLogsFilter()
        {
            var filterSw = System.Diagnostics.Stopwatch.StartNew();
            // הגנה מפני קריסה אם המטמון ריק
            if (_sessionVM?.AllAppLogsCache == null) return;

            bool isActive = _isAppFilterActive;

            // קביעת מקור הנתונים (Cache רגיל או Cache של Focus Context)
            var source = _sessionVM.AllAppLogsCache;
            if (isActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }

            // --- TIKUN: החלת סינון טווח זמן גלובלי גם על ה-APP ---
            if (IsGlobalTimeRangeActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                source = source.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value).ToList();
            }
            // -----------------------------------------------------

            // בדיקה האם יש פילטרים שמורים (Stored) - אבל נחיל אותם רק אם הצ'קבוקס מסומן
            bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            // Filters are only applied when checkbox is checked (isActive)
            bool hasThreadFilter = isActive && _appActiveThreadFilters.Any();
            bool hasLoggerFilter = isActive && _activeLoggerFilters.Any();
            bool hasMethodFilter = isActive && _activeMethodFilters.Any();
            bool hasTreeFilter = isActive && (_treeShowOnlyLogger != null || _treeShowOnlyPrefix != null || _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0);
            bool hasAdvancedFilter = isActive && _appFilterRoot != null && _appFilterRoot.Children.Count > 0;

            // אם הצ'קבוקס לא מסומן ואין חיפוש, מציגים את הכל
            if (!isActive && !hasSearch)
            {
                AppDevLogsFiltered.ReplaceAll(source);
                return;
            }

            var query = source.AsParallel().AsOrdered();

            // 1. Thread Filter (only if checkbox checked) - use APP-specific list
            if (hasThreadFilter)
                query = query.Where(l => _appActiveThreadFilters.Contains(l.ThreadName));

            // 2. Logger Filter (only if checkbox checked) - use HashSet for O(1) lookup
            if (hasLoggerFilter)
            {
                var loggerSet = new HashSet<string>(_activeLoggerFilters, StringComparer.OrdinalIgnoreCase);
                query = query.Where(l => l.Logger != null && loggerSet.Contains(l.Logger));
            }

            // 3. Method Filter (only if checkbox checked)
            if (hasMethodFilter)
                query = query.Where(l => _activeMethodFilters.Contains(l.Method));

            // 4. Advanced Filter (only if checkbox checked)
            if (hasAdvancedFilter)
                query = query.Where(l => EvaluateFilterNode(l, _appFilterRoot));

            // 5. Tree Filter (only if checkbox checked)
            if (hasTreeFilter)
            {
                if (_treeShowOnlyLogger != null)
                {
                    // Show only this specific logger (prefix match to include children)
                    string showLogger = _treeShowOnlyLogger;
                    string showLoggerDot = showLogger + "."; // Pre-allocate once
                    query = query.Where(l => l.Logger != null &&
                        (l.Logger.Equals(showLogger, StringComparison.OrdinalIgnoreCase) ||
                         l.Logger.StartsWith(showLoggerDot, StringComparison.OrdinalIgnoreCase)));
                }
                else if (_treeShowOnlyPrefix != null)
                {
                    string showPrefix = _treeShowOnlyPrefix;
                    string showPrefixDot = showPrefix + "."; // Pre-allocate once
                    query = query.Where(l => l.Logger != null &&
                        (l.Logger.Equals(showPrefix, StringComparison.OrdinalIgnoreCase) ||
                         l.Logger.StartsWith(showPrefixDot, StringComparison.OrdinalIgnoreCase)));
                }
                else if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                {
                    // Copy to local variables for thread safety with PLINQ
                    var hiddenLoggers = new HashSet<string>(_treeHiddenLoggers, StringComparer.OrdinalIgnoreCase);
                    // Pre-append "." to each prefix once instead of allocating per-log
                    var hiddenPrefixDots = _treeHiddenPrefixes.Select(p => p + ".").ToArray();
                    var hiddenPrefixExact = _treeHiddenPrefixes.ToArray();
                    query = query.Where(l =>
                    {
                        if (l.Logger == null) return true;
                        if (hiddenLoggers.Contains(l.Logger)) return false;
                        for (int i = 0; i < hiddenPrefixExact.Length; i++)
                            if (l.Logger.Equals(hiddenPrefixExact[i], StringComparison.OrdinalIgnoreCase) ||
                                l.Logger.StartsWith(hiddenPrefixDots[i], StringComparison.OrdinalIgnoreCase))
                                return false;
                        return true;
                    });
                }
            }

            // 6. Search (always applied, regardless of checkbox)
            if (hasSearch)
            {
                string search = SearchText;
                if (QueryParserService.HasBooleanOperators(SearchText))
                {
                    var parser = new QueryParserService();
                    var filterTree = parser.Parse(SearchText, out string errorMessage);
                    if (filterTree != null)
                        query = query.Where(l => EvaluateFilterNode(l, filterTree));
                    else
                        query = query.Where(l => MatchesSearch(l, search));
                }
                else
                {
                    query = query.Where(l => MatchesSearch(l, search));
                }
            }

            // 7. APP Filter Out (negative filters) – mirror of PLC filter-out but for APP logs
            if (_isAppFilterOutActive && _appNegativeFilters.Count > 0)
            {
                var threadFiltersOut  = new List<string>();
                var messageFiltersOut = new List<string>();
                foreach (var f in _appNegativeFilters)
                {
                    if (f.StartsWith("THREAD:"))
                        threadFiltersOut.Add(f.Substring(7));
                    else
                        messageFiltersOut.Add(f);
                }

                query = query.Where(l =>
                {
                    for (int i = 0; i < threadFiltersOut.Count; i++)
                        if (l.ThreadName != null && l.ThreadName.IndexOf(threadFiltersOut[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    for (int i = 0; i < messageFiltersOut.Count; i++)
                        if (l.Message != null && l.Message.IndexOf(messageFiltersOut[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    return true;
                });
            }

            var filtered = query.ToList();
            AppDevLogsFiltered.ReplaceAll(filtered);
            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            AppLogger.Info($"[Filter] APP filter applied: {source.Count:N0} → {filtered.Count:N0} entries — {filterSw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Returns true if the search term is found in the entry's Message
        /// OR in any of its plugin-defined ExtraFields values.
        /// </summary>
        private static bool MatchesSearch(LogEntry l, string search)
        {
            if (l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (l.ExtraFields != null)
                foreach (var v in l.ExtraFields.Values)
                    if (v != null && v.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            return false;
        }

        /// <summary>
        /// Recursively evaluates a filter tree node against a log entry, returning true if the entry matches.
        /// </summary>
        public bool EvaluateFilterNode(LogEntry log, FilterNode node)
        {
            if (node == null) return true;

            if (node.Type == NodeType.Condition)
            {
                string val = "";
                switch (node.Field)
                {
                    case "Level":       val = log.Level;       break;
                    case "ThreadName":  val = log.ThreadName;  break;
                    case "Logger":      val = log.Logger;      break;
                    case "ProcessName": val = log.ProcessName; break;
                    case "Method":      val = log.Method;      break;
                    case "Pattern":     val = log.Pattern;     break;
                    case "Data":        val = log.Data;        break;
                    case "Exception":   val = log.Exception;   break;
                    case "Message":     val = log.Message;     break;
                    default:
                        // Check ExtraFields for plugin/custom fields — no fallback to Message
                        if (log.ExtraFields != null &&
                            log.ExtraFields.TryGetValue(node.Field, out string efVal))
                            val = efVal ?? "";
                        else
                            return false; // Field not found → condition does not match
                        break;
                }

                if (string.IsNullOrEmpty(val)) return false;

                string op = node.Operator;
                string criteria = node.Value;

                if (op == "Equals") return val.Equals(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Begins With") return val.StartsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Ends With") return val.EndsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Regex")
                {
                    try { return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
                    catch (Exception ex) { AppLogger.Warn($"Invalid regex pattern '{criteria}': {ex.Message}"); return false; }
                }
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
                    foreach (var child in node.Children)
                    {
                        if (EvaluateFilterNode(log, child)) { baseResult = true; break; }
                    }
                }
                else
                {
                    baseResult = true;
                    foreach (var child in node.Children)
                    {
                        if (!EvaluateFilterNode(log, child)) { baseResult = false; break; }
                    }
                }

                if (op.StartsWith("NOT")) return !baseResult;
                return baseResult;
            }
        }

        /// <summary>
        /// Returns true if the log entry matches the default PLC filter (used when no explicit filters are active).
        /// </summary>
        public bool IsDefaultLog(LogEntry l)
        {
            var filter = _defaultPlcFilter ?? DefaultConfigurationService.GetFactoryPlcFilter();
            return EvaluateFilterNode(l, filter);
        }

        /// <summary>
        /// Resets all filter state including advanced filters, thread/logger filters, search, and tree filters.
        /// </summary>
        public void ClearFilters()
        {
            _mainFilterRoot = null;
            _appFilterRoot = null;
            _savedFilterRoot = null;
            IsMainFilterActive = false;
            IsAppFilterActive = false;
            IsAppErrorFilterActive = false;
            IsMainFilterOutActive = false;
            IsAppFilterOutActive = false;
            IsTimeFocusActive = false;
            IsAppTimeFocusActive = false;
            SearchText = "";

            _negativeFilters.Clear();
            _appNegativeFilters.Clear();

            // Clear all column filters
            _activeThreadFilters.Clear();
            _appActiveThreadFilters.Clear();
            _activeLoggerFilters.Clear();
            _activeMethodFilters.Clear();

            _lastFilteredCache = null;
            _lastFilteredAppCache = null;

            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;

            // PLC tree filters
            _plcTreeHiddenLoggers.Clear();
            _plcTreeHiddenPrefixes.Clear();
            _plcTreeShowOnlyLogger = null;
            _plcTreeShowOnlyPrefix = null;

            // Reset visual state on all tree nodes
            ResetTreeVisualState();
            ResetPlcVisualStates();
        }

        /// <summary>
        /// Clear a specific active filter by its key. Called on double-click in Active Filters panel.
        /// This deactivates the filter effect without deleting the underlying data,
        /// so reloading a configuration will restore it.
        /// </summary>
        public void RemoveActiveFilter(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            bool needMainRefresh = false;
            bool needAppRefresh = false;
            bool needColorRefresh = false;

            switch (key)
            {
                // === APP-specific filters ===
                case "APP_ERROR_FILTER":
                    IsAppErrorFilterActive = false;
                    needAppRefresh = true;
                    break;
                case "APP_TIME_FOCUS":
                    IsAppTimeFocusActive = false;
                    needAppRefresh = true;
                    break;
                // APP_THREAD is now handled as parameterized key in default case (APP_THREAD:threadName)


                // === PLC/Main-specific filters ===
                case "MAIN_TIME_FOCUS":
                    IsTimeFocusActive = false;
                    needMainRefresh = true;
                    break;
                // MAIN_THREAD is now handled as parameterized key in default case (MAIN_THREAD:threadName)


                // === Shared filters ===
                case "GLOBAL_TIME_RANGE":
                    _globalTimeRangeStart = null;
                    _globalTimeRangeEnd = null;
                    OnPropertyChanged(nameof(IsGlobalTimeRangeActive));
                    needMainRefresh = true;
                    needAppRefresh = true;
                    break;
                case "SEARCH":
                    SearchText = "";
                    needMainRefresh = true;
                    needAppRefresh = true;
                    break;
                case "RANGE":
                    _hasRangeStart = false;
                    _rangeStartLog = null;
                    break;

                // === Logger tree filters ===
                case "TREE_SHOW_ONLY_LOGGER":
                    _treeShowOnlyLogger = null;
                    ResetTreeVisualState();
                    needAppRefresh = true;
                    break;
                case "TREE_SHOW_ONLY_PREFIX":
                    _treeShowOnlyPrefix = null;
                    ResetTreeVisualState();
                    needAppRefresh = true;
                    break;
                case "PLC_TREE_SHOW_ONLY_LOGGER":
                    _plcTreeShowOnlyLogger = null;
                    ResetPlcVisualStates();
                    needMainRefresh = true;
                    break;
                case "PLC_TREE_SHOW_ONLY_PREFIX":
                    _plcTreeShowOnlyPrefix = null;
                    ResetPlcVisualStates();
                    needMainRefresh = true;
                    break;

                default:
                    // Handle parameterized keys like "APP_FILTER:0", "LOGGER:xxx", "COLORING:1", etc.
                    if (key.StartsWith("APP_FILTER:"))
                    {
                        if (int.TryParse(key.Substring(11), out int appIdx))
                        {
                            RemoveFilterConditionByIndex(_appFilterRoot, appIdx);
                            // If tree is now empty, deactivate the filter entirely
                            if (_appFilterRoot?.Children == null || _appFilterRoot.Children.Count == 0)
                            {
                                _appFilterRoot = null;
                                IsAppFilterActive = false;
                            }
                            _lastFilteredAppCache = null;
                            needAppRefresh = true;
                        }
                    }
                    else if (key.StartsWith("MAIN_FILTER:"))
                    {
                        if (int.TryParse(key.Substring(12), out int mainIdx))
                        {
                            RemoveFilterConditionByIndex(_mainFilterRoot, mainIdx);
                            // If tree is now empty, deactivate the filter entirely
                            if (_mainFilterRoot?.Children == null || _mainFilterRoot.Children.Count == 0)
                            {
                                _mainFilterRoot = null;
                                IsMainFilterActive = false;
                            }
                            _lastFilteredCache = null;
                            needMainRefresh = true;
                        }
                    }
                    else if (key.StartsWith("APP_THREAD:"))
                    {
                        var threadName = key.Substring(11);
                        _appActiveThreadFilters.Remove(threadName);
                        // Re-sync thread conditions in filter tree
                        RemoveThreadConditionsFromFilterTree(true);
                        if (_appActiveThreadFilters.Any())
                        {
                            SyncThreadFiltersToFilterTree(true, _appActiveThreadFilters);
                        }
                        CheckIfFiltersEmpty(true);
                        _lastFilteredAppCache = null;
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("MAIN_THREAD:"))
                    {
                        var threadName = key.Substring(12);
                        _activeThreadFilters.Remove(threadName);
                        // Re-sync thread conditions in filter tree
                        RemoveThreadConditionsFromFilterTree(false);
                        if (_activeThreadFilters.Any())
                        {
                            SyncThreadFiltersToFilterTree(false, _activeThreadFilters);
                        }
                        CheckIfFiltersEmpty(false);
                        _lastFilteredCache = null;
                        needMainRefresh = true;
                    }
                    else if (key.StartsWith("LOGGER:"))
                    {
                        var logger = key.Substring(7);
                        _activeLoggerFilters.Remove(logger);
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("METHOD:"))
                    {
                        var method = key.Substring(7);
                        _activeMethodFilters.Remove(method);
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("TREE_HIDE_LOGGER:"))
                    {
                        var logger = key.Substring(17);
                        _treeHiddenLoggers.Remove(logger);
                        ResetTreeVisualState();
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("TREE_HIDE_PREFIX:"))
                    {
                        var prefix = key.Substring(17);
                        _treeHiddenPrefixes.Remove(prefix);
                        ResetTreeVisualState();
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("PLC_TREE_HIDE_LOGGER:"))
                    {
                        var logger = key.Substring(21);
                        _plcTreeHiddenLoggers.Remove(logger);
                        ResetPlcVisualStates();
                        needMainRefresh = true;
                    }
                    else if (key.StartsWith("PLC_TREE_HIDE_PREFIX:"))
                    {
                        var prefix = key.Substring(21);
                        _plcTreeHiddenPrefixes.Remove(prefix);
                        ResetPlcVisualStates();
                        needMainRefresh = true;
                    }
                    else if (key.StartsWith("NEGATIVE:"))
                    {
                        var nf = key.Substring(9);
                        _negativeFilters.Remove(nf);
                        if (!_negativeFilters.Any()) IsMainFilterOutActive = false;
                        needMainRefresh = true;
                    }
                    else if (key.StartsWith("APP_NEGATIVE:"))
                    {
                        var nf = key.Substring(13);
                        _appNegativeFilters.Remove(nf);
                        if (!_appNegativeFilters.Any()) IsAppFilterOutActive = false;
                        needAppRefresh = true;
                    }
                    else if (key.StartsWith("COLORING:"))
                    {
                        // Remove specific session coloring rule by index
                        if (int.TryParse(key.Substring(9), out int colorIdx))
                        {
                            int tab = _parent?.SelectedTabIndex ?? 0;
                            bool isApp = (tab == AppConstants.TAB_APP);
                            var rules = isApp ? _parent?.CaseVM?.AppColoringRules : _parent?.CaseVM?.MainColoringRules;
                            if (rules != null && colorIdx >= 0 && colorIdx < rules.Count)
                            {
                                rules.RemoveAt(colorIdx);
                                needColorRefresh = true;
                            }
                        }
                    }
                    else if (key.StartsWith("DEFAULT_COLORING:"))
                    {
                        // Remove specific default coloring rule by index
                        if (int.TryParse(key.Substring(17), out int colorIdx))
                        {
                            int tab = _parent?.SelectedTabIndex ?? 0;
                            bool isApp = (tab == AppConstants.TAB_APP);
                            var rules = isApp ? _parent?.ColoringService?.UserDefaultAppRules : _parent?.ColoringService?.UserDefaultMainRules;
                            if (rules != null && colorIdx >= 0 && colorIdx < rules.Count)
                            {
                                rules.RemoveAt(colorIdx);
                                needColorRefresh = true;
                            }
                        }
                    }
                    break;
            }

            // Refresh filters as needed
            if (needMainRefresh)
            {
                _lastFilteredCache = null;
                ApplyMainLogsFilter();
            }
            if (needAppRefresh)
            {
                _lastFilteredAppCache = null;
                ApplyAppLogsFilter();
            }
            if (needColorRefresh)
            {
                // Re-apply coloring after disabling a rule
                var allLogs = _sessionVM?.AllLogsCache;
                var allAppLogs = _sessionVM?.AllAppLogsCache;
                if (allLogs != null)
                    _parent?.ColoringService?.ApplyDefaultColorsAsync(allLogs, false);
                if (allAppLogs != null)
                    _parent?.ColoringService?.ApplyDefaultColorsAsync(allAppLogs, true);

                // Apply remaining session coloring rules
                var mainRules = _parent?.CaseVM?.MainColoringRules;
                var appRules = _parent?.CaseVM?.AppColoringRules;
                if (mainRules != null && mainRules.Count > 0 && allLogs != null)
                    _parent?.ColoringService?.ApplyCustomColoringAsync(allLogs, mainRules);
                if (appRules != null && appRules.Count > 0 && allAppLogs != null)
                    _parent?.ColoringService?.ApplyCustomColoringAsync(allAppLogs, appRules);

                // Force UI refresh
                ApplyMainLogsFilter();
                ApplyAppLogsFilter();
            }

            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
        }

        /// <summary>
        /// Re-applies all filters on both PLC and APP logs and refreshes the active filters display.
        /// </summary>
        public void ToggleFilterView(bool show)
        {
            ApplyMainLogsFilter();
            ApplyAppLogsFilter();
            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
        }

        /// <summary>
        /// Clears all APP logger tree filter state (hidden loggers, show-only selections).
        /// </summary>
        public void ResetTreeFilters()
        {
            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;
        }

        private async Task OpenFilterWindow(object obj)
        {
            try
            {
            // ── Different Logs tab (index 12): route to its own filter logic ──
            if (_parent.SelectedTabIndex == 12)
            {
                await OpenDifferentLogsFilterWindow(obj);
                return;
            }

            // Save the currently selected log and its scroll position BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            bool isAppTab = _parent.SelectedTabIndex == 1;

            // Get available threads and loggers from the appropriate cache
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;
            var threads = cache?.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList() ?? new List<string>();
            var loggers = cache?.Select(l => l.Logger).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList() ?? new List<string>();

            var win = new Views.FilterWindow(threads, loggers);
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = Application.Current.MainWindow;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            if (currentRoot != null)
            {
                win.ViewModel.RootNodes.Clear();
                win.ViewModel.RootNodes.Add(currentRoot.DeepClone());
            }

            if (win.ShowDialog() == true)
            {
                // Check if user clicked "Reset" button to clear all filters
                if (win.ShouldClearAllFilters)
                {
                    _sessionVM.IsBusy = true;

                    await Task.Run(() =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Clear all filters for the current tab
                            if (isAppTab)
                            {
                                AppFilterRoot = null;
                                _activeLoggerFilters.Clear();
                                _activeMethodFilters.Clear();
                                _appActiveThreadFilters.Clear();
                                IsAppFilterActive = false;
                                IsAppTimeFocusActive = false;
                                LastFilteredAppCache = null;
                                ResetTreeFilters();
                            }
                            else
                            {
                                MainFilterRoot = null;
                                _activeThreadFilters.Clear();
                                IsMainFilterActive = false;
                                IsMainFilterOutActive = false;
                                IsTimeFocusActive = false;
                                LastFilteredCache = null;
                                _negativeFilters.Clear();
                            }
                        });
                    });

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (isAppTab)
                            ApplyAppLogsFilter();
                        else
                            ApplyMainLogsFilter();

                        _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                        _sessionVM.IsBusy = false;

                        // Restore the selected log and scroll to it, preserving visual position
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    _parent.SelectedLog = logToRestore;
                                    _parent.ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    });
                    return;
                }

                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                _sessionVM.IsBusy = true;

                // Clear separate thread filters since FilterWindow now contains all filter conditions
                // This prevents duplicate filtering when user modifies a ThreadFilter condition in FilterWindow
                _activeThreadFilters.Clear();

                await Task.Run(() =>
                {
                    if (isAppTab)
                    {
                        AppFilterRoot = newRoot;
                    }
                    else
                    {
                        MainFilterRoot = newRoot;
                        if (hasAdvanced)
                        {
                            var cacheCopy = _sessionVM.AllLogsCache?.ToList() ?? new List<LogEntry>();
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, MainFilterRoot)).ToList();
                            LastFilteredCache = res;
                        }
                        else LastFilteredCache = null;
                    }
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isAppTab)
                    {
                        IsAppFilterActive = hasAdvanced;
                        ApplyAppLogsFilter();
                    }
                    else
                    {
                        IsMainFilterActive = hasAdvanced;
                        ApplyMainLogsFilter();
                    }
                    _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                    _sessionVM.IsBusy = false;
                });
            }
            }
            catch (Exception ex) { AppLogger.Error("OpenFilterWindow failed", ex); }
        }

        /// <summary>
        /// Opens the filter window targeting the Different Logs tab (tab 12).
        /// Uses dynamic fields from the loaded plugin columns.
        /// </summary>
        private async Task OpenDifferentLogsFilterWindow(object obj)
        {
            try
            {
            var diffVM = _parent.DifferentLogsVM;
            if (diffVM == null || !diffVM.HasFile) return;

            // Safety: rebuild available fields if empty (could happen if load order was interrupted)
            if (diffVM.AvailableFields == null || diffVM.AvailableFields.Count == 0)
            {
                diffVM.BuildAvailableFields();
            }

            // Get threads/loggers from the Different Logs entries
            var threads = diffVM.AllLogEntries.Select(l => l.ThreadName)
                .Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            var loggers = diffVM.AllLogEntries.Select(l => l.Logger)
                .Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();

            // Create window with dynamic fields from plugin columns
            var win = new Views.FilterWindow(threads, loggers, diffVM.AvailableFields);

            // Position near button
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = Application.Current.MainWindow;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            // Load existing filter if any
            if (diffVM.FilterRoot != null)
            {
                win.ViewModel.RootNodes.Clear();
                win.ViewModel.RootNodes.Add(diffVM.FilterRoot.DeepClone());
            }

            if (win.ShowDialog() == true)
            {
                if (win.ShouldClearAllFilters)
                {
                    diffVM.FilterRoot = null;
                    diffVM.IsFilterActive = false;
                    diffVM.FilteredEntries = new ObservableCollection<LogEntry>(diffVM.AllLogEntries);
                    _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                    return;
                }

                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                diffVM.FilterRoot = newRoot;
                diffVM.IsFilterActive = hasAdvanced;

                if (hasAdvanced)
                {
                    await Task.Run(() =>
                    {
                        var filtered = diffVM.AllLogEntries
                            .Where(l => EvaluateFilterNode(l, newRoot))
                            .ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            diffVM.FilteredEntries = new ObservableCollection<LogEntry>(filtered);
                        });
                    });
                }
                else
                {
                    diffVM.FilteredEntries = new ObservableCollection<LogEntry>(diffVM.AllLogEntries);
                }

                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
            }
            }
            catch (Exception ex) { AppLogger.Error("OpenDifferentLogsFilterWindow failed", ex); }
        }

        private void FilterOut(object p)
        {
            if (_parent.SelectedLog == null) return;
            var w = new Views.FilterOutWindow(_parent.SelectedLog.Message);
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.TextToRemove))
            {
                bool isAppTab = _parent.SelectedTabIndex == 1;
                if (isAppTab)
                {
                    _appNegativeFilters.Add(w.TextToRemove);
                    IsAppFilterOutActive = true;
                }
                else
                {
                    _negativeFilters.Add(w.TextToRemove);
                    IsMainFilterOutActive = true;
                }
                ToggleFilterView(true);
            }
        }

        private void FilterOutThread(object obj)
        {
            if (_parent.SelectedLog == null || string.IsNullOrEmpty(_parent.SelectedLog.ThreadName)) return;
            var win = new Views.FilterOutWindow(_parent.SelectedLog.ThreadName);
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.TextToRemove))
            {
                string filterKey = "THREAD:" + win.TextToRemove;
                bool isAppTab = _parent.SelectedTabIndex == 1;
                if (isAppTab)
                {
                    if (!_appNegativeFilters.Contains(filterKey))
                    {
                        _appNegativeFilters.Add(filterKey);
                        IsAppFilterOutActive = true;
                        ToggleFilterView(true);
                    }
                }
                else
                {
                    if (!_negativeFilters.Contains(filterKey))
                    {
                        _negativeFilters.Add(filterKey);
                        IsMainFilterOutActive = true;
                        ToggleFilterView(true);
                    }
                }
            }
        }

        private void OpenThreadFilter(object obj)
        {
            // Check which tab is active and use appropriate cache
            bool isAppTab = _parent.SelectedTabIndex == 1;
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;

            if (cache == null || !cache.Any()) return;
            var threads = cache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

            // Save the currently selected log and its scroll position BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            var win = new Views.ThreadFilterWindow(threads) { Title = "Filter by Thread" };

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = Application.Current.MainWindow;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            if (win.ShowDialog() == true)
            {
                // Use the correct thread filter list per tab
                var threadList = isAppTab ? _appActiveThreadFilters : _activeThreadFilters;

                if (win.ShouldClear)
                {
                    threadList.Clear();
                    // Also remove thread conditions from the filter tree
                    RemoveThreadConditionsFromFilterTree(isAppTab);
                    CheckIfFiltersEmpty(isAppTab);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    threadList.Clear();
                    threadList.AddRange(win.SelectedThreads);
                    // Sync thread filters to filter tree so they appear in Filter Window
                    SyncThreadFiltersToFilterTree(isAppTab, win.SelectedThreads);
                    SetFilterActive(isAppTab);
                }
                ToggleFilterView(true); // Must re-trigger filter

                // Restore the selected log and scroll to it after CLEAR
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }
            }
        }

        /// <summary>
        /// Syncs thread filters to the filter tree so they appear in the Filter Window.
        /// Creates an OR group with all selected threads as conditions.
        /// </summary>
        private void SyncThreadFiltersToFilterTree(bool isAppTab, List<string> selectedThreads)
        {
            // Get or create the root filter node
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;

            if (currentRoot == null)
            {
                currentRoot = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" };
                if (isAppTab) AppFilterRoot = currentRoot;
                else MainFilterRoot = currentRoot;
            }

            // First, remove any existing thread filter group
            RemoveThreadConditionsFromFilterTree(isAppTab);

            // If only one thread, add it directly as a condition
            if (selectedThreads.Count == 1)
            {
                var condition = new FilterNode
                {
                    Type = NodeType.Condition,
                    Field = "ThreadName",
                    Operator = "Equals",
                    Value = selectedThreads[0]
                };
                currentRoot.Children.Add(condition);
            }
            else if (selectedThreads.Count > 1)
            {
                // Create an OR group for multiple threads
                var threadGroup = new FilterNode
                {
                    Type = NodeType.Group,
                    LogicalOperator = "OR"
                };

                foreach (var thread in selectedThreads)
                {
                    var condition = new FilterNode
                    {
                        Type = NodeType.Condition,
                        Field = "ThreadName",
                        Operator = "Equals",
                        Value = thread
                    };
                    threadGroup.Children.Add(condition);
                }

                currentRoot.Children.Add(threadGroup);
            }

            // Notify property changed
            if (isAppTab) OnPropertyChanged(nameof(AppFilterRoot));
            else OnPropertyChanged(nameof(MainFilterRoot));
        }

        /// <summary>
        /// Removes all ThreadName conditions from the filter tree.
        /// </summary>
        private void RemoveThreadConditionsFromFilterTree(bool isAppTab)
        {
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;
            if (currentRoot == null || currentRoot.Children == null) return;

            // Remove thread conditions recursively
            RemoveThreadConditionsRecursive(currentRoot);

            // Notify property changed
            if (isAppTab) OnPropertyChanged(nameof(AppFilterRoot));
            else OnPropertyChanged(nameof(MainFilterRoot));
        }

        private void RemoveThreadConditionsRecursive(FilterNode node)
        {
            if (node.Children == null) return;

            // Find items to remove (ThreadName conditions and groups containing only ThreadName conditions)
            var toRemove = new List<FilterNode>();

            foreach (var child in node.Children)
            {
                if (child.Type == NodeType.Condition && child.Field == "ThreadName")
                {
                    toRemove.Add(child);
                }
                else if (child.Type == NodeType.Group)
                {
                    // Check if this group contains only ThreadName conditions
                    if (child.Children != null && child.Children.All(c => c.Type == NodeType.Condition && c.Field == "ThreadName"))
                    {
                        toRemove.Add(child);
                    }
                    else
                    {
                        // Recursively clean nested groups
                        RemoveThreadConditionsRecursive(child);
                    }
                }
            }

            foreach (var item in toRemove)
            {
                node.Children.Remove(item);
            }
        }

        private void OpenLoggerFilter(object obj)
        {
            bool isAppTab = _parent.SelectedTabIndex == 1;

            if (!isAppTab)
            {
                return;
            }

            var cache = _sessionVM.AllAppLogsCache;

            if (cache == null || !cache.Any())
            {
                return;
            }

            var loggers = cache.Select(l => l.Logger).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();

            // Save the currently selected log BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;

            var win = new Views.ThreadFilterWindow(loggers) { Title = "Filter by Logger" };

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = Application.Current.MainWindow;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeLoggerFilters.Clear();
                    CheckIfFiltersEmpty(true);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeLoggerFilters.Clear();
                    _activeLoggerFilters.AddRange(win.SelectedThreads);
                    SetFilterActive(true);
                }

                ToggleFilterView(true);

                // Restore the selected log and scroll to it (only on clear)
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }
            }
        }

        private void OpenMethodFilter(object obj)
        {
            bool isAppTab = _parent.SelectedTabIndex == 1;
            if (!isAppTab) return;

            var cache = _sessionVM.AllAppLogsCache;
            if (cache == null || !cache.Any()) return;

            var methods = cache.Select(l => l.Method).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m).ToList();

            // Save the currently selected log BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;

            var win = new Views.ThreadFilterWindow(methods) { Title = "Filter by Method" };

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = Application.Current.MainWindow;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeMethodFilters.Clear();
                    CheckIfFiltersEmpty(true);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeMethodFilters.Clear();
                    _activeMethodFilters.AddRange(win.SelectedThreads);
                    SetFilterActive(true);
                }
                ToggleFilterView(true);

                // Restore the selected log and scroll to it (only on clear)
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }
            }
        }

        private void SetFilterActive(bool isAppTab)
        {
            if (isAppTab) IsAppFilterActive = true;
            else IsMainFilterActive = true;
        }

        private void CheckIfFiltersEmpty(bool isAppTab)
        {
            if (isAppTab)
            {
                // Check if app filter root is empty (null or has no children)
                bool appFilterRootEmpty = _appFilterRoot == null || _appFilterRoot.Children == null || _appFilterRoot.Children.Count == 0;
                bool noTreeFilters = _treeShowOnlyLogger == null && _treeShowOnlyPrefix == null && _treeHiddenLoggers.Count == 0 && _treeHiddenPrefixes.Count == 0;

                if (!_appActiveThreadFilters.Any() && !_activeLoggerFilters.Any() && !_activeMethodFilters.Any() && appFilterRootEmpty && noTreeFilters)
                {
                    IsAppFilterActive = false;
                    _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                }
            }
            else
            {
                // Check if main filter root is empty (null or has no children)
                bool mainFilterRootEmpty = _mainFilterRoot == null || _mainFilterRoot.Children == null || _mainFilterRoot.Children.Count == 0;

                if (!_activeThreadFilters.Any() && mainFilterRootEmpty)
                {
                    IsMainFilterActive = false;
                    _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                }
            }
        }

        // --- ?????? ?? ???????? ????? ---
        private void FilterContext(object obj)
        {
            if (_parent.SelectedLog == null) return;
            _sessionVM.IsBusy = true;
            double multiplier = _parent.SelectedTimeUnit == "Minutes" ? 60 : 1;
            double rangeInSeconds = _parent.ContextSeconds * multiplier;
            DateTime targetTime = _parent.SelectedLog.Date;
            DateTime startTime = targetTime.AddSeconds(-rangeInSeconds);
            DateTime endTime = targetTime.AddSeconds(rangeInSeconds);
            bool isAppTab = _parent.SelectedTabIndex == 1;

            Task.Run(() =>
            {
                if (isAppTab)
                {
                    if (_sessionVM.AllAppLogsCache != null)
                    {
                        var contextLogs = _sessionVM.AllAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderBy(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LastFilteredAppCache = contextLogs;
                            IsAppTimeFocusActive = true;
                            AppFilterRoot = null;
                            IsAppFilterActive = true;
                            ToggleFilterView(true);
                            _sessionVM.StatusMessage = $"APP Focus Time: {contextLogs.Count} logs shown";
                            _sessionVM.IsBusy = false;
                        });
                    }
                }
                else
                {
                    if (_sessionVM.AllLogsCache != null)
                    {
                        var contextLogs = _sessionVM.AllLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderBy(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LastFilteredCache = contextLogs;
                            SavedFilterRoot = null;
                            IsTimeFocusActive = true;
                            IsMainFilterActive = true;
                            ToggleFilterView(true);
                            _sessionVM.StatusMessage = $"Focus Time: +/- {rangeInSeconds}s | {contextLogs.Count} logs shown";
                            _sessionVM.IsBusy = false;
                        });
                    }
                }
            });
        }

        private void StartRange(object obj)
        {
            if (_parent.SelectedLog == null) return;
            _rangeStartLog = _parent.SelectedLog;
            HasRangeStart = true;
            _sessionVM.StatusMessage = $"Range Start: {_rangeStartLog.Date:HH:mm:ss.ffffff} — Now scroll to end and select 'End Range'";
        }

        private void EndRange(object obj)
        {
            if (_parent.SelectedLog == null || _rangeStartLog == null) return;

            var logA = _rangeStartLog;
            var logB = _parent.SelectedLog;
            var startTime = logA.Date < logB.Date ? logA.Date : logB.Date;
            var endTime = logA.Date < logB.Date ? logB.Date : logA.Date;

            _sessionVM.IsBusy = true;
            bool isAppTab = _parent.SelectedTabIndex == 1;

            Task.Run(() =>
            {
                if (isAppTab)
                {
                    if (_sessionVM.AllAppLogsCache != null)
                    {
                        // Find indices of the two selected rows to get exact range
                        int idxA = _sessionVM.AllAppLogsCache.IndexOf(logA);
                        int idxB = _sessionVM.AllAppLogsCache.IndexOf(logB);
                        List<LogEntry> rangedLogs;
                        if (idxA >= 0 && idxB >= 0)
                        {
                            int startIdx = Math.Min(idxA, idxB);
                            int endIdx = Math.Max(idxA, idxB);
                            rangedLogs = _sessionVM.AllAppLogsCache.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();
                        }
                        else
                        {
                            // Fallback to time-based range
                            rangedLogs = _sessionVM.AllAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).ToList();
                        }
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LastFilteredAppCache = rangedLogs;
                            IsAppTimeFocusActive = true;
                            AppFilterRoot = null;
                            IsAppFilterActive = true;
                            ToggleFilterView(true);
                            _sessionVM.StatusMessage = $"Range Filter: {startTime:HH:mm:ss.ffffff} → {endTime:HH:mm:ss.ffffff} | {rangedLogs.Count} logs";
                            _sessionVM.IsBusy = false;
                        });
                    }
                }
                else
                {
                    if (_sessionVM.AllLogsCache != null)
                    {
                        // Find indices of the two selected rows to get exact range
                        int idxA = _sessionVM.AllLogsCache.IndexOf(logA);
                        int idxB = _sessionVM.AllLogsCache.IndexOf(logB);
                        List<LogEntry> rangedLogs;
                        if (idxA >= 0 && idxB >= 0)
                        {
                            int startIdx = Math.Min(idxA, idxB);
                            int endIdx = Math.Max(idxA, idxB);
                            rangedLogs = _sessionVM.AllLogsCache.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();
                        }
                        else
                        {
                            // Fallback to time-based range
                            rangedLogs = _sessionVM.AllLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).ToList();
                        }
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LastFilteredCache = rangedLogs;
                            SavedFilterRoot = null;
                            IsTimeFocusActive = true;
                            IsMainFilterActive = true;
                            ToggleFilterView(true);
                            _sessionVM.StatusMessage = $"Range Filter: {startTime:HH:mm:ss.ffffff} → {endTime:HH:mm:ss.ffffff} | {rangedLogs.Count} logs";
                            _sessionVM.IsBusy = false;
                        });
                    }
                }
            });

            _rangeStartLog = null;
            HasRangeStart = false;
        }

        private void ClearRange(object obj)
        {
            _rangeStartLog = null;
            HasRangeStart = false;

            // Also clear the applied range filter (TimeFocus) so it disappears from ACTIVE FILTERS
            bool isAppTab = _parent.SelectedTabIndex == 1;
            if (isAppTab)
            {
                if (_isAppTimeFocusActive)
                {
                    IsAppTimeFocusActive = false;
                    LastFilteredAppCache = null;
                    IsAppFilterActive = false;
                }
            }
            else
            {
                if (_isTimeFocusActive)
                {
                    IsTimeFocusActive = false;
                    LastFilteredCache = null;
                    IsMainFilterActive = false;
                }
            }

            // Re-apply filters (will show unfiltered if no other filters remain)
            ToggleFilterView(false);

            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            _sessionVM.StatusMessage = "Range selection cleared";
        }

        private void UndoFilterOut(object obj)
        {
            bool isAppTab = _parent?.SelectedTabIndex == 1;
            if (isAppTab)
            {
                if (_appNegativeFilters.Any())
                {
                    _appNegativeFilters.RemoveAt(_appNegativeFilters.Count - 1);
                    if (!_appNegativeFilters.Any())
                        IsAppFilterOutActive = false;
                    ToggleFilterView(IsAppFilterActive || IsAppFilterOutActive);
                }
            }
            else
            {
                if (_negativeFilters.Any())
                {
                    _negativeFilters.RemoveAt(_negativeFilters.Count - 1);
                    if (!_negativeFilters.Any())
                        IsMainFilterOutActive = false;
                    ToggleFilterView(IsMainFilterActive || IsMainFilterOutActive);
                }
            }
        }

        private bool IsPlcTabActive => _parent?.IsPLCTabSelected == true;

        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    _plcTreeHiddenLoggers.Remove(node.FullPath);
                    _plcTreeHiddenPrefixes.Remove(node.FullPath);
                    var prefixesToRemove = _plcTreeHiddenPrefixes
                        .Where(p => node.FullPath == p || node.FullPath.StartsWith(p + ".")).ToList();
                    foreach (var p in prefixesToRemove) _plcTreeHiddenPrefixes.Remove(p);

                    node.IsHidden = false;
                    node.IsActive = false;
                    SetChildrenVisualState(node, false, false);

                    bool hasAny = _plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0;
                    IsMainFilterActive = hasAny || IsMainFilterActive;
                    if (!hasAny) ResetPlcVisualStates();
                    ToggleFilterView(hasAny);
                }
                else
                {
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    _treeHiddenLoggers.Remove(node.FullPath);
                    _treeHiddenPrefixes.Remove(node.FullPath);
                    var prefixesToRemove = _treeHiddenPrefixes
                        .Where(p => node.FullPath == p || node.FullPath.StartsWith(p + ".")).ToList();
                    foreach (var p in prefixesToRemove) _treeHiddenPrefixes.Remove(p);

                    node.IsHidden = false;
                    node.IsActive = false;
                    SetChildrenVisualState(node, false, false);

                    bool hasAnyTreeFilter = _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0;
                    IsAppFilterActive = hasAnyTreeFilter || HasAnyColumnFilter();
                    if (!IsAppFilterActive) ResetAllVisualStates();
                    ToggleFilterView(IsAppFilterActive);
                }
            }
        }

        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    if (_plcTreeShowOnlyPrefix != null || _plcTreeShowOnlyLogger != null) ResetPlcVisualStates();
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    if (node.Children != null && node.Children.Count > 0)
                        _plcTreeHiddenPrefixes.Add(node.FullPath);
                    else
                        _plcTreeHiddenLoggers.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    ToggleFilterView(true);
                }
                else
                {
                    if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null) ResetAllVisualStates();
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    if (node.Children != null && node.Children.Count > 0)
                        _treeHiddenPrefixes.Add(node.FullPath);
                    else
                        _treeHiddenLoggers.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    ResetPlcTreeFilters();
                    _plcTreeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath, PlcLoggerTreeRoot);
                    ToggleFilterView(true);
                }
                else
                {
                    ResetTreeFilters();
                    _treeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    ResetPlcTreeFilters();
                    _plcTreeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath, PlcLoggerTreeRoot);
                    ToggleFilterView(true);
                }
                else
                {
                    ResetTreeFilters();
                    _treeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    if (_plcTreeShowOnlyPrefix != null || _plcTreeShowOnlyLogger != null) ResetPlcVisualStates();
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    _plcTreeHiddenPrefixes.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    ToggleFilterView(true);
                }
                else
                {
                    if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null) ResetAllVisualStates();
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    _treeHiddenPrefixes.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowAll(object obj)
        {
            if (IsPlcTabActive)
            {
                ResetPlcTreeFilters();
                ResetPlcVisualStates();
                ToggleFilterView(false);
            }
            else
            {
                ResetTreeFilters();
                ResetAllVisualStates();
                IsAppFilterActive = HasAnyColumnFilter();
                ToggleFilterView(IsAppFilterActive);
            }
        }

        /// <summary>
        /// Clears all PLC logger tree filter state (hidden loggers, show-only selections).
        /// </summary>
        public void ResetPlcTreeFilters()
        {
            _plcTreeHiddenLoggers.Clear();
            _plcTreeHiddenPrefixes.Clear();
            _plcTreeShowOnlyLogger = null;
            _plcTreeShowOnlyPrefix = null;
        }

        private void ResetPlcVisualStates()
        {
            foreach (var rootNode in PlcLoggerTreeRoot)
            {
                ResetNodeVisualState(rootNode);
            }
        }

        private void ResetNodeVisualState(LoggerNode node)
        {
            node.IsHidden = false;
            node.IsActive = false;
            foreach (var child in node.Children)
                ResetNodeVisualState(child);
        }

        private void MarkAllNodesShowOnly(string activePrefix, ObservableCollection<LoggerNode> treeRoot)
        {
            foreach (var rootNode in treeRoot)
            {
                MarkNodeShowOnly(rootNode, activePrefix);
            }
        }

        /// <summary>
        /// Recursively set IsHidden and IsActive on all children of a node
        /// </summary>
        private void SetChildrenVisualState(LoggerNode node, bool isHidden, bool isActive)
        {
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                child.IsHidden = isHidden;
                child.IsActive = isActive;
                SetChildrenVisualState(child, isHidden, isActive);
            }
        }

        /// <summary>
        /// Mark all nodes as hidden, then mark the matching node (by prefix) and its children as active.
        /// This gives clear visual feedback for "Show Only This" / "Show With Children".
        /// </summary>
        private void MarkAllNodesShowOnly(string activePrefix)
        {
            foreach (var rootNode in LoggerTreeRoot)
            {
                MarkNodeShowOnly(rootNode, activePrefix);
            }
        }

        private void MarkNodeShowOnly(LoggerNode node, string activePrefix)
        {
            bool isMatch = node.FullPath != null &&
                (node.FullPath.Equals(activePrefix, System.StringComparison.OrdinalIgnoreCase) ||
                 node.FullPath.StartsWith(activePrefix + ".", System.StringComparison.OrdinalIgnoreCase));

            // Also check if this node is a parent/ancestor of the active prefix
            bool isAncestor = activePrefix.StartsWith(node.FullPath + ".", System.StringComparison.OrdinalIgnoreCase);

            if (isMatch)
            {
                // This node matches - mark it and all children as active (green)
                node.IsHidden = false;
                node.IsActive = true;
                SetChildrenVisualState(node, false, true);
            }
            else if (isAncestor)
            {
                // This is a parent of the target - keep normal (not hidden, not active)
                node.IsHidden = false;
                node.IsActive = false;
                // Recurse into children to find the matching one
                if (node.Children != null)
                {
                    foreach (var child in node.Children)
                        MarkNodeShowOnly(child, activePrefix);
                }
            }
            else
            {
                // Not related - mark as hidden (greyed out with X)
                node.IsHidden = true;
                node.IsActive = false;
                SetChildrenVisualState(node, true, false);
            }
        }

        /// <summary>
        /// Reset all visual states (IsHidden + IsActive) on all tree nodes
        /// </summary>
        private void ResetAllVisualStates()
        {
            foreach (var rootNode in LoggerTreeRoot)
            {
                rootNode.IsHidden = false;
                rootNode.IsActive = false;
                SetChildrenVisualState(rootNode, false, false);
            }
        }

        /// <summary>
        /// Reset visual IsHidden state on all tree nodes (backward compat)
        /// </summary>
        private void ResetTreeVisualState()
        {
            ResetAllVisualStates();
        }

        /// <summary>
        /// Check if any column-based (non-tree) filters are active
        /// </summary>
        private bool HasAnyColumnFilter()
        {
            return _activeLoggerFilters.Any() || _activeThreadFilters.Any() || _activeMethodFilters.Any() ||
                   (_appFilterRoot != null && _appFilterRoot.Children.Count > 0) ||
                   _isAppTimeFocusActive;
        }

        private void OpenTimeRangeFilter(object obj)
        {
            // Get earliest and latest log times from all caches
            DateTime? earliestLog = null;
            DateTime? latestLog = null;

            var allLogs = _sessionVM?.AllLogsCache;
            var appLogs = _sessionVM?.AllAppLogsCache;

            if (allLogs != null && allLogs.Any())
            {
                earliestLog = allLogs.Min(l => l.Date);
                latestLog = allLogs.Max(l => l.Date);
            }

            if (appLogs != null && appLogs.Any())
            {
                var appEarliest = appLogs.Min(l => l.Date);
                var appLatest = appLogs.Max(l => l.Date);

                if (!earliestLog.HasValue || appEarliest < earliestLog.Value)
                    earliestLog = appEarliest;
                if (!latestLog.HasValue || appLatest > latestLog.Value)
                    latestLog = appLatest;
            }

            if (!earliestLog.HasValue || !latestLog.HasValue)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("No logs available to filter.", "No Logs", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Pass current filter values so the window shows the already-filtered range
                var window = new Views.TimeRangeWindow(earliestLog.Value, latestLog.Value, GlobalTimeRangeStart, GlobalTimeRangeEnd);

                // Position window near the button that was clicked
                if (obj is FrameworkElement buttonElement)
                {
                    window.Owner = Application.Current.MainWindow;
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.PositionNearElement(buttonElement);
                }

                if (window.ShowDialog() == true)
                {
                    if (window.ShouldClear)
                    {
                        GlobalTimeRangeStart = null;
                        GlobalTimeRangeEnd = null;
                        OnPropertyChanged(nameof(IsGlobalTimeRangeActive));
                        ApplyGlobalTimeRangeFilter();
                    }
                    else if (window.ResultStartDateTime.HasValue && window.ResultEndDateTime.HasValue)
                    {
                        GlobalTimeRangeStart = window.ResultStartDateTime.Value;
                        GlobalTimeRangeEnd = window.ResultEndDateTime.Value;
                        OnPropertyChanged(nameof(IsGlobalTimeRangeActive));
                        ApplyGlobalTimeRangeFilter();
                    }
                }
            });
        }



        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= OnSearchTimerTick;
                }
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase

    /// <summary>
    /// Applies all active filters (advanced, thread, search, negative, time range) to PLC/main logs and updates FilteredLogs.
    /// </summary>
    public void ApplyMainLogsFilter()
        {
            var filterSw = System.Diagnostics.Stopwatch.StartNew();
            if (_parent.LiveVM?.IsLiveMode == true) return;

            bool isActive = _isMainFilterActive;
            IEnumerable<LogEntry> currentLogs;
            bool hasSearchText = !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2;
            // Only apply thread filter if checkbox is checked (isActive) AND there are stored thread filters
            bool hasThreadFilter = isActive && _activeThreadFilters.Any();
            // Check if there's an advanced filter to apply
            bool hasAdvancedFilter = isActive && _mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0;

            // 1. קביעת מקור הנתונים
            if (isActive || hasSearchText)
            {
                // Always start from AllLogsCache, then apply filters
                // _lastFilteredCache is only used for TimeFocus mode
                if (isActive && _isTimeFocusActive && _lastFilteredCache != null)
                {
                    currentLogs = _lastFilteredCache;
                }
                else
                {
                    currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();
                }

                // Apply advanced filter from FilterWindow (only if checkbox is checked)
                if (hasAdvancedFilter)
                {
                    currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, _mainFilterRoot));
                }

                // Thread filter (only if checkbox is checked) - use HashSet for O(1) lookup
                if (hasThreadFilter)
                {
                    var threadSet = new HashSet<string>(_activeThreadFilters, StringComparer.OrdinalIgnoreCase);
                    currentLogs = currentLogs.Where(l => l.ThreadName != null && threadSet.Contains(l.ThreadName));
                }

                // סינון לפי טקסט חיפוש (always apply search, regardless of checkbox)
                if (hasSearchText)
                {
                    string searchText = SearchText;
                    if (QueryParserService.HasBooleanOperators(SearchText))
                    {
                        var parser = new QueryParserService();
                        var filterTree = parser.Parse(SearchText, out string errorMessage);
                        if (filterTree != null)
                            currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, filterTree));
                        else
                            currentLogs = currentLogs.Where(l => MatchesSearch(l, searchText));
                    }
                    else
                    {
                        currentLogs = currentLogs.Where(l => MatchesSearch(l, searchText));
                    }
                }
            }
            else
            {
                // כשאין פילטרים פעילים (checkbox unchecked), מציגים את הכל
                currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();
            }

            // 2. החלת סינון טווח זמן גלובלי (התיקון הקריטי)
            // מתבצע לפני הסינון השלילי ולפני העדכון למסך
            if (IsGlobalTimeRangeActive && !_isTimeFocusActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                currentLogs = currentLogs.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value);
            }

            // 3. סינון שלילי (Filter Out) - pre-split for faster iteration
            if (_isMainFilterOutActive && _negativeFilters.Count > 0)
            {
                var threadFiltersOut = new List<string>();
                var messageFiltersOut = new List<string>();
                foreach (var f in _negativeFilters)
                {
                    if (f.StartsWith("THREAD:"))
                        threadFiltersOut.Add(f.Substring(7));
                    else
                        messageFiltersOut.Add(f);
                }

                currentLogs = currentLogs.Where(l =>
                {
                    for (int i = 0; i < threadFiltersOut.Count; i++)
                        if (l.ThreadName != null && l.ThreadName.IndexOf(threadFiltersOut[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    for (int i = 0; i < messageFiltersOut.Count; i++)
                        if (l.Message != null && l.Message.IndexOf(messageFiltersOut[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    return true;
                });
            }

            // PLC Logger tree filter
            bool hasPlcTreeFilter = _plcTreeShowOnlyLogger != null || _plcTreeShowOnlyPrefix != null ||
                                    _plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0;
            if (hasPlcTreeFilter)
            {
                if (_plcTreeShowOnlyLogger != null)
                {
                    string showLogger = _plcTreeShowOnlyLogger;
                    currentLogs = currentLogs.Where(l => l.Logger != null &&
                        l.Logger.Equals(showLogger, StringComparison.OrdinalIgnoreCase));
                }
                else if (_plcTreeShowOnlyPrefix != null)
                {
                    string showPrefix = _plcTreeShowOnlyPrefix;
                    string showPrefixDot = showPrefix + "."; // Pre-allocate once
                    currentLogs = currentLogs.Where(l => l.Logger != null &&
                        (l.Logger.Equals(showPrefix, StringComparison.OrdinalIgnoreCase) ||
                         l.Logger.StartsWith(showPrefixDot, StringComparison.OrdinalIgnoreCase)));
                }
                else if (_plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0)
                {
                    var hiddenLoggers = new HashSet<string>(_plcTreeHiddenLoggers, StringComparer.OrdinalIgnoreCase);
                    // Pre-append "." to each prefix once instead of allocating per-log
                    var hiddenPrefixDots = _plcTreeHiddenPrefixes.Select(p => p + ".").ToArray();
                    var hiddenPrefixExact = _plcTreeHiddenPrefixes.ToArray();
                    currentLogs = currentLogs.Where(l =>
                    {
                        if (l.Logger == null) return true;
                        if (hiddenLoggers.Contains(l.Logger)) return false;
                        for (int i = 0; i < hiddenPrefixExact.Length; i++)
                        {
                            if (l.Logger.Equals(hiddenPrefixExact[i], StringComparison.OrdinalIgnoreCase) ||
                                l.Logger.StartsWith(hiddenPrefixDots[i], StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                        return true;
                    });
                }
            }

            // Parallel materialization of filter chain (mirrors ApplyAppLogsFilter's PLINQ approach)
            var logsList = currentLogs.AsParallel().AsOrdered().ToList();

            // עדכון ה-PLC Logs Tab
            if (_sessionVM != null)
                _sessionVM.Logs = logsList;

            // PLC Filtered Tab - לא מעדכנים כאן!
            // הטאב הזה צריך להישאר עם הנתונים המקוריים (Manager, Events, Error)
            // ולא להיות מושפע מהפילטר של PLC Logs.
            // הנתונים נטענים פעם אחת ב-SwitchToSession ונשארים קבועים.

            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            int srcCount = _sessionVM?.AllLogsCache?.Count ?? 0;
            AppLogger.Info($"[Filter] PLC filter applied: {srcCount:N0} → {logsList.Count:N0} entries — {filterSw.ElapsedMilliseconds}ms");
        }

        private void ApplyGlobalTimeRangeFilter()
        {
            // 1. טיפול באירועים (Events) - סינון או איפוס
            if (_sessionVM?.AllEvents != null)
            {
                List<EventEntry> eventsToShow;

                if (!IsGlobalTimeRangeActive)
                {
                    // מצב ניקוי: מציגים את כל האירועים (already sorted during load)
                    eventsToShow = _sessionVM.AllEvents.ToList();
                }
                else
                {
                    // מצב סינון: לוקחים רק את האירועים בטווח (already sorted, Where preserves order)
                    eventsToShow = _sessionVM.AllEvents
                        .Where(e => e.Time >= GlobalTimeRangeStart.Value && e.Time <= GlobalTimeRangeEnd.Value)
                        .ToList();
                }

                // עדכון הרשימה
                if (_sessionVM.Events is ObservableRangeCollection<EventEntry> rangeCol)
                {
                    rangeCol.ReplaceAll(eventsToShow);
                }
                else
                {
                    _sessionVM.Events.Clear();
                    foreach (var evt in eventsToShow) _sessionVM.Events.Add(evt);
                }
            }

            // 2. עדכון הלוגים (App + PLC)
            // קריאה לפונקציות הסינון תתחשב כעת ב-IsGlobalTimeRangeActive באופן אוטומטי
            ApplyAppLogsFilter();
            ApplyMainLogsFilter();

            // 3. עדכון סטטוס והודעה ל-UI
            if (!IsGlobalTimeRangeActive)
            {
                _sessionVM.StatusMessage = "Time range filter cleared";
            }
            else
            {
                var plcCount = (_sessionVM?.Logs?.Count()) ?? 0;
                var appCount = (AppDevLogsFiltered?.Count) ?? 0;
                var filteredCount = (FilteredLogs?.Count) ?? 0;
                var eventsCount = (_sessionVM?.Events?.Count) ?? 0;
                _sessionVM.StatusMessage = $"Time Range Filter: PLC={plcCount}, APP={appCount}, FILTERED={filteredCount}, Events={eventsCount}";
            }

            // Visual Timeline updates via SessionVM's own PropertyChanged / CollectionChanged
        }
    }
}