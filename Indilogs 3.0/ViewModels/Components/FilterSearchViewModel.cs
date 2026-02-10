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
    // Debug helper class to track who clears a list
    public class TrackedList<T> : List<T>, IList<T>
    {
        private readonly string _name;

        public TrackedList(string name) { _name = name; }

        public new void Clear()
        {
            System.Diagnostics.Debug.WriteLine($"[{_name}] CLEAR called! Count before: {Count}");
            System.Diagnostics.Debug.WriteLine($"[{_name}] Stack trace:");
            System.Diagnostics.Debug.WriteLine(new System.Diagnostics.StackTrace().ToString());
            base.Clear();
        }

        // Override ICollection<T>.Clear explicitly
        void ICollection<T>.Clear()
        {
            System.Diagnostics.Debug.WriteLine($"[{_name}] ICollection.CLEAR called! Count before: {Count}");
            System.Diagnostics.Debug.WriteLine($"[{_name}] Stack trace:");
            System.Diagnostics.Debug.WriteLine(new System.Diagnostics.StackTrace().ToString());
            base.Clear();
        }
    }

    public class FilterSearchViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;

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
                    _parent?.NotifyPropertyChanged(nameof(_parent.SearchText));
                    OnSearchTextChanged();
                }
            }
        }

        private bool _isSearchPanelVisible;
        public bool IsSearchPanelVisible
        {
            get
            {
                System.Diagnostics.Debug.WriteLine($"[SEARCH PROP GET] IsSearchPanelVisible = {_isSearchPanelVisible}");
                return _isSearchPanelVisible;
            }
            set
            {
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] Called with value: {value}");
                System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] Current value: {_isSearchPanelVisible}");

                if (_isSearchPanelVisible != value)
                {
                    _isSearchPanelVisible = value;
                    System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] Value changed! Calling OnPropertyChanged()");
                    OnPropertyChanged();

                    if (_parent != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] Notifying parent");
                        _parent.NotifyPropertyChanged(nameof(_parent.IsSearchPanelVisible));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] ⚠️ WARNING: _parent is NULL!");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SEARCH PROP SET] Value unchanged, skipping");
                }
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            }
        }

        // --- Collections ---
        private ObservableRangeCollection<LogEntry> _filteredLogs;
        public ObservableRangeCollection<LogEntry> FilteredLogs
        {
            get => _filteredLogs;
            set
            {
                _filteredLogs = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.FilteredLogs));
            }
        }

        private ObservableRangeCollection<LogEntry> _appDevLogsFiltered;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered
        {
            get => _appDevLogsFiltered;
            set
            {
                _appDevLogsFiltered = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.AppDevLogsFiltered));
            }
        }

        private ObservableCollection<LoggerNode> _loggerTreeRoot;
        public ObservableCollection<LoggerNode> LoggerTreeRoot
        {
            get => _loggerTreeRoot;
            set
            {
                _loggerTreeRoot = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.LoggerTreeRoot));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.SelectedTreeItem));
            }
        }

        // --- Filter Roots ---
        private FilterNode _mainFilterRoot;
        public FilterNode MainFilterRoot
        {
            get => _mainFilterRoot;
            set
            {
                System.Diagnostics.Debug.WriteLine($"[MainFilterRoot SET] Setting to {(value == null ? "NULL" : $"node with {value.Children?.Count ?? 0} children")}");
                System.Diagnostics.Debug.WriteLine($"[MainFilterRoot SET] Stack trace:\n{new System.Diagnostics.StackTrace()}");
                _mainFilterRoot = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.MainFilterRoot));
            }
        }

        private FilterNode _appFilterRoot;
        public FilterNode AppFilterRoot
        {
            get => _appFilterRoot;
            set { _appFilterRoot = value; OnPropertyChanged(); _parent?.NotifyPropertyChanged(nameof(_parent.AppFilterRoot)); }
        }

        private FilterNode _savedFilterRoot;
        public FilterNode SavedFilterRoot
        {
            get => _savedFilterRoot;
            set { _savedFilterRoot = value; OnPropertyChanged(); _parent?.NotifyPropertyChanged(nameof(_parent.SavedFilterRoot)); }
        }

        // --- Active Flags ---
        private bool _isMainFilterActive;
        public bool IsMainFilterActive
        {
            get => _isMainFilterActive;
            set
            {
                System.Diagnostics.Debug.WriteLine($"[IsMainFilterActive SET] value={value}, _activeThreadFilters.Count={_activeThreadFilters.Count}, _mainFilterRoot null={_mainFilterRoot == null}, Children={_mainFilterRoot?.Children?.Count ?? -1}");
                System.Diagnostics.Debug.WriteLine($"[IsMainFilterActive SET] Stack trace:\n{new System.Diagnostics.StackTrace()}");
                _isMainFilterActive = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsMainFilterActive));
                _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
            }
        }

        private bool _isAppFilterActive;
        public bool IsAppFilterActive
        {
            get => _isAppFilterActive;
            set
            {
                _isAppFilterActive = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsAppFilterActive));
                _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsMainFilterOutActive));
                _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsAppFilterOutActive));
                _parent?.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));
            }
        }

        private bool _isTimeFocusActive = false;
        public bool IsTimeFocusActive
        {
            get => _isTimeFocusActive;
            set { _isTimeFocusActive = value; OnPropertyChanged(); _parent?.NotifyPropertyChanged(nameof(_parent.IsTimeFocusActive)); }
        }

        private bool _isAppTimeFocusActive = false;
        public bool IsAppTimeFocusActive
        {
            get => _isAppTimeFocusActive;
            set { _isAppTimeFocusActive = value; OnPropertyChanged(); _parent?.NotifyPropertyChanged(nameof(_parent.IsAppTimeFocusActive)); }
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

        private TrackedList<string> _activeThreadFilters = new TrackedList<string>("ActiveThreadFilters");
        public TrackedList<string> ActiveThreadFilters => _activeThreadFilters;

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
                System.Diagnostics.Debug.WriteLine($"[HasMainStoredFilter] hasAdvanced={hasAdvanced}, hasThread={hasThread} (count={_activeThreadFilters.Count}), hasTimeFocus={hasTimeFocus} => {result}");
                return result;
            }
        }

        public bool HasAppStoredFilter =>
            (_appFilterRoot != null && _appFilterRoot.Children != null && _appFilterRoot.Children.Count > 0) ||
            _activeThreadFilters.Any() ||
            _activeLoggerFilters.Any() ||
            _activeMethodFilters.Any() ||
            _treeShowOnlyLogger != null ||
            _treeShowOnlyPrefix != null ||
            _treeHiddenLoggers.Count > 0 ||
            _treeHiddenPrefixes.Count > 0 ||
            _isAppTimeFocusActive;

        // HasStoredFilterOut - indicates whether there are negative filters stored
        public bool HasMainStoredFilterOut => _negativeFilters.Any();
        public bool HasAppStoredFilterOut => false; // App tab doesn't have filter out functionality

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

        // --- Tree Filter State ---
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

        public FilterSearchViewModel(MainViewModel parent, LogSessionViewModel sessionVM)
        {
            _parent = parent;
            _sessionVM = sessionVM;

            _filteredLogs = new ObservableRangeCollection<LogEntry>();
            _appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            _loggerTreeRoot = new ObservableCollection<LoggerNode>();

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            ToggleSearchCommand = new RelayCommand(o =>
            {
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[SEARCH CMD] ToggleSearchCommand CALLED");
                System.Diagnostics.Debug.WriteLine($"[SEARCH CMD] Current: {IsSearchPanelVisible}");

                // Force refresh by toggling if already true
                if (IsSearchPanelVisible)
                {
                    System.Diagnostics.Debug.WriteLine($"[SEARCH CMD] Already visible, forcing refresh");
                    IsSearchPanelVisible = false;
                }
                IsSearchPanelVisible = true;

                System.Diagnostics.Debug.WriteLine($"[SEARCH CMD] After: {IsSearchPanelVisible}");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            });
            CloseSearchCommand = new RelayCommand(o =>
            {
                System.Diagnostics.Debug.WriteLine("[SEARCH] CloseSearchCommand executed");

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
            OpenFilterWindowCommand = new RelayCommand(OpenFilterWindow);
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

        public void BuildLoggerTree(IEnumerable<LogEntry> logs)
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


        public void ApplyAppLogsFilter()
        {
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
            bool hasThreadFilter = isActive && _activeThreadFilters.Any();
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

            // 1. Thread Filter (only if checkbox checked)
            if (hasThreadFilter)
                query = query.Where(l => _activeThreadFilters.Contains(l.ThreadName));

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
                    query = query.Where(l => l.Logger != null &&
                        (l.Logger.Equals(showLogger, StringComparison.OrdinalIgnoreCase) ||
                         l.Logger.StartsWith(showLogger + ".", StringComparison.OrdinalIgnoreCase)));
                }
                else if (_treeShowOnlyPrefix != null)
                {
                    string showPrefix = _treeShowOnlyPrefix;
                    query = query.Where(l => l.Logger != null &&
                        (l.Logger.Equals(showPrefix, StringComparison.OrdinalIgnoreCase) ||
                         l.Logger.StartsWith(showPrefix + ".", StringComparison.OrdinalIgnoreCase)));
                }
                else if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                {
                    // Copy to local variables for thread safety with PLINQ
                    var hiddenLoggers = new HashSet<string>(_treeHiddenLoggers, StringComparer.OrdinalIgnoreCase);
                    var hiddenPrefixes = _treeHiddenPrefixes.ToList();
                    query = query.Where(l =>
                    {
                        if (l.Logger == null) return true;
                        if (hiddenLoggers.Contains(l.Logger)) return false;
                        foreach (var prefix in hiddenPrefixes)
                            if (l.Logger.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                                l.Logger.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
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
                        query = query.Where(l => l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else
                {
                    query = query.Where(l => l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }

            AppDevLogsFiltered.ReplaceAll(query.ToList());
        }

        public bool EvaluateFilterNode(LogEntry log, FilterNode node)
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
                    case "Pattern": val = log.Pattern; break;
                    case "Data": val = log.Data; break;
                    case "Exception": val = log.Exception; break;
                    default: val = log.Message; break;
                }

                if (string.IsNullOrEmpty(val)) return false;

                string op = node.Operator;
                string criteria = node.Value;

                if (op == "Equals") return val.Equals(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Begins With") return val.StartsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Ends With") return val.EndsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Regex")
                {
                    try { return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                    catch { return false; }
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

        public bool IsDefaultLog(LogEntry l)
        {
            if (string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Message != null && l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Events", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Logger != null && l.Logger.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void ClearFilters()
        {
            _mainFilterRoot = null;
            _appFilterRoot = null;
            _savedFilterRoot = null;
            IsMainFilterActive = false;
            IsAppFilterActive = false;
            IsMainFilterOutActive = false;
            IsTimeFocusActive = false;
            IsAppTimeFocusActive = false;
            SearchText = "";

            _negativeFilters.Clear();

            // Clear all column filters
            _activeThreadFilters.Clear();
            _activeLoggerFilters.Clear();
            _activeMethodFilters.Clear();

            _lastFilteredCache = null;
            _lastFilteredAppCache = null;

            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;

            // Reset visual state on all tree nodes
            ResetTreeVisualState();
        }

        public void ToggleFilterView(bool show)
        {
            ApplyMainLogsFilter();
            ApplyAppLogsFilter();
        }

        public void ResetTreeFilters()
        {
            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;
        }

        private async void OpenFilterWindow(object obj)
        {
            // Save the currently selected log and its scroll position BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            bool isAppTab = _parent.SelectedTabIndex == 2;

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
                                _activeThreadFilters.Clear();
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

                // Debug: Log filter conditions
                if (newRoot != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FILTER WINDOW] Apply clicked. Root has {newRoot.Children.Count} children");
                    foreach (var child in newRoot.Children)
                    {
                        if (child.Type == NodeType.Condition)
                            System.Diagnostics.Debug.WriteLine($"[FILTER WINDOW] Condition: Field={child.Field}, Operator={child.Operator}, Value={child.Value}");
                        else
                            System.Diagnostics.Debug.WriteLine($"[FILTER WINDOW] Group: {child.LogicalOperator} with {child.Children.Count} children");
                    }
                }

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

        private void FilterOut(object p)
        {
            if (_parent.SelectedLog == null) return;
            var w = new Views.FilterOutWindow(_parent.SelectedLog.Message);
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.TextToRemove))
            {
                _negativeFilters.Add(w.TextToRemove);
                IsMainFilterOutActive = true;
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
                if (!_negativeFilters.Contains(filterKey))
                {
                    _negativeFilters.Add(filterKey);
                    IsMainFilterOutActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void OpenThreadFilter(object obj)
        {
            // Check which tab is active and use appropriate cache
            bool isAppTab = _parent.SelectedTabIndex == 2;
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;

            System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] OpenThreadFilter called. isAppTab={isAppTab}");
            System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Cache count: {cache?.Count ?? 0}");

            if (cache == null || !cache.Any()) return;
            var threads = cache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

            System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Found {threads.Count} unique threads: {string.Join(", ", threads.Take(10))}");

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
                if (win.ShouldClear)
                {
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Clearing filters");
                    _activeThreadFilters.Clear();
                    // Also remove thread conditions from the filter tree
                    RemoveThreadConditionsFromFilterTree(isAppTab);
                    CheckIfFiltersEmpty(isAppTab);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Selected threads: {string.Join(", ", win.SelectedThreads)}");
                    _activeThreadFilters.Clear();
                    _activeThreadFilters.AddRange(win.SelectedThreads);
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Active thread filters now: {string.Join(", ", _activeThreadFilters)}");
                    // Sync thread filters to filter tree so they appear in Filter Window
                    SyncThreadFiltersToFilterTree(isAppTab, win.SelectedThreads);
                    SetFilterActive(isAppTab);
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] IsMainFilterActive={IsMainFilterActive}, IsAppFilterActive={IsAppFilterActive}");
                }
                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Calling ToggleFilterView(true)");
                ToggleFilterView(true); // Must re-trigger filter

                // Restore the selected log and scroll to it after CLEAR
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }

                // Debug: check results
                if (isAppTab)
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] After filter: AppDevLogsFiltered count={AppDevLogsFiltered?.Count ?? 0}");
                else
                    System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] After filter: Logs count={_sessionVM?.Logs?.Count() ?? 0}");
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
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] OpenLoggerFilter CALLED");

            bool isAppTab = _parent.SelectedTabIndex == 2;
            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] SelectedTabIndex={_parent.SelectedTabIndex}, isAppTab={isAppTab}");

            if (!isAppTab)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] ❌ NOT APP TAB - RETURNING");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
                return;
            }

            var cache = _sessionVM.AllAppLogsCache;
            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Cache count={cache?.Count ?? 0}");

            if (cache == null || !cache.Any())
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] ❌ CACHE IS NULL OR EMPTY - RETURNING");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
                return;
            }

            var loggers = cache.Select(l => l.Logger).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();
            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Found {loggers.Count} unique loggers");
            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Current active filters: {string.Join(", ", _activeLoggerFilters)}");

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

            System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Opening dialog window...");

            if (win.ShowDialog() == true)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Dialog result = TRUE");
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] ShouldClear={win.ShouldClear}");
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] SelectedThreads count={win.SelectedThreads?.Count ?? 0}");

                if (win.ShouldClear)
                {
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Clearing logger filters");
                    _activeLoggerFilters.Clear();
                    CheckIfFiltersEmpty(true);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Setting logger filters: {string.Join(", ", win.SelectedThreads)}");
                    _activeLoggerFilters.Clear();
                    _activeLoggerFilters.AddRange(win.SelectedThreads);
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Active logger filters now: {string.Join(", ", _activeLoggerFilters)}");
                    SetFilterActive(true);
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] IsAppFilterActive={IsAppFilterActive}");
                }

                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Calling ToggleFilterView(true)...");
                ToggleFilterView(true);
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] After filter: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");

                // Restore the selected log and scroll to it (only on clear)
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER FILTER] Dialog result = FALSE (cancelled)");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void OpenMethodFilter(object obj)
        {
            bool isAppTab = _parent.SelectedTabIndex == 2;
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
            System.Diagnostics.Debug.WriteLine($"[SetFilterActive] isAppTab={isAppTab}, Setting filter to ACTIVE");
            System.Diagnostics.Debug.WriteLine($"[SetFilterActive] BEFORE: _activeThreadFilters.Count={_activeThreadFilters.Count}, _mainFilterRoot={_mainFilterRoot != null}");
            if (isAppTab) IsAppFilterActive = true;
            else IsMainFilterActive = true;
            System.Diagnostics.Debug.WriteLine($"[SetFilterActive] AFTER: IsMainFilterActive={IsMainFilterActive}, IsAppFilterActive={IsAppFilterActive}");
        }

        private void CheckIfFiltersEmpty(bool isAppTab)
        {
            if (isAppTab)
            {
                // Check if app filter root is empty (null or has no children)
                bool appFilterRootEmpty = _appFilterRoot == null || _appFilterRoot.Children == null || _appFilterRoot.Children.Count == 0;
                bool noTreeFilters = _treeShowOnlyLogger == null && _treeShowOnlyPrefix == null && _treeHiddenLoggers.Count == 0 && _treeHiddenPrefixes.Count == 0;

                if (!_activeThreadFilters.Any() && !_activeLoggerFilters.Any() && !_activeMethodFilters.Any() && appFilterRootEmpty && noTreeFilters)
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
            bool isAppTab = _parent.SelectedTabIndex == 2;

            Task.Run(() =>
            {
                if (isAppTab)
                {
                    if (_sessionVM.AllAppLogsCache != null)
                    {
                        var contextLogs = _sessionVM.AllAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
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
                        var contextLogs = _sessionVM.AllLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
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

        private void UndoFilterOut(object obj)
        {
            if (_negativeFilters.Any())
            {
                _negativeFilters.RemoveAt(_negativeFilters.Count - 1);
                if (!_negativeFilters.Any())
                {
                    IsMainFilterOutActive = false;
                }
                ToggleFilterView(IsMainFilterActive || IsMainFilterOutActive);
            }
        }

        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                // Clear any "show only" filters
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;

                // Remove this exact logger from hidden sets
                _treeHiddenLoggers.Remove(node.FullPath);

                // Also remove from prefix-hidden if this node was hidden via "Hide With Children"
                _treeHiddenPrefixes.Remove(node.FullPath);

                // Remove any parent prefix that covers this node
                var prefixesToRemove = _treeHiddenPrefixes
                    .Where(p => node.FullPath == p || node.FullPath.StartsWith(p + "."))
                    .ToList();
                foreach (var p in prefixesToRemove)
                    _treeHiddenPrefixes.Remove(p);

                // Update visual state
                node.IsHidden = false;
                node.IsActive = false;
                SetChildrenVisualState(node, false, false);

                // If no more filters active, turn off filter
                bool hasAnyTreeFilter = _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0;
                IsAppFilterActive = hasAnyTreeFilter || HasAnyColumnFilter();
                if (!IsAppFilterActive)
                    ResetAllVisualStates(); // clear all icons when no filters
                ToggleFilterView(IsAppFilterActive);
            }
        }

        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                // Clear any "show only" filters and reset active states
                if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null)
                {
                    ResetAllVisualStates();
                }
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;

                // Hide this node and all its children using prefix match
                if (node.Children != null && node.Children.Count > 0)
                {
                    // Parent node: use prefix-based hiding (hides node and all children)
                    _treeHiddenPrefixes.Add(node.FullPath);
                }
                else
                {
                    // Leaf node: use exact match hiding
                    _treeHiddenLoggers.Add(node.FullPath);
                }

                // Update visual state - mark hidden with X
                node.IsHidden = true;
                node.IsActive = false;
                SetChildrenVisualState(node, true, false);

                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();

                // "Show Only This" uses prefix matching so it includes children
                _treeShowOnlyPrefix = node.FullPath;

                // Visual: mark ALL loggers as hidden, then mark only the selected one (+ children) as active
                MarkAllNodesShowOnly(node.FullPath);

                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();

                _treeShowOnlyPrefix = node.FullPath;

                // Visual: mark ALL loggers as hidden, then mark only the selected one (+ children) as active
                MarkAllNodesShowOnly(node.FullPath);

                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                // Clear any "show only" filters and reset active states
                if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null)
                {
                    ResetAllVisualStates();
                }
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenPrefixes.Add(node.FullPath);

                // Update visual state for node and all children - mark with X
                node.IsHidden = true;
                node.IsActive = false;
                SetChildrenVisualState(node, true, false);

                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowAll(object obj)
        {
            ResetTreeFilters();
            ResetAllVisualStates();

            // Check if any non-tree filters are active
            IsAppFilterActive = HasAnyColumnFilter();
            ToggleFilterView(IsAppFilterActive);
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
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TIME RANGE] OpenTimeRangeFilter CALLED");

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
                System.Diagnostics.Debug.WriteLine($"[TIME RANGE] No logs found");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("No logs available to filter.", "No Logs", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            var duration = latestLog.Value - earliestLog.Value;
            System.Diagnostics.Debug.WriteLine($"[TIME RANGE] Log range: {earliestLog:yyyy-MM-dd HH:mm:ss} to {latestLog:yyyy-MM-dd HH:mm:ss}");
            System.Diagnostics.Debug.WriteLine($"[TIME RANGE] Duration: {duration.TotalMinutes:F2} minutes");

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
                        System.Diagnostics.Debug.WriteLine($"[TIME RANGE] Clearing time range filter");
                        GlobalTimeRangeStart = null;
                        GlobalTimeRangeEnd = null;
                        OnPropertyChanged(nameof(IsGlobalTimeRangeActive));
                        ApplyGlobalTimeRangeFilter();
                    }
                    else if (window.ResultStartDateTime.HasValue && window.ResultEndDateTime.HasValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TIME RANGE] Setting filter: {window.ResultStartDateTime:yyyy-MM-dd HH:mm:ss} to {window.ResultEndDateTime:yyyy-MM-dd HH:mm:ss}");
                        GlobalTimeRangeStart = window.ResultStartDateTime.Value;
                        GlobalTimeRangeEnd = window.ResultEndDateTime.Value;
                        OnPropertyChanged(nameof(IsGlobalTimeRangeActive));
                        ApplyGlobalTimeRangeFilter();
                    }
                }
            });

            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }



        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public void ApplyMainLogsFilter()
        {
            if (_parent.IsLiveMode) return;

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
                    if (QueryParserService.HasBooleanOperators(SearchText))
                    {
                        var parser = new QueryParserService();
                        var filterTree = parser.Parse(SearchText, out string errorMessage);
                        if (filterTree != null)
                            currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, filterTree));
                        else
                            currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    else
                    {
                        currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
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

            var logsList = currentLogs.ToList();

            // עדכון ה-PLC Logs Tab
            if (_sessionVM != null)
                _sessionVM.Logs = logsList;

            // PLC Filtered Tab - לא מעדכנים כאן!
            // הטאב הזה צריך להישאר עם הנתונים המקוריים (Manager, Events, Error)
            // ולא להיות מושפע מהפילטר של PLC Logs.
            // הנתונים נטענים פעם אחת ב-SwitchToSession ונשארים קבועים.
        }

        private void ApplyGlobalTimeRangeFilter()
        {
            System.Diagnostics.Debug.WriteLine($"[TIME RANGE FILTER] Applying global time range filter. Active={IsGlobalTimeRangeActive}");

            // 1. טיפול באירועים (Events) - סינון או איפוס
            if (_sessionVM?.AllEvents != null)
            {
                List<EventEntry> eventsToShow;

                if (!IsGlobalTimeRangeActive)
                {
                    // מצב ניקוי: מציגים את כל האירועים ממוינים
                    eventsToShow = _sessionVM.AllEvents.OrderBy(e => e.Time).ToList();
                }
                else
                {
                    // מצב סינון: לוקחים רק את האירועים בטווח
                    eventsToShow = _sessionVM.AllEvents
                        .Where(e => e.Time >= GlobalTimeRangeStart.Value && e.Time <= GlobalTimeRangeEnd.Value)
                        .OrderBy(e => e.Time)
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

            // 4. עדכון הוויזואליזציה (Visual Timeline)
            if (_parent != null)
            {
                // שימוש ב-NotifyPropertyChanged כדי לוודא שה-MainViewModel מתעדכן
                _parent.NotifyPropertyChanged(nameof(_parent.Logs));
                _parent.NotifyPropertyChanged(nameof(_parent.Events));
            }
        }
    }
}