using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        /// <summary>
        /// Returns a list of active filter descriptions for display in the right panel
        /// </summary>
        public List<ActiveFilterItem> GetActiveFilters()
        {
            var items = new List<ActiveFilterItem>();
            int tab = _parent?.SelectedTabIndex ?? 0;
            bool isAppTab = (tab == AppConstants.TAB_APP);
            bool isPLCTab = (tab == AppConstants.TAB_PLC);

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
                    try { return Regex.IsMatch(val, criteria, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
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
        // ToggleFilterView, ApplyAppLogsFilter, ApplyMainLogsFilter, ApplyGlobalTimeRangeFilter
        // → moved to FilterSearchViewModel.ApplyFilter.cs
    }
}
