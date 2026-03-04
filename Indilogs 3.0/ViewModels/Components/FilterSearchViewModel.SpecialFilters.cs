using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        private bool IsPlcTabActive => _parent?.IsPLCTabSelected == true;

        private async Task OpenFilterWindow(object obj)
        {
            try
            {
            // Different Logs tab (index 12): route to its own filter logic
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

            var win = _viewFactory.Create<Views.FilterWindow>(threads, loggers);
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
                        _dispatcher.Post(() =>
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

                    _dispatcher.Post(() =>
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
                            _dispatcher.Post(() =>
                            {
                                _parent.SelectedLog = logToRestore;
                                _parent.ScrollToLogPreservePosition(logToRestore);
                            }, DispatchPriority.ContextIdle);
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

                _dispatcher.Post(() =>
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
            var win = _viewFactory.Create<Views.FilterWindow>(threads, loggers, diffVM.AvailableFields);

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

                        _dispatcher.Post(() =>
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
            var w = _viewFactory.Create<Views.FilterOutWindow>(_parent.SelectedLog.Message);
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
            var win = _viewFactory.Create<Views.FilterOutWindow>(_parent.SelectedLog.ThreadName);
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

            var win = _viewFactory.Create<Views.ThreadFilterWindow>(threads);
            win.Title = "Filter by Thread";

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

            var win = _viewFactory.Create<Views.ThreadFilterWindow>(loggers);
            win.Title = "Filter by Logger";

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

            var win = _viewFactory.Create<Views.ThreadFilterWindow>(methods);
            win.Title = "Filter by Method";

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
                        _dispatcher.Post(() =>
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
                        _dispatcher.Post(() =>
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
                            rangedLogs = ((List<LogEntry>)_sessionVM.AllAppLogsCache).GetRange(startIdx, endIdx - startIdx + 1);
                        }
                        else
                        {
                            // Fallback to time-based range
                            rangedLogs = _sessionVM.AllAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).ToList();
                        }
                        _dispatcher.Post(() =>
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
                            rangedLogs = ((List<LogEntry>)_sessionVM.AllLogsCache).GetRange(startIdx, endIdx - startIdx + 1);
                        }
                        else
                        {
                            // Fallback to time-based range
                            rangedLogs = _sessionVM.AllLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).ToList();
                        }
                        _dispatcher.Post(() =>
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
                _dialogService.ShowInfo("No logs available to filter.", "No Logs");
                return;
            }

            // Pass current filter values so the window shows the already-filtered range
            var window = _viewFactory.Create<Views.TimeRangeWindow>(earliestLog.Value, latestLog.Value, GlobalTimeRangeStart, GlobalTimeRangeEnd);

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
        }
    }
}
