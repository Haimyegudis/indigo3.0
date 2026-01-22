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
            set { _mainFilterRoot = value; OnPropertyChanged(); _parent?.NotifyPropertyChanged(nameof(_parent.MainFilterRoot)); }
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

        private List<string> _activeThreadFilters = new List<string>();
        public List<string> ActiveThreadFilters => _activeThreadFilters;

        // New Lists for independent column filtering
        private List<string> _activeLoggerFilters = new List<string>();
        public List<string> ActiveLoggerFilters => _activeLoggerFilters;

        private List<string> _activeMethodFilters = new List<string>();
        public List<string> ActiveMethodFilters => _activeMethodFilters;

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
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
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
                IsSearchPanelVisible = false;
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
            ApplyMainLogsFilter();
            ApplyAppLogsFilter();
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

            // קביעת מקור הנתונים (Cache רגיל או Cache של Focus Context)
            var source = _sessionVM.AllAppLogsCache;
            if (_isAppFilterActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }

            // --- TIKUN: החלת סינון טווח זמן גלובלי גם על ה-APP ---
            if (IsGlobalTimeRangeActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                source = source.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value).ToList();
            }
            // -----------------------------------------------------

            // בדיקה האם יש פילטרים פעילים בתוך הטאב (עמודות, חיפוש וכו')
            bool hasThreadFilter = _activeThreadFilters.Any();
            bool hasLoggerFilter = _activeLoggerFilters.Any();
            bool hasMethodFilter = _activeMethodFilters.Any();
            bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            bool hasTreeFilter = _treeShowOnlyLogger != null || _treeShowOnlyPrefix != null || _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0;
            bool hasAdvancedFilter = _appFilterRoot != null && _appFilterRoot.Children.Count > 0;

            // אם אין שום פילטר נוסף, מציגים את המקור (שכבר עבר סינון זמן אם היה צריך)
            if (!_isAppFilterActive && !hasSearch && !hasThreadFilter && !hasLoggerFilter && !hasMethodFilter && !hasTreeFilter)
            {
                AppDevLogsFiltered.ReplaceAll(source);
                return;
            }

            var query = source.AsParallel().AsOrdered();

            // 1. Thread Filter
            if (hasThreadFilter)
                query = query.Where(l => _activeThreadFilters.Contains(l.ThreadName));

            // 2. Logger Filter
            if (hasLoggerFilter)
            {
                query = query.Where(l => _activeLoggerFilters.Any(filter => string.Equals(l.Logger, filter, StringComparison.OrdinalIgnoreCase)));
            }

            // 3. Method Filter
            if (hasMethodFilter)
                query = query.Where(l => _activeMethodFilters.Contains(l.Method));

            // 4. Advanced Filter
            if (hasAdvancedFilter)
                query = query.Where(l => EvaluateFilterNode(l, _appFilterRoot));

            // 5. Tree Filter
            if (_isAppFilterActive)
            {
                if (_treeShowOnlyLogger != null)
                {
                    query = query.Where(l => l.Logger == _treeShowOnlyLogger);
                }
                else if (_treeShowOnlyPrefix != null)
                {
                    query = query.Where(l => l.Logger != null && (l.Logger == _treeShowOnlyPrefix || l.Logger.StartsWith(_treeShowOnlyPrefix + ".")));
                }
                else if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                {
                    query = query.Where(l =>
                    {
                        if (l.Logger == null) return true;
                        if (_treeHiddenLoggers.Contains(l.Logger)) return false;
                        foreach (var prefix in _treeHiddenPrefixes)
                            if (l.Logger == prefix || l.Logger.StartsWith(prefix + ".")) return false;
                        return true;
                    });
                }
            }

            // 6. Search
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
            var win = new Views.FilterWindow();
            bool isAppTab = _parent.SelectedTabIndex == 2;
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;

            if (currentRoot != null)
            {
                win.ViewModel.RootNodes.Clear();
                win.ViewModel.RootNodes.Add(currentRoot.DeepClone());
            }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                _sessionVM.IsBusy = true;

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
                        IsMainFilterActive = hasAdvanced || ActiveThreadFilters.Any();
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

            if (cache == null || !cache.Any()) return;
            var threads = cache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

            var win = new Views.ThreadFilterWindow(threads) { Title = "Filter by Thread" };

            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeThreadFilters.Clear();
                    CheckIfFiltersEmpty(isAppTab);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeThreadFilters.Clear();
                    _activeThreadFilters.AddRange(win.SelectedThreads);
                    SetFilterActive(isAppTab);
                }
                ToggleFilterView(true); // Must re-trigger filter
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

            var win = new Views.ThreadFilterWindow(loggers) { Title = "Filter by Logger" };
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

            var win = new Views.ThreadFilterWindow(methods) { Title = "Filter by Method" };

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
                if (!_activeThreadFilters.Any() && !_activeLoggerFilters.Any() && !_activeMethodFilters.Any() && _appFilterRoot == null)
                    IsAppFilterActive = false;
            }
            else
            {
                if (!_activeThreadFilters.Any() && _mainFilterRoot == null)
                    IsMainFilterActive = false;
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
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeShowThis CALLED");
            System.Diagnostics.Debug.WriteLine($"[TREE] Parameter: {obj?.GetType().Name ?? "NULL"}");

            if (obj is LoggerNode node)
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] Logger: {node.FullPath}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Before: IsHidden={node.IsHidden}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Before: HiddenLoggers count={_treeHiddenLoggers.Count}");

                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                bool wasRemoved = _treeHiddenLoggers.Remove(node.FullPath);
                node.IsHidden = false;

                System.Diagnostics.Debug.WriteLine($"[TREE] Removed from hidden: {wasRemoved}");
                System.Diagnostics.Debug.WriteLine($"[TREE] After: IsHidden={node.IsHidden}");
                System.Diagnostics.Debug.WriteLine($"[TREE] After: HiddenLoggers count={_treeHiddenLoggers.Count}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Setting IsAppFilterActive=true");

                IsAppFilterActive = true;

                System.Diagnostics.Debug.WriteLine($"[TREE] Calling ToggleFilterView(true)");
                ToggleFilterView(true);

                System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] ❌ NOT LoggerNode!");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void ExecuteTreeHideThis(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeHideThis CALLED");
            System.Diagnostics.Debug.WriteLine($"[TREE] Parameter: {obj?.GetType().Name ?? "NULL"}");

            if (obj is LoggerNode node)
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] Logger: {node.FullPath}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Before: IsHidden={node.IsHidden}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Before: HiddenLoggers count={_treeHiddenLoggers.Count}");

                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenLoggers.Add(node.FullPath);
                node.IsHidden = true;

                System.Diagnostics.Debug.WriteLine($"[TREE] After: IsHidden={node.IsHidden}");
                System.Diagnostics.Debug.WriteLine($"[TREE] After: HiddenLoggers count={_treeHiddenLoggers.Count}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Hidden loggers: {string.Join(", ", _treeHiddenLoggers)}");
                System.Diagnostics.Debug.WriteLine($"[TREE] Setting IsAppFilterActive=true");

                IsAppFilterActive = true;

                System.Diagnostics.Debug.WriteLine($"[TREE] Calling ToggleFilterView(true)");
                ToggleFilterView(true);

                System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] ❌ NOT LoggerNode!");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void ExecuteTreeShowOnlyThis(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeShowOnlyThis CALLED");

            if (obj is LoggerNode node)
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] Logger: {node.FullPath}");
                ResetTreeFilters();
                _treeShowOnlyLogger = node.FullPath;
                System.Diagnostics.Debug.WriteLine($"[TREE] ShowOnlyLogger set to: {_treeShowOnlyLogger}");
                IsAppFilterActive = true;
                ToggleFilterView(true);
                System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void ExecuteTreeShowWithChildren(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeShowWithChildren CALLED");

            if (obj is LoggerNode node)
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] Logger: {node.FullPath}");
                ResetTreeFilters();
                _treeShowOnlyPrefix = node.FullPath;
                System.Diagnostics.Debug.WriteLine($"[TREE] ShowOnlyPrefix set to: {_treeShowOnlyPrefix}");
                IsAppFilterActive = true;
                ToggleFilterView(true);
                System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void ExecuteTreeHideWithChildren(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeHideWithChildren CALLED");

            if (obj is LoggerNode node)
            {
                System.Diagnostics.Debug.WriteLine($"[TREE] Logger: {node.FullPath}");
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenPrefixes.Add(node.FullPath);
                node.IsHidden = true;
                System.Diagnostics.Debug.WriteLine($"[TREE] Added to HiddenPrefixes: {node.FullPath}");
                System.Diagnostics.Debug.WriteLine($"[TREE] HiddenPrefixes count: {_treeHiddenPrefixes.Count}");
                IsAppFilterActive = true;
                ToggleFilterView(true);
                System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            }
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
        }

        private void ExecuteTreeShowAll(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[TREE] ExecuteTreeShowAll CALLED");
            ResetTreeFilters();
            IsAppFilterActive = false;
            ToggleFilterView(false);
            System.Diagnostics.Debug.WriteLine($"[TREE] Result: AppDevLogsFiltered count={_appDevLogsFiltered?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════");
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
                var window = new Views.TimeRangeWindow(earliestLog.Value, latestLog.Value);
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

            // 1. קביעת מקור הנתונים
            if (isActive || hasSearchText)
            {
                if ((_mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0) || _isTimeFocusActive)
                    currentLogs = _lastFilteredCache ?? new List<LogEntry>();
                else
                    currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();

                // סינון לפי Threads (קיים בטאב PLC)
                if (_activeThreadFilters.Any())
                    currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));

                // סינון לפי טקסט חיפוש
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
                // כשאין פילטרים פעילים, מציגים את הכל
                currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();
            }

            // 2. החלת סינון טווח זמן גלובלי (התיקון הקריטי)
            // מתבצע לפני הסינון השלילי ולפני העדכון למסך
            if (IsGlobalTimeRangeActive && !_isTimeFocusActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                currentLogs = currentLogs.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value);
            }

            // 3. סינון שלילי (Filter Out)
            if (_isMainFilterOutActive && _negativeFilters.Any())
            {
                currentLogs = currentLogs.Where(l =>
                {
                    foreach (var f in _negativeFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            if (l.ThreadName != null && l.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                        else
                        {
                            if (l.Message != null && l.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                    }
                    return true;
                });
            }

            var logsList = currentLogs.ToList();

            // עדכון ה-PLC Logs Tab
            if (_sessionVM != null)
                _sessionVM.Logs = logsList;

            // עדכון ה-PLC Filtered Tab לפי החוקים
            var filteredByRules = logsList
                .Where(l =>
                    string.Equals(l.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.ThreadName, "Events", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

            FilteredLogs.ReplaceAll(filteredByRules);
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