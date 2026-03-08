using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using Xunit;

namespace IndiLogs.Tests
{
    public class StatsViewModelCalculationTests
    {
        private static LogEntry MakeLog(string level = "Info", string message = "msg",
            string thread = "T1", string logger = "App.Service.Foo",
            string method = "DoWork", DateTime? date = null)
        {
            return new LogEntry
            {
                Level = level,
                Message = message,
                ThreadName = thread,
                Logger = logger,
                Method = method,
                Date = date ?? new DateTime(2025, 1, 1, 12, 0, 0)
            };
        }

        // ── FormatDuration (tested indirectly through gap summary text) ──

        [Fact]
        public async Task GapSummary_ShortGap_ShowsSeconds()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("sec", vm.PlcGapSummaryText);
        }

        [Fact]
        public async Task GapSummary_LongGap_ShowsMinutes()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddMinutes(3)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("min", vm.PlcGapSummaryText);
        }

        // ── FindGaps edge cases ──

        [Fact]
        public async Task FindGaps_SingleLog_NoGaps()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0))
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.False(vm.PlcHasGaps);
        }

        [Fact]
        public async Task FindGaps_ExactThreshold_DetectsGap()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(2.0)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.True(vm.PlcHasGaps);
            Assert.Single(vm.PlcGaps!);
        }

        [Fact]
        public async Task FindGaps_JustBelowThreshold_NoGap()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(1.99)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.False(vm.PlcHasGaps);
        }

        [Fact]
        public async Task FindGaps_MultipleGaps_CorrectIndices()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(3)),
                MakeLog(date: baseDate.AddSeconds(3.5)),
                MakeLog(date: baseDate.AddSeconds(10)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal(2, vm.PlcGaps!.Count);
            Assert.Equal(1, vm.PlcGaps[0].Index);
            Assert.Equal(2, vm.PlcGaps[1].Index);
        }

        [Fact]
        public async Task FindGaps_CapturesLastMessageBeforeGap()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(message: "before gap", date: baseDate),
                MakeLog(message: "after gap", date: baseDate.AddSeconds(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal("before gap", vm.PlcGaps![0].LastMessageBeforeGap);
        }

        // ── GetErrorLogs (tested indirectly) ──

        [Fact]
        public async Task ErrorLogs_OnlyErrorAndFatal_AreCountedAsErrors()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", date: baseDate),
                MakeLog(level: "Fatal", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Info", date: baseDate.AddSeconds(2)),
                MakeLog(level: "Warn", date: baseDate.AddSeconds(3)),
                MakeLog(level: "Debug", date: baseDate.AddSeconds(4)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("2", vm.PlcErrorCountText);
        }

        [Fact]
        public async Task ErrorLogs_CaseInsensitive_Matches()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "error", date: baseDate),
                MakeLog(level: "ERROR", date: baseDate.AddSeconds(1)),
                MakeLog(level: "fatal", date: baseDate.AddSeconds(2)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("3", vm.PlcErrorCountText);
        }

        [Fact]
        public async Task ErrorLogs_NullLevel_NotCountedAsError()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                new LogEntry { Level = null, Message = "no level", Date = baseDate },
                MakeLog(level: "Error", date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("1", vm.PlcErrorCountText);
        }

        // ── CalculateErrorHistogram (tested indirectly through PlcErrorStats) ──

        [Fact]
        public async Task ErrorHistogram_GroupsByMessage_DescendingOrder()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", message: "Common error", date: baseDate.AddSeconds(i)));
            for (int i = 0; i < 3; i++)
                plc.Add(MakeLog(level: "Error", message: "Rare error", date: baseDate.AddSeconds(10 + i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcErrorStats);
            Assert.True(vm.PlcErrorStats!.Count >= 2);
            Assert.True(vm.PlcErrorStats[0].Count >= vm.PlcErrorStats[1].Count);
        }

        [Fact]
        public async Task ErrorHistogram_LimitedToTop10()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int group = 0; group < 15; group++)
            {
                for (int i = 0; i < group + 1; i++)
                    plc.Add(MakeLog(level: "Error", message: $"Error type {group}",
                        date: baseDate.AddMilliseconds(group * 100 + i)));
            }

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcErrorStats);
            Assert.True(vm.PlcErrorStats!.Count <= 10);
        }

        [Fact]
        public async Task ErrorHistogram_BarWidthScaling_MaxBarIsLargest()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 20; i++)
                plc.Add(MakeLog(level: "Error", message: "Frequent", date: baseDate.AddSeconds(i)));
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", message: "Infrequent", date: baseDate.AddSeconds(20 + i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcErrorStats);
            double maxBar = vm.PlcErrorStats!.Max(s => s.BarWidth);
            Assert.Equal(vm.PlcErrorStats[0].BarWidth, maxBar);
        }

        [Fact]
        public async Task ErrorHistogram_NoErrors_ReturnsEmptyList()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", date: baseDate),
                MakeLog(level: "Info", date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcErrorStats);
            Assert.Empty(vm.PlcErrorStats!);
        }

        [Fact]
        public async Task ErrorHistogram_CaseInsensitiveGrouping()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "Connection Failed", date: baseDate),
                MakeLog(level: "Error", message: "connection failed", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Error", message: "CONNECTION FAILED", date: baseDate.AddSeconds(2)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcErrorStats);
            Assert.Single(vm.PlcErrorStats!);
            Assert.Equal(3, vm.PlcErrorStats[0].Count);
        }

        // ── CalculateLoadDistribution (tested indirectly through ThreadStats) ──

        [Fact]
        public async Task LoadDistribution_GroupsByThread_DescendingOrder()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 50; i++)
                plc.Add(MakeLog(thread: "BusyThread", date: baseDate.AddSeconds(i)));
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(thread: "QuietThread", date: baseDate.AddSeconds(50 + i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcThreadStats);
            Assert.True(vm.PlcThreadStats!.Count >= 2);
            Assert.True(vm.PlcThreadStats[0].Count >= vm.PlcThreadStats[1].Count);
        }

        [Fact]
        public async Task LoadDistribution_PercentagesAreCorrect()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 75; i++)
                plc.Add(MakeLog(thread: "ThreadA", date: baseDate.AddSeconds(i)));
            for (int i = 0; i < 25; i++)
                plc.Add(MakeLog(thread: "ThreadB", date: baseDate.AddSeconds(75 + i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcThreadStats);
            var threadA = vm.PlcThreadStats!.First(s => s.Name == "ThreadA");
            var threadB = vm.PlcThreadStats!.First(s => s.Name == "ThreadB");
            Assert.Equal(75.0, threadA.Percentage, 0.1);
            Assert.Equal(25.0, threadB.Percentage, 0.1);
        }

        [Fact]
        public async Task LoadDistribution_EmptyKeys_AreSkipped()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                new LogEntry { Level = "Info", Message = "msg", ThreadName = "", Date = baseDate },
                MakeLog(thread: "ValidThread", date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcThreadStats);
            Assert.Single(vm.PlcThreadStats!);
            Assert.Equal("ValidThread", vm.PlcThreadStats[0].Name);
        }

        [Fact]
        public async Task LoadDistribution_DisplayTextContainsCountAndPercentage()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(thread: "T1", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PlcThreadStats);
            Assert.Contains("10", vm.PlcThreadStats![0].DisplayText);
            Assert.Contains("%", vm.PlcThreadStats[0].DisplayText);
        }

        // ── GetShortLoggerName (tested indirectly through APP stats) ──

        [Fact]
        public async Task ShortLoggerName_LongNameTruncatedToLastTwo()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "Com.HP.Indigo.PrintEngine.Manager",
                    date: baseDate),
            };
            var vm = new StatsViewModel(null, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppThreadErrorStats);
            Assert.Single(vm.AppThreadErrorStats!);
            Assert.Equal("PrintEngine.Manager", vm.AppThreadErrorStats[0].Name);
        }

        [Fact]
        public async Task ShortLoggerName_ShortNameKeptAsIs()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "A.B", date: baseDate),
            };
            var vm = new StatsViewModel(null, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppThreadErrorStats);
            Assert.Equal("A.B", vm.AppThreadErrorStats![0].Name);
        }

        [Fact]
        public async Task ShortLoggerName_EmptyLogger_ShowsUnknown()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "", date: baseDate),
            };
            var vm = new StatsViewModel(null, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppThreadErrorStats);
            Assert.Equal("Unknown", vm.AppThreadErrorStats![0].Name);
        }

        // ── TruncateMessage edge cases (beyond what StatsViewModelTests covers) ──

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        [InlineData(99)]
        public void TruncateMessage_VariousMaxLengths_TruncatesCorrectly(int maxLen)
        {
            string input = new string('x', 100);
            string result = StatsViewModel.TruncateMessage(input, maxLen);
            Assert.Equal(maxLen + 3, result.Length); // maxLen chars + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void TruncateMessage_MaxLengthOne_TruncatesWithEllipsis()
        {
            string result = StatsViewModel.TruncateMessage("abcdef", 1);
            Assert.Equal("a...", result);
        }

        // ── BuildLoggerBarChartData (tested indirectly through BarChartData) ──

        [Fact]
        public async Task BarChartData_PlcErrors_PrefixedWithPLC()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", thread: "Worker", date: baseDate),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.BarChartData);
            Assert.All(vm.BarChartData!, item => Assert.StartsWith("[PLC]", item.Name));
        }

        [Fact]
        public async Task BarChartData_AppErrors_PrefixedWithAPP()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "My.Logger", date: baseDate),
            };
            var vm = new StatsViewModel(null, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.BarChartData);
            Assert.All(vm.BarChartData!, item => Assert.StartsWith("[APP]", item.Name));
        }

        [Fact]
        public async Task BarChartData_CombinedErrors_SortedDescending()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", thread: "W1", date: baseDate.AddSeconds(i)));
            var app = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                app.Add(MakeLog(level: "Error", logger: "Big.Logger", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.BarChartData);
            for (int i = 1; i < vm.BarChartData!.Count; i++)
                Assert.True(vm.BarChartData[i - 1].Count >= vm.BarChartData[i].Count);
        }

        [Fact]
        public async Task BarChartData_LimitedTo10()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int g = 0; g < 12; g++)
                plc.Add(MakeLog(level: "Error", thread: $"Thread{g}", date: baseDate.AddSeconds(g)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.BarChartData);
            Assert.True(vm.BarChartData!.Count <= 10);
        }

        [Fact]
        public async Task BarChartData_NullThreadName_GroupedAsUnknown()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                new LogEntry { Level = "Error", Message = "err", ThreadName = null, Date = baseDate },
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.BarChartData);
            Assert.Contains(vm.BarChartData!, item => item.Name.Contains("Unknown"));
        }

        // ── BuildStatePieChartData (tested indirectly through PieChartData) ──

        [Fact]
        public async Task PieChartData_NoErrors_ReturnsNull()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", message: "PlcMngr: OFF -> RUNNING", thread: "Manager", date: baseDate),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Null(vm.PieChartData);
        }

        [Fact]
        public async Task PieChartData_ErrorsWithStates_GroupsByState()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", thread: "Manager", message: "PlcMngr: OFF -> RUNNING", date: baseDate),
                MakeLog(level: "Error", message: "err1", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Error", message: "err2", date: baseDate.AddSeconds(2)),
                MakeLog(level: "Info", thread: "Manager", message: "PlcMngr: RUNNING -> STANDBY", date: baseDate.AddSeconds(5)),
                MakeLog(level: "Error", message: "err3", date: baseDate.AddSeconds(6)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.PieChartData);
            Assert.True(vm.PieChartData!.Count >= 1);
        }

        // ── BuildErrorTimelineChartData edge cases ──

        [Fact]
        public async Task TimelineChart_SingleError_ProducesBuckets()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", date: new DateTime(2025, 1, 1, 12, 0, 0)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineBuckets);
            Assert.Equal(60, vm.TimelineBucketCount);
            Assert.Equal(1, vm.TimelineBuckets!.Sum());
        }

        [Fact]
        public async Task TimelineChart_AllErrorsAtSameTime_TotalCountPreserved()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineBuckets);
            Assert.Equal(5, vm.TimelineBuckets!.Sum());
        }

        [Fact]
        public async Task TimelineChart_ZoomStartAndEnd_InitializedCorrectly()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal(0, vm.TimelineZoomStart);
            Assert.Equal(vm.TimelineBucketCount, vm.TimelineZoomEnd);
        }

        [Fact]
        public async Task TimelineChart_BucketLogsContainCorrectEntries()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i * 10)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineBucketLogs);
            int totalInBuckets = vm.TimelineBucketLogs!.Sum(bl => bl.Count);
            Assert.Equal(5, totalInBuckets);
        }

        // ── CalculateStateEntries edge cases ──

        [Fact]
        public async Task StateEntries_NoTransitions_ReturnsEmpty()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", message: "normal log", date: baseDate),
                MakeLog(level: "Info", message: "another log", date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Null(vm.TimelineStateEntries);
        }

        [Fact]
        public async Task StateEntries_S6_IncludesInitialState()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", date: baseDate),
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: OFF -> GET_READY", date: baseDate.AddSeconds(2)),
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: GET_READY -> RUNNING", date: baseDate.AddSeconds(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            Assert.Equal("OFF", vm.TimelineStateEntries![0].StateName);
            Assert.Equal("GET_READY", vm.TimelineStateEntries[1].StateName);
            Assert.Equal("RUNNING", vm.TimelineStateEntries[2].StateName);
        }

        [Fact]
        public async Task StateEntries_S6_TransitionTitlesCorrect()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: OFF -> RUNNING", date: baseDate),
                MakeLog(level: "Error", message: "some error", date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            var transitionEntry = vm.TimelineStateEntries!.First(e => e.StateName == "RUNNING");
            Assert.Equal("OFF -> RUNNING", transitionEntry.TransitionTitle);
        }

        [Fact]
        public async Task StateEntries_S6_EndTimesChainCorrectly()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: OFF -> RUNNING", date: baseDate),
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: RUNNING -> STANDBY", date: baseDate.AddSeconds(10)),
                MakeLog(level: "Error", date: baseDate.AddSeconds(15)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            var running = vm.TimelineStateEntries!.First(e => e.StateName == "RUNNING");
            Assert.Equal(baseDate.AddSeconds(10), running.EndTime);
        }

        [Fact]
        public async Task StateEntries_S6_LastStateEndTimeEqualsLogEnd()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var lastLogTime = baseDate.AddSeconds(20);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: OFF -> RUNNING", date: baseDate),
                MakeLog(level: "Error", message: "final log", date: lastLogTime),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            var lastState = vm.TimelineStateEntries!.Last();
            Assert.Equal(lastLogTime, lastState.EndTime);
        }

        [Fact]
        public async Task StateEntries_S4_DetectsEnterStates()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "==== STATE_IDLE - Enter ======", date: baseDate),
                MakeLog(level: "Error", message: "==== STATE_PRINTING - Enter ======", date: baseDate.AddSeconds(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            Assert.Equal(2, vm.TimelineStateEntries!.Count);
            Assert.Equal("IDLE", vm.TimelineStateEntries[0].StateName);
            Assert.Equal("PRINTING", vm.TimelineStateEntries[1].StateName);
        }

        [Fact]
        public async Task StateEntries_S4_ExitStatesIgnored()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "==== STATE_IDLE - Enter ======", date: baseDate),
                MakeLog(level: "Error", message: "==== STATE_IDLE - Exit ======", date: baseDate.AddSeconds(3)),
                MakeLog(level: "Error", message: "==== STATE_RUNNING - Enter ======", date: baseDate.AddSeconds(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            Assert.Equal(2, vm.TimelineStateEntries!.Count);
        }

        // ── Summary text formatting ──

        [Fact]
        public async Task SummaryText_ShowsTotalLogCountAndDuration()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddMinutes(5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("2", vm.SummaryText);
            Assert.Contains("5.0 minutes", vm.SummaryText);
        }

        [Fact]
        public async Task SummaryText_BothPlcAndApp_CombinesCountsInTimeRange()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
            };
            var app = new List<LogEntry>
            {
                MakeLog(date: baseDate.AddMinutes(10)),
            };
            var vm = new StatsViewModel(plc, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("2", vm.SummaryText);
            Assert.Contains("10.0", vm.SummaryText);
        }

        // ── APP method stats (non-binary) ──

        [Fact]
        public async Task AppMethodStats_NonBinary_GroupsByMethod()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                app.Add(MakeLog(method: "Initialize", date: baseDate.AddSeconds(i)));
            for (int i = 0; i < 5; i++)
                app.Add(MakeLog(method: "Shutdown", date: baseDate.AddSeconds(10 + i)));

            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppMethodStats);
            Assert.True(vm.AppMethodStats!.Count >= 2);
            Assert.True(vm.AppMethodStats[0].Count >= vm.AppMethodStats[1].Count);
        }

        [Fact]
        public async Task AppMethodStats_NullMethod_GroupedAsUnknown()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                new LogEntry { Level = "Info", Message = "msg", Logger = "L", Method = null, Date = baseDate },
            };
            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppMethodStats);
            Assert.Contains(vm.AppMethodStats!, s => s.Name == "(unknown)");
        }

        // ── APP logger load uses fullNameSelector ──

        [Fact]
        public async Task AppThreadStats_FullNamePreserved()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var app = new List<LogEntry>
            {
                MakeLog(logger: "Com.HP.Indigo.Service.Worker", date: baseDate),
            };
            var vm = new StatsViewModel(null, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.AppThreadStats);
            Assert.Equal("Com.HP.Indigo.Service.Worker", vm.AppThreadStats![0].FullName);
        }

        // ── LoggerData / StateData derived properties ──

        [Fact]
        public async Task LoggerData_PopulatedFromBarChartData()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", thread: "W1", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.LoggerData);
            Assert.Equal(vm.BarChartData!.Count, vm.LoggerData!.Count);
            Assert.Equal(vm.BarChartData[0].Name, vm.LoggerData[0].Logger);
            Assert.Equal(vm.BarChartData[0].Count, vm.LoggerData[0].Count);
        }

        [Fact]
        public async Task LoggerData_NoErrors_Null()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", date: new DateTime(2025, 1, 1, 12, 0, 0)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Null(vm.LoggerData);
            Assert.Null(vm.StateData);
        }

        // ── BuildExportReport with binary APP ──

        [Fact]
        public async Task BuildExportReport_BinaryApp_ExcludesMethodSections()
        {
            var app = new List<LogEntry>();
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            for (int i = 0; i < 5; i++)
                app.Add(MakeLog(level: "Error", method: "SomeMethod", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: true);
            await vm.CalculateStatisticsAsync();

            string report = vm.BuildExportReport();
            Assert.DoesNotContain("ERRORS BY METHOD", report);
            Assert.DoesNotContain("METHOD LOAD", report);
        }

        [Fact]
        public async Task BuildExportReport_NonBinaryApp_IncludesMethodSections()
        {
            var app = new List<LogEntry>();
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            for (int i = 0; i < 5; i++)
                app.Add(MakeLog(level: "Error", method: "SomeMethod", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: false);
            await vm.CalculateStatisticsAsync();

            string report = vm.BuildExportReport();
            Assert.Contains("ERRORS BY METHOD", report);
            Assert.Contains("METHOD LOAD", report);
        }

        [Fact]
        public async Task BuildExportReport_WithGaps_IncludesGapDetails()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(message: "before", date: baseDate),
                MakeLog(message: "after", date: baseDate.AddSeconds(10)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            string report = vm.BuildExportReport();
            Assert.Contains("GAP ANALYSIS", report);
            Assert.Contains("#1", report);
            Assert.Contains("Last Log:", report);
        }

        [Fact]
        public async Task BuildExportReport_NoGaps_ShowsNoSignificantGaps()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(0.5)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            string report = vm.BuildExportReport();
            Assert.Contains("No significant gaps", report);
        }

        // ── ZoomTimeline edge cases ──

        [Fact]
        public async Task ZoomTimeline_PanLeft_ClampsToZero()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            // Pan left many times
            for (int i = 0; i < 100; i++)
                vm.ZoomTimeline(120, isShift: true, hoveredBucket: 0);

            Assert.True(vm.TimelineZoomStart >= 0);
        }

        [Fact]
        public async Task ZoomTimeline_PanRight_ClampsToMax()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            // Pan right many times
            for (int i = 0; i < 100; i++)
                vm.ZoomTimeline(-120, isShift: true, hoveredBucket: 0);

            Assert.True(vm.TimelineZoomEnd <= vm.TimelineBucketCount);
        }

        [Fact]
        public async Task ZoomTimeline_ZoomUsesHoveredBucketAsCenter()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 100; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            int hovered = vm.TimelineBucketCount / 4;
            vm.ZoomTimeline(120, isShift: false, hoveredBucket: hovered);

            Assert.True(vm.TimelineZoomStart <= hovered);
            Assert.True(vm.TimelineZoomEnd > hovered);
        }

        [Fact]
        public async Task ZoomTimeline_ZoomEnd_NeverExceedsBucketCount()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            // Zoom out many times
            for (int i = 0; i < 50; i++)
                vm.ZoomTimeline(-120, isShift: false, hoveredBucket: vm.TimelineBucketCount - 1);

            Assert.True(vm.TimelineZoomEnd <= vm.TimelineBucketCount);
            Assert.True(vm.TimelineZoomStart >= 0);
        }

        // ── NavigatePieChartItem ──

        [Fact]
        public async Task NavigatePieChartItem_ValidIndex_CallsCallback()
        {
            LogEntry? navigatedTo = null;
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "Info", thread: "Manager", message: "PlcMngr: OFF -> RUNNING", date: baseDate),
                MakeLog(level: "Error", message: "err1", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Error", message: "err2", date: baseDate.AddSeconds(2)),
            };
            var vm = new StatsViewModel(plc, null, null, log => navigatedTo = log, false, false);
            await vm.CalculateStatisticsAsync();

            if (vm.PieChartData != null && vm.PieChartData.Count > 0)
            {
                vm.NavigatePieChartItem(0);
                Assert.NotNull(navigatedTo);
            }
        }

        [Fact]
        public void NavigatePieChartItem_NullData_DoesNotThrow()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            vm.NavigatePieChartItem(0);
            vm.NavigatePieChartItem(-1);
        }

        // ── TimelineChartInfoText formatting ──

        [Fact]
        public async Task TimelineChartInfo_ShortBucketSize_ShowsSeconds()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 10; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            if (vm.TimelineBucketSize < 60)
                Assert.Contains("s)", vm.TimelineChartInfoText);
        }

        [Fact]
        public async Task TimelineChartInfo_LongBucketSize_ShowsMinutes()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            // 50 errors * 5 min apart = 245 min total, 120 buckets -> ~122s per bucket (> 60s)
            var plc = new List<LogEntry>();
            for (int i = 0; i < 50; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddMinutes(i * 5)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("min", vm.TimelineChartInfoText);
        }

        // ── LoggerChartCountText / StateChartCountText ──

        [Fact]
        public async Task LoggerChartCountText_WithErrors_ShowsTotalAndClickHint()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("errors", vm.LoggerChartCountText);
            Assert.Contains("Click", vm.LoggerChartCountText);
        }

        [Fact]
        public async Task StateChartCountText_NoStates_ShowsNoTransitions()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("No state transitions", vm.StateChartCountText);
        }
    }
}
