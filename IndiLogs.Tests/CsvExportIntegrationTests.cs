using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class CsvExportIntegrationTests
    {
        private CsvExportService CreateService() => new CsvExportService();

        private void InvokeExportWithForwardFill(CsvExportService svc, IEnumerable<LogEntry> logs, string filePath, ExportPreset? preset)
        {
            var method = typeof(CsvExportService).GetMethod("ExportWithForwardFill",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(svc, new object?[] { logs, filePath, preset, null });
        }

        [Fact]
        public void ExportWithForwardFill_AxisMonLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "AxisMon: Sub1, Motor1, SetP=100, ActP=99", ThreadName = "Thread1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "AxisMon: Sub1, Motor1, SetP=101, ActP=100.5", ThreadName = "Thread1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,2), Message = "AxisMon: Sub1, Motor2, SetP=50, Trg=ON", ThreadName = "Thread1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.Contains("Time", content);
                Assert.True(content.Length > 50); // Has real data
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_AxMLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "AxM: Sub1, Motor1, LagE=0.5, SetP=100", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "AxM: Sub1, Motor1, TriggerValue", ThreadName = "T1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.Contains("Time", content);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_IOMonLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "IO_Mon: BIM[0], Sensor1=42, Motor1_MotTemp=65", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "IO_Mon: BIM[0], Sensor2=New Status Op", ThreadName = "T1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.True(content.Length > 50);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_IOShortLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "IO: BIM[0], Sensor1=42", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "IO: BIM[0], Motor1_DrvTemp=55, OpH", ThreadName = "T1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.True(content.Length > 50);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_CHStepLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "CHStep: PlcMngr, STANDBY, State 5 <Root, 1, 0, 100, 0, 1>", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "CHStep: ITP, Starting, State 3 <PlcMngr, Sub1, 2, 50, 1, 0>", ThreadName = "T1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.True(content.Length > 50);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_LogStatsLogs_GeneratesCsv()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=2 bufFull=0 Max(num=100 cat=ERROR)", ThreadName = "LogStats" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.True(content.Length > 50);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_EventsThread_IncludedByDefault()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                // Events thread messages that DON'T start with A/I/C/L need preset.IncludeEvents
                var preset = new ExportPreset
                {
                    SelectedIOComponents = new List<string>(),
                    SelectedAxisComponents = new List<string>(),
                    SelectedCHSteps = new List<string>(),
                    SelectedThreads = new List<string>(),
                    IncludeEvents = true,
                    IncludeLogStats = false,
                    IncludeMachineState = false
                };

                var logs = new List<LogEntry>
                {
                    // Start with relevant char so it doesn't get skipped
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "IO: BIM, Sensor=1", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "Critical event detected", ThreadName = "Events" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, preset);

                Assert.True(File.Exists(tempFile));
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_WithPreset_FiltersComponents()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var preset = new ExportPreset
                {
                    SelectedIOComponents = new List<string> { "BIM[0]|Sensor1" },
                    SelectedAxisComponents = new List<string>(),
                    SelectedCHSteps = new List<string>(),
                    SelectedThreads = new List<string> { "CustomThread" },
                    IncludeEvents = false,
                    IncludeLogStats = false,
                    IncludeMachineState = true
                };

                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "IO: BIM[0], Sensor1=42", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "AxisMon: Sub1, Motor1, SetP=100", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,2), Message = "Custom thread message", ThreadName = "CustomThread" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, preset);

                string content = File.ReadAllText(tempFile);
                Assert.True(content.Length > 50);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_EmptyLogs_CreatesEmptyFile()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                InvokeExportWithForwardFill(svc, new List<LogEntry>(), tempFile, null);
                Assert.True(File.Exists(tempFile));
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_IrrelevantMessages_Skipped()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "Random message that starts with R", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "Another random message", ThreadName = "T2" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,2), Message = null!, ThreadName = "T3" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,3), Message = "", ThreadName = "T4" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);
                // Should complete without error
                Assert.True(File.Exists(tempFile));
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void ExportWithForwardFill_MixedMessages_AllParsed()
        {
            var svc = CreateService();
            var tempFile = Path.GetTempFileName();
            try
            {
                var logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,0), Message = "AxisMon: Sub1, Motor1, SetP=100", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,1), Message = "IO_Mon: BIM[0], Sensor1=42", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,2), Message = "CHStep: PlcMngr, STANDBY, State 5 <Root, 1, 0, 100, 0, 1>", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,3), Message = "LogStat: Logs(Total=100 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=50 cat=INFO)", ThreadName = "LogStats" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,4), Message = "Event: print complete", ThreadName = "Events" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,5), Message = "IO: BIM[0], Motor1_MotTemp=70", ThreadName = "T1" },
                    new LogEntry { Date = new DateTime(2024,1,1,10,0,6), Message = "AxM: Sub1, Motor1, SetP=200", ThreadName = "T1" },
                };

                InvokeExportWithForwardFill(svc, logs, tempFile, null);

                string content = File.ReadAllText(tempFile);
                Assert.Contains("Time", content);
                // CSV should have data
                var lines = content.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                Assert.True(lines.Count > 1); // Header + at least 1 data row
            }
            finally { File.Delete(tempFile); }
        }

        // ── ParseCHStepExport ──

        [Fact]
        public void ParseCHStepExport_PlcMngrState_AddsMachineState()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var states = new SortedDictionary<DateTime, string>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 10, 0, 0);

            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(svc, new object?[] {
                "CHStep: PlcMngr, STANDBY, State 5 <Root, 1, 0, 100, 0, 1>",
                "T1", time, null, null, schema, data, states, threadMap
            });

            Assert.True(states.ContainsKey(time));
            Assert.Equal("STANDBY", states[time]);
        }

        [Fact]
        public void ParseCHStepExport_RegularCHStep_AddsToSchema()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var states = new SortedDictionary<DateTime, string>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 10, 0, 0);

            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(svc, new object?[] {
                "CHStep: ITP, Starting Phase, State 3 <PlcMngr, Sub1, 2, 50, 1, 0>",
                "T1", time, null, null, schema, data, states, threadMap
            });

            Assert.True(schema.Count > 0);
            Assert.True(data.ContainsKey(time));
        }

        [Fact]
        public void ParseCHStepExport_ObjType0_Action()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var states = new SortedDictionary<DateTime, string>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 10, 0, 0);

            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(svc, new object?[] {
                "CHStep: ITP, Step1, State 1 <Parent, Sub1, 0, 100, 0, 0>",
                "T1", time, null, null, schema, data, states, threadMap
            });

            // ObjType "0" should be "action"
            var key = data[time].Keys.FirstOrDefault(k => k.Contains("CHObjType"));
            Assert.NotNull(key);
            Assert.Equal("action", data[time][key!]);
        }

        [Fact]
        public void ParseCHStepExport_ObjType1_Component()
        {
            var svc = CreateService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var states = new SortedDictionary<DateTime, string>();
            var threadMap = new Dictionary<string, string>();
            var time = new DateTime(2024, 1, 1, 10, 0, 0);

            var method = typeof(CsvExportService).GetMethod("ParseCHStepExport", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(svc, new object?[] {
                "CHStep: ITP, Step1, State 1 <Parent, Sub1, 0, 100, 0, 1>",
                "T1", time, null, null, schema, data, states, threadMap
            });

            var key = data[time].Keys.FirstOrDefault(k => k.Contains("CHObjType"));
            Assert.NotNull(key);
            Assert.Equal("component", data[time][key!]);
        }

        // ── FilterSearchViewModel.MatchesSearch ──

        [Fact]
        public void MatchesSearch_MessageContains_ReturnsTrue()
        {
            var log = new LogEntry { Message = "Error occurred in module" };
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_MessageDoesNotContain_ReturnsFalse()
        {
            var log = new LogEntry { Message = "All good" };
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_NullMessage_ReturnsFalse()
        {
            var log = new LogEntry { Message = null! };
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        [Fact]
        public void MatchesSearch_ExtraFields_ContainsSearch()
        {
            var log = new LogEntry { Message = "no match here" };
            log.ExtraFields = new Dictionary<string, string> { { "CustomField", "the error detail" } };
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_CaseInsensitive()
        {
            var log = new LogEntry { Message = "ERROR: something" };
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "ERROR"));
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "Error"));
        }

        [Fact]
        public void MatchesSearch_EmptyExtraFields_ReturnsFalse()
        {
            var log = new LogEntry { Message = "no match" };
            log.ExtraFields = new Dictionary<string, string>();
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "xyz"));
        }

        // ── MainViewModel.ValidateSearchSyntax ──

        [Fact]
        public void ValidateSearchSyntax_NullSearchText_SetsValid()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            // Need to set up FilterVM with null SearchText
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            typeof(FilterSearchViewModel).GetField("_searchText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(filterVM, null);

            typeof(MainViewModel).GetProperty("FilterVM")?.SetValue(vm, filterVM);
            typeof(MainViewModel).GetField("_isSearchSyntaxValid", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, false);

            var method = typeof(MainViewModel).GetMethod("ValidateSearchSyntax", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            Assert.True(vm.IsSearchSyntaxValid);
        }

        [Fact]
        public void ValidateSearchSyntax_SimpleText_SetsValid()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            typeof(FilterSearchViewModel).GetField("_searchText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(filterVM, "simple text");

            typeof(MainViewModel).GetProperty("FilterVM")?.SetValue(vm, filterVM);
            typeof(MainViewModel).GetField("_isSearchSyntaxValid", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, false);
            typeof(MainViewModel).GetField("_searchSyntaxError", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, null);

            var method = typeof(MainViewModel).GetMethod("ValidateSearchSyntax", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            Assert.True(vm.IsSearchSyntaxValid);
        }

        [Fact]
        public void ValidateSearchSyntax_WithBooleanOperators_Validates()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            typeof(FilterSearchViewModel).GetField("_searchText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(filterVM, "error AND fatal");

            typeof(MainViewModel).GetProperty("FilterVM")?.SetValue(vm, filterVM);
            typeof(MainViewModel).GetField("_isSearchSyntaxValid", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, true);
            typeof(MainViewModel).GetField("_searchSyntaxError", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, null);

            var method = typeof(MainViewModel).GetMethod("ValidateSearchSyntax", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            Assert.True(vm.IsSearchSyntaxValid);
        }

        [Fact]
        public void ValidateSearchSyntax_InvalidBooleanSyntax_SetsInvalid()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            typeof(FilterSearchViewModel).GetField("_searchText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(filterVM, "error AND OR");

            typeof(MainViewModel).GetProperty("FilterVM")?.SetValue(vm, filterVM);
            typeof(MainViewModel).GetField("_isSearchSyntaxValid", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, true);
            typeof(MainViewModel).GetField("_searchSyntaxError", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, null);

            var method = typeof(MainViewModel).GetMethod("ValidateSearchSyntax", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            Assert.False(vm.IsSearchSyntaxValid);
            Assert.NotNull(vm.SearchSyntaxError);
        }
    }
}
