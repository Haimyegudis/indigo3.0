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
        //   Loaded(6): sync scroll applied → tab at correct synced time
        //   ApplicationIdle(2): ScrollGridToBottom fires → overwrites synced position with last line
        public bool TimeSyncScrollWasApplied { get; set; } = false;

        // Double (not int) so that sub-second APP/PLC clock differences are preserved.
        // An int would round a 1.7s offset to 1s, causing a visible ~0.7s search error.
        private double _timeSyncOffsetSeconds = 0;
        // Pending sync: stores the log to scroll to when user switches to target tab
        private LogEntry? _pendingSyncLog;
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

        /// <summary>
        /// Binary search for the log entry whose Date is nearest to targetTime.
        /// Collection must be sorted by Date ascending.
        /// </summary>
        private int BinarySearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            return BinarySearchNearestBy(collection, targetTime, log => log.Date);
        }

        /// <summary>
        /// Binary search for the log entry whose time (extracted by selector) is nearest to targetTime.
        /// Collection must be sorted by the selector value in ascending order.
        /// </summary>
        private int BinarySearchNearestBy(IList<LogEntry> collection, DateTime targetTime, Func<LogEntry, DateTime> timeSelector)
        {
            if (collection == null || collection.Count == 0) return -1;

            int left = 0;
            int right = collection.Count - 1;

            if (targetTime <= timeSelector(collection[0])) return 0;
            if (targetTime >= timeSelector(collection[right])) return right;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = timeSelector(collection[mid]);

                if (midTime == targetTime) return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            if (left >= collection.Count) return right;

            DateTime leftTime = timeSelector(collection[left]);
            DateTime rightTime = timeSelector(collection[right]);

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff < rightDiff ? left : right;
        }

        /// <summary>
        /// Called from TriggerTimeSyncScroll in the view. Accepts the source log entry
        /// directly (not just time) so we can use SyncedTime when available for ms precision.
        /// </summary>
        public void RequestSyncScroll(LogEntry sourceLog, string sourceGrid)
        {
            if (!IsTimeSyncEnabled || _isSyncScrolling || _isTabSwitching) return;

            _isSyncScrolling = true;

            try
            {
                IList<LogEntry>? targetCollection = null;
                string? targetGrid = null;
                int targetTabIndex = -1;
                DateTime adjustedTime;

                if (sourceGrid == "PLC")
                {
                    // PLC → APP: need to convert PLC time to APP clock
                    // If SyncedTime exists (tick-precise), use it directly (already in APP clock).
                    // Otherwise fall back to Date + offset (double→ticks conversion, less precise).
                    if (sourceLog.SyncedTime.HasValue)
                        adjustedTime = sourceLog.SyncedTime.Value;
                    else
                        adjustedTime = sourceLog.Date.AddSeconds(TimeSyncOffsetSeconds);

                    if (FilterVM?.AppDevLogsFiltered != null && FilterVM.AppDevLogsFiltered.Count > 0)
                    {
                        targetCollection = FilterVM.AppDevLogsFiltered;
                        targetGrid = "APP";
                        targetTabIndex = 1;
                    }
                }
                else if (sourceGrid == "APP")
                {
                    // APP → PLC: need to find PLC log at the same real-world moment.
                    // If PLC logs have SyncedTime, search by SyncedTime (already in APP clock)
                    // using the APP log's Date directly — avoids double→ticks precision loss.
                    // Otherwise search by Date with offset subtracted.
                    adjustedTime = sourceLog.Date;

                    var plcLogs = SessionVM?.Logs as IList<LogEntry>;
                    if (plcLogs != null && plcLogs.Count > 0)
                    {
                        targetCollection = plcLogs;
                        targetGrid = "PLC";
                        targetTabIndex = 0;
                    }
                }
                else
                {
                    adjustedTime = sourceLog.Date;
                }

                if (targetCollection == null || targetCollection.Count == 0) return;

                // For APP→PLC with SyncedTime: search PLC by SyncedTime field (APP clock ↔ APP clock).
                // For all other cases: search by Date field.
                int nearestIndex;
                if (sourceGrid == "APP" && HasTimeSyncData && targetCollection.Count > 0
                    && targetCollection[0].SyncedTime.HasValue)
                {
                    nearestIndex = BinarySearchNearestBy(targetCollection, adjustedTime,
                        log => log.SyncedTime ?? log.Date);
                }
                else
                {
                    nearestIndex = BinarySearchNearest(targetCollection, adjustedTime);
                }

                if (nearestIndex >= 0)
                {
                    LogEntry nearestLog = targetCollection[nearestIndex];

                    // Calculate diff using the same clock space for accurate display
                    DateTime nearestTime = (sourceGrid == "APP" && nearestLog.SyncedTime.HasValue)
                        ? nearestLog.SyncedTime.Value
                        : nearestLog.Date;
                    TimeSpan timeDiff = (nearestTime - adjustedTime).Duration();

                    if (timeDiff.TotalSeconds <= 60)
                    {
                        _pendingSyncLog = nearestLog;
                        _pendingSyncTabIndex = targetTabIndex;

                        _dispatcher.Post(() =>
                        {
                            if (SessionVM != null) SessionVM.StatusMessage = $"\ud83d\udd17 Synced to {targetGrid} @ {nearestLog.Date:HH:mm:ss.fff} (\u00b1{timeDiff.TotalMilliseconds:F0}ms) - switch tab to see";
                        });
                    }
                    else
                    {
                        _dispatcher.Post(() =>
                        {
                            if (SessionVM != null) SessionVM.StatusMessage = $"\u26a0 No correlated logs within 60s (closest: {timeDiff.TotalSeconds:F1}s)";
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
        /// Legacy overload for backward compatibility (chart navigation, etc.)
        /// </summary>
        public void RequestSyncScroll(DateTime targetTime, string sourceGrid)
        {
            // Create a temporary LogEntry wrapper for the DateTime-only call path
            var tempLog = new LogEntry { Date = targetTime };
            RequestSyncScroll(tempLog, sourceGrid);
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
