using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace IndiLogs.Tests
{
    public class CsvExportServiceExtendedTests : IDisposable
    {
        private readonly CsvExportService _service = new();
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                try { File.Delete(f); } catch { }
        }

        private string CreateTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.csv");
            _tempFiles.Add(path);
            return path;
        }

        private void InvokeExportWithForwardFill(IEnumerable<LogEntry> logs, string filePath,
            ExportPreset? preset, CsvExportService.IProgress? progress = null)
        {
            var method = typeof(CsvExportService).GetMethod("ExportWithForwardFill",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            try
            {
                method!.Invoke(_service, new object?[] { logs, filePath, preset, progress });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private void InvokeParseCHStepExport(string msg, string threadName, DateTime time,
            ExportPreset? preset, HashSet<string>? selectedCHSteps,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            SortedDictionary<DateTime, string> machineStates,
            Dictionary<string, string> threadNameMap)
        {
            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(_service, new object?[] { msg, threadName, time, preset, selectedCHSteps,
                schema, dataMatrix, machineStates, threadNameMap });
        }

        private List<LogEntry> MakeLogEntries(params (DateTime time, string threadName, string message)[] entries)
        {
            return entries.Select(e => new LogEntry
            {
                Date = e.time,
                ThreadName = e.threadName,
                Message = e.message
            }).ToList();
        }

        private string[] ReadCsvLines(string filePath)
        {
            return File.ReadAllLines(filePath);
        }

        // ===================================================================
        // ExportWithForwardFill - basic output structure
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_AxisMonLogs_ProducesCsvWithHeaderAndData()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = MakeLogEntries(
                (t1, "Thread1", "AxisMon: Sub1, Motor1, SetP=100, ActP=200"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.True(lines.Length >= 2);
            Assert.StartsWith("Time", lines[0]);
            Assert.Contains("Machine_State", lines[0]);
            Assert.Contains("Sub1-Motor1-SetP", lines[0]);
            Assert.Contains("100", lines[1]);
            Assert.Contains("200", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_EmptyLogs_ProducesNoFile()
        {
            var path = CreateTempFile();
            var logs = new List<LogEntry>();

            InvokeExportWithForwardFill(logs, path, null);

            // File should not exist or be empty since no data was found
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void ExportWithForwardFill_NullMessages_SkipsGracefully()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "T1", Message = null! },
                new LogEntry { Date = t1.AddSeconds(1), ThreadName = "T1", Message = "" },
                new LogEntry { Date = t1.AddSeconds(2), ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=50" }
            };

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.True(lines.Length >= 2);
            Assert.Contains("50", lines[1]);
        }

        // ===================================================================
        // Forward-fill behavior
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_ForwardFillCarriesLastValue()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);
            var t3 = t1.AddSeconds(2);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"),
                (t2, "T1", "AxisMon: Sub1, Motor1, ActP=200"),
                (t3, "T1", "AxisMon: Sub1, Motor1, SetP=300"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            // 3 data rows + header
            Assert.Equal(4, lines.Length);

            // Row 2 (t2): SetP should still be 100 (forward-filled), ActP is 200
            Assert.Contains("100", lines[2]);
            Assert.Contains("200", lines[2]);

            // Row 3 (t3): SetP updated to 300, ActP should still be 200 (forward-filled)
            Assert.Contains("300", lines[3]);
            Assert.Contains("200", lines[3]);
        }

        [Fact]
        public void ExportWithForwardFill_MachineStateForwardFilled()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);
            var t3 = t1.AddSeconds(2);

            var logs = MakeLogEntries(
                (t1, "T1", "CHStep: PlcMngr, Printing, State 5 <Parent, 1, 0, 100, 0, 0>"),
                (t2, "T1", "AxisMon: Sub1, Motor1, SetP=50"),
                (t3, "T1", "CHStep: PlcMngr, Idle, State 2 <Parent, 1, 0, 200, 0, 0>"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            // t1 has machine state "Printing", t2 should carry it forward, t3 changes to "Idle"
            Assert.Contains("Printing", lines[1]);
            Assert.Contains("Printing", lines[2]); // forward-filled
            Assert.Contains("Idle", lines[3]);
        }

        // ===================================================================
        // Preset: IncludeUnixTime
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_IncludeUnixTime_AddsUnixTimeColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var preset = new ExportPreset
            {
                IncludeUnixTime = true,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Unix_Time", lines[0]);
            // Verify unix time value is present in the data row
            long expectedUnix = ((DateTimeOffset)t1).ToUnixTimeMilliseconds();
            Assert.Contains(expectedUnix.ToString(), lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_NoUnixTime_OmitsColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                IncludeUnixTime = false,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.DoesNotContain("Unix_Time", lines[0]);
        }

        // ===================================================================
        // Preset: IncludeMachineState
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_IncludeMachineState_AddsMachineStateColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                IncludeMachineState = true,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Machine_State", lines[0]);
        }

        [Fact]
        public void ExportWithForwardFill_ExcludeMachineState_OmitsColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                IncludeMachineState = false,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.DoesNotContain("Machine_State", lines[0]);
        }

        // ===================================================================
        // Preset: IncludeEvents
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_IncludeEvents_CollectsEventMessages()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);
            var preset = new ExportPreset
            {
                IncludeEvents = true,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "Events", Message = "System started" },
                new LogEntry { Date = t2, ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=100" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Events_Message", lines[0]);
            Assert.Contains("System started", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_ExcludeEvents_OmitsEventsColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                IncludeEvents = false,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "Events", Message = "System started" },
                new LogEntry { Date = t1.AddSeconds(1), ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=100" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.DoesNotContain("Events_Message", lines[0]);
        }

        // ===================================================================
        // Preset: IncludeLogStats
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_IncludeLogStats_ParsesLogStatsColumns()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            // Note: null preset defaults to including LogStats
            var logs = MakeLogEntries(
                (t1, "LogStats", "LogStat: Logs(Total=5000 IsReady=1) Lost=0 bufFull=0"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Total", lines[0]);
            Assert.Contains("5000", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_ExcludeLogStats_OmitsLogStatsColumns()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset { IncludeLogStats = false };

            var logs = MakeLogEntries(
                (t1, "LogStats", "LogStat: Logs(Total=5000 IsReady=1) Lost=0 bufFull=0"));

            InvokeExportWithForwardFill(logs, path, preset);

            // With LogStats excluded and no other data, no file should be produced
            Assert.False(File.Exists(path));
        }

        // ===================================================================
        // Thread message collection
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_SelectedThreads_CollectsThreadMessages()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                SelectedThreads = new List<string> { "MyThread" },
                IncludeEvents = false,
                IncludeMachineState = false
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "MyThread", Message = "Custom thread data" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("MyThread_Message", lines[0]);
            Assert.Contains("Custom thread data", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_MultipleSelectedThreads_EachGetsColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset
            {
                SelectedThreads = new List<string> { "ThreadA", "ThreadB" },
                IncludeEvents = false,
                IncludeMachineState = false
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "ThreadA", Message = "Data A" },
                new LogEntry { Date = t1, ThreadName = "ThreadB", Message = "Data B" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("ThreadA_Message", lines[0]);
            Assert.Contains("ThreadB_Message", lines[0]);
        }

        // ===================================================================
        // ParseCHStepExport
        // ===================================================================

        [Fact]
        public void ParseCHStepExport_ValidCHStep_PopulatesSchemaAndData()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            InvokeParseCHStepExport(
                "CHStep: CompName, Step message text, State 42 <ParentComp, 7, 3, 150, 1, 0>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.NotEmpty(schema);
            Assert.NotEmpty(dataMatrix);

            var row = dataMatrix[time];
            // Verify CHStep data fields are populated
            Assert.Contains(row.Values, v => v == "Step message text");
            Assert.Contains(row.Values, v => v == "42");
        }

        [Fact]
        public void ParseCHStepExport_PlcMngrMessage_PopulatesMachineState()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            InvokeParseCHStepExport(
                "CHStep: PlcMngr, Printing, State 5 <Parent, 1, 0, 100, 0, 0>",
                "Thread1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.Single(machineStates);
            Assert.Equal("Printing", machineStates[time]);
        }

        [Fact]
        public void ParseCHStepExport_WithPresetExcludeMachineState_SkipsMachineState()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset { IncludeMachineState = false };

            InvokeParseCHStepExport(
                "CHStep: PlcMngr, Printing, State 5 <Parent, 1, 0, 100, 0, 0>",
                "Thread1", time, preset, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.Empty(machineStates);
        }

        [Fact]
        public void ParseCHStepExport_SelectedCHSteps_FiltersComponents()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);
            var selectedCHSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ParentComp|CompName" };

            InvokeParseCHStepExport(
                "CHStep: CompName, Step message text, State 42 <ParentComp, 7, 3, 150, 1, 0>",
                "Thread1", time, null, selectedCHSteps, schema, dataMatrix, machineStates, threadNameMap);

            Assert.NotEmpty(dataMatrix);
        }

        [Fact]
        public void ParseCHStepExport_SelectedCHSteps_ExcludesNonMatching()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);
            var selectedCHSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Other|Other" };

            InvokeParseCHStepExport(
                "CHStep: CompName, Step message text, State 42 <ParentComp, 7, 3, 150, 1, 0>",
                "Thread1", time, null, selectedCHSteps, schema, dataMatrix, machineStates, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseCHStepExport_ObjType0_MapsToAction()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            InvokeParseCHStepExport(
                "CHStep: Comp, Step, State 1 <Par, 1, 0, 100, 0, 0>",
                "T1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            var row = dataMatrix[time];
            Assert.Contains(row.Values, v => v == "action");
        }

        [Fact]
        public void ParseCHStepExport_ObjType1_MapsToComponent()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            InvokeParseCHStepExport(
                "CHStep: Comp, Step, State 1 <Par, 1, 0, 100, 0, 1>",
                "T1", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            var row = dataMatrix[time];
            Assert.Contains(row.Values, v => v == "component");
        }

        [Fact]
        public void ParseCHStepExport_SetsThreadNameMap()
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            InvokeParseCHStepExport(
                "CHStep: Comp, Step, State 1 <Par, 2, 0, 100, 0, 0>",
                "MyThread", time, null, null, schema, dataMatrix, machineStates, threadNameMap);

            Assert.All(threadNameMap.Values, v => Assert.Equal("MyThread", v));
            Assert.NotEmpty(threadNameMap);
        }

        // ===================================================================
        // CHStep via ExportWithForwardFill
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_CHStepMessages_ProduceCHStepColumns()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "CHStep: Motor, Running, State 10 <Subsystem, 2, 5, 200, 0, 1>"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("StepMessage", lines[0]);
            Assert.Contains("Running", lines[1]);
            Assert.Contains("component", lines[1]); // ObjType=1
        }

        // ===================================================================
        // Progress reporting
        // ===================================================================

        private class TestProgress : CsvExportService.IProgress
        {
            public List<(int pct, string status, string details)> Reports { get; } = new();
            public bool IsCancelled { get; set; }

            public void Report(int percentage, string status, string details = "")
            {
                Reports.Add((percentage, status, details));
            }
        }

        [Fact]
        public void ExportWithForwardFill_ProgressReporting_ReportsInitAndCompletion()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var progress = new TestProgress();

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, null, progress);

            Assert.True(progress.Reports.Count > 0);
            Assert.Contains(progress.Reports, r => r.pct == 0);
            Assert.Contains(progress.Reports, r => r.pct == 100);
        }

        [Fact]
        public void ExportWithForwardFill_Cancellation_ThrowsOperationCancelled()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var progress = new TestProgress { IsCancelled = true };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            Assert.Throws<OperationCanceledException>(() =>
                InvokeExportWithForwardFill(logs, path, null, progress));
        }

        [Fact]
        public void ExportWithForwardFill_NoData_ReportsNoDataFound()
        {
            var path = CreateTempFile();
            var progress = new TestProgress();

            // Logs that won't produce any parseable data
            var logs = MakeLogEntries(
                (new DateTime(2025, 1, 1, 10, 0, 0), "T1", "Some random message"));

            InvokeExportWithForwardFill(logs, path, null, progress);

            Assert.Contains(progress.Reports, r => r.status.Contains("No data"));
        }

        // ===================================================================
        // Mixed message types
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_MixedAxisMonAndIO_ProducesBothColumnSets()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"),
                (t2, "T1", "IO_Mon: SubSys1, Sensor1=42.5 extra"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Sub1-Motor1-SetP", lines[0]);
            Assert.Contains("SubSys1-Sensor1-Value", lines[0]);
        }

        [Fact]
        public void ExportWithForwardFill_MixedAxisMonIOAndCHStep_AllColumnsPresent()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);
            var t3 = t1.AddSeconds(2);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"),
                (t2, "T1", "IO: Sub2, Signal1=50"),
                (t3, "T1", "CHStep: Comp, Running, State 5 <Parent, 1, 0, 100, 0, 0>"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Sub1-Motor1-SetP", lines[0]);
            Assert.Contains("Sub2-Signal1-Value", lines[0]);
            Assert.Contains("StepMessage", lines[0]);
        }

        // ===================================================================
        // WriteForwardFilledCsv - CSV escaping
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_ValuesWithQuotes_AreEscaped()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            // LogStats warning values get "[!] " prefix which doesn't contain commas,
            // but thread messages with quotes will be escaped
            var preset = new ExportPreset
            {
                IncludeEvents = true,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=100" },
                new LogEntry { Date = t1.AddSeconds(1), ThreadName = "Events", Message = "Value is \"test\"" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var rawContent = File.ReadAllText(path);
            // Quotes in events should be doubled for CSV escaping
            Assert.Contains("\"\"test\"\"", rawContent);
        }

        // ===================================================================
        // LogStats warning markers
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_LogStatsNonZeroLost_MarkedWithWarning()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "LogStats", "LogStat: Logs(Total=1000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=5 bufFull=0 Max(num=50 cat=Info)"));

            InvokeExportWithForwardFill(logs, path, null);

            var rawContent = File.ReadAllText(path);
            Assert.Contains("[!] 5", rawContent);
        }

        [Fact]
        public void ExportWithForwardFill_LogStatsZeroLost_NoWarningMarker()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "LogStats", "LogStat: Logs(Total=1000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=50 cat=Info)"));

            InvokeExportWithForwardFill(logs, path, null);

            var rawContent = File.ReadAllText(path);
            Assert.DoesNotContain("[!]", rawContent);
        }

        // ===================================================================
        // Preset with all options combined
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_AllPresetOptionsEnabled_ProducesAllColumns()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var t2 = t1.AddSeconds(1);
            var t3 = t1.AddSeconds(2);

            var preset = new ExportPreset
            {
                IncludeUnixTime = true,
                IncludeEvents = true,
                IncludeMachineState = true,
                IncludeLogStats = true,
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=100" },
                new LogEntry { Date = t2, ThreadName = "Events", Message = "Event occurred" },
                new LogEntry { Date = t3, ThreadName = "LogStats", Message = "LogStat: Logs(Total=500 IsReady=1) Lost=0 bufFull=0" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Unix_Time", lines[0]);
            Assert.Contains("Machine_State", lines[0]);
            Assert.Contains("Events_Message", lines[0]);
            Assert.Contains("Total", lines[0]);
        }

        // ===================================================================
        // Edge cases
        // ===================================================================

        [Fact]
        public void ExportWithForwardFill_SingleLogEntry_ProducesOneDataRow()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=42"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Equal(2, lines.Length); // header + 1 data row
        }

        [Fact]
        public void ExportWithForwardFill_SameTimestamp_MergesIntoOneRow()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"),
                (t1, "T1", "AxisMon: Sub1, Motor1, ActP=200"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Equal(2, lines.Length); // header + 1 merged data row
            Assert.Contains("100", lines[1]);
            Assert.Contains("200", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_EventsOnlyWithPreset_WritesEventColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var preset = new ExportPreset { IncludeEvents = true };

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "Events", Message = "System event occurred" }
            };

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Events_Message", lines[0]);
            Assert.Contains("System event occurred", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_AxMFormat_ParsedSameAsAxisMon()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "AxM: Sub1, Motor1, SetP=77, ActP=88"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Sub1-Motor1-SetP", lines[0]);
            Assert.Contains("77", lines[1]);
            Assert.Contains("88", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_IOFormat_ParsedAsIOMon()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "IO: Sub1, Signal1=100, OpH"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Sub1-Signal1-Value", lines[0]);
            Assert.Contains("100", lines[1]);
            Assert.Contains("OperationalHigh", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_OnlyNonParsableMessages_NoOutput()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            // Use null preset; random messages won't match any parser pattern
            var logs = MakeLogEntries(
                (t1, "T1", "Random log message that doesn't match any pattern"));

            InvokeExportWithForwardFill(logs, path, null);

            // Events thread message would still be collected with null preset,
            // but "T1" thread is not "Events" thread, so nothing is collected
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void ExportWithForwardFill_TimeColumnFormat_IsCorrect()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 3, 7, 14, 30, 45, 123);
            t1 = t1.AddTicks(4560); // add 456 microseconds

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=10"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.StartsWith("2025-03-07 14:30:45.", lines[1]);
        }

        [Fact]
        public void ExportWithForwardFill_NullPreset_DefaultsToIncludeMachineState()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Contains("Machine_State", lines[0]);
        }

        [Fact]
        public void ExportWithForwardFill_NullPreset_EventsCollectedButNoExplicitColumn()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = t1, ThreadName = "Events", Message = "Auto-included event" },
                new LogEntry { Date = t1.AddSeconds(1), ThreadName = "T1", Message = "AxisMon: Sub1, Motor1, SetP=100" }
            };

            // null preset: events are collected into threadMessages but no explicit Events_Message
            // column is added (that requires preset.IncludeEvents = true).
            // However, the event timestamp creates a row in the time series.
            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            // Two timestamps means two data rows
            Assert.Equal(3, lines.Length); // header + 2 data rows
            // No Events_Message column with null preset
            Assert.DoesNotContain("Events_Message", lines[0]);
        }

        [Fact]
        public void ExportWithForwardFill_LargeNumberOfRows_AllWritten()
        {
            var path = CreateTempFile();
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = Enumerable.Range(0, 500).Select(i => new LogEntry
            {
                Date = baseTime.AddSeconds(i),
                ThreadName = "T1",
                Message = $"AxisMon: Sub1, Motor1, SetP={i}"
            }).ToList();

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            Assert.Equal(501, lines.Length); // header + 500 data rows
        }

        [Fact]
        public void ExportWithForwardFill_SelectedAxisComponents_FiltersOutput()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);

            var preset = new ExportPreset
            {
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" }
            };

            var logs = MakeLogEntries(
                (t1, "T1", "AxisMon: Sub1, Motor1, SetP=100"),
                (t2, "T1", "AxisMon: Sub1, Motor2, SetP=200"));

            InvokeExportWithForwardFill(logs, path, preset);

            var lines = ReadCsvLines(path);
            Assert.Contains("Motor1", lines[0]);
            Assert.DoesNotContain("Motor2", lines[0]);
        }

        [Fact]
        public void ExportWithForwardFill_SelectedIOComponents_FiltersOutput()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = t1.AddSeconds(1);

            // null preset means no IO filtering (selectedIO is null)
            var logs = MakeLogEntries(
                (t1, "T1", "IO_Mon: SubSys1, Sensor1=42.5 extra"),
                (t2, "T1", "IO_Mon: SubSys1, Sensor2=99.9 extra"));

            // First verify both show up with null preset
            InvokeExportWithForwardFill(logs, path, null);
            var lines = ReadCsvLines(path);
            Assert.Contains("Sensor1", lines[0]);
            Assert.Contains("Sensor2", lines[0]);

            // Now with preset filtering to only Sensor1
            var path2 = CreateTempFile();
            var preset = new ExportPreset
            {
                SelectedIOComponents = new List<string> { "SubSys1|Sensor1" }
            };

            InvokeExportWithForwardFill(logs, path2, preset);

            var lines2 = ReadCsvLines(path2);
            Assert.Contains("Sensor1", lines2[0]);
            Assert.DoesNotContain("Sensor2", lines2[0]);
        }

        [Fact]
        public void ExportWithForwardFill_ThreadHeaderIncludesThreadName()
        {
            var path = CreateTempFile();
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            var logs = MakeLogEntries(
                (t1, "MyThread", "AxisMon: Sub1, Motor1, SetP=100"));

            InvokeExportWithForwardFill(logs, path, null);

            var lines = ReadCsvLines(path);
            // Thread name should be in bracket notation in the header
            Assert.Contains("[MyThread]", lines[0]);
        }
    }
}
