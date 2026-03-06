using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // ── Filter actions ──

        private void ResetTimeFocus(object? obj)
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

        private void FilterToState(object? obj)
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

                        _dispatcher.Post(() =>
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
                                var logsForVisual = (FilterVM?.IsGlobalTimeRangeActive == true) ? SessionVM.Logs : (IEnumerable<LogEntry>)SessionVM.AllLogsCache;
                                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
                                VisualTimelineVM.LoadData(logsForVisual, eventsToShow);
                                VisualTimelineVM.FocusOnState(state.StateName);
                            }

                            SessionVM!.IsBusy =false;
                        });
                    }
                    else
                    {
                        SessionVM.IsBusy =false;
                    }
                });
            }
        }

        private void FilterAppErrors(object? obj)
        {
            if (SessionVM.AllAppLogsCache == null || !SessionVM.AllAppLogsCache.Any()) return;
            SessionVM.IsBusy = true;
            SessionVM.StatusMessage ="Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = SessionVM.AllAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                _dispatcher.Post(() =>
                {
                    FilterVM?.AppDevLogsFiltered?.ReplaceAll(errors);
                    SessionVM.IsBusy =false;
                    SessionVM.StatusMessage =$"Showing {errors.Count} Errors";
                    if (FilterVM != null)
                    {
                        FilterVM.IsAppErrorFilterActive = true;
                        FilterVM.IsAppFilterActive = true;
                    }
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

                    int logCount = (SessionVM?.Logs as ICollection<LogEntry>)?.Count ?? 0;
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

                    int logCount = (SessionVM?.Logs as ICollection<LogEntry>)?.Count ?? 0;
                    _dialogService.ShowInfo($"Filter applied: STATE = {filterValue}\n\nShowing {logCount} matching logs.", "State Filter Applied");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error applying filter: {ex.Message}", "Filter Error");
            }
        }

        // ── Search timer callback ──

        private void OnSearchTimerTick(object? sender, EventArgs e)
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
                _dispatcher.Post(new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }), Services.Interfaces.DispatchPriority.ContextIdle);
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
                var sorted = FilterVM.AppDevLogsFiltered.ToList();
                Comparison<LogEntry> cmp = sortBy switch
                {
                    "Time" => (a, b) => ascending ? a.Date.CompareTo(b.Date) : b.Date.CompareTo(a.Date),
                    "Level" => (a, b) => ascending ? string.Compare(a.Level, b.Level, StringComparison.Ordinal) : string.Compare(b.Level, a.Level, StringComparison.Ordinal),
                    "Logger" => (a, b) => ascending ? string.Compare(a.Logger, b.Logger, StringComparison.Ordinal) : string.Compare(b.Logger, a.Logger, StringComparison.Ordinal),
                    "Thread" => (a, b) => ascending ? string.Compare(a.ThreadName, b.ThreadName, StringComparison.Ordinal) : string.Compare(b.ThreadName, a.ThreadName, StringComparison.Ordinal),
                    _ => null!,
                };
                if (cmp != null) sorted.Sort(cmp);
                _dispatcher.Post(() =>
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
                    _dispatcher.Post(new Action(() => RequestScrollToLog?.Invoke(result.ReferencedLogEntry)),
                        Services.Interfaces.DispatchPriority.Background);
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
                    _dispatcher.Post(() =>
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
                _dispatcher.Post(new Action(() => RequestScrollToLog?.Invoke(target)),
                    Services.Interfaces.DispatchPriority.Background);
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
