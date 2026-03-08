using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Views;
using Xunit;

namespace IndiLogs.Tests
{
    public class PatternChartServiceTests
    {
        private static List<LogEntry> CreateLogs(params (string msg, DateTime dt)[] entries)
        {
            var logs = new List<LogEntry>();
            foreach (var (msg, dt) in entries)
                logs.Add(new LogEntry { Message = msg, Date = dt, Level = "INFO", ThreadName = "Test" });
            return logs;
        }

        [Fact]
        public void NumericPattern_ExtractsValues()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = CreateLogs(
                ("Temp: 25.3 C", baseTime),
                ("Temp: 26.1 C", baseTime.AddSeconds(1)),
                ("Temp: 27.5 C", baseTime.AddSeconds(2)),
                ("Unrelated log", baseTime.AddSeconds(3)));

            var package = PatternChartService.BuildFromPattern(
                logs, @"Temp:\s*(-?\d+\.?\d*)\s*C", "Temperature", PatternSearchField.Message, false, "Test");

            Assert.Single(package.Signals);
            Assert.Equal("Temperature", package.Signals[0].Name);
            Assert.Equal(SignalType.Analog, package.Signals[0].SignalType);
            Assert.NotNull(package.Signals[0].SparsePoints);
            Assert.Equal(3, package.Signals[0].SparsePoints!.Count);
        }

        [Fact]
        public void BooleanPattern_ExtractsAsBinary()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = CreateLogs(
                ("Valve: True", baseTime),
                ("Valve: False", baseTime.AddSeconds(1)),
                ("Valve: True", baseTime.AddSeconds(2)));

            var package = PatternChartService.BuildFromPattern(
                logs, @"Valve:\s*(True|False)", "Valve", PatternSearchField.Message, false, "Test");

            Assert.Single(package.Signals);
            Assert.Equal(SignalType.Digital, package.Signals[0].SignalType);
            Assert.Equal(3, package.Signals[0].SparsePoints!.Count);
            // True → 1.0
            Assert.Equal(1.0, package.Signals[0].SparsePoints![0].Value);
            // False → 0.0
            Assert.Equal(0.0, package.Signals[0].SparsePoints![1].Value);
        }

        [Fact]
        public void NoMatch_ReturnsEmptySparsePoints()
        {
            var logs = CreateLogs(("No data here", new DateTime(2025, 1, 1)));

            var package = PatternChartService.BuildFromPattern(
                logs, @"Pressure:\s*(\d+)", "Pressure", PatternSearchField.Message, false, "Test");

            Assert.Single(package.Signals);
            Assert.Empty(package.Signals[0].SparsePoints!);
        }

        [Fact]
        public void SearchField_Logger_MatchesLoggerField()
        {
            var baseTime = new DateTime(2025, 1, 1);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "irrelevant", Logger = "Temp.Sensor.25", Date = baseTime, Level = "INFO", ThreadName = "T" },
                new LogEntry { Message = "irrelevant", Logger = "Temp.Sensor.30", Date = baseTime.AddSeconds(1), Level = "INFO", ThreadName = "T" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"Temp\.Sensor\.(\d+)", "FromLogger", PatternSearchField.Logger, false, "Test");

            Assert.Equal(2, package.Signals[0].SparsePoints!.Count);
        }

        [Fact]
        public void IncludeState_ParsesS6Transitions()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "Temp: 25 C", Date = baseTime, Level = "INFO", ThreadName = "Test" },
                new LogEntry { Message = "PlcMngr: INIT -> STANDBY", Date = baseTime.AddSeconds(1), Level = "INFO", ThreadName = "Manager" },
                new LogEntry { Message = "Temp: 26 C", Date = baseTime.AddSeconds(2), Level = "INFO", ThreadName = "Test" },
                new LogEntry { Message = "PlcMngr: STANDBY -> READY", Date = baseTime.AddSeconds(3), Level = "INFO", ThreadName = "Manager" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"Temp:\s*(\d+)", "Temp", PatternSearchField.Message, true, "Test");

            Assert.NotEmpty(package.States);
            Assert.Equal("MachineState", package.States[0].Name);
            Assert.True(package.States[0].Intervals.Count >= 2);
        }

        [Fact]
        public void TimestampsAreUnique()
        {
            var baseTime = new DateTime(2025, 1, 1);
            var logs = CreateLogs(
                ("Temp: 1", baseTime),
                ("Temp: 2", baseTime),   // same timestamp
                ("Temp: 3", baseTime.AddSeconds(1)));

            var package = PatternChartService.BuildFromPattern(
                logs, @"Temp:\s*(\d+)", "T", PatternSearchField.Message, false, "Test");

            Assert.Equal(2, package.TimeStamps.Count);
        }
    }
}
