using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IEmailNotificationService : IDisposable
    {
        void ProcessScanResult(
            ScheduledSearch schedule,
            List<GrepResult> results,
            LogStatisticsResult stats,
            string htmlReportPath,
            bool forceImmediate = false);

        Task<(bool Success, string Message)> TestConnectionAsync(
            EmailNotificationConfig config, string recipient);
    }
}
