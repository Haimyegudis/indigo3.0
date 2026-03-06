using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using Xunit;

namespace IndiLogs.Tests
{
    public class StatsViewModelTests
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

        private static List<LogEntry> MakePlcLogs(int count, int errorCount = 0, double secondsApart = 1.0)
        {
            var logs = new List<LogEntry>();
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            for (int i = 0; i < count; i++)
            {
                bool isError = i < errorCount;
                logs.Add(MakeLog(
                    level: isError ? "Error" : "Info",
                    message: isError ? $"Error message {i}" : $"Info message {i}",
                    thread: $"Thread{i % 3}",
                    date: baseDate.AddSeconds(i * secondsApart)));
            }
            return logs;
        }

        // ── TruncateMessage ──

        [Fact]
        public void TruncateMessage_ShortString_ReturnsUnchanged()
        {
            Assert.Equal("hello", StatsViewModel.TruncateMessage("hello", 100));
        }

        [Fact]
        public void TruncateMessage_ExactLength_ReturnsUnchanged()
        {
            Assert.Equal("abcde", StatsViewModel.TruncateMessage("abcde", 5));
        }

        [Fact]
        public void TruncateMessage_LongString_TruncatesWithEllipsis()
        {
            Assert.Equal("abcde...", StatsViewModel.TruncateMessage("abcdefghij", 5));
        }

