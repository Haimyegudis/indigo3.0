using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;
using Xunit;

namespace IndiLogs.Tests
{
    public class EmStatisticsServiceTests
    {
        private const string Header = "Name,StartTime,EndTime,Duration";

        private static string BuildCsv(params string[] dataRows)
        {
            return Header + Environment.NewLine + string.Join(Environment.NewLine, dataRows);
        }

        // --- Empty / null / whitespace input ---

        [Fact]
        public void ParseEmStatistics_NullContent_ReturnsEmpty()
        {
            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(null!);

            Assert.Empty(states);
            Assert.Empty(timestamps);
            Assert.Equal(0, totalLength);
        }

        [Fact]
        public void ParseEmStatistics_EmptyString_ReturnsEmpty()
        {
            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics("");

            Assert.Empty(states);
            Assert.Empty(timestamps);
            Assert.Equal(0, totalLength);
        }

        [Fact]
        public void ParseEmStatistics_WhitespaceOnly_ReturnsEmpty()
        {
            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics("   \t  ");

            Assert.Empty(states);
            Assert.Empty(timestamps);
            Assert.Equal(0, totalLength);
        }

        [Fact]
        public void ParseEmStatistics_HeaderOnly_ReturnsEmpty()
        {
            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(Header);

            Assert.Empty(states);
            Assert.Empty(timestamps);
            Assert.Equal(0, totalLength);
        }

        // --- Single valid row ---

        [Fact]
        public void ParseEmStatistics_SingleValidRow_ReturnsOneState()
        {
            var csv = BuildCsv("Idle,01:00:00,01:00:10,00:00:10");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Idle", states[0].Name);
            Assert.Equal("", states[0].Category);
            Assert.Equal(11, totalLength); // 10 seconds + 1
            Assert.Equal(11, timestamps.Length);
        }

        [Fact]
        public void ParseEmStatistics_SingleValidRow_IntervalIndicesCorrect()
        {
            var csv = BuildCsv("Idle,01:00:00,01:00:10,00:00:10");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            var interval = states[0].Intervals[0];
            Assert.Equal(0, interval.StartIndex);
            Assert.Equal(10, interval.EndIndex);
            Assert.Equal(0, interval.StateId);
        }

