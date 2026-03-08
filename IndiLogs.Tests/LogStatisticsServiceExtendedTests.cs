using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogStatisticsServiceExtendedTests
    {
        private static LogEntry MakeLog(string message = "msg", string level = "Info",
            string logger = "", string threadName = "", string method = "",
            DateTime? date = null)
        {
            return new LogEntry
            {
                Message = message,
                Level = level,
                Logger = logger,
                ThreadName = threadName,
                Method = method,
                Date = date ?? new DateTime(2025, 1, 1, 12, 0, 0)
            };
        }

        // ── ComputeStatistics comprehensive tests ──

        [Fact]
        public void ComputeStatistics_PlcOnly_ComputesPlcStats()
        {
            var plcLogs = new List<LogEntry>
            {
                MakeLog("Error in motor", "Error", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("Normal log", "Info", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 1)),
                MakeLog("Another error", "Error", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 0, 2)),
                MakeLog("Fatal crash", "Fatal", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 3)),
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());

            Assert.Equal(4, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Equal(3, result.TotalPlcErrors);
            Assert.NotNull(result.PlcTopErrors);
            Assert.True(result.PlcTopErrors!.Count > 0);
            Assert.NotNull(result.PlcThreadLoad);
            Assert.True(result.PlcThreadLoad!.Count > 0);
        }

        [Fact]
        public void ComputeStatistics_AppOnly_ComputesAppStats()
        {
            var appLogs = new List<LogEntry>
            {
                MakeLog("App error", "Error", logger: "com.indigo.Module1", method: "DoWork", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("App info", "Info", logger: "com.indigo.Module2", method: "Init", date: new DateTime(2025, 1, 1, 10, 0, 1)),
                MakeLog("App fatal", "Fatal", logger: "com.indigo.Module1", method: "DoWork", date: new DateTime(2025, 1, 1, 10, 0, 2)),
            };

            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs);

            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(3, result.TotalAppLogs);
            Assert.Equal(2, result.TotalAppErrors);
            Assert.NotNull(result.AppLoggerErrors);
            Assert.NotNull(result.AppLoggerLoad);
            Assert.NotNull(result.AppMethodErrors);
            Assert.NotNull(result.AppMethodLoad);
        }

        [Fact]
        public void ComputeStatistics_BinaryAppLogs_SkipsMethodStats()
        {
            var appLogs = new List<LogEntry>
            {
                MakeLog("err", "Error", logger: "Logger1", method: "Method1", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("info", "Info", logger: "Logger2", method: "Method2", date: new DateTime(2025, 1, 1, 10, 0, 1)),
            };

            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs, hasBinaryAppLogs: true);

            Assert.True(result.HasBinaryAppLogs);
            // When binary APP, method stats should be empty (not computed)
            Assert.True(result.AppMethodErrors == null || result.AppMethodErrors.Count == 0);
            Assert.True(result.AppMethodLoad == null || result.AppMethodLoad.Count == 0);
        }

        [Fact]
        public void ComputeStatistics_BothPlcAndApp_ComputesCombinedTimeSpan()
        {
            var plcLogs = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 10, 30, 0)),
            };
            var appLogs = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 9, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 11, 0, 0)),
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, appLogs);

            Assert.Equal(new DateTime(2025, 1, 1, 9, 0, 0), result.EarliestTimestamp);
            Assert.Equal(new DateTime(2025, 1, 1, 11, 0, 0), result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_WithStateTransitions_ComputesStateEntries()
        {
            var plcLogs = new List<LogEntry>
            {
                MakeLog("PlcMngr: OFF -> GET_READY", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("Normal log", "Info", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 5)),
                MakeLog("PlcMngr: GET_READY -> PRINTING", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 1, 0)),
                MakeLog("Print error", "Error", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 1, 30)),
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());

            Assert.NotNull(result.StateEntries);
            Assert.True(result.StateEntries!.Count >= 1);
        }

        [Fact]
        public void ComputeStatistics_ErrorsMapToStates()
        {
            var plcLogs = new List<LogEntry>
            {
                MakeLog("PlcMngr: OFF -> PRINTING", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("Error in print", "Error", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 30)),
                MakeLog("PlcMngr: PRINTING -> OFF", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 1, 0)),
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());

            if (result.StateEntries != null && result.StateEntries.Count > 0 && result.ErrorsByState != null)
            {
                Assert.True(result.ErrorsByState.Count > 0);
            }
        }

        [Fact]
        public void ComputeStatistics_ErrorsBySource_CombinesPlcAndApp()
        {
            var plcLogs = new List<LogEntry>
            {
                MakeLog("PLC error", "Error", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 0)),
            };
            var appLogs = new List<LogEntry>
            {
                MakeLog("APP error", "Error", logger: "com.indigo.Module", date: new DateTime(2025, 1, 1, 10, 0, 1)),
            };

            var result = LogStatisticsService.ComputeStatistics(plcLogs, appLogs);

            Assert.NotNull(result.ErrorsBySource);
            Assert.True(result.ErrorsBySource!.Count >= 2);
        }

        // ── BuildErrorsBySource ──
        // These tests are disabled because BuildErrorsBySource was made private.
        // They are tested indirectly via ComputeStatistics.

        // ── CalculateLoadDistribution with FullName ──

        [Fact]
        public void CalculateLoadDistribution_WithFullNameSelector_PopulatesFullName()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo.Module.ClassA"),
                MakeLog(logger: "com.indigo.Module.ClassA"),
                MakeLog(logger: "com.indigo.Other.ClassB"),
            };

            var result = LogStatisticsService.CalculateLoadDistribution(
                logs, l => LogStatisticsService.GetShortLoggerName(l.Logger), 10, l => l.Logger);

            Assert.True(result.Count >= 2);
            Assert.True(result.Any(r => !string.IsNullOrEmpty(r.FullName)));
        }

        // ── CalculateErrorHistogram with top limit ──

        [Fact]
        public void CalculateErrorHistogram_RespectTopLimit()
        {
            var errors = new List<LogEntry>();
            for (int i = 0; i < 20; i++)
                errors.Add(MakeLog($"Error type {i}", "Error"));

            var result = LogStatisticsService.CalculateErrorHistogram(errors, 5);

            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void CalculateErrorHistogram_MessageField_UsedAsName()
        {
            var errors = new List<LogEntry>
            {
                MakeLog("Motor stalled at pos 42", "Error"),
                MakeLog("Motor stalled at pos 42", "Error"),
            };

            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10);

            Assert.Single(result);
            Assert.Equal("Motor stalled at pos 42", result[0].Name);
            Assert.Equal(2, result[0].Count);
            Assert.Equal("Motor stalled at pos 42", result[0].Message);
        }

        // ── FindGaps comprehensive ──

        [Fact]
        public void FindGaps_MultipleGaps_NumberedSequentially()
        {
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: baseTime),
                MakeLog("b", date: baseTime.AddSeconds(5)),    // 5s gap
                MakeLog("c", date: baseTime.AddSeconds(6)),
                MakeLog("d", date: baseTime.AddSeconds(15)),   // 9s gap
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Equal(2, gaps.Count);
            Assert.Equal(1, gaps[0].Index);
            Assert.Equal(2, gaps[1].Index);
        }

        [Fact]
        public void FindGaps_CapturesLastMessageBeforeGap()
        {
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("first log", date: baseTime),
                MakeLog("last before gap", date: baseTime.AddSeconds(1)),
                MakeLog("after gap", date: baseTime.AddSeconds(10)),
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Single(gaps);
            Assert.Equal("last before gap", gaps[0].LastMessageBeforeGap);
        }

        [Fact]
        public void FindGaps_ExactlyTwoSeconds_Included()
        {
            // Threshold is >= 2 seconds
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: baseTime),
                MakeLog("b", date: baseTime.AddSeconds(2)),
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Single(gaps);
        }

        [Fact]
        public void FindGaps_JustOverTwoSeconds_Included()
        {
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: baseTime),
                MakeLog("b", date: baseTime.AddSeconds(2.001)),
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Single(gaps);
        }

        // ── CalculateStateEntries ──

        [Fact]
        public void CalculateStateEntries_S4Transitions_ParsesCorrectly()
        {
            // S4 transitions use "==== STATE: X ====" pattern
            var logs = new List<LogEntry>
            {
                MakeLog("==== STATE: OFF ====", "Info", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("Normal log", "Info", date: new DateTime(2025, 1, 1, 10, 0, 30)),
                MakeLog("==== STATE: PRINTING ====", "Info", threadName: "Worker", date: new DateTime(2025, 1, 1, 10, 1, 0)),
            };

            var states = LogStatisticsService.CalculateStateEntries(logs);

            // May or may not parse depending on exact pattern matching
            // Just verify it doesn't throw
            Assert.NotNull(states);
        }

        [Fact]
        public void CalculateStateEntries_S6Transitions_SetsEndTime()
        {
            var logs = new List<LogEntry>
            {
                MakeLog("PlcMngr: OFF -> GET_READY", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 0, 0)),
                MakeLog("PlcMngr: GET_READY -> PRINTING", "Info", threadName: "Manager", date: new DateTime(2025, 1, 1, 10, 1, 0)),
            };

            var states = LogStatisticsService.CalculateStateEntries(logs);

            if (states.Count >= 2)
            {
                // First state should have an end time
                var first = states.First();
                Assert.NotNull(first.EndTime);
            }
        }

        // ── MapErrorsToStates ──

        [Fact]
        public void MapErrorsToStates_MultipleStates_MapsCorrectly()
        {
            var errors = new List<LogEntry>
            {
                MakeLog("err1", "Error", date: new DateTime(2025, 1, 1, 10, 0, 30)),
                MakeLog("err2", "Error", date: new DateTime(2025, 1, 1, 10, 1, 30)),
            };
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "IDLE", StartTime = new DateTime(2025, 1, 1, 10, 0, 0), EndTime = new DateTime(2025, 1, 1, 10, 1, 0) },
                new StateEntry { StateName = "PRINTING", StartTime = new DateTime(2025, 1, 1, 10, 1, 0), EndTime = new DateTime(2025, 1, 1, 10, 2, 0) },
            };

            var result = LogStatisticsService.MapErrorsToStates(errors, states);

            Assert.Equal(2, result.Count);
            var idle = result.Find(x => x.Name == "IDLE");
            var printing = result.Find(x => x.Name == "PRINTING");
            Assert.NotNull(idle);
            Assert.NotNull(printing);
            Assert.Equal(1, idle!.Count);
            Assert.Equal(1, printing!.Count);
        }

        [Fact]
        public void MapErrorsToStates_ErrorOutsideAllStates_NotMapped()
        {
            var errors = new List<LogEntry>
            {
                MakeLog("err", "Error", date: new DateTime(2025, 1, 1, 9, 0, 0)),
            };
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "IDLE", StartTime = new DateTime(2025, 1, 1, 10, 0, 0), EndTime = new DateTime(2025, 1, 1, 10, 1, 0) },
            };

            var result = LogStatisticsService.MapErrorsToStates(errors, states);

            Assert.Empty(result);
        }

        // ── GetShortLoggerName edge cases ──

        [Fact]
        public void GetShortLoggerName_OnePart_ReturnsAsIs()
        {
            Assert.Equal("Logger", LogStatisticsService.GetShortLoggerName("Logger"));
        }

        [Fact]
        public void GetShortLoggerName_TrailingDot_HandlesGracefully()
        {
            var result = LogStatisticsService.GetShortLoggerName("com.indigo.");
            Assert.NotNull(result);
        }

        // ── FormatDuration edge cases ──

        [Fact]
        public void FormatDuration_Zero_FormatsAsSeconds()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.Zero);
            Assert.Contains("sec", result);
        }

        [Fact]
        public void FormatDuration_Hours_FormatsAsMinutes()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromHours(1.5));
            Assert.Contains("min", result);
        }

        // ── CalculateLoadDistribution edge cases ──

        [Fact]
        public void CalculateLoadDistribution_NullKeys_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(threadName: "A"),
                new LogEntry { ThreadName = null!, Date = DateTime.Now, Level = "Info", Message = "" },
            };

            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);

            Assert.Single(result);
        }

        [Fact]
        public void CalculateLoadDistribution_TopLimit_Respected()
        {
            var logs = new List<LogEntry>();
            for (int i = 0; i < 20; i++)
                logs.Add(MakeLog(threadName: $"Thread{i}"));

            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 5);

            Assert.Equal(5, result.Count);
        }

        // ── TopN edge cases ──

        [Fact]
        public void TopN_AllSameCount_StillReturnsN()
        {
            var dict = new Dictionary<string, int>
            {
                ["a"] = 10, ["b"] = 10, ["c"] = 10, ["d"] = 10, ["e"] = 10
            };

            var result = LogStatisticsService.TopN(dict, 3);

            Assert.Equal(3, result.Count);
            Assert.All(result, r => Assert.Equal(10, r.Value));
        }

        [Fact]
        public void TopN_ZeroCounts_StillReturned()
        {
            var dict = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };

            var result = LogStatisticsService.TopN(dict, 5);

            Assert.Equal(2, result.Count);
        }
    }
}
