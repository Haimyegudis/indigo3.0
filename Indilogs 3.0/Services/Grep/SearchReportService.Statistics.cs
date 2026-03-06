using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Grep
{
    public static partial class SearchReportService
    {
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
                        TotalDuration = TimeSpan.FromTicks(g.Sum(s => (s.EndTime!.Value - s.StartTime).Ticks)),
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
                if (hasPlcGaps && stats.PlcGaps != null)
                {
                    sb.AppendLine($"<h3>PLC Gaps ({stats.PlcGaps.Count})</h3>");
                    AppendGapTable(sb, stats.PlcGaps);
                }
                if (hasAppGaps && stats.AppGaps != null)
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
                sb.AppendLine($"<tr><td>{g.Index}</td><td><strong>{Enc(g.DurationText)}</strong></td><td>{g.StartTime:HH:mm:ss.ffffff}</td><td>{g.EndTime:HH:mm:ss.ffffff}</td><td>{Enc(LogStatisticsService.TruncateMessage(g.LastMessageBeforeGap ?? "", 80))}</td></tr>");
            }
            sb.AppendLine("</table>");
        }
    }
}
