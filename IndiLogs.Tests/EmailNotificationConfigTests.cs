using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class EmailNotificationConfigTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var config = new EmailNotificationConfig();
            Assert.False(config.IsEnabled);
            Assert.Empty(config.Recipients);
            Assert.Equal(EmailTiming.Immediately, config.Timing);
            Assert.Null(config.CustomSubject);
        }

        [Fact]
        public void Recipients_CanBePopulated()
        {
            var config = new EmailNotificationConfig();
            config.Recipients.Add("test@example.com");
            config.Recipients.Add("other@example.com");
            Assert.Equal(2, config.Recipients.Count);
        }

        [Fact]
        public void Timing_CanBeSetToDeferred()
        {
            var config = new EmailNotificationConfig
            {
                Timing = EmailTiming.AtSpecificTime,
                SendTime = new TimeSpan(14, 30, 0)
            };
            Assert.Equal(EmailTiming.AtSpecificTime, config.Timing);
            Assert.Equal(14, config.SendTime.Hours);
            Assert.Equal(30, config.SendTime.Minutes);
        }
    }
}
