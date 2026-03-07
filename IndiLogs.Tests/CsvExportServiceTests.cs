using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class CsvExportServiceTests
    {
        private readonly CsvExportService _service = new();

        // ===================================================================
        // DecodeIoStatus tests
        // ===================================================================

        [Theory]
        [InlineData("Inv", "InvalidHW")]
        [InlineData("NM", "NotMonitored")]
        [InlineData("NA", "NotActive")]
        [InlineData("Op", "Operational")]
        [InlineData("OpL", "OperationalLow")]
        [InlineData("OpH", "OperationalHigh")]
        [InlineData("PnL", "PanicLow")]
        [InlineData("PnH", "PanicHigh")]
        public void DecodeIoStatus_KnownCodes_ReturnsExpectedText(string input, string expected)
        {
            var result = CsvExportService.DecodeIoStatus(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DecodeIoStatus_EmptyString_ReturnsOp()
        {
            var result = CsvExportService.DecodeIoStatus("");
            Assert.Equal("Op", result);
        }

        [Fact]
        public void DecodeIoStatus_Null_ReturnsOp()
        {
            var result = CsvExportService.DecodeIoStatus(null!);
            Assert.Equal("Op", result);
        }

        [Fact]
        public void DecodeIoStatus_UnknownCode_ReturnsTrimmedInput()
        {
            var result = CsvExportService.DecodeIoStatus(" CustomStatus ");
            Assert.Equal("CustomStatus", result);
        }

        [Fact]
        public void DecodeIoStatus_WhitespacePadded_TrimsAndDecodes()
        {
            var result = CsvExportService.DecodeIoStatus(" PnH ");
            Assert.Equal("PanicHigh", result);
        }

        // ===================================================================
        // DecomposeIOSymbol tests
        // ===================================================================

        [Fact]
        public void DecomposeIOSymbol_MotTempSuffix_ExtractsComponentAndParam()
        {
            CsvExportService.DecomposeIOSymbol("Motor1_MotTemp", out string componentName, out string paramName);
            Assert.Equal("Motor1", componentName);
            Assert.Equal("MotTemp", paramName);
        }

        [Fact]
        public void DecomposeIOSymbol_DrvTempSuffix_ExtractsComponentAndParam()
        {
            CsvExportService.DecomposeIOSymbol("Driver2_DrvTemp", out string componentName, out string paramName);
            Assert.Equal("Driver2", componentName);
            Assert.Equal("DrvTemp", paramName);
        }

        [Fact]
        public void DecomposeIOSymbol_NoKnownSuffix_ReturnsFullNameAsComponent()
        {
            CsvExportService.DecomposeIOSymbol("SomeSignal", out string componentName, out string paramName);
            Assert.Equal("SomeSignal", componentName);
            Assert.Equal("Value", paramName);
        }

        [Fact]
        public void DecomposeIOSymbol_MotTempCaseInsensitive_Extracts()
        {
            CsvExportService.DecomposeIOSymbol("Pump_mottemp", out string componentName, out string paramName);
            Assert.Equal("Pump", componentName);
            Assert.Equal("MotTemp", paramName);
        }

        [Fact]
        public void DecomposeIOSymbol_DrvTempCaseInsensitive_Extracts()
        {
            CsvExportService.DecomposeIOSymbol("Valve_drvtemp", out string componentName, out string paramName);
            Assert.Equal("Valve", componentName);
            Assert.Equal("DrvTemp", paramName);
        }

        [Fact]
        public void DecomposeIOSymbol_NameContainsMotTempButNotSuffix_ReturnsValue()
        {
            CsvExportService.DecomposeIOSymbol("MotTemp_Sensor", out string componentName, out string paramName);
            Assert.Equal("MotTemp_Sensor", componentName);
            Assert.Equal("Value", paramName);
        }

        // ===================================================================
        // TryParsePlcMngrState tests
        // ===================================================================

        [Fact]
        public void TryParsePlcMngrState_ValidPlcMngrMessage_ExtractsStateName()
        {
            string msg = "CHStep: PlcMngr, Printing, State 5 <Parent, 1, 0, 100, 0, 0>";
            var result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.True(result);
            Assert.Equal("Printing", stateName);
        }

        [Fact]
        public void TryParsePlcMngrState_NonPlcMngr_ReturnsFalse()
        {
            string msg = "CHStep: OtherComponent, SomeState, State 3 <Parent, 1, 0, 100, 0, 0>";
            var result = CsvExportService.TryParsePlcMngrState(msg, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_NullMessage_ReturnsFalse()
        {
            var result = CsvExportService.TryParsePlcMngrState(null!, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_EmptyMessage_ReturnsFalse()
        {
            var result = CsvExportService.TryParsePlcMngrState("", out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_TooShortMessage_ReturnsFalse()
        {
            var result = CsvExportService.TryParsePlcMngrState("CHStep: PlcMngr", out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_NotCHStepPrefix_ReturnsFalse()
        {
            string msg = "IO_Mon: PlcMngr, SomeState, Data";
            var result = CsvExportService.TryParsePlcMngrState(msg, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParsePlcMngrState_PlcMngrWithCommaAfterState_ExtractsUpToComma()
        {
            string msg = "CHStep: PlcMngr, Idle, State 2 <Parent, 1, 0, 0, 0, 0>";
            var result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.True(result);
            Assert.Equal("Idle", stateName);
        }

        // ===================================================================
        // TryParseCHStep tests
        // ===================================================================

        [Fact]
        public void TryParseCHStep_ValidMessage_ParsesAllFields()
        {
            string msg = "CHStep: CompName, Step message text, State 42 <ParentComp, 7, 3, 150, 1, 0>";
            var result = CsvExportService.TryParseCHStep(msg,
                out string? chName, out string? stepMessage, out string? stateId,
                out string? chParentName, out string? subsysID, out string? prevStepNo,
                out string? diffTime, out string? subStepNo, out string? chObjType);

            Assert.True(result);
            Assert.Equal("CompName", chName);
            Assert.Equal("Step message text", stepMessage);
            Assert.Equal("42", stateId);
            Assert.Equal("ParentComp", chParentName);
            Assert.Equal("7", subsysID);
            Assert.Equal("3", prevStepNo);
            Assert.Equal("150", diffTime);
            Assert.Equal("1", subStepNo);
            Assert.Equal("0", chObjType);
        }

        [Fact]
        public void TryParseCHStep_TooShortMessage_ReturnsFalse()
        {
            var result = CsvExportService.TryParseCHStep("CHStep: short",
                out _, out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseCHStep_MissingBrackets_ReturnsFalse()
        {
            string msg = "CHStep: CompName, Step message text, State 42 ParentComp, 7, 3, 150, 1, 0";
            var result = CsvExportService.TryParseCHStep(msg,
                out _, out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseCHStep_MissingStateKeyword_ReturnsFalse()
        {
            string msg = "CHStep: CompName, Step message text, Id 42 <ParentComp, 7, 3, 150, 1, 0>";
            var result = CsvExportService.TryParseCHStep(msg,
                out _, out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseCHStep_InsufficientBracketParts_ReturnsFalse()
        {
            string msg = "CHStep: CompName, Step text, State 1 <Parent, 7, 3>";
            var result = CsvExportService.TryParseCHStep(msg,
                out _, out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseCHStep_ObjType1_ParsedCorrectly()
        {
            string msg = "CHStep: Motor, Running, State 10 <Subsystem, 2, 5, 200, 0, 1>";
            var result = CsvExportService.TryParseCHStep(msg,
                out string? chName, out string? stepMessage, out string? stateId,
                out string? chParentName, out string? subsysID, out string? prevStepNo,
                out string? diffTime, out string? subStepNo, out string? chObjType);

            Assert.True(result);
            Assert.Equal("Motor", chName);
            Assert.Equal("Running", stepMessage);
            Assert.Equal("10", stateId);
            Assert.Equal("Subsystem", chParentName);
            Assert.Equal("2", subsysID);
            Assert.Equal("5", prevStepNo);
            Assert.Equal("200", diffTime);
            Assert.Equal("0", subStepNo);
            Assert.Equal("1", chObjType);
        }

        // ===================================================================
        // TryParseLogStats tests
        // ===================================================================

        [Fact]
        public void TryParseLogStats_FullMessage_ParsesAllFields()
        {
            string msg = "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=Errors)";
            var result = CsvExportService.TryParseLogStats(msg,
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
            Assert.Equal("Errors", maxCat);
        }

        [Fact]
        public void TryParseLogStats_NonLogStatPrefix_ReturnsFalse()
        {
            string msg = "SomeOther: Logs(Total=100 IsReady=1)";
            var result = CsvExportService.TryParseLogStats(msg,
                out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseLogStats_MissingTotal_ReturnsFalse()
        {
            string msg = "LogStat: nSemMissed(total=0 Mult=0) Lost=0 bufFull=0";
            var result = CsvExportService.TryParseLogStats(msg,
                out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseLogStats_PartialMessage_ParsesAvailableFields()
        {
            string msg = "LogStat: Logs(Total=250 IsReady=0) Lost=5 bufFull=2";
            var result = CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out string? semTotal,
                out string? semMult, out string? lost, out string? bufFull,
                out string? maxNum, out string? maxCat);

            Assert.True(result);
            Assert.Equal("250", total);
            Assert.Equal("0", isReady);
            Assert.Null(semTotal);
            Assert.Null(semMult);
            Assert.Equal("5", lost);
            Assert.Equal("2", bufFull);
            Assert.Null(maxNum);
            Assert.Null(maxCat);
        }

        [Fact]
        public void TryParseLogStats_LargeNumbers_ParsesCorrectly()
        {
            string msg = "LogStat: Logs(Total=999999 IsReady=1) nSemMissed(total=42 Mult=7) Lost=123 bufFull=456 Max(num=789 cat=Warnings)";
            var result = CsvExportService.TryParseLogStats(msg,
                out string? total, out string? isReady, out string? semTotal,
                out string? semMult, out string? lost, out string? bufFull,
                out string? maxNum, out string? maxCat);

            Assert.True(result);
            Assert.Equal("999999", total);
            Assert.Equal("1", isReady);
            Assert.Equal("42", semTotal);
            Assert.Equal("7", semMult);
            Assert.Equal("123", lost);
            Assert.Equal("456", bufFull);
            Assert.Equal("789", maxNum);
            Assert.Equal("Warnings", maxCat);
        }

        // ===================================================================
        // AddToSchema tests
        // ===================================================================

        [Fact]
        public void AddToSchema_NewSubsystem_CreatesHierarchy()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            _service.AddToSchema(schema, "AxisMon: Sub1", "Motor1", new[] { "SetP", "ActP" });

            Assert.Single(schema);
            Assert.True(schema.ContainsKey("AxisMon: Sub1"));
            Assert.Single(schema["AxisMon: Sub1"]);
            Assert.True(schema["AxisMon: Sub1"].ContainsKey("Motor1"));
            Assert.Equal(2, schema["AxisMon: Sub1"]["Motor1"].Count);
            Assert.Contains("SetP", schema["AxisMon: Sub1"]["Motor1"]);
            Assert.Contains("ActP", schema["AxisMon: Sub1"]["Motor1"]);
        }

        [Fact]
        public void AddToSchema_ExistingSubsystem_AddsParams()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            _service.AddToSchema(schema, "AxisMon: Sub1", "Motor1", new[] { "SetP" });
            _service.AddToSchema(schema, "AxisMon: Sub1", "Motor1", new[] { "ActP", "Trq" });

            Assert.Single(schema);
            Assert.Equal(3, schema["AxisMon: Sub1"]["Motor1"].Count);
        }

        [Fact]
        public void AddToSchema_DuplicateParam_DoesNotDuplicate()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            _service.AddToSchema(schema, "Sub", "Comp", new[] { "Param1" });
            _service.AddToSchema(schema, "Sub", "Comp", new[] { "Param1" });

            Assert.Single(schema["Sub"]["Comp"]);
        }

        [Fact]
        public void AddToSchema_CaseInsensitive_MergesSubsystems()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            _service.AddToSchema(schema, "axismon: sub1", "Motor1", new[] { "SetP" });
            _service.AddToSchema(schema, "AxisMon: Sub1", "Motor1", new[] { "ActP" });

            Assert.Single(schema);
            Assert.Equal(2, schema["AxisMon: Sub1"]["Motor1"].Count);
        }

        [Fact]
        public void AddToSchema_MultipleComponents_UnderSameSubsystem()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            _service.AddToSchema(schema, "IO_Mon: Sub1", "Sensor1", new[] { "Value" });
            _service.AddToSchema(schema, "IO_Mon: Sub1", "Sensor2", new[] { "Value" });

            Assert.Single(schema);
            Assert.Equal(2, schema["IO_Mon: Sub1"].Count);
        }

        // ===================================================================
        // ParseAndAddValue tests
        // ===================================================================

        [Fact]
        public void ParseAndAddValue_ValidParam_AddsToDataMatrix()
        {
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);
            var validParams = new[] { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr", "Trigger" };

            _service.ParseAndAddValue("SetP=123.45", "AxisMon: Sub1", "Motor1", time, data, validParams);

            Assert.Single(data);
            Assert.Equal("123.45", data[time]["AxisMon: Sub1|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAndAddValue_InvalidParam_DoesNotAdd()
        {
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);
            var validParams = new[] { "SetP", "ActP" };

            _service.ParseAndAddValue("UnknownParam=42", "Sub", "Motor", time, data, validParams);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_NoEquals_DoesNotAdd()
        {
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);
            var validParams = new[] { "SetP" };

            _service.ParseAndAddValue("SetP_no_equals", "Sub", "Motor", time, data, validParams);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_MultipleValuesAtSameTime_AllStored()
        {
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);
            var validParams = new[] { "SetP", "ActP" };

            _service.ParseAndAddValue("SetP=100", "Sub", "M1", time, data, validParams);
            _service.ParseAndAddValue("ActP=200", "Sub", "M1", time, data, validParams);

            Assert.Single(data);
            Assert.Equal("100", data[time]["Sub|M1|SetP"]);
            Assert.Equal("200", data[time]["Sub|M1|ActP"]);
        }

        // ===================================================================
        // ParseAxisMon tests
        // ===================================================================

        [Fact]
        public void ParseAxisMon_ValidMessage_PopulatesSchemaAndData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxisMon("AxisMon: Subsys1, Motor1, SetP=100, ActP=200", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("AxisMon: Subsys1"));
            Assert.True(schema["AxisMon: Subsys1"].ContainsKey("Motor1"));
            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("100", dataMatrix[time]["AxisMon: Subsys1|Motor1|SetP"]);
            Assert.Equal("200", dataMatrix[time]["AxisMon: Subsys1|Motor1|ActP"]);
        }

        [Fact]
        public void ParseAxisMon_WithTrigger_ParsesTriggerValue()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxisMon("AxisMon: Sub1, Motor1, Trg=5", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("5", dataMatrix[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxisMon_WithSelectedAxis_FiltersCorrectly()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            var selectedAxis = new HashSet<string> { "Sub1|MotorA" };

            _service.ParseAxisMon("AxisMon: Sub1, MotorB, SetP=999", "Thread1", time,
                selectedAxis, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseAxisMon_SelectedAxisMatch_IncludesData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            var selectedAxis = new HashSet<string> { "Sub1|Motor1" };

            _service.ParseAxisMon("AxisMon: Sub1, Motor1, SetP=50", "Thread1", time,
                selectedAxis, schema, dataMatrix, threadNameMap);

            Assert.Single(dataMatrix);
            Assert.Equal("50", dataMatrix[time]["AxisMon: Sub1|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAxisMon_TooFewParts_DoesNothing()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxisMon("AxisMon: Sub1, Motor1", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseAxisMon_SetsThreadNameMap()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxisMon("AxisMon: Sub1, Motor1, SetP=100", "MyThread", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("MyThread", threadNameMap["AxisMon: Sub1|Motor1|SetP"]);
            Assert.Equal("MyThread", threadNameMap["AxisMon: Sub1|Motor1|ActP"]);
            Assert.Equal("MyThread", threadNameMap["AxisMon: Sub1|Motor1|Trigger"]);
        }

        // ===================================================================
        // ParseAxM tests
        // ===================================================================

        [Fact]
        public void ParseAxM_ValidMessage_ParsesData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxM("AxM: Sub1, Motor1, SetP=55, ActP=60", "Thread2", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("AxisMon: Sub1"));
            Assert.Equal("55", dataMatrix[time]["AxisMon: Sub1|Motor1|SetP"]);
            Assert.Equal("60", dataMatrix[time]["AxisMon: Sub1|Motor1|ActP"]);
        }

        [Fact]
        public void ParseAxM_LagE_ParsesAsLagErr()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxM("AxM: Sub1, Motor1, LagE=0.5", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("0.5", dataMatrix[time]["AxisMon: Sub1|Motor1|LagErr"]);
        }

        [Fact]
        public void ParseAxM_TriggerWithoutEquals_ParsesAsTrigger()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseAxM("AxM: Sub1, Motor1, TriggerValue", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("TriggerValue", dataMatrix[time]["AxisMon: Sub1|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxM_FilteredOut_NoData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            var selectedAxis = new HashSet<string> { "Sub1|MotorX" };

            _service.ParseAxM("AxM: Sub1, Motor1, SetP=10", "Thread1", time,
                selectedAxis, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ===================================================================
        // ParseIOMon tests
        // ===================================================================

        [Fact]
        public void ParseIOMon_ValuePair_ParsesCorrectly()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIOMon("IO_Mon: SubSys1, Sensor1=42.5 extra", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("IO_Mon: SubSys1"));
            Assert.Equal("42.5", dataMatrix[time]["IO_Mon: SubSys1|Sensor1|Value"]);
        }

        [Fact]
        public void ParseIOMon_MotTempSuffix_DecomposesCorrectly()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIOMon("IO_Mon: SubSys1, Motor1_MotTemp=85", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("85", dataMatrix[time]["IO_Mon: SubSys1|Motor1|MotTemp"]);
        }

        [Fact]
        public void ParseIOMon_NewStatusValue_DecodesIoStatus()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIOMon("IO_Mon: SubSys1, Signal1=New Status PnH", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("PanicHigh", dataMatrix[time]["IO_Mon: SubSys1|Signal1|eIoStatus"]);
        }

        [Fact]
        public void ParseIOMon_SelectedIOFilter_ExcludesNonMatching()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            var selectedIO = new HashSet<string> { "SubSys1|OtherSensor" };

            _service.ParseIOMon("IO_Mon: SubSys1, Sensor1=42.5", "Thread1", time,
                selectedIO, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseIOMon_TooFewParts_DoesNothing()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIOMon("IO_Mon: SubSys1", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ===================================================================
        // ParseIO tests
        // ===================================================================

        [Fact]
        public void ParseIO_SimpleValue_ParsesCorrectly()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIO("IO: Sub1, Signal1=100", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("IO_Mon: Sub1"));
            Assert.Equal("100", dataMatrix[time]["IO_Mon: Sub1|Signal1|Value"]);
        }

        [Fact]
        public void ParseIO_WithStatusPart_IncludesIoStatus()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIO("IO: Sub1, Signal1=100, OpH", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("100", dataMatrix[time]["IO_Mon: Sub1|Signal1|Value"]);
            Assert.Equal("OperationalHigh", dataMatrix[time]["IO_Mon: Sub1|Signal1|eIoStatus"]);
        }

        [Fact]
        public void ParseIO_WithMotTempSymbol_DecomposesCorrectly()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIO("IO: Sub1, Pump_MotTemp=90", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("90", dataMatrix[time]["IO_Mon: Sub1|Pump|MotTemp"]);
        }

        [Fact]
        public void ParseIO_FilteredBySelectedIO_Excluded()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            var selectedIO = new HashSet<string> { "Sub1|OtherSignal" };

            _service.ParseIO("IO: Sub1, Signal1=100", "Thread1", time,
                selectedIO, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseIO_StatusPartWithEquals_SkipsIoStatus()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseIO("IO: Sub1, Signal1=100, Key=Val", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("100", dataMatrix[time]["IO_Mon: Sub1|Signal1|Value"]);
            Assert.False(dataMatrix[time].ContainsKey("IO_Mon: Sub1|Signal1|eIoStatus"));
        }

        // ===================================================================
        // ParseLogStats instance method tests
        // ===================================================================

        [Fact]
        public void ParseLogStats_ValidLogStatsThread_PopulatesSchemaAndData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseLogStats(
                "LogStat: Logs(Total=1000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=50 cat=Info)",
                "LogStats", time, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("LogStats"));
            Assert.True(schema["LogStats"].ContainsKey("Metrics"));
            Assert.Equal("1000", dataMatrix[time]["LogStats|Metrics|Total"]);
            Assert.Equal("1", dataMatrix[time]["LogStats|Metrics|IsReady"]);
            Assert.Equal("0", dataMatrix[time]["LogStats|Metrics|Lost"]);
        }

        [Fact]
        public void ParseLogStats_WrongThreadName_DoesNothing()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseLogStats(
                "LogStat: Logs(Total=1000 IsReady=1)",
                "OtherThread", time, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseLogStats_WrongMessagePrefix_DoesNothing()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseLogStats(
                "SomeOther: data here",
                "LogStats", time, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseLogStats_SetsThreadNameMap()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            _service.ParseLogStats(
                "LogStat: Logs(Total=500 IsReady=1) Lost=0 bufFull=0",
                "LogStats", time, schema, dataMatrix, threadNameMap);

            Assert.Equal("LogStats", threadNameMap["LogStats|Metrics|Total"]);
            Assert.Equal("LogStats", threadNameMap["LogStats|Metrics|Lost"]);
        }
    }
}
