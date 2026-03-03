using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogStatisticsServiceTests
    {
        private LogEntry MakeEntry(string message = "msg", string level = "Info",
            string logger = "App.Service", DateTime? date = null)
        {
            return new LogEntry
            {
                Message = message,
                Level = level,
                Logger = logger,
                Date = date ?? DateTime.Now
            };
        }

        // ── TruncateMessage ──

        [Fact]
        public void TruncateMessage_Short_ReturnsUnchanged()
        {
            Assert.Equal("hello", LogStatisticsService.TruncateMessage("hello", 100));
        }

        [Fact]
        public void TruncateMessage_Long_TruncatesWithEllipsis()
        {
            string result = LogStatisticsService.TruncateMessage("abcdefghij", 5);
            Assert.Equal("abcde...", result);
        }

        [Fact]
        public void TruncateMessage_Null_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null, 100));
        }

        [Fact]
        public void TruncateMessage_Empty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 100));
        }

        [Fact]
        public void TruncateMessage_ExactLength_ReturnsUnchanged()
        {
            Assert.Equal("abcde", LogStatisticsService.TruncateMessage("abcde", 5));
        }

        // ── GetShortLoggerName ──

        [Fact]
        public void GetShortLoggerName_TwoParts_ReturnsFull()
        {
            Assert.Equal("App.Service", LogStatisticsService.GetShortLoggerName("App.Service"));
        }

        [Fact]
        public void GetShortLoggerName_ManyParts_ReturnsLastTwo()
        {
            Assert.Equal("Services.Logger", LogStatisticsService.GetShortLoggerName("MyApp.Core.Services.Logger"));
        }

        [Fact]
        public void GetShortLoggerName_SinglePart_ReturnsFull()
        {
            Assert.Equal("Logger", LogStatisticsService.GetShortLoggerName("Logger"));
        }

        [Fact]
        public void GetShortLoggerName_Null_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null));
        }

        [Fact]
        public void GetShortLoggerName_Empty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        // ── FormatDuration ──

        [Fact]
        public void FormatDuration_Seconds_FormatsCorrectly()
        {
            var ts = TimeSpan.FromSeconds(45.3);
            Assert.Equal("45.3 sec", LogStatisticsService.FormatDuration(ts));
        }

        [Fact]
        public void FormatDuration_Minutes_FormatsCorrectly()
        {
            var ts = TimeSpan.FromMinutes(3.5);
            Assert.Equal("3.5 min", LogStatisticsService.FormatDuration(ts));
        }

        [Fact]
        public void FormatDuration_ExactlyOneMinute_FormatsAsMinutes()
        {
            var ts = TimeSpan.FromMinutes(1.0);
            Assert.Equal("1.0 min", LogStatisticsService.FormatDuration(ts));
        }

        [Fact]
        public void FormatDuration_SubSecond_FormatsAsSeconds()
        {
            var ts = TimeSpan.FromMilliseconds(500);
            Assert.Equal("0.5 sec", LogStatisticsService.FormatDuration(ts));
        }

        // ── TopN ──

        [Fact]
        public void TopN_ReturnsTopItems_Sorted()
        {
            var dict = new Dictionary<string, int>
            {
                { "A", 10 }, { "B", 50 }, { "C", 30 }, { "D", 20 }, { "E", 40 }
            };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, result.Count);
            Assert.Equal("B", result[0].Key);
            Assert.Equal(50, result[0].Value);
            Assert.Equal("E", result[1].Key);
            Assert.Equal(40, result[1].Value);
            Assert.Equal("C", result[2].Key);
            Assert.Equal(30, result[2].Value);
        }

        [Fact]
        public void TopN_LessThanN_ReturnsAllSorted()
        {
            var dict = new Dictionary<string, int> { { "A", 5 }, { "B", 10 } };
            var result = LogStatisticsService.TopN(dict, 5);
            Assert.Equal(2, result.Count);
            Assert.Equal("B", result[0].Key);
            Assert.Equal("A", result[1].Key);
        }

        [Fact]
        public void TopN_Empty_ReturnsEmpty()
        {
            var result = LogStatisticsService.TopN(new Dictionary<string, int>(), 3);
            Assert.Empty(result);
        }

        [Fact]
        public void TopN_SingleItem_ReturnsSingle()
        {
            var dict = new Dictionary<string, int> { { "only", 42 } };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Single(result);
            Assert.Equal("only", result[0].Key);
        }

        // ── GetErrorLogs ──

        [Fact]
        public void GetErrorLogs_FiltersErrorAndFatal()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(level: "Info"),
                MakeEntry(level: "Error"),
                MakeEntry(level: "Warning"),
                MakeEntry(level: "Fatal"),
                MakeEntry(level: "Debug")
            };
            var errors = LogStatisticsService.GetErrorLogs(logs);
            Assert.Equal(2, errors.Count);
            Assert.All(errors, e => Assert.True(
                e.Level.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                e.Level.Equals("Fatal", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void GetErrorLogs_CaseInsensitive()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(level: "error"),
                MakeEntry(level: "ERROR"),
                MakeEntry(level: "fatal")
            };
            Assert.Equal(3, LogStatisticsService.GetErrorLogs(logs).Count);
        }

        [Fact]
        public void GetErrorLogs_NullLevel_Excluded()
        {
            var logs = new List<LogEntry> { new LogEntry { Level = null } };
            Assert.Empty(LogStatisticsService.GetErrorLogs(logs));
        }

        // ── FindGaps ──

        [Fact]
        public void FindGaps_DetectsGapsAboveThreshold()
        {
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeEntry(date: baseTime),
                MakeEntry(date: baseTime.AddSeconds(1)),    // 1s gap — below threshold
                MakeEntry(date: baseTime.AddSeconds(5)),    // 4s gap — above threshold
                MakeEntry(date: baseTime.AddSeconds(5.5)),  // 0.5s gap — below threshold
                MakeEntry(date: baseTime.AddSeconds(10))    // 4.5s gap — above threshold
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Equal(2, gaps.Count);
            Assert.True(gaps[0].Duration.TotalSeconds >= 2.0);
            Assert.True(gaps[1].Duration.TotalSeconds >= 2.0);
        }

        [Fact]
        public void FindGaps_NoGaps_ReturnsEmpty()
        {
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeEntry(date: baseTime),
                MakeEntry(date: baseTime.AddSeconds(0.5)),
                MakeEntry(date: baseTime.AddSeconds(1.0))
            };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_SingleLog_ReturnsEmpty()
        {
            var logs = new List<LogEntry> { MakeEntry() };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_NullLogs_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(null));
        }

        // ── CalculateErrorHistogram ──

        [Fact]
        public void CalculateErrorHistogram_GroupsByMessage()
        {
            var errors = new List<LogEntry>
            {
                MakeEntry("Disk full"),
                MakeEntry("Disk full"),
                MakeEntry("Disk full"),
                MakeEntry("Timeout"),
                MakeEntry("Timeout"),
                MakeEntry("Connection reset")
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 2);
            Assert.Equal(2, result.Count);
            Assert.Equal(3, result[0].Count); // "Disk full" is most common
        }

        [Fact]
        public void CalculateErrorHistogram_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 5));
        }

        [Fact]
        public void CalculateErrorHistogram_BarWidth_MaxIs200()
        {
            var errors = new List<LogEntry>
            {
                MakeEntry("A"), MakeEntry("A"),
                MakeEntry("B")
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 5);
            Assert.Equal(200, result[0].BarWidth); // Top item gets full width
            Assert.Equal(100, result[1].BarWidth); // Half the count = half the width
        }

        // ── CalculateLoadDistribution ──

        [Fact]
        public void CalculateLoadDistribution_CalculatesPercentages()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(logger: "A"),
                MakeEntry(logger: "A"),
                MakeEntry(logger: "A"),
                MakeEntry(logger: "B"),
                MakeEntry(logger: "B"),
                MakeEntry(logger: "C")
            };
            var result = LogStatisticsService.CalculateLoadDistribution(
                logs, l => l.Logger, 10);
            Assert.Equal(3, result.Count);
            Assert.Equal(50.0, result[0].Percentage, 1); // A = 3/6 = 50%
        }

        [Fact]
        public void CalculateLoadDistribution_Empty_ReturnsEmpty()
        {
            var result = LogStatisticsService.CalculateLoadDistribution(
                new List<LogEntry>(), l => l.Logger, 10);
            Assert.Empty(result);
        }
    }
}
