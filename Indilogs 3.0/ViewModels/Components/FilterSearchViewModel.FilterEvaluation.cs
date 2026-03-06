using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
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
                            log.ExtraFields.TryGetValue(node.Field, out string? efVal))
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

        public bool IsDefaultLog(LogEntry l)
        {
            var filter = _defaultPlcFilter ?? DefaultConfigurationService.GetFactoryPlcFilter();
            return EvaluateFilterNode(l, filter);
        }

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
    }
}
