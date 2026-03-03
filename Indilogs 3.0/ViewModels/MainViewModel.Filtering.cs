#nullable disable
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // ── Filter state management ──

        private bool _isSearchSyntaxValid = true;
        public bool IsSearchSyntaxValid
        {
            get => _isSearchSyntaxValid;
            set
            {
                if (_isSearchSyntaxValid != value)
                {
                    _isSearchSyntaxValid = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _searchSyntaxError;
        public string SearchSyntaxError
        {
            get => _searchSyntaxError;
            set
            {
                if (_searchSyntaxError != value)
                {
                    _searchSyntaxError = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ValidateSearchSyntax()
        {
            if (string.IsNullOrWhiteSpace(FilterVM?.SearchText))
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
                return;
            }

            if (QueryParserService.HasBooleanOperators(FilterVM.SearchText))
            {
                var parser = new QueryParserService();
                var result = parser.Parse(FilterVM.SearchText, out string errorMessage);

                if (result == null)
                {
                    IsSearchSyntaxValid = false;
                    SearchSyntaxError = errorMessage;
                }
                else
                {
                    IsSearchSyntaxValid = true;
                    SearchSyntaxError = null;
                }
            }
            else
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
            }
        }

        public bool IsFilterActive
        {
            get
            {
                if (SelectedTabIndex == AppConstants.TAB_DIFFERENT_LOGS) return DifferentLogsVM?.IsFilterActive ?? false;
                return SelectedTabIndex == AppConstants.TAB_APP ? (FilterVM?.IsAppFilterActive ?? false) : (FilterVM?.IsMainFilterActive ?? false);
            }
            set
            {
                // Different Logs tab: toggle its own filter
                if (SelectedTabIndex == AppConstants.TAB_DIFFERENT_LOGS)
                {
                    var diffVM = DifferentLogsVM;
                    if (diffVM != null && diffVM.IsFilterActive != value)
                    {
                        if (value && diffVM.FilterRoot == null) return; // No stored filter
                        diffVM.IsFilterActive = value;
                        OnPropertyChanged();

                        if (value && diffVM.FilterRoot != null)
                        {
                            var filtered = diffVM.AllLogEntries
                                .Where(l => FilterVM.EvaluateFilterNode(l, diffVM.FilterRoot))
                                .ToList();
                            diffVM.FilteredEntries = new ObservableCollection<LogEntry>(filtered);
                        }
                        else
                        {
                            diffVM.FilteredEntries = new ObservableCollection<LogEntry>(diffVM.AllLogEntries);
                        }
                    }
                    return;
                }

                if (SelectedTabIndex == AppConstants.TAB_APP)
                {
                    ToggleFilterState(value,
                        () => FilterVM?.IsAppFilterActive ?? false,
                        v => FilterVM.IsAppFilterActive = v,
                        () => FilterVM?.HasAppStoredFilter ?? false,
                        () => ApplyAppLogsFilter());
                }
                else
                {
                    ToggleFilterState(value,
                        () => FilterVM?.IsMainFilterActive ?? false,
                        v => FilterVM.IsMainFilterActive = v,
                        () => FilterVM?.HasMainStoredFilter ?? false,
                        () => UpdateMainLogsFilter(value));
                }
            }
        }

        public bool IsFilterOutActive
        {
            get => SelectedTabIndex == AppConstants.TAB_APP ? (FilterVM?.IsAppFilterOutActive ?? false) : (FilterVM?.IsMainFilterOutActive ?? false);
            set
            {
                if (SelectedTabIndex == AppConstants.TAB_APP)
                {
                    ToggleFilterState(value,
                        () => FilterVM?.IsAppFilterOutActive ?? false,
                        v => FilterVM.IsAppFilterOutActive = v,
                        () => FilterVM?.HasAppStoredFilterOut ?? false,
                        () => ApplyAppLogsFilter());
                }
                else
                {
                    ToggleFilterState(value,
                        () => FilterVM?.IsMainFilterOutActive ?? false,
                        v => FilterVM.IsMainFilterOutActive = v,
                        () => FilterVM?.HasMainStoredFilterOut ?? false,
                        () => UpdateMainLogsFilter(FilterVM.IsMainFilterActive));
                }
            }
        }

        /// <summary>
        /// Shared helper for filter toggle logic: saves scroll position, toggles filter,
        /// applies changes, and restores scroll position.
        /// </summary>
        private void ToggleFilterState(bool value, Func<bool> getCurrent, Action<bool> setCurrent,
            Func<bool> hasStoredFilter, Action applyFilter)
        {
            if (FilterVM == null || getCurrent() == value) return;
            if (value && !hasStoredFilter()) return;

            var savedSelectedLog = SelectedLog;
            if (savedSelectedLog != null)
                SaveScrollPosition(savedSelectedLog);

            setCurrent(value);
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterOutActive));
            applyFilter();

            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }));
            }
        }

        // ── Filter window ──

        private async Task OpenFilterWindow(object obj)
        {
            try
            {
            var win = new Views.FilterWindow();
            bool isAppTab = SelectedTabIndex == AppConstants.TAB_APP;
            var currentRoot = isAppTab ? FilterVM.AppFilterRoot : FilterVM.MainFilterRoot;

            if (currentRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(currentRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                SessionVM.IsBusy = true;
                await Task.Run(() =>
                {
                    if (isAppTab) FilterVM.AppFilterRoot = newRoot;
                    else
                    {
                        FilterVM.MainFilterRoot = newRoot;
                        if (hasAdvanced)
                        {
                            List<LogEntry> cacheCopy;
                            lock (_collectionLock)
                            {
                                cacheCopy = _allLogsCache.ToList();
                            }
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, FilterVM.MainFilterRoot)).ToList();
                            FilterVM.LastFilteredCache = res;
                        }
                        else FilterVM.LastFilteredCache.Clear();
                    }
                });

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (isAppTab) { FilterVM.IsAppFilterActive = hasAdvanced; ApplyAppLogsFilter(); }
                    else { FilterVM.IsMainFilterActive = hasAdvanced || FilterVM.ActiveThreadFilters.Any(); UpdateMainLogsFilter(FilterVM.IsMainFilterActive); }
                    OnPropertyChanged(nameof(IsFilterActive));
                    SessionVM.IsBusy =false;
                });
            }
            }
            catch (Exception ex) { AppLogger.Error("OpenFilterWindow failed", ex); }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node) => FilterVM?.EvaluateFilterNode(log, node) ?? true;
        private void ToggleFilterView(bool show) => FilterVM?.ToggleFilterView(show);
        private void UpdateMainLogsFilter(bool show) => FilterVM?.ApplyMainLogsFilter();
        private void ApplyAppLogsFilter() => FilterVM?.ApplyAppLogsFilter();
        private bool IsDefaultLog(LogEntry l) => FilterVM?.IsDefaultLog(l) ?? false;

        // ── Filter actions ──

        private void ResetTimeFocus(object obj)
        {
            if (VisualTimelineVM != null)
            {
                VisualTimelineVM.ViewScale = 1.0;
                VisualTimelineVM.ViewOffset = 0;
                VisualTimelineVM.SelectedState = null;
            }

            FilterVM.IsTimeFocusActive = false;
            FilterVM.IsAppTimeFocusActive = false;
            FilterVM.LastFilteredCache?.Clear();
            FilterVM.LastFilteredAppCache = null;
            FilterVM.SavedFilterRoot = null;
            FilterVM.SearchText = string.Empty;
            FilterVM.IsMainFilterActive = false;
            FilterVM.IsAppFilterActive = false;
            FilterVM.IsMainFilterOutActive = false;
            FilterVM.IsAppFilterOutActive = false;
            FilterVM.ActiveThreadFilters.Clear();
            FilterVM.NegativeFilters.Clear();
            ResetTreeFilters();

            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterOutActive));

            UpdateMainLogsFilter(false);
            ApplyAppLogsFilter();

            InitializeVisualMode();

            SessionVM.StatusMessage ="Filter reset. Showing all data.";
        }

        private void FilterToState(object obj)
        {
            if (obj is StateEntry state)
            {
                SessionVM.IsBusy = true;
                SessionVM.StatusMessage =$"Focusing state: {state.StateName}...";

                Task.Run(() =>
                {
                    DateTime start = state.StartTime;
                    DateTime end = state.EndTime ?? DateTime.MaxValue;

                    if (SessionVM.AllLogsCache != null)
                    {
                        var timeSlice = SessionVM.AllLogsCache.Where(l => l.Date >= start && l.Date <= end).OrderByDescending(l => l.Date).ToList();
                        var smartFiltered = timeSlice.Where(l => IsDefaultLog(l)).ToList();

                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            FilterVM.LastFilteredCache = timeSlice;
                            FilterVM.SavedFilterRoot = null;
                            FilterVM.IsTimeFocusActive = true;
                            FilterVM.IsMainFilterActive = true;
                            SelectedTabIndex = AppConstants.TAB_PLC;
                            UpdateMainLogsFilter(true);
                            if (FilterVM?.FilteredLogs != null)
                            {
                                FilterVM.FilteredLogs.ReplaceAll(smartFiltered);
                                if (FilterVM.FilteredLogs.Count > 0) SelectedLog = FilterVM.FilteredLogs[0];
                            }
                            OnPropertyChanged(nameof(IsFilterActive));
                            SessionVM.StatusMessage =$"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";

                            if (IsVisualMode && VisualTimelineVM != null)
                            {
                                // Use filtered logs if time range is active
                                var logsForVisual = FilterVM.IsGlobalTimeRangeActive ? SessionVM.Logs : SessionVM.AllLogsCache.ToList();
                                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
                                VisualTimelineVM.LoadData(logsForVisual, eventsToShow);
                                VisualTimelineVM.FocusOnState(state.StateName);
                            }

                            SessionVM.IsBusy =false;
                        });
                    }
                    else
                    {
                        SessionVM.IsBusy =false;
                    }
                });
            }
        }

        private void FilterAppErrors(object obj)
        {
            if (SessionVM.AllAppLogsCache == null || !SessionVM.AllAppLogsCache.Any()) return;
            SessionVM.IsBusy = true;
            SessionVM.StatusMessage ="Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = SessionVM.AllAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    FilterVM?.AppDevLogsFiltered?.ReplaceAll(errors);
                    SessionVM.IsBusy =false;
                    SessionVM.StatusMessage =$"Showing {errors.Count} Errors";
                    FilterVM.IsAppErrorFilterActive = true;
                    FilterVM.IsAppFilterActive = true;
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                });
            });
        }

        private void ApplyChartDrillDownFilter(string filterType, string filterValue)
        {
            try
            {
                if (filterType == "Logger")
                {
                    // Filter by Logger field - search for the logger name in the message
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = AppConstants.TAB_PLC;

                    int logCount = SessionVM?.Logs?.Count() ?? 0;
                    _dialogService.ShowInfo($"Filter applied: Logger = {filterValue}\n\nShowing {logCount} matching logs.", "Logger Filter Applied");
                }
                else if (filterType == "State")
                {
                    // Filter by STATE - search for the state name
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = AppConstants.TAB_PLC;

                    int logCount = SessionVM?.Logs?.Count() ?? 0;
                    _dialogService.ShowInfo($"Filter applied: STATE = {filterValue}\n\nShowing {logCount} matching logs.", "State Filter Applied");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error applying filter: {ex.Message}", "Filter Error");
            }
        }

        // ── Search timer callback ──

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            // Save the currently selected log and its scroll position BEFORE toggling filter
            var savedSelectedLog = SelectedLog;
            if (savedSelectedLog != null)
            {
                SaveScrollPosition(savedSelectedLog);
            }

            ToggleFilterView(IsFilterActive);

            // Restore the selected log and scroll to it, preserving visual position
            // Use Dispatcher to ensure UI has fully updated before scrolling
            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }));
            }
        }

        // ── Logger tree ──

        private void BuildLoggerTree(System.Collections.Generic.IEnumerable<LogEntry> logs) => FilterVM?.BuildLoggerTree(logs);

        public void ResetTreeFilters()
        {
            FilterVM.TreeHiddenLoggers.Clear();
            FilterVM.TreeHiddenPrefixes.Clear();
            FilterVM.TreeShowOnlyLogger = null;
            FilterVM.TreeShowOnlyPrefix = null;
            if (FilterVM?.LoggerTreeRoot != null)
                foreach (var node in FilterVM.LoggerTreeRoot) ResetVisualHiddenState(node);
        }

        private void ResetVisualHiddenState(LoggerNode node)
        {
            node.IsHidden = false;
            node.IsActive = false;
            foreach (var child in node.Children) ResetVisualHiddenState(child);
        }

        // ── App log sorting ──

        public async Task SortAppLogs(string sortBy, bool ascending)
        {
            try
            {
            if (FilterVM?.AppDevLogsFiltered == null || FilterVM.AppDevLogsFiltered.Count == 0) return;
            SessionVM.IsBusy = true;
            SessionVM.StatusMessage ="Sorting...";
            await Task.Run(() =>
            {
                List<LogEntry> sorted = null;
                var source = FilterVM.AppDevLogsFiltered.ToList();
                switch (sortBy)
                {
                    case "Time": sorted = ascending ? source.OrderBy(x => x.Date).ToList() : source.OrderByDescending(x => x.Date).ToList(); break;
                    case "Level": sorted = ascending ? source.OrderBy(x => x.Level).ToList() : source.OrderByDescending(x => x.Level).ToList(); break;
                    case "Logger": sorted = ascending ? source.OrderBy(x => x.Logger).ToList() : source.OrderByDescending(x => x.Logger).ToList(); break;
                    case "Thread": sorted = ascending ? source.OrderBy(x => x.ThreadName).ToList() : source.OrderByDescending(x => x.ThreadName).ToList(); break;
                    default: sorted = source; break;
                }
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    FilterVM.AppDevLogsFiltered.ReplaceAll(sorted);
                    SessionVM.IsBusy =false;
                    SessionVM.StatusMessage ="Sorted.";
                });
            });
            }
            catch (Exception ex) { AppLogger.Error("SortAppLogs failed", ex); }
        }

        // ── Grep result navigation ──

        internal void NavigateToGrepResult(GrepResult result)
        {
            if (result == null) return;

            // If we have a direct reference to the log entry (in-memory search)
            if (result.ReferencedLogEntry != null && result.SessionIndex >= 0)
            {
                // Navigate to the loaded session
                if (result.SessionIndex < SessionVM.LoadedSessions.Count)
                {
                    SessionVM.SelectedSession = SessionVM.LoadedSessions[result.SessionIndex];

                    // Switch to the appropriate tab (0 for PLC, 1 for APP)
                    SelectedTabIndex = (result.LogType == "APP") ? 1 : 0;

                    // Wait for UI to update, then scroll to the log entry
                    Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => RequestScrollToLog?.Invoke(result.ReferencedLogEntry)));
                }
                return;
            }

            // If we don't have a direct reference (external file search)
            if (string.IsNullOrEmpty(result.FilePath)) return;

            // Check if the file is already loaded
            var session = SessionVM.LoadedSessions.FirstOrDefault(s => s.FilePath == result.FilePath);

            if (session != null)
            {
                SessionVM.SelectedSession = session;
                JumpByTime(result, session);
            }
            else
            {
                // Load the file if not already loaded
                ProcessFiles(new[] { result.FilePath }, (loadedSession) =>
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        SessionVM.SelectedSession = loadedSession;
                        JumpByTime(result, loadedSession);
                    });
                });
            }
        }

        private void JumpByTime(GrepResult result, LogSessionData session)
        {
            // Switch to the appropriate tab (0 for PLC, 1 for APP)
            SelectedTabIndex = (result.LogType == "APP") ? 1 : 0;

            // Get the appropriate log collection
            var logs = (result.LogType == "APP") ? session.AppDevLogs : session.Logs;

            // Find the exact log entry by Timestamp and Message
            var target = logs?.FirstOrDefault(l =>
                l.Date == result.Timestamp &&
                l.Message == result.ReferencedLogEntry?.Message &&
                l.ThreadName == result.ReferencedLogEntry?.ThreadName)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp && l.Message == result.ReferencedLogEntry?.Message)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp);

            if (target != null)
            {
                // Wait for UI to update, then scroll to the log entry
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => RequestScrollToLog?.Invoke(target)));
            }
        }

        private void NavigateToLogFromStats(LogEntry log)
        {
            if (log == null) return;
            try
            {
                // Determine which tab to switch to based on log type
                bool isAppLog = !string.IsNullOrEmpty(log.Logger) && !log.Logger.Contains("E1.PLC");
                SelectedTabIndex = isAppLog ? AppConstants.TAB_APP : AppConstants.TAB_PLC;

                SelectedLog = log;
                ScrollToLog(log);
            }
            catch (Exception ex)
            {
                AppLogger.Error("NavigateToLogFromStats failed", ex);
            }
        }
    }
}
