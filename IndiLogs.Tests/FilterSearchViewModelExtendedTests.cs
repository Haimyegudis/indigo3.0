using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class FilterSearchViewModelExtendedTests
    {
        private static readonly FilterSearchViewModel _vm =
            (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

        private static bool Evaluate(LogEntry log, FilterNode node) =>
            _vm.EvaluateFilterNode(log, node);

        private static LogEntry MakeLog(
            string message = "",
            string level = "",
            string logger = "",
            string threadName = "",
            string processName = "",
            string method = "",
            string pattern = "",
            string data = "",
            string exception = "",
            DateTime? date = null,
            Dictionary<string, string>? extraFields = null) =>
            new()
            {
                Message = message,
                Level = level,
                Logger = logger,
                ThreadName = threadName,
                ProcessName = processName,
                Method = method,
                Pattern = pattern,
                Data = data,
                Exception = exception,
                Date = date ?? DateTime.MinValue,
                ExtraFields = extraFields
            };

        private static FilterNode MakeCondition(string field, string op, string value) =>
            new()
            {
                Type = NodeType.Condition,
                Field = field,
                Operator = op,
                Value = value
            };

        private static FilterNode MakeGroup(string logicalOperator, params FilterNode[] children)
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = logicalOperator,
                Children = new ObservableCollection<FilterNode>(children)
            };
            return node;
        }

        private static FilterSearchViewModel CreateFreshVM()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_filteredLogs", new IndiLogs_3._0.Models.ObservableRangeCollection<LogEntry>());
            SetField(vm, "_appDevLogsFiltered", new IndiLogs_3._0.Models.ObservableRangeCollection<LogEntry>());
            // Pre-set _searchText to "" so ClearFilters (which sets SearchText = "") does not
            // trigger OnSearchTextChanged and access the null _searchDebounceTimer.
            SetField(vm, "_searchText", "");
            return vm;
        }

        private static void SetField(object obj, string fieldName, object? value)
        {
            var field = typeof(FilterSearchViewModel).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(obj, value);
        }

        private static T? GetField<T>(object obj, string fieldName)
        {
            var field = typeof(FilterSearchViewModel).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (T?)field?.GetValue(obj);
        }

        private static readonly MethodInfo _matchesSearchMethod =
            typeof(FilterSearchViewModel).GetMethod("MatchesSearch",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool MatchesSearch(LogEntry log, string search) =>
            (bool)_matchesSearchMethod.Invoke(null, new object[] { log, search })!;

        // ──────────────────────────────────────────────
        // EvaluateFilterNode — Condition operators
        // ──────────────────────────────────────────────

        [Fact]
        public void Evaluate_Contains_CaseInsensitive()
        {
            var log = MakeLog(message: "Motor Temperature High");
            Assert.True(Evaluate(log, MakeCondition("Message", "Contains", "temperature")));
        }

        [Fact]
        public void Evaluate_Contains_NotFound()
        {
            var log = MakeLog(message: "Motor Temperature High");
            Assert.False(Evaluate(log, MakeCondition("Message", "Contains", "voltage")));
        }

        [Fact]
        public void Evaluate_Equals_ExactMatch()
        {
            var log = MakeLog(level: "WARNING");
            Assert.True(Evaluate(log, MakeCondition("Level", "Equals", "warning")));
        }

        [Fact]
        public void Evaluate_Equals_NoMatch()
        {
            var log = MakeLog(level: "INFO");
            Assert.False(Evaluate(log, MakeCondition("Level", "Equals", "Error")));
        }

        [Fact]
        public void Evaluate_BeginsWith_Match()
        {
            var log = MakeLog(logger: "com.hp.indigo.print");
            Assert.True(Evaluate(log, MakeCondition("Logger", "Begins With", "com.hp")));
        }

        [Fact]
        public void Evaluate_BeginsWith_NoMatch()
        {
            var log = MakeLog(logger: "com.hp.indigo.print");
            Assert.False(Evaluate(log, MakeCondition("Logger", "Begins With", "hp.indigo")));
        }

        [Fact]
        public void Evaluate_EndsWith_Match()
        {
            var log = MakeLog(method: "InitializeComponents");
            Assert.True(Evaluate(log, MakeCondition("Method", "Ends With", "components")));
        }

        [Fact]
        public void Evaluate_EndsWith_NoMatch()
        {
            var log = MakeLog(method: "InitializeComponents");
            Assert.False(Evaluate(log, MakeCondition("Method", "Ends With", "Init")));
        }

        [Fact]
        public void Evaluate_Regex_ValidPattern_Match()
        {
            var log = MakeLog(message: "Error at step 15 during calibration");
            Assert.True(Evaluate(log, MakeCondition("Message", "Regex", @"step\s+\d+")));
        }

        [Fact]
        public void Evaluate_Regex_ValidPattern_NoMatch()
        {
            var log = MakeLog(message: "Normal operation");
            Assert.False(Evaluate(log, MakeCondition("Message", "Regex", @"error\s+\d+")));
        }

        [Fact]
        public void Evaluate_Regex_InvalidPattern_ReturnsFalse()
        {
            var log = MakeLog(message: "some text");
            Assert.False(Evaluate(log, MakeCondition("Message", "Regex", "(?P<invalid")));
        }

        [Fact]
        public void Evaluate_Regex_UsesCompiledRegex()
        {
            var node = MakeCondition("Message", "Regex", @"\d{3}");
            var log = MakeLog(message: "Code 456 found");
            Assert.True(Evaluate(log, node));
            Assert.NotNull(node.CompiledRegex);
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_UnknownOperator_FallsBackToContains()
        {
            var log = MakeLog(message: "Hello World");
            Assert.True(Evaluate(log, MakeCondition("Message", "FuzzyMatch", "World")));
        }

        // ──────────────────────────────────────────────
        // EvaluateFilterNode — Field mapping
        // ──────────────────────────────────────────────

        [Fact]
        public void Evaluate_ProcessNameField()
        {
            var log = MakeLog(processName: "IndPrtMngr");
            Assert.True(Evaluate(log, MakeCondition("ProcessName", "Contains", "PrtMngr")));
        }

        [Fact]
        public void Evaluate_PatternField()
        {
            var log = MakeLog(pattern: "State.Transition");
            Assert.True(Evaluate(log, MakeCondition("Pattern", "Begins With", "State")));
        }

        [Fact]
        public void Evaluate_DataField()
        {
            var log = MakeLog(data: "{\"key\":\"value\"}");
            Assert.True(Evaluate(log, MakeCondition("Data", "Contains", "key")));
        }

        [Fact]
        public void Evaluate_ExceptionField()
        {
            var log = MakeLog(exception: "System.NullReferenceException: Object reference");
            Assert.True(Evaluate(log, MakeCondition("Exception", "Contains", "NullReference")));
        }

        [Fact]
        public void Evaluate_ExtraFieldFound()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Station", "S1" } });
            Assert.True(Evaluate(log, MakeCondition("Station", "Equals", "S1")));
        }

        [Fact]
        public void Evaluate_ExtraFieldNotFound_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Station", "S1" } });
            Assert.False(Evaluate(log, MakeCondition("MissingField", "Contains", "any")));
        }

        [Fact]
        public void Evaluate_ExtraFieldNullValue_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Col", null! } });
            Assert.False(Evaluate(log, MakeCondition("Col", "Contains", "x")));
        }

        [Fact]
        public void Evaluate_NoExtraFields_UnknownField_ReturnsFalse()
        {
            var log = MakeLog();
            Assert.False(Evaluate(log, MakeCondition("UnknownField", "Contains", "any")));
        }

        [Fact]
        public void Evaluate_EmptyFieldValue_ReturnsFalse()
        {
            var log = MakeLog(threadName: "");
            Assert.False(Evaluate(log, MakeCondition("ThreadName", "Contains", "any")));
        }

        // ──────────────────────────────────────────────
        // EvaluateFilterNode — Group logic
        // ──────────────────────────────────────────────

        [Fact]
        public void Evaluate_AndGroup_AllTrue()
        {
            var log = MakeLog(message: "error", level: "Error", threadName: "Main");
            var group = MakeGroup("AND",
                MakeCondition("Message", "Contains", "error"),
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("ThreadName", "Equals", "Main"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_AndGroup_OneFails()
        {
            var log = MakeLog(message: "error", level: "Info");
            var group = MakeGroup("AND",
                MakeCondition("Message", "Contains", "error"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_OrGroup_OneMatches()
        {
            var log = MakeLog(level: "Warning");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_OrGroup_NoneMatch()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_NotAnd_InvertsTrue()
        {
            var log = MakeLog(message: "test", level: "Error");
            var group = MakeGroup("NOT AND",
                MakeCondition("Message", "Contains", "test"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_NotAnd_InvertsFalse()
        {
            var log = MakeLog(message: "test", level: "Info");
            var group = MakeGroup("NOT AND",
                MakeCondition("Message", "Contains", "test"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_NotOr_InvertsTrue()
        {
            var log = MakeLog(level: "Error");
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_NotOr_InvertsFalse()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_NullNode_ReturnsTrue()
        {
            Assert.True(Evaluate(MakeLog(), null!));
        }

        [Fact]
        public void Evaluate_GroupWithNullChildren_ReturnsTrue()
        {
            var group = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND", Children = null! };
            Assert.True(Evaluate(MakeLog(), group));
        }

        [Fact]
        public void Evaluate_GroupWithEmptyChildren_ReturnsTrue()
        {
            var group = MakeGroup("OR");
            group.Children.Clear();
            Assert.True(Evaluate(MakeLog(), group));
        }

        // ──────────────────────────────────────────────
        // Nested groups
        // ──────────────────────────────────────────────

        [Fact]
        public void Evaluate_DeeplyNestedGroups()
        {
            var log = MakeLog(message: "motor error", level: "Error", logger: "com.hp.motor");
            var innermost = MakeGroup("AND",
                MakeCondition("Logger", "Begins With", "com.hp"),
                MakeCondition("Message", "Contains", "motor"));
            var middle = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                innermost);
            var outer = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Warning"),
                middle);
            Assert.True(Evaluate(log, outer));
        }

        [Fact]
        public void Evaluate_NestedNotOr()
        {
            var log = MakeLog(level: "Info", message: "ok");
            var notOr = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            var outer = MakeGroup("AND", notOr, MakeCondition("Message", "Contains", "ok"));
            Assert.True(Evaluate(log, outer));
        }

        [Fact]
        public void Evaluate_AndGroup_ShortCircuitsOnFirstFalse()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Regex", "[invalid"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_OrGroup_ShortCircuitsOnFirstTrue()
        {
            var log = MakeLog(message: "hello", level: "Error");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Regex", "[invalid"));
            Assert.True(Evaluate(log, group));
        }

        // ──────────────────────────────────────────────
        // MatchesSearch
        // ──────────────────────────────────────────────

        [Fact]
        public void MatchesSearch_MessageMatch_CaseInsensitive()
        {
            var log = MakeLog(message: "Calibration started at 10:00");
            Assert.True(MatchesSearch(log, "calibration"));
        }

        [Fact]
        public void MatchesSearch_MessageNoMatch()
        {
            var log = MakeLog(message: "Calibration started");
            Assert.False(MatchesSearch(log, "printing"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldMatch()
        {
            var log = MakeLog(message: "no match here",
                extraFields: new Dictionary<string, string> { { "Source", "PrintEngine" }, { "Code", "42" } });
            Assert.True(MatchesSearch(log, "PrintEngine"));
            Assert.True(MatchesSearch(log, "42"));
        }

        [Fact]
        public void MatchesSearch_NullMessageAndNullExtraFields()
        {
            var log = new LogEntry { Message = null! };
            Assert.False(MatchesSearch(log, "x"));
        }

        [Fact]
        public void MatchesSearch_EmptySearch_MatchesAnything()
        {
            var log = MakeLog(message: "something");
            Assert.True(MatchesSearch(log, ""));
        }

        [Fact]
        public void MatchesSearch_NullExtraFieldValue_Skipped()
        {
            var log = MakeLog(message: "no",
                extraFields: new Dictionary<string, string> { { "K", null! } });
            Assert.False(MatchesSearch(log, "search"));
        }

        [Fact]
        public void MatchesSearch_MultipleExtraFields_ChecksAll()
        {
            var log = MakeLog(message: "unrelated",
                extraFields: new Dictionary<string, string>
                {
                    { "A", "alpha" },
                    { "B", "beta" },
                    { "C", "gamma" }
                });
            Assert.True(MatchesSearch(log, "gamma"));
            Assert.False(MatchesSearch(log, "delta"));
        }

        // ──────────────────────────────────────────────
        // BuildLoggerTree
        // ──────────────────────────────────────────────

        [Fact]
        public void BuildLoggerTree_EmptyList_ReturnsEmptyTree()
        {
            var vm = CreateFreshVM();
            vm.BuildLoggerTree(new List<LogEntry>());
            Assert.Empty(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_SingleLogger_CreatesHierarchy()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.hp.indigo"),
                MakeLog(logger: "com.hp.indigo"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
            Assert.Equal("com", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("com", vm.LoggerTreeRoot[0].FullPath);
            Assert.Equal(2, vm.LoggerTreeRoot[0].Count);
            var hp = vm.LoggerTreeRoot[0].Children.First();
            Assert.Equal("hp", hp.Name);
            Assert.Equal("com.hp", hp.FullPath);
            var indigo = hp.Children.First();
            Assert.Equal("indigo", indigo.Name);
            Assert.Equal("com.hp.indigo", indigo.FullPath);
        }

        [Fact]
        public void BuildLoggerTree_MultipleLoggers_SortedAlphabetically()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "Beta.Service"),
                MakeLog(logger: "Alpha.Service"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal(2, vm.LoggerTreeRoot.Count);
            Assert.Equal("Alpha", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("Beta", vm.LoggerTreeRoot[1].Name);
        }

        [Fact]
        public void BuildLoggerTree_SharedPrefixes_MergedCorrectly()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.hp.print"),
                MakeLog(logger: "com.hp.scan"),
                MakeLog(logger: "com.hp.print"),
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
            var hp = vm.LoggerTreeRoot[0].Children.First();
            Assert.Equal(2, hp.Children.Count);
            Assert.Equal("print", hp.Children[0].Name);
            Assert.Equal("scan", hp.Children[1].Name);
            Assert.Equal(2, hp.Children[0].Count);
            Assert.Equal(1, hp.Children[1].Count);
        }

        [Fact]
        public void BuildLoggerTree_NullOrEmptyLoggers_Skipped()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: ""),
                MakeLog(logger: "Valid.Logger"),
                new LogEntry { Logger = null! },
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
            Assert.Equal("Valid", vm.LoggerTreeRoot[0].Name);
        }

        [Fact]
        public void BuildLoggerTree_SinglePartLogger()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry> { MakeLog(logger: "SimpleLogger") };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
            Assert.Equal("SimpleLogger", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("SimpleLogger", vm.LoggerTreeRoot[0].FullPath);
            Assert.Equal(1, vm.LoggerTreeRoot[0].Count);
        }

        [Fact]
        public void BuildLoggerTree_NullInput_ReturnsEmptyTree()
        {
            var vm = CreateFreshVM();
            vm.BuildLoggerTree(null!);
            Assert.Empty(vm.LoggerTreeRoot);
        }

        // ──────────────────────────────────────────────
        // BuildPlcLoggerTree
        // ──────────────────────────────────────────────

        [Fact]
        public void BuildPlcLoggerTree_EmptyList_ReturnsEmptyTree()
        {
            var vm = CreateFreshVM();
            vm.BuildPlcLoggerTree(new List<LogEntry>());
            Assert.Empty(vm.PlcLoggerTreeRoot);
        }

        [Fact]
        public void BuildPlcLoggerTree_BuildsHierarchy()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "PlcMngr.Engine"),
                MakeLog(logger: "PlcMngr.Motor"),
            };
            vm.BuildPlcLoggerTree(logs);
            Assert.Single(vm.PlcLoggerTreeRoot);
            Assert.Equal("PlcMngr", vm.PlcLoggerTreeRoot[0].Name);
            Assert.Equal(2, vm.PlcLoggerTreeRoot[0].Children.Count);
        }

        [Fact]
        public void BuildPlcLoggerTree_CountsAggregated()
        {
            var vm = CreateFreshVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "A.B"),
                MakeLog(logger: "A.B"),
                MakeLog(logger: "A.C"),
            };
            vm.BuildPlcLoggerTree(logs);
            Assert.Equal(3, vm.PlcLoggerTreeRoot[0].Count);
        }

        // ──────────────────────────────────────────────
        // ResetTreeFilters / ResetPlcTreeFilters
        // ──────────────────────────────────────────────

        [Fact]
        public void ResetTreeFilters_ClearsAllState()
        {
            var vm = CreateFreshVM();
            vm.TreeHiddenLoggers.Add("com.hp");
            vm.TreeHiddenPrefixes.Add("com");
            vm.TreeShowOnlyLogger = "com.hp.logger";
            vm.TreeShowOnlyPrefix = "com.hp";
            vm.ResetTreeFilters();
            Assert.Empty(vm.TreeHiddenLoggers);
            Assert.Empty(vm.TreeHiddenPrefixes);
            Assert.Null(vm.TreeShowOnlyLogger);
            Assert.Null(vm.TreeShowOnlyPrefix);
        }

        [Fact]
        public void ResetPlcTreeFilters_ClearsAllState()
        {
            var vm = CreateFreshVM();
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "Logger1" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "Prefix1" });
            SetField(vm, "_plcTreeShowOnlyLogger", "SomeLogger");
            SetField(vm, "_plcTreeShowOnlyPrefix", "SomePrefix");
            vm.ResetPlcTreeFilters();
            Assert.Empty(GetField<HashSet<string>>(vm, "_plcTreeHiddenLoggers")!);
            Assert.Empty(GetField<HashSet<string>>(vm, "_plcTreeHiddenPrefixes")!);
            Assert.Null(GetField<string?>(vm, "_plcTreeShowOnlyLogger"));
            Assert.Null(GetField<string?>(vm, "_plcTreeShowOnlyPrefix"));
        }

        // ──────────────────────────────────────────────
        // ClearFilters
        // ──────────────────────────────────────────────

        [Fact]
        public void ClearFilters_ResetsAllFilterState()
        {
            var vm = CreateFreshVM();
            vm.IsMainFilterActive = true;
            vm.IsAppFilterActive = true;
            vm.IsAppErrorFilterActive = true;
            vm.IsMainFilterOutActive = true;
            vm.IsAppFilterOutActive = true;
            vm.IsTimeFocusActive = true;
            vm.IsAppTimeFocusActive = true;
            vm.NegativeFilters.Add("test");
            vm.AppNegativeFilters.Add("test2");
            vm.ActiveThreadFilters.Add("thread1");
            vm.AppActiveThreadFilters.Add("thread2");
            vm.ActiveLoggerFilters.Add("logger1");
            vm.ActiveMethodFilters.Add("method1");
            vm.TreeHiddenLoggers.Add("hidden1");
            vm.TreeHiddenPrefixes.Add("prefix1");
            vm.TreeShowOnlyLogger = "only";
            vm.TreeShowOnlyPrefix = "prefix";
            vm.ClearFilters();
            Assert.False(vm.IsMainFilterActive);
            Assert.False(vm.IsAppFilterActive);
            Assert.False(vm.IsAppErrorFilterActive);
            Assert.False(vm.IsMainFilterOutActive);
            Assert.False(vm.IsAppFilterOutActive);
            Assert.False(vm.IsTimeFocusActive);
            Assert.False(vm.IsAppTimeFocusActive);
            Assert.Empty(vm.NegativeFilters);
            Assert.Empty(vm.AppNegativeFilters);
            Assert.Empty(vm.ActiveThreadFilters);
            Assert.Empty(vm.AppActiveThreadFilters);
            Assert.Empty(vm.ActiveLoggerFilters);
            Assert.Empty(vm.ActiveMethodFilters);
            Assert.Empty(vm.TreeHiddenLoggers);
            Assert.Empty(vm.TreeHiddenPrefixes);
            Assert.Null(vm.TreeShowOnlyLogger);
            Assert.Null(vm.TreeShowOnlyPrefix);
        }

        [Fact]
        public void ClearFilters_ClearsPlcTreeState()
        {
            var vm = CreateFreshVM();
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "x" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "y" });
            SetField(vm, "_plcTreeShowOnlyLogger", "z");
            SetField(vm, "_plcTreeShowOnlyPrefix", "w");
            vm.ClearFilters();
            Assert.Empty(GetField<HashSet<string>>(vm, "_plcTreeHiddenLoggers")!);
            Assert.Empty(GetField<HashSet<string>>(vm, "_plcTreeHiddenPrefixes")!);
            Assert.Null(GetField<string?>(vm, "_plcTreeShowOnlyLogger"));
            Assert.Null(GetField<string?>(vm, "_plcTreeShowOnlyPrefix"));
        }

        [Fact]
        public void ClearFilters_ClearsCaches()
        {
            var vm = CreateFreshVM();
            vm.LastFilteredCache = new List<LogEntry> { MakeLog() };
            vm.LastFilteredAppCache = new List<LogEntry> { MakeLog() };
            vm.ClearFilters();
            Assert.Null(vm.LastFilteredCache);
            Assert.Null(vm.LastFilteredAppCache);
        }

        // ──────────────────────────────────────────────
        // FilterNode.DeepClone
        // ──────────────────────────────────────────────

        [Fact]
        public void FilterNode_DeepClone_PreservesNestedStructure()
        {
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeGroup("OR",
                    MakeCondition("Message", "Contains", "motor"),
                    MakeCondition("Message", "Contains", "print")));
            var clone = root.DeepClone();
            Assert.Equal(2, clone.Children.Count);
            Assert.Equal(NodeType.Group, clone.Children[1].Type);
            Assert.Equal(2, clone.Children[1].Children.Count);
            Assert.Equal("motor", clone.Children[1].Children[0].Value);
            clone.Children[1].Children[0].Value = "changed";
            Assert.Equal("motor", root.Children[1].Children[0].Value);
        }

        [Fact]
        public void FilterNode_DeepClone_EmptyChildren()
        {
            var node = MakeGroup("AND");
            node.Children.Clear();
            var clone = node.DeepClone();
            Assert.Empty(clone.Children);
        }

        [Fact]
        public void FilterNode_DeepClone_CopiesAllProperties()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Logger",
                Operator = "Begins With",
                Value = "MyApp",
                LogicalOperator = "OR"
            };
            var clone = original.DeepClone();
            Assert.Equal(NodeType.Condition, clone.Type);
            Assert.Equal("Logger", clone.Field);
            Assert.Equal("Begins With", clone.Operator);
            Assert.Equal("MyApp", clone.Value);
            Assert.Equal("OR", clone.LogicalOperator);
        }

        // ──────────────────────────────────────────────
        // FilterNode.CompiledRegex
        // ──────────────────────────────────────────────

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenNotRegexOperator()
        {
            var node = MakeCondition("Message", "Contains", "test");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenEmptyValue()
        {
            var node = MakeCondition("Message", "Regex", "");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenInvalidPattern()
        {
            var node = MakeCondition("Message", "Regex", "[invalid");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesAndReturns()
        {
            var node = MakeCondition("Message", "Regex", @"\d+");
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex1);
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RecompilesOnValueChange()
        {
            var node = MakeCondition("Message", "Regex", @"\d+");
            var regex1 = node.CompiledRegex;
            Assert.NotNull(regex1);
            node.Value = @"\w+";
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex2);
            Assert.NotSame(regex1, regex2);
        }

        // ──────────────────────────────────────────────
        // FilterState properties
        // ──────────────────────────────────────────────

        [Fact]
        public void IsGlobalTimeRangeActive_BothSet_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.GlobalTimeRangeStart = DateTime.Now.AddHours(-1);
            vm.GlobalTimeRangeEnd = DateTime.Now;
            Assert.True(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_OneNull_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            vm.GlobalTimeRangeStart = DateTime.Now;
            vm.GlobalTimeRangeEnd = null;
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_BothNull_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void HasMainStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithAdvancedFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.MainFilterRoot = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithThreadFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveThreadFilters.Add("Thread-1");
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithTimeFocus_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.IsTimeFocusActive = true;
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveLoggerFilters.Add("com.hp.logger");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithMethodFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveMethodFilters.Add("DoWork");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.TreeShowOnlyLogger = "com.hp";
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithHiddenLoggers_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.TreeHiddenLoggers.Add("com.hp.hidden");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeShowOnlyPrefix_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.TreeShowOnlyPrefix = "com.hp";
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithHiddenPrefixes_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.TreeHiddenPrefixes.Add("com");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithAppTimeFocus_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.IsAppTimeFocusActive = true;
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithAppThreadFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.AppActiveThreadFilters.Add("Thread-1");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilterOut_WithNegativeFilters()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.HasMainStoredFilterOut);
            vm.NegativeFilters.Add("noise");
            Assert.True(vm.HasMainStoredFilterOut);
        }

        [Fact]
        public void HasAppStoredFilterOut_WithNegativeFilters()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.HasAppStoredFilterOut);
            vm.AppNegativeFilters.Add("noise");
            Assert.True(vm.HasAppStoredFilterOut);
        }

        // ──────────────────────────────────────────────
        // RemoveFilterConditionByIndex (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesCorrectCondition()
        {
            var vm = CreateFreshVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "motor"),
                MakeCondition("Logger", "Begins With", "com"));
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, 1 });
            Assert.Equal(2, root.Children.Count);
            Assert.Equal("Level", root.Children[0].Field);
            Assert.Equal("Logger", root.Children[1].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesFirstCondition()
        {
            var vm = CreateFreshVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, 0 });
            Assert.Single(root.Children);
            Assert.Equal("Message", root.Children[0].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesFromNestedGroup()
        {
            var vm = CreateFreshVM();
            var inner = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            var root = MakeGroup("AND",
                MakeCondition("Message", "Contains", "test"),
                inner);
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, 2 });
            Assert.Single(inner.Children);
            Assert.Equal("Error", inner.Children[0].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesEmptyGroup()
        {
            var vm = CreateFreshVM();
            var inner = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"));
            var root = MakeGroup("AND",
                MakeCondition("Message", "Contains", "test"),
                inner);
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, 1 });
            Assert.Single(root.Children);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NullRoot_NoException()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object?[] { null, 0 });
        }

        [Fact]
        public void RemoveFilterConditionByIndex_IndexOutOfRange_NoChange()
        {
            var vm = CreateFreshVM();
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, 99 });
            Assert.Single(root.Children);
        }

        // ──────────────────────────────────────────────
        // SyncThreadFiltersToFilterTree (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void SyncThreadFiltersToFilterTree_SingleThread_AddsCondition()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false, new List<string> { "Thread-1" } });
            Assert.NotNull(vm.MainFilterRoot);
            Assert.Single(vm.MainFilterRoot!.Children);
            Assert.Equal("ThreadName", vm.MainFilterRoot.Children[0].Field);
            Assert.Equal("Equals", vm.MainFilterRoot.Children[0].Operator);
            Assert.Equal("Thread-1", vm.MainFilterRoot.Children[0].Value);
        }

        [Fact]
        public void SyncThreadFiltersToFilterTree_MultipleThreads_CreatesOrGroup()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true, new List<string> { "Thread-1", "Thread-2" } });
            Assert.NotNull(vm.AppFilterRoot);
            Assert.Single(vm.AppFilterRoot!.Children);
            var orGroup = vm.AppFilterRoot.Children[0];
            Assert.Equal(NodeType.Group, orGroup.Type);
            Assert.Equal("OR", orGroup.LogicalOperator);
            Assert.Equal(2, orGroup.Children.Count);
        }

        [Fact]
        public void SyncThreadFiltersToFilterTree_ReplacesExistingThreadConditions()
        {
            var vm = CreateFreshVM();
            vm.MainFilterRoot = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "OldThread"),
                MakeCondition("Message", "Contains", "keep"));
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false, new List<string> { "NewThread" } });
            Assert.Equal(2, vm.MainFilterRoot!.Children.Count);
            Assert.True(vm.MainFilterRoot.Children.Any(c => c.Field == "Message"));
            Assert.True(vm.MainFilterRoot.Children.Any(c => c.Field == "ThreadName" && c.Value == "NewThread"));
        }

        [Fact]
        public void SyncThreadFiltersToFilterTree_CreatesRootIfNull()
        {
            var vm = CreateFreshVM();
            vm.AppFilterRoot = null;
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true, new List<string> { "T1" } });
            Assert.NotNull(vm.AppFilterRoot);
            Assert.Equal("AND", vm.AppFilterRoot!.LogicalOperator);
        }

        // ──────────────────────────────────────────────
        // RemoveThreadConditionsFromFilterTree (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesDirectConditions()
        {
            var vm = CreateFreshVM();
            vm.MainFilterRoot = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "Thread-1"),
                MakeCondition("Message", "Contains", "test"));
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            Assert.Single(vm.MainFilterRoot!.Children);
            Assert.Equal("Message", vm.MainFilterRoot.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesThreadOnlyGroups()
        {
            var vm = CreateFreshVM();
            var threadGroup = MakeGroup("OR",
                MakeCondition("ThreadName", "Equals", "T1"),
                MakeCondition("ThreadName", "Equals", "T2"));
            vm.AppFilterRoot = MakeGroup("AND",
                MakeCondition("Message", "Contains", "test"),
                threadGroup);
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.Single(vm.AppFilterRoot!.Children);
            Assert.Equal("Message", vm.AppFilterRoot.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_NullRoot_NoException()
        {
            var vm = CreateFreshVM();
            vm.MainFilterRoot = null;
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_KeepsMixedGroups()
        {
            var vm = CreateFreshVM();
            var mixedGroup = MakeGroup("OR",
                MakeCondition("ThreadName", "Equals", "T1"),
                MakeCondition("Message", "Contains", "important"));
            vm.MainFilterRoot = MakeGroup("AND", mixedGroup);
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            // Mixed group should remain but thread condition inside removed
            Assert.Single(vm.MainFilterRoot!.Children);
            Assert.Single(mixedGroup.Children);
            Assert.Equal("Message", mixedGroup.Children[0].Field);
        }

        // ──────────────────────────────────────────────
        // CollectFilterNodeDescriptions (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void CollectFilterNodeDescriptions_CollectsConditions()
        {
            var vm = CreateFreshVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));
            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { items, root, "FILTER", "", "APP_FILTER", idx });
            Assert.Equal(2, items.Count);
            Assert.Contains("Level Equals \"Error\"", items[0].Description);
            Assert.Equal("APP_FILTER:0", items[0].Key);
            Assert.Contains("Message Contains \"test\"", items[1].Description);
            Assert.Equal("APP_FILTER:1", items[1].Key);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_NoException()
        {
            var vm = CreateFreshVM();
            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object?[] { items, null, "FILTER", "", "APP_FILTER", idx });
            Assert.Empty(items);
        }

        // ──────────────────────────────────────────────
        // LoggerNode tree visual state helpers
        // ──────────────────────────────────────────────

        [Fact]
        public void SetChildrenVisualState_SetsAllDescendants()
        {
            var vm = CreateFreshVM();
            var root = new LoggerNode
            {
                Name = "root",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode
                    {
                        Name = "child1",
                        Children = new ObservableCollection<LoggerNode>
                        {
                            new LoggerNode { Name = "grandchild" }
                        }
                    },
                    new LoggerNode { Name = "child2" }
                }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, true, true });
            Assert.True(root.Children[0].IsHidden);
            Assert.True(root.Children[0].IsActive);
            Assert.True(root.Children[0].Children[0].IsHidden);
            Assert.True(root.Children[0].Children[0].IsActive);
            Assert.True(root.Children[1].IsHidden);
            Assert.True(root.Children[1].IsActive);
        }

        [Fact]
        public void SetChildrenVisualState_ClearsState()
        {
            var vm = CreateFreshVM();
            var root = new LoggerNode
            {
                Name = "root",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode { Name = "child", IsHidden = true, IsActive = true }
                }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root, false, false });
            Assert.False(root.Children[0].IsHidden);
            Assert.False(root.Children[0].IsActive);
        }

        [Fact]
        public void ResetNodeVisualState_ResetsAllDescendants()
        {
            var vm = CreateFreshVM();
            var root = new LoggerNode
            {
                Name = "root",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode { Name = "child", IsHidden = true, IsActive = true }
                }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("ResetNodeVisualState",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { root });
            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(root.Children[0].IsHidden);
            Assert.False(root.Children[0].IsActive);
        }

        // ──────────────────────────────────────────────
        // MarkNodeShowOnly (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void MarkNodeShowOnly_MatchingNode_MarkedActive()
        {
            var vm = CreateFreshVM();
            var node = new LoggerNode
            {
                Name = "hp",
                FullPath = "com.hp",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode { Name = "print", FullPath = "com.hp.print" }
                }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { node, "com.hp" });
            Assert.True(node.IsActive);
            Assert.False(node.IsHidden);
            Assert.True(node.Children[0].IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_AncestorNode_NotHidden()
        {
            var vm = CreateFreshVM();
            var child = new LoggerNode { Name = "print", FullPath = "com.hp.print" };
            var node = new LoggerNode
            {
                Name = "com",
                FullPath = "com",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode
                    {
                        Name = "hp",
                        FullPath = "com.hp",
                        Children = new ObservableCollection<LoggerNode> { child }
                    }
                }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { node, "com.hp.print" });
            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_UnrelatedNode_MarkedHidden()
        {
            var vm = CreateFreshVM();
            var node = new LoggerNode
            {
                Name = "other",
                FullPath = "other.module",
                Children = new ObservableCollection<LoggerNode>()
            };
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { node, "com.hp" });
            Assert.True(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_ChildOfMatch_AlsoActive()
        {
            var vm = CreateFreshVM();
            var grandchild = new LoggerNode { Name = "sub", FullPath = "com.hp.print.sub" };
            var child = new LoggerNode
            {
                Name = "print",
                FullPath = "com.hp.print",
                Children = new ObservableCollection<LoggerNode> { grandchild }
            };
            var node = new LoggerNode
            {
                Name = "hp",
                FullPath = "com.hp",
                Children = new ObservableCollection<LoggerNode> { child }
            };
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { node, "com.hp" });
            Assert.True(child.IsActive);
            Assert.True(grandchild.IsActive);
        }

        // ──────────────────────────────────────────────
        // HasAnyColumnFilter (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void HasAnyColumnFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.False((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveLoggerFilters.Add("com.hp");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithMethodFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveMethodFilters.Add("DoWork");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithThreadFilter_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.ActiveThreadFilters.Add("Main");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithAppTimeFocus_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.IsAppTimeFocusActive = true;
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithAppFilterRoot_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            vm.AppFilterRoot = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        // ──────────────────────────────────────────────
        // CheckIfFiltersEmpty (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_NoFilters_DeactivatesFilter()
        {
            var vm = CreateFreshVM();
            vm.IsAppFilterActive = true;
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.False(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_WithThreadFilter_StaysActive()
        {
            var vm = CreateFreshVM();
            vm.IsAppFilterActive = true;
            vm.AppActiveThreadFilters.Add("Thread-1");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_WithLoggerFilter_StaysActive()
        {
            var vm = CreateFreshVM();
            vm.IsAppFilterActive = true;
            vm.ActiveLoggerFilters.Add("com.hp");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_WithTreeFilter_StaysActive()
        {
            var vm = CreateFreshVM();
            vm.IsAppFilterActive = true;
            vm.TreeHiddenLoggers.Add("hidden");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_NoFilters_DeactivatesFilter()
        {
            var vm = CreateFreshVM();
            vm.IsMainFilterActive = true;
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            Assert.False(vm.IsMainFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_WithAdvancedFilter_StaysActive()
        {
            var vm = CreateFreshVM();
            vm.IsMainFilterActive = true;
            vm.MainFilterRoot = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_WithThreadFilter_StaysActive()
        {
            var vm = CreateFreshVM();
            vm.IsMainFilterActive = true;
            vm.ActiveThreadFilters.Add("T1");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        // ──────────────────────────────────────────────
        // SetFilterActive (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void SetFilterActive_AppTab_SetsAppFilterActive()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void SetFilterActive_MainTab_SetsMainFilterActive()
        {
            var vm = CreateFreshVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        // ──────────────────────────────────────────────
        // IsPlcTreeFilterActive property
        // ──────────────────────────────────────────────

        [Fact]
        public void IsPlcTreeFilterActive_NoFilters_ReturnsFalse()
        {
            var vm = CreateFreshVM();
            Assert.False(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            SetField(vm, "_plcTreeShowOnlyLogger", "SomeLogger");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithShowOnlyPrefix_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            SetField(vm, "_plcTreeShowOnlyPrefix", "SomePrefix");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenLogger_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            GetField<HashSet<string>>(vm, "_plcTreeHiddenLoggers")!.Add("test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenPrefix_ReturnsTrue()
        {
            var vm = CreateFreshVM();
            GetField<HashSet<string>>(vm, "_plcTreeHiddenPrefixes")!.Add("test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        // ──────────────────────────────────────────────
        // IsDefaultLog
        // ──────────────────────────────────────────────

        [Fact]
        public void IsDefaultLog_WithErrorLevel()
        {
            var vm = CreateFreshVM();
            var log = MakeLog(level: "Error", message: "Something went wrong");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_WithManagerThread()
        {
            var vm = CreateFreshVM();
            var log = MakeLog(threadName: "ManagerThread", message: "Status update");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_WithEventsThread()
        {
            var vm = CreateFreshVM();
            var log = MakeLog(threadName: "Events", message: "Event occurred");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_PlcMngrMessage()
        {
            var vm = CreateFreshVM();
            var log = MakeLog(message: "PlcMngr: initialized");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_NonMatchingLog()
        {
            var vm = CreateFreshVM();
            var log = MakeLog(level: "Info", threadName: "Worker-1", message: "Normal log line");
            Assert.False(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_WithCustomDefaultFilter()
        {
            var vm = CreateFreshVM();
            vm.DefaultPlcFilter = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Debug"));
            var log = MakeLog(level: "Debug");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_CustomFilterOverridesFactory()
        {
            var vm = CreateFreshVM();
            vm.DefaultPlcFilter = MakeGroup("AND",
                MakeCondition("Message", "Contains", "specific"));
            // This log matches factory filter (Error level) but NOT custom
            var log = MakeLog(level: "Error", message: "generic error");
            Assert.False(vm.IsDefaultLog(log));
        }

        // ──────────────────────────────────────────────
        // LoggerNode model tests
        // ──────────────────────────────────────────────

        [Fact]
        public void LoggerNode_DisplayText_ShowsNameAndCount()
        {
            var node = new LoggerNode { Name = "com", Count = 42 };
            Assert.Equal("com (42)", node.DisplayText);
        }

        [Fact]
        public void LoggerNode_DefaultValues()
        {
            var node = new LoggerNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.FullPath);
            Assert.Equal(0, node.Count);
            Assert.False(node.IsExpanded);
            Assert.False(node.IsSelected);
            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
            Assert.Empty(node.Children);
        }

        [Fact]
        public void LoggerNode_PropertyChangedFired()
        {
            var node = new LoggerNode();
            var changed = new List<string>();
            node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            node.IsHidden = true;
            node.IsActive = true;
            node.IsExpanded = true;
            node.IsSelected = true;
            Assert.Contains("IsHidden", changed);
            Assert.Contains("IsActive", changed);
            Assert.Contains("IsExpanded", changed);
            Assert.Contains("IsSelected", changed);
        }

        // ──────────────────────────────────────────────
        // FilterNode model tests
        // ──────────────────────────────────────────────

        [Fact]
        public void FilterNode_DefaultValues()
        {
            var node = new FilterNode();
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("", node.Value);
            Assert.True(node.IsEnabled);
            Assert.Empty(node.Children);
        }

        [Fact]
        public void FilterNode_PropertyChangedFired()
        {
            var node = new FilterNode();
            var changed = new List<string>();
            node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            node.Type = NodeType.Condition;
            node.LogicalOperator = "OR";
            node.Field = "Level";
            node.Operator = "Equals";
            node.Value = "Error";
            Assert.Contains("Type", changed);
            Assert.Contains("LogicalOperator", changed);
            Assert.Contains("Field", changed);
            Assert.Contains("Operator", changed);
            Assert.Contains("Value", changed);
        }

        [Fact]
        public void FilterNode_SettingValue_ResetsCompiledRegex()
        {
            var node = MakeCondition("Message", "Regex", @"\d+");
            var regex = node.CompiledRegex;
            Assert.NotNull(regex);
            node.Value = @"\w+";
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex2);
            Assert.NotSame(regex, regex2);
        }

        // ──────────────────────────────────────────────
        // ActiveFilterItem model tests
        // ──────────────────────────────────────────────

        [Fact]
        public void ActiveFilterItem_DefaultValues()
        {
            var item = new ActiveFilterItem();
            Assert.Equal("", item.Category);
            Assert.Equal("", item.Description);
            Assert.False(item.IsActive);
            Assert.Equal("", item.Key);
            Assert.Null(item.ColorBrush);
        }

        // ──────────────────────────────────────────────
        // All fields via Theory
        // ──────────────────────────────────────────────

        [Theory]
        [InlineData("Level", "Error", true)]
        [InlineData("ThreadName", "Main", true)]
        [InlineData("Logger", "com.hp", true)]
        [InlineData("ProcessName", "MyProc", true)]
        [InlineData("Method", "Init", true)]
        [InlineData("Pattern", "State", true)]
        [InlineData("Data", "payload", true)]
        [InlineData("Exception", "NullRef", true)]
        [InlineData("Message", "hello", true)]
        public void Evaluate_AllFields_ContainsOperator(string field, string value, bool expected)
        {
            var log = MakeLog(
                level: "Error",
                threadName: "Main",
                logger: "com.hp",
                processName: "MyProc",
                method: "Init",
                pattern: "State",
                data: "payload",
                exception: "NullRef",
                message: "hello");
            Assert.Equal(expected, Evaluate(log, MakeCondition(field, "Contains", value)));
        }

        [Fact]
        public void Evaluate_Regex_CompiledRegex_AnchoredPattern()
        {
            var node = MakeCondition("Message", "Regex", @"^Error\s");
            _ = node.CompiledRegex;
            var log = MakeLog(message: "Error at step 5");
            Assert.True(Evaluate(log, node));
            var log2 = MakeLog(message: "No Error at step 5");
            Assert.False(Evaluate(log2, node));
        }

        // ──────────────────────────────────────────────
        // Filter property setters
        // ──────────────────────────────────────────────

        [Fact]
        public void FilterProperties_SettersWork()
        {
            var vm = CreateFreshVM();
            vm.MainFilterRoot = MakeGroup("AND");
            Assert.NotNull(vm.MainFilterRoot);
            vm.AppFilterRoot = MakeGroup("OR");
            Assert.NotNull(vm.AppFilterRoot);
            vm.SavedFilterRoot = MakeGroup("AND");
            Assert.NotNull(vm.SavedFilterRoot);
            vm.DefaultPlcFilter = MakeGroup("AND");
            Assert.NotNull(vm.DefaultPlcFilter);
        }

        [Fact]
        public void LastFilteredCache_SetterWorks()
        {
            var vm = CreateFreshVM();
            Assert.Null(vm.LastFilteredCache);
            var logs = new List<LogEntry> { MakeLog(message: "test") };
            vm.LastFilteredCache = logs;
            Assert.Same(logs, vm.LastFilteredCache);
        }

        [Fact]
        public void LastFilteredAppCache_SetterWorks()
        {
            var vm = CreateFreshVM();
            Assert.Null(vm.LastFilteredAppCache);
            var logs = new List<LogEntry> { MakeLog(message: "test") };
            vm.LastFilteredAppCache = logs;
            Assert.Same(logs, vm.LastFilteredAppCache);
        }

        // ──────────────────────────────────────────────
        // ResetPlcVisualStates (private)
        // ──────────────────────────────────────────────

        [Fact]
        public void ResetPlcVisualStates_ResetsAllNodes()
        {
            var vm = CreateFreshVM();
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var root = new LoggerNode
            {
                Name = "root",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };
            vm.PlcLoggerTreeRoot = new ObservableCollection<LoggerNode> { root };
            var method = typeof(FilterSearchViewModel).GetMethod("ResetPlcVisualStates",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(vm, null);
            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        // ──────────────────────────────────────────────
        // MarkAllNodesShowOnly (overload with treeRoot)
        // ──────────────────────────────────────────────

        [Fact]
        public void MarkAllNodesShowOnly_WithTreeRoot_MarksCorrectly()
        {
            var vm = CreateFreshVM();
            var match = new LoggerNode { Name = "target", FullPath = "com.target" };
            var other = new LoggerNode { Name = "other", FullPath = "other" };
            var treeRoot = new ObservableCollection<LoggerNode> { match, other };
            var method = typeof(FilterSearchViewModel).GetMethod("MarkAllNodesShowOnly",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(ObservableCollection<LoggerNode>) },
                null)!;
            method.Invoke(vm, new object[] { "com.target", treeRoot });
            Assert.True(match.IsActive);
            Assert.False(match.IsHidden);
            Assert.True(other.IsHidden);
            Assert.False(other.IsActive);
        }

        // ──────────────────────────────────────────────
        // Evaluate with AND containing single child
        // ──────────────────────────────────────────────

        [Fact]
        public void Evaluate_AndGroup_SingleChild_Match()
        {
            var log = MakeLog(level: "Error");
            var group = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_OrGroup_SingleChild_Match()
        {
            var log = MakeLog(level: "Error");
            var group = MakeGroup("OR", MakeCondition("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void Evaluate_OrGroup_SingleChild_NoMatch()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("OR", MakeCondition("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, group));
        }
    }
}
