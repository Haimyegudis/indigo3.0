using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Grep;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogStatisticsServiceComputationTests
    {
        // ── TopN ──

        [Fact]
        public void TopN_ReturnsTopItems()
        {
            var dict = new Dictionary<string, int>
            {
                ["a"] = 10, ["b"] = 50, ["c"] = 30, ["d"] = 20, ["e"] = 40
            };

            var top3 = LogStatisticsService.TopN(dict, 3);

            Assert.Equal(3, top3.Count);
            Assert.Equal("b", top3[0].Key);
            Assert.Equal(50, top3[0].Value);
            Assert.Equal("e", top3[1].Key);
            Assert.Equal(40, top3[1].Value);
            Assert.Equal("c", top3[2].Key);
            Assert.Equal(30, top3[2].Value);
        }

        [Fact]
        public void TopN_LessThanN_ReturnsAllSorted()
        {
            var dict = new Dictionary<string, int> { ["a"] = 5, ["b"] = 15 };

            var top5 = LogStatisticsService.TopN(dict, 5);

            Assert.Equal(2, top5.Count);
            Assert.Equal("b", top5[0].Key);
            Assert.Equal("a", top5[1].Key);
        }

        [Fact]
        public void TopN_EmptyDict_ReturnsEmpty()
        {
            var result = LogStatisticsService.TopN(new Dictionary<string, int>(), 5);
            Assert.Empty(result);
        }

        [Fact]
        public void TopN_SingleItem_ReturnsSingle()
        {
            var dict = new Dictionary<string, int> { ["only"] = 42 };
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
                new LogEntry { Level = "Error", Message = "err1" },
                new LogEntry { Level = "Info", Message = "info" },
                new LogEntry { Level = "Fatal", Message = "fatal1" },
                new LogEntry { Level = "Warning", Message = "warn" },
                new LogEntry { Level = "error", Message = "err2" }, // case insensitive
            };

            var errors = LogStatisticsService.GetErrorLogs(logs);

            Assert.Equal(3, errors.Count);
        }

        [Fact]
        public void GetErrorLogs_NoErrors_ReturnsEmpty()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Level = "Info" },
                new LogEntry { Level = "Debug" }
            };

            Assert.Empty(LogStatisticsService.GetErrorLogs(logs));
        }

        [Fact]
        public void GetErrorLogs_EmptyList_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.GetErrorLogs(new List<LogEntry>()));
        }

        // ── CalculateErrorHistogram ──

        [Fact]
        public void CalculateErrorHistogram_GroupsByMessage()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Level = "Error", Message = "Disk error" },
                new LogEntry { Level = "Error", Message = "Disk error" },
                new LogEntry { Level = "Error", Message = "Disk error" },
                new LogEntry { Level = "Error", Message = "Network error" },
            };

            var histogram = LogStatisticsService.CalculateErrorHistogram(errors, 10);

            Assert.Equal(2, histogram.Count);
            Assert.Equal("Disk error", histogram[0].Name);
            Assert.Equal(3, histogram[0].Count);
        }

        [Fact]
        public void CalculateErrorHistogram_EmptyErrors_ReturnsEmpty()
        {
            var result = LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 10);
            Assert.Empty(result);
        }

        [Fact]
        public void CalculateErrorHistogram_WithCustomKeySelector()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Level = "Error", Logger = "com.indigo.Module1" },
                new LogEntry { Level = "Error", Logger = "com.indigo.Module1" },
                new LogEntry { Level = "Error", Logger = "com.indigo.Module2" },
            };

            var histogram = LogStatisticsService.CalculateErrorHistogram(errors, 10, l => l.Logger);

            Assert.Equal(2, histogram.Count);
        }

        // ── CalculateLoadDistribution ──

        [Fact]
        public void CalculateLoadDistribution_GroupsByThread()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { ThreadName = "Worker" },
                new LogEntry { ThreadName = "Worker" },
                new LogEntry { ThreadName = "Worker" },
                new LogEntry { ThreadName = "Manager" },
                new LogEntry { ThreadName = "Manager" },
            };

            var load = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);

            Assert.Equal(2, load.Count);
            Assert.Equal("Worker", load[0].Name);
            Assert.Equal(3, load[0].Count);
            Assert.True(load[0].Percentage > 50);
        }

        [Fact]
        public void CalculateLoadDistribution_EmptyLogs_ReturnsEmpty()
        {
            var result = LogStatisticsService.CalculateLoadDistribution(
                new List<LogEntry>(), l => l.ThreadName, 10);
            Assert.Empty(result);
        }

        [Fact]
        public void CalculateLoadDistribution_SkipsEmptyKeys()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { ThreadName = "Worker" },
                new LogEntry { ThreadName = "" },
                new LogEntry { ThreadName = "" },
            };

            var load = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);

            Assert.Single(load);
            Assert.Equal("Worker", load[0].Name);
        }

        // ── FindGaps ──

        [Fact]
        public void FindGaps_DetectsGapsOver2Seconds()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 0), Message = "log1" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 1), Message = "log2" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 5), Message = "log3" }, // 4s gap
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 6), Message = "log4" },
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Single(gaps);
            Assert.Equal(1, gaps[0].Index);
            Assert.Equal(4, gaps[0].Duration.TotalSeconds);
            Assert.Equal("log2", gaps[0].LastMessageBeforeGap);
        }

        [Fact]
        public void FindGaps_NoGaps_ReturnsEmpty()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 0) },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 1) },
            };

            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(null!));
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry>()));
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry> { new LogEntry() }));
        }

        // ── TruncateMessage ──

        [Fact]
        public void TruncateMessage_ShortMessage_Unchanged()
        {
            Assert.Equal("hello", LogStatisticsService.TruncateMessage("hello", 100));
        }

        [Fact]
        public void TruncateMessage_LongMessage_Truncated()
        {
            var msg = new string('x', 200);
            var result = LogStatisticsService.TruncateMessage(msg, 100);
            Assert.Equal(103, result.Length); // 100 + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void TruncateMessage_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 100));
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 100));
        }

        // ── GetShortLoggerName ──

        [Fact]
        public void GetShortLoggerName_LongPath_ReturnsLastTwo()
        {
            Assert.Equal("Pipeline.PipelineCancellationProvider",
                LogStatisticsService.GetShortLoggerName("Press.BL.Printing.Pipeline.PipelineCancellationProvider"));
        }

        [Fact]
        public void GetShortLoggerName_TwoParts_ReturnsFullName()
        {
            Assert.Equal("Module.Class",
                LogStatisticsService.GetShortLoggerName("Module.Class"));
        }

        [Fact]
        public void GetShortLoggerName_NullOrEmpty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        // ── FormatDuration ──

        [Fact]
        public void FormatDuration_Minutes()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromMinutes(3.5));
            Assert.Contains("min", result);
        }

        [Fact]
        public void FormatDuration_Seconds()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromSeconds(30));
            Assert.Contains("sec", result);
        }

        // ── CalculateStateEntries ──

        [Fact]
        public void CalculateStateEntries_S6Transitions_ParsesCorrectly()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 0), ThreadName = "Worker", Message = "Normal log" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 5), ThreadName = "Manager", Message = "PlcMngr: OFF -> GET_READY" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 1, 0), ThreadName = "Manager", Message = "PlcMngr: GET_READY -> RUNNING" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 2, 0), ThreadName = "Worker", Message = "Final log" },
            };

            var states = LogStatisticsService.CalculateStateEntries(logs);

            Assert.True(states.Count >= 2);
            // First real transition
            var getReady = states.FirstOrDefault(s => s.StateName == "GET_READY");
            Assert.NotNull(getReady);
        }

        [Fact]
        public void CalculateStateEntries_NoTransitions_ReturnsEmpty()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, ThreadName = "Worker", Message = "Normal log" }
            };

            Assert.Empty(LogStatisticsService.CalculateStateEntries(logs));
        }

        [Fact]
        public void CalculateStateEntries_EmptyLogs_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        // ── MapErrorsToStates ──

        [Fact]
        public void MapErrorsToStates_MapsCorrectly()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 30), Level = "Error" },
            };
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "GET_READY", StartTime = new DateTime(2025, 1, 1, 10, 0, 0), EndTime = new DateTime(2025, 1, 1, 10, 1, 0) }
            };

            var result = LogStatisticsService.MapErrorsToStates(errors, states);

            Assert.Single(result);
            Assert.Equal("GET_READY", result[0].Name);
            Assert.Equal(1, result[0].Count);
        }

        [Fact]
        public void MapErrorsToStates_EmptyErrors_ReturnsEmpty()
        {
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "X", StartTime = DateTime.Now, EndTime = DateTime.Now.AddMinutes(1) }
            };

            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), states));
        }

        [Fact]
        public void MapErrorsToStates_NoStates_ReturnsEmpty()
        {
            var errors = new List<LogEntry> { new LogEntry { Date = DateTime.Now, Level = "Error" } };
            Assert.Empty(LogStatisticsService.MapErrorsToStates(errors, new List<StateEntry>()));
        }

        // ── ComputeStatistics (integration) ──

        [Fact]
        public void ComputeStatistics_EmptyLogs_ReturnsDefaults()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());

            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Equal(0, result.TotalPlcErrors);
            Assert.Equal(0, result.TotalAppErrors);
        }

        [Fact]
        public void ComputeStatistics_WithLogs_ComputesTimeSpan()
        {
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 0), Level = "Info" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 11, 0, 0), Level = "Info" }
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());

            Assert.Equal(2, result.TotalPlcLogs);
            Assert.NotNull(result.EarliestTimestamp);
            Assert.NotNull(result.LatestTimestamp);
            Assert.Equal(new DateTime(2025, 1, 1, 10, 0, 0), result.EarliestTimestamp);
            Assert.Equal(new DateTime(2025, 1, 1, 11, 0, 0), result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_CountsErrors()
        {
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Error", Message = "err" },
                new LogEntry { Date = DateTime.Now, Level = "Info", Message = "info" },
            };
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Fatal", Message = "fatal" },
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, appLogs);

            Assert.Equal(1, result.TotalPlcErrors);
            Assert.Equal(1, result.TotalAppErrors);
        }
    }
}
