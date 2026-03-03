using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace IndiLogs_3._0.Models.Grep
{
    public class EmailNotificationConfig
    {
        public bool IsEnabled { get; set; }

        // ── SMTP Settings ──
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public bool UseSsl { get; set; }
        public SmtpAuthMode AuthMode { get; set; } = SmtpAuthMode.WindowsIntegrated;
        public string SmtpUsername { get; set; } = "";

        /// <summary>
        /// DPAPI-encrypted SMTP password (Base64). Serialized to JSON.
        /// </summary>
        public string? EncryptedSmtpPassword { get; set; }

        /// <summary>
        /// Runtime-only accessor for the plaintext password.
        /// On get: decrypts EncryptedSmtpPassword via DPAPI.
        /// On set: encrypts the value and stores it in EncryptedSmtpPassword.
        /// Falls back to reading legacy plaintext SmtpPasswordLegacy if EncryptedSmtpPassword is empty.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? SmtpPassword
        {
            get
            {
                // Prefer encrypted
                if (!string.IsNullOrEmpty(EncryptedSmtpPassword))
                {
                    try
                    {
                        byte[] encrypted = Convert.FromBase64String(EncryptedSmtpPassword);
                        byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(decrypted);
                    }
                    catch (Exception)
                    {
                        // Decryption failed (different user, corrupted data)
                        return null;
                    }
                }

                // Backward compat: if legacy plaintext field is still present
                if (!string.IsNullOrEmpty(SmtpPasswordLegacy))
                {
                    // Auto-migrate: encrypt the legacy value
                    string legacy = SmtpPasswordLegacy;
                    SmtpPassword = legacy; // This triggers the setter, encrypting it
                    SmtpPasswordLegacy = null; // Clear legacy
                    return legacy;
                }

                return null;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    EncryptedSmtpPassword = null;
                }
                else
                {
                    try
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(value);
                        byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                        EncryptedSmtpPassword = Convert.ToBase64String(encrypted);
                    }
                    catch (Exception)
                    {
                        // If DPAPI fails, store nothing rather than plaintext
                        EncryptedSmtpPassword = null;
                    }
                }
                SmtpPasswordLegacy = null; // Clear legacy field once encrypted
            }
        }

        /// <summary>
        /// Legacy plaintext password field for backward compatibility with existing JSON files.
        /// New saves will use EncryptedSmtpPassword instead.
        /// </summary>
        [Newtonsoft.Json.JsonProperty("SmtpPassword")]
        public string? SmtpPasswordLegacy { get; set; }

        public string FromAddress { get; set; } = "";
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
        public string? CustomSubject { get; set; }
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
