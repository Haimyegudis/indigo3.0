using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models.Grep
{
    public class EmailNotificationConfig
    {
        public bool IsEnabled { get; set; }

        // ── SMTP Settings ──
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 25;
        public bool UseSsl { get; set; }
        public SmtpAuthMode AuthMode { get; set; } = SmtpAuthMode.WindowsIntegrated;
        public string SmtpUsername { get; set; }
        public string SmtpPassword { get; set; }
        public string FromAddress { get; set; }
        public string FromDisplayName { get; set; } = "IndiLogs 3.0";

        // ── Recipients ──
        public List<string> Recipients { get; set; } = new List<string>();

        // ── Timing ──
        public EmailTiming Timing { get; set; } = EmailTiming.Immediately;

        /// <summary>
        /// If Timing == AtSpecificTime, the time of day to send the email.
        /// </summary>
        public TimeSpan SendTime { get; set; }

        /// <summary>
        /// Custom email subject. If empty, auto-generated from schedule name + status.
        /// </summary>
        public string CustomSubject { get; set; }
    }

    public enum EmailTiming
    {
        Immediately,
        AtSpecificTime
    }

    public enum SmtpAuthMode
    {
        None,
        WindowsIntegrated,
        UsernamePassword
    }
}
