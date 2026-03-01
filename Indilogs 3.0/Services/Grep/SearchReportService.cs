using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Parameters describing a search for report generation.
    /// </summary>
    public class SearchReportParams
    {
        public List<string> LocationNames { get; set; } = new List<string>();
        public string QueryText { get; set; }
        public string CriteriaSummary { get; set; }
        public string SearchDuration { get; set; }
        public string LogTypes { get; set; }
        public string FileTimeRange { get; set; }
        public string ResultTimeRange { get; set; }
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
            var html = BuildHtml(searchParams, results);
            File.WriteAllText(outputPath, html, Encoding.UTF8);
        }

        private static string BuildHtml(SearchReportParams p, List<GrepResult> results)
        {
            var sb = new StringBuilder(64 * 1024);

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine("<title>IndiLogs Search Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(GetCss());
            sb.AppendLine("</style></head><body>");

            // Header
            sb.AppendLine("<div class=\"header\">");
            sb.AppendLine("<h1>IndiLogs Search Report</h1>");
            sb.AppendLine($"<p class=\"timestamp\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</div>");

            // Search Parameters
            sb.AppendLine("<div class=\"section\"><h2>Search Parameters</h2><table class=\"params\">");
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

            // Summary
            sb.AppendLine("<div class=\"section\"><h2>Summary</h2>");
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

            // Results table
            sb.AppendLine("<div class=\"section\"><h2>Results</h2>");
            if (results.Count == 0)
            {
                sb.AppendLine("<p>No results found.</p>");
            }
            else
            {
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
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void AddParamRow(StringBuilder sb, string label, string value)
        {
            sb.AppendLine($"<tr><td class=\"label\">{Enc(label)}</td><td>{Enc(value)}</td></tr>");
        }

        private static string Enc(string s)
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
";
        }
    }
}
