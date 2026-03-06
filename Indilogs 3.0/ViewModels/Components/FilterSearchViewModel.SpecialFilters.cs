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

        private async Task OpenFilterWindow(object? obj)
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
                win.Owner = _windowOwner.GetOwner();
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
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, MainFilterRoot!)).ToList();
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
        private async Task OpenDifferentLogsFilterWindow(object? obj)
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
            var win = _viewFactory.Create<Views.FilterWindow>(threads, loggers, diffVM.AvailableFields ?? new List<string>());

            // Position near button
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = _windowOwner.GetOwner();
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
                            .Where(l => EvaluateFilterNode(l, newRoot!))
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

        private void FilterOut(object? p)
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

        private void FilterOutThread(object? obj)
        {
            if (_parent.SelectedLog == null) return;
            var win = _viewFactory.Create<Views.FilterOutWindow>(_parent.SelectedLog.ThreadName ?? "");
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

    }
}
