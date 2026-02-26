using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services.Charts
{
    /// <summary>
    /// Service for synchronizing time positions between charts and log entries
    /// </summary>
    public class ChartSyncService
    {
        // Sorted list of (DateTime, ChartIndex) for fast lookup
        private List<(DateTime Time, int Index)> _timeMap = new List<(DateTime, int)>();

        // Reverse index: chartIndex -> DateTime for O(1) lookup in GetTimeForIndex
        private Dictionary<int, DateTime> _indexToTimeMap = new Dictionary<int, DateTime>();

        // Events for bidirectional synchronization
        public event Action<DateTime> ChartTimeClicked;
        public event Action<int> LogTimeSelected;

        public bool HasMapping => _timeMap.Count > 0;
        public int DataPointCount => _timeMap.Count;

        /// <summary>
        /// Build time mapping from chart data time column
        /// </summary>
        public void BuildTimeMapping(string[] timeStrings)
        {
            _timeMap.Clear();

            for (int i = 0; i < timeStrings.Length; i++)
            {
                if (TryParseTime(timeStrings[i], out DateTime time))
                {
                    _timeMap.Add((time, i));
                }
            }

            // Sort by time for binary search
            _timeMap = _timeMap.OrderBy(x => x.Time).ToList();

            // Build reverse index for O(1) GetTimeForIndex
            _indexToTimeMap = new Dictionary<int, DateTime>(_timeMap.Count);
            foreach (var entry in _timeMap)
                _indexToTimeMap[entry.Index] = entry.Time;
        }

        /// <summary>
        /// Build time mapping from log entries
        /// </summary>
        public void BuildTimeMappingFromLogs(IList<LogEntry> logs)
        {
            _timeMap.Clear();

            for (int i = 0; i < logs.Count; i++)
            {
                if (logs[i].Date != default)
                {
                    _timeMap.Add((logs[i].Date, i));
                }
            }

            // Sort by time for binary search
            _timeMap = _timeMap.OrderBy(x => x.Time).ToList();

            // Build reverse index for O(1) GetTimeForIndex
            _indexToTimeMap = new Dictionary<int, DateTime>(_timeMap.Count);
            foreach (var entry in _timeMap)
                _indexToTimeMap[entry.Index] = entry.Time;
        }

        /// <summary>
        /// Find the nearest chart index for a given log time
        /// </summary>
        public int FindChartIndex(DateTime logTime)
        {
            if (_timeMap.Count == 0) return 0;

            // Binary search for the closest time
            int left = 0;
            int right = _timeMap.Count - 1;

            while (left < right)
            {
                int mid = (left + right) / 2;
                if (_timeMap[mid].Time < logTime)
                {
                    left = mid + 1;
                }
                else
                {
                    right = mid;
                }
            }

            // Check if the previous element is closer
            if (left > 0)
            {
                var prev = _timeMap[left - 1];
                var curr = _timeMap[left];

                if (Math.Abs((prev.Time - logTime).TotalMilliseconds) <
                    Math.Abs((curr.Time - logTime).TotalMilliseconds))
                {
                    return prev.Index;
                }
            }

            return _timeMap[left].Index;
        }

        /// <summary>
        /// Get the time for a given chart index - O(1) via dictionary, O(log N) fallback via binary search
        /// </summary>
        public DateTime GetTimeForIndex(int chartIndex)
        {
            // O(1) direct lookup
            if (_indexToTimeMap.TryGetValue(chartIndex, out DateTime time))
                return time;

            if (_timeMap.Count == 0) return DateTime.MinValue;

            // O(log N) binary search for closest index
            int left = 0, right = _timeMap.Count - 1;
            while (left < right)
            {
                int mid = (left + right) / 2;
                if (_timeMap[mid].Index < chartIndex)
                    left = mid + 1;
                else
                    right = mid;
            }

            // Check if previous entry is closer
            if (left > 0)
            {
                int diffPrev = Math.Abs(_timeMap[left - 1].Index - chartIndex);
                int diffCurr = Math.Abs(_timeMap[left].Index - chartIndex);
                if (diffPrev < diffCurr)
                    return _timeMap[left - 1].Time;
            }

            return _timeMap[left].Time;
        }

        /// <summary>
        /// Get the time range covered by the data
        /// </summary>
        public (DateTime Start, DateTime End) GetTimeRange()
        {
            if (_timeMap.Count == 0)
                return (DateTime.MinValue, DateTime.MinValue);

            return (_timeMap.First().Time, _timeMap.Last().Time);
        }

        /// <summary>
        /// Notify that a time was clicked on the chart
        /// </summary>
        public void NotifyChartTimeClicked(int chartIndex)
        {
            var time = GetTimeForIndex(chartIndex);
            if (time != DateTime.MinValue)
            {
                ChartTimeClicked?.Invoke(time);
            }
        }

        /// <summary>
        /// Notify that a log entry was selected
        /// </summary>
        public void NotifyLogTimeSelected(DateTime logTime)
        {
            int index = FindChartIndex(logTime);
            LogTimeSelected?.Invoke(index);
        }

        /// <summary>
        /// Try to parse various time formats
        /// </summary>
        private bool TryParseTime(string timeStr, out DateTime result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(timeStr)) return false;

            // Try ISO 8601 format (2024-01-15T10:30:45.123)
            if (DateTime.TryParse(timeStr, out result))
                return true;

            // Try numeric formats (milliseconds, seconds, etc.)
            if (double.TryParse(timeStr, out double numericValue))
            {
                // Assume milliseconds since epoch if large number
                if (numericValue > 1e12)
                {
                    result = DateTimeOffset.FromUnixTimeMilliseconds((long)numericValue).DateTime;
                    return true;
                }
                // Assume seconds since epoch
                else if (numericValue > 1e9)
                {
                    result = DateTimeOffset.FromUnixTimeSeconds((long)numericValue).DateTime;
                    return true;
                }
                // Small number - treat as relative time in seconds from a base
                else
                {
                    result = DateTime.Today.AddSeconds(numericValue);
                    return true;
                }
            }

            // Try common date formats
            string[] formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ss.ffffff", "yyyy-MM-dd HH:mm:ss.fffff",
                "yyyy-MM-dd HH:mm:ss.ffff",    "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm:ss",
                "HH:mm:ss.fffffff", "HH:mm:ss.ffffff", "HH:mm:ss.fffff",
                "HH:mm:ss.ffff",    "HH:mm:ss.fff",
                "HH:mm:ss"
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(timeStr, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out result))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Format a time for display on the X-axis
        /// </summary>
        public string FormatTimeForDisplay(int chartIndex)
        {
            var time = GetTimeForIndex(chartIndex);
            if (time == DateTime.MinValue) return chartIndex.ToString();

            // If time span is less than a day, show time only
            var range = GetTimeRange();
            if ((range.End - range.Start).TotalHours < 24)
            {
                return time.ToString("HH:mm:ss");
            }

            return time.ToString("MM/dd HH:mm");
        }

        /// <summary>
        /// Clear all mappings
        /// </summary>
        public void Clear()
        {
            _timeMap.Clear();
            _indexToTimeMap.Clear();
        }
    }
}
