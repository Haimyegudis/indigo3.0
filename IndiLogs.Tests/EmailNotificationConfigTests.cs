using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class EmailNotificationConfigTests
    {
        [Fact]
        public void SmtpPassword_SetAndGet_RoundTripsViaDpapi()
        {
            var config = new EmailNotificationConfig();
            config.SmtpPassword = "MySecretPass123";

            // EncryptedSmtpPassword should be populated (Base64)
            Assert.False(string.IsNullOrEmpty(config.EncryptedSmtpPassword));

            // Should round-trip
            Assert.Equal("MySecretPass123", config.SmtpPassword);
        }

        [Fact]
        public void SmtpPassword_SetNull_ClearsEncrypted()
        {
            var config = new EmailNotificationConfig();
            config.SmtpPassword = "something";
            Assert.NotNull(config.EncryptedSmtpPassword);

            config.SmtpPassword = null;
            Assert.Null(config.EncryptedSmtpPassword);
        }

        [Fact]
        public void SmtpPassword_SetEmpty_ClearsEncrypted()
        {
            var config = new EmailNotificationConfig();
            config.SmtpPassword = "something";

            config.SmtpPassword = "";
            Assert.Null(config.EncryptedSmtpPassword);
        }

        [Fact]
        public void SmtpPassword_LegacyFallback_AutoMigrates()
        {
            var config = new EmailNotificationConfig
            {
                SmtpPasswordLegacy = "legacyPass"
            };

            // Reading SmtpPassword should auto-migrate
            string? result = config.SmtpPassword;
            Assert.Equal("legacyPass", result);

            // Legacy field should be cleared
            Assert.Null(config.SmtpPasswordLegacy);

            // Encrypted field should be populated
            Assert.False(string.IsNullOrEmpty(config.EncryptedSmtpPassword));
        }

        [Fact]
        public void SmtpPassword_NoPasswordSet_ReturnsNull()
        {
            var config = new EmailNotificationConfig();
            Assert.Null(config.SmtpPassword);
        }

        [Fact]
        public void SmtpPassword_EncryptedPreferredOverLegacy()
        {
            var config = new EmailNotificationConfig();
            config.SmtpPassword = "encryptedValue";

            // Even if legacy is set, encrypted should win
            config.SmtpPasswordLegacy = "legacyValue";
            Assert.Equal("encryptedValue", config.SmtpPassword);
        }

        [Fact]
        public void SmtpPassword_CorruptedEncrypted_ReturnsNull()
        {
            var config = new EmailNotificationConfig
            {
                EncryptedSmtpPassword = "not-valid-base64-encrypted-data!!!"
            };

            // Should not throw, should return null
            Assert.Null(config.SmtpPassword);
        }

        // ── Default values ──

        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var config = new EmailNotificationConfig();
            Assert.False(config.IsEnabled);
            Assert.Equal("", config.SmtpHost);
            Assert.Equal(25, config.SmtpPort);
            Assert.False(config.UseSsl);
            Assert.Equal(SmtpAuthMode.WindowsIntegrated, config.AuthMode);
            Assert.Equal("IndiLogs 3.0", config.FromDisplayName);
            Assert.Empty(config.Recipients);
            Assert.Equal(EmailTiming.Immediately, config.Timing);
        }
    }
}
