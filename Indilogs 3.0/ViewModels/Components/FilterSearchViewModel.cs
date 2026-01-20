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
    /// Manages filtering, searching, and logger tree operations
    /// </summary>
    public class FilterSearchViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;

        // Search functionality
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
            get => _isSearchPanelVisible;
            set
            {
                _isSearchPanelVisible = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsSearchPanelVisible));
            }
        }

        // Filtered collections (results after filtering)
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

        // Logger Tree
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

        // Filter state
        private FilterNode _mainFilterRoot;
        public FilterNode MainFilterRoot
        {
            get => _mainFilterRoot;
            set
            {
                _mainFilterRoot = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.MainFilterRoot));
            }
        }

        private FilterNode _appFilterRoot;
        public FilterNode AppFilterRoot
        {
            get => _appFilterRoot;
            set
            {
                _appFilterRoot = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.AppFilterRoot));
            }
        }

        private FilterNode _savedFilterRoot;
        public FilterNode SavedFilterRoot
        {
            get => _savedFilterRoot;
            set
            {
                _savedFilterRoot = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.SavedFilterRoot));
            }
        }

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
            set
            {
                _isTimeFocusActive = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsTimeFocusActive));
            }
        }

        private bool _isAppTimeFocusActive = false;
        public bool IsAppTimeFocusActive
        {
            get => _isAppTimeFocusActive;
            set
            {
                _isAppTimeFocusActive = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsAppTimeFocusActive));
            }
        }

        // Negative filters (filter out)
        private List<string> _negativeFilters = new List<string>();
        public List<string> NegativeFilters => _negativeFilters;

        private List<string> _activeThreadFilters = new List<string>();
        public List<string> ActiveThreadFilters => _activeThreadFilters;

        // Cached filtered results
        private List<LogEntry> _lastFilteredCache = null;
        public List<LogEntry> LastFilteredCache
        {
            get => _lastFilteredCache;
            set
            {
                _lastFilteredCache = value;
                OnPropertyChanged();
            }
        }

        private List<LogEntry> _lastFilteredAppCache = null;
        public List<LogEntry> LastFilteredAppCache
        {
            get => _lastFilteredAppCache;
            set
            {
                _lastFilteredAppCache = value;
                OnPropertyChanged();
            }
        }

        // Tree filter state
        private HashSet<string> _treeHiddenLoggers = new HashSet<string>();
        public HashSet<string> TreeHiddenLoggers => _treeHiddenLoggers;

        private HashSet<string> _treeHiddenPrefixes = new HashSet<string>();
        public HashSet<string> TreeHiddenPrefixes => _treeHiddenPrefixes;

        private string _treeShowOnlyLogger = null;
        public string TreeShowOnlyLogger
        {
            get => _treeShowOnlyLogger;
            set
            {
                _treeShowOnlyLogger = value;
                OnPropertyChanged();
            }
        }

        private string _treeShowOnlyPrefix = null;
        public string TreeShowOnlyPrefix
        {
            get => _treeShowOnlyPrefix;
            set
            {
                _treeShowOnlyPrefix = value;
                OnPropertyChanged();
            }
        }

        // Search debounce timer
        private DispatcherTimer _searchDebounceTimer;

        // Commands
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

        public FilterSearchViewModel(MainViewModel parent, LogSessionViewModel sessionVM)
        {
            _parent = parent;
            _sessionVM = sessionVM;

            // Initialize collections
            _filteredLogs = new ObservableRangeCollection<LogEntry>();
            _appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            _loggerTreeRoot = new ObservableCollection<LoggerNode>();

            // Initialize search debounce timer
            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            // Initialize commands (placeholders for now)
            ToggleSearchCommand = new RelayCommand(o => IsSearchPanelVisible = !IsSearchPanelVisible);
            CloseSearchCommand = new RelayCommand(o => IsSearchPanelVisible = false);
            OpenFilterWindowCommand = new RelayCommand(OpenFilterWindow);
            FilterOutCommand = new RelayCommand(FilterOut);
            FilterOutThreadCommand = new RelayCommand(FilterOutThread);
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
        }

        private void OnSearchTextChanged()
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            // Apply search filter - to be implemented
            ApplyMainLogsFilter();
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

        public void ApplyMainLogsFilter()
        {
            // Check if in Live Mode - don't replace the live collection
            if (_parent.IsLiveMode)
            {
                // In Live mode, filtering happens only in FilteredLogs tab
                // Main logs stay connected to live collection
                return;
            }

            bool isActive = _isMainFilterActive;
            IEnumerable<LogEntry> currentLogs;
            bool hasSearchText = !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2;

            if (isActive || hasSearchText)
            {
                if ((_mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0) || _isTimeFocusActive)
                    currentLogs = _lastFilteredCache ?? new List<LogEntry>();
                else
                    currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();

                if (_activeThreadFilters.Any())
                    currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));

                if (hasSearchText)
                {
                    // Smart Boolean Search: Check if query has special operators
                    if (QueryParserService.HasBooleanOperators(SearchText))
                    {
                        var parser = new QueryParserService();
                        var filterTree = parser.Parse(SearchText, out string errorMessage);

                        if (filterTree != null)
                        {
                            // Use smart filtering with parsed tree
                            currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, filterTree));
                        }
                        else
                        {
                            // Parsing failed - fall back to simple search
                            currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                    }
                    else
                    {
                        // Simple search for maximum performance
                        currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
            }
            else
            {
                currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();
            }

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

            // Update the Logs in SessionVM
            if (_sessionVM != null)
            {
                _sessionVM.Logs = currentLogs.ToList();
            }
        }

        public void ApplyAppLogsFilter()
        {
            if (_sessionVM?.AllAppLogsCache == null) return;

            if (!_isAppFilterActive && string.IsNullOrWhiteSpace(SearchText) && !_activeThreadFilters.Any())
            {
                AppDevLogsFiltered.ReplaceAll(_sessionVM.AllAppLogsCache);
                return;
            }

            var source = _sessionVM.AllAppLogsCache;
            if (_isAppFilterActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }

            var query = source.AsParallel().AsOrdered();

            // Apply thread/logger/method filters (from column header clicks)
            if (_isAppFilterActive && _activeThreadFilters.Any())
            {
                query = query.Where(l =>
                    _activeThreadFilters.Contains(l.ThreadName) ||
                    _activeThreadFilters.Contains(l.Logger) ||
                    _activeThreadFilters.Contains(l.Method));
            }

            if (_isAppFilterActive && !_isAppTimeFocusActive && _appFilterRoot != null && _appFilterRoot.Children.Count > 0)
            {
                query = query.Where(l => EvaluateFilterNode(l, _appFilterRoot));
            }

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
                else
                {
                    if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                    {
                        query = query.Where(l =>
                        {
                            if (l.Logger == null) return true;
                            if (_treeHiddenLoggers.Contains(l.Logger)) return false;
                            foreach (var prefix in _treeHiddenPrefixes)
                            {
                                if (l.Logger == prefix || l.Logger.StartsWith(prefix + ".")) return false;
                            }
                            return true;
                        });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText;

                // Smart Boolean Search for App Logs
                if (QueryParserService.HasBooleanOperators(SearchText))
                {
                    var parser = new QueryParserService();
                    var filterTree = parser.Parse(SearchText, out string errorMessage);

                    if (filterTree != null)
                    {
                        // Use smart filtering with parsed tree
                        query = query.Where(l => EvaluateFilterNode(l, filterTree));
                    }
                    else
                    {
                        // Fall back to simple search
                        query = query.Where(l => l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
                else
                {
                    // Simple search
                    query = query.Where(l => l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }

            var result = query.ToList();
            AppDevLogsFiltered.ReplaceAll(result);
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
                    try
                    {
                        return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
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
                        if (EvaluateFilterNode(log, child))
                        {
                            baseResult = true;
                            break;
                        }
                    }
                }
                else
                {
                    baseResult = true;
                    foreach (var child in node.Children)
                    {
                        if (!EvaluateFilterNode(log, child))
                        {
                            baseResult = false;
                            break;
                        }
                    }
                }

                if (op.StartsWith("NOT")) return !baseResult;
                return baseResult;
            }
        }

        /// <summary>
        /// Default PLC filter - determines which logs show by default in PLC FILTERED tab
        /// </summary>
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
            _activeThreadFilters.Clear();
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

        // Command implementations
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
                        else
                        {
                            LastFilteredCache = null;
                        }
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
            var win = new Views.ThreadFilterWindow(threads);
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeThreadFilters.Clear();
                    if (_savedFilterRoot == null)
                    {
                        if (isAppTab) IsAppFilterActive = false;
                        else IsMainFilterActive = false;
                    }
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeThreadFilters.Clear();
                    foreach (var thread in win.SelectedThreads)
                        _activeThreadFilters.Add(thread);
                    if (isAppTab) IsAppFilterActive = true;
                    else IsMainFilterActive = true;
                }
                ToggleFilterView(isAppTab ? IsAppFilterActive : IsMainFilterActive);
            }
        }

        private void OpenLoggerFilter(object obj)
        {
            // Check which tab is active and use appropriate cache
            bool isAppTab = _parent.SelectedTabIndex == 2;
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;

            if (cache == null || !cache.Any()) return;
            var loggers = cache.Select(l => l.Logger).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();
            var win = new Views.ThreadFilterWindow(loggers) { Title = "Filter by Logger" };
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeThreadFilters.Clear();
                    if (_savedFilterRoot == null)
                    {
                        if (isAppTab) IsAppFilterActive = false;
                        else IsMainFilterActive = false;
                    }
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeThreadFilters.Clear();
                    foreach (var logger in win.SelectedThreads)
                        _activeThreadFilters.Add(logger);
                    if (isAppTab) IsAppFilterActive = true;
                    else IsMainFilterActive = true;
                }
                ToggleFilterView(isAppTab ? IsAppFilterActive : IsMainFilterActive);
            }
        }

        private void OpenMethodFilter(object obj)
        {
            // Check which tab is active and use appropriate cache
            bool isAppTab = _parent.SelectedTabIndex == 2;
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;

            if (cache == null || !cache.Any()) return;
            var methods = cache.Select(l => l.Method).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m).ToList();
            var win = new Views.ThreadFilterWindow(methods) { Title = "Filter by Method" };
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeThreadFilters.Clear();
                    if (_savedFilterRoot == null) IsMainFilterActive = false;
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeThreadFilters.Clear();
                    foreach (var method in win.SelectedThreads)
                        _activeThreadFilters.Add(method);
                    IsMainFilterActive = true;
                }
                ToggleFilterView(IsMainFilterActive);
            }
        }

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
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenLoggers.Remove(node.FullPath);
                node.IsHidden = false;
                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenLoggers.Add(node.FullPath);
                node.IsHidden = true;
                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                _treeShowOnlyLogger = node.FullPath;
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
                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;
                _treeHiddenPrefixes.Add(node.FullPath);
                node.IsHidden = true;
                IsAppFilterActive = true;
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowAll(object obj)
        {
            ResetTreeFilters();
            IsAppFilterActive = false;
            ToggleFilterView(false);
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
