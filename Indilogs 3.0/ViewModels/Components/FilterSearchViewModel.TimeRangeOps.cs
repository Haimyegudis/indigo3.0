using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        private void FilterContext(object? obj)
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

        private void StartRange(object? obj)
        {
            if (_parent.SelectedLog == null) return;
            _rangeStartLog = _parent.SelectedLog;
            HasRangeStart = true;
            _sessionVM.StatusMessage = $"Range Start: {_rangeStartLog.Date:HH:mm:ss.ffffff} — Now scroll to end and select 'End Range'";
        }

        private void EndRange(object? obj)
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

        private void ClearRange(object? obj)
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
    }
}
