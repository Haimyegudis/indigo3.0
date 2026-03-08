using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace IndiLogs.Tests
{
    // ===================================================================
    // CsvExportService — Static Parsing Helpers
    // ===================================================================

    public class CsvExportPlcMngrStateTests
    {
        [Fact]
        public void TryParsePlcMngrState_NullMessage_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState(null!, out _));
        }

        [Fact]
        public void TryParsePlcMngrState_EmptyMessage_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_ShortMessage_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: short", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NotCHStep_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("AxisMon: PlcMngr, Printing, State 1 <x>", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NoComma_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: PlcMngrNoComma", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NotPlcMngr_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: OtherCH, SomeState, something else here", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_ValidWithSecondComma_ReturnsTrue()
        {
            bool result = CsvExportService.TryParsePlcMngrState(
                "CHStep: PlcMngr, Printing, State 5 <Parent, 1, 2, 3, 4, 5>", out string? state);
            Assert.True(result);
            Assert.Equal("Printing", state);
        }

        [Fact]
        public void TryParsePlcMngrState_ValidWithStateKeyword_ReturnsTrue()
        {
            bool result = CsvExportService.TryParsePlcMngrState(
                "CHStep: PlcMngr, Idle State Active", out string? state);
            Assert.True(result);
            Assert.Equal("Idle", state);
        }

        [Fact]
        public void TryParsePlcMngrState_NoSecondCommaOrState_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState(
                "CHStep: PlcMngr, SomethingWithoutCommaOrState", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_CaseInsensitive()
        {
            bool result = CsvExportService.TryParsePlcMngrState(
                "chstep: plcmngr, Running, State 3 <P>", out string? state);
            Assert.True(result);
            Assert.Equal("Running", state);
        }

        [Fact]
        public void TryParsePlcMngrState_EmptyStateName_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState(
                "CHStep: PlcMngr, , State 3 <P>", out _));
        }
    }

    public class CsvExportCHStepTests
    {
        [Fact]
        public void TryParseCHStep_ShortMessage_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep("short", out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoCHStepPrefix_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "AxisMon: some, long, message, with, many, fields, 1234567890",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoFirstComma_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: NoCommaInThisVeryLongMsg",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoSecondComma_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName only one comma here",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoStateKeyword_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName, StepMsg, NoStateHereAtAll",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoDigitsAfterState_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName, StepMsg, State ABC <P,1,2,3,4,5>",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoOpenBracket_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName, StepMsg, State 5 NoBrackets123",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoCloseBracket_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName, StepMsg, State 5 <Parent,1,2,3,4,5",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_TooFewPartsInBrackets_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseCHStep(
                "CHStep: CHName, StepMsg, State 5 <Parent,1,2>",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_ValidMessage_ParsesAllFields()
        {
            bool result = CsvExportService.TryParseCHStep(
                "CHStep: PlcMngr, Printing Step, State 42 <ParentCH, SubSys1, 10, 500, 3, 1>",
                out string? chName, out string? stepMessage, out string? stateId,
                out string? chParent, out string? subsysID, out string? prevStepNo,
                out string? diffTime, out string? subStepNo, out string? chObjType);

            Assert.True(result);
            Assert.Equal("PlcMngr", chName);
            Assert.Equal("Printing Step", stepMessage);
            Assert.Equal("42", stateId);
            Assert.Equal("ParentCH", chParent);
            Assert.Equal("SubSys1", subsysID);
            Assert.Equal("10", prevStepNo);
            Assert.Equal("500", diffTime);
            Assert.Equal("3", subStepNo);
            Assert.Equal("1", chObjType);
        }

        [Fact]
        public void TryParseCHStep_CaseInsensitive()
        {
            bool result = CsvExportService.TryParseCHStep(
                "chstep: Motor1, Init, state 1 <Root, Sub1, 0, 100, 0, 0>",
                out string? chName, out _, out _, out _, out _, out _, out _, out _, out _);
            Assert.True(result);
            Assert.Equal("Motor1", chName);
        }
    }

    public class CsvExportLogStatsTests
    {
        [Fact]
        public void TryParseLogStats_NotLogStat_ReturnsFalse()
        {
            Assert.False(CsvExportService.TryParseLogStats("SomeOtherMessage", out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseLogStats_ValidFull_ParsesAllFields()
        {
            string msg = "LogStat: Logs(Total=1234 IsReady=5) nSemMissed(total=10 Mult=2) Lost=3 bufFull=4 Max(num=100 cat=ErrorCat)";
            bool result = CsvExportService.TryParseLogStats(msg, out string? total, out string? isReady,
                out string? semTotal, out string? semMult, out string? lost, out string? bufFull,
                out string? maxNum, out string? maxCat);

            Assert.True(result);
            Assert.Equal("1234", total);
            Assert.Equal("5", isReady);
            Assert.Equal("10", semTotal);
            Assert.Equal("2", semMult);
            Assert.Equal("3", lost);
            Assert.Equal("4", bufFull);
            Assert.Equal("100", maxNum);
            Assert.Equal("ErrorCat", maxCat);
        }

        [Fact]
        public void TryParseLogStats_MinimalValid_OnlyTotal()
        {
            string msg = "LogStat: Logs(Total=42 IsReady=1)";
            bool result = CsvExportService.TryParseLogStats(msg, out string? total, out _, out _, out _, out _, out _, out _, out _);
            Assert.True(result);
            Assert.Equal("42", total);
        }

        [Fact]
        public void TryParseLogStats_NoLogsSection_ReturnsFalse()
        {
            string msg = "LogStat: nSemMissed(total=10 Mult=2) Lost=3";
            Assert.False(CsvExportService.TryParseLogStats(msg, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseLogStats_CaseInsensitive()
        {
            string msg = "logstat: Logs(Total=99 IsReady=0) Lost=1 bufFull=0";
            bool result = CsvExportService.TryParseLogStats(msg, out string? total, out _, out _, out _, out string? lost, out string? bufFull, out _, out _);
            Assert.True(result);
            Assert.Equal("99", total);
            Assert.Equal("1", lost);
            Assert.Equal("0", bufFull);
        }
    }

    // ===================================================================
    // CsvExportService — DecodeIoStatus
    // ===================================================================

    public class CsvExportDecodeIoStatusTests
    {
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
        [InlineData("UnknownStatus", "UnknownStatus")]
        [InlineData("  Op  ", "Operational")]
        public void DecodeIoStatus_Maps_Correctly(string? input, string expected)
        {
            Assert.Equal(expected, CsvExportService.DecodeIoStatus(input!));
        }
    }

    // ===================================================================
    // CsvExportService — DecomposeIOSymbol
    // ===================================================================

    public class CsvExportDecomposeIOSymbolTests
    {
        [Fact]
        public void DecomposeIOSymbol_MotTemp_Suffix()
        {
            CsvExportService.DecomposeIOSymbol("Pump_MotTemp", out string comp, out string param);
            Assert.Equal("Pump", comp);
            Assert.Equal("MotTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_DrvTemp_Suffix()
        {
            CsvExportService.DecomposeIOSymbol("Motor1_DrvTemp", out string comp, out string param);
            Assert.Equal("Motor1", comp);
            Assert.Equal("DrvTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_NoSuffix_ValueParam()
        {
            CsvExportService.DecomposeIOSymbol("SomeSignal", out string comp, out string param);
            Assert.Equal("SomeSignal", comp);
            Assert.Equal("Value", param);
        }

        [Fact]
        public void DecomposeIOSymbol_CaseInsensitive_MotTemp()
        {
            CsvExportService.DecomposeIOSymbol("Pump_MOTTEMP", out string comp, out string param);
            Assert.Equal("Pump", comp);
            Assert.Equal("MotTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_CaseInsensitive_DrvTemp()
        {
            CsvExportService.DecomposeIOSymbol("Motor_drvtemp", out string comp, out string param);
            Assert.Equal("Motor", comp);
            Assert.Equal("DrvTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_ExactMotTemp_NotJustEnding()
        {
            CsvExportService.DecomposeIOSymbol("_MotTemp", out string comp, out string param);
            Assert.Equal("", comp);
            Assert.Equal("MotTemp", param);
        }
    }

    // ===================================================================
    // CsvExportService — AddToSchema
    // ===================================================================

    public class CsvExportAddToSchemaTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        [Fact]
        public void AddToSchema_CreatesNewSubsystem()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P1", "P2" });

            Assert.True(schema.ContainsKey("Sub1"));
            Assert.True(schema["Sub1"].ContainsKey("Comp1"));
            Assert.Contains("P1", schema["Sub1"]["Comp1"]);
            Assert.Contains("P2", schema["Sub1"]["Comp1"]);
        }

        [Fact]
        public void AddToSchema_AddsToExisting()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P1" });
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P2" });

            Assert.Equal(2, schema["Sub1"]["Comp1"].Count);
        }

        [Fact]
        public void AddToSchema_DuplicateParams_NoDuplicates()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "P1", "P1" });

            Assert.Single(schema["Sub1"]["Comp1"]);
        }
    }

    // ===================================================================
    // CsvExportService — ParseAndAddValue
    // ===================================================================

    public class CsvExportParseAndAddValueTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        [Fact]
        public void ParseAndAddValue_ValidParam_AddsToData()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP", "ActP" };
            var time = DateTime.Now;

            svc.ParseAndAddValue("SetP=100.5", "AxisMon: Sub1", "Motor1", time, data, validParams);

            Assert.True(data.ContainsKey(time));
            Assert.Equal("100.5", data[time]["AxisMon: Sub1|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAndAddValue_InvalidParam_DoesNotAdd()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var validParams = new[] { "SetP" };
            var time = DateTime.Now;

            svc.ParseAndAddValue("InvalidParam=100", "Sub", "Motor", time, data, validParams);

            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_NoEquals_DoesNotAdd()
        {
            var svc = CreateService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            svc.ParseAndAddValue("NoEquals", "Sub", "Motor", DateTime.Now, data, new[] { "P1" });
            Assert.Empty(data);
        }

        [Fact]
        public void ParseAndAddValue_ExistingTimeRow_AddsToRow()
        {
            var svc = CreateService();
            var time = DateTime.Now;
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>
            {
                [time] = new Dictionary<string, string> { ["existing"] = "val" }
            };
            var validParams = new[] { "P1" };

            svc.ParseAndAddValue("P1=42", "Sub", "Mot", time, data, validParams);

            Assert.Equal(2, data[time].Count);
            Assert.Equal("42", data[time]["Sub|Mot|P1"]);
        }
    }

    // ===================================================================
    // CsvExportService — ParseAxisMon / ParseAxM / ParseIOMon / ParseIO
    // ===================================================================

    public class CsvExportLogParsingTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        [Fact]
        public void ParseAxisMon_ValidMessage_ParsesData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: SubSys, Motor1, SetP=100, ActP=99",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("AxisMon: SubSys"));
            Assert.True(schema["AxisMon: SubSys"].ContainsKey("Motor1"));
        }

        [Fact]
        public void ParseAxisMon_WithTrigger_ParsesTriggerValue()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: Sub1, Mot1, Trg=5",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("5", dataMatrix[time]["AxisMon: Sub1|Mot1|Trigger"]);
        }

        [Fact]
        public void ParseAxisMon_FilteredOut_BySelection()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selectedAxis = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OtherSub|OtherMotor" };
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: Sub1, Mot1, SetP=100",
                "Thread1", time, selectedAxis, schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseAxisMon_TooFewParts_NoSchemaAdded()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseAxisMon("AxisMon: OnlyOnePart",
                "Thread1", DateTime.Now, null, schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseAxM_ValidMessage_ParsesData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxM("AxM: SubSys, Motor1, SetP=100, ActP=99",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("AxisMon: SubSys"));
        }

        [Fact]
        public void ParseAxM_LagE_MapsToLagErr()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxM("AxM: Sub1, Mot1, LagE=0.5",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("0.5", dataMatrix[time]["AxisMon: Sub1|Mot1|LagErr"]);
        }

        [Fact]
        public void ParseAxM_NoEquals_SetsTrigger()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseAxM("AxM: Sub1, Mot1, 42",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.Equal("42", dataMatrix[time]["AxisMon: Sub1|Mot1|Trigger"]);
        }

        [Fact]
        public void ParseAxM_FilteredOut_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selectedAxis = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X|Y" };

            svc.ParseAxM("AxM: Sub1, Mot1, SetP=100",
                "Thread1", DateTime.Now, selectedAxis, schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseIOMon_ValidWithValue()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIOMon("IO_Mon: SubSys, Signal1=42.5",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("IO_Mon: SubSys"));
        }

        [Fact]
        public void ParseIOMon_WithNewStatus()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIOMon("IO_Mon: SubSys, Signal1=New Status Op",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseIOMon_FilteredBySelection()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selectedIO = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Other|Sig" };

            svc.ParseIOMon("IO_Mon: SubSys, Signal1=42",
                "Thread1", DateTime.Now, selectedIO, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseIOMon_TooFewParts_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseIOMon("IO_Mon: OnlyOne",
                "Thread1", DateTime.Now, null, schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseIO_ValidWithValue()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: SubSys, Signal1=42.5",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("IO_Mon: SubSys"));
        }

        [Fact]
        public void ParseIO_WithStatusPart()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: SubSys, Signal1=42, Op",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            var row = dataMatrix[time];
            Assert.True(row.ContainsKey("IO_Mon: SubSys|Signal1|eIoStatus"));
        }

        [Fact]
        public void ParseIO_FilteredBySelection()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selectedIO = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Other|Sig" };

            svc.ParseIO("IO: SubSys, Signal1=42",
                "Thread1", DateTime.Now, selectedIO, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseIO_NoEquals_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseIO("IO: SubSys, NoEqualsHere",
                "Thread1", DateTime.Now, null, schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseIO_MotTemp_DecomposesCorrectly()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseIO("IO: SubSys, Pump_MotTemp=75",
                "Thread1", time, null, schema, dataMatrix, threadNameMap);

            Assert.True(schema["IO_Mon: SubSys"].ContainsKey("Pump"));
        }
    }

    // ===================================================================
    // CsvExportService — ParseLogStats (instance method)
    // ===================================================================

    public class CsvExportParseLogStatsInstanceTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        [Fact]
        public void ParseLogStats_WrongThreadName_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseLogStats("LogStat: Logs(Total=100 IsReady=1)", "WrongThread", DateTime.Now,
                schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseLogStats_WrongMessage_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseLogStats("SomeOtherMessage", "LogStats", DateTime.Now,
                schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseLogStats_ValidMessage_AddsAllMetrics()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            svc.ParseLogStats(
                "LogStat: Logs(Total=500 IsReady=3) nSemMissed(total=1 Mult=0) Lost=0 bufFull=0 Max(num=50 cat=Info)",
                "LogStats", time, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("LogStats"));
            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("500", dataMatrix[time]["LogStats|Metrics|Total"]);
            Assert.Equal("3", dataMatrix[time]["LogStats|Metrics|IsReady"]);
        }

        [Fact]
        public void ParseLogStats_NullThreadName_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseLogStats("LogStat: Logs(Total=100 IsReady=1)", null!, DateTime.Now,
                schema, dataMatrix, threadNameMap);

            Assert.Empty(schema);
        }
    }

    // ===================================================================
    // IoTerminalDataService — ExtractDeviceName
    // ===================================================================

    public class IoTerminalExtractDeviceNameTests
    {
        [Theory]
        [InlineData("Io-BIM[0]_e[1]-Mon.csv", "BIM[0]")]
        [InlineData("Io-ECM_e[1]-test.csv", "ECM")]
        [InlineData("Io-PDC.csv", "PDC")]
        [InlineData("SomeOtherFile.csv", "SomeOtherFile")]
        [InlineData("Io-ABC_DEF.csv", "ABC")]
        public void ExtractDeviceName_VariousFormats(string fileName, string expected)
        {
            Assert.Equal(expected, IoTerminalDataService.ExtractDeviceName(fileName));
        }
    }

    // ===================================================================
    // IoTerminalDataService — ParseIoFiles
    // ===================================================================

    public class IoTerminalParseIoFilesTests
    {
        [Fact]
        public void ParseIoFiles_EmptyInput_ReturnsEmptyList()
        {
            var svc = new IoTerminalDataService();
            var result = svc.ParseIoFiles(new Dictionary<string, string>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_NonIoFile_Skipped()
        {
            var svc = new IoTerminalDataService();
            var files = new Dictionary<string, string>
            {
                ["SomeOtherFile.csv"] = "Ticks,rawTime,Timestamp\n1,2,12:00:00"
            };
            var result = svc.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_ValidIoCsv_ParsesCorrectly()
        {
            var svc = new IoTerminalDataService();
            var csvContent = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal1,Signal2\n" +
                             "1,1000000000,12:00:00.000,Printing,100,42,99\n" +
                             "2,2000000000,12:00:01.000,Printing,101,43,100\n";
            var files = new Dictionary<string, string>
            {
                ["Io-BIM[0]_e1.csv"] = csvContent
            };
            var result = svc.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Equal("BIM[0]", result[0].DeviceName);
            Assert.Equal(2, result[0].Columns.Length);
            Assert.Contains("Signal1", result[0].Columns);
            Assert.Contains("Signal2", result[0].Columns);
            Assert.Equal(2, result[0].Rows.Count);
        }

        [Fact]
        public void ParseIoFiles_FromBytes_ParsesCorrectly()
        {
            var svc = new IoTerminalDataService();
            var csvContent = "Ticks,rawTime,Timestamp,Signal1\n1,1000000000,12:00:00.000,42\n";
            var bytes = new Dictionary<string, byte[]>
            {
                ["Io-ECM_e1.csv"] = Encoding.UTF8.GetBytes(csvContent)
            };
            var result = svc.ParseIoFiles(new Dictionary<string, string>(), bytes);
            Assert.Single(result);
            Assert.Equal("ECM", result[0].DeviceName);
        }

        [Fact]
        public void ParseIoFiles_EmptyCsvContent_Skipped()
        {
            var svc = new IoTerminalDataService();
            var files = new Dictionary<string, string>
            {
                ["Io-Empty_e1.csv"] = ""
            };
            var result = svc.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_HeaderOnly_NoRows()
        {
            var svc = new IoTerminalDataService();
            var files = new Dictionary<string, string>
            {
                ["Io-Test_e1.csv"] = "Ticks,rawTime,Timestamp,Signal1\n"
            };
            var result = svc.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Empty(result[0].Rows);
        }

        [Fact]
        public void ParseIoFiles_BytesPreferredOverString()
        {
            var svc = new IoTerminalDataService();
            var csvFromString = "Ticks,rawTime,Timestamp,StringSignal\n1,1000,12:00:00,A\n";
            var csvFromBytes = "Ticks,rawTime,Timestamp,ByteSignal\n1,1000,12:00:00,B\n";
            var strings = new Dictionary<string, string> { ["Io-Dup_e1.csv"] = csvFromString };
            var bytes = new Dictionary<string, byte[]> { ["Io-Dup_e1.csv"] = Encoding.UTF8.GetBytes(csvFromBytes) };

            var result = svc.ParseIoFiles(strings, bytes);
            Assert.Single(result);
            Assert.Contains("ByteSignal", result[0].Columns);
        }

        [Fact]
        public void ParseIoFiles_MachineStatePreserved()
        {
            var svc = new IoTerminalDataService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,Signal1\n1,1000000000,12:00:00,Printing,42\n";
            var files = new Dictionary<string, string> { ["Io-MS_e1.csv"] = csv };
            var result = svc.ParseIoFiles(files);
            Assert.Equal("Printing", result[0].Rows[0].MachineState);
        }
    }

    // ===================================================================
    // IoTerminalDataService — GetAllComponents
    // ===================================================================

    public class IoTerminalGetAllComponentsTests
    {
        [Fact]
        public void GetAllComponents_EmptyDevices_ReturnsEmpty()
        {
            var svc = new IoTerminalDataService();
            var result = svc.GetAllComponents(new List<IoDeviceData>());
            Assert.Empty(result);
        }

        [Fact]
        public void GetAllComponents_MultipleDevices_ReturnsAll()
        {
            var svc = new IoTerminalDataService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData { DeviceName = "BIM", Columns = new[] { "S1", "S2" } },
                new IoDeviceData { DeviceName = "ECM", Columns = new[] { "S3" } }
            };
            var result = svc.GetAllComponents(devices);
            Assert.Equal(3, result.Count);
            Assert.Equal("BIM", result[0].Category);
            Assert.Equal("S1", result[0].Name);
            Assert.False(result[0].IsSelected);
        }
    }

    // ===================================================================
    // IoTerminalDataService — FormatTimestampUs (private static, via reflection)
    // ===================================================================

    public class IoTerminalFormatTimestampTests
    {
        private static string InvokeFormatTimestampUs(long rawTimeNs)
        {
            var method = typeof(IoTerminalDataService).GetMethod("FormatTimestampUs",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (string)method.Invoke(null, new object[] { rawTimeNs })!;
        }

        [Fact]
        public void FormatTimestampUs_Zero_ReturnsEmpty()
        {
            Assert.Equal("", InvokeFormatTimestampUs(0));
        }

        [Fact]
        public void FormatTimestampUs_ValidNs_FormatsCorrectly()
        {
            // 1 second in nanoseconds = 1_000_000_000
            long ns = 1_000_000_000L;
            string result = InvokeFormatTimestampUs(ns);
            Assert.Contains(":", result);
            Assert.Contains(".", result);
        }
    }

    // ===================================================================
    // IoTerminalDataService — SplitCsv (private static, via reflection)
    // ===================================================================

    public class IoTerminalSplitCsvTests
    {
        private static string[] InvokeSplitCsv(string line, int expectedColumns = 0)
        {
            var method = typeof(IoTerminalDataService).GetMethod("SplitCsv",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (string[])method.Invoke(null, new object[] { line, expectedColumns })!;
        }

        [Fact]
        public void SplitCsv_SimpleLine_SplitsCorrectly()
        {
            var parts = InvokeSplitCsv("A,B,C");
            Assert.Equal(3, parts.Length);
            Assert.Equal("A", parts[0]);
            Assert.Equal("B", parts[1]);
            Assert.Equal("C", parts[2]);
        }

        [Fact]
        public void SplitCsv_QuotedField_PreservesComma()
        {
            var parts = InvokeSplitCsv("A,\"B,C\",D");
            Assert.Equal(3, parts.Length);
            Assert.Equal("B,C", parts[1]);
        }

        [Fact]
        public void SplitCsv_SingleField()
        {
            var parts = InvokeSplitCsv("SingleValue");
            Assert.Single(parts);
            Assert.Equal("SingleValue", parts[0]);
        }

        [Fact]
        public void SplitCsv_EmptyFields()
        {
            var parts = InvokeSplitCsv(",,,");
            Assert.Equal(4, parts.Length);
        }

        [Fact]
        public void SplitCsv_WithExpectedColumns()
        {
            var parts = InvokeSplitCsv("A,B,C", 3);
            Assert.Equal(3, parts.Length);
        }
    }

    // ===================================================================
    // IoTerminalDataService — ParseLong / ParseTimestamp (private static)
    // ===================================================================

    public class IoTerminalHelperTests
    {
        private static long InvokeParseLong(string? raw)
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseLong",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (long)method.Invoke(null, new object?[] { raw })!;
        }

        private static DateTime InvokeParseTimestamp(string? raw)
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseTimestamp",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (DateTime)method.Invoke(null, new object?[] { raw })!;
        }

        [Fact]
        public void ParseLong_Null_ReturnsZero()
        {
            Assert.Equal(0, InvokeParseLong(null));
        }

        [Fact]
        public void ParseLong_ValidNumber()
        {
            Assert.Equal(12345L, InvokeParseLong("12345"));
        }

        [Fact]
        public void ParseLong_InvalidString()
        {
            Assert.Equal(0, InvokeParseLong("notanumber"));
        }

        [Fact]
        public void ParseTimestamp_Null_ReturnsMinValue()
        {
            Assert.Equal(DateTime.MinValue, InvokeParseTimestamp(null));
        }

        [Fact]
        public void ParseTimestamp_ValidHHmmss()
        {
            var dt = InvokeParseTimestamp("12:30:45.123");
            Assert.Equal(12, dt.Hour);
            Assert.Equal(30, dt.Minute);
            Assert.Equal(45, dt.Second);
        }

        [Fact]
        public void ParseTimestamp_Whitespace_ReturnsMinValue()
        {
            Assert.Equal(DateTime.MinValue, InvokeParseTimestamp("  "));
        }
    }

    // ===================================================================
    // IoTerminalDataService — ExportMergedCsvAsync
    // ===================================================================

    public class IoTerminalExportMergedCsvTests
    {
        [Fact]
        public async Task ExportMergedCsvAsync_WritesCorrectOutput()
        {
            var svc = new IoTerminalDataService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData
                {
                    DeviceName = "TestDev",
                    Columns = new[] { "S1" },
                    Rows = new List<IoDataRow>
                    {
                        new IoDataRow
                        {
                            RawTime = 1_000_000_000_000L,
                            MachineState = "Printing",
                            Values = new Dictionary<string, string> { ["S1"] = "42" }
                        },
                        new IoDataRow
                        {
                            RawTime = 2_000_000_000_000L,
                            MachineState = "",
                            Values = new Dictionary<string, string> { ["S1"] = "43" }
                        }
                    }
                }
            };

            var selectedKeys = new List<string> { "TestDev|S1" };
            var tempFile = Path.GetTempFileName();
            try
            {
                var progress = new Progress<double>();
                await svc.ExportMergedCsvAsync(devices, selectedKeys, tempFile, progress, CancellationToken.None);

                string content = File.ReadAllText(tempFile);
                Assert.Contains("Timestamp,MachineState", content);
                Assert.Contains("TestDev|S1", content);
                Assert.Contains("42", content);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_ForwardFillsMachineState()
        {
            var svc = new IoTerminalDataService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData
                {
                    DeviceName = "Dev",
                    Columns = new[] { "S" },
                    Rows = new List<IoDataRow>
                    {
                        new IoDataRow { RawTime = 1_000_000_000_000L, MachineState = "Idle", Values = new() { ["S"] = "1" } },
                        new IoDataRow { RawTime = 2_000_000_000_000L, MachineState = "", Values = new() { ["S"] = "2" } },
                    }
                }
            };

            var tempFile = Path.GetTempFileName();
            try
            {
                await svc.ExportMergedCsvAsync(devices, new List<string> { "Dev|S" }, tempFile,
                    new Progress<double>(), CancellationToken.None);

                var lines = File.ReadAllLines(tempFile);
                Assert.Equal(3, lines.Length); // header + 2 rows
                Assert.Contains("Idle", lines[2]); // forward-filled
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_Cancellation_Throws()
        {
            var svc = new IoTerminalDataService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData
                {
                    DeviceName = "Dev",
                    Columns = new[] { "S" },
                    Rows = new List<IoDataRow>
                    {
                        new IoDataRow { RawTime = 1_000_000_000_000L, Values = new() { ["S"] = "1" } }
                    }
                }
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();
            var tempFile = Path.GetTempFileName();
            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    svc.ExportMergedCsvAsync(devices, new List<string> { "Dev|S" }, tempFile,
                        new Progress<double>(), cts.Token));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }

    // ===================================================================
    // ChartDataTransferService — TryParseDoubleFast (private static)
    // ===================================================================

    public class ChartDataTransferTryParseDoubleFastTests
    {
        private static bool InvokeTryParseDoubleFast(string s, int start, int length, out double result)
        {
            var method = typeof(ChartDataTransferService).GetMethod("TryParseDoubleFast",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var args = new object[] { s, start, length, 0.0 };
            bool ret = (bool)method.Invoke(null, args)!;
            result = (double)args[3];
            return ret;
        }

        [Fact]
        public void TryParseDoubleFast_Integer()
        {
            Assert.True(InvokeTryParseDoubleFast("123", 0, 3, out double result));
            Assert.Equal(123.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_Decimal()
        {
            Assert.True(InvokeTryParseDoubleFast("3.14", 0, 4, out double result));
            Assert.Equal(3.14, result, 2);
        }

        [Fact]
        public void TryParseDoubleFast_Negative()
        {
            Assert.True(InvokeTryParseDoubleFast("-42.5", 0, 5, out double result));
            Assert.Equal(-42.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_Positive_Sign()
        {
            Assert.True(InvokeTryParseDoubleFast("+10", 0, 3, out double result));
            Assert.Equal(10.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_Scientific_Fallback()
        {
            Assert.True(InvokeTryParseDoubleFast("1.5e2", 0, 5, out double result));
            Assert.Equal(150.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_ZeroLength_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("123", 0, 0, out _));
        }

        [Fact]
        public void TryParseDoubleFast_OnlySign_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("-", 0, 1, out _));
        }

        [Fact]
        public void TryParseDoubleFast_NonDigit_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("abc", 0, 3, out _));
        }

        [Fact]
        public void TryParseDoubleFast_TrailingGarbage_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("12x", 0, 3, out _));
        }

        [Fact]
        public void TryParseDoubleFast_SubstringParsing()
        {
            Assert.True(InvokeTryParseDoubleFast("xxx42.5yyy", 3, 4, out double result));
            Assert.Equal(42.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_DecimalOnly()
        {
            Assert.True(InvokeTryParseDoubleFast(".5", 0, 2, out double result));
            Assert.Equal(0.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_Zero()
        {
            Assert.True(InvokeTryParseDoubleFast("0", 0, 1, out double result));
            Assert.Equal(0.0, result);
        }
    }

    // ===================================================================
    // ChartDataTransferService — InternString (private static)
    // ===================================================================

    public class ChartDataTransferInternStringTests
    {
        private static string InvokeInternString(Dictionary<string, string> pool, string source, int start, int length)
        {
            var method = typeof(ChartDataTransferService).GetMethod("InternString",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (string)method.Invoke(null, new object[] { pool, source, start, length })!;
        }

        [Fact]
        public void InternString_NewString_AddedToPool()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string result = InvokeInternString(pool, "Hello World", 0, 5);
            Assert.Equal("Hello", result);
            Assert.True(pool.ContainsKey("Hello"));
        }

        [Fact]
        public void InternString_ExistingString_ReusesInstance()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string first = InvokeInternString(pool, "Hello World", 0, 5);
            string second = InvokeInternString(pool, "prefix Hello suffix", 7, 5);
            Assert.Same(first, second);
        }

        [Fact]
        public void InternString_TrimsWhitespace()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string result = InvokeInternString(pool, "  Test  Value  ", 2, 6);
            Assert.Equal("Test", result);
        }
    }

    // ===================================================================
    // ChartDataTransferService — ParseIOSignals (private, via reflection)
    // ===================================================================

    public class ChartDataTransferParseIOSignalsTests
    {
        private static ChartDataTransferService GetInstance()
        {
            return ChartDataTransferService.Instance;
        }

        private static List<SignalData> InvokeParseIOSignals(
            ChartDataTransferService svc,
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var method = typeof(ChartDataTransferService).GetMethod("ParseIOSignals",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (List<SignalData>)method.Invoke(svc, new object?[] { logs, selectedComponents, dataLength, timeIndexLookup, null })!;
        }

        [Fact]
        public void ParseIOSignals_EmptyLogs_ReturnsEmpty()
        {
            var svc = GetInstance();
            var result = InvokeParseIOSignals(svc, new List<LogEntry>(),
                new List<string> { "Sub|Comp" }, 10, new Dictionary<DateTime, int>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIOSignals_NoMatchingMessages_ReturnsEmpty()
        {
            var svc = GetInstance();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "CHStep: some message here" }
            };
            var result = InvokeParseIOSignals(svc, logs,
                new List<string> { "Sub|Comp" }, 10, new Dictionary<DateTime, int>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIOSignals_IOMonMessage_ParsesSignal()
        {
            var svc = GetInstance();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "IO_Mon: SubSys, Signal1=42.5", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseIOSignals(svc, logs,
                new List<string> { "SubSys|Signal1" }, 10, timeIndex);
            Assert.NotEmpty(result);
            Assert.Equal("IO", result[0].Category);
        }

        [Fact]
        public void ParseIOSignals_IOShortMessage_ParsesSignal()
        {
            var svc = GetInstance();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "IO: SubSys, Signal1=42.5", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseIOSignals(svc, logs,
                new List<string> { "SubSys|Signal1" }, 10, timeIndex);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void ParseIOSignals_SkipsNewStatus()
        {
            var svc = GetInstance();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "IO_Mon: SubSys, Signal1=New Status Op", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseIOSignals(svc, logs,
                new List<string> { "SubSys|Signal1" }, 10, timeIndex);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIOSignals_UnselectedSubsystem_Skipped()
        {
            var svc = GetInstance();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "IO_Mon: SubSys, Signal1=42.5", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseIOSignals(svc, logs,
                new List<string> { "OtherSub|Comp" }, 10, timeIndex);
            Assert.Empty(result);
        }
    }

    // ===================================================================
    // ChartDataTransferService — ParseAxisSignals (private, via reflection)
    // ===================================================================

    public class ChartDataTransferParseAxisSignalsTests
    {
        private static List<SignalData> InvokeParseAxisSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var svc = ChartDataTransferService.Instance;
            var method = typeof(ChartDataTransferService).GetMethod("ParseAxisSignals",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (List<SignalData>)method.Invoke(svc, new object?[] { logs, selectedComponents, dataLength, timeIndexLookup, null })!;
        }

        [Fact]
        public void ParseAxisSignals_EmptyLogs_ReturnsEmpty()
        {
            var result = InvokeParseAxisSignals(new List<LogEntry>(),
                new List<string> { "Sub|Motor" }, 10, new Dictionary<DateTime, int>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseAxisSignals_AxisMonMessage_ParsesSignal()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: SubSys, Motor1, SetP=100", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|Motor1" }, 10, timeIndex);
            Assert.NotEmpty(result);
            Assert.Equal("Axis", result[0].Category);
        }

        [Fact]
        public void ParseAxisSignals_AxMMessage_ParsesSignal()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxM: SubSys, Motor1, SetP=100", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|Motor1" }, 10, timeIndex);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void ParseAxisSignals_LagE_MappedToLagErr()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: SubSys, Motor1, LagE=0.5", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|Motor1" }, 10, timeIndex);
            Assert.NotEmpty(result);
            Assert.Contains(result, s => s.Name.Contains("LagErr"));
        }

        [Fact]
        public void ParseAxisSignals_Trg_MappedToTrigger()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: SubSys, Motor1, Trg=5", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|Motor1" }, 10, timeIndex);
            Assert.NotEmpty(result);
            Assert.Contains(result, s => s.Name.Contains("Trigger"));
        }

        [Fact]
        public void ParseAxisSignals_AxM_BareTrigger()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxM: SubSys, Motor1, 42", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|Motor1" }, 10, timeIndex);
            Assert.NotEmpty(result);
            Assert.Contains(result, s => s.Name.Contains("Trigger"));
        }

        [Fact]
        public void ParseAxisSignals_UnselectedMotor_Skipped()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: SubSys, Motor1, SetP=100", Date = time }
            };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSys|OtherMotor" }, 10, new Dictionary<DateTime, int> { [time] = 0 });
            Assert.Empty(result);
        }

        [Fact]
        public void ParseAxisSignals_NoCommaAfterSubsystem_Skipped()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: SubSysNoComma", Date = DateTime.Now }
            };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "SubSysNoComma|X" }, 10, new Dictionary<DateTime, int>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseAxisSignals_MultipleParams_AllParsed()
        {
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "AxisMon: Sub, Mot, SetP=10, ActP=11, SetV=5, ActV=6", Date = time }
            };
            var timeIndex = new Dictionary<DateTime, int> { [time] = 0 };
            var result = InvokeParseAxisSignals(logs,
                new List<string> { "Sub|Mot" }, 10, timeIndex);
            Assert.True(result.Count >= 4);
        }
    }

    // ===================================================================
    // ChartDataService — FindColumnIndex / FindEventsColumnIndex
    // ===================================================================

    public class ChartDataServiceFindColumnTests
    {
        private ChartDataService CreateWithColumns(params string[] columns)
        {
            var svc = new ChartDataService();
            var colNamesProp = typeof(ChartDataService).GetProperty("ColumnNames")!;
            colNamesProp.SetValue(svc, columns.ToList());
            var rawColNamesProp = typeof(ChartDataService).GetProperty("RawColumnNames")!;
            rawColNamesProp.SetValue(svc, columns.ToList());
            return svc;
        }

        [Fact]
        public void FindColumnIndex_ExactMatch_ReturnsIndex()
        {
            var svc = CreateWithColumns("Time", "SetP", "ActP");
            Assert.Equal(1, svc.FindColumnIndex("SetP"));
        }

        [Fact]
        public void FindColumnIndex_CaseInsensitive()
        {
            var svc = CreateWithColumns("Time", "SetP", "ActP");
            Assert.Equal(1, svc.FindColumnIndex("setp"));
        }

        [Fact]
        public void FindColumnIndex_PartialMatch()
        {
            var svc = CreateWithColumns("Time", "Data.OPCUAInterface.SetP", "ActP");
            Assert.Equal(1, svc.FindColumnIndex("SetP"));
        }

        [Fact]
        public void FindColumnIndex_NoMatch_ReturnsMinusOne()
        {
            var svc = CreateWithColumns("Time", "SetP");
            Assert.Equal(-1, svc.FindColumnIndex("NonExistent"));
        }

        [Fact]
        public void FindEventsColumnIndex_ExactMatch()
        {
            var svc = CreateWithColumns("Time", "Events_Message");
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void FindEventsColumnIndex_ContainsMatch()
        {
            var svc = CreateWithColumns("Time", "Data.Events_Message");
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void FindEventsColumnIndex_NoMatch()
        {
            var svc = CreateWithColumns("Time", "SetP", "ActP");
            Assert.Equal(-1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void FindEventsColumnIndex_InRawColumns()
        {
            var svc = new ChartDataService();
            typeof(ChartDataService).GetProperty("ColumnNames")!.SetValue(svc, new List<string> { "Time" });
            typeof(ChartDataService).GetProperty("RawColumnNames")!.SetValue(svc, new List<string> { "Time", "Events_Message" });
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }
    }

    // ===================================================================
    // ChartDataService — Dispose
    // ===================================================================

    public class ChartDataServiceDisposeTests
    {
        [Fact]
        public void Dispose_ClearsState()
        {
            var svc = new ChartDataService();
            typeof(ChartDataService).GetProperty("ColumnNames")!.SetValue(svc, new List<string> { "A", "B" });
            typeof(ChartDataService).GetProperty("RawColumnNames")!.SetValue(svc, new List<string> { "A", "B" });

            svc.Dispose();

            Assert.Empty(svc.ColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.Null(svc.LoadedFilePath);
            Assert.False(svc.IsLoaded);
        }
    }

    // ===================================================================
    // ChartDataService — Constructor defaults
    // ===================================================================

    public class ChartDataServiceConstructorTests
    {
        [Fact]
        public void Constructor_InitializesEmptyCollections()
        {
            var svc = new ChartDataService();
            Assert.NotNull(svc.ColumnNames);
            Assert.Empty(svc.ColumnNames);
            Assert.NotNull(svc.RawColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.Equal(0, svc.TotalRows);
            Assert.Equal(3, svc.DataStartRow);
            Assert.Equal(CsvFormat.Unknown, svc.DetectedFormat);
            Assert.Null(svc.LoadedFilePath);
            Assert.False(svc.IsLoaded);
        }
    }

    // ===================================================================
    // CsvExportService — ParseCHStepExport (private, via reflection)
    // ===================================================================

    public class CsvExportParseCHStepExportTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        private void InvokeParseCHStepExport(CsvExportService svc, string msg, string threadName, DateTime time,
            ExportPreset? preset, HashSet<string>? selectedCHSteps,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            SortedDictionary<DateTime, string> machineStates,
            Dictionary<string, string> threadNameMap)
        {
            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(svc, new object?[] { msg, threadName, time, preset, selectedCHSteps,
                schema, dataMatrix, machineStates, threadNameMap });
        }

        [Fact]
        public void ParseCHStepExport_PlcMngrState_Added()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            InvokeParseCHStepExport(svc,
                "CHStep: PlcMngr, Printing, State 5 <Parent, Sub1, 0, 100, 0, 0>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.True(machineStates.ContainsKey(time));
            Assert.Equal("Printing", machineStates[time]);
        }

        [Fact]
        public void ParseCHStepExport_RegularCHStep_AddsToSchema()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            InvokeParseCHStepExport(svc,
                "CHStep: Motor1, InitStep, State 1 <RootParent, SubSys1, 0, 200, 0, 0>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.NotEmpty(schema);
            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseCHStepExport_ObjType0_MapToAction()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            InvokeParseCHStepExport(svc,
                "CHStep: Motor1, InitStep, State 1 <RootParent, SubSys1, 0, 200, 0, 0>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            var row = dataMatrix[time];
            Assert.True(row.Values.Any(v => v == "action"));
        }

        [Fact]
        public void ParseCHStepExport_ObjType1_MapToComponent()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            InvokeParseCHStepExport(svc,
                "CHStep: Motor1, InitStep, State 1 <RootParent, SubSys1, 0, 200, 0, 1>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            var row = dataMatrix[time];
            Assert.True(row.Values.Any(v => v == "component"));
        }

        [Fact]
        public void ParseCHStepExport_FilteredByCHSteps_NoData()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var selectedCHSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Other|Component" };

            InvokeParseCHStepExport(svc,
                "CHStep: Motor1, InitStep, State 1 <RootParent, SubSys1, 0, 200, 0, 0>",
                "Thread1", DateTime.Now, null, selectedCHSteps, schema, dataMatrix, machineStates, threadNameMap);

            Assert.Empty(schema);
        }

        [Fact]
        public void ParseCHStepExport_IncludeMachineStateFalse_NoMachineState()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var preset = new ExportPreset { IncludeMachineState = false };
            var time = new DateTime(2024, 1, 1, 12, 0, 0);

            InvokeParseCHStepExport(svc,
                "CHStep: PlcMngr, Printing, State 5 <Parent, Sub1, 0, 100, 0, 0>",
                "Thread1", time, preset, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.Empty(machineStates);
        }
    }

    // ===================================================================
    // MergeReloadResults — (private static in LogFileService)
    // ===================================================================

    public class LogFileServiceMergeReloadResultsTests
    {
        private static void InvokeMergeReloadResults(
            LogSessionData session, TabSelectionConfig sel, string componentName,
            System.Collections.Concurrent.ConcurrentBag<List<LogEntry>> logsBag,
            System.Collections.Concurrent.ConcurrentBag<List<LogEntry>> appLogsBag,
            System.Collections.Concurrent.ConcurrentBag<List<EventEntry>> eventsBag,
            List<System.Windows.Media.Imaging.BitmapImage> screenshotsList)
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            method.Invoke(null, new object[] { session, sel, componentName, logsBag, appLogsBag, eventsBag, screenshotsList });
        }

        [Theory]
        [InlineData("App")]
        [InlineData("Plc")]
        [InlineData("Events")]
        [InlineData("Screenshots")]
        [InlineData("TerminalLogs")]
        [InlineData("Configuration")]
        [InlineData("Systab")]
        [InlineData("Globals")]
        [InlineData("Lrs")]
        [InlineData("SetupInfo")]
        [InlineData("ManagerThread")]
        public void MergeReloadResults_SetsTabSelectionFlag(string componentName)
        {
            var session = new LogSessionData();
            session.LoadTabSelection = new TabSelectionConfig();
            var sel = new TabSelectionConfig { LoadPlc = false, LoadApp = false, LoadEvents = false, LoadScreenshots = false };
            var logsBag = new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>();
            var appLogsBag = new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>();
            var eventsBag = new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>();
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            InvokeMergeReloadResults(session, sel, componentName, logsBag, appLogsBag, eventsBag, screenshots);

            var prop = typeof(TabSelectionConfig).GetProperty($"Load{componentName}");
            if (prop != null)
                Assert.True((bool)prop.GetValue(session.LoadTabSelection)!);
        }

        [Fact]
        public void MergeReloadResults_MergesPlcLogs()
        {
            var session = new LogSessionData();
            session.Logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 12, 0, 0), Message = "Existing" }
            };
            var sel = new TabSelectionConfig { LoadPlc = true };
            var logsBag = new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>();
            logsBag.Add(new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 12, 0, 1), Message = "New" }
            });

            InvokeMergeReloadResults(session, sel, "Plc", logsBag,
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>(),
                new List<System.Windows.Media.Imaging.BitmapImage>());

            Assert.Equal(2, session.Logs.Count);
        }

        [Fact]
        public void MergeReloadResults_MergesAppLogs()
        {
            var session = new LogSessionData();
            session.AppDevLogs = new List<LogEntry>();
            var sel = new TabSelectionConfig { LoadApp = true };
            var appLogsBag = new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>();
            appLogsBag.Add(new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 12, 0, 0), Message = "AppLog" }
            });

            InvokeMergeReloadResults(session, sel, "App",
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                appLogsBag,
                new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>(),
                new List<System.Windows.Media.Imaging.BitmapImage>());

            Assert.Single(session.AppDevLogs);
        }

        [Fact]
        public void MergeReloadResults_MergesEvents()
        {
            var session = new LogSessionData();
            session.Events = new List<EventEntry>();
            var sel = new TabSelectionConfig { LoadEvents = true };
            var eventsBag = new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>();
            eventsBag.Add(new List<EventEntry>
            {
                new EventEntry { Time = new DateTime(2024, 1, 1, 12, 0, 0), Name = "Evt1" }
            });

            InvokeMergeReloadResults(session, sel, "Events",
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>());

            Assert.Single(session.Events);
        }

        [Fact]
        public void MergeReloadResults_NullLoadTabSelection_NoError()
        {
            var session = new LogSessionData();
            session.LoadTabSelection = null;
            var sel = new TabSelectionConfig();

            InvokeMergeReloadResults(session, sel, "App",
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>(),
                new List<System.Windows.Media.Imaging.BitmapImage>());
            // Should not throw
        }

        [Fact]
        public void MergeReloadResults_EmptyBags_NoChanges()
        {
            var session = new LogSessionData();
            session.Logs = new List<LogEntry> { new LogEntry { Message = "A" } };
            var sel = new TabSelectionConfig { LoadPlc = true };

            InvokeMergeReloadResults(session, sel, "Plc",
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<LogEntry>>(),
                new System.Collections.Concurrent.ConcurrentBag<List<EventEntry>>(),
                new List<System.Windows.Media.Imaging.BitmapImage>());

            Assert.Single(session.Logs);
        }
    }

    // ===================================================================
    // IoDeviceData / IoDataRow model tests
    // ===================================================================

    public class IoDataModelTests
    {
        [Fact]
        public void IoDeviceData_Defaults()
        {
            var d = new IoDeviceData();
            Assert.Equal("", d.DeviceName);
            Assert.Equal("", d.FileName);
            Assert.Empty(d.Columns);
            Assert.NotNull(d.Rows);
            Assert.Empty(d.Rows);
        }

        [Fact]
        public void IoDataRow_Defaults()
        {
            var r = new IoDataRow();
            Assert.Equal(0L, r.RawTime);
            Assert.Equal(DateTime.MinValue, r.Timestamp);
            Assert.Equal("", r.MachineState);
            Assert.NotNull(r.Values);
        }
    }

    // ===================================================================
    // CsvFormat enum
    // ===================================================================

    public class CsvFormatEnumTests
    {
        [Fact]
        public void CsvFormat_HasExpectedValues()
        {
            Assert.Equal(0, (int)CsvFormat.Unknown);
            Assert.Equal(1, (int)CsvFormat.PlcIos);
            Assert.Equal(2, (int)CsvFormat.YTScope);
            Assert.Equal(3, (int)CsvFormat.Legacy);
        }
    }
}
