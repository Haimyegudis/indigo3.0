using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Handles tab orchestration: time-sync scrolling between PLC/APP tabs,
    /// binary search for time correlation, and sync scroll state.
    /// Extracted from MainViewModel to reduce its responsibility.
    /// </summary>
    public class TabOrchestrationViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;

        // Time-Sync state
        private bool _isTimeSyncEnabled;
        private bool _isSyncScrolling;
        private int _timeSyncOffsetSeconds;
        private LogEntry _pendingSyncLog;
        private int _pendingSyncTabIndex = -1;

        public bool IsTimeSyncEnabled
        {
            get => _isTimeSyncEnabled;
            set
            {
                _isTimeSyncEnabled = value;
                OnPropertyChanged();
                _parent.StatusMessage = value ? "🔗 Time-Sync ENABLED" : "⛓ Time-Sync DISABLED";
            }
        }

        public int TimeSyncOffsetSeconds
        {
            get => _timeSyncOffsetSeconds;
            set { _timeSyncOffsetSeconds = value; OnPropertyChanged(); }
        }

        public ICommand ToggleTimeSyncCommand { get; }

        /// <summary>
        /// Pending sync log — stores a log entry to scroll to when user switches to the target tab.
        /// MainViewModel's SelectedTabIndex setter checks this.
        /// </summary>
        public LogEntry PendingSyncLog
        {
            get => _pendingSyncLog;
            set => _pendingSyncLog = value;
        }

        public int PendingSyncTabIndex
        {
            get => _pendingSyncTabIndex;
            set => _pendingSyncTabIndex = value;
        }

        public TabOrchestrationViewModel(MainViewModel parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            ToggleTimeSyncCommand = new RelayCommand(o => IsTimeSyncEnabled = !IsTimeSyncEnabled);
        }

        /// <summary>
        /// Called when user scrolls one grid — finds the corresponding log in the other tab
        /// and stores it as a pending sync target.
        /// </summary>
        public void RequestSyncScroll(DateTime targetTime, string sourceGrid)
        {
            if (!IsTimeSyncEnabled || _isSyncScrolling) return;

            _isSyncScrolling = true;

            try
            {
                DateTime adjustedTime = targetTime.AddSeconds(TimeSyncOffsetSeconds);
                IList<LogEntry> targetCollection = null;
                string targetGrid = null;
                int targetTabIndex = -1;

                if (sourceGrid == "PLC" || sourceGrid == "PLCFiltered")
                {
                    if (_parent.AppDevLogsFiltered != null && _parent.AppDevLogsFiltered.Count > 0)
                    {
                        targetCollection = _parent.AppDevLogsFiltered;
                        targetGrid = "APP";
                        targetTabIndex = 2;
                    }
                }
                else if (sourceGrid == "APP")
                {
                    if (_parent.AllLogsCache != null && _parent.AllLogsCache.Count > 0)
                    {
                        targetCollection = _parent.AllLogsCache;
                        targetGrid = "PLC";
                        targetTabIndex = 0;
                    }
                }

                if (targetCollection == null || targetCollection.Count == 0) return;

                int nearestIndex = BinarySearchNearest(targetCollection, adjustedTime);

                if (nearestIndex >= 0)
                {
                    LogEntry nearestLog = targetCollection[nearestIndex];
                    TimeSpan timeDiff = (nearestLog.Date - adjustedTime).Duration();

                    if (timeDiff.TotalSeconds <= 60)
                    {
                        _pendingSyncLog = nearestLog;
                        _pendingSyncTabIndex = targetTabIndex;

                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            _parent.StatusMessage = $"🔗 Synced to {targetGrid} @ {nearestLog.Date:HH:mm:ss.fff} (±{timeDiff.TotalSeconds:F1}s) - switch tab to see";
                        });
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            _parent.StatusMessage = $"⚠ No correlated logs within 60s (closest: {timeDiff.TotalSeconds:F0}s)";
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
            if (_parent.FilteredLogs == null || _parent.FilteredLogs.Count == 0) return;

            var nearestLog = FindNearestByTime(_parent.FilteredLogs, time);
            if (nearestLog != null)
                _parent.ScrollToLog(nearestLog);
        }

        /// <summary>
        /// Sync chart cursor when a log entry is selected (called from DataGrid selection)
        /// </summary>
        public void OnLogEntrySelected(LogEntry entry)
        {
            if (entry != null)
            {
                if (_parent.ChartVM?.HasData == true)
                    _parent.ChartVM.SyncToLogTime(entry.Date);

                ChartDataTransferService.Instance.NotifyLogTimeSelected(entry.Date);
            }
        }

        /// <summary>
        /// Called from SelectedTabIndex setter when user switches to the pending sync target tab.
        /// Clears the pending sync and triggers the scroll.
        /// </summary>
        public void TryApplyPendingSync(int newTabIndex, Action<LogEntry> scrollAction)
        {
            if (_pendingSyncLog != null && newTabIndex == _pendingSyncTabIndex)
            {
                var logToScroll = _pendingSyncLog;
                _pendingSyncLog = null;
                _pendingSyncTabIndex = -1;
                Application.Current?.Dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => scrollAction?.Invoke(logToScroll)));
            }
        }

        #region Search Algorithms

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

        private LogEntry FindNearestByTime(IList<LogEntry> collection, DateTime time)
        {
            int index = BinarySearchNearest(collection, time);
            return index >= 0 ? collection[index] : null;
        }

        #endregion
    }
}
