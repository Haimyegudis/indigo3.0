using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Parameters describing a search for report generation.
    /// </summary>
    public class SearchReportParams
    {
        public List<string> LocationNames { get; set; } = new List<string>();
        public string QueryText { get; set; } = "";
        public string CriteriaSummary { get; set; } = "";
        public string SearchDuration { get; set; } = "";
        public string LogTypes { get; set; } = "";
        public string FileTimeRange { get; set; } = "";
        public string ResultTimeRange { get; set; } = "";
    }

    /// <summary>
    /// Generates HTML search reports with search parameters, summary statistics, and results.
    /// </summary>
    public static class SearchReportService
    {
        /// <summary>
        /// Generates an HTML report and saves it to the specified file path.
        /// </summary>
        public static void GenerateHtmlReport(string outputPath, SearchReportParams searchParams, List<GrepResult> results)
        {
            var html = BuildHtml(searchParams, results, null);
            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        /// <summary>
        /// Generates an HTML report with optional statistics sections.
        /// </summary>
        public static void GenerateHtmlReport(string outputPath, SearchReportParams searchParams, List<GrepResult> results, LogStatisticsResult stats)
        {
            var html = BuildHtml(searchParams, results, stats);
            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        /// <summary>
        /// Generates a statistics-only HTML report (no search results).
        /// </summary>
        public static void GenerateStatisticsReport(string outputPath, SearchReportParams searchParams, LogStatisticsResult stats)
        {
            var html = BuildHtml(searchParams, null, stats);
            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        private static string BuildHtml(SearchReportParams p, List<GrepResult>? results, LogStatisticsResult? stats)
        {
            var sb = new StringBuilder(64 * 1024);
            bool hasResults = results != null && results.Count > 0;
            bool hasStats = stats != null;
            string reportTitle = hasStats && !hasResults ? "IndiLogs Statistics Report"
                : hasStats ? "IndiLogs Search & Statistics Report"
                : "IndiLogs Search Report";

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>{reportTitle}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(GetCss());
            sb.AppendLine("</style></head><body>");

            // Header
            sb.AppendLine("<div class=\"header\">");
            sb.AppendLine($"<h1>{reportTitle}</h1>");
            sb.AppendLine($"<p class=\"timestamp\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</div>");

            // Search Parameters
            sb.AppendLine("<div class=\"section\"><h2>Scan Parameters</h2><table class=\"params\">");
            AddParamRow(sb, "Locations", p.LocationNames.Count > 0 ? string.Join(", ", p.LocationNames) : "(none)");
            if (!string.IsNullOrWhiteSpace(p.QueryText))
                AddParamRow(sb, "Query", p.QueryText);
            if (!string.IsNullOrWhiteSpace(p.CriteriaSummary))
                AddParamRow(sb, "Criteria", p.CriteriaSummary);
            AddParamRow(sb, "Log Types", p.LogTypes ?? "PLC + APP");
            if (!string.IsNullOrWhiteSpace(p.FileTimeRange))
                AddParamRow(sb, "File Date Range", p.FileTimeRange);
            if (!string.IsNullOrWhiteSpace(p.ResultTimeRange))
                AddParamRow(sb, "Result Time Range", p.ResultTimeRange);
            AddParamRow(sb, "Duration", p.SearchDuration ?? "N/A");
            sb.AppendLine("</table></div>");

            // Search Results Summary (shown first, before statistics)
            if (results == null) results = new List<GrepResult>();
            if (hasResults || !hasStats)
            {
            sb.AppendLine("<div class=\"section\"><h2>Search Results Summary</h2>");
            sb.AppendLine($"<p class=\"total\">Total matches: <strong>{results.Count:N0}</strong></p>");

            // Matches per location
            var byLocation = results.GroupBy(r => r.LocationName ?? "(loaded session)")
                                    .OrderByDescending(g => g.Count()).ToList();
            if (byLocation.Count > 0)
            {
                sb.AppendLine("<h3>Matches by Location</h3><table class=\"summary\"><tr><th>Location</th><th>Count</th></tr>");
                foreach (var g in byLocation)
                    sb.AppendLine($"<tr><td>{Enc(g.Key)}</td><td>{g.Count():N0}</td></tr>");
                sb.AppendLine("</table>");
            }

            // Matches per file
            var byFile = results.GroupBy(r => r.SessionName ?? "(unknown)")
                                .OrderByDescending(g => g.Count()).ToList();
            if (byFile.Count > 0)
            {
                sb.AppendLine("<h3>Matches by File</h3><table class=\"summary\"><tr><th>File</th><th>Path</th><th>Count</th></tr>");
                foreach (var g in byFile)
                {
                    var first = g.First();
                    string filePath = first.FilePath ?? "";
                    string fileLink = !string.IsNullOrEmpty(filePath)
                        ? $"<a href=\"file:///{Enc(filePath.Replace('\\', '/'))}\" title=\"{Enc(filePath)}\">{Enc(g.Key)}</a>"
                        : Enc(g.Key);
                    sb.AppendLine($"<tr><td>{fileLink}</td><td class=\"path\">{Enc(filePath)}</td><td>{g.Count():N0}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            // Matches per level
            var byLevel = results.Where(r => r.ReferencedLogEntry?.Level != null)
                                 .GroupBy(r => r.ReferencedLogEntry.Level)
                                 .OrderByDescending(g => g.Count()).ToList();
            if (byLevel.Count > 0)
            {
                sb.AppendLine("<h3>Matches by Log Level</h3><table class=\"summary\"><tr><th>Level</th><th>Count</th></tr>");
                foreach (var g in byLevel)
                {
                    string levelClass = g.Key.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-error"
                        : g.Key.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-warn"
                        : g.Key.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-fatal"
                        : "";
                    sb.AppendLine($"<tr><td class=\"{levelClass}\">{Enc(g.Key)}</td><td>{g.Count():N0}</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</div>");
            } // end if (hasResults || !hasStats)

            // Results detail table
            if (hasResults)
            {
            sb.AppendLine("<div class=\"section\"><h2>Search Results</h2>");
            sb.AppendLine("<table class=\"results\"><thead><tr>");
            sb.AppendLine("<th>#</th><th>Timestamp</th><th>Location</th><th>File</th><th>Lvl</th><th>Matched In</th><th>Thread</th><th>Logger</th><th>Method</th><th>Message</th>");
            sb.AppendLine("</tr></thead><tbody>");

            int rowNum = 0;
            foreach (var r in results)
            {
                rowNum++;
                var e = r.ReferencedLogEntry;
                string rowClass = rowNum % 2 == 0 ? "even" : "odd";
                string level = e?.Level ?? "";
                string levelClass = level.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-error"
                    : level.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-warn"
                    : level.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0 ? "lvl-fatal"
                    : "";

                string filePath = r.FilePath ?? "";
                string fileCell = !string.IsNullOrEmpty(filePath)
                    ? $"<a href=\"file:///{Enc(filePath.Replace('\\', '/'))}\" title=\"{Enc(filePath)}\">{Enc(r.SessionName)}</a>"
                    : Enc(r.SessionName);

                sb.AppendLine($"<tr class=\"{rowClass}\">");
                sb.Append($"<td>{rowNum}</td>");
                sb.Append($"<td class=\"ts\">{Enc(r.TimestampDisplay)}</td>");
                sb.Append($"<td>{Enc(r.LocationName)}</td>");
                sb.Append($"<td>{fileCell}</td>");
                sb.Append($"<td class=\"{levelClass}\">{Enc(level)}</td>");
                sb.Append($"<td>{Enc(r.MatchedField)}</td>");
                sb.Append($"<td>{Enc(e?.ThreadName)}</td>");
                sb.Append($"<td>{Enc(e?.Logger)}</td>");
                sb.Append($"<td>{Enc(e?.Method)}</td>");
                sb.Append($"<td class=\"msg\">{Enc(e?.Message)}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
            } // end if (hasResults)

            // Statistics sections (after search results)
            if (hasStats)
                AppendStatisticsHtml(sb, stats);

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // ====================================================================
        //  Statistics HTML generation
        // ====================================================================

        private static void AppendStatisticsHtml(StringBuilder sb, LogStatisticsResult stats)
        {
            // Overview
            sb.AppendLine("<div class=\"section\"><h2>Log Statistics Overview</h2>");
            sb.AppendLine("<div class=\"stats-grid\">");
            AppendStatCard(sb, "PLC Logs", $"{stats.TotalPlcLogs:N0}");
            AppendStatCard(sb, "APP Logs", $"{stats.TotalAppLogs:N0}");
            AppendStatCard(sb, "PLC Errors", $"{stats.TotalPlcErrors:N0}", stats.TotalPlcErrors > 0 ? "error" : null);
            AppendStatCard(sb, "APP Errors", $"{stats.TotalAppErrors:N0}", stats.TotalAppErrors > 0 ? "error" : null);
            sb.AppendLine("</div>");
            if (stats.EarliestTimestamp.HasValue && stats.LatestTimestamp.HasValue)
            {
                var span = stats.LatestTimestamp.Value - stats.EarliestTimestamp.Value;
                sb.AppendLine($"<p style=\"margin-top:8px;color:#a0a0cc;\">Time span: {stats.EarliestTimestamp.Value:yyyy-MM-dd HH:mm:ss} — {stats.LatestTimestamp.Value:yyyy-MM-dd HH:mm:ss} ({span.TotalMinutes:F1} minutes)</p>");
            }
            sb.AppendLine("</div>");

            // PLC Error Histogram
            if (stats.PlcTopErrors != null && stats.PlcTopErrors.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>PLC — Top Errors</h2>");
                AppendErrorHistogramTable(sb, stats.PlcTopErrors);
                sb.AppendLine("</div>");
            }

            // PLC Thread Load
            if (stats.PlcThreadLoad != null && stats.PlcThreadLoad.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>PLC — Thread Load Distribution</h2>");
                AppendLoadDistributionTable(sb, stats.PlcThreadLoad);
                sb.AppendLine("</div>");
            }

            // APP Logger Errors
            if (stats.AppLoggerErrors != null && stats.AppLoggerErrors.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>APP — Errors by Logger</h2>");
                AppendErrorHistogramTable(sb, stats.AppLoggerErrors);
                sb.AppendLine("</div>");
            }

            // APP Logger Load
            if (stats.AppLoggerLoad != null && stats.AppLoggerLoad.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>APP — Logger Load Distribution</h2>");
                AppendLoadDistributionTable(sb, stats.AppLoggerLoad);
                sb.AppendLine("</div>");
            }

            // APP Method Errors (S6 only)
            if (!stats.HasBinaryAppLogs && stats.AppMethodErrors != null && stats.AppMethodErrors.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>APP — Errors by Method</h2>");
                AppendErrorHistogramTable(sb, stats.AppMethodErrors);
                sb.AppendLine("</div>");
            }

            // APP Method Load (S6 only)
            if (!stats.HasBinaryAppLogs && stats.AppMethodLoad != null && stats.AppMethodLoad.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>APP — Method Load Distribution</h2>");
                AppendLoadDistributionTable(sb, stats.AppMethodLoad);
                sb.AppendLine("</div>");
            }

            // Errors by Source (combined PLC+APP)
            if (stats.ErrorsBySource != null && stats.ErrorsBySource.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>Errors by Source (PLC Thread / APP Logger)</h2>");
                sb.AppendLine("<table class=\"summary\"><tr><th>Source</th><th>Count</th><th>Distribution</th></tr>");
                int maxSrc = stats.ErrorsBySource.Max(x => x.Count);
                foreach (var item in stats.ErrorsBySource)
                {
                    double pct = maxSrc > 0 ? (double)item.Count / maxSrc * 100 : 0;
                    sb.AppendLine($"<tr><td>{Enc(item.Name)}</td><td>{item.Count:N0}</td><td><div class=\"bar\" style=\"width:{pct:F0}%\"></div></td></tr>");
                }
                sb.AppendLine("</table></div>");
            }

            // Errors by State
            if (stats.ErrorsByState != null && stats.ErrorsByState.Count > 0)
            {
                sb.AppendLine("<div class=\"section\"><h2>Errors by Printer State</h2>");
                sb.AppendLine("<table class=\"summary\"><tr><th>State</th><th>Errors</th><th>Distribution</th></tr>");
                int maxState = stats.ErrorsByState.Max(x => x.Count);
                foreach (var item in stats.ErrorsByState)
                {
                    double pct = maxState > 0 ? (double)item.Count / maxState * 100 : 0;
                    sb.AppendLine($"<tr><td><strong>{Enc(item.Name)}</strong></td><td>{item.Count:N0}</td><td><div class=\"bar bar-state\" style=\"width:{pct:F0}%\"></div></td></tr>");
                }
                sb.AppendLine("</table></div>");
            }

            // State Duration Summary (aggregated across all sessions)
            if (stats.StateEntries != null && stats.StateEntries.Count > 0)
            {
                var stateAgg = stats.StateEntries
                    .Where(s => s.EndTime.HasValue)
                    .GroupBy(s => s.StateName)
                    .Select(g => new
                    {
                        State = g.Key,
                        TotalDuration = TimeSpan.FromTicks(g.Sum(s => (s.EndTime.Value - s.StartTime).Ticks)),
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.TotalDuration)
                    .ToList();

                if (stateAgg.Count > 0)
                {
                    double maxTicks = stateAgg.Max(x => x.TotalDuration.Ticks);
                    sb.AppendLine("<div class=\"section\"><h2>State Duration Summary</h2>");
                    sb.AppendLine("<table class=\"summary\"><tr><th>State</th><th>Occurrences</th><th>Total Duration</th><th>Distribution</th></tr>");
                    foreach (var s in stateAgg)
                    {
                        double pct = maxTicks > 0 ? s.TotalDuration.Ticks / maxTicks * 100 : 0;
                        sb.AppendLine($"<tr><td><strong>{Enc(s.State)}</strong></td><td>{s.Count}</td><td>{LogStatisticsService.FormatDuration(s.TotalDuration)}</td><td><div class=\"bar bar-state\" style=\"width:{pct:F0}%\"></div></td></tr>");
                    }
                    sb.AppendLine("</table></div>");
                }
            }

            // Gap Analysis (collapsible — hidden by default)
            bool hasPlcGaps = stats.PlcGaps != null && stats.PlcGaps.Count > 0;
            bool hasAppGaps = stats.AppGaps != null && stats.AppGaps.Count > 0;
            if (hasPlcGaps || hasAppGaps)
            {
                int totalGaps = (stats.PlcGaps?.Count ?? 0) + (stats.AppGaps?.Count ?? 0);
                sb.AppendLine("<div class=\"section\">");
                sb.AppendLine($"<details><summary class=\"collapsible-header\"><h2 style=\"display:inline;\">Gap Analysis (>= 2 seconds)</h2> <span class=\"gap-count\">{totalGaps} gap(s)</span></summary>");
                sb.AppendLine("<div class=\"collapsible-content\">");
                if (hasPlcGaps)
                {
                    sb.AppendLine($"<h3>PLC Gaps ({stats.PlcGaps.Count})</h3>");
                    AppendGapTable(sb, stats.PlcGaps);
                }
                if (hasAppGaps)
                {
                    sb.AppendLine($"<h3>APP Gaps ({stats.AppGaps.Count})</h3>");
                    AppendGapTable(sb, stats.AppGaps);
                }
                sb.AppendLine("</div></details>");
                sb.AppendLine("</div>");
            }
        }

        private static void AppendStatCard(StringBuilder sb, string label, string value, string? variant = null)
        {
            string cls = variant == "error" ? "stat-card stat-error" : "stat-card";
            sb.AppendLine($"<div class=\"{cls}\"><div class=\"stat-value\">{value}</div><div class=\"stat-label\">{label}</div></div>");
        }

        private static void AppendErrorHistogramTable(StringBuilder sb, List<ErrorStat> items)
        {
            sb.AppendLine("<table class=\"summary\"><tr><th>Error / Key</th><th>Count</th><th>Distribution</th></tr>");
            int maxCount = items.Count > 0 ? items.Max(x => x.Count) : 1;
            foreach (var item in items)
            {
                double pct = maxCount > 0 ? (double)item.Count / maxCount * 100 : 0;
                sb.AppendLine($"<tr><td class=\"lvl-error\" title=\"{Enc(item.Message)}\">{Enc(LogStatisticsService.TruncateMessage(item.Name, 80))}</td><td>{item.Count:N0}</td><td><div class=\"bar bar-error\" style=\"width:{pct:F0}%\"></div></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        private static void AppendLoadDistributionTable(StringBuilder sb, List<LoadStat> items)
        {
            sb.AppendLine("<table class=\"summary\"><tr><th>Name</th><th>Count</th><th>%</th><th>Distribution</th></tr>");
            int maxCount = items.Count > 0 ? items.Max(x => x.Count) : 1;
            foreach (var item in items)
            {
                double pct = maxCount > 0 ? (double)item.Count / maxCount * 100 : 0;
                sb.AppendLine($"<tr><td title=\"{Enc(item.FullName)}\">{Enc(item.Name)}</td><td>{item.Count:N0}</td><td>{item.Percentage:F1}%</td><td><div class=\"bar bar-load\" style=\"width:{pct:F0}%\"></div></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        private static void AppendGapTable(StringBuilder sb, List<GapInfo> gaps)
        {
            sb.AppendLine("<table class=\"summary\"><tr><th>#</th><th>Duration</th><th>Start</th><th>End</th><th>Last Log Before Gap</th></tr>");
            foreach (var g in gaps)
            {
                sb.AppendLine($"<tr><td>{g.Index}</td><td><strong>{Enc(g.DurationText)}</strong></td><td>{g.StartTime:HH:mm:ss.ffffff}</td><td>{g.EndTime:HH:mm:ss.ffffff}</td><td>{Enc(LogStatisticsService.TruncateMessage(g.LastMessageBeforeGap, 80))}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        private static void AddParamRow(StringBuilder sb, string label, string value)
        {
            sb.AppendLine($"<tr><td class=\"label\">{Enc(label)}</td><td>{Enc(value)}</td></tr>");
        }

        private static string Enc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string GetCss()
        {
            return @"
* { margin: 0; padding: 0; box-sizing: border-box; }
body { font-family: 'Segoe UI Variable Display', 'Segoe UI', sans-serif; background: #1a1a2e; color: #e0e0e0; padding: 24px; }
.header { text-align: center; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 2px solid #3a3a5c; }
.header h1 { font-size: 24px; color: #7c83ff; margin-bottom: 4px; }
.header .timestamp { font-size: 13px; color: #888; }
.section { background: #22223a; border: 1px solid #3a3a5c; border-radius: 8px; padding: 16px; margin-bottom: 16px; }
h2 { font-size: 16px; color: #7c83ff; margin-bottom: 12px; border-bottom: 1px solid #3a3a5c; padding-bottom: 6px; }
h3 { font-size: 14px; color: #a0a0cc; margin: 12px 0 6px 0; }
.total { font-size: 18px; margin-bottom: 12px; }
table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
th { background: #2a2a4a; color: #7c83ff; text-align: left; padding: 6px 10px; font-size: 12px; border-bottom: 2px solid #3a3a5c; position: sticky; top: 0; }
td { padding: 5px 10px; font-size: 12px; border-bottom: 1px solid #2a2a4a; vertical-align: top; }
.params td.label { font-weight: 600; color: #a0a0cc; width: 140px; }
.summary td, .summary th { padding: 4px 10px; }
.results { font-family: Consolas, 'Courier New', monospace; font-size: 11px; }
.results td { max-width: 400px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.results td.msg { max-width: 600px; }
.results td.ts { white-space: nowrap; }
.path { font-size: 10px; color: #888; max-width: 300px; overflow: hidden; text-overflow: ellipsis; }
tr.odd { background: #1e1e36; }
tr.even { background: #252545; }
tr:hover { background: #303060; }
a { color: #7c83ff; text-decoration: none; }
a:hover { text-decoration: underline; color: #9da3ff; }
.lvl-error { color: #ff6b6b; font-weight: bold; }
.lvl-warn { color: #ffd93d; font-weight: bold; }
.lvl-fatal { color: #ff4444; font-weight: bold; background: #3a1a1a; }
.stats-grid { display: flex; gap: 12px; flex-wrap: wrap; }
.stat-card { background: #2a2a4a; border: 1px solid #3a3a5c; border-radius: 8px; padding: 12px 20px; min-width: 140px; text-align: center; }
.stat-card .stat-value { font-size: 28px; font-weight: bold; color: #7c83ff; }
.stat-card .stat-label { font-size: 12px; color: #a0a0cc; margin-top: 2px; }
.stat-error .stat-value { color: #ff6b6b; }
.bar { height: 16px; border-radius: 3px; background: #3498DB; min-width: 2px; }
.bar-error { background: #E74C3C; }
.bar-load { background: #3498DB; }
.bar-state { background: #9B59B6; }
details summary { cursor: pointer; list-style: none; }
details summary::-webkit-details-marker { display: none; }
.collapsible-header { padding: 4px 0; user-select: none; }
.collapsible-header::before { content: '\u25B6'; display: inline-block; margin-right: 8px; font-size: 12px; color: #7c83ff; transition: transform 0.2s; }
details[open] .collapsible-header::before { transform: rotate(90deg); }
.collapsible-content { margin-top: 10px; }
.gap-count { font-size: 13px; color: #a0a0cc; font-weight: normal; margin-left: 8px; }
";
        }
    }
}
