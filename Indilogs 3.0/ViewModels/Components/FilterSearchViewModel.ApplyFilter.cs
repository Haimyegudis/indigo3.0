using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        public void ToggleFilterView(bool show)
        {
            ApplyMainLogsFilter();
            ApplyAppLogsFilter();
            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
        }

        /// <summary>
        /// Applies all active filters (thread, logger, advanced, search, negative) to APP logs and updates AppDevLogsFiltered.
        /// </summary>
        public void ApplyAppLogsFilter()
        {
            var filterSw = System.Diagnostics.Stopwatch.StartNew();
            // Guard against crash if cache is empty
            if (_sessionVM?.AllAppLogsCache == null) return;

            bool isActive = _isAppFilterActive;

            // Determine data source (regular cache or Focus Context cache)
            var source = _sessionVM.AllAppLogsCache;
            if (isActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }

            // Apply global time range filter to APP logs
            if (IsGlobalTimeRangeActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                source = source.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value).ToList();
            }

            // Check if stored filters exist - but only apply them if checkbox is checked
            bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            // Filters are only applied when checkbox is checked (isActive)
            bool hasThreadFilter = isActive && _appActiveThreadFilters.Any();
            bool hasLoggerFilter = isActive && _activeLoggerFilters.Any();
            bool hasMethodFilter = isActive && _activeMethodFilters.Any();
            bool hasTreeFilter = isActive && (_treeShowOnlyLogger != null || _treeShowOnlyPrefix != null || _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0);
            bool hasAdvancedFilter = isActive && _appFilterRoot != null && _appFilterRoot.Children.Count > 0;

            // If checkbox unchecked and no search, show everything
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
            if (hasAdvancedFilter && _appFilterRoot != null)
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
                string search = SearchText ?? "";
                if (QueryParserService.HasBooleanOperators(search))
                {
                    var parser = new QueryParserService();
                    var filterTree = parser.Parse(search, out string? errorMessage);
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

            // 7. APP Filter Out (negative filters) - mirror of PLC filter-out but for APP logs
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

            // 1. Determine data source
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
                if (hasAdvancedFilter && _mainFilterRoot != null)
                {
                    currentLogs = currentLogs.Where(l => EvaluateFilterNode(l, _mainFilterRoot));
                }

                // Thread filter (only if checkbox is checked) - use HashSet for O(1) lookup
                if (hasThreadFilter)
                {
                    var threadSet = new HashSet<string>(_activeThreadFilters, StringComparer.OrdinalIgnoreCase);
                    currentLogs = currentLogs.Where(l => l.ThreadName != null && threadSet.Contains(l.ThreadName));
                }

                // Search text filter (always apply search, regardless of checkbox)
                if (hasSearchText)
                {
                    string searchText = SearchText ?? "";
                    if (QueryParserService.HasBooleanOperators(searchText))
                    {
                        var parser = new QueryParserService();
                        var filterTree = parser.Parse(searchText, out string? errorMessage);
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
                // When no active filters (checkbox unchecked), show everything
                currentLogs = _sessionVM?.AllLogsCache ?? new List<LogEntry>();
            }

            // 2. Apply global time range filter
            // Applied before negative filter and before UI update
            if (IsGlobalTimeRangeActive && !_isTimeFocusActive && GlobalTimeRangeStart.HasValue && GlobalTimeRangeEnd.HasValue)
            {
                currentLogs = currentLogs.Where(l => l.Date >= GlobalTimeRangeStart.Value && l.Date <= GlobalTimeRangeEnd.Value);
            }

            // 3. Negative filter (Filter Out) - pre-split for faster iteration
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

            // Update PLC Logs Tab
            if (_sessionVM != null)
                _sessionVM.Logs = logsList;

            // PLC Filtered Tab - not updated here!
            // This tab should keep the original data (Manager, Events, Error)
            // and not be affected by the PLC Logs filter.
            // Data is loaded once in SwitchToSession and remains constant.

            _parent?.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
            _parent?.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            int srcCount = _sessionVM?.AllLogsCache?.Count ?? 0;
            AppLogger.Info($"[Filter] PLC filter applied: {srcCount:N0} → {logsList.Count:N0} entries — {filterSw.ElapsedMilliseconds}ms");
        }

        private void ApplyGlobalTimeRangeFilter()
        {
            // 1. Handle events - filter or reset
            if (_sessionVM?.AllEvents != null)
            {
                List<EventEntry> eventsToShow;

                if (!IsGlobalTimeRangeActive)
                {
                    // Clear mode: show all events (already sorted during load)
                    eventsToShow = _sessionVM.AllEvents.ToList();
                }
                else
                {
                    // Filter mode: take only events in range (already sorted, Where preserves order)
                    eventsToShow = _sessionVM.AllEvents
                        .Where(e => e.Time >= GlobalTimeRangeStart!.Value && e.Time <= GlobalTimeRangeEnd!.Value)
                        .ToList();
                }

                // Update the list
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

            // 2. Update logs (App + PLC)
            // Filter functions will now automatically account for IsGlobalTimeRangeActive
            ApplyAppLogsFilter();
            ApplyMainLogsFilter();

            // 3. Update status and notify UI
            if (!IsGlobalTimeRangeActive)
            {
                if (_sessionVM != null)
                    _sessionVM.StatusMessage = "Time range filter cleared";
            }
            else if (_sessionVM != null)
            {
                var plcCount = (_sessionVM.Logs as ICollection<LogEntry>)?.Count ?? 0;
                var appCount = (AppDevLogsFiltered?.Count) ?? 0;
                var filteredCount = (FilteredLogs?.Count) ?? 0;
                var eventsCount = (_sessionVM.Events?.Count) ?? 0;
                _sessionVM.StatusMessage = $"Time Range Filter: PLC={plcCount}, APP={appCount}, FILTERED={filteredCount}, Events={eventsCount}";
            }

            // Visual Timeline updates via SessionVM's own PropertyChanged / CollectionChanged
        }
    }
}
