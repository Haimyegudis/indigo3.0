using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class ComparisonPaneViewModel
    {
        #region Filtering & Search

        /// <summary>
        /// Updates the available filters based on the selected source type.
        /// </summary>
        private void UpdateAvailableFilters()
        {
            AvailableFilters.Clear();
            _selectedFilter = null;

            switch (_selectedSourceType)
            {
                case SourceType.ByThread:
                    var plcThreads = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.ThreadName))
                        .Select(l => l.ThreadName)
                        .Distinct()
                        .OrderBy(t => t);
                    foreach (var t in plcThreads)
                        AvailableFilters.Add(t);
                    break;

                case SourceType.ByThreadFromApp:
                    var appThreads = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.ThreadName))
                        .Select(l => l.ThreadName)
                        .Distinct()
                        .OrderBy(t => t);
                    foreach (var t in appThreads)
                        AvailableFilters.Add(t);
                    break;

                case SourceType.ByLogger:
                    var appLoggers = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.Logger))
                        .Select(l => l.Logger)
                        .Distinct()
                        .OrderBy(l => l);
                    foreach (var l in appLoggers)
                        AvailableFilters.Add(l);
                    break;

                case SourceType.ByLoggerFromPLC:
                    var plcLoggers = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Logger))
                        .Select(l => l.Logger)
                        .Distinct()
                        .OrderBy(l => l);
                    foreach (var l in plcLoggers)
                        AvailableFilters.Add(l);
                    break;

                case SourceType.ByMethod:
                    var appMethods = _allAppLogs
                        .Where(l => !string.IsNullOrEmpty(l.Method))
                        .Select(l => l.Method)
                        .Distinct()
                        .OrderBy(m => m);
                    foreach (var m in appMethods)
                        AvailableFilters.Add(m);
                    break;

                case SourceType.ByMethodFromPLC:
                    var plcMethods = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Method))
                        .Select(l => l.Method)
                        .Distinct()
                        .OrderBy(m => m);
                    foreach (var m in plcMethods)
                        AvailableFilters.Add(m);
                    break;

                case SourceType.ByPattern:
                    var patterns = _allPlcLogs
                        .Where(l => !string.IsNullOrEmpty(l.Pattern))
                        .Select(l => l.Pattern)
                        .Distinct()
                        .OrderBy(p => p);
                    foreach (var p in patterns)
                        AvailableFilters.Add(p);
                    break;
            }

            // Select first filter if available
            if (AvailableFilters.Count > 0)
            {
                SelectedFilter = AvailableFilters[0];
            }

            OnPropertyChanged(nameof(AvailableFilters));
        }

        /// <summary>
        /// Applies the current source type and filter to get the base log list.
        /// </summary>
        public void ApplySourceFilter()
        {
            IEnumerable<LogEntry> result;

            switch (_selectedSourceType)
            {
                case SourceType.AllPLC:
                    result = _allPlcLogs;
                    break;

                case SourceType.AllAPP:
                    result = _allAppLogs;
                    break;

                case SourceType.ByThread:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.ThreadName == _selectedFilter);
                    break;

                case SourceType.ByThreadFromApp:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.ThreadName == _selectedFilter);
                    break;

                case SourceType.ByLogger:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.Logger == _selectedFilter);
                    break;

                case SourceType.ByLoggerFromPLC:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Logger == _selectedFilter);
                    break;

                case SourceType.ByMethod:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allAppLogs
                        : _allAppLogs.Where(l => l.Method == _selectedFilter);
                    break;

                case SourceType.ByMethodFromPLC:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Method == _selectedFilter);
                    break;

                case SourceType.ByPattern:
                    result = string.IsNullOrEmpty(_selectedFilter)
                        ? _allPlcLogs
                        : _allPlcLogs.Where(l => l.Pattern == _selectedFilter);
                    break;

                default:
                    result = _allPlcLogs;
                    break;
            }

            // Apply search filter if present
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var searchLower = _searchText.ToLowerInvariant();
                result = result.Where(l =>
                    (l.Message?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.Logger?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.ThreadName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (l.Method?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            FilteredLogs.ReplaceAll(result.OrderBy(l => l.Date));
            OnPropertyChanged(nameof(FilteredLogs));
        }

        /// <summary>
        /// Applies only the search filter (called when search text changes).
        /// </summary>
        private void ApplySearchFilter()
        {
            ApplySourceFilter();
        }

        /// <summary>
        /// Performs binary search to find the log entry nearest to the target time.
        /// Returns the index in FilteredLogs.
        /// </summary>
        public int BinarySearchNearest(DateTime targetTime)
        {
            if (FilteredLogs == null || FilteredLogs.Count == 0)
                return -1;

            int left = 0;
            int right = FilteredLogs.Count - 1;

            // Edge cases
            if (targetTime <= FilteredLogs[0].Date)
                return 0;
            if (targetTime >= FilteredLogs[right].Date)
                return right;

            // Binary search
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = FilteredLogs[mid].Date;

                if (midTime == targetTime)
                    return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            // Find nearest between left and right
            if (left >= FilteredLogs.Count)
                return right;

            DateTime leftTime = FilteredLogs[left].Date;
            DateTime rightTime = FilteredLogs[right].Date;

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff <= rightDiff ? left : right;
        }

        /// <summary>
        /// Gets the log entry at the specified index, or null if out of range.
        /// </summary>
        public LogEntry? GetLogAtIndex(int index)
        {
            if (index >= 0 && index < FilteredLogs.Count)
                return FilteredLogs[index];
            return null;
        }

        #endregion
    }
}
