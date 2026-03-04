using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class CaseManagementViewModel
    {
        // ── Marked Logs: mark/unmark, window management, navigation ──

        private void MarkRow(object obj)
        {
            if (_parent.SelectedLog != null)
            {
                var currentLog = _parent.SelectedLog;
                currentLog.IsMarked = !currentLog.IsMarked;

                // Force UI refresh by re-notifying RowBackground
                currentLog.OnPropertyChanged(nameof(currentLog.RowBackground));

                bool isAppTab = _parent.SelectedTabIndex == 1;
                var targetList = isAppTab ? MarkedAppLogs : MarkedLogs;

                if (currentLog.IsMarked)
                {
                    targetList.Add(currentLog);
                    var sorted = targetList.OrderByDescending(x => x.Date).ToList();
                    targetList.Clear();
                    foreach (var l in sorted) targetList.Add(l);
                }
                else
                {
                    targetList.Remove(currentLog);
                }
            }
        }

        private void UnmarkLog(object obj)
        {
            // Placeholder for future unmark functionality
        }

        /// <summary>
        /// Opens marked logs window - combined or separate based on IsMarkedLogsCombined setting
        /// </summary>
        private void OpenMarkedLogsWindow(object obj)
        {
            if (IsMarkedLogsCombined)
            {
                // Check if combined window already exists
                if (_combinedMarkedWindow != null && _combinedMarkedWindow.IsVisible)
                {
                    _combinedMarkedWindow.Activate();
                    return;
                }

                // Combine main and app marked logs
                var combinedList = new List<LogEntry>();
                if (MarkedLogs != null) combinedList.AddRange(MarkedLogs);
                if (MarkedAppLogs != null) combinedList.AddRange(MarkedAppLogs);

                var sortedList = combinedList.OrderByDescending(x => x.Date).ToList();
                var collectionToShow = new ObservableCollection<LogEntry>(sortedList);

                _combinedMarkedWindow = _viewFactory.Create<MarkedLogsWindow>(collectionToShow, "Marked Lines (Combined - Main & App)");
                _combinedMarkedWindow.DataContext = _parent;
                _combinedMarkedWindow.Closed += (s, e) => _combinedMarkedWindow = null;
                WindowManager.OpenWindow(_combinedMarkedWindow);
            }
            else
            {
                bool isAppTab = _parent.SelectedTabIndex == 1;

                if (isAppTab)
                {
                    // Show App logs marked window
                    if (_markedAppLogsWindow != null && _markedAppLogsWindow.IsVisible)
                    {
                        WindowManager.ActivateWindow(_markedAppLogsWindow);
                        return;
                    }
                    _markedAppLogsWindow = _viewFactory.Create<MarkedLogsWindow>(MarkedAppLogs, "Marked Lines (APP)");
                    _markedAppLogsWindow.DataContext = _parent;
                    _markedAppLogsWindow.Closed += (s, e) => _markedAppLogsWindow = null;
                    WindowManager.OpenWindow(_markedAppLogsWindow);
                }
                else
                {
                    // Show Main logs marked window
                    if (_markedMainLogsWindow != null && _markedMainLogsWindow.IsVisible)
                    {
                        WindowManager.ActivateWindow(_markedMainLogsWindow);
                        return;
                    }
                    _markedMainLogsWindow = _viewFactory.Create<MarkedLogsWindow>(MarkedLogs, "Marked Lines (LOGS)");
                    _markedMainLogsWindow.DataContext = _parent;
                    _markedMainLogsWindow.Closed += (s, e) => _markedMainLogsWindow = null;
                    WindowManager.OpenWindow(_markedMainLogsWindow);
                }
            }
        }

        /// <summary>
        /// Closes all marked log windows (combined, main, and app)
        /// </summary>
        private void CloseAllMarkedWindows()
        {
            if (_combinedMarkedWindow != null) { _combinedMarkedWindow.Close(); _combinedMarkedWindow = null; }
            if (_markedMainLogsWindow != null) { _markedMainLogsWindow.Close(); _markedMainLogsWindow = null; }
            if (_markedAppLogsWindow != null) { _markedAppLogsWindow.Close(); _markedAppLogsWindow = null; }
        }

        // Track the currently highlighted marked log
        private LogEntry? _currentMarkedLog = null;

        private void ClearCurrentMarked()
        {
            if (_currentMarkedLog != null)
            {
                _currentMarkedLog.IsCurrentMarked = false;
                _currentMarkedLog = null;
            }
        }

        /// <summary>
        /// Returns the correct log collection for the currently selected tab.
        /// </summary>
        private IEnumerable<LogEntry> GetActiveLogCollection()
        {
            if (_parent.SelectedTabIndex == AppConstants.TAB_APP)
                return _filterVM.AppDevLogsFiltered ?? Enumerable.Empty<LogEntry>();
            return _sessionVM.Logs ?? Enumerable.Empty<LogEntry>();
        }

        /// <summary>
        /// Navigate to the next marked log entry
        /// </summary>
        private void GoToNextMarked(object obj)
        {
            var logs = GetActiveLogCollection();
            if (!logs.Any()) return;

            var list = logs.ToList();
            int current = _parent.SelectedLog != null ? list.IndexOf(_parent.SelectedLog) : -1;
            var next = list.Skip(current + 1).FirstOrDefault(l => l.IsMarked) ?? list.FirstOrDefault(l => l.IsMarked);

            if (next != null)
            {
                ClearCurrentMarked();
                next.IsCurrentMarked = true;
                _currentMarkedLog = next;
                _parent.SelectedLog = next;
                _parent.ScrollToLog(next);
            }
        }

        /// <summary>
        /// Navigate to the previous marked log entry
        /// </summary>
        private void GoToPrevMarked(object obj)
        {
            var logs = GetActiveLogCollection();
            if (!logs.Any()) return;

            var list = logs.ToList();
            int current = _parent.SelectedLog != null ? list.IndexOf(_parent.SelectedLog) : list.Count;
            var prev = list.Take(current).LastOrDefault(l => l.IsMarked) ?? list.LastOrDefault(l => l.IsMarked);

            if (prev != null)
            {
                ClearCurrentMarked();
                prev.IsCurrentMarked = true;
                _currentMarkedLog = prev;
                _parent.SelectedLog = prev;
                _parent.ScrollToLog(prev);
            }
        }

        /// <summary>
        /// Clears all marked logs collections
        /// </summary>
        public void ClearMarkedLogs()
        {
            MarkedLogs.Clear();
            MarkedAppLogs.Clear();
        }
    }
}
