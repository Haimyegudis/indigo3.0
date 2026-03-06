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

        private string? _searchSyntaxError;
        public string? SearchSyntaxError
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
                var result = parser.Parse(FilterVM.SearchText, out string? errorMessage);

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
                _dispatcher.Post(new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }), Services.Interfaces.DispatchPriority.ContextIdle);
            }
        }

        // ── Filter window ──

        private async Task OpenFilterWindow(object? obj)
        {
            try
            {
            var win = _viewFactory.Create<Views.FilterWindow>();
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
                            IList<LogEntry>? cacheRef;
                            lock (_collectionLock)
                            {
                                cacheRef = _allLogsCache;
                            }
                            var res = (cacheRef ?? Array.Empty<LogEntry>()).Where(l => EvaluateFilterNode(l, FilterVM.MainFilterRoot!)).ToList();
                            FilterVM.LastFilteredCache = res;
                        }
                        else FilterVM.LastFilteredCache?.Clear();
                    }
                });

                _dispatcher.Post(() =>
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
    }
}
