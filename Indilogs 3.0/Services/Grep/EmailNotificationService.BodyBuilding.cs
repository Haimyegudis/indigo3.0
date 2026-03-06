using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Grep
{
    public partial class EmailNotificationService
    {
        private string BuildSubject(ScheduledSearch schedule, int matchCount, LogStatisticsResult? stats)
        {
            if (!string.IsNullOrWhiteSpace(schedule.EmailConfig?.CustomSubject))
                return schedule.EmailConfig.CustomSubject;

            var parts = new List<string>();
            if (matchCount > 0)
                parts.Add($"{matchCount:N0} matches");
            else if (schedule.ScanMode == ScanMode.SearchOnly || schedule.ScanMode == ScanMode.SearchAndStatistics)
                parts.Add("No matches");

            if (stats != null)
            {
                int totalLogs = stats.TotalPlcLogs + stats.TotalAppLogs;
                int totalErrors = stats.TotalPlcErrors + stats.TotalAppErrors;
                parts.Add($"{totalLogs:N0} logs analyzed");
                if (totalErrors > 0)
                    parts.Add($"{totalErrors:N0} errors");
            }

            return $"[IndiLogs] {schedule.Name} — {string.Join(", ", parts)}";
        }

        internal static string BuildPlainTextBody(
            ScheduledSearch schedule,
            List<GrepResult>? results,
            LogStatisticsResult? stats)
        {
            var sb = new StringBuilder(8192);
            bool hasResults = results != null && results.Count > 0;
            bool hasStats = stats != null;

            // ═══ HEADER ═══
            sb.AppendLine("========================================================");
            string reportType = hasStats && !hasResults ? "IndiLogs Statistics Report"
                : hasStats ? "IndiLogs Search & Statistics Report"
                : "IndiLogs Search Report";
            sb.AppendLine(reportType);
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Schedule:  {schedule.Name} ({schedule.ScanMode})");
            sb.AppendLine("========================================================");
            sb.AppendLine();

            // ═══ SEARCH RESULTS (first) ═══
            if (hasResults && results != null)
            {
                sb.AppendLine("--- SEARCH RESULTS ---");
                sb.AppendLine($"  Total matches: {results.Count:N0}");
                sb.AppendLine();

                // By Location
                var byLoc = results.GroupBy(r => r.LocationName ?? "(unknown)")
                                   .OrderByDescending(g => g.Count());
                sb.AppendLine("  By Location:");
                foreach (var g in byLoc)
                    sb.AppendLine($"    {g.Key}: {g.Count():N0}");
                sb.AppendLine();

                // By Level
                var byLevel = results
                    .Where(r => r.ReferencedLogEntry?.Level != null)
                    .GroupBy(r => r.ReferencedLogEntry!.Level)
                    .OrderByDescending(g => g.Count());
                if (byLevel.Any())
                {
                    sb.AppendLine("  By Log Level:");
                    foreach (var g in byLevel)
                        sb.AppendLine($"    {g.Key}: {g.Count():N0}");
                    sb.AppendLine();
                }

                // First N results preview
                int previewCount = Math.Min(results.Count, 20);
                sb.AppendLine($"  First {previewCount} results:");
                foreach (var r in results.Take(previewCount))
                {
                    sb.AppendLine($"    [{r.TimestampDisplay}] [{r.LogType}] " +
                                 $"{r.LocationName} - {Truncate(r.PreviewText, 120)}");
                }
                if (results.Count > previewCount)
                    sb.AppendLine($"    ... and {results.Count - previewCount:N0} more (see attached HTML report for full details)");
                sb.AppendLine();
            }

            // ═══ STATISTICS (after search results) ═══
            if (hasStats && stats != null)
            {
                sb.AppendLine("--- LOG STATISTICS OVERVIEW ---");
                sb.AppendLine($"  PLC Logs:   {stats.TotalPlcLogs:N0}");
                sb.AppendLine($"  APP Logs:   {stats.TotalAppLogs:N0}");
                sb.AppendLine($"  PLC Errors: {stats.TotalPlcErrors:N0}");
                sb.AppendLine($"  APP Errors: {stats.TotalAppErrors:N0}");
                if (stats.EarliestTimestamp.HasValue && stats.LatestTimestamp.HasValue)
                {
                    var span = stats.LatestTimestamp.Value - stats.EarliestTimestamp.Value;
                    sb.AppendLine($"  Time span:  {stats.EarliestTimestamp.Value:yyyy-MM-dd HH:mm:ss} -> " +
                                 $"{stats.LatestTimestamp.Value:yyyy-MM-dd HH:mm:ss} ({span.TotalMinutes:F1} min)");
                }
                sb.AppendLine();

                // PLC Top Errors
                if (stats.PlcTopErrors?.Count > 0)
                {
                    sb.AppendLine("--- PLC TOP ERRORS ---");
                    foreach (var e in stats.PlcTopErrors)
                        sb.AppendLine($"  [{e.Count:N0}x] {Truncate(e.Message ?? e.Name, 100)}");
                    sb.AppendLine();
                }

                // PLC Thread Load
                if (stats.PlcThreadLoad?.Count > 0)
                {
                    sb.AppendLine("--- PLC THREAD LOAD ---");
                    foreach (var t in stats.PlcThreadLoad)
                        sb.AppendLine($"  {t.Name}: {t.Count:N0} ({t.Percentage:F1}%)");
                    sb.AppendLine();
                }

                // APP Logger Errors
                if (stats.AppLoggerErrors?.Count > 0)
                {
                    sb.AppendLine("--- APP ERRORS BY LOGGER ---");
                    foreach (var e in stats.AppLoggerErrors)
                        sb.AppendLine($"  [{e.Count:N0}x] {Truncate(e.Message ?? e.Name, 100)}");
                    sb.AppendLine();
                }

                // APP Logger Load
                if (stats.AppLoggerLoad?.Count > 0)
                {
                    sb.AppendLine("--- APP LOGGER LOAD ---");
                    foreach (var l in stats.AppLoggerLoad)
                        sb.AppendLine($"  {l.Name}: {l.Count:N0} ({l.Percentage:F1}%)");
                    sb.AppendLine();
                }

                // APP Method Errors (S6 only)
                if (stats.AppMethodErrors?.Count > 0)
                {
                    sb.AppendLine("--- APP ERRORS BY METHOD ---");
                    foreach (var e in stats.AppMethodErrors)
                        sb.AppendLine($"  [{e.Count:N0}x] {Truncate(e.Message ?? e.Name, 100)}");
                    sb.AppendLine();
                }

                // APP Method Load (S6 only)
                if (stats.AppMethodLoad?.Count > 0)
                {
                    sb.AppendLine("--- APP METHOD LOAD ---");
                    foreach (var l in stats.AppMethodLoad)
                        sb.AppendLine($"  {l.Name}: {l.Count:N0} ({l.Percentage:F1}%)");
                    sb.AppendLine();
                }

                // Errors by Source
                if (stats.ErrorsBySource?.Count > 0)
                {
                    sb.AppendLine("--- ERRORS BY SOURCE ---");
                    foreach (var s in stats.ErrorsBySource)
                        sb.AppendLine($"  {s.Name}: {s.Count:N0}");
                    sb.AppendLine();
                }

                // Errors by State
                if (stats.ErrorsByState?.Count > 0)
                {
                    sb.AppendLine("--- ERRORS BY PRINTER STATE ---");
                    foreach (var s in stats.ErrorsByState)
                        sb.AppendLine($"  {s.Name}: {s.Count:N0}");
                    sb.AppendLine();
                }

                // State Duration Summary (aggregated)
                if (stats.StateEntries?.Count > 0)
                {
                    var stateAgg = stats.StateEntries
                        .Where(s => s.EndTime.HasValue)
                        .GroupBy(s => s.StateName)
                        .Select(g => new
                        {
                            State = g.Key,
                            TotalDuration = TimeSpan.FromTicks(g.Sum(s => (s.EndTime!.Value - s.StartTime).Ticks)),
                            Count = g.Count()
                        })
                        .OrderByDescending(x => x.TotalDuration)
                        .ToList();

                    if (stateAgg.Count > 0)
                    {
                        sb.AppendLine("--- STATE DURATION SUMMARY ---");
                        foreach (var s in stateAgg)
                            sb.AppendLine($"  {s.State}: {s.TotalDuration.TotalSeconds:F1}s total ({s.Count} occurrences)");
                        sb.AppendLine();
                    }
                }

                // Gap Analysis
                if (stats.PlcGaps?.Count > 0 || stats.AppGaps?.Count > 0)
                {
                    sb.AppendLine("--- GAP ANALYSIS (>= 2 seconds) ---");
                    if (stats.PlcGaps?.Count > 0)
                    {
                        sb.AppendLine($"  PLC: {stats.PlcGaps.Count} gap(s)");
                        foreach (var g in stats.PlcGaps.Take(10))
                            sb.AppendLine($"    {g.StartTime:HH:mm:ss} -> {g.EndTime:HH:mm:ss} ({g.DurationText})");
                        if (stats.PlcGaps.Count > 10)
                            sb.AppendLine($"    ... and {stats.PlcGaps.Count - 10} more");
                    }
                    if (stats.AppGaps?.Count > 0)
                    {
                        sb.AppendLine($"  APP: {stats.AppGaps.Count} gap(s)");
                        foreach (var g in stats.AppGaps.Take(10))
                            sb.AppendLine($"    {g.StartTime:HH:mm:ss} -> {g.EndTime:HH:mm:ss} ({g.DurationText})");
                        if (stats.AppGaps.Count > 10)
                            sb.AppendLine($"    ... and {stats.AppGaps.Count - 10} more");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("--------------------------------------------------------");
            sb.AppendLine("See the attached HTML report for full details with formatting.");
            sb.AppendLine("This email was sent by IndiLogs 3.0 scheduled scan.");

            return sb.ToString();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
    }
}
