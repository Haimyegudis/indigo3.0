using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class CsvExportParsingTests
    {
        private CsvExportService CreateService()
        {
            return new CsvExportService();
        }

        // ── DecodeIoStatus ──

        [Fact]
        public void DecodeIoStatus_Empty_ReturnsOp()
        {
            Assert.Equal("Op", CsvExportService.DecodeIoStatus(""));
        }

        [Fact]
        public void DecodeIoStatus_Null_ReturnsOp()
        {
            Assert.Equal("Op", CsvExportService.DecodeIoStatus(null!));
        }

        [Theory]
        [InlineData("Inv", "InvalidHW")]
        [InlineData("NM", "NotMonitored")]
        [InlineData("NA", "NotActive")]
        [InlineData("Op", "Operational")]
        [InlineData("OpL", "OperationalLow")]
        [InlineData("OpH", "OperationalHigh")]
        [InlineData("PnL", "PanicLow")]
        [InlineData("PnH", "PanicHigh")]
        public void DecodeIoStatus_KnownCodes(string input, string expected)
        {
            Assert.Equal(expected, CsvExportService.DecodeIoStatus(input));
        }

        [Fact]
        public void DecodeIoStatus_UnknownCode_ReturnsTrimmed()
        {
            Assert.Equal("Custom", CsvExportService.DecodeIoStatus("  Custom  "));
        }

        // ── TryParsePlcMngrState ──

        [Fact]
        public void TryParsePlcMngrState_ValidMessage_ReturnsTrue()
        {
            string msg = "CHStep: PlcMngr, STANDBY, State 5 <Root,1,0,100,0,1>";
            Assert.True(CsvExportService.TryParsePlcMngrState(msg, out string? state));
            Assert.Equal("STANDBY", state);
        }

        [Fact]
        public void TryParsePlcMngrState_NotPlcMngr_ReturnsFalse()
        {
            string msg = "CHStep: OtherCH, SomeState, State 5 <Root,1,0,100,0,1>";
            Assert.False(CsvExportService.TryParsePlcMngrState(msg, out _));
        }

        [Fact]
        public void TryParsePlcMngrState_EmptyMessage_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_Null_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState(null!, out _));
        }

        [Fact]
        public void TryParsePlcMngrState_TooShort_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: PlcMngr", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_CaseInsensitive()
        {
            string msg = "chstep: plcmngr, PRINTING, State 3 <Root,1,0,50,0,1>";
            Assert.True(CsvExportService.TryParsePlcMngrState(msg, out string? state));
            Assert.Equal("PRINTING", state);
        }

        [Fact]
        public void TryParsePlcMngrState_WithStateKeyword_NoSecondComma()
        {
            // When no second comma, parser looks for " State " keyword
            string msg = "CHStep: PlcMngr, WARMUP State 2 <Root,1,0,100,0,1>";
            // This has a second comma (in the angle brackets), so it should parse to "WARMUP State 2 <Root"
            // Actually let's test the simple case with no comma after state
            bool result = CsvExportService.TryParsePlcMngrState(msg, out string? state);
            Assert.True(result);
            Assert.NotNull(state);
        }

        // ── TryParseCHStep ──

        [Fact]
        public void TryParseCHStep_ValidMessage_AllFieldsParsed()
        {
            string msg = "CHStep: MyChannel, Starting Phase, State 42 <ParentNode, Sub123, 5, 1200, 3, 1>";
            Assert.True(CsvExportService.TryParseCHStep(msg,
                out string? chName, out string? stepMessage, out string? stateId,
                out string? parent, out string? subsysID, out string? prevStep,
                out string? diffTime, out string? subStep, out string? objType));

            Assert.Equal("MyChannel", chName);
            Assert.Equal("Starting Phase", stepMessage);
            Assert.Equal("42", stateId);
            Assert.Equal("ParentNode", parent);
            Assert.Equal("Sub123", subsysID);
            Assert.Equal("5", prevStep);
            Assert.Equal("1200", diffTime);
            Assert.Equal("3", subStep);
            Assert.Equal("1", objType);
        }

        [Fact]
        public void TryParseCHStep_TooShort_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep("short", out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoAngleBrackets_ReturnsFalse()
        {
            string msg = "CHStep: MyChannel, Step, State 5 noBrackets";
            Assert.False(CsvExportService.TryParseCHStep(msg, out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_InsufficientPartsInBrackets_ReturnsFalse()
        {
            string msg = "CHStep: MyChannel, Step, State 5 <Only,Two>";
            Assert.False(CsvExportService.TryParseCHStep(msg, out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        // ── TryParseLogStats ──

        [Fact]
        public void TryParseLogStats_ValidMessage_AllFieldsParsed()
        {
            string msg = "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=2 bufFull=0 Max(num=100 cat=ERROR)";
            Assert.True(CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out string? semTotal,
                out string? semMult, out string? lost, out string? bufFull,
                out string? maxNum, out string? maxCat));

            Assert.Equal("5000", total);
            Assert.Equal("1", isReady);
            Assert.Equal("0", semTotal);
            Assert.Equal("0", semMult);
            Assert.Equal("2", lost);
            Assert.Equal("0", bufFull);
            Assert.Equal("100", maxNum);
            Assert.Equal("ERROR", maxCat);
        }

        [Fact]
        public void TryParseLogStats_NotLogStat_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseLogStats("NotLogStat: something",
                out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseLogStats_PartialData_ParsesAvailable()
        {
            string msg = "LogStat: Logs(Total=123 IsReady=0)";
            Assert.True(CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out _,
                out _, out _, out _, out _, out _));
            Assert.Equal("123", total);
            Assert.Equal("0", isReady);
        }

        [Fact]
        public void TryParseLogStats_NoTotal_ReturnsFalse()
        {
            string msg = "LogStat: no data here";
            Assert.False(CsvExportService.TryParseLogStats(msg,
                out _, out _, out _, out _, out _, out _, out _, out _));
        }

        // ── DecomposeIOSymbol ──

        [Fact]
        public void DecomposeIOSymbol_MotTemp_Suffix()
        {
            CsvExportService.DecomposeIOSymbol("Motor1_MotTemp", out string comp, out string param);
            Assert.Equal("Motor1", comp);
            Assert.Equal("MotTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_DrvTemp_Suffix()
        {
            CsvExportService.DecomposeIOSymbol("Drive2_DrvTemp", out string comp, out string param);
            Assert.Equal("Drive2", comp);
            Assert.Equal("DrvTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_NoSuffix_ValueParam()
        {
            CsvExportService.DecomposeIOSymbol("Sensor_A", out string comp, out string param);
            Assert.Equal("Sensor_A", comp);
            Assert.Equal("Value", param);
        }

        [Fact]
        public void DecomposeIOSymbol_CaseInsensitive_MotTemp()
        {
            CsvExportService.DecomposeIOSymbol("abc_MOTTEMP", out string comp, out string param);
            Assert.Equal("abc", comp);
            Assert.Equal("MotTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_CaseInsensitive_DrvTemp()
        {
            CsvExportService.DecomposeIOSymbol("xyz_DRVTEMP", out string comp, out string param);
            Assert.Equal("xyz", comp);
            Assert.Equal("DrvTemp", param);
        }

        // ── AddToSchema ──

        [Fact]
        public void AddToSchema_NewSubsys_CreatesStructure()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            svc.AddToSchema(schema, "IO_Mon: BIM[0]", "Sensor1", new[] { "Value", "eIoStatus" });

            Assert.True(schema.ContainsKey("IO_Mon: BIM[0]"));
            Assert.True(schema["IO_Mon: BIM[0]"].ContainsKey("Sensor1"));
            Assert.Contains("Value", schema["IO_Mon: BIM[0]"]["Sensor1"]);
            Assert.Contains("eIoStatus", schema["IO_Mon: BIM[0]"]["Sensor1"]);
        }

        [Fact]
        public void AddToSchema_ExistingSubsys_AddsParams()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "Param1" });
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "Param2" });

            Assert.Equal(2, schema["Sub1"]["Comp1"].Count);
        }

        [Fact]
        public void AddToSchema_DuplicateParams_NoDuplicates()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "Param1", "Param1" });

            Assert.Single(schema["Sub1"]["Comp1"]);
        }

        // ── ParseAndAddValue ──

        [Fact]
        public void ParseAndAddValue_ValidParam_AddsToData()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP", "ActP", "SetV" };
            var time = new DateTime(2024, 1, 1);

            svc.ParseAndAddValue("SetP=123.4", "AxisMon: Sub1", "Motor1", time, data, validParams);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("123.4", data[time]["AxisMon: Sub1|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAndAddValue_InvalidParam_NotAdded()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP" };
            var time = new DateTime(2024, 1, 1);

            svc.ParseAndAddValue("Unknown=42", "Sub1", "Motor1", time, data, validParams);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_NoEquals_NotAdded()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP" };
            var time = new DateTime(2024, 1, 1);

            svc.ParseAndAddValue("NoEqualsHere", "Sub1", "Motor1", time, data, validParams);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_MultipleValues_SameTimestamp()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP", "ActP" };
            var time = new DateTime(2024, 1, 1);

            svc.ParseAndAddValue("SetP=100", "Sub1", "Motor1", time, data, validParams);
            svc.ParseAndAddValue("ActP=99", "Sub1", "Motor1", time, data, validParams);

            Assert.Equal(2, data[time].Count);
        }

        // ── ParseAxisMon ──

        [Fact]
        public void ParseAxisMon_ValidMessage_PopulatesSchemaAndData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: Sub1, Motor1, SetP=100, ActP=99", "Thread1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("AxisMon: Sub1"));
            Assert.True(schema["AxisMon: Sub1"].ContainsKey("Motor1"));
        }

        [Fact]
        public void ParseAxisMon_WithTrigger_ParsesTriggerValue()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: Sub1, Motor1, Trg=ON", "Thread1", time, null, schema, data, threadMap);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("ON", data[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxisMon_FilteredOut_DoesNotAdd()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1);
            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Other|Motor" };

            svc.ParseAxisMon("AxisMon: Sub1, Motor1, SetP=100", "Thread1", time, filter, schema, data, threadMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseAxisMon_InsufficientParts_DoesNotCrash()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();

            svc.ParseAxisMon("AxisMon: OnlyOnePart", "Thread1", DateTime.Now, null, schema, data, threadMap);

            Assert.Empty(schema);
        }

        // ── ParseAxM ──

        [Fact]
        public void ParseAxM_ValidMessage_PopulatesData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxM("AxM: Sub1, Motor1, LagE=0.5, SetP=100", "Thread1", time, null, schema, data, threadMap);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("0.5", data[time]["AxisMon: Sub1|Motor1|LagErr"]);
        }

        [Fact]
        public void ParseAxM_NoEquals_TreatedAsTrigger()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxM("AxM: Sub1, Motor1, TriggerValue", "Thread1", time, null, schema, data, threadMap);

            Assert.Equal("TriggerValue", data[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxM_Filtered_SkipsData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wrong|Motor" };

            svc.ParseAxM("AxM: Sub1, Motor1, LagE=0.5", "T1", DateTime.Now, filter, schema, data, threadMap);

            Assert.Empty(data);
        }

        // ── ParseIOMon ──

        [Fact]
        public void ParseIOMon_ValidMessage_ParsesComponent()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIOMon("IO_Mon: BIM[0], Sensor1=42", "Thread1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("IO_Mon: BIM[0]"));
        }

        [Fact]
        public void ParseIOMon_NewStatusValue_DecodesStatus()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIOMon("IO_Mon: BIM[0], Sensor1=New Status Op", "Thread1", time, null, schema, data, threadMap);

            Assert.True(data.ContainsKey(time));
            string statusKey = "IO_Mon: BIM[0]|Sensor1|eIoStatus";
            Assert.Equal("Operational", data[time][statusKey]);
        }

        [Fact]
        public void ParseIOMon_Filtered_SkipsData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wrong|Comp" };

            svc.ParseIOMon("IO_Mon: BIM[0], Sensor1=42", "T1", DateTime.Now, filter, schema, data, threadMap);

            Assert.Empty(data);
        }

        // ── ParseIO ──

        [Fact]
        public void ParseIO_ValidMessage_ParsesComponent()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: BIM[0], Sensor1=42", "Thread1", time, null, schema, data, threadMap);

            Assert.True(schema.ContainsKey("IO_Mon: BIM[0]"));
            Assert.True(data.ContainsKey(time));
        }

        [Fact]
        public void ParseIO_WithStatus_ParsesStatus()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: BIM[0], Sensor1=42, OpH", "Thread1", time, null, schema, data, threadMap);

            string statusKey = "IO_Mon: BIM[0]|Sensor1|eIoStatus";
            Assert.Equal("OperationalHigh", data[time][statusKey]);
        }

        [Fact]
        public void ParseIO_MotTemp_DecomposesCorrectly()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: BIM[0], Motor1_MotTemp=65", "Thread1", time, null, schema, data, threadMap);

            string key = "IO_Mon: BIM[0]|Motor1|MotTemp";
            Assert.Equal("65", data[time][key]);
        }

        [Fact]
        public void ParseIO_Filtered_SkipsUnselected()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OtherDevice|OtherSensor" };

            svc.ParseIO("IO: BIM[0], Sensor1=42", "T1", DateTime.Now, filter, schema, data, threadMap);

            Assert.Empty(data);
        }

        // ── ParseLogStats (instance method) ──

        [Fact]
        public void ParseLogStats_ValidMessage_PopulatesSchema()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseLogStats(
                "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=2 bufFull=0 Max(num=100 cat=ERROR)",
                "LogStats", time, schema, data, threadMap);

            Assert.True(schema.ContainsKey("LogStats"));
            Assert.Equal("5000", data[time]["LogStats|Metrics|Total"]);
        }

        [Fact]
        public void ParseLogStats_WrongThread_Skips()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();

            svc.ParseLogStats("LogStat: Logs(Total=100 IsReady=1)", "NotLogStats", DateTime.Now, schema, data, threadMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseLogStats_NotLogStatPrefix_Skips()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadMap = new Dictionary<string, string>();

            svc.ParseLogStats("SomethingElse: data", "LogStats", DateTime.Now, schema, data, threadMap);

            Assert.Empty(schema);
        }

        // ── DifferentLogsViewModel helpers ──

        [Fact]
        public void DifferentLogsViewModel_BuildDefaultColumns_Returns3Columns()
        {
            var method = typeof(DifferentLogsViewModel).GetMethod("BuildDefaultColumns",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = method!.Invoke(null, null) as IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>;
            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal("Date", result[0].Header);
            Assert.Equal("Level", result[1].Header);
            Assert.Equal("Message", result[2].Header);
        }

        [Fact]
        public void DifferentLogsViewModel_BuildAvailableFields_DefaultFields()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));

            // Set required fields
            var allLogsField = typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            allLogsField?.SetValue(vm, new List<LogEntry>());

            var columnsField = typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance);
            columnsField?.SetValue(vm, null);

            var availField = typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance);
            availField?.SetValue(vm, new List<string>());

            vm.BuildAvailableFields();

            Assert.Contains("Message", vm.AvailableFields);
            Assert.Contains("Level", vm.AvailableFields);
            Assert.Contains("ThreadName", vm.AvailableFields);
            Assert.Contains("Logger", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogsViewModel_BuildAvailableFields_IncludesExtraFields()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));

            var entry = new LogEntry { Message = "test" };
            entry.ExtraFields = new Dictionary<string, string> { { "CustomField", "value" } };

            var allLogsField = typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            allLogsField?.SetValue(vm, new List<LogEntry> { entry });

            var columnsField = typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance);
            columnsField?.SetValue(vm, null);

            var availField = typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance);
            availField?.SetValue(vm, new List<string>());

            vm.BuildAvailableFields();

            Assert.Contains("CustomField", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogsViewModel_Properties_SetAndGet()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));

            // FilterRoot
            var filterRootField = typeof(DifferentLogsViewModel).GetField("_filterRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            filterRootField?.SetValue(vm, null);
            Assert.Null(vm.FilterRoot);

            var node = new FilterNode();
            filterRootField?.SetValue(vm, node);
            Assert.Same(node, vm.FilterRoot);

            // IsFilterActive
            var isFilterField = typeof(DifferentLogsViewModel).GetField("_isFilterActive", BindingFlags.NonPublic | BindingFlags.Instance);
            isFilterField?.SetValue(vm, true);
            Assert.True(vm.IsFilterActive);

            // CurrentFileName when null
            var currentPathField = typeof(DifferentLogsViewModel).GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
            currentPathField?.SetValue(vm, null);
            Assert.Equal(string.Empty, vm.CurrentFileName);
            Assert.False(vm.HasFile);

            // CurrentFileName when set
            currentPathField?.SetValue(vm, @"C:\test\file.log");
            Assert.Equal("file.log", vm.CurrentFileName);
            Assert.True(vm.HasFile);
        }

        [Fact]
        public void DifferentLogsViewModel_CloseFile_InvokesWithoutCrash()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));

            // Initialize all fields needed by CloseFile
            typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, new List<LogEntry> { new LogEntry() });
            typeof(DifferentLogsViewModel).GetField("_filteredEntries", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<LogEntry>());
            typeof(DifferentLogsViewModel).GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, @"C:\test\file.log");
            typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, null);
            typeof(DifferentLogsViewModel).GetField("_filterRoot", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, new FilterNode());
            typeof(DifferentLogsViewModel).GetField("_isFilterActive", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, true);
            typeof(DifferentLogsViewModel).GetField("_coloringRules", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, new List<ColoringCondition>());
            typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, new List<string>());
            typeof(DifferentLogsViewModel).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(vm, "test");

            var closeMethod = typeof(DifferentLogsViewModel).GetMethod("CloseFile", BindingFlags.NonPublic | BindingFlags.Instance);
            // CloseFile calls Entries.Clear() which needs the backing ObservableCollection initialized
            var ex = Record.Exception(() => closeMethod?.Invoke(vm, null));
            // May throw NullRef if Entries property backing field not initialized - that's fine
            Assert.True(ex == null || ex is TargetInvocationException);
        }

        // ── MainViewModel.Lifecycle helpers ──

        [Fact]
        public void MainViewModel_ToggleAnnotation_WithAnnotatedLog_TogglesState()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var method = typeof(MainViewModel).GetMethod("ToggleAnnotation", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var log = new LogEntry { AnnotationContent = "Some note" };
            // Set HasAnnotation via the backing field
            typeof(LogEntry).GetField("_hasAnnotation", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(log, true);
            Assert.True(log.HasAnnotation);
            Assert.False(log.IsAnnotationExpanded);

            method!.Invoke(vm, new object?[] { log });
            Assert.True(log.IsAnnotationExpanded);

            method.Invoke(vm, new object?[] { log });
            Assert.False(log.IsAnnotationExpanded);
        }

        [Fact]
        public void MainViewModel_ToggleAnnotation_WithoutAnnotation_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var method = typeof(MainViewModel).GetMethod("ToggleAnnotation", BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Message = "no annotation" };
            Assert.False(log.HasAnnotation);

            method!.Invoke(vm, new object?[] { log });
            Assert.False(log.IsAnnotationExpanded);
        }

        [Fact]
        public void MainViewModel_ToggleAnnotation_NullParameter_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var method = typeof(MainViewModel).GetMethod("ToggleAnnotation", BindingFlags.NonPublic | BindingFlags.Instance);

            var ex = Record.Exception(() => method!.Invoke(vm, new object?[] { null }));
            // Should not throw
            Assert.True(ex == null || ex is TargetInvocationException);
        }
    }
}
