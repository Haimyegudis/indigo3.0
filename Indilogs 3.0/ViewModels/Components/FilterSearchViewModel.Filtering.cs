using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
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
                        if (int.TryParse(key.Substring(11), out int appIdx) && _appFilterRoot != null)
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
                        if (int.TryParse(key.Substring(12), out int mainIdx) && _mainFilterRoot != null)
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

        // ToggleFilterView, ApplyAppLogsFilter, ApplyMainLogsFilter, ApplyGlobalTimeRangeFilter
        // → moved to FilterSearchViewModel.ApplyFilter.cs
    }
}
