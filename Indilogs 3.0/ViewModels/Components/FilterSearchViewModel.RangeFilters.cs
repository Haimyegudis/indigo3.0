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
        private void OpenLoggerFilter(object? obj)
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
                win.Owner = _windowOwner.GetOwner();
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

        private void OpenMethodFilter(object? obj)
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
                win.Owner = _windowOwner.GetOwner();
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

        private void UndoFilterOut(object? obj)
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

        private void OpenTimeRangeFilter(object? obj)
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
            var window = _viewFactory.Create<Views.TimeRangeWindow>(earliestLog.Value, latestLog.Value, (object)GlobalTimeRangeStart!, (object)GlobalTimeRangeEnd!);

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                window.Owner = _windowOwner.GetOwner();
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
