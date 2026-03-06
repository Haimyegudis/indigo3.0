using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services.Grep
{
    public partial class EmailNotificationService : IEmailNotificationService
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
            List<GrepResult>? results,
            LogStatisticsResult? stats,
            string? htmlReportPath,
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
                    HtmlReportPath = htmlReportPath ?? "",
                    ScheduleName = schedule.Name,
                    SendTime = config.SendTime,
                    QueuedAt = DateTime.Now
                });
                AppLogger.Info($"[Email] Deferred email for \"{schedule.Name}\" — will send at {config.SendTime:hh\\:mm}");
            }
            else
            {
                Task.Run(() => SendViaOutlookAsync(config, subject, plainTextBody, htmlReportPath, schedule.Name));
            }
        }

        public Task<(bool Success, string Message)> TestConnectionAsync(
            EmailNotificationConfig config, string testRecipient)
        {
            AppLogger.Info($"[Email] Test starting via Outlook — To: {testRecipient}");
            try
            {
                SendViaOutlook(
                    new[] { testRecipient },
                    "[IndiLogs] Test Email",
                    $"This is a test email from IndiLogs 3.0 scheduled scan notifications.\r\n\r\nSent at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    null);

                AppLogger.Info("[Email] Test email sent successfully via Outlook.");
                return Task.FromResult<(bool, string)>((true, "Test email sent successfully via Outlook."));
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Email] Test failed — {ex.GetType().Name}: {ex.Message}", ex);
                return Task.FromResult<(bool, string)>((false, $"Failed: {ex.Message}"));
            }
        }

        private Task SendViaOutlookAsync(
            EmailNotificationConfig config,
            string subject,
            string plainTextBody,
            string? htmlReportPath,
            string scheduleName)
        {
            try
            {
                SendViaOutlook(config.Recipients, subject, plainTextBody, htmlReportPath);
                AppLogger.Info($"[Email] Sent notification for \"{scheduleName}\" to {config.Recipients.Count} recipient(s) via Outlook");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Email] Failed to send for \"{scheduleName}\": {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private static void SendViaOutlook(
            IEnumerable<string> recipients,
            string subject,
            string body,
            string? attachmentPath)
        {
            dynamic outlook = Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application")!)!;
            try
            {
                dynamic mail = outlook.CreateItem(0); // olMailItem = 0
                mail.Subject = subject;
                mail.Body = body;
                mail.To = string.Join(";", recipients);

                if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                    mail.Attachments.Add(attachmentPath);

                AppLogger.Info($"[Email] Sending via Outlook to: {mail.To}");
                mail.Send();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(outlook);
            }
        }

        private void OnDeferredTimerElapsed(object? sender, ElapsedEventArgs e) => _ = OnDeferredTimerElapsedAsync();

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
                    await SendViaOutlookAsync(item.Config, item.Subject,
                        item.PlainTextBody, item.HtmlReportPath, item.ScheduleName).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[EmailNotification] Deferred timer callback failed", ex);
            }
        }

        public void Dispose()
        {
            _deferredTimer?.Stop();
            _deferredTimer?.Dispose();
        }

        private class DeferredEmail
        {
            public EmailNotificationConfig Config { get; set; } = null!;
            public string Subject { get; set; } = "";
            public string PlainTextBody { get; set; } = "";
            public string HtmlReportPath { get; set; } = "";
            public string ScheduleName { get; set; } = "";
            public TimeSpan SendTime { get; set; }
            public DateTime QueuedAt { get; set; }
        }
    }
}