        [Fact]
        public void ParseEmStatistics_SingleValidRow_TooltipContainsName()
        {
            var csv = BuildCsv("Idle,01:00:00,01:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Contains("Idle", states[0].Intervals[0].TooltipText);
        }

        // --- Multiple valid rows ---

        [Fact]
        public void ParseEmStatistics_MultipleRows_ReturnsAllStates()
        {
            var csv = BuildCsv(
                "Idle,01:00:00,01:00:30,00:00:30",
                "Running,01:00:30,01:01:00,00:00:30",
                "Cleanup,01:01:00,01:01:10,00:00:10");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Equal(3, states.Count);
            Assert.Equal("Idle", states[0].Name);
            Assert.Equal("Running", states[1].Name);
            Assert.Equal("Cleanup", states[2].Name);
        }

        [Fact]
        public void ParseEmStatistics_MultipleRows_TimestampsSpanFullRange()
        {
            var csv = BuildCsv(
                "Idle,01:00:00,01:00:30,00:00:30",
                "Running,01:00:30,01:01:00,00:00:30");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            // Min start = 01:00:00, max end = 01:01:00 => 60 seconds + 1
            Assert.Equal(61, totalLength);
            Assert.Equal(61, timestamps.Length);
        }

        [Fact]
        public void ParseEmStatistics_MultipleRows_IndicesRelativeToMinTime()
        {
            var csv = BuildCsv(
                "Idle,02:00:00,02:00:10,00:00:10",
                "Running,02:00:20,02:00:30,00:00:10");

            var (states, _, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            // Min = 02:00:00, Max = 02:00:30, totalLength = 31
            Assert.Equal(31, totalLength);

            // Idle: start=0, end=10
            Assert.Equal(0, states[0].Intervals[0].StartIndex);
            Assert.Equal(10, states[0].Intervals[0].EndIndex);

            // Running: start=20, end=30
            Assert.Equal(20, states[1].Intervals[0].StartIndex);
            Assert.Equal(30, states[1].Intervals[0].EndIndex);
        }

        [Fact]
        public void ParseEmStatistics_MultipleRows_StateIdMatchesRowOrder()
        {
            var csv = BuildCsv(
                "A,00:00:00,00:00:05,00:00:05",
                "B,00:00:05,00:00:10,00:00:05",
                "C,00:00:10,00:00:15,00:00:05");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Equal(0, states[0].Intervals[0].StateId);
            Assert.Equal(1, states[1].Intervals[0].StateId);
            Assert.Equal(2, states[2].Intervals[0].StateId);
        }

        // --- Malformed rows ---

        [Fact]
        public void ParseEmStatistics_RowWithTooFewColumns_Skipped()
        {
            var csv = BuildCsv(
                "Idle,01:00:00",
                "Running,01:00:00,01:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Running", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_RowWithInvalidStartTime_Skipped()
        {
            var csv = BuildCsv(
                "Idle,NOTTIME,01:00:10,00:00:10",
                "Running,01:00:00,01:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Running", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_RowWithInvalidEndTime_Skipped()
        {
            var csv = BuildCsv(
                "Idle,01:00:00,BADTIME,00:00:10",
                "Running,01:00:00,01:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Running", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_RowWithEmptyName_Skipped()
        {
            var csv = BuildCsv(
                ",01:00:00,01:00:10,00:00:10",
                "Running,01:00:00,01:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Running", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_EndBeforeOrEqualStart_Skipped()
        {
            var csv = BuildCsv(
                "Backward,01:00:10,01:00:00,00:00:10",
                "Equal,01:00:00,01:00:00,00:00:00",
                "Valid,01:00:00,01:00:05,00:00:05");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Valid", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_AllRowsMalformed_ReturnsEmpty()
        {
            var csv = BuildCsv(
                "Bad1,INVALID,01:00:00,00:00:10",
                "Bad2,01:00:10,01:00:00,00:00:10",
                ",01:00:00,01:00:10,00:00:10");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Empty(states);
            Assert.Empty(timestamps);
            Assert.Equal(0, totalLength);
        }

        // --- Edge cases ---

        [Fact]
        public void ParseEmStatistics_QuotedValues_ParsedCorrectly()
        {
            var csv = BuildCsv("\"Idle State\",\"01:00:00\",\"01:00:10\",\"00:00:10\"");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Idle State", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_MissingDurationColumn_ComputesDuration()
        {
            var csv = Header + Environment.NewLine + "Idle,01:00:00,01:00:10";

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Contains("00:00:10", states[0].Intervals[0].TooltipText);
        }

        [Fact]
        public void ParseEmStatistics_OneSecondDuration_TotalLengthIsTwo()
        {
            var csv = BuildCsv("Brief,00:00:00,00:00:01,00:00:01");

            var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Equal(2, totalLength); // 1 second + 1
            Assert.Equal(2, timestamps.Length);
        }

        [Fact]
        public void ParseEmStatistics_TimestampsStartAtBaseDate()
        {
            var csv = BuildCsv("Idle,10:30:00,10:30:05,00:00:05");

            var (_, timestamps, _) = EmStatisticsService.ParseEmStatistics(csv);

            var expected = DateTime.Today.Add(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(30));
            Assert.Equal(expected, timestamps[0]);
        }

        [Fact]
        public void ParseEmStatistics_TimestampsIncrementByOneSecond()
        {
            var csv = BuildCsv("Idle,00:00:00,00:00:03,00:00:03");

            var (_, timestamps, _) = EmStatisticsService.ParseEmStatistics(csv);

            for (int i = 1; i < timestamps.Length; i++)
            {
                Assert.Equal(TimeSpan.FromSeconds(1), timestamps[i] - timestamps[i - 1]);
            }
        }

        [Fact]
        public void ParseEmStatistics_CrLfAndLfLineEndings_BothWork()
        {
            var csvLf = "Name,StartTime,EndTime,Duration\nIdle,01:00:00,01:00:05,00:00:05";
            var csvCrLf = "Name,StartTime,EndTime,Duration\r\nIdle,01:00:00,01:00:05,00:00:05";

            var (statesLf, _, _) = EmStatisticsService.ParseEmStatistics(csvLf);
            var (statesCrLf, _, _) = EmStatisticsService.ParseEmStatistics(csvCrLf);

            Assert.Single(statesLf);
            Assert.Single(statesCrLf);
        }

        [Fact]
        public void ParseEmStatistics_SingleDigitHour_Parsed()
        {
            var csv = BuildCsv("Idle,1:00:00,1:00:10,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Idle", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_MillisecondFormat_Parsed()
        {
            var csv = BuildCsv("Idle,01:00:00.000,01:00:10.500,00:00:10");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
        }

        [Fact]
        public void ParseEmStatistics_WhitespaceAroundValues_Trimmed()
        {
            var csv = BuildCsv("  Idle  ,  01:00:00  ,  01:00:10  ,  00:00:10  ");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            Assert.Single(states);
            Assert.Equal("Idle", states[0].Name);
        }

        [Fact]
        public void ParseEmStatistics_EachStateHasExactlyOneInterval()
        {
            var csv = BuildCsv(
                "A,00:00:00,00:00:05,00:00:05",
                "B,00:00:05,00:00:10,00:00:05");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            foreach (var state in states)
            {
                Assert.Single(state.Intervals);
            }
        }

        [Fact]
        public void ParseEmStatistics_TooltipContainsArrowAndDuration()
        {
            var csv = BuildCsv("Printing,02:30:00,02:45:00,00:15:00");

            var (states, _, _) = EmStatisticsService.ParseEmStatistics(csv);

            var tooltip = states[0].Intervals[0].TooltipText!;
            Assert.Contains("Printing", tooltip);
            Assert.Contains("\u2192", tooltip); // arrow character
            Assert.Contains("00:15:00", tooltip);
        }
    }
}
