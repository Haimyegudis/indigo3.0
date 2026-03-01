using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // --- TIME-SYNC SCROLLING ---

        // Time-Sync Scrolling
        private bool _isTimeSyncEnabled;
        public bool IsTimeSyncEnabled
        {
            get => _isTimeSyncEnabled;
            set
            {
                _isTimeSyncEnabled = value;
                OnPropertyChanged();
                SessionVM.StatusMessage = value ? "\ud83d\udd17 Time-Sync ENABLED" : "\u26d3 Time-Sync DISABLED";
            }
        }

        private bool _isSyncScrolling = false;
        // True while a tab switch is in progress (between SelectedTabIndex change and
        // DispatcherPriority.Loaded firing). Blocks RequestSyncScroll so that the
        // initial ScrollChanged caused by the newly-visible tab's render cycle does not
        // overwrite _pendingSyncLog (Bug 1) or create a spurious sync (Bug 2).
        private bool _isTabSwitching = false;

        // Set to true inside the BeginInvoke(Loaded) that fires the sync scroll.
        // Read and cleared by the view's TabControl_SelectionChanged ApplicationIdle callback.
        // Without this flag, the dispatch order causes:
        //   Loaded(6): sync scroll applied \u2192 tab at correct synced time
        //   ApplicationIdle(2): ScrollGridToBottom fires \u2192 overwrites synced position with last line
        public bool TimeSyncScrollWasApplied { get; set; } = false;

        // Double (not int) so that sub-second APP/PLC clock differences are preserved.
        // An int would round a 1.7s offset to 1s, causing a visible ~0.7s search error.
        private double _timeSyncOffsetSeconds = 0;
        // Pending sync: stores the log to scroll to when user switches to target tab
        private LogEntry _pendingSyncLog;
        private int _pendingSyncTabIndex = -1;

        public double TimeSyncOffsetSeconds
        {
            get => _timeSyncOffsetSeconds;
            set { _timeSyncOffsetSeconds = value; OnPropertyChanged(); }
        }

        private bool _showSyncedTimeColumn;
        public bool ShowSyncedTimeColumn
        {
            get => _showSyncedTimeColumn;
            set { _showSyncedTimeColumn = value; OnPropertyChanged(); }
        }

        /// <summary>True only when at least one PLC log has SyncedTime populated.
        /// Prevents the user from manually enabling the column when there is no sync data.</summary>
        private bool _hasTimeSyncData;
        public bool HasTimeSyncData
        {
            get => _hasTimeSyncData;
            set
            {
                _hasTimeSyncData = value;
                if (!value) ShowSyncedTimeColumn = false; // auto-hide when data is gone
                OnPropertyChanged();
            }
        }

        // ==================== TIME-SYNC SCROLLING METHODS ====================

        private int LinearSearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0)
                return -1;

            int nearestIndex = 0;
            TimeSpan minDiff = (collection[0].Date - targetTime).Duration();

            for (int i = 1; i < collection.Count; i++)
            {
                TimeSpan currentDiff = (collection[i].Date - targetTime).Duration();
                if (currentDiff < minDiff)
                {
                    minDiff = currentDiff;
                    nearestIndex = i;
                }
                if (minDiff.TotalMilliseconds < 1)
                    break;
            }

            return nearestIndex;
        }

        private int BinarySearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0) return -1;

            int left = 0;
            int right = collection.Count - 1;

            if (targetTime <= collection[0].Date) return 0;
            if (targetTime >= collection[right].Date) return right;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = collection[mid].Date;

                if (midTime == targetTime) return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            if (left >= collection.Count) return right;

            DateTime leftTime = collection[left].Date;
            DateTime rightTime = collection[right].Date;

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff < rightDiff ? left : right;
        }

        public void RequestSyncScroll(DateTime targetTime, string sourceGrid)
        {
            // _isTabSwitching: tab is mid-transition; the ScrollChanged that fired here
            // is from WPF re-rendering the newly-visible DataGrid, not from a real user scroll.
            if (!IsTimeSyncEnabled || _isSyncScrolling || _isTabSwitching) return;

            _isSyncScrolling = true;

            try
            {
                // TimeSyncOffsetSeconds = APP.Date - PLC.Date  (calculated at session load)
                //
                // PLC \u2192 APP:  PLC time is raw PLC clock.  We ADD the offset to convert it to
                //              APP clock space before searching AppDevLogsFiltered.
                //
                // APP \u2192 PLC:  APP time is in APP clock space.  We SUBTRACT the offset to
                //              convert it back to PLC clock space before searching AllLogsCache.
                //              Using +offset here was the bug that caused >1 min offset in this
                //              direction (it shifted the search time by 2\u00d7 the actual offset).
                IList<LogEntry> targetCollection = null;
                string targetGrid = null;
                int targetTabIndex = -1;
                DateTime adjustedTime;

                if (sourceGrid == "PLC")
                {
                    // PLC \u2192 APP: convert PLC time \u2192 APP clock
                    adjustedTime = targetTime.AddSeconds(TimeSyncOffsetSeconds);
                    if (FilterVM?.AppDevLogsFiltered != null && FilterVM.AppDevLogsFiltered.Count > 0)
                    {
                        targetCollection = FilterVM.AppDevLogsFiltered;
                        targetGrid = "APP";
                        targetTabIndex = 1;
                    }
                }
                else if (sourceGrid == "APP")
                {
                    // APP \u2192 PLC: convert APP time \u2192 PLC clock (reverse direction)
                    adjustedTime = targetTime.AddSeconds(-TimeSyncOffsetSeconds);
                    if (SessionVM?.AllLogsCache != null && SessionVM.AllLogsCache.Count > 0)
                    {
                        targetCollection = SessionVM.AllLogsCache;
                        targetGrid = "PLC";
                        targetTabIndex = 0;
                    }
                }
                else
                {
                    adjustedTime = targetTime;
                }

                if (targetCollection == null || targetCollection.Count == 0) return;

                int nearestIndex = BinarySearchNearest(targetCollection, adjustedTime);

                if (nearestIndex >= 0)
                {
                    LogEntry nearestLog = targetCollection[nearestIndex];
                    TimeSpan timeDiff = (nearestLog.Date - adjustedTime).Duration();

                    if (timeDiff.TotalSeconds <= 60)
                    {
                        // Store pending sync - will scroll when user switches to target tab
                        _pendingSyncLog = nearestLog;
                        _pendingSyncTabIndex = targetTabIndex;

                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            SessionVM.StatusMessage =$"\ud83d\udd17 Synced to {targetGrid} @ {nearestLog.Date:HH:mm:ss.ffffff} (\u00b1{timeDiff.TotalSeconds:F1}s) - switch tab to see";
                        });
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            SessionVM.StatusMessage =$"\u26a0 No correlated logs within 60s (closest: {timeDiff.TotalSeconds:F0}s)";
                        });
                    }
                }
            }
            finally
            {
                _isSyncScrolling = false;
            }
        }

        /// <summary>
        /// Navigate to a log entry by time (called from Charts when user clicks on a point)
        /// </summary>
        public void NavigateToLogTime(DateTime time)
        {
            if (FilterVM?.FilteredLogs == null || FilterVM.FilteredLogs.Count == 0) return;

            // Use O(log N) binary search instead of O(N log N) sort
            int idx = BinarySearchNearest(FilterVM.FilteredLogs, time);
            if (idx >= 0)
            {
                RequestScrollToLog?.Invoke(FilterVM.FilteredLogs[idx]);
            }
        }

        /// <summary>
        /// Sync chart cursor when a log entry is selected (called from DataGrid selection)
        /// </summary>
        public void OnLogEntrySelected(LogEntry entry)
        {
            if (entry != null)
            {
                // Sync via ChartVM if available
                if (ChartVM?.HasData == true)
                {
                    ChartVM.SyncToLogTime(entry.Date);
                }

                // Also notify the transfer service for In-Memory sync
                ChartDataTransferService.Instance.NotifyLogTimeSelected(entry.Date);
            }
        }
    }
}