        [Fact]
        public void TruncateMessage_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", StatsViewModel.TruncateMessage(null!, 100));
            Assert.Equal("(empty)", StatsViewModel.TruncateMessage("", 100));
        }

        // ── Constructor ──

        [Fact]
        public void Constructor_NullLogs_CreatesEmptyLists()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            Assert.Equal("Analyzing logs...", vm.SummaryText);
            Assert.True(vm.IsLoading);
        }

        // ── CalculateStatisticsAsync — empty logs ──

        [Fact]
        public async Task CalculateStatisticsAsync_NoLogs_SetsNoLogsMessage()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal("No logs available for analysis.", vm.SummaryText);
            Assert.False(vm.IsLoading);
            Assert.False(vm.PlcHasLogs);
            Assert.False(vm.AppHasLogs);
        }

        // ── CalculateStatisticsAsync — PLC only ──

        [Fact]
        public async Task CalculateStatisticsAsync_PlcOnly_SetsPlcStats()
        {
            var plcLogs = MakePlcLogs(50, errorCount: 5);
            var vm = new StatsViewModel(plcLogs, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.False(vm.IsLoading);
            Assert.True(vm.PlcHasLogs);
            Assert.False(vm.AppHasLogs);
            Assert.Contains("50", vm.PlcSummaryText);
            Assert.Contains("5", vm.PlcErrorCountText);
            Assert.NotNull(vm.PlcErrorStats);
            Assert.NotNull(vm.PlcThreadStats);
            Assert.Equal("No APP logs available.", vm.AppSummaryText);
        }

        // ── CalculateStatisticsAsync — APP only ──

        [Fact]
        public async Task CalculateStatisticsAsync_AppOnly_SetsAppStats()
        {
            var appLogs = MakePlcLogs(30, errorCount: 3);
            var vm = new StatsViewModel(null, appLogs, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.False(vm.IsLoading);
            Assert.False(vm.PlcHasLogs);
            Assert.True(vm.AppHasLogs);
            Assert.Contains("30", vm.AppSummaryText);
        }

        // ── CalculateStatisticsAsync — both logs ──

        [Fact]
        public async Task CalculateStatisticsAsync_BothLogs_SetsSummary()
        {
            var plc = MakePlcLogs(20, errorCount: 2);
            var app = MakePlcLogs(10, errorCount: 1);
            var vm = new StatsViewModel(plc, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("30", vm.SummaryText);
            Assert.True(vm.PlcHasLogs);
            Assert.True(vm.AppHasLogs);
        }

        // ── CalculateStatisticsAsync — errors produce analytics ──

        [Fact]
        public async Task CalculateStatisticsAsync_WithErrors_ProducesAnalytics()
        {
            var plc = MakePlcLogs(100, errorCount: 20, secondsApart: 0.5);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("Advanced Analytics", vm.AnalyticsSummaryText);
            Assert.NotNull(vm.BarChartData);
            Assert.True(vm.BarChartData!.Count > 0);
            Assert.NotNull(vm.TimelineBuckets);
            Assert.True(vm.TimelineBucketCount > 0);
        }

        // ── CalculateStatisticsAsync — no errors produces no-analytics message ──

        [Fact]
        public async Task CalculateStatisticsAsync_NoErrors_ShowsNoAnalytics()
        {
            var plc = MakePlcLogs(10, errorCount: 0);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Contains("No error logs", vm.AnalyticsSummaryText);
            Assert.Null(vm.BarChartData);
        }

        // ── Gap detection ──

        [Fact]
        public async Task CalculateStatisticsAsync_WithGaps_DetectsGaps()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(0.5)),
                MakeLog(date: baseDate.AddSeconds(5)),   // 4.5s gap
                MakeLog(date: baseDate.AddSeconds(5.1)),
                MakeLog(date: baseDate.AddSeconds(10)),  // 4.9s gap
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.True(vm.PlcHasGaps);
            Assert.NotNull(vm.PlcGaps);
            Assert.Equal(2, vm.PlcGaps!.Count);
            Assert.Contains("2 gap(s)", vm.PlcGapSummaryText);
        }

        [Fact]
        public async Task CalculateStatisticsAsync_NoGaps_ShowsNoGaps()
        {
            var plc = MakePlcLogs(5, secondsApart: 0.5);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.False(vm.PlcHasGaps);
            Assert.Contains("No significant", vm.PlcGapSummaryText);
        }

        // ── Binary APP logs ──

        [Fact]
        public async Task CalculateStatisticsAsync_BinaryApp_SkipsMethodStats()
        {
            var app = MakePlcLogs(20, errorCount: 3);
            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: true);
            await vm.CalculateStatisticsAsync();

            Assert.True(vm.AppHasLogs);
            Assert.Null(vm.AppMethodErrorStats);
            Assert.Null(vm.AppMethodStats);
        }

        [Fact]
        public async Task CalculateStatisticsAsync_NonBinaryApp_HasMethodStats()
        {
            var app = MakePlcLogs(20, errorCount: 3);
            var vm = new StatsViewModel(null, app, null, null, false, hasBinaryAppLogs: false);
            await vm.CalculateStatisticsAsync();

            Assert.True(vm.AppHasLogs);
            Assert.NotNull(vm.AppMethodErrorStats);
            Assert.NotNull(vm.AppMethodStats);
        }

        // ── BuildExportReport ──

        [Fact]
        public async Task BuildExportReport_ContainsAllSections()
        {
            var plc = MakePlcLogs(10, errorCount: 2);
            var app = MakePlcLogs(5, errorCount: 1);
            var vm = new StatsViewModel(plc, app, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            string report = vm.BuildExportReport();
            Assert.Contains("LOG STATISTICS REPORT", report);
            Assert.Contains("PLC Logs: 10", report);
            Assert.Contains("APP Logs: 5", report);
            Assert.Contains("PLC LOGS STATISTICS", report);
            Assert.Contains("APP LOGS STATISTICS", report);
            Assert.Contains("GAP ANALYSIS", report);
        }

        // ── ZoomTimeline ──

        [Fact]
        public async Task ZoomTimeline_ZoomIn_ReducesVisibleRange()
        {
            var plc = MakePlcLogs(100, errorCount: 20, secondsApart: 1.0);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            int originalRange = vm.TimelineZoomEnd - vm.TimelineZoomStart;
            vm.ZoomTimeline(120, isShift: false, hoveredBucket: vm.TimelineBucketCount / 2);
            int newRange = vm.TimelineZoomEnd - vm.TimelineZoomStart;

            Assert.True(newRange <= originalRange);
        }

        [Fact]
        public async Task ZoomTimeline_ZoomOut_IncreasesOrMaintainsRange()
        {
            var plc = MakePlcLogs(100, errorCount: 20, secondsApart: 1.0);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            // Zoom in first
            vm.ZoomTimeline(120, isShift: false, hoveredBucket: vm.TimelineBucketCount / 2);
            int zoomedRange = vm.TimelineZoomEnd - vm.TimelineZoomStart;

            // Zoom out
            vm.ZoomTimeline(-120, isShift: false, hoveredBucket: vm.TimelineBucketCount / 2);
            int afterZoomOut = vm.TimelineZoomEnd - vm.TimelineZoomStart;

            Assert.True(afterZoomOut >= zoomedRange);
        }

        [Fact]
        public async Task ZoomTimeline_Pan_ShiftsRange()
        {
            var plc = MakePlcLogs(100, errorCount: 20, secondsApart: 1.0);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            // Zoom in first so we can pan
            vm.ZoomTimeline(120, isShift: false, hoveredBucket: vm.TimelineBucketCount / 2);
            vm.ZoomTimeline(120, isShift: false, hoveredBucket: vm.TimelineBucketCount / 2);
            int startBefore = vm.TimelineZoomStart;
            int visibleBefore = vm.TimelineZoomEnd - vm.TimelineZoomStart;

            // Pan right (shift + negative delta)
            vm.ZoomTimeline(-120, isShift: true, hoveredBucket: 0);
            int startAfter = vm.TimelineZoomStart;
            int visibleAfter = vm.TimelineZoomEnd - vm.TimelineZoomStart;

            Assert.Equal(visibleBefore, visibleAfter);
            Assert.True(startAfter >= startBefore);
        }

        [Fact]
        public void ZoomTimeline_NoData_DoesNotThrow()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            vm.ZoomTimeline(120, false, 0); // Should not throw
        }

        // ── NavigateBarChartItem ──

        [Fact]
        public async Task NavigateBarChartItem_ValidIndex_CallsCallback()
        {
            LogEntry? navigatedTo = null;
            var plc = MakePlcLogs(50, errorCount: 10, secondsApart: 0.5);
            var vm = new StatsViewModel(plc, null, null, log => navigatedTo = log, false, false);
            await vm.CalculateStatisticsAsync();

            if (vm.BarChartData != null && vm.BarChartData.Count > 0)
            {
                vm.NavigateBarChartItem(0);
                Assert.NotNull(navigatedTo);
            }
        }

        [Fact]
        public async Task NavigateBarChartItem_InvalidIndex_DoesNothing()
        {
            var plc = MakePlcLogs(50, errorCount: 10);
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            vm.NavigateBarChartItem(-1);  // Should not throw
            vm.NavigateBarChartItem(999); // Should not throw
        }

        // ── NavigateTimelineBucket ──

        [Fact]
        public async Task NavigateTimelineBucket_ValidBucket_CallsCallback()
        {
            LogEntry? navigatedTo = null;
            var plc = MakePlcLogs(50, errorCount: 10, secondsApart: 0.5);
            var vm = new StatsViewModel(plc, null, null, log => navigatedTo = log, false, false);
            await vm.CalculateStatisticsAsync();

            if (vm.TimelineBucketLogs != null && vm.TimelineBucketCount > 0)
            {
                // Find a bucket with logs
                for (int i = 0; i < vm.TimelineBucketCount; i++)
                {
                    if (vm.TimelineBucketLogs[i].Count > 0)
                    {
                        vm.NavigateTimelineBucket(i);
                        Assert.NotNull(navigatedTo);
                        break;
                    }
                }
            }
        }

        // ── TryApplyLoggerFilter / TryApplyStateFilter ──

        [Fact]
        public void TryApplyLoggerFilter_Confirmed_ReturnsTrue()
        {
            string? filterType = null, filterValue = null;
            var vm = new StatsViewModel(null, null,
                (type, val) => { filterType = type; filterValue = val; },
                null, false, false);

            bool result = vm.TryApplyLoggerFilter("MyLogger", (_, __) => true);

            Assert.True(result);
            Assert.Equal("Logger", filterType);
            Assert.Equal("MyLogger", filterValue);
        }

        [Fact]
        public void TryApplyLoggerFilter_Declined_ReturnsFalse()
        {
            var vm = new StatsViewModel(null, null,
                (_, __) => { }, null, false, false);

            bool result = vm.TryApplyLoggerFilter("MyLogger", (_, __) => false);
            Assert.False(result);
        }

        [Fact]
        public void TryApplyLoggerFilter_NoCallback_ReturnsFalse()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            bool result = vm.TryApplyLoggerFilter("MyLogger", (_, __) => true);
            Assert.False(result);
        }

        [Fact]
        public void TryApplyStateFilter_Confirmed_ReturnsTrue()
        {
            string? filterType = null, filterValue = null;
            var vm = new StatsViewModel(null, null,
                (type, val) => { filterType = type; filterValue = val; },
                null, false, false);

            bool result = vm.TryApplyStateFilter("RUNNING", (_, __) => true);

            Assert.True(result);
            Assert.Equal("State", filterType);
            Assert.Equal("RUNNING", filterValue);
        }

        // ── State detection — S6 PlcMngr transitions ──

        [Fact]
        public async Task CalculateStatisticsAsync_S6StateTransitions_DetectsStates()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plcLogs = new List<LogEntry>
            {
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: OFF -> GET_READY", date: baseDate),
                MakeLog(level: "Info", thread: "Worker", message: "Normal log", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Error", thread: "Manager", message: "PlcMngr: GET_READY -> RUNNING", date: baseDate.AddSeconds(5)),
                MakeLog(level: "Error", thread: "Worker", message: "Some error", date: baseDate.AddSeconds(6)),
            };

            var vm = new StatsViewModel(plcLogs, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            Assert.True(vm.TimelineStateEntries!.Count >= 2);
        }

        // ── State detection — S4-5 STATE_XXX Enter ──

        [Fact]
        public async Task CalculateStatisticsAsync_S4StateEnter_DetectsStates()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plcLogs = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "==== STATE_STANDBY - Enter ======", date: baseDate),
                MakeLog(level: "Error", message: "Some error in standby", date: baseDate.AddSeconds(2)),
                MakeLog(level: "Error", message: "==== STATE_RUNNING - Enter ======", date: baseDate.AddSeconds(5)),
                MakeLog(level: "Error", message: "Error in running", date: baseDate.AddSeconds(6)),
            };

            var vm = new StatsViewModel(plcLogs, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.NotNull(vm.TimelineStateEntries);
            Assert.Equal(2, vm.TimelineStateEntries!.Count);
            Assert.Equal("STANDBY", vm.TimelineStateEntries[0].StateName);
            Assert.Equal("RUNNING", vm.TimelineStateEntries[1].StateName);
        }

        // ── Properties ──

        [Fact]
        public void HasBinaryAppLogs_ReflectsConstructorArg()
        {
            var vm1 = new StatsViewModel(null, null, null, null, false, true);
            Assert.True(vm1.HasBinaryAppLogs);

            var vm2 = new StatsViewModel(null, null, null, null, false, false);
            Assert.False(vm2.HasBinaryAppLogs);
        }

        [Fact]
        public void IsDarkMode_ReflectsConstructorArg()
        {
            var vm1 = new StatsViewModel(null, null, null, null, true, false);
            Assert.True(vm1.IsDarkMode);

            var vm2 = new StatsViewModel(null, null, null, null, false, false);
            Assert.False(vm2.IsDarkMode);
        }

        // ── Timeline buckets ──

        [Fact]
        public async Task CalculateStatisticsAsync_ShortDuration_Uses60Buckets()
        {
            // < 2 minutes of errors → 60 buckets
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 30; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddSeconds(i * 2)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal(60, vm.TimelineBucketCount);
        }

        [Fact]
        public async Task CalculateStatisticsAsync_MediumDuration_Uses100Buckets()
        {
            // 2-30 minutes → 100 buckets
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 50; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddMinutes(i * 0.3)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal(100, vm.TimelineBucketCount);
        }

        [Fact]
        public async Task CalculateStatisticsAsync_LongDuration_Uses120Buckets()
        {
            // > 30 minutes → 120 buckets
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>();
            for (int i = 0; i < 50; i++)
                plc.Add(MakeLog(level: "Error", date: baseDate.AddMinutes(i)));

            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();

            Assert.Equal(120, vm.TimelineBucketCount);
        }
    }
}
