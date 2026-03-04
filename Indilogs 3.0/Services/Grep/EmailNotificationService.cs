#pragma warning disable SYSLIB0014 // SmtpClient is deprecated but functional
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Grep
{
    public class EmailNotificationService : IDisposable
    {
        private readonly ConcurrentQueue<DeferredEmail> _deferredQueue
            = new ConcurrentQueue<DeferredEmail>();
        private readonly Timer _deferredTimer;

        public EmailNotificationService()
        {
            _deferredTimer = new Timer(60_000);
            _deferredTimer.Elapsed += OnDeferredTimerElapsed;
            _deferredTimer.AutoReset = true;
            _deferredTimer.Start();
        }

        /// <summary>
        /// Evaluates send conditions and either sends immediately or enqueues for deferred delivery.
        /// </summary>
        public void ProcessScanResult(
            ScheduledSearch schedule,
            List<GrepResult> results,
            LogStatisticsResult stats,
            string htmlReportPath,
            bool forceImmediate = false)
        {
            var config = schedule.EmailConfig;
            if (config == null || !config.IsEnabled) return;
            if (config.Recipients == null || config.Recipients.Count == 0) return;

            bool doSearch = schedule.ScanMode == ScanMode.SearchOnly
                         || schedule.ScanMode == ScanMode.SearchAndStatistics;
            bool doStats = schedule.ScanMode == ScanMode.StatisticsOnly
                         || schedule.ScanMode == ScanMode.SearchAndStatistics;

            int matchCount = results?.Count ?? 0;

            // Send conditions:
            // - SearchOnly: send only if matches > 0
            // - StatisticsOnly: always send
            // - SearchAndStatistics: send if matches > 0 OR stats exist
            if (doSearch && !doStats && matchCount == 0) return;

            string subject = BuildSubject(schedule, matchCount, stats);
            string plainTextBody = BuildPlainTextBody(schedule, results, stats);

            if (!forceImmediate && config.Timing == EmailTiming.AtSpecificTime)
            {
                _deferredQueue.Enqueue(new DeferredEmail
                {
                    Config = config,
                    Subject = subject,
                    PlainTextBody = plainTextBody,
                    HtmlReportPath = htmlReportPath,
                    ScheduleName = schedule.Name,
                    SendTime = config.SendTime,
                    QueuedAt = DateTime.Now
                });
                AppLogger.Info($"[Email] Deferred email for \"{schedule.Name}\" — will send at {config.SendTime:hh\\:mm}");
            }
            else
            {
                Task.Run(() => SendEmailAsync(config, subject, plainTextBody, htmlReportPath, schedule.Name));
            }
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(
            EmailNotificationConfig config, string testRecipient)
        {
            try
            {
                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(config.FromAddress, config.FromDisplayName ?? "IndiLogs 3.0");
                    msg.To.Add(testRecipient);
                    msg.Subject = "[IndiLogs] Test Email";
                    msg.Body = "This is a test email from IndiLogs 3.0 scheduled scan notifications.\r\n\r\n" +
                               $"SMTP: {config.SmtpHost}:{config.SmtpPort} (SSL: {config.UseSsl})\r\n" +
                               $"Sent at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                    using (var client = new SmtpClient(config.SmtpHost, config.SmtpPort))
                    {
                        ConfigureSmtpAuth(client, config);
                        client.Timeout = 15_000;

                        await client.SendMailAsync(msg);
                    }
                }
                return (true, "Test email sent successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed: {ex.Message}");
            }
        }

        private async Task SendEmailAsync(
            EmailNotificationConfig config,
            string subject,
            string plainTextBody,
            string htmlReportPath,
            string scheduleName)
        {
            try
            {
                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(config.FromAddress, config.FromDisplayName ?? "IndiLogs 3.0");
                    foreach (var recipient in config.Recipients)
                        msg.To.Add(recipient.Trim());

                    msg.Subject = subject;
                    msg.Body = plainTextBody;
                    msg.IsBodyHtml = false;

                    if (!string.IsNullOrEmpty(htmlReportPath) && File.Exists(htmlReportPath))
                        msg.Attachments.Add(new Attachment(htmlReportPath));

                    using (var client = new SmtpClient(config.SmtpHost, config.SmtpPort))
                    {
                        ConfigureSmtpAuth(client, config);
                        client.Timeout = 30_000;

                        await client.SendMailAsync(msg);
                    }
                }
                AppLogger.Info($"[Email] Sent notification for \"{scheduleName}\" to {config.Recipients.Count} recipient(s)");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Email] Failed to send for \"{scheduleName}\": {ex.Message}");
            }
        }

        private static void ConfigureSmtpAuth(SmtpClient client, EmailNotificationConfig config)
        {
            client.EnableSsl = config.UseSsl;

            switch (config.AuthMode)
            {
                case SmtpAuthMode.WindowsIntegrated:
                    client.UseDefaultCredentials = true;
                    break;

                case SmtpAuthMode.UsernamePassword:
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword);
                    break;

                default: // None
                    client.UseDefaultCredentials = false;
                    break;
            }
        }

        private string BuildSubject(ScheduledSearch schedule, int matchCount, LogStatisticsResult stats)
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
            List<GrepResult> results,
            LogStatisticsResult stats)
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
            if (hasResults)
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
                    .GroupBy(r => r.ReferencedLogEntry.Level)
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
            if (hasStats)
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
                            TotalDuration = TimeSpan.FromTicks(g.Sum(s => (s.EndTime.Value - s.StartTime).Ticks)),
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

        private void OnDeferredTimerElapsed(object sender, ElapsedEventArgs e) => _ = OnDeferredTimerElapsedAsync();

        private async Task OnDeferredTimerElapsedAsync()
        {
            try
            {
                var now = DateTime.Now;
                var toSend = new List<DeferredEmail>();

                int count = _deferredQueue.Count;
                for (int i = 0; i < count; i++)
                {
                    if (_deferredQueue.TryDequeue(out var item))
                    {
                        // Calculate the target send time
                        DateTime sendAt = item.QueuedAt.Date.Add(item.SendTime);
                        if (sendAt <= item.QueuedAt)
                            sendAt = sendAt.AddDays(1); // send time is next day

                        if (now >= sendAt)
                            toSend.Add(item);
                        else
                            _deferredQueue.Enqueue(item); // not yet, put back
                    }
                }

                foreach (var item in toSend)
                {
                    await SendEmailAsync(item.Config, item.Subject,
                        item.PlainTextBody, item.HtmlReportPath, item.ScheduleName).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[EmailNotification] Deferred timer callback failed", ex);
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        public void Dispose()
        {
            _deferredTimer?.Stop();
            _deferredTimer?.Dispose();
        }

        private class DeferredEmail
        {
            public EmailNotificationConfig Config { get; set; }
            public string Subject { get; set; }
            public string PlainTextBody { get; set; }
            public string HtmlReportPath { get; set; }
            public string ScheduleName { get; set; }
            public TimeSpan SendTime { get; set; }
            public DateTime QueuedAt { get; set; }
        }
    }
}
