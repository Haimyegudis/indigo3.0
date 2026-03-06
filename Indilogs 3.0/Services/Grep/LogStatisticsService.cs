using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Computes log statistics (error histograms, load distributions, gap analysis, state errors).
    /// Algorithms extracted from StatsWindow for reuse in scheduled/on-demand scans.
    /// </summary>
    public static partial class LogStatisticsService
    {
        private static readonly HashSet<string> ErrorLevels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Error", "Fatal" };



        // ====================================================================
        //  Main entry point
        // ====================================================================

        /// <summary>
        /// Computes all statistics from pre-loaded PLC and APP log lists.
        /// </summary>
        public static LogStatisticsResult ComputeStatistics(
            List<LogEntry> plcLogs, List<LogEntry> appLogs, bool hasBinaryAppLogs = false)
        {
            var result = new LogStatisticsResult
            {
                TotalPlcLogs = plcLogs.Count,
                TotalAppLogs = appLogs.Count,
                HasBinaryAppLogs = hasBinaryAppLogs
            };

            // Time span
            DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
            for (int i = 0; i < plcLogs.Count; i++)
            {
                if (plcLogs[i].Date < min) min = plcLogs[i].Date;
                if (plcLogs[i].Date > max) max = plcLogs[i].Date;
            }
            for (int i = 0; i < appLogs.Count; i++)
            {
                if (appLogs[i].Date < min) min = appLogs[i].Date;
                if (appLogs[i].Date > max) max = appLogs[i].Date;
            }
            if (min < DateTime.MaxValue)
            {
                result.EarliestTimestamp = min;
                result.LatestTimestamp = max;
            }

            // Error logs (cached)
            var plcErrors = GetErrorLogs(plcLogs);
            var appErrors = GetErrorLogs(appLogs);
            result.TotalPlcErrors = plcErrors.Count;
            result.TotalAppErrors = appErrors.Count;

            // PLC statistics
            if (plcLogs.Count > 0)
            {
                result.PlcTopErrors = CalculateErrorHistogram(plcErrors, 10);
                result.PlcThreadLoad = CalculateLoadDistribution(plcLogs, l => l.ThreadName, 10);
                result.PlcGaps = FindGaps(plcLogs);
            }

            // APP statistics
            if (appLogs.Count > 0)
            {
                result.AppLoggerErrors = CalculateErrorHistogram(appErrors, 10, l => GetShortLoggerName(l.Logger));
                result.AppLoggerLoad = CalculateLoadDistribution(appLogs, l => GetShortLoggerName(l.Logger), 15, l => l.Logger);

                if (!hasBinaryAppLogs)
                {
                    result.AppMethodErrors = CalculateErrorHistogram(appErrors, 10, l => l.Method ?? "(unknown)");
                    result.AppMethodLoad = CalculateLoadDistribution(appLogs, l => l.Method ?? "(unknown)", 15);
                }

                result.AppGaps = FindGaps(appLogs);
            }

            // Advanced analytics
            var allErrors = new List<LogEntry>(plcErrors.Count + appErrors.Count);
            allErrors.AddRange(plcErrors);
            allErrors.AddRange(appErrors);

            if (allErrors.Count > 0)
            {
                // Errors by source (PLC by thread, APP by logger)
                result.ErrorsBySource = BuildErrorsBySource(plcErrors, appErrors);

                // State entries + errors by state
                result.StateEntries = CalculateStateEntries(plcLogs);
                if (result.StateEntries.Count > 0)
                    result.ErrorsByState = MapErrorsToStates(plcErrors, result.StateEntries);
            }

            return result;
        }
    }
}
