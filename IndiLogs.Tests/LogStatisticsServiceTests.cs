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
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 100));
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
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
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
            var logs = new List<LogEntry> { new LogEntry { Level = null! } };
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
            Assert.Empty(LogStatisticsService.FindGaps(null!));
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

        // ── CalculateStateEntries ──

        [Fact]
        public void CalculateStateEntries_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        [Fact]
        public void CalculateStateEntries_S6_WithTransitions_ParsesStates()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = now, ThreadName = "Events", Message = "boot" },
                new LogEntry { Date = now.AddSeconds(1), ThreadName = "Manager", Message = "PlcMngr: Idle -> Printing" },
                new LogEntry { Date = now.AddSeconds(5), ThreadName = "Manager", Message = "PlcMngr: Printing -> Done" },
                new LogEntry { Date = now.AddSeconds(10), ThreadName = "Worker", Message = "regular" },
            };
            var states = LogStatisticsService.CalculateStateEntries(logs);
            Assert.True(states.Count >= 2);
            Assert.Contains(states, s => s.StateName == "Printing");
            Assert.Contains(states, s => s.StateName == "Done");
        }

        [Fact]
        public void CalculateStateEntries_S6_InitialState_Extracted()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = now, ThreadName = "Events", Message = "start" },
                new LogEntry { Date = now.AddSeconds(1), ThreadName = "Manager", Message = "PlcMngr: Idle -> Printing" },
            };
            var states = LogStatisticsService.CalculateStateEntries(logs);
            Assert.True(states.Count >= 1);
            Assert.Equal("Idle", states[0].StateName);
        }

        [Fact]
        public void CalculateStateEntries_NoTransitions_ReturnsEmpty()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = now, ThreadName = "Worker", Message = "no transitions here" },
                new LogEntry { Date = now.AddSeconds(1), ThreadName = "Events", Message = "just events" },
            };
            var states = LogStatisticsService.CalculateStateEntries(logs);
            Assert.Empty(states);
        }

        // ── MapErrorsToStates ──

        [Fact]
        public void MapErrorsToStates_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), new List<StateEntry>()));
        }

        [Fact]
        public void MapErrorsToStates_NullStates_ReturnsEmpty()
        {
            var errors = new List<LogEntry> { new LogEntry { Date = DateTime.Now, Level = "Error" } };
            Assert.Empty(LogStatisticsService.MapErrorsToStates(errors, null!));
        }

        [Fact]
        public void MapErrorsToStates_MapsErrorsToCorrectState()
        {
            var now = DateTime.Now;
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "Idle", StartTime = now, EndTime = now.AddMinutes(1) },
                new StateEntry { StateName = "Printing", StartTime = now.AddMinutes(1), EndTime = now.AddMinutes(5) },
            };
            var errors = new List<LogEntry>
            {
                new LogEntry { Date = now.AddSeconds(30), Level = "Error", Message = "e1" },
                new LogEntry { Date = now.AddMinutes(2), Level = "Error", Message = "e2" },
                new LogEntry { Date = now.AddMinutes(3), Level = "Error", Message = "e3" },
            };
            var result = LogStatisticsService.MapErrorsToStates(errors, states);
            Assert.True(result.Count >= 1);
            var printing = result.FirstOrDefault(r => r.Name == "Printing");
            Assert.NotNull(printing);
            Assert.Equal(2, printing!.Count);
        }

        // ── ComputeStatistics integration ──

        [Fact]
        public void ComputeStatistics_EmptyLogs_ReturnsZeroCounts()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Null(result.EarliestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_WithPlcLogs_PopulatesAllStats()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "Info", ThreadName = "Manager", Message = "start" },
                new LogEntry { Date = now.AddSeconds(1), Level = "Error", ThreadName = "Manager", Message = "err" },
                new LogEntry { Date = now.AddSeconds(2), Level = "Info", ThreadName = "Worker", Message = "work" },
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());
            Assert.Equal(3, result.TotalPlcLogs);
            Assert.Equal(1, result.TotalPlcErrors);
            Assert.NotNull(result.EarliestTimestamp);
            Assert.NotNull(result.LatestTimestamp);
            Assert.True(result.PlcTopErrors.Count > 0);
            Assert.True(result.PlcThreadLoad.Count > 0);
        }

        [Fact]
        public void ComputeStatistics_WithAppLogs_PopulatesAppStats()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "Info", Logger = "App.Service", Method = "Init", Message = "ok" },
                new LogEntry { Date = now.AddSeconds(1), Level = "Error", Logger = "App.DB", Method = "Query", Message = "fail" },
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs);
            Assert.Equal(2, result.TotalAppLogs);
            Assert.Equal(1, result.TotalAppErrors);
            Assert.True(result.AppLoggerLoad.Count > 0);
        }

        [Fact]
        public void ComputeStatistics_BinaryApp_SkipsMethodStats()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "Info", Logger = "App", Message = "a" },
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs, hasBinaryAppLogs: true);
            Assert.True(result.HasBinaryAppLogs);
            Assert.Empty(result.AppMethodLoad);
        }

        [Fact]
        public void ComputeStatistics_WithTransitions_PopulatesStates()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "Info", ThreadName = "Events", Message = "boot" },
                new LogEntry { Date = now.AddSeconds(1), Level = "Info", ThreadName = "Manager", Message = "PlcMngr: Idle -> Printing" },
                new LogEntry { Date = now.AddSeconds(2), Level = "Error", ThreadName = "Worker", Message = "Print error" },
                new LogEntry { Date = now.AddSeconds(5), Level = "Info", ThreadName = "Manager", Message = "PlcMngr: Printing -> Done" },
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());
            Assert.True(result.StateEntries.Count > 0);
            Assert.True(result.ErrorsByState.Count > 0);
        }

        [Fact]
        public void ComputeStatistics_TimeSpan_Correct()
        {
            var start = new DateTime(2024, 1, 15, 10, 0, 0);
            var end = new DateTime(2024, 1, 15, 12, 0, 0);
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = start, Level = "Info", Message = "first" },
                new LogEntry { Date = end, Level = "Info", Message = "last" },
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());
            Assert.Equal(start, result.EarliestTimestamp);
            Assert.Equal(end, result.LatestTimestamp);
        }

        [Fact]
        public void CalculateLoadDistribution_WithFullName_PreservesFullName()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Logger = "Com.HP.Indigo.Service", Message = "a" },
                new LogEntry { Logger = "Com.HP.Indigo.Service", Message = "b" },
            };
            var result = LogStatisticsService.CalculateLoadDistribution(
                logs, l => LogStatisticsService.GetShortLoggerName(l.Logger), 10, l => l.Logger);
            Assert.Single(result);
            Assert.Equal("Com.HP.Indigo.Service", result[0].FullName);
        }

        [Fact]
        public void CalculateErrorHistogram_LongMessage_Truncated()
        {
            var longMsg = new string('x', 200);
            var errors = new List<LogEntry>
            {
                new LogEntry { Level = "Error", Message = longMsg },
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10);
            Assert.Single(result);
            Assert.True(result[0].Name.Length < 200);
        }
    }
}
