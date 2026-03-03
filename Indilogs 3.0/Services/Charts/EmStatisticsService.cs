#nullable disable
using IndiLogs_3._0.Models.Charts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IndiLogs_3._0.Services.Charts
{
    /// <summary>
    /// Parses EM_Statistics CSV (Indigo.Infra.EM.Statistics.csv) into StateData for Gantt visualization.
    /// CSV format: Name, StartTime (HH:MM:SS), EndTime (HH:MM:SS), Duration
    /// </summary>
    internal static class EmStatisticsService
    {
        public static (List<StateData> States, DateTime[] Timestamps, int TotalLength) ParseEmStatistics(string csvContent)
        {
            var states = new List<StateData>();
            if (string.IsNullOrWhiteSpace(csvContent))
                return (states, Array.Empty<DateTime>(), 0);

            var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                return (states, Array.Empty<DateTime>(), 0);

            // Collect row data
            var rows = new List<(string Name, TimeSpan Start, TimeSpan End, string Duration)>();

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 3) continue;

                string name = parts[0].Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!TryParseTime(parts[1].Trim().Trim('"'), out var startTime)) continue;
                if (!TryParseTime(parts[2].Trim().Trim('"'), out var endTime)) continue;

                // Ensure end > start (skip invalid rows)
                if (endTime <= startTime) continue;

                string duration = parts.Length >= 4 ? parts[3].Trim().Trim('"') : (endTime - startTime).ToString(@"hh\:mm\:ss");
                rows.Add((name, startTime, endTime, duration));
            }

            if (rows.Count == 0)
                return (states, Array.Empty<DateTime>(), 0);

            // Build a dense 1-second timeline from min start to max end
            // This ensures bars are proportional to actual duration (no distortion)
            var minTime = rows.Min(r => r.Start);
            var maxTime = rows.Max(r => r.End);
            int totalSeconds = (int)(maxTime - minTime).TotalSeconds;
            if (totalSeconds <= 0) totalSeconds = 1;

            int totalLength = totalSeconds + 1; // inclusive of both endpoints
            var baseDate = DateTime.Today;
            var timestamps = new DateTime[totalLength];
            for (int i = 0; i < totalLength; i++)
                timestamps[i] = baseDate.Add(minTime + TimeSpan.FromSeconds(i));

            // Create one StateData per row — map times to dense indices
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int startIdx = (int)(row.Start - minTime).TotalSeconds;
                int endIdx = (int)(row.End - minTime).TotalSeconds;
                startIdx = Math.Max(0, Math.Min(startIdx, totalLength - 1));
                endIdx = Math.Max(0, Math.Min(endIdx, totalLength - 1));

                var stateData = new StateData
                {
                    Name = row.Name,
                    Category = "",
                    Intervals = new List<StateInterval>
                    {
                        new StateInterval
                        {
                            StartIndex = startIdx,
                            EndIndex = endIdx,
                            StateId = r,
                            TooltipText = $"{row.Name}: {row.Start:hh\\:mm\\:ss} → {row.End:hh\\:mm\\:ss} ({row.Duration})"
                        }
                    }
                };
                states.Add(stateData);
            }

            return (states, timestamps, totalLength);
        }

        private static bool TryParseTime(string value, out TimeSpan result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Try HH:MM:SS, H:MM:SS, HH:MM:SS.fff
            if (TimeSpan.TryParseExact(value, new[] { @"hh\:mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss\.fff" },
                CultureInfo.InvariantCulture, out result))
                return true;

            // Fallback: general parse
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result);
        }
    }
}
