using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Cpr;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using Xunit;

namespace IndiLogs.Tests
{
    // ════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════

    file static class VmHelper
    {
        public static LogEntry MakeLog(string level = "Info", string msg = "test",
            string thread = "T1", string logger = "com.hp.indigo.Service",
            string method = "Run", string pattern = "", DateTime? date = null)
        {
            return new LogEntry
            {
                Level = level,
                Message = msg,
                ThreadName = thread,
                Logger = logger,
                Method = method,
                Pattern = pattern,
                Date = date ?? new DateTime(2025, 6, 1, 12, 0, 0)
            };
        }

        public static void SetField(object obj, string fieldName, object? value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null) { fi.SetValue(obj, value); return; }
                t = t.BaseType;
            }
            throw new MissingFieldException($"Field '{fieldName}' not found on {obj.GetType().Name}");
        }

        public static object? GetField(object obj, string fieldName)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null) return fi.GetValue(obj);
                t = t.BaseType;
            }
            throw new MissingFieldException($"Field '{fieldName}' not found on {obj.GetType().Name}");
        }

        public static object? InvokePrivate(object obj, string methodName, params object?[] args)
        {
            var mi = obj.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi == null) throw new MissingMethodException($"Method '{methodName}' not found");
            return mi.Invoke(obj, args);
        }

        public static object? InvokeStatic(Type type, string methodName, params object?[] args)
        {
            var mi = type.GetMethod(methodName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi == null) throw new MissingMethodException($"Static method '{methodName}' not found on {type.Name}");
            return mi.Invoke(null, args);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ComparisonPaneViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class ComparisonPaneViewModelTests
    {
        private static ComparisonPaneViewModel CreateVM(
            IList<LogEntry>? plc = null, IList<LogEntry>? app = null)
        {
            return new ComparisonPaneViewModel(
                plc ?? new List<LogEntry>(),
                app ?? new List<LogEntry>());
        }

        // ── BinarySearchNearest ──

        [Fact]
        public void BinarySearchNearest_EmptyList_ReturnsNegativeOne()
        {
            var vm = CreateVM();
            int idx = vm.BinarySearchNearest(DateTime.Now);
            Assert.Equal(-1, idx);
        }

        [Fact]
        public void BinarySearchNearest_SingleItem_ReturnsZero()
        {
            var t = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry> { VmHelper.MakeLog(date: t) };
            var vm = CreateVM(plc);
            Assert.Equal(0, vm.BinarySearchNearest(t));
        }

        [Fact]
        public void BinarySearchNearest_ExactMatch_ReturnsIndex()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 10);
            var t3 = new DateTime(2025, 1, 1, 12, 0, 20);
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1),
                VmHelper.MakeLog(date: t2),
                VmHelper.MakeLog(date: t3)
            };
            var vm = CreateVM(plc);
            Assert.Equal(1, vm.BinarySearchNearest(t2));
        }

        [Fact]
        public void BinarySearchNearest_BeforeFirst_ReturnsZero()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 10);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 20);
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1),
                VmHelper.MakeLog(date: t2)
            };
            var vm = CreateVM(plc);
            Assert.Equal(0, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 11, 0, 0)));
        }

        [Fact]
        public void BinarySearchNearest_AfterLast_ReturnsLastIndex()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 10);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 20);
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1),
                VmHelper.MakeLog(date: t2)
            };
            var vm = CreateVM(plc);
            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 13, 0, 0)));
        }

        [Fact]
        public void BinarySearchNearest_BetweenEntries_ReturnsNearest()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 10);
            var t3 = new DateTime(2025, 1, 1, 12, 0, 30);
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1),
                VmHelper.MakeLog(date: t2),
                VmHelper.MakeLog(date: t3)
            };
            var vm = CreateVM(plc);
            // 12:00:12 is closer to t2 (12:00:10) than t3 (12:00:30)
            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 12, 0, 12)));
        }

        // ── GetLogAtIndex ──

        [Fact]
        public void GetLogAtIndex_ValidIndex_ReturnsLog()
        {
            var log = VmHelper.MakeLog(msg: "found");
            var vm = CreateVM(new List<LogEntry> { log });
            Assert.Same(log, vm.GetLogAtIndex(0));
        }

        [Fact]
        public void GetLogAtIndex_OutOfRange_ReturnsNull()
        {
            var vm = CreateVM();
            Assert.Null(vm.GetLogAtIndex(5));
            Assert.Null(vm.GetLogAtIndex(-1));
        }

        // ── ApplySourceFilter ──

        [Fact]
        public void ApplySourceFilter_AllPLC_ReturnsAllPlc()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(msg: "plc1"),
                VmHelper.MakeLog(msg: "plc2")
            };
            var vm = CreateVM(plc);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllPLC;
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ApplySourceFilter_AllAPP_ReturnsAllApp()
        {
            var app = new List<LogEntry>
            {
                VmHelper.MakeLog(msg: "app1"),
                VmHelper.MakeLog(msg: "app2"),
                VmHelper.MakeLog(msg: "app3")
            };
            var vm = CreateVM(app: app);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllAPP;
            Assert.Equal(3, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ApplySourceFilter_ByThread_FiltersCorrectly()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(thread: "Alpha", msg: "a"),
                VmHelper.MakeLog(thread: "Beta", msg: "b"),
                VmHelper.MakeLog(thread: "Alpha", msg: "c")
            };
            var vm = CreateVM(plc);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByThread;
            vm.SelectedFilter = "Alpha";
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ApplySourceFilter_ByLogger_FiltersAppLogs()
        {
            var app = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "com.hp.Foo", msg: "a"),
                VmHelper.MakeLog(logger: "com.hp.Bar", msg: "b"),
            };
            var vm = CreateVM(app: app);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByLogger;
            vm.SelectedFilter = "com.hp.Foo";
            Assert.Single(vm.FilteredLogs);
            Assert.Equal("a", vm.FilteredLogs[0].Message);
        }

        [Fact]
        public void ApplySourceFilter_ByMethod_FiltersAppLogs()
        {
            var app = new List<LogEntry>
            {
                VmHelper.MakeLog(method: "DoA", msg: "a"),
                VmHelper.MakeLog(method: "DoB", msg: "b"),
            };
            var vm = CreateVM(app: app);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByMethod;
            vm.SelectedFilter = "DoB";
            Assert.Single(vm.FilteredLogs);
            Assert.Equal("b", vm.FilteredLogs[0].Message);
        }

        [Fact]
        public void ApplySourceFilter_ByPattern_FiltersPlcLogs()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(pattern: "P1", msg: "a"),
                VmHelper.MakeLog(pattern: "P2", msg: "b"),
                VmHelper.MakeLog(pattern: "P1", msg: "c"),
            };
            var vm = CreateVM(plc);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByPattern;
            vm.SelectedFilter = "P2";
            Assert.Single(vm.FilteredLogs);
        }

        [Fact]
        public void ApplySourceFilter_ByThreadFromApp_FiltersAppLogs()
        {
            var app = new List<LogEntry>
            {
                VmHelper.MakeLog(thread: "Worker1", msg: "a"),
                VmHelper.MakeLog(thread: "Worker2", msg: "b"),
            };
            var vm = CreateVM(app: app);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByThreadFromApp;
            vm.SelectedFilter = "Worker1";
            Assert.Single(vm.FilteredLogs);
        }

        [Fact]
        public void ApplySourceFilter_ByLoggerFromPLC_FiltersPlcLogs()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "PLC.Motor", msg: "a"),
                VmHelper.MakeLog(logger: "PLC.Sensor", msg: "b"),
            };
            var vm = CreateVM(plc);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByLoggerFromPLC;
            vm.SelectedFilter = "PLC.Motor";
            Assert.Single(vm.FilteredLogs);
        }

        [Fact]
        public void ApplySourceFilter_ByMethodFromPLC_FiltersPlcLogs()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(method: "Init", msg: "a"),
                VmHelper.MakeLog(method: "Run", msg: "b"),
            };
            var vm = CreateVM(plc);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByMethodFromPLC;
            vm.SelectedFilter = "Init";
            Assert.Single(vm.FilteredLogs);
        }

        // ── Search filter ──

        [Fact]
        public void SearchText_FiltersOnMessage()
        {
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(msg: "Error in motor"),
                VmHelper.MakeLog(msg: "All good"),
                VmHelper.MakeLog(msg: "Error in sensor")
            };
            var vm = CreateVM(plc);
            vm.SearchText = "Error";
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void SearchText_CaseInsensitive()
        {
            var plc = new List<LogEntry> { VmHelper.MakeLog(msg: "Warning ABC") };
            var vm = CreateVM(plc);
            vm.SearchText = "warning";
            Assert.Single(vm.FilteredLogs);
        }

        [Fact]
        public void SearchText_FiltersOnLogger()
        {
            var plc = new List<LogEntry> { VmHelper.MakeLog(logger: "com.hp.xaxis") };
            var vm = CreateVM(plc);
            vm.SearchText = "xaxis";
            Assert.Single(vm.FilteredLogs);
        }

        [Fact]
        public void ShowFilterDropdown_FalseForAllPLC()
        {
            var vm = CreateVM();
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllPLC;
            Assert.False(vm.ShowFilterDropdown);
        }

        [Fact]
        public void ShowFilterDropdown_TrueForByThread()
        {
            var vm = CreateVM();
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByThread;
            Assert.True(vm.ShowFilterDropdown);
        }

        // ── SelectedLog property change ──

        [Fact]
        public void SelectedLog_RaisesPropertyChanged()
        {
            var vm = CreateVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.SelectedLog = VmHelper.MakeLog();
            Assert.Contains("SelectedLog", changed);
        }

        [Fact]
        public void TopVisibleIndex_RaisesPropertyChanged()
        {
            var vm = CreateVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.TopVisibleIndex = 42;
            Assert.Contains("TopVisibleIndex", changed);
            Assert.Equal(42, vm.TopVisibleIndex);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  LogComparisonViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class LogComparisonViewModelTests
    {
        private static LogComparisonViewModel CreateVM(
            IList<LogEntry>? plc = null, IList<LogEntry>? app = null)
        {
            return new LogComparisonViewModel(
                plc ?? new List<LogEntry>(),
                app ?? new List<LogEntry>());
        }

        [Fact]
        public void Constructor_InitializesPanes()
        {
            var vm = CreateVM();
            Assert.NotNull(vm.LeftPane);
            Assert.NotNull(vm.RightPane);
            Assert.NotNull(vm.DiffEngine);
        }

        [Fact]
        public void ShowDiffs_Default_True()
        {
            var vm = CreateVM();
            Assert.True(vm.ShowDiffs);
        }

        [Fact]
        public void ShowDiffs_Toggle_IncrementsDiffVersion()
        {
            var vm = CreateVM();
            int v0 = vm.DiffVersion;
            vm.ShowDiffs = false;
            Assert.Equal(v0 + 1, vm.DiffVersion);
        }

        [Fact]
        public void IsSyncLocked_Default_True()
        {
            var vm = CreateVM();
            Assert.True(vm.IsSyncLocked);
        }

        [Fact]
        public void IgnoreMaskPattern_SetsOnDiffEngine()
        {
            var vm = CreateVM();
            vm.IgnoreMaskPattern = @"\d+";
            Assert.Equal(@"\d+", vm.DiffEngine.IgnoreMaskPattern);
        }

        [Fact]
        public void IsMaskValid_InvalidPattern_ReturnsFalse()
        {
            var vm = CreateVM();
            vm.IgnoreMaskPattern = @"[invalid";
            Assert.False(vm.IsMaskValid);
        }

        [Fact]
        public void IsMaskValid_ValidPattern_ReturnsTrue()
        {
            var vm = CreateVM();
            vm.IgnoreMaskPattern = @"\d+";
            Assert.True(vm.IsMaskValid);
        }

        [Fact]
        public void DeltaDisplay_NothingSelected_ReturnsDash()
        {
            var vm = CreateVM();
            Assert.Equal("\u2014", vm.DeltaDisplay); // em dash
        }

        [Fact]
        public void DeltaDisplay_BothSelected_ReturnsMs()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 0, 500); // +500ms
            var vm = CreateVM();
            vm.LeftPane.SelectedLog = VmHelper.MakeLog(date: t1);
            vm.RightPane.SelectedLog = VmHelper.MakeLog(date: t2);
            Assert.Contains("500", vm.DeltaDisplay);
            Assert.Contains("ms", vm.DeltaDisplay);
        }

        [Fact]
        public void DeltaDisplay_LargeDelta_ReturnsSeconds()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 5); // +5s
            var vm = CreateVM();
            vm.LeftPane.SelectedLog = VmHelper.MakeLog(date: t1);
            vm.RightPane.SelectedLog = VmHelper.MakeLog(date: t2);
            Assert.Contains("s", vm.DeltaDisplay);
        }

        [Fact]
        public void DeltaDisplay_Zero_ReturnsZeroMs()
        {
            var t = new DateTime(2025, 1, 1, 12, 0, 0);
            var vm = CreateVM();
            vm.LeftPane.SelectedLog = VmHelper.MakeLog(date: t);
            vm.RightPane.SelectedLog = VmHelper.MakeLog(date: t);
            Assert.Equal("0ms", vm.DeltaDisplay);
        }

        [Fact]
        public void GetDiffForRow_NullLeft_ReturnsNull()
        {
            var vm = CreateVM();
            Assert.Null(vm.GetDiffForRow(null, VmHelper.MakeLog()));
        }

        [Fact]
        public void GetDiffForRow_NullRight_ReturnsNull()
        {
            var vm = CreateVM();
            Assert.Null(vm.GetDiffForRow(VmHelper.MakeLog(), null));
        }

        [Fact]
        public void GetDiffForRow_ShowDiffsFalse_ReturnsNull()
        {
            var vm = CreateVM();
            vm.ShowDiffs = false;
            Assert.Null(vm.GetDiffForRow(VmHelper.MakeLog(), VmHelper.MakeLog()));
        }

        [Fact]
        public void GetDiffForRow_SameMessage_AreEqual()
        {
            var vm = CreateVM();
            var result = vm.GetDiffForRow(
                VmHelper.MakeLog(msg: "same"),
                VmHelper.MakeLog(msg: "same"));
            Assert.NotNull(result);
            Assert.True(result!.AreEqual);
        }

        [Fact]
        public void GetDiffForRow_DifferentMessages_NotEqual()
        {
            var vm = CreateVM();
            var result = vm.GetDiffForRow(
                VmHelper.MakeLog(msg: "hello world"),
                VmHelper.MakeLog(msg: "hello there"));
            Assert.NotNull(result);
            Assert.False(result!.AreEqual);
        }

        [Fact]
        public void GetCorrespondingLog_NullLog_ReturnsNull()
        {
            var vm = CreateVM();
            Assert.Null(vm.GetCorrespondingLog(vm.LeftPane, null));
        }

        [Fact]
        public void GetCorrespondingLog_FindsNearestInOtherPane()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 10);
            var plc = new List<LogEntry> { VmHelper.MakeLog(date: t1), VmHelper.MakeLog(date: t2) };
            var app = new List<LogEntry> { VmHelper.MakeLog(date: t1.AddSeconds(1)) };
            var vm = new LogComparisonViewModel(plc, app);
            var result = vm.GetCorrespondingLog(vm.LeftPane, VmHelper.MakeLog(date: t1));
            Assert.NotNull(result);
        }

        [Fact]
        public void OnPaneScrollChanged_SyncLocked_SetsTopVisibleIndex()
        {
            var t1 = new DateTime(2025, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 12, 0, 10);
            var plc = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1),
                VmHelper.MakeLog(date: t2)
            };
            var app = new List<LogEntry>
            {
                VmHelper.MakeLog(date: t1.AddSeconds(2)),
                VmHelper.MakeLog(date: t2.AddSeconds(2))
            };
            var vm = new LogComparisonViewModel(plc, app);
            vm.IsSyncLocked = true;
            vm.OnPaneScrollChanged(vm.LeftPane, t1);
            // Should have set top visible index on RightPane
            Assert.True(vm.RightPane.TopVisibleIndex >= 0);
        }

        [Fact]
        public void OnPaneScrollChanged_NotSyncLocked_DoesNothing()
        {
            var plc = new List<LogEntry> { VmHelper.MakeLog() };
            var app = new List<LogEntry> { VmHelper.MakeLog() };
            var vm = new LogComparisonViewModel(plc, app);
            vm.IsSyncLocked = false;
            int before = vm.RightPane.TopVisibleIndex;
            vm.OnPaneScrollChanged(vm.LeftPane, DateTime.Now);
            Assert.Equal(before, vm.RightPane.TopVisibleIndex);
        }

        [Fact]
        public void PropertyChanged_SelectedLog_UpdatesDelta()
        {
            var vm = CreateVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.LeftPane.SelectedLog = VmHelper.MakeLog();
            Assert.Contains("DeltaDisplay", changed);
            Assert.Contains("DeltaColor", changed);
        }

        [Fact]
        public void Dispose_Succeeds()
        {
            var vm = CreateVM();
            vm.Dispose();
            // No exception = success
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FilterSearchViewModel.LoggerTree Tests
    // ════════════════════════════════════════════════════════════════════

    public class FilterSearchViewModelLoggerTreeTests
    {
        private static FilterSearchViewModel CreateVM()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            // Initialize required fields
            VmHelper.SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            VmHelper.SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            VmHelper.SetField(vm, "_treeShowOnlyLogger", (string?)null);
            VmHelper.SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            VmHelper.SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            VmHelper.SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            VmHelper.SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            VmHelper.SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            VmHelper.SetField(vm, "_activeLoggerFilters", new List<string>());
            VmHelper.SetField(vm, "_activeThreadFilters", new List<string>());
            VmHelper.SetField(vm, "_activeMethodFilters", new List<string>());
            VmHelper.SetField(vm, "_appFilterRoot", (FilterNode?)null);
            VmHelper.SetField(vm, "_isAppTimeFocusActive", false);

            // Initialize ObservableCollections used by tree methods
            VmHelper.SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            VmHelper.SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());

            return vm;
        }

        [Fact]
        public void BuildLoggerTree_EmptyLogs_ClearsTree()
        {
            var vm = CreateVM();
            vm.BuildLoggerTree(Array.Empty<LogEntry>());
            Assert.Empty(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_SingleLogger_CreatesHierarchy()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "com.hp.indigo")
            };
            vm.BuildLoggerTree(logs);
            // Root children: "com"
            Assert.Single(vm.LoggerTreeRoot);
            Assert.Equal("com", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("com", vm.LoggerTreeRoot[0].FullPath);
            Assert.Equal(1, vm.LoggerTreeRoot[0].Count);
        }

        [Fact]
        public void BuildLoggerTree_MultipleLoggers_SortedAlphabetically()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "b.service"),
                VmHelper.MakeLog(logger: "a.service"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal(2, vm.LoggerTreeRoot.Count);
            Assert.Equal("a", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("b", vm.LoggerTreeRoot[1].Name);
        }

        [Fact]
        public void BuildLoggerTree_DuplicateLoggers_AggregatesCounts()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "com.hp"),
                VmHelper.MakeLog(logger: "com.hp"),
                VmHelper.MakeLog(logger: "com.hp"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
            Assert.Equal(3, vm.LoggerTreeRoot[0].Count);
        }

        [Fact]
        public void BuildLoggerTree_SkipsEmptyLoggers()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: ""),
                VmHelper.MakeLog(logger: "com.valid"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot); // only "com"
        }

        [Fact]
        public void BuildLoggerTree_NestedHierarchy_ThreeLevels()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "com.hp.indigo"),
            };
            vm.BuildLoggerTree(logs);
            var comNode = vm.LoggerTreeRoot[0];
            Assert.Equal("com", comNode.Name);
            Assert.Single(comNode.Children);
            var hpNode = comNode.Children[0];
            Assert.Equal("hp", hpNode.Name);
            Assert.Equal("com.hp", hpNode.FullPath);
            Assert.Single(hpNode.Children);
            var indigoNode = hpNode.Children[0];
            Assert.Equal("indigo", indigoNode.Name);
            Assert.Equal("com.hp.indigo", indigoNode.FullPath);
        }

        [Fact]
        public void BuildPlcLoggerTree_EmptyLogs_ClearsTree()
        {
            var vm = CreateVM();
            vm.BuildPlcLoggerTree(Array.Empty<LogEntry>());
            Assert.Empty(vm.PlcLoggerTreeRoot);
        }

        [Fact]
        public void BuildPlcLoggerTree_WithLogs_BuildsTree()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                VmHelper.MakeLog(logger: "plc.motor"),
                VmHelper.MakeLog(logger: "plc.sensor"),
            };
            vm.BuildPlcLoggerTree(logs);
            Assert.Single(vm.PlcLoggerTreeRoot);
            Assert.Equal("plc", vm.PlcLoggerTreeRoot[0].Name);
            Assert.Equal(2, vm.PlcLoggerTreeRoot[0].Children.Count);
        }

        [Fact]
        public void ResetTreeFilters_ClearsAllState()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)VmHelper.GetField(vm, "_treeHiddenLoggers")!;
            hidden.Add("test");
            vm.ResetTreeFilters();
            Assert.Empty(hidden);
        }

        [Fact]
        public void ResetPlcTreeFilters_ClearsAllState()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)VmHelper.GetField(vm, "_plcTreeHiddenLoggers")!;
            hidden.Add("test");
            vm.ResetPlcTreeFilters();
            Assert.Empty(hidden);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ConfigExplorerViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class ConfigExplorerViewModelTests
    {
        private static ConfigExplorerViewModel CreateVMViaReflection()
        {
            var vm = (ConfigExplorerViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ConfigExplorerViewModel));
            // Initialize collections
            vm.ConfigurationFiles = new ObservableCollection<string>();
            vm.DbTreeNodes = new ObservableCollection<DbTreeNode>();
            VmHelper.SetField(vm, "_allDbTreeNodes", new ObservableCollection<DbTreeNode>());
            VmHelper.SetField(vm, "_configSearchText", "");
            return vm;
        }

        // ── ParseCsvToDataView ──

        [Fact]
        public void ParseCsvToDataView_ValidCsv_ReturnsDataView()
        {
            var vm = CreateVMViaReflection();
            string csv = "Name,Value,Status\nFoo,123,OK\nBar,456,ERR";
            var result = (DataView?)VmHelper.InvokePrivate(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(result);
            Assert.Equal(3, result!.Table!.Columns.Count);
            Assert.Equal(2, result.Table.Rows.Count);
            Assert.Equal("Foo", result.Table.Rows[0]["Name"]);
        }

        [Fact]
        public void ParseCsvToDataView_EmptyContent_ReturnsNull()
        {
            var vm = CreateVMViaReflection();
            var result = (DataView?)VmHelper.InvokePrivate(vm, "ParseCsvToDataView", "");
            Assert.Null(result);
        }

        [Fact]
        public void ParseCsvToDataView_HeaderOnly_ReturnsEmptyTable()
        {
            var vm = CreateVMViaReflection();
            var result = (DataView?)VmHelper.InvokePrivate(vm, "ParseCsvToDataView", "Col1,Col2");
            Assert.NotNull(result);
            Assert.Equal(0, result!.Table!.Rows.Count);
            Assert.Equal(2, result.Table.Columns.Count);
        }

        [Fact]
        public void ParseCsvToDataView_DuplicateHeaders_CreatesUnique()
        {
            var vm = CreateVMViaReflection();
            string csv = "X,X,X\n1,2,3";
            var result = (DataView?)VmHelper.InvokePrivate(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(result);
            var table = result!.Table!;
            Assert.Equal(3, table.Columns.Count);
            Assert.Equal("X", table.Columns[0].ColumnName);
            Assert.Equal("X_2", table.Columns[1].ColumnName);
            Assert.Equal("X_3", table.Columns[2].ColumnName);
        }

        [Fact]
        public void ParseCsvToDataView_EmptyColumnHeaders_GetDefaultName()
        {
            var vm = CreateVMViaReflection();
            string csv = "Name,,\n1,2,3";
            var result = (DataView?)VmHelper.InvokePrivate(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(result);
            Assert.Contains("Col", result!.Table!.Columns[1].ColumnName);
        }

        // ── FilterConfigContent ──

        [Fact]
        public void FilterConfigContent_EmptySearch_ReturnsFullContent()
        {
            var vm = CreateVMViaReflection();
            var piContent = vm.GetType().GetProperty("ConfigFileContent")!;
            piContent.SetValue(vm, "line1\nline2\nline3");
            VmHelper.SetField(vm, "_configSearchText", "");
            VmHelper.InvokePrivate(vm, "FilterConfigContent");
            Assert.Equal("line1\nline2\nline3", vm.FilteredConfigContent);
        }

        [Fact]
        public void FilterConfigContent_WithSearch_FiltersLines()
        {
            var vm = CreateVMViaReflection();
            var piContent = vm.GetType().GetProperty("ConfigFileContent")!;
            piContent.SetValue(vm, "apple pie\nbanana split\napple sauce");
            VmHelper.SetField(vm, "_configSearchText", "apple");
            VmHelper.InvokePrivate(vm, "FilterConfigContent");
            Assert.Contains("apple pie", vm.FilteredConfigContent!);
            Assert.Contains("apple sauce", vm.FilteredConfigContent!);
            Assert.DoesNotContain("banana", vm.FilteredConfigContent!);
        }

        [Fact]
        public void FilterConfigContent_CaseInsensitive()
        {
            var vm = CreateVMViaReflection();
            var piContent = vm.GetType().GetProperty("ConfigFileContent")!;
            piContent.SetValue(vm, "Hello World\nbye");
            VmHelper.SetField(vm, "_configSearchText", "hello");
            VmHelper.InvokePrivate(vm, "FilterConfigContent");
            Assert.Contains("Hello World", vm.FilteredConfigContent!);
        }

        // ── FilterTreeNode ──

        [Fact]
        public void FilterTreeNode_MatchByName_SetsVisible()
        {
            var vm = CreateVMViaReflection();
            var node = new DbTreeNode { Name = "Users", Type = "Table" };
            var result = (bool)VmHelper.InvokePrivate(vm, "FilterTreeNode", node, "users")!;
            Assert.True(result);
            Assert.True(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_NoMatch_SetsInvisible()
        {
            var vm = CreateVMViaReflection();
            var node = new DbTreeNode { Name = "Orders", Type = "Table" };
            var result = (bool)VmHelper.InvokePrivate(vm, "FilterTreeNode", node, "xyz")!;
            Assert.False(result);
            Assert.False(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_MatchByType_SetsVisible()
        {
            var vm = CreateVMViaReflection();
            var node = new DbTreeNode { Name = "col1", Type = "INTEGER" };
            var result = (bool)VmHelper.InvokePrivate(vm, "FilterTreeNode", node, "integer")!;
            Assert.True(result);
        }

        [Fact]
        public void FilterTreeNode_ChildMatch_MakesParentVisible()
        {
            var vm = CreateVMViaReflection();
            var parent = new DbTreeNode { Name = "Table1" };
            parent.Children.Add(new DbTreeNode { Name = "MatchColumn", Type = "TEXT" });
            var result = (bool)VmHelper.InvokePrivate(vm, "FilterTreeNode", parent, "matchcol")!;
            Assert.True(result);
            Assert.True(parent.IsVisible);
            Assert.True(parent.IsExpanded);
        }

        // ── SetNodeVisibility ──

        [Fact]
        public void SetNodeVisibility_CascadesToChildren()
        {
            var vm = CreateVMViaReflection();
            var parent = new DbTreeNode { Name = "root" };
            parent.Children.Add(new DbTreeNode { Name = "child1" });
            parent.Children.Add(new DbTreeNode { Name = "child2" });
            VmHelper.InvokePrivate(vm, "SetNodeVisibility", parent, false);
            Assert.False(parent.IsVisible);
            Assert.False(parent.Children[0].IsVisible);
            Assert.False(parent.Children[1].IsVisible);
        }

        // ── ClearConfigurationFiles ──

        [Fact]
        public void ClearConfigurationFiles_ClearsAll()
        {
            var vm = CreateVMViaReflection();
            vm.ConfigurationFiles.Add("test.json");
            vm.DbTreeNodes.Add(new DbTreeNode { Name = "tbl" });
            // Need to set _configFileContent via property
            var piContent = vm.GetType().GetProperty("ConfigFileContent")!;
            piContent.SetValue(vm, "some content");

            vm.ClearConfigurationFiles();

            Assert.Empty(vm.ConfigurationFiles);
            Assert.Empty(vm.DbTreeNodes);
            Assert.False(vm.IsDbFileSelected);
            Assert.False(vm.IsCsvFileSelected);
            Assert.Null(vm.CsvDataView);
        }

        // ── FilterDbTreeNodes ──

        [Fact]
        public void FilterDbTreeNodes_EmptySearch_ShowsAll()
        {
            var vm = CreateVMViaReflection();
            var rootNode = new DbTreeNode { Name = "Tables (2)" };
            rootNode.Children.Add(new DbTreeNode { Name = "Users" });
            rootNode.Children.Add(new DbTreeNode { Name = "Orders" });
            vm.DbTreeNodes.Add(rootNode);
            VmHelper.SetField(vm, "_configSearchText", "");
            VmHelper.InvokePrivate(vm, "FilterDbTreeNodes");
            Assert.True(rootNode.Children[0].IsVisible);
            Assert.True(rootNode.Children[1].IsVisible);
        }

        [Fact]
        public void FilterDbTreeNodes_WithSearch_FiltersTablesByName()
        {
            var vm = CreateVMViaReflection();
            var rootNode = new DbTreeNode { Name = "Tables (2)" };
            rootNode.Children.Add(new DbTreeNode { Name = "Users" });
            rootNode.Children.Add(new DbTreeNode { Name = "Orders" });
            vm.DbTreeNodes.Add(rootNode);
            VmHelper.SetField(vm, "_configSearchText", "Users");
            VmHelper.InvokePrivate(vm, "FilterDbTreeNodes");
            Assert.True(rootNode.Children[0].IsVisible);
            Assert.False(rootNode.Children[1].IsVisible);
        }

        // ── Property notifications ──

        [Fact]
        public void IsDbFileSelected_RaisesPropertyChanged()
        {
            var vm = CreateVMViaReflection();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.IsDbFileSelected = true;
            Assert.Contains("IsDbFileSelected", changed);
        }

        [Fact]
        public void IsCsvFileSelected_RaisesPropertyChanged()
        {
            var vm = CreateVMViaReflection();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.IsCsvFileSelected = true;
            Assert.Contains("IsCsvFileSelected", changed);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DifferentLogsViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class DifferentLogsViewModelBatch3Tests
    {
        [Fact]
        public void BuiltInFields_IsNotEmpty()
        {
            Assert.True(DifferentLogsViewModel.BuiltInFields.Count > 0);
        }

        [Fact]
        public void BuiltInFields_ContainsDate()
        {
            Assert.True(DifferentLogsViewModel.BuiltInFields.Contains("Date"));
        }

        [Fact]
        public void BuiltInFields_ContainsMessage()
        {
            Assert.True(DifferentLogsViewModel.BuiltInFields.Contains("Message"));
        }

        [Fact]
        public void BuildAvailableFields_NoColumns_StandardFieldsOnly()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_allLogEntries", new List<LogEntry>());
            VmHelper.SetField(vm, "_columns", null);
            VmHelper.SetField(vm, "_availableFields", new List<string>());
            vm.BuildAvailableFields();
            Assert.Contains("Message", vm.AvailableFields);
            Assert.Contains("Level", vm.AvailableFields);
            Assert.Contains("Logger", vm.AvailableFields);
            Assert.Contains("ThreadName", vm.AvailableFields);
        }

        [Fact]
        public void BuildAvailableFields_WithExtraFields_IncludesThem()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            var entries = new List<LogEntry>
            {
                new LogEntry { ExtraFields = new Dictionary<string, string> { { "CustomCol", "value" } } }
            };
            VmHelper.SetField(vm, "_allLogEntries", entries);
            VmHelper.SetField(vm, "_columns", null);
            VmHelper.SetField(vm, "_availableFields", new List<string>());
            vm.BuildAvailableFields();
            Assert.Contains("CustomCol", vm.AvailableFields);
        }

        [Fact]
        public void HasFile_WhenNoPath_ReturnsFalse()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_currentFilePath", null);
            Assert.False(vm.HasFile);
        }

        [Fact]
        public void HasFile_WhenPathSet_ReturnsTrue()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_currentFilePath", @"C:\test\file.log");
            Assert.True(vm.HasFile);
        }

        [Fact]
        public void CurrentFileName_ExtractsFileName()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_currentFilePath", @"C:\test\mylog.txt");
            Assert.Equal("mylog.txt", vm.CurrentFileName);
        }

        [Fact]
        public void CurrentFileName_WhenNull_ReturnsEmpty()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_currentFilePath", null);
            Assert.Equal(string.Empty, vm.CurrentFileName);
        }

        [Fact]
        public void StatusText_Default_NonEmpty()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_statusText", "Open a log file to begin.");
            Assert.Equal("Open a log file to begin.", vm.StatusText);
        }

        [Fact]
        public void IsFilterActive_Default_False()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            VmHelper.SetField(vm, "_isFilterActive", false);
            Assert.False(vm.IsFilterActive);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CprAnalysisViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class CprAnalysisViewModelTests
    {
        private static CprAnalysisViewModel CreateVM()
        {
            var vm = (CprAnalysisViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CprAnalysisViewModel));
            VmHelper.SetField(vm, "_dataService", new CprDataService());
            VmHelper.SetField(vm, "_analysisService", new CprAnalysisService());
            VmHelper.SetField(vm, "_isLoadingFilters", false);
            VmHelper.SetField(vm, "_selectedGraphType", CprGraphType.Colors);
            VmHelper.SetField(vm, "_selectedMachine", 0);
            VmHelper.SetField(vm, "_selectedCalibTime", (string?)null);
            VmHelper.SetField(vm, "_selectedRevolution", (string?)null);
            VmHelper.SetField(vm, "_selectedIteration", 0);
            VmHelper.SetField(vm, "_selectedCycleFrom", 0);
            VmHelper.SetField(vm, "_selectedCycleTo", 0);
            VmHelper.SetField(vm, "_selectedColumnFrom", 0);
            VmHelper.SetField(vm, "_selectedColumnTo", 0);
            VmHelper.SetField(vm, "_isYAxis", true);
            VmHelper.SetField(vm, "_removeDC", false);
            VmHelper.SetField(vm, "_autoYAxis", true);
            VmHelper.SetField(vm, "_sharedYAxis", false);
            VmHelper.SetField(vm, "_selectedSmoothing", 1);
            VmHelper.SetField(vm, "_selectedBowDegree", 3);
            VmHelper.SetField(vm, "_yAxisFrom", "-200");
            VmHelper.SetField(vm, "_yAxisTo", "200");
            VmHelper.SetField(vm, "_blanketCyclesText", "");
            VmHelper.SetField(vm, "_histoStationsText", "1 2 3 4 5 6");
            VmHelper.SetField(vm, "_currentResult", (CprGraphResult?)null);

            // Auto-property backing fields for get-only properties
            var testSelField = typeof(CprAnalysisViewModel).GetField("<StationTestSelections>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            testSelField?.SetValue(vm, new int[] { 1, 2, 3, 4, 5, 6 });
            var refSelField = typeof(CprAnalysisViewModel).GetField("<StationRefSelections>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            refSelField?.SetValue(vm, new int[6]);

            return vm;
        }

        // ── ParseIntList ──

        [Fact]
        public void ParseIntList_EmptyString_ReturnsEmptyArray()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "")!;
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIntList_SpaceSeparated_ParsesCorrectly()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "1 2 3")!;
            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        [Fact]
        public void ParseIntList_CommaSeparated_ParsesCorrectly()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "4,5,6")!;
            Assert.Equal(new[] { 4, 5, 6 }, result);
        }

        [Fact]
        public void ParseIntList_SemicolonSeparated_ParsesCorrectly()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "1;3;5")!;
            Assert.Equal(new[] { 1, 3, 5 }, result);
        }

        [Fact]
        public void ParseIntList_MixedSeparators_ParsesCorrectly()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "1 2,3;4")!;
            Assert.Equal(new[] { 1, 2, 3, 4 }, result);
        }

        [Fact]
        public void ParseIntList_ZeroAndNegative_FiltersOut()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "0 -1 2 3")!;
            Assert.Equal(new[] { 2, 3 }, result);
        }

        [Fact]
        public void ParseIntList_InvalidTokens_Skipped()
        {
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "abc 2 def 4")!;
            Assert.Equal(new[] { 2, 4 }, result);
        }

        [Fact]
        public void ParseIntList_Null_ReturnsEmpty()
        {
            // Whitespace-only
            var result = (int[])VmHelper.InvokeStatic(typeof(CprAnalysisViewModel), "ParseIntList", "   ")!;
            Assert.Empty(result);
        }

        // ── BuildFilterState ──

        [Fact]
        public void BuildFilterState_ReturnsCorrectDefaults()
        {
            var vm = CreateVM();
            var filter = (CprFilterState)VmHelper.InvokePrivate(vm, "BuildFilterState")!;
            Assert.Equal("Y", filter.Axis);
            Assert.True(filter.AutoYAxis);
            Assert.Equal(1, filter.SmoothingWindow);
            Assert.Equal(3, filter.BowDegree);
            Assert.Equal(-200, filter.YAxisFrom);
            Assert.Equal(200, filter.YAxisTo);
        }

        [Fact]
        public void BuildFilterState_XAxis_ReturnsX()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_isYAxis", false);
            var filter = (CprFilterState)VmHelper.InvokePrivate(vm, "BuildFilterState")!;
            Assert.Equal("X", filter.Axis);
        }

        [Fact]
        public void BuildFilterState_CustomYRange_Parsed()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_yAxisFrom", "-500");
            VmHelper.SetField(vm, "_yAxisTo", "500");
            VmHelper.SetField(vm, "_autoYAxis", false);
            var filter = (CprFilterState)VmHelper.InvokePrivate(vm, "BuildFilterState")!;
            Assert.Equal(-500, filter.YAxisFrom);
            Assert.Equal(500, filter.YAxisTo);
            Assert.False(filter.AutoYAxis);
        }

        // ── BuildStationPairs ──

        [Fact]
        public void BuildStationPairs_Returns6Pairs()
        {
            var vm = CreateVM();
            var pairs = (CprStationPair[])VmHelper.InvokePrivate(vm, "BuildStationPairs")!;
            Assert.Equal(6, pairs.Length);
        }

        // ── Graph type visibility ──

        [Fact]
        public void IsBlanketCyclesVisible_WhenBlanketCycles_ReturnsTrue()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_selectedGraphType", CprGraphType.BlanketCycles);
            Assert.True(vm.IsBlanketCyclesVisible);
        }

        [Fact]
        public void IsBlanketCyclesVisible_WhenColors_ReturnsFalse()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_selectedGraphType", CprGraphType.Colors);
            Assert.False(vm.IsBlanketCyclesVisible);
        }

        [Fact]
        public void IsHistoStationsVisible_WhenHistogram_ReturnsTrue()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_selectedGraphType", CprGraphType.Histogram);
            Assert.True(vm.IsHistoStationsVisible);
        }

        [Fact]
        public void IsHistoStationsVisible_WhenColors_ReturnsFalse()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_selectedGraphType", CprGraphType.Colors);
            Assert.False(vm.IsHistoStationsVisible);
        }

        [Fact]
        public void IsManualYVisible_WhenAutoTrue_ReturnsFalse()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_autoYAxis", true);
            Assert.False(vm.IsManualYVisible);
        }

        [Fact]
        public void IsManualYVisible_WhenAutoFalse_ReturnsTrue()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_autoYAxis", false);
            Assert.True(vm.IsManualYVisible);
        }

        // ── SetAllRefStationsBatch ──

        [Fact]
        public void SetAllRefStationsBatch_SetsAllToValue()
        {
            var vm = CreateVM();
            // Prevent Apply() from running (it needs full data loaded)
            VmHelper.SetField(vm, "_isLoadingFilters", true);
            vm.SetAllRefStationsBatch(3);
            for (int i = 0; i < 6; i++)
                Assert.Equal(3, vm.StationRefSelections[i]);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  StatsViewModel.Calculation Tests (additional)
    // ════════════════════════════════════════════════════════════════════

    public class StatsViewModelCalculationExtraTests
    {
        private static LogEntry MakeLog(string level = "Info", string msg = "test",
            string thread = "T1", string logger = "com.hp.Service",
            string method = "Run", DateTime? date = null)
        {
            return new LogEntry
            {
                Level = level,
                Message = msg,
                ThreadName = thread,
                Logger = logger,
                Method = method,
                Date = date ?? new DateTime(2025, 6, 1, 12, 0, 0)
            };
        }

        [Fact]
        public void TruncateMessage_EmptyString_ReturnsEmpty()
        {
            var result = (string)VmHelper.InvokeStatic(typeof(StatsViewModel), "TruncateMessage", "", 50)!;
            Assert.Equal("(empty)", result);
        }

        [Fact]
        public void TruncateMessage_Null_ReturnsEmpty()
        {
            var result = (string)VmHelper.InvokeStatic(typeof(StatsViewModel), "TruncateMessage", (string)null!, 50)!;
            Assert.Equal("(empty)", result);
        }

        [Fact]
        public void TruncateMessage_ShortString_ReturnsUnchanged()
        {
            var result = (string)VmHelper.InvokeStatic(typeof(StatsViewModel), "TruncateMessage", "hello", 50)!;
            Assert.Equal("hello", result);
        }

        [Fact]
        public void TruncateMessage_LongString_Truncates()
        {
            string longStr = new string('x', 200);
            var result = (string)VmHelper.InvokeStatic(typeof(StatsViewModel), "TruncateMessage", longStr, 100)!;
            Assert.Equal(103, result.Length); // 100 + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void GetShortLoggerName_EmptyLogger_ReturnsUnknown()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            var result = (string)VmHelper.InvokePrivate(vm, "GetShortLoggerName", "")!;
            Assert.Equal("Unknown", result);
        }

        [Fact]
        public void GetShortLoggerName_ShortLogger_ReturnsUnchanged()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            var result = (string)VmHelper.InvokePrivate(vm, "GetShortLoggerName", "A.B")!;
            Assert.Equal("A.B", result);
        }

        [Fact]
        public void GetShortLoggerName_LongLogger_ReturnsLastTwo()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            var result = (string)VmHelper.InvokePrivate(vm, "GetShortLoggerName", "com.hp.indigo.Service.Foo")!;
            Assert.Equal("Service.Foo", result);
        }

        [Fact]
        public void GetShortLoggerName_CachesResult()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            string r1 = (string)VmHelper.InvokePrivate(vm, "GetShortLoggerName", "a.b.c.d")!;
            string r2 = (string)VmHelper.InvokePrivate(vm, "GetShortLoggerName", "a.b.c.d")!;
            Assert.Equal(r1, r2);
        }

        [Fact]
        public async System.Threading.Tasks.Task CalculateStatistics_NoLogs_SetsSummary()
        {
            var vm = new StatsViewModel(null, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();
            Assert.Contains("No logs", vm.SummaryText);
        }

        [Fact]
        public async System.Threading.Tasks.Task CalculateStatistics_PlcOnly_SetsPlcSummary()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(1)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();
            Assert.True(vm.PlcHasLogs);
            Assert.False(vm.AppHasLogs);
            Assert.Contains("PLC", vm.PlcSummaryText);
        }

        [Fact]
        public async System.Threading.Tasks.Task CalculateStatistics_WithErrors_PopulatesErrorStats()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(level: "ERROR", msg: "fail1", date: baseDate),
                MakeLog(level: "ERROR", msg: "fail2", date: baseDate.AddSeconds(1)),
                MakeLog(level: "Info", msg: "ok", date: baseDate.AddSeconds(2)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();
            Assert.NotNull(vm.PlcErrorStats);
            Assert.True(vm.PlcErrorStats!.Count > 0);
        }

        [Fact]
        public async System.Threading.Tasks.Task CalculateStatistics_WithGaps_DetectsGaps()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddSeconds(10)), // 10s gap > 2s threshold
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();
            Assert.True(vm.PlcHasGaps);
            Assert.NotNull(vm.PlcGaps);
            Assert.Single(vm.PlcGaps!);
        }

        [Fact]
        public async System.Threading.Tasks.Task CalculateStatistics_NoGaps_SetsNoGaps()
        {
            var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);
            var plc = new List<LogEntry>
            {
                MakeLog(date: baseDate),
                MakeLog(date: baseDate.AddMilliseconds(500)),
            };
            var vm = new StatsViewModel(plc, null, null, null, false, false);
            await vm.CalculateStatisticsAsync();
            Assert.False(vm.PlcHasGaps);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExportConfigurationViewModel.Filtering Tests
    // ════════════════════════════════════════════════════════════════════

    public class ExportConfigurationViewModelFilteringTests
    {
        private static ExportConfigurationViewModel CreateVM()
        {
            var vm = (ExportConfigurationViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ExportConfigurationViewModel));
            // Initialize observable collections
            var ioComp = new ObservableCollection<SelectableItem>();
            var axisComp = new ObservableCollection<SelectableItem>();
            var chStepComp = new ObservableCollection<SelectableItem>();
            var threadItems = new ObservableCollection<SelectableItem>();

            vm.IOComponents = ioComp;
            vm.AxisComponents = axisComp;
            vm.CHStepComponents = chStepComp;
            vm.ThreadItems = threadItems;

            VmHelper.SetField(vm, "_ioSearchText", "");
            VmHelper.SetField(vm, "_axisSearchText", "");
            VmHelper.SetField(vm, "_chStepSearchText", "");
            VmHelper.SetField(vm, "_threadSearchText", "");
            VmHelper.SetField(vm, "_cachedIOFiltered", null);
            VmHelper.SetField(vm, "_cachedAxisFiltered", null);
            VmHelper.SetField(vm, "_cachedCHStepFiltered", null);
            VmHelper.SetField(vm, "_cachedThreadFiltered", null);

            return vm;
        }

        [Fact]
        public void UpdateIOFilter_EmptySearch_ClearsCache()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "IO1", Category = "Digital" });
            VmHelper.SetField(vm, "_ioSearchText", "");
            VmHelper.InvokePrivate(vm, "UpdateIOFilter");
            var cached = VmHelper.GetField(vm, "_cachedIOFiltered");
            Assert.Null(cached);
        }

        [Fact]
        public void UpdateIOFilter_WithSearch_FiltersByName()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "MotorA", Category = "Digital" });
            vm.IOComponents.Add(new SelectableItem { Name = "SensorB", Category = "Analog" });
            vm.IOComponents.Add(new SelectableItem { Name = "MotorC", Category = "Digital" });
            VmHelper.SetField(vm, "_ioSearchText", "Motor");
            VmHelper.InvokePrivate(vm, "UpdateIOFilter");
            var cached = (List<SelectableItem>?)VmHelper.GetField(vm, "_cachedIOFiltered");
            Assert.NotNull(cached);
            Assert.Equal(2, cached!.Count);
        }

        [Fact]
        public void UpdateIOFilter_WithSearch_FiltersByCategory()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "X1", Category = "DigitalOutput" });
            vm.IOComponents.Add(new SelectableItem { Name = "X2", Category = "AnalogInput" });
            VmHelper.SetField(vm, "_ioSearchText", "analog");
            VmHelper.InvokePrivate(vm, "UpdateIOFilter");
            var cached = (List<SelectableItem>?)VmHelper.GetField(vm, "_cachedIOFiltered");
            Assert.NotNull(cached);
            Assert.Single(cached!);
        }

        [Fact]
        public void UpdateAxisFilter_EmptySearch_ClearsCache()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_axisSearchText", "");
            VmHelper.InvokePrivate(vm, "UpdateAxisFilter");
            Assert.Null(VmHelper.GetField(vm, "_cachedAxisFiltered"));
        }

        [Fact]
        public void UpdateAxisFilter_WithSearch_Filters()
        {
            var vm = CreateVM();
            vm.AxisComponents.Add(new SelectableItem { Name = "XAxis", Category = "Servo" });
            vm.AxisComponents.Add(new SelectableItem { Name = "YAxis", Category = "Servo" });
            VmHelper.SetField(vm, "_axisSearchText", "xaxis");
            VmHelper.InvokePrivate(vm, "UpdateAxisFilter");
            var cached = (List<SelectableItem>?)VmHelper.GetField(vm, "_cachedAxisFiltered");
            Assert.Single(cached!);
        }

        [Fact]
        public void UpdateCHStepFilter_WithSearch_Filters()
        {
            var vm = CreateVM();
            vm.CHStepComponents.Add(new SelectableItem { Name = "Step1", Category = "CH" });
            vm.CHStepComponents.Add(new SelectableItem { Name = "Step2", Category = "CH" });
            VmHelper.SetField(vm, "_chStepSearchText", "Step1");
            VmHelper.InvokePrivate(vm, "UpdateCHStepFilter");
            var cached = (List<SelectableItem>?)VmHelper.GetField(vm, "_cachedCHStepFiltered");
            Assert.Single(cached!);
        }

        [Fact]
        public void UpdateThreadFilter_WithSearch_Filters()
        {
            var vm = CreateVM();
            vm.ThreadItems.Add(new SelectableItem { Name = "Worker-1" });
            vm.ThreadItems.Add(new SelectableItem { Name = "Main" });
            VmHelper.SetField(vm, "_threadSearchText", "worker");
            VmHelper.InvokePrivate(vm, "UpdateThreadFilter");
            var cached = (List<SelectableItem>?)VmHelper.GetField(vm, "_cachedThreadFiltered");
            Assert.Single(cached!);
        }

        [Fact]
        public void FilteredIOComponents_NoCachedFilter_ReturnsIOComponents()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "X" });
            VmHelper.SetField(vm, "_cachedIOFiltered", null);
            Assert.Single(vm.FilteredIOComponents);
        }

        [Fact]
        public void FilteredIOComponents_WithCachedFilter_ReturnsCached()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "X" });
            vm.IOComponents.Add(new SelectableItem { Name = "Y" });
            var cached = new List<SelectableItem> { new SelectableItem { Name = "X" } };
            VmHelper.SetField(vm, "_cachedIOFiltered", cached);
            Assert.Single(vm.FilteredIOComponents);
        }

        // ── SelectAll / DeselectAll ──

        [Fact]
        public void SelectAll_SetsAllSelected()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "A", IsSelected = false });
            vm.IOComponents.Add(new SelectableItem { Name = "B", IsSelected = false });
            VmHelper.InvokePrivate(vm, "SelectAll", vm.IOComponents);
            Assert.All(vm.IOComponents, item => Assert.True(item.IsSelected));
        }

        [Fact]
        public void DeselectAll_UnsetsAllSelected()
        {
            var vm = CreateVM();
            vm.IOComponents.Add(new SelectableItem { Name = "A", IsSelected = true });
            vm.IOComponents.Add(new SelectableItem { Name = "B", IsSelected = true });
            VmHelper.InvokePrivate(vm, "DeselectAll", vm.IOComponents);
            Assert.All(vm.IOComponents, item => Assert.False(item.IsSelected));
        }

        // ── ApplyPreset ──

        [Fact]
        public void ApplyPreset_SetsCheckboxes()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_includeUnixTime", false);
            VmHelper.SetField(vm, "_includeEvents", false);
            VmHelper.SetField(vm, "_includeMachineState", false);
            VmHelper.SetField(vm, "_includeLogStats", false);
            var preset = new ExportPreset
            {
                IncludeUnixTime = true,
                IncludeEvents = true,
                IncludeMachineState = true,
                IncludeLogStats = true,
                SelectedIOComponents = new List<string>(),
                SelectedAxisComponents = new List<string>(),
                SelectedCHSteps = new List<string>(),
                SelectedThreads = new List<string>()
            };
            VmHelper.InvokePrivate(vm, "ApplyPreset", preset);
            Assert.True(vm.IncludeUnixTime);
            Assert.True(vm.IncludeEvents);
            Assert.True(vm.IncludeMachineState);
            Assert.True(vm.IncludeLogStats);
        }

        [Fact]
        public void ApplyPreset_SelectsMatchingComponents()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_includeUnixTime", false);
            VmHelper.SetField(vm, "_includeEvents", false);
            VmHelper.SetField(vm, "_includeMachineState", false);
            VmHelper.SetField(vm, "_includeLogStats", false);
            vm.IOComponents.Add(new SelectableItem { Name = "Motor", Category = "Digital", IsSelected = false });
            vm.IOComponents.Add(new SelectableItem { Name = "Sensor", Category = "Analog", IsSelected = false });
            var preset = new ExportPreset
            {
                IncludeUnixTime = false,
                IncludeEvents = false,
                IncludeMachineState = false,
                IncludeLogStats = false,
                SelectedIOComponents = new List<string> { "Digital|Motor" },
                SelectedAxisComponents = new List<string>(),
                SelectedCHSteps = new List<string>(),
                SelectedThreads = new List<string>()
            };
            VmHelper.InvokePrivate(vm, "ApplyPreset", preset);
            Assert.True(vm.IOComponents[0].IsSelected);
            Assert.False(vm.IOComponents[1].IsSelected);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TabOrchestrationViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class TabOrchestrationViewModelTests
    {
        [Fact]
        public void Constructor_ThrowsOnNullParent()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TabOrchestrationViewModel(null!, new TestDispatcher()));
        }

        [Fact]
        public void Constructor_ThrowsOnNullDispatcher()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.Throws<ArgumentNullException>(() =>
                new TabOrchestrationViewModel(parent, null!));
        }

        [Fact]
        public void IsTimeSyncEnabled_Default_False()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var td = new TestDispatcher();
            // Need to init SessionVM on parent
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            VmHelper.SetField(sessionVM, "_statusMessage", "");
            var piSession = parent.GetType().GetProperty("SessionVM");
            piSession?.SetValue(parent, sessionVM);

            var vm = new TabOrchestrationViewModel(parent, td);
            Assert.False(vm.IsTimeSyncEnabled);
        }

        [Fact]
        public void TimeSyncOffsetSeconds_DefaultZero()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            VmHelper.SetField(sessionVM, "_statusMessage", "");
            parent.GetType().GetProperty("SessionVM")?.SetValue(parent, sessionVM);

            var vm = new TabOrchestrationViewModel(parent, new TestDispatcher());
            Assert.Equal(0, vm.TimeSyncOffsetSeconds);
        }

        [Fact]
        public void PendingSyncLog_DefaultNull()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            VmHelper.SetField(sessionVM, "_statusMessage", "");
            parent.GetType().GetProperty("SessionVM")?.SetValue(parent, sessionVM);

            var vm = new TabOrchestrationViewModel(parent, new TestDispatcher());
            Assert.Null(vm.PendingSyncLog);
            Assert.Equal(-1, vm.PendingSyncTabIndex);
        }

        [Fact]
        public void TryApplyPendingSync_NoPending_DoesNothing()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            VmHelper.SetField(sessionVM, "_statusMessage", "");
            parent.GetType().GetProperty("SessionVM")?.SetValue(parent, sessionVM);

            var vm = new TabOrchestrationViewModel(parent, new TestDispatcher());
            bool called = false;
            vm.TryApplyPendingSync(0, log => called = true);
            Assert.False(called);
        }

        [Fact]
        public void TryApplyPendingSync_WrongTab_DoesNotApply()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            VmHelper.SetField(sessionVM, "_statusMessage", "");
            parent.GetType().GetProperty("SessionVM")?.SetValue(parent, sessionVM);

            var vm = new TabOrchestrationViewModel(parent, new TestDispatcher());
            vm.PendingSyncLog = VmHelper.MakeLog();
            vm.PendingSyncTabIndex = 1;
            bool called = false;
            vm.TryApplyPendingSync(0, log => called = true);
            Assert.False(called);
            // Pending should still be set
            Assert.NotNull(vm.PendingSyncLog);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  LiveMonitoringViewModel Tests
    // ════════════════════════════════════════════════════════════════════

    public class LiveMonitoringViewModelTests
    {
        [Fact]
        public void IsLiveMode_RaisesPropertyChanged()
        {
            var vm = (LiveMonitoringViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LiveMonitoringViewModel));
            VmHelper.SetField(vm, "_isLiveMode", false);
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.IsLiveMode = true;
            Assert.Contains("IsLiveMode", changed);
            Assert.True(vm.IsLiveMode);
        }

        [Fact]
        public void IsRunning_RaisesPropertyChanged()
        {
            var vm = (LiveMonitoringViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LiveMonitoringViewModel));
            VmHelper.SetField(vm, "_isRunning", false);
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.IsRunning = true;
            Assert.Contains("IsRunning", changed);
            Assert.Contains("IsPaused", changed); // also notifies IsPaused
        }

        [Fact]
        public void IsPaused_RaisesPropertyChanged()
        {
            var vm = (LiveMonitoringViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LiveMonitoringViewModel));
            VmHelper.SetField(vm, "_isPaused", false);
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.IsPaused = true;
            Assert.Contains("IsPaused", changed);
            Assert.Contains("IsRunning", changed);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MainViewModel.Systab Tests
    // ════════════════════════════════════════════════════════════════════

    public class MainViewModelSystabTests
    {
        private static MainViewModel CreateVM()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            VmHelper.SetField(vm, "_systabTree", new ObservableCollection<SystabTopicNode>());
            VmHelper.SetField(vm, "_systabEntries", new ObservableCollection<SystabEntry>());
            VmHelper.SetField(vm, "_allSystabEntries", new ObservableCollection<SystabEntry>());
            VmHelper.SetField(vm, "_selectedSystabNode", (SystabTopicNode?)null);
            VmHelper.SetField(vm, "_systabSearchText", "");
            VmHelper.SetField(vm, "_systabShowDiffsOnly", false);
            return vm;
        }

        [Fact]
        public void FilterSystabEntries_EmptySearch_ShowsAll()
        {
            var vm = CreateVM();
            var allEntries = (ObservableCollection<SystabEntry>)VmHelper.GetField(vm, "_allSystabEntries")!;
            allEntries.Add(new SystabEntry { Parameter = "P1", Saved = "100" });
            allEntries.Add(new SystabEntry { Parameter = "P2", Saved = "200" });
            VmHelper.SetField(vm, "_systabSearchText", "");
            VmHelper.InvokePrivate(vm, "FilterSystabEntries");
            Assert.Equal(2, vm.SystabEntries.Count);
        }

        [Fact]
        public void FilterSystabEntries_WithSearch_FiltersEntries()
        {
            var vm = CreateVM();
            var allEntries = (ObservableCollection<SystabEntry>)VmHelper.GetField(vm, "_allSystabEntries")!;
            allEntries.Add(new SystabEntry { Parameter = "MotorSpeed", Saved = "100" });
            allEntries.Add(new SystabEntry { Parameter = "SensorGain", Saved = "200" });
            VmHelper.SetField(vm, "_systabSearchText", "motor");
            VmHelper.InvokePrivate(vm, "FilterSystabEntries");
            Assert.Single(vm.SystabEntries);
            Assert.Equal("MotorSpeed", vm.SystabEntries[0].Parameter);
        }

        [Fact]
        public void FilterSystabEntries_DiffsOnly_FiltersDiffs()
        {
            var vm = CreateVM();
            var allEntries = (ObservableCollection<SystabEntry>)VmHelper.GetField(vm, "_allSystabEntries")!;
            allEntries.Add(new SystabEntry { Parameter = "P1", IsDifferent = true });
            allEntries.Add(new SystabEntry { Parameter = "P2", IsDifferent = false });
            allEntries.Add(new SystabEntry { Parameter = "P3", IsDifferent = true });
            VmHelper.SetField(vm, "_systabShowDiffsOnly", true);
            VmHelper.InvokePrivate(vm, "FilterSystabEntries");
            Assert.Equal(2, vm.SystabEntries.Count);
        }

        [Fact]
        public void FilterSystabEntries_DiffsAndSearch_CombinesFilters()
        {
            var vm = CreateVM();
            var allEntries = (ObservableCollection<SystabEntry>)VmHelper.GetField(vm, "_allSystabEntries")!;
            allEntries.Add(new SystabEntry { Parameter = "Motor", IsDifferent = true });
            allEntries.Add(new SystabEntry { Parameter = "Sensor", IsDifferent = true });
            allEntries.Add(new SystabEntry { Parameter = "Motor2", IsDifferent = false });
            VmHelper.SetField(vm, "_systabShowDiffsOnly", true);
            VmHelper.SetField(vm, "_systabSearchText", "motor");
            VmHelper.InvokePrivate(vm, "FilterSystabEntries");
            Assert.Single(vm.SystabEntries);
            Assert.Equal("Motor", vm.SystabEntries[0].Parameter);
        }

        [Fact]
        public void FilterSystabEntries_SearchInSavedValue()
        {
            var vm = CreateVM();
            var allEntries = (ObservableCollection<SystabEntry>)VmHelper.GetField(vm, "_allSystabEntries")!;
            allEntries.Add(new SystabEntry { Parameter = "P1", Saved = "SpecialValue" });
            allEntries.Add(new SystabEntry { Parameter = "P2", Saved = "Normal" });
            VmHelper.SetField(vm, "_systabSearchText", "special");
            VmHelper.InvokePrivate(vm, "FilterSystabEntries");
            Assert.Single(vm.SystabEntries);
        }

        [Fact]
        public void LoadSystabEntries_NullNode_ClearsEntries()
        {
            var vm = CreateVM();
            VmHelper.SetField(vm, "_selectedSystabNode", (SystabTopicNode?)null);
            VmHelper.InvokePrivate(vm, "LoadSystabEntries");
            Assert.Empty(vm.SystabEntries);
        }

        [Fact]
        public void LoadSystabEntries_NodeWithEntries_LoadsThem()
        {
            var vm = CreateVM();
            var node = new SystabTopicNode { Name = "Topic1" };
            node.Entries.Add(new SystabEntry { Parameter = "P1" });
            node.Entries.Add(new SystabEntry { Parameter = "P2" });
            VmHelper.SetField(vm, "_selectedSystabNode", node);
            VmHelper.InvokePrivate(vm, "LoadSystabEntries");
            Assert.Equal(2, vm.SystabEntries.Count);
        }

        [Fact]
        public void LoadSystabEntries_TopicWithChildren_LoadsChildEntries()
        {
            var vm = CreateVM();
            var parent = new SystabTopicNode { Name = "Parent" };
            var child1 = new SystabTopicNode { Name = "Child1" };
            child1.Entries.Add(new SystabEntry { Parameter = "CP1" });
            var child2 = new SystabTopicNode { Name = "Child2" };
            child2.Entries.Add(new SystabEntry { Parameter = "CP2" });
            parent.Children.Add(child1);
            parent.Children.Add(child2);
            VmHelper.SetField(vm, "_selectedSystabNode", parent);
            VmHelper.InvokePrivate(vm, "LoadSystabEntries");
            Assert.Equal(2, vm.SystabEntries.Count);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  LoggerNode Visual State Tests (via FilterSearchViewModel internals)
    // ════════════════════════════════════════════════════════════════════

    public class LoggerTreeVisualStateTests
    {
        private static FilterSearchViewModel CreateVM()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            VmHelper.SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            VmHelper.SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            VmHelper.SetField(vm, "_treeShowOnlyLogger", (string?)null);
            VmHelper.SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            VmHelper.SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            VmHelper.SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            VmHelper.SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            VmHelper.SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            VmHelper.SetField(vm, "_activeLoggerFilters", new List<string>());
            VmHelper.SetField(vm, "_activeThreadFilters", new List<string>());
            VmHelper.SetField(vm, "_activeMethodFilters", new List<string>());
            VmHelper.SetField(vm, "_appFilterRoot", (FilterNode?)null);
            VmHelper.SetField(vm, "_isAppTimeFocusActive", false);

            VmHelper.SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            VmHelper.SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());

            return vm;
        }

        [Fact]
        public void SetChildrenVisualState_SetsAllChildren()
        {
            var vm = CreateVM();
            var parent = new LoggerNode { Name = "root" };
            var child1 = new LoggerNode { Name = "c1" };
            var child2 = new LoggerNode { Name = "c2" };
            parent.Children.Add(child1);
            parent.Children.Add(child2);

            VmHelper.InvokePrivate(vm, "SetChildrenVisualState", parent, true, false);
            Assert.True(child1.IsHidden);
            Assert.True(child2.IsHidden);
            Assert.False(child1.IsActive);
            Assert.False(child2.IsActive);
        }

        [Fact]
        public void ResetNodeVisualState_ClearsAllFlags()
        {
            var vm = CreateVM();
            var node = new LoggerNode { Name = "test", IsHidden = true, IsActive = true };
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            node.Children.Add(child);

            VmHelper.InvokePrivate(vm, "ResetNodeVisualState", node);
            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_MatchingNode_SetsActive()
        {
            var vm = CreateVM();
            var node = new LoggerNode { Name = "com", FullPath = "com" };
            var child = new LoggerNode { Name = "hp", FullPath = "com.hp" };
            node.Children.Add(child);

            VmHelper.InvokePrivate(vm, "MarkNodeShowOnly", node, "com");
            Assert.True(node.IsActive);
            Assert.False(node.IsHidden);
        }

        [Fact]
        public void MarkNodeShowOnly_NonMatchingNode_SetsHidden()
        {
            var vm = CreateVM();
            var node = new LoggerNode { Name = "other", FullPath = "other" };

            VmHelper.InvokePrivate(vm, "MarkNodeShowOnly", node, "com");
            Assert.True(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void HasAnyColumnFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateVM();
            var result = (bool)VmHelper.InvokePrivate(vm, "HasAnyColumnFilter")!;
            Assert.False(result);
        }

        [Fact]
        public void HasAnyColumnFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var loggerFilters = (List<string>)VmHelper.GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("com.hp");
            var result = (bool)VmHelper.InvokePrivate(vm, "HasAnyColumnFilter")!;
            Assert.True(result);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TestDispatcher for TabOrchestration tests
    // ════════════════════════════════════════════════════════════════════

    file class TestDispatcher : IndiLogs_3._0.Services.Interfaces.IDispatcher
    {
        public void Post(Action action) => action();
        public void Post(Action action, IndiLogs_3._0.Services.Interfaces.DispatchPriority priority) => action();
        public System.Threading.Tasks.Task InvokeAsync(Action action) { action(); return System.Threading.Tasks.Task.CompletedTask; }
    }
}
