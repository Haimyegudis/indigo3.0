using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Grep;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace IndiLogs.Tests
{
    public class ServiceBatch3Tests
    {
        // ====================================================================
        //  Helper: create LogEntry
        // ====================================================================
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

        // ====================================================================
        //  LogFileClassifier Tests
        // ====================================================================

        [Theory]
        [InlineData("enginegroupa.file.log", true, false)]
        [InlineData("enginegroupb.file.log", true, false)]
        [InlineData("something.file.log", true, false)]
        [InlineData("appdev.log", false, true)]
        [InlineData("press.host.app.log", false, true)]
        [InlineData("randomfile.txt", false, false)]
        public void LogFileClassifier_IsSearchableLogFile(string fileName, bool expectedPlc, bool expectedApp)
        {
            string lp = fileName.ToLowerInvariant();
            bool resultPlc = LogFileClassifier.IsSearchableLogFile(lp, true, false);
            bool resultApp = LogFileClassifier.IsSearchableLogFile(lp, false, true);
            Assert.Equal(expectedPlc, resultPlc);
            Assert.Equal(expectedApp, resultApp);
        }

        [Fact]
        public void LogFileClassifier_IsSearchableLogFile_BothTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("enginegroupa.file.log", true, true));
            Assert.True(LogFileClassifier.IsSearchableLogFile("appdev.file.log", true, true));
        }

        [Fact]
        public void LogFileClassifier_IsSearchableLogFile_BothFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file.log", false, false));
            Assert.False(LogFileClassifier.IsSearchableLogFile("appdev.file.log", false, false));
        }

        [Theory]
        [InlineData("123.file.log", true)]
        [InlineData("abc.file.log", false)]
        [InlineData("enginegroupa.file.log", false)]
        [InlineData("test42.file.log", true)]
        [InlineData("nofile.txt", false)]
        public void LogFileClassifier_IsNumericAppFileName(string fileName, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(fileName));
        }

        [Theory]
        [InlineData("appdev.file.log", "APP")]
        [InlineData("press.host.app.file.log", "APP")]
        [InlineData("enginegroupa.file.log", "PLC")]
        [InlineData("123.file.log", "APP")]
        [InlineData("random.file.log", "PLC")]
        public void LogFileClassifier_DetermineLogType(string fileName, string expected)
        {
            Assert.Equal(expected, LogFileClassifier.DetermineLogType(fileName));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_ZipAlwaysTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("test.zip", false, false));
        }

        [Fact]
        public void LogFileClassifier_IsLogEntry_ZipAlwaysFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("test.zip", true, true));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_WithPath()
        {
            Assert.True(LogFileClassifier.IsLogFile(@"C:\logs\enginegroupa.file.log", true, false));
            Assert.True(LogFileClassifier.IsLogFile("some/path/appdev.file.log", false, true));
        }

        [Fact]
        public void LogFileClassifier_NoSnFile_IsPLC()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("no-sn_something.file.log", true, false));
        }

        // ====================================================================
        //  CsvExportService Static Helpers
        // ====================================================================

        [Theory]
        [InlineData("", "Op")]
        [InlineData(null, "Op")]
        [InlineData("Inv", "InvalidHW")]
        [InlineData("NM", "NotMonitored")]
        [InlineData("NA", "NotActive")]
        [InlineData("Op", "Operational")]
        [InlineData("OpL", "OperationalLow")]
        [InlineData("OpH", "OperationalHigh")]
        [InlineData("PnL", "PanicLow")]
        [InlineData("PnH", "PanicHigh")]
        [InlineData("XYZ", "XYZ")]
        [InlineData("  Op  ", "Operational")]
        public void CsvExport_DecodeIoStatus(string input, string expected)
        {
            Assert.Equal(expected, CsvExportService.DecodeIoStatus(input!));
        }

        [Theory]
        [InlineData("Motor1_MotTemp", "Motor1", "MotTemp")]
        [InlineData("Motor1_DrvTemp", "Motor1", "DrvTemp")]
        [InlineData("PlainSymbol", "PlainSymbol", "Value")]
        [InlineData("Complex_Name_MotTemp", "Complex_Name", "MotTemp")]
        public void CsvExport_DecomposeIOSymbol(string input, string expectedComponent, string expectedParam)
        {
            CsvExportService.DecomposeIOSymbol(input, out string component, out string param);
            Assert.Equal(expectedComponent, component);
            Assert.Equal(expectedParam, param);
        }

        // ── TryParsePlcMngrState ──

        [Fact]
        public void TryParsePlcMngrState_ValidInput_ReturnsTrue()
        {
            string msg = "CHStep: PlcMngr, GET_READY, State 3 <Root, 0, 1, 100, 0, 1>";
            bool result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.True(result);
            Assert.Equal("GET_READY", stateName);
        }

        [Fact]
        public void TryParsePlcMngrState_NonPlcMngr_ReturnsFalse()
        {
            string msg = "CHStep: OtherCH, SomeStep, State 1 <Root, 0, 1, 100, 0, 1>";
            bool result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("", out _));
            Assert.False(CsvExportService.TryParsePlcMngrState(null!, out _));
        }

        [Fact]
        public void TryParsePlcMngrState_TooShort_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: PlcMngr", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NotCHStep_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("SomethingElse: PlcMngr, GET_READY, State 3", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_WithStateKeyword()
        {
            // When no second comma but " State " exists
            string msg = "CHStep: PlcMngr, GET_READY State 3";
            bool result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.True(result);
            Assert.Equal("GET_READY", stateName);
        }

        // ── TryParseCHStep ──

        [Fact]
        public void TryParseCHStep_ValidInput()
        {
            string msg = "CHStep: BID, InitBidSys, State 3 <Root, 10, 5, 200, 0, 1>";
            bool result = CsvExportService.TryParseCHStep(msg,
                out string? chName, out string? stepMessage, out string? stateId,
                out string? chParent, out string? subsysID, out string? prevStepNo,
                out string? diffTime, out string? subStepNo, out string? chObjType);
            Assert.True(result);
            Assert.Equal("BID", chName);
            Assert.Equal("InitBidSys", stepMessage);
            Assert.Equal("3", stateId);
            Assert.Equal("Root", chParent);
            Assert.Equal("10", subsysID);
            Assert.Equal("5", prevStepNo);
            Assert.Equal("200", diffTime);
            Assert.Equal("0", subStepNo);
            Assert.Equal("1", chObjType);
        }

        [Fact]
        public void TryParseCHStep_TooShort_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep("short", out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoCHStep_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep("SomethingElse: BID, Init, State 3 <Root, 10, 5, 200, 0, 1>",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        // ── TryParseLogStats ──

        [Fact]
        public void TryParseLogStats_ValidInput()
        {
            string msg = "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=5)";
            bool result = CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out string? semTotal,
                out string? semMult, out string? lost, out string? bufFull,
                out string? maxNum, out string? maxCat);
            Assert.True(result);
            Assert.Equal("5000", total);
            Assert.Equal("1", isReady);
            Assert.Equal("0", semTotal);
            Assert.Equal("0", semMult);
            Assert.Equal("0", lost);
            Assert.Equal("0", bufFull);
            Assert.Equal("100", maxNum);
            Assert.Equal("5", maxCat);
        }

        [Fact]
        public void TryParseLogStats_NotLogStat_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseLogStats("SomethingElse: data",
                out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseLogStats_PartialData_ReturnsTrue()
        {
            string msg = "LogStat: Logs(Total=100 IsReady=0)";
            bool result = CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out _, out _, out _, out _, out _, out _);
            Assert.True(result);
            Assert.Equal("100", total);
            Assert.Equal("0", isReady);
        }

        // ── AddToSchema & ParseAndAddValue (instance methods via GetUninitializedObject) ──

        [Fact]
        public void CsvExport_AddToSchema_CreatesHierarchy()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "Param1", "Param2" });

            Assert.True(schema.ContainsKey("Sub1"));
            Assert.True(schema["Sub1"].ContainsKey("Comp1"));
            Assert.Contains("Param1", schema["Sub1"]["Comp1"]);
            Assert.Contains("Param2", schema["Sub1"]["Comp1"]);
        }

        [Fact]
        public void CsvExport_AddToSchema_MergesParams()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P1" });
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P2" });
            Assert.Equal(2, schema["Sub1"]["Comp1"].Count);
        }

        [Fact]
        public void CsvExport_ParseAndAddValue_ValidParam()
        {
            var svc = CreateCsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP", "ActP" };
            var time = new DateTime(2025, 1, 1);

            svc.ParseAndAddValue("SetP=123.4", "AxisMon: Sub1", "Motor1", time, data, validParams);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("123.4", data[time]["AxisMon: Sub1|Motor1|SetP"]);
        }

        [Fact]
        public void CsvExport_ParseAndAddValue_InvalidParam_Ignored()
        {
            var svc = CreateCsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP" };
            var time = new DateTime(2025, 1, 1);

            svc.ParseAndAddValue("UnknownParam=123", "Sub1", "Motor1", time, data, validParams);

            Assert.False(data.ContainsKey(time));
        }

        [Fact]
        public void CsvExport_ParseAndAddValue_NoEquals_Ignored()
        {
            var svc = CreateCsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            svc.ParseAndAddValue("noequalssign", "Sub", "M", DateTime.Now, data, new[] { "x" });
            Assert.Empty(data);
        }

        private static CsvExportService CreateCsvExportService()
        {
            return (CsvExportService)RuntimeHelpers.GetUninitializedObject(typeof(CsvExportService));
        }

        // ====================================================================
        //  LogStatisticsService.Computation Tests
        // ====================================================================

        [Fact]
        public void TopN_EmptyDictionary_ReturnsEmpty()
        {
            var dict = new Dictionary<string, int>();
            var result = LogStatisticsService.TopN(dict, 5);
            Assert.Empty(result);
        }

        [Fact]
        public void TopN_FewerThanN_ReturnsSortedDescending()
        {
            var dict = new Dictionary<string, int>
            {
                ["a"] = 1, ["b"] = 3, ["c"] = 2
            };
            var result = LogStatisticsService.TopN(dict, 5);
            Assert.Equal(3, result.Count);
            Assert.Equal("b", result[0].Key);
            Assert.Equal(3, result[0].Value);
            Assert.Equal("c", result[1].Key);
            Assert.Equal("a", result[2].Key);
        }

        [Fact]
        public void TopN_MoreThanN_ReturnsTopN()
        {
            var dict = new Dictionary<string, int>
            {
                ["a"] = 1, ["b"] = 5, ["c"] = 3, ["d"] = 4, ["e"] = 2
            };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, result.Count);
            Assert.Equal(5, result[0].Value);
            Assert.Equal(4, result[1].Value);
            Assert.Equal(3, result[2].Value);
        }

        [Fact]
        public void TopN_ExactlyN_ReturnsSorted()
        {
            var dict = new Dictionary<string, int>
            {
                ["a"] = 10, ["b"] = 20, ["c"] = 15
            };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, result.Count);
            Assert.Equal(20, result[0].Value);
            Assert.Equal(15, result[1].Value);
            Assert.Equal(10, result[2].Value);
        }

        [Fact]
        public void GetErrorLogs_FiltersErrors()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Info"),
                MakeLog(level: "ERROR"),
                MakeLog(level: "WARN"),
                MakeLog(level: "FATAL"),
                MakeLog(level: "Debug"),
            };
            var errors = LogStatisticsService.GetErrorLogs(logs);
            Assert.Equal(2, errors.Count); // ERROR and FATAL
        }

        [Fact]
        public void GetErrorLogs_Empty_ReturnsEmpty()
        {
            var result = LogStatisticsService.GetErrorLogs(new List<LogEntry>());
            Assert.Empty(result);
        }

        [Fact]
        public void TruncateMessage_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 100));
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 100));
        }

        [Fact]
        public void TruncateMessage_ShortMessage_ReturnsUnchanged()
        {
            Assert.Equal("hello", LogStatisticsService.TruncateMessage("hello", 100));
        }

        [Fact]
        public void TruncateMessage_LongMessage_Truncates()
        {
            string long_ = new string('x', 200);
            var result = LogStatisticsService.TruncateMessage(long_, 100);
            Assert.Equal(103, result.Length); // 100 + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void GetShortLoggerName_NullOrEmpty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        [Fact]
        public void GetShortLoggerName_ShortName_ReturnsAsIs()
        {
            Assert.Equal("Foo.Bar", LogStatisticsService.GetShortLoggerName("Foo.Bar"));
        }

        [Fact]
        public void GetShortLoggerName_LongName_ReturnsLastTwo()
        {
            Assert.Equal("Service.Foo", LogStatisticsService.GetShortLoggerName("App.Core.Service.Foo"));
        }

        [Fact]
        public void FormatDuration_LessThanMinute_ShowsSeconds()
        {
            var ts = TimeSpan.FromSeconds(30);
            var result = LogStatisticsService.FormatDuration(ts);
            Assert.Contains("sec", result);
            Assert.Contains("30", result);
        }

        [Fact]
        public void FormatDuration_MoreThanMinute_ShowsMinutes()
        {
            var ts = TimeSpan.FromMinutes(5.5);
            var result = LogStatisticsService.FormatDuration(ts);
            Assert.Contains("min", result);
        }

        [Fact]
        public void FindGaps_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry>()));
        }

        [Fact]
        public void FindGaps_SingleEntry_ReturnsEmpty()
        {
            var logs = new List<LogEntry> { MakeLog() };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_NoGaps_ReturnsEmpty()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(0.5)),
                MakeLog(date: baseDate.AddSeconds(1)),
            };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_DetectsGap()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(5)),
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Single(gaps);
            Assert.Equal(baseDate, gaps[0].StartTime);
            Assert.Equal(baseDate.AddSeconds(5), gaps[0].EndTime);
        }

        [Fact]
        public void FindGaps_Null_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(null!));
        }

        [Fact]
        public void CalculateErrorHistogram_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 10));
        }

        [Fact]
        public void CalculateErrorHistogram_GroupsByMessage()
        {
            var errors = new List<LogEntry>
            {
                MakeLog(level: "ERROR", message: "Error A"),
                MakeLog(level: "ERROR", message: "Error A"),
                MakeLog(level: "ERROR", message: "Error B"),
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal("Error A", result[0].Name);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void CalculateLoadDistribution_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateLoadDistribution(new List<LogEntry>(), l => l.ThreadName, 10));
        }

        [Fact]
        public void CalculateLoadDistribution_GroupsByKey()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(thread: "T1"),
                MakeLog(thread: "T1"),
                MakeLog(thread: "T2"),
            };
            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal("T1", result[0].Name);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void MapErrorsToStates_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), new List<StateEntry>()));
        }

        [Fact]
        public void MapErrorsToStates_MapsCorrectly()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "IDLE", StartTime = baseDate, EndTime = baseDate.AddMinutes(5) },
                new StateEntry { StateName = "PRINTING", StartTime = baseDate.AddMinutes(5), EndTime = baseDate.AddMinutes(10) },
            };
            var errors = new List<LogEntry>
            {
                MakeLog(level: "ERROR", date: baseDate.AddMinutes(1)),
                MakeLog(level: "ERROR", date: baseDate.AddMinutes(2)),
                MakeLog(level: "ERROR", date: baseDate.AddMinutes(6)),
            };
            var result = LogStatisticsService.MapErrorsToStates(errors, states);
            Assert.Equal(2, result.Count);
            var idle = result.First(r => r.Name == "IDLE");
            var printing = result.First(r => r.Name == "PRINTING");
            Assert.Equal(2, idle.Count);
            Assert.Equal(1, printing.Count);
        }

        [Fact]
        public void CalculateStateEntries_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        [Fact]
        public void CalculateStateEntries_S6_PlcMngrTransitions()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(thread: "Manager", message: "PlcMngr: OFF -> GET_READY", date: baseDate),
                MakeLog(thread: "Manager", message: "PlcMngr: GET_READY -> PRINTING", date: baseDate.AddMinutes(2)),
                MakeLog(thread: "Worker", message: "Some other log", date: baseDate.AddMinutes(3)),
            };
            var states = LogStatisticsService.CalculateStateEntries(logs);
            Assert.True(states.Count >= 2);
            Assert.Equal("GET_READY", states[1].StateName);
            Assert.Contains("OFF -> GET_READY", states[1].TransitionTitle);
        }

        // ====================================================================
        //  ComputeStatistics Integration Tests
        // ====================================================================

        [Fact]
        public void ComputeStatistics_EmptyLogs_ReturnsDefaults()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Null(result.EarliestTimestamp);
            Assert.Null(result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_SetsTimeSpan()
        {
            var d1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var d2 = new DateTime(2025, 1, 1, 11, 0, 0);
            var plc = new List<LogEntry> { MakeLog(date: d1), MakeLog(date: d2) };
            var result = LogStatisticsService.ComputeStatistics(plc, new List<LogEntry>());
            Assert.Equal(d1, result.EarliestTimestamp);
            Assert.Equal(d2, result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_CountsErrors()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(level: "ERROR"), MakeLog(level: "Info"),
            };
            var app = new List<LogEntry>
            {
                MakeLog(level: "FATAL"), MakeLog(level: "Debug"),
            };
            var result = LogStatisticsService.ComputeStatistics(plc, app);
            Assert.Equal(1, result.TotalPlcErrors);
            Assert.Equal(1, result.TotalAppErrors);
        }

        // ====================================================================
        //  ChartDataService Tests
        // ====================================================================

        [Fact]
        public void ChartDataService_FindColumnIndex_ExactMatch()
        {
            var svc = new ChartDataService();
            // Set ColumnNames via reflection or direct property
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature", "Pressure" });

            Assert.Equal(0, svc.FindColumnIndex("Time"));
            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
            Assert.Equal(2, svc.FindColumnIndex("Pressure"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_CaseInsensitive()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature" });
            Assert.Equal(1, svc.FindColumnIndex("temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_PartialMatch()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Station_Temperature_C" });
            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_NotFound()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature" });
            Assert.Equal(-1, svc.FindColumnIndex("NonExistent"));
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_NotFound()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Value" });
            svc.RawColumnNames.AddRange(new[] { "Time", "Value" });
            Assert.Equal(-1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_Found()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Events_Message", "Value" });
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_InRawNames()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Value" });
            svc.RawColumnNames.AddRange(new[] { "Time", "Data.Events_Message" });
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_Dispose_ClearsState()
        {
            var svc = new ChartDataService();
            svc.ColumnNames.Add("Test");
            svc.RawColumnNames.Add("Test");
            svc.Dispose();
            Assert.Empty(svc.ColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.Null(svc.LoadedFilePath);
            Assert.False(svc.IsLoaded);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_StationPArr()
        {
            var svc = new ChartDataService();
            var method = typeof(ChartDataService).GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (string)method!.Invoke(svc, new object[] { "Station.pArrSomething" })!;
            Assert.Equal("Something", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_gStationAxes()
        {
            var svc = new ChartDataService();
            var method = typeof(ChartDataService).GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (string)method!.Invoke(svc, new object[] { "gStationAxes_Motor1" })!;
            Assert.Equal("StnMotor1", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_arrInk()
        {
            var svc = new ChartDataService();
            var method = typeof(ChartDataService).GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (string)method!.Invoke(svc, new object[] { "arrInk[0].Value" })!;
            Assert.Equal("Ink[0].Value", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_EmptyOrNull()
        {
            var svc = new ChartDataService();
            var method = typeof(ChartDataService).GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Equal("", (string)method!.Invoke(svc, new object[] { "" })!);
            Assert.Equal("  ", (string)method!.Invoke(svc, new object[] { "  " })!);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_CaretDot()
        {
            var svc = new ChartDataService();
            var method = typeof(ChartDataService).GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (string)method!.Invoke(svc, new object[] { "Something^.Value" })!;
            Assert.Equal("Something.Value", result);
        }

        [Fact]
        public void ChartDataService_DetectFormat_Legacy_WhenEmpty()
        {
            // Use file-based approach: create a small CSV with legacy format
            var svc = new ChartDataService();
            // Without loading, DetectedFormat should be Unknown (default)
            Assert.Equal(CsvFormat.Unknown, svc.DetectedFormat);
        }

        [Fact]
        public void ChartDataService_LoadAndDetect_LegacyFormat()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_legacy_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Group1,,Group2,");
                sb.AppendLine("Sub1,Sub2,Sub3,Sub4");
                sb.AppendLine("Param1,Param2,Param3,Param4");
                sb.AppendLine("1.0,2.0,3.0,4.0");
                sb.AppendLine("5.0,6.0,7.0,8.0");
                File.WriteAllText(tempFile, sb.ToString());

                svc.Load(tempFile);

                Assert.Equal(CsvFormat.Legacy, svc.DetectedFormat);
                Assert.Equal(3, svc.DataStartRow);
                Assert.Equal(4, svc.ColumnNames.Count);
                Assert.True(svc.IsLoaded);
                Assert.Equal(tempFile, svc.LoadedFilePath);

                // Read data
                Assert.Equal("1.0", svc.GetStringAt(3, 0));
                Assert.Equal(1.0, svc.GetValueAt(3, 0));
                Assert.Equal(6.0, svc.GetValueAt(4, 1));
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_LoadAndDetect_PlcIosFormat()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_plcios_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Unix_Time,Machine_State,PolicyName,Data.OPCUAInterface.Value1,Data.OPCUAInterface.Value2");
                sb.AppendLine("1700000000,5,Policy1,100,200");
                sb.AppendLine("1700000001,5,Policy1,101,201");
                File.WriteAllText(tempFile, sb.ToString());

                svc.Load(tempFile);

                Assert.Equal(CsvFormat.PlcIos, svc.DetectedFormat);
                Assert.Equal(1, svc.DataStartRow);
                Assert.Equal(5, svc.ColumnNames.Count);
                // OPC-UA names with <=4 parts stay as-is; >4 parts get simplified
                Assert.Equal("Data.OPCUAInterface.Value1", svc.ColumnNames[3]);
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_LoadAndDetect_YTScopeFormat()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_ytscope_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Name,YT Scope Project,Version");
                sb.AppendLine("Name,Station.Value1,Station.Value2");
                sb.AppendLine("SymbolComment,,");
                sb.AppendLine("DataType,REAL,REAL");
                sb.AppendLine("SampleTime,10ms,10ms");
                sb.AppendLine(",,");
                sb.AppendLine(",,");
                sb.AppendLine(",,");
                sb.AppendLine(",,");
                sb.AppendLine("0,1.0,2.0");
                sb.AppendLine("1,3.0,4.0");
                File.WriteAllText(tempFile, sb.ToString());

                svc.Load(tempFile);

                Assert.Equal(CsvFormat.YTScope, svc.DetectedFormat);
                Assert.True(svc.ColumnNames.Count >= 2);
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_GetStringAt_OutOfBounds_ReturnsEmpty()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_oob_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                File.WriteAllText(tempFile, "a,b,c\n1,2,3\n");
                svc.Load(tempFile);

                Assert.Equal("", svc.GetStringAt(999, 0));
                Assert.Equal("", svc.GetStringAt(0, 999));
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_GetValueAt_OutOfBounds_ReturnsNaN()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_nan_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                File.WriteAllText(tempFile, "a,b\n1,2\n");
                svc.Load(tempFile);

                Assert.True(double.IsNaN(svc.GetValueAt(999, 0)));
                Assert.True(double.IsNaN(svc.GetValueAt(0, 999)));
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_GetValueAt_NonNumeric_ReturnsNaN()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_nonnum_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                File.WriteAllText(tempFile, "a,b\nhello,world\n");
                svc.Load(tempFile);

                Assert.True(double.IsNaN(svc.GetValueAt(1, 0)));
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Fact]
        public void ChartDataService_GetStringAt_QuotedFieldsWithCommas()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_quoted_{Guid.NewGuid()}.csv");
            var svc = new ChartDataService();
            try
            {
                File.WriteAllText(tempFile, "a,\"hello, world\",c\n");
                svc.Load(tempFile);

                Assert.Equal("a", svc.GetStringAt(0, 0));
                Assert.Equal("hello, world", svc.GetStringAt(0, 1));
                Assert.Equal("c", svc.GetStringAt(0, 2));
            }
            finally
            {
                svc.Dispose();
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        // ====================================================================
        //  SearchSchedulerService.ShouldRun Tests
        // ====================================================================

        [Fact]
        public void ShouldRun_Disabled_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch { IsEnabled = false, ScheduleType = ScheduleType.Daily };
            Assert.False(InvokeShouldRun(svc, schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_NeverRan_TimeReached_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunTime = new TimeSpan(10, 0, 0),
                LastRunTime = null
            };
            var now = new DateTime(2025, 3, 1, 11, 0, 0);
            Assert.True(InvokeShouldRun(svc, schedule, now));
        }

        [Fact]
        public void ShouldRun_Once_AlreadyRan_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunTime = new TimeSpan(10, 0, 0),
                LastRunTime = new DateTime(2025, 3, 1, 10, 0, 0)
            };
            Assert.False(InvokeShouldRun(svc, schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_WithRunDate_BeforeTarget_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunDate = new DateTime(2025, 6, 15),
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = null
            };
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 6, 15, 13, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Once_WithRunDate_AfterTarget_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunDate = new DateTime(2025, 6, 15),
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 6, 15, 15, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Daily_BeforeTime_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 0, 0)
            };
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 3, 1, 13, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Daily_AfterTime_NeverRan_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 3, 1, 15, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Daily_AlreadyRanToday_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2025, 3, 1, 15, 0, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = new DateTime(2025, 3, 1, 14, 0, 0)
            };
            Assert.False(InvokeShouldRun(svc, schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_WrongDay_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Weekly,
                RunTime = new TimeSpan(10, 0, 0),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday }
            };
            // Use a Tuesday
            var tuesday = new DateTime(2025, 3, 4, 11, 0, 0); // March 4, 2025 is a Tuesday
            Assert.False(InvokeShouldRun(svc, schedule, tuesday));
        }

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_AfterTime_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Weekly,
                RunTime = new TimeSpan(10, 0, 0),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = null
            };
            var monday = new DateTime(2025, 3, 3, 11, 0, 0); // March 3, 2025 is Monday
            Assert.True(InvokeShouldRun(svc, schedule, monday));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = null,
                RunTime = TimeSpan.Zero
            };
            Assert.True(InvokeShouldRun(svc, schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Interval_NotElapsed_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2025, 3, 1, 12, 30, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = new DateTime(2025, 3, 1, 12, 0, 0)
            };
            Assert.False(InvokeShouldRun(svc, schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_Elapsed_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2025, 3, 1, 14, 0, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = new DateTime(2025, 3, 1, 12, 0, 0)
            };
            Assert.True(InvokeShouldRun(svc, schedule, now));
        }

        private static SearchSchedulerService CreateSchedulerService()
        {
            return (SearchSchedulerService)RuntimeHelpers.GetUninitializedObject(typeof(SearchSchedulerService));
        }

        private static bool InvokeShouldRun(SearchSchedulerService svc, ScheduledSearch schedule, DateTime now)
        {
            var method = typeof(SearchSchedulerService).GetMethod("ShouldRun", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            return (bool)method!.Invoke(svc, new object[] { schedule, now })!;
        }

        // ====================================================================
        //  EmailNotificationService.BodyBuilding Tests
        // ====================================================================

        [Fact]
        public void BuildPlainTextBody_NoResults_NoStats()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchOnly };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, null);
            Assert.Contains("IndiLogs Search Report", body);
            Assert.Contains("TestScan", body);
            Assert.DoesNotContain("SEARCH RESULTS", body);
            Assert.DoesNotContain("LOG STATISTICS", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithResults()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchOnly };
            var results = new List<GrepResult>
            {
                new GrepResult { Timestamp = DateTime.Now, PreviewText = "Error found", LogType = "PLC", LocationName = "Loc1" },
                new GrepResult { Timestamp = DateTime.Now, PreviewText = "Another error", LogType = "APP", LocationName = "Loc2" },
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("Total matches: 2", body);
            Assert.Contains("By Location:", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithStats()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                TotalPlcLogs = 1000,
                TotalAppLogs = 2000,
                TotalPlcErrors = 10,
                TotalAppErrors = 20,
                EarliestTimestamp = new DateTime(2025, 1, 1, 10, 0, 0),
                LatestTimestamp = new DateTime(2025, 1, 1, 11, 0, 0),
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("IndiLogs Statistics Report", body);
            Assert.Contains("PLC Logs:   1,000", body);
            Assert.Contains("APP Logs:   2,000", body);
            Assert.Contains("PLC Errors: 10", body);
            Assert.Contains("APP Errors: 20", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithBothSearchAndStats()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchAndStatistics };
            var results = new List<GrepResult>
            {
                new GrepResult { Timestamp = DateTime.Now, PreviewText = "match", LogType = "PLC", LocationName = "Loc1" },
            };
            var stats = new LogStatisticsResult { TotalPlcLogs = 500, TotalAppLogs = 0 };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, stats);
            Assert.Contains("Search & Statistics Report", body);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("LOG STATISTICS", body);
        }

        [Fact]
        public void BuildPlainTextBody_ManyResults_ShowsPreviewAndMore()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchOnly };
            var results = Enumerable.Range(0, 30).Select(i => new GrepResult
            {
                Timestamp = DateTime.Now,
                PreviewText = $"Error {i}",
                LogType = "PLC",
                LocationName = "Loc1"
            }).ToList();
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("First 20 results:", body);
            Assert.Contains("and 10 more", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithGapAnalysis()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                TotalPlcLogs = 100,
                PlcGaps = new List<GapInfo>
                {
                    new GapInfo { StartTime = new DateTime(2025,1,1,10,0,0), EndTime = new DateTime(2025,1,1,10,0,5), DurationText = "5.0 sec" },
                },
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("GAP ANALYSIS", body);
            Assert.Contains("PLC: 1 gap", body);
        }

        // ====================================================================
        //  SearchSchedulerService.Escape Tests
        // ====================================================================

        [Fact]
        public void Escape_NullOrEmpty_ReturnsEmpty()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.Equal("", (string)method!.Invoke(null, new object?[] { null })!);
            Assert.Equal("", (string)method.Invoke(null, new object?[] { "" })!);
        }

        [Fact]
        public void Escape_SimpleString_ReturnsUnchanged()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("hello", (string)method!.Invoke(null, new object?[] { "hello" })!);
        }

        [Fact]
        public void Escape_WithComma_Quotes()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("\"hello, world\"", (string)method!.Invoke(null, new object?[] { "hello, world" })!);
        }

        [Fact]
        public void Escape_WithQuote_DoubleQuotes()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("\"he said \"\"hi\"\"\"", (string)method!.Invoke(null, new object?[] { "he said \"hi\"" })!);
        }

        // ====================================================================
        //  ScheduledSearch Model Tests
        // ====================================================================

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Hours()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 2, IntervalUnit = IntervalUnit.Hours };
            Assert.Equal(120, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Days()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 1, IntervalUnit = IntervalUnit.Days };
            Assert.Equal(1440, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Minutes()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 30, IntervalUnit = IntervalUnit.Minutes };
            Assert.Equal(30, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_NoCriteria()
        {
            var s = new ScheduledSearch();
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_StatisticsOnly()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = true,
                    Groups = new List<SearchConditionGroup> { new SearchConditionGroup() }
                }
            };
            Assert.Contains("[Stats]", s.SearchSummary);
            Assert.Contains("All logs", s.SearchSummary);
        }

        // ====================================================================
        //  CsvExportService Instance Methods (ParseAxisMon, ParseIOMon, etc.)
        // ====================================================================

        [Fact]
        public void ParseAxisMon_ValidInput_PopulatesDataMatrix()
        {
            var svc = CreateCsvExportService();
            SetAxisParams(svc);
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseAxisMon("AxisMon: SubSys1, Motor1, SetP=100.5, ActP=99.8", "Thread1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("AxisMon: SubSys1"));
            Assert.True(data.ContainsKey(time));
            Assert.Equal("100.5", data[time]["AxisMon: SubSys1|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAxisMon_WithTrigger_PopulatesTriggerField()
        {
            var svc = CreateCsvExportService();
            SetAxisParams(svc);
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseAxisMon("AxisMon: Sub1, Motor1, Trg=5", "T1", time, null, schema, data, threadMap);

            Assert.Equal("5", data[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxM_ValidInput_PopulatesDataMatrix()
        {
            var svc = CreateCsvExportService();
            SetAxisParams(svc);
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseAxM("AxM: SubSys1, Motor1, LagE=0.5, SetP=100", "T1", time, null, schema, data, threadMap);

            Assert.Equal("0.5", data[time]["AxisMon: SubSys1|Motor1|LagErr"]);
        }

        [Fact]
        public void ParseAxM_TriggerValue_NoEquals()
        {
            var svc = CreateCsvExportService();
            SetAxisParams(svc);
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseAxM("AxM: Sub1, Motor1, SomeTriggerValue", "T1", time, null, schema, data, threadMap);

            Assert.Equal("SomeTriggerValue", data[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseIOMon_ValidInput()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIOMon("IO_Mon: SubSys1, Sensor1=42.5", "T1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("IO_Mon: SubSys1"));
        }

        [Fact]
        public void ParseIOMon_WithNewStatus()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIOMon("IO_Mon: SubSys1, Sensor1=New Status Op", "T1", time, null, schema, data, threadMap);

            Assert.True(data.ContainsKey(time));
            // eIoStatus should be decoded
            Assert.Equal("Operational", data[time]["IO_Mon: SubSys1|Sensor1|eIoStatus"]);
        }

        [Fact]
        public void ParseIO_ValidInput()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: SubSys1, Sensor1=42.5", "T1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("IO_Mon: SubSys1"));
            Assert.True(data.ContainsKey(time));
        }

        [Fact]
        public void ParseIO_WithStatus()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: Sub1, Sensor_MotTemp=55.0, OpH", "T1", time, null, schema, data, threadMap);

            Assert.Equal("55.0", data[time]["IO_Mon: Sub1|Sensor|MotTemp"]);
            Assert.Equal("OperationalHigh", data[time]["IO_Mon: Sub1|Sensor|eIoStatus"]);
        }

        [Fact]
        public void ParseLogStats_ValidInput_AddsToDataMatrix()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseLogStats(
                "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=5)",
                "LogStats", time, schema, data, threadMap);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("5000", data[time]["LogStats|Metrics|Total"]);
            Assert.Equal("1", data[time]["LogStats|Metrics|IsReady"]);
        }

        [Fact]
        public void ParseLogStats_WrongThread_DoesNothing()
        {
            var svc = CreateCsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseLogStats("LogStat: Logs(Total=5000 IsReady=1)", "OtherThread", time, schema, data, threadMap);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAxisMon_WithFilter_Excluded()
        {
            var svc = CreateCsvExportService();
            SetAxisParams(svc);
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);
            var selectedAxis = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DifferentSub|DifferentMotor" };

            svc.ParseAxisMon("AxisMon: SubSys1, Motor1, SetP=100", "T1", time, selectedAxis, schema, data, threadMap);

            Assert.Empty(data);
        }

        // ====================================================================
        //  CsvFormat Enum Tests
        // ====================================================================

        [Theory]
        [InlineData(CsvFormat.Unknown)]
        [InlineData(CsvFormat.PlcIos)]
        [InlineData(CsvFormat.YTScope)]
        [InlineData(CsvFormat.Legacy)]
        public void CsvFormat_EnumValues_AreValid(CsvFormat format)
        {
            Assert.True(Enum.IsDefined(typeof(CsvFormat), format));
        }

        // ====================================================================
        //  RentRowBuffer private static method test
        // ====================================================================

        [Fact]
        public void ChartDataService_RentRowBuffer_ReturnsBufferOfMinSize()
        {
            var method = typeof(ChartDataService).GetMethod("RentRowBuffer", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var buf = (byte[])method!.Invoke(null, new object[] { 100 })!;
            Assert.True(buf.Length >= 100);
        }

        [Fact]
        public void ChartDataService_RentRowBuffer_ReusesBuffer()
        {
            var method = typeof(ChartDataService).GetMethod("RentRowBuffer", BindingFlags.NonPublic | BindingFlags.Static);
            var buf1 = (byte[])method!.Invoke(null, new object[] { 100 })!;
            var buf2 = (byte[])method.Invoke(null, new object[] { 50 })!;
            Assert.Same(buf1, buf2); // Should reuse the same buffer
        }

        // ====================================================================
        //  Helpers for reflection-based field setup
        // ====================================================================

        private static void SetAxisParams(CsvExportService svc)
        {
            var field = typeof(CsvExportService).GetField("_axisParams", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(svc, new[] { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr", "Trigger" });
        }
    }
}
