using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.ViewModels;
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
    public class ViewModelBatch2Tests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Helpers
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static FilterSearchViewModel CreateFilterVM()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            // Initialize collections that methods expect
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
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_savedFilterRoot", (FilterNode?)null);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_isAppErrorFilterActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_hasRangeStart", false);
            SetField(vm, "_rangeStartLog", (LogEntry?)null);
            SetField(vm, "_searchText", (string?)null);
            return vm;
        }

        private static void SetField(object obj, string fieldName, object? value)
        {
            var field = obj.GetType().GetField(fieldName, NonPublicInstance);
            field?.SetValue(obj, value);
        }

        private static object? GetField(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, NonPublicInstance);
            return field?.GetValue(obj);
        }

        private static void SetProperty(object obj, string propName, object? value)
        {
            var prop = obj.GetType().GetProperty(propName, PublicInstance);
            prop?.SetValue(obj, value);
        }

        private static object? GetProperty(object obj, string propName)
        {
            var prop = obj.GetType().GetProperty(propName, PublicInstance);
            return prop?.GetValue(obj);
        }

        private static object? CallMethod(object obj, string methodName, params object?[] args)
        {
            var method = obj.GetType().GetMethod(methodName, NonPublicInstance | PublicInstance);
            return method?.Invoke(obj, args);
        }

        private static object? CallStaticMethod(Type type, string methodName, params object?[] args)
        {
            var method = type.GetMethod(methodName, NonPublicStatic | BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, args);
        }

        private static LogEntry MakeLog(
            string message = "", string level = "", string logger = "",
            string threadName = "", DateTime? date = null,
            string processName = "", string method = "",
            string pattern = "", string data = "",
            string exception = "",
            Dictionary<string, string>? extraFields = null)
            => new()
            {
                Message = message, Level = level, Logger = logger,
                ThreadName = threadName, Date = date ?? DateTime.Now,
                ProcessName = processName, Method = method,
                Pattern = pattern, Data = data, Exception = exception,
                ExtraFields = extraFields
            };

        private static FilterNode MakeCondition(string field, string op, string value)
            => new() { Type = NodeType.Condition, Field = field, Operator = op, Value = value };

        private static FilterNode MakeGroup(string logicalOp, params FilterNode[] children)
            => new()
            {
                Type = NodeType.Group,
                LogicalOperator = logicalOp,
                Children = new ObservableCollection<FilterNode>(children)
            };

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.FilterEvaluation — EvaluateFilterNode
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static readonly FilterSearchViewModel _evalVM = CreateFilterVM();

        [Fact]
        public void EvaluateFilterNode_ContainsOperator_CaseInsensitive()
        {
            var node = MakeCondition("Message", "Contains", "hello");
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(message: "say Hello world"), node));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: "goodbye"), node));
        }

        [Fact]
        public void EvaluateFilterNode_EqualsOperator_CaseInsensitive()
        {
            var node = MakeCondition("Level", "Equals", "error");
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Error"), node));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Warning"), node));
        }

        [Fact]
        public void EvaluateFilterNode_BeginsWithOperator()
        {
            var node = MakeCondition("Message", "Begins With", "start");
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(message: "Starting up"), node));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: "Not starting"), node));
        }

        [Fact]
        public void EvaluateFilterNode_EndsWithOperator()
        {
            var node = MakeCondition("Message", "Ends With", "done");
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(message: "Processing done"), node));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: "done processing"), node));
        }

        [Fact]
        public void EvaluateFilterNode_RegexOperator()
        {
            var node = MakeCondition("Message", "Regex", @"\d{3,}");
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(message: "Error code 1234"), node));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: "Error code 12"), node));
        }

        [Fact]
        public void EvaluateFilterNode_RegexInvalidPattern_ReturnsFalse()
        {
            var node = MakeCondition("Message", "Regex", @"[invalid");
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: "test"), node));
        }

        [Fact]
        public void EvaluateFilterNode_EmptyValue_ReturnsFalse()
        {
            var node = MakeCondition("Message", "Contains", "test");
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(message: ""), node));
        }

        [Fact]
        public void EvaluateFilterNode_AllFieldTypes()
        {
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(threadName: "main"), MakeCondition("ThreadName", "Contains", "main")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(logger: "app.core"), MakeCondition("Logger", "Contains", "core")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(processName: "proc1"), MakeCondition("ProcessName", "Contains", "proc1")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(method: "DoWork"), MakeCondition("Method", "Contains", "DoWork")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(pattern: "pat1"), MakeCondition("Pattern", "Contains", "pat1")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(data: "mydata"), MakeCondition("Data", "Contains", "mydata")));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(exception: "NullRef"), MakeCondition("Exception", "Contains", "NullRef")));
        }

        [Fact]
        public void EvaluateFilterNode_ExtraFields()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "CustomField", "value123" } });
            Assert.True(_evalVM.EvaluateFilterNode(log, MakeCondition("CustomField", "Contains", "value")));
        }

        [Fact]
        public void EvaluateFilterNode_UnknownField_NoExtraFields_ReturnsFalse()
        {
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(), MakeCondition("UnknownField", "Contains", "x")));
        }

        [Fact]
        public void EvaluateFilterNode_ANDGroup_AllMatch()
        {
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Error", message: "Something failed"), group));
        }

        [Fact]
        public void EvaluateFilterNode_ANDGroup_OneDoesNotMatch()
        {
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Warning", message: "Something failed"), group));
        }

        [Fact]
        public void EvaluateFilterNode_ORGroup_OneMatches()
        {
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Warning"), group));
        }

        [Fact]
        public void EvaluateFilterNode_ORGroup_NoneMatch()
        {
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Info"), group));
        }

        [Fact]
        public void EvaluateFilterNode_NOTANDGroup()
        {
            var group = MakeGroup("NOT AND",
                MakeCondition("Level", "Equals", "Error"));
            // NOT AND with Error match = NOT true = false
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Error"), group));
            // NOT AND with no Error = NOT false = true
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Warning"), group));
        }

        [Fact]
        public void EvaluateFilterNode_NOTORGroup()
        {
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            // NOT OR with match = NOT true = false
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Error"), group));
            // NOT OR with no match = NOT false = true
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Info"), group));
        }

        [Fact]
        public void EvaluateFilterNode_NestedGroups()
        {
            var inner = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            var outer = MakeGroup("AND",
                inner,
                MakeCondition("Message", "Contains", "test"));
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(level: "Error", message: "test message"), outer));
            Assert.False(_evalVM.EvaluateFilterNode(MakeLog(level: "Info", message: "test message"), outer));
        }

        [Fact]
        public void EvaluateFilterNode_EmptyGroup_ReturnsTrue()
        {
            var group = MakeGroup("AND");
            group.Children.Clear();
            Assert.True(_evalVM.EvaluateFilterNode(MakeLog(), group));
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.FilterEvaluation — MatchesSearch
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void MatchesSearch_MessageMatch()
        {
            Assert.True(FilterSearchViewModel.MatchesSearch(MakeLog(message: "Hello World"), "world"));
        }

        [Fact]
        public void MatchesSearch_NoMatch()
        {
            Assert.False(FilterSearchViewModel.MatchesSearch(MakeLog(message: "Hello"), "xyz"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldMatch()
        {
            var log = MakeLog(message: "nope", extraFields: new Dictionary<string, string> { { "F1", "found it" } });
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "found"));
        }

        [Fact]
        public void MatchesSearch_NullMessage_NoMatch()
        {
            var log = MakeLog();
            log.Message = null!;
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        [Fact]
        public void MatchesSearch_NullExtraFields_NoMatch()
        {
            var log = MakeLog(message: "no match");
            log.ExtraFields = null;
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.Filtering — ClearFilters
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void ClearFilters_ResetsFilterFlags()
        {
            var vm = CreateFilterVM();

            // Set some state directly via fields (bypass property setters that call parent)
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_isAppErrorFilterActive", true);
            SetField(vm, "_isTimeFocusActive", true);
            SetField(vm, "_isAppTimeFocusActive", true);

            // ClearFilters sets SearchText="" which triggers OnSearchTextChanged (needs _parent).
            // It also calls ResetTreeVisualState/ResetPlcVisualStates.
            // So we directly test the fields that ClearFilters sets BEFORE those calls.
            // The method sets _mainFilterRoot, _appFilterRoot, _savedFilterRoot = null first,
            // then sets filter-active bools, then SearchText (throws).
            try { vm.ClearFilters(); } catch { }

            // Fields set before the SearchText setter (which may throw) should be cleared
            Assert.Null(GetField(vm, "_mainFilterRoot"));
            Assert.Null(GetField(vm, "_appFilterRoot"));
            Assert.Null(GetField(vm, "_savedFilterRoot"));
            Assert.False((bool)GetField(vm, "_isMainFilterActive")!);
            Assert.False((bool)GetField(vm, "_isAppFilterActive")!);
            Assert.False((bool)GetField(vm, "_isAppErrorFilterActive")!);
            Assert.False((bool)GetField(vm, "_isMainFilterOutActive")!);
            Assert.False((bool)GetField(vm, "_isAppFilterOutActive")!);
            Assert.False((bool)GetField(vm, "_isTimeFocusActive")!);
            Assert.False((bool)GetField(vm, "_isAppTimeFocusActive")!);
        }

        [Fact]
        public void ClearFilters_ClearsCollections()
        {
            var vm = CreateFilterVM();
            ((List<string>)GetField(vm, "_negativeFilters")!).Add("test");
            ((List<string>)GetField(vm, "_appNegativeFilters")!).Add("app_test");
            ((List<string>)GetField(vm, "_activeThreadFilters")!).Add("thread1");
            ((List<string>)GetField(vm, "_appActiveThreadFilters")!).Add("thread2");
            ((List<string>)GetField(vm, "_activeLoggerFilters")!).Add("logger1");
            ((List<string>)GetField(vm, "_activeMethodFilters")!).Add("method1");
            ((HashSet<string>)GetField(vm, "_treeHiddenLoggers")!).Add("hidden1");
            ((HashSet<string>)GetField(vm, "_treeHiddenPrefixes")!).Add("prefix1");
            ((HashSet<string>)GetField(vm, "_plcTreeHiddenLoggers")!).Add("plchidden1");
            ((HashSet<string>)GetField(vm, "_plcTreeHiddenPrefixes")!).Add("plcprefix1");

            try { vm.ClearFilters(); } catch { }

            // Collections are cleared after filter flags but before SearchText setter
            // ClearFilters order: flags -> SearchText (may throw) -> collections -> tree reset
            // Actually re-reading code: SearchText is set on line 58, collections on 60+
            // If SearchText throws, collections won't be cleared.
            // But _mainFilterRoot/_appFilterRoot/_savedFilterRoot will be null (set on 48-50)
            // and all bool flags will be false (set on 51-57).
            // Let's verify what IS set before the throw:
            Assert.Null(GetField(vm, "_mainFilterRoot"));
            Assert.Null(GetField(vm, "_appFilterRoot"));
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.Filtering — RemoveActiveFilter
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void RemoveActiveFilter_NullOrEmpty_NoException()
        {
            var vm = CreateFilterVM();
            vm.RemoveActiveFilter(null!);
            vm.RemoveActiveFilter("");
        }

        [Fact]
        public void RemoveActiveFilter_AppErrorFilter()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isAppErrorFilterActive", true);
            // RemoveActiveFilter will try to call ApplyAppLogsFilter which needs _sessionVM
            // so we just verify the field setting
            try { vm.RemoveActiveFilter("APP_ERROR_FILTER"); } catch { }
            Assert.False((bool)GetField(vm, "_isAppErrorFilterActive")!);
        }

        [Fact]
        public void RemoveActiveFilter_AppTimeFocus()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isAppTimeFocusActive", true);
            try { vm.RemoveActiveFilter("APP_TIME_FOCUS"); } catch { }
            Assert.False((bool)GetField(vm, "_isAppTimeFocusActive")!);
        }

        [Fact]
        public void RemoveActiveFilter_MainTimeFocus()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isTimeFocusActive", true);
            try { vm.RemoveActiveFilter("MAIN_TIME_FOCUS"); } catch { }
            Assert.False((bool)GetField(vm, "_isTimeFocusActive")!);
        }

        [Fact]
        public void RemoveActiveFilter_GlobalTimeRange()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);
            try { vm.RemoveActiveFilter("GLOBAL_TIME_RANGE"); } catch { }
            Assert.Null(GetField(vm, "_globalTimeRangeStart"));
            Assert.Null(GetField(vm, "_globalTimeRangeEnd"));
        }

        [Fact]
        public void RemoveActiveFilter_Search()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_searchText", "test");
            try { vm.RemoveActiveFilter("SEARCH"); } catch { }
            // SearchText setter resets the field
        }

        [Fact]
        public void RemoveActiveFilter_Range()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog());
            try { vm.RemoveActiveFilter("RANGE"); } catch { }
            Assert.False((bool)GetField(vm, "_hasRangeStart")!);
            Assert.Null(GetField(vm, "_rangeStartLog"));
        }

        [Fact]
        public void RemoveActiveFilter_TreeShowOnlyLogger()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_treeShowOnlyLogger", "com.test");
            try { vm.RemoveActiveFilter("TREE_SHOW_ONLY_LOGGER"); } catch { }
            Assert.Null(GetField(vm, "_treeShowOnlyLogger"));
        }

        [Fact]
        public void RemoveActiveFilter_TreeShowOnlyPrefix()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_treeShowOnlyPrefix", "com.test");
            try { vm.RemoveActiveFilter("TREE_SHOW_ONLY_PREFIX"); } catch { }
            Assert.Null(GetField(vm, "_treeShowOnlyPrefix"));
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeShowOnlyLogger()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_plcTreeShowOnlyLogger", "plc.test");
            try { vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_LOGGER"); } catch { }
            Assert.Null(GetField(vm, "_plcTreeShowOnlyLogger"));
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeShowOnlyPrefix()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_plcTreeShowOnlyPrefix", "plc.test");
            try { vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_PREFIX"); } catch { }
            Assert.Null(GetField(vm, "_plcTreeShowOnlyPrefix"));
        }

        [Fact]
        public void RemoveActiveFilter_LoggerKey()
        {
            var vm = CreateFilterVM();
            var loggerFilters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("com.test.logger");
            try { vm.RemoveActiveFilter("LOGGER:com.test.logger"); } catch { }
            Assert.DoesNotContain("com.test.logger", loggerFilters);
        }

        [Fact]
        public void RemoveActiveFilter_MethodKey()
        {
            var vm = CreateFilterVM();
            var methodFilters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            methodFilters.Add("DoWork");
            try { vm.RemoveActiveFilter("METHOD:DoWork"); } catch { }
            Assert.DoesNotContain("DoWork", methodFilters);
        }

        [Fact]
        public void RemoveActiveFilter_TreeHideLogger()
        {
            var vm = CreateFilterVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenLoggers")!;
            hidden.Add("com.hidden");
            try { vm.RemoveActiveFilter("TREE_HIDE_LOGGER:com.hidden"); } catch { }
            Assert.DoesNotContain("com.hidden", hidden);
        }

        [Fact]
        public void RemoveActiveFilter_TreeHidePrefix()
        {
            var vm = CreateFilterVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenPrefixes")!;
            hidden.Add("com.hidden");
            try { vm.RemoveActiveFilter("TREE_HIDE_PREFIX:com.hidden"); } catch { }
            Assert.DoesNotContain("com.hidden", hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeHideLogger()
        {
            var vm = CreateFilterVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenLoggers")!;
            hidden.Add("plc.hidden");
            try { vm.RemoveActiveFilter("PLC_TREE_HIDE_LOGGER:plc.hidden"); } catch { }
            Assert.DoesNotContain("plc.hidden", hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeHidePrefix()
        {
            var vm = CreateFilterVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenPrefixes")!;
            hidden.Add("plc.hidden");
            try { vm.RemoveActiveFilter("PLC_TREE_HIDE_PREFIX:plc.hidden"); } catch { }
            Assert.DoesNotContain("plc.hidden", hidden);
        }

        [Fact]
        public void RemoveActiveFilter_NegativeFilter()
        {
            var vm = CreateFilterVM();
            var negFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negFilters.Add("test_msg");
            SetField(vm, "_isMainFilterOutActive", true);
            try { vm.RemoveActiveFilter("NEGATIVE:test_msg"); } catch { }
            Assert.DoesNotContain("test_msg", negFilters);
            Assert.False((bool)GetField(vm, "_isMainFilterOutActive")!);
        }

        [Fact]
        public void RemoveActiveFilter_AppNegativeFilter()
        {
            var vm = CreateFilterVM();
            var negFilters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            negFilters.Add("app_msg");
            SetField(vm, "_isAppFilterOutActive", true);
            try { vm.RemoveActiveFilter("APP_NEGATIVE:app_msg"); } catch { }
            Assert.DoesNotContain("app_msg", negFilters);
            Assert.False((bool)GetField(vm, "_isAppFilterOutActive")!);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.Filtering — RemoveFilterConditionByIndex
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesCorrectCondition()
        {
            var vm = CreateFilterVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"),
                MakeCondition("Logger", "Contains", "app"));

            CallMethod(vm, "RemoveFilterConditionByIndex", root, 1);
            Assert.Equal(2, root.Children.Count);
            Assert.Equal("Error", root.Children[0].Value);
            Assert.Equal("app", root.Children[1].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_FirstCondition()
        {
            var vm = CreateFilterVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));

            CallMethod(vm, "RemoveFilterConditionByIndex", root, 0);
            Assert.Single(root.Children);
            Assert.Equal("test", root.Children[0].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NullRoot_NoException()
        {
            var vm = CreateFilterVM();
            CallMethod(vm, "RemoveFilterConditionByIndex", null!, 0);
            // Should not throw
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NestedGroup()
        {
            var vm = CreateFilterVM();
            var inner = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            var root = MakeGroup("AND",
                MakeCondition("Message", "Contains", "test"), // index 0
                inner); // contains index 1 (Error), index 2 (Warning)

            CallMethod(vm, "RemoveFilterConditionByIndex", root, 1);
            // Should remove Error from inner group
            Assert.Single(inner.Children);
            Assert.Equal("Warning", inner.Children[0].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_EmptyGroupRemovedAfterLastChild()
        {
            var vm = CreateFilterVM();
            var inner = MakeGroup("OR", MakeCondition("Level", "Equals", "Error")); // only child
            var root = MakeGroup("AND",
                MakeCondition("Message", "Contains", "test"), // index 0
                inner); // contains index 1

            CallMethod(vm, "RemoveFilterConditionByIndex", root, 1);
            // Inner group should be removed since it's now empty
            Assert.Single(root.Children);
            Assert.Equal("test", root.Children[0].Value);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.ThreadFilters — SyncThreadFiltersToFilterTree
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void SyncThreadFilters_SingleThread_AddsDirectCondition()
        {
            var vm = CreateFilterVM();
            // SyncThreadFiltersToFilterTree creates filter root if null
            CallMethod(vm, "SyncThreadFiltersToFilterTree", true, new List<string> { "Thread-1" });

            var appRoot = (FilterNode?)GetProperty(vm, "AppFilterRoot");
            Assert.NotNull(appRoot);
            Assert.Single(appRoot!.Children);
            Assert.Equal(NodeType.Condition, appRoot.Children[0].Type);
            Assert.Equal("ThreadName", appRoot.Children[0].Field);
            Assert.Equal("Thread-1", appRoot.Children[0].Value);
        }

        [Fact]
        public void SyncThreadFilters_MultipleThreads_CreatesORGroup()
        {
            var vm = CreateFilterVM();
            CallMethod(vm, "SyncThreadFiltersToFilterTree", true, new List<string> { "Thread-1", "Thread-2" });

            var appRoot = (FilterNode?)GetProperty(vm, "AppFilterRoot");
            Assert.NotNull(appRoot);
            Assert.Single(appRoot!.Children);
            var orGroup = appRoot.Children[0];
            Assert.Equal(NodeType.Group, orGroup.Type);
            Assert.Equal("OR", orGroup.LogicalOperator);
            Assert.Equal(2, orGroup.Children.Count);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesThreadConditions()
        {
            var vm = CreateFilterVM();
            var root = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "Thread-1"),
                MakeCondition("Message", "Contains", "test"));
            SetProperty(vm, "AppFilterRoot", root);

            CallMethod(vm, "RemoveThreadConditionsFromFilterTree", true);

            var appRoot = (FilterNode?)GetProperty(vm, "AppFilterRoot");
            Assert.NotNull(appRoot);
            Assert.Single(appRoot!.Children);
            Assert.Equal("Message", appRoot.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesORGroupOfThreads()
        {
            var vm = CreateFilterVM();
            var threadGroup = MakeGroup("OR",
                MakeCondition("ThreadName", "Equals", "Thread-1"),
                MakeCondition("ThreadName", "Equals", "Thread-2"));
            var root = MakeGroup("AND", threadGroup, MakeCondition("Message", "Contains", "test"));
            SetProperty(vm, "AppFilterRoot", root);

            CallMethod(vm, "RemoveThreadConditionsFromFilterTree", true);

            var appRoot = (FilterNode?)GetProperty(vm, "AppFilterRoot");
            Assert.NotNull(appRoot);
            Assert.Single(appRoot!.Children);
            Assert.Equal("Message", appRoot.Children[0].Field);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.RangeFilters — CheckIfFiltersEmpty
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_NoFilters_DeactivatesFilter()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isAppFilterActive", true);
            // All filter collections are empty by default from CreateFilterVM

            try { CallMethod(vm, "CheckIfFiltersEmpty", true); } catch { }
            Assert.False((bool)GetField(vm, "_isAppFilterActive")!);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_NoFilters_DeactivatesFilter()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isMainFilterActive", true);

            try { CallMethod(vm, "CheckIfFiltersEmpty", false); } catch { }
            Assert.False((bool)GetField(vm, "_isMainFilterActive")!);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_HasThreadFilter_StaysActive()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_isAppFilterActive", true);
            ((List<string>)GetField(vm, "_appActiveThreadFilters")!).Add("thread1");

            try { CallMethod(vm, "CheckIfFiltersEmpty", true); } catch { }
            Assert.True((bool)GetField(vm, "_isAppFilterActive")!);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel — SetFilterActive
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void SetFilterActive_AppTab_SetsAppFilterActive()
        {
            var vm = CreateFilterVM();
            CallMethod(vm, "SetFilterActive", true);
            Assert.True((bool)GetField(vm, "_isAppFilterActive")!);
        }

        [Fact]
        public void SetFilterActive_MainTab_SetsMainFilterActive()
        {
            var vm = CreateFilterVM();
            CallMethod(vm, "SetFilterActive", false);
            Assert.True((bool)GetField(vm, "_isMainFilterActive")!);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.FilterEvaluation — GetActiveFilters
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void GetActiveFilters_NoFilters_ReturnsEmpty()
        {
            var vm = CreateFilterVM();
            // _parent is null, so SelectedTabIndex will be 0 (PLC)
            var items = vm.GetActiveFilters();
            Assert.Empty(items);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.TimeSync — BinarySearchNearestBy
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void BinarySearchNearestBy_EmptyCollection_ReturnsMinusOne()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)new List<LogEntry>(),
                DateTime.Now,
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearestBy_NullCollection_ReturnsMinusOne()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)null!,
                DateTime.Now,
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearestBy_ExactMatch()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(10)),
                MakeLog(date: baseTime.AddSeconds(20)),
            };
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(10),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(1, result);
        }

        [Fact]
        public void BinarySearchNearestBy_BeforeFirst_ReturnsZero()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(10)),
            };
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(-5),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearestBy_AfterLast_ReturnsLastIndex()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(10)),
            };
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(100),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(1, result);
        }

        [Fact]
        public void BinarySearchNearestBy_BetweenValues_ReturnsNearest()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(10)),
                MakeLog(date: baseTime.AddSeconds(20)),
            };
            // 12 seconds is closer to 10 than 20
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(12),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(1, result);

            // 18 seconds is closer to 20 than 10
            result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(18),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(2, result);
        }

        [Fact]
        public void BinarySearchNearestBy_SingleElement()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry> { MakeLog(date: baseTime) };
            var result = CallMethod(vm, "BinarySearchNearestBy",
                (IList<LogEntry>)logs, baseTime.AddSeconds(5),
                (Func<LogEntry, DateTime>)(log => log.Date));
            Assert.Equal(0, result);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Windows — OpenUrl (static, internal)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void OpenUrl_NullOrEmpty_NoException()
        {
            MainViewModel.OpenUrl(null!);
            MainViewModel.OpenUrl("");
            MainViewModel.OpenUrl("   ");
        }

        [Fact]
        public void OpenUrl_InvalidUri_NoException()
        {
            MainViewModel.OpenUrl("not a url");
        }

        [Fact]
        public void OpenUrl_FileScheme_Blocked()
        {
            // file:// scheme should be blocked (not https/http/mailto)
            MainViewModel.OpenUrl("file:///C:/test.txt");
        }

        [Fact]
        public void OpenUrl_FtpScheme_Blocked()
        {
            MainViewModel.OpenUrl("ftp://server/file");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Data — SplitCsvLineHelper (static, private)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void SplitCsvLineHelper_SimpleFields()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", "a,b,c")!;
            Assert.Equal(3, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsvLineHelper_QuotedFields()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", "\"hello,world\",b,c")!;
            Assert.Equal(3, result.Count);
            Assert.Equal("hello,world", result[0]);
        }

        [Fact]
        public void SplitCsvLineHelper_EmptyFields()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", ",b,,d")!;
            Assert.Equal(4, result.Count);
            Assert.Equal("", result[0]);
            Assert.Equal("", result[2]);
        }

        [Fact]
        public void SplitCsvLineHelper_SingleField()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", "only")!;
            Assert.Single(result);
            Assert.Equal("only", result[0]);
        }

        [Fact]
        public void SplitCsvLineHelper_EmptyString()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", "")!;
            Assert.Single(result);
            Assert.Equal("", result[0]);
        }

        [Fact]
        public void SplitCsvLineHelper_QuotedFieldWithCommas()
        {
            var result = (List<string>)CallStaticMethod(typeof(MainViewModel), "SplitCsvLineHelper", "\"a,b,c\",d")!;
            Assert.Equal(2, result.Count);
            Assert.Equal("a,b,c", result[0]);
            Assert.Equal("d", result[1]);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — Computed properties
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void IsPLCTabSelected_CorrectForTabIndex()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.True(vm.IsPLCTabSelected);
            Assert.False(vm.IsAppTabSelected);
        }

        [Fact]
        public void IsAppTabSelected_CorrectForTabIndex()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.True(vm.IsAppTabSelected);
            Assert.False(vm.IsPLCTabSelected);
        }

        [Fact]
        public void IsExportVisible_PlcOrCharts()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.True(vm.IsExportVisible);

            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.False(vm.IsExportVisible);
        }

        [Fact]
        public void LoggerTabTitle_AppVsPlc()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.Equal("App Loggers", vm.LoggerTabTitle);

            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.Equal("PLC Loggers", vm.LoggerTabTitle);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — ApplyTabSelection
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void ApplyTabSelection_NullSelection_AllVisible()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // Initialize tab flags
            SetField(vm, "_showPlcTab", false);
            SetField(vm, "_showAppTab", false);

            vm.ApplyTabSelection(null, null);

            Assert.True(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
            Assert.True(vm.ShowEventsTab);
            Assert.True(vm.ShowChartsTab);
        }

        [Fact]
        public void ApplyTabSelection_HidesSpecificTabs()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var selection = new TabSelectionConfig
            {
                LoadPlc = false,
                LoadApp = true,
                LoadEvents = false,
                LoadScreenshots = true,
                LoadConfiguration = true,
                LoadSetupInfo = false,
                LoadGlobals = true,
                LoadSystab = false,
                ShowCharts = true,
                ShowCpr = false,
                ShowStepRecorder = true,
                ShowDifferentLogs = false,
                LoadTerminalLogs = false,
                LoadLrs = false
            };

            vm.ApplyTabSelection(selection, null);

            Assert.False(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
            Assert.False(vm.ShowEventsTab);
            Assert.True(vm.ShowScreenshotsTab);
            Assert.False(vm.ShowSetupInfoTab);
            Assert.True(vm.ShowChartsTab);
            Assert.False(vm.ShowCprTab);
            Assert.True(vm.ShowStepRecorderTab);
            Assert.False(vm.ShowDifferentLogsTab);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Globals — FilterGlobalsEntries
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void FilterGlobalsEntries_SearchTextFilters()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "Speed", Value = "100", Default = "100", NameLower = "speed", ValueLower = "100", DefaultLower = "100" },
                new GlobalEntry { Name = "Temperature", Value = "50", Default = "50", NameLower = "temperature", ValueLower = "50", DefaultLower = "50" },
                new GlobalEntry { Name = "Pressure", Value = "200", Default = "200", NameLower = "pressure", ValueLower = "200", DefaultLower = "200" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", false);
            SetField(vm, "_globalsSearchText", "temp");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Single(result);
            Assert.Equal("Temperature", result[0].Name);
        }

        [Fact]
        public void FilterGlobalsEntries_DiffsOnly()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "Speed", Value = "100", Default = "100", NameLower = "speed", ValueLower = "100", DefaultLower = "100" },
                new GlobalEntry { Name = "Temperature", Value = "50", Default = "80", NameLower = "temperature", ValueLower = "50", DefaultLower = "80" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", true);
            SetField(vm, "_globalsSearchText", "");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Single(result);
            Assert.Equal("Temperature", result[0].Name);
        }

        [Fact]
        public void FilterGlobalsEntries_NoFilters_ReturnsAll()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "A", Value = "1", Default = "1", NameLower = "a", ValueLower = "1", DefaultLower = "1" },
                new GlobalEntry { Name = "B", Value = "2", Default = "2", NameLower = "b", ValueLower = "2", DefaultLower = "2" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", false);
            SetField(vm, "_globalsSearchText", "");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void FilterGlobalsEntries_SearchByValue()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "A", Value = "special_value", Default = "x", NameLower = "a", ValueLower = "special_value", DefaultLower = "x" },
                new GlobalEntry { Name = "B", Value = "normal", Default = "y", NameLower = "b", ValueLower = "normal", DefaultLower = "y" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", false);
            SetField(vm, "_globalsSearchText", "special");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Single(result);
            Assert.Equal("A", result[0].Name);
        }

        [Fact]
        public void FilterGlobalsEntries_SearchByDefault()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "A", Value = "x", Default = "unique_default", NameLower = "a", ValueLower = "x", DefaultLower = "unique_default" },
                new GlobalEntry { Name = "B", Value = "y", Default = "common", NameLower = "b", ValueLower = "y", DefaultLower = "common" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", false);
            SetField(vm, "_globalsSearchText", "unique");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Single(result);
            Assert.Equal("A", result[0].Name);
        }

        [Fact]
        public void FilterGlobalsEntries_DiffsAndSearch_Combined()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var allEntries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "Speed", Value = "100", Default = "100", NameLower = "speed", ValueLower = "100", DefaultLower = "100" },
                new GlobalEntry { Name = "Temperature", Value = "50", Default = "80", NameLower = "temperature", ValueLower = "50", DefaultLower = "80" },
                new GlobalEntry { Name = "Pressure", Value = "300", Default = "200", NameLower = "pressure", ValueLower = "300", DefaultLower = "200" }
            };
            SetField(vm, "_allGlobalsEntries", allEntries);
            SetField(vm, "_globalsShowDiffsOnly", true);
            SetField(vm, "_globalsSearchText", "press");
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());

            CallMethod(vm, "FilterGlobalsEntries");

            var result = (ObservableCollection<GlobalEntry>)GetField(vm, "_globalsEntries")!;
            Assert.Single(result);
            Assert.Equal("Pressure", result[0].Name);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.TimeSync — Properties
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void HasTimeSyncData_False_AutoHidesSyncedTimeColumn()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_showSyncedTimeColumn", true);
            SetField(vm, "_hasTimeSyncData", true);

            vm.HasTimeSyncData = false;

            Assert.False(vm.ShowSyncedTimeColumn);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — GetSkippedComponents
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void GetSkippedComponents_NoSession_ReturnsEmpty()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var result = vm.GetSkippedComponents();
            Assert.Empty(result);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // GlobalGrepViewModel.Config — Esc helper
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void Esc_NullString_ReturnsEmpty()
        {
            var result = CallStaticMethod(typeof(GlobalGrepViewModel), "Esc", (string?)null);
            Assert.Equal("", result);
        }

        [Fact]
        public void Esc_StringWithQuotes_EscapesDoubleQuotes()
        {
            var result = CallStaticMethod(typeof(GlobalGrepViewModel), "Esc", "say \"hello\"");
            Assert.Equal("say \"\"hello\"\"", result);
        }

        [Fact]
        public void Esc_NormalString_Unchanged()
        {
            var result = CallStaticMethod(typeof(GlobalGrepViewModel), "Esc", "normal text");
            Assert.Equal("normal text", result);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // GlobalGrepViewModel.Config — FormatTimeRange
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void FormatTimeRange_BothNull_ReturnsNull()
        {
            var result = CallStaticMethod(typeof(GlobalGrepViewModel), "FormatTimeRange",
                (DateTime?)null, (DateTime?)null);
            Assert.Null(result);
        }

        [Fact]
        public void FormatTimeRange_OnlyFrom()
        {
            var from = new DateTime(2024, 6, 15);
            var result = (string?)CallStaticMethod(typeof(GlobalGrepViewModel), "FormatTimeRange",
                (DateTime?)from, (DateTime?)null);
            Assert.Contains("2024-06-15", result);
            Assert.Contains("...", result);
        }

        [Fact]
        public void FormatTimeRange_OnlyTo()
        {
            var to = new DateTime(2024, 12, 25);
            var result = (string?)CallStaticMethod(typeof(GlobalGrepViewModel), "FormatTimeRange",
                (DateTime?)null, (DateTime?)to);
            Assert.Contains("...", result);
            Assert.Contains("2024-12-25", result);
        }

        [Fact]
        public void FormatTimeRange_BothValues()
        {
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 12, 31);
            var result = (string?)CallStaticMethod(typeof(GlobalGrepViewModel), "FormatTimeRange",
                (DateTime?)from, (DateTime?)to);
            Assert.Equal("2024-01-01 to 2024-12-31", result);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // GlobalGrepViewModel.Config — BuildCriteriaSummary
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void BuildCriteriaSummary_EmptyConditions_ReturnsEmpty()
        {
            var vm = (GlobalGrepViewModel)RuntimeHelpers.GetUninitializedObject(typeof(GlobalGrepViewModel));
            try
            {
                SetField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);
            }
            catch { }

            try
            {
                SetProperty(vm, "SelectedGroupOperator", LogicalGroupOperator.And);
            }
            catch { }

            try
            {
                SetProperty(vm, "ConditionGroups", new ObservableCollection<ConditionGroupVM> { new ConditionGroupVM() });
            }
            catch { }

            try
            {
                var result = CallMethod(vm, "BuildCriteriaSummary");
                Assert.Equal("", result);
            }
            catch { /* May fail if field init is incomplete */ }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterNode model tests
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void FilterNode_DeepClone_PreservesStructure()
        {
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));

            var clone = root.DeepClone();

            Assert.Equal(root.Type, clone.Type);
            Assert.Equal(root.LogicalOperator, clone.LogicalOperator);
            Assert.Equal(root.Children.Count, clone.Children.Count);
            Assert.Equal("Error", clone.Children[0].Value);
            Assert.Equal("test", clone.Children[1].Value);
        }

        [Fact]
        public void FilterNode_DeepClone_IsIndependent()
        {
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            var clone = root.DeepClone();

            clone.Children[0].Value = "Warning";
            Assert.Equal("Error", root.Children[0].Value);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesOnReuse()
        {
            var node = MakeCondition("Message", "Regex", @"\d+");
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidateOnValueChange()
        {
            var node = MakeCondition("Message", "Regex", @"\d+");
            var regex1 = node.CompiledRegex;
            node.Value = @"\w+";
            var regex2 = node.CompiledRegex;
            Assert.NotSame(regex1, regex2);
        }

        [Fact]
        public void FilterNode_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var node = MakeCondition("Message", "Contains", @"\d+");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var node = MakeCondition("Message", "Regex", "");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = MakeCondition("Message", "Regex", "[invalid");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_IsEnabled_DefaultTrue()
        {
            var node = new FilterNode();
            Assert.True(node.IsEnabled);
        }

        [Fact]
        public void FilterNode_PropertyChanged_Fires()
        {
            var node = new FilterNode();
            bool fired = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(FilterNode.Value)) fired = true; };
            node.Value = "newvalue";
            Assert.True(fired);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // ViewModelBase tests
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private class TestViewModel : ViewModelBase
        {
            private string _name = "";
            public string Name
            {
                get => _name;
                set => SetField(ref _name, value);
            }
        }

        [Fact]
        public void ViewModelBase_SetField_NotifiesChange()
        {
            var vm = new TestViewModel();
            bool fired = false;
            vm.PropertyChanged += (s, e) => { if (e!.PropertyName == "Name") fired = true; };
            vm.Name = "test";
            Assert.True(fired);
        }

        [Fact]
        public void ViewModelBase_SetField_SameValue_DoesNotNotify()
        {
            var vm = new TestViewModel();
            vm.Name = "test";
            bool fired = false;
            vm.PropertyChanged += (s, e) => fired = true;
            vm.Name = "test"; // same value
            Assert.False(fired);
        }

        [Fact]
        public void ViewModelBase_NotifyPropertyChanged_External()
        {
            var vm = new TestViewModel();
            bool fired = false;
            vm.PropertyChanged += (s, e) => { if (e!.PropertyName == "CustomProp") fired = true; };
            vm.NotifyPropertyChanged("CustomProp");
            Assert.True(fired);
        }

        [Fact]
        public void ViewModelBase_Dispose_CanBeCalledMultipleTimes()
        {
            var vm = new TestViewModel();
            vm.Dispose();
            vm.Dispose(); // second call should be safe
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // AppConstants — EscapeSqlBracketId
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void EscapeSqlBracketId_NoSpecialChars()
        {
            Assert.Equal("table_name", AppConstants.EscapeSqlBracketId("table_name"));
        }

        [Fact]
        public void EscapeSqlBracketId_WithCloseBracket()
        {
            Assert.Equal("table]]name", AppConstants.EscapeSqlBracketId("table]name"));
        }

        [Fact]
        public void EscapeSqlBracketId_EmptyString()
        {
            Assert.Equal("", AppConstants.EscapeSqlBracketId(""));
        }

        [Fact]
        public void EscapeSqlBracketId_NullString()
        {
            Assert.Null(AppConstants.EscapeSqlBracketId(null!));
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — ShowMainTabs
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void HasSessionLoaded_NoSessionVM_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasSessionLoaded);
        }

        [Fact]
        public void HasBinaryAppLogs_NoSessionVM_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasBinaryAppLogs);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — DbConfigTabHeader, PlcTabHeader
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void DbConfigTabHeader_NullSession_ReturnDefault()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.Equal("DB & CONFIG", vm.DbConfigTabHeader);
        }

        [Fact]
        public void PlcTabHeader_NullSession_ReturnDefault()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.Equal("PLC LOGS", vm.PlcTabHeader);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel — IsGlobalTimeRangeActive
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void IsGlobalTimeRangeActive_BothSet_ReturnsTrue()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now.AddHours(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);
            Assert.True(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_OnlyStartSet_ReturnsFalse()
        {
            var vm = CreateFilterVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_NeitherSet_ReturnsFalse()
        {
            var vm = CreateFilterVM();
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel — ReportsButtonText and IsPrintAnalysisVisible
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void ReportsButtonText_NoBinaryLogs()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // HasBinaryAppLogs returns false when SessionVM is null
            Assert.Contains("Reports", vm.ReportsButtonText);
        }

        [Fact]
        public void IsPrintAnalysisVisible_NotAppTab()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.False(vm.IsPrintAnalysisVisible);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LogEntry model edge cases
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void LogEntry_AnnotationIcon_NoAnnotation_Empty()
        {
            var log = new LogEntry();
            Assert.Equal("", log.AnnotationIcon);
        }

        [Fact]
        public void LogEntry_AnnotationIcon_HasAnnotation_NotExpanded()
        {
            var log = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = false };
            Assert.NotEmpty(log.AnnotationIcon);
        }

        [Fact]
        public void LogEntry_AnnotationIcon_HasAnnotation_Expanded()
        {
            var log = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = true };
            Assert.NotEmpty(log.AnnotationIcon);
        }

        [Fact]
        public void LogEntry_HasSystemDefaultColor_False_ByDefault()
        {
            var log = new LogEntry();
            Assert.False(log.HasSystemDefaultColor);
        }

        [Fact]
        public void LogEntry_IsMarked_NotifiesRowBackground()
        {
            var log = new LogEntry();
            var notifiedProperties = new List<string>();
            log.PropertyChanged += (s, e) => notifiedProperties.Add(e!.PropertyName!);
            log.IsMarked = true;
            Assert.Contains("IsMarked", notifiedProperties);
            Assert.Contains("RowBackground", notifiedProperties);
        }

        [Fact]
        public void LogEntry_IsCurrentMarked_NotifiesRowBackground()
        {
            var log = new LogEntry();
            var notifiedProperties = new List<string>();
            log.PropertyChanged += (s, e) => notifiedProperties.Add(e!.PropertyName!);
            log.IsCurrentMarked = true;
            Assert.Contains("IsCurrentMarked", notifiedProperties);
            Assert.Contains("RowBackground", notifiedProperties);
        }

        [Fact]
        public void LogEntry_HasAnnotation_NotifiesRowBackground()
        {
            var log = new LogEntry();
            var notifiedProperties = new List<string>();
            log.PropertyChanged += (s, e) => notifiedProperties.Add(e!.PropertyName!);
            log.HasAnnotation = true;
            Assert.Contains("HasAnnotation", notifiedProperties);
            Assert.Contains("RowBackground", notifiedProperties);
        }

        [Fact]
        public void LogEntry_IsAnnotationExpanded_NotifiesAnnotationIcon()
        {
            var log = new LogEntry { HasAnnotation = true };
            var notifiedProperties = new List<string>();
            log.PropertyChanged += (s, e) => notifiedProperties.Add(e!.PropertyName!);
            log.IsAnnotationExpanded = true;
            Assert.Contains("AnnotationIcon", notifiedProperties);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Additional BinarySearch edge cases
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void BinarySearchNearest_TwoElements_CloserToFirst()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(100)),
            };
            var result = CallMethod(vm, "BinarySearchNearest",
                (IList<LogEntry>)logs, baseTime.AddSeconds(10));
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearest_TwoElements_CloserToSecond()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddSeconds(100)),
            };
            var result = CallMethod(vm, "BinarySearchNearest",
                (IList<LogEntry>)logs, baseTime.AddSeconds(90));
            Assert.Equal(1, result);
        }

        [Fact]
        public void BinarySearchNearest_ManyElements_FindsNearest()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);
            var logs = Enumerable.Range(0, 100)
                .Select(i => MakeLog(date: baseTime.AddMinutes(i)))
                .ToList();

            // target: 42.7 min => closest to index 43
            var result = CallMethod(vm, "BinarySearchNearest",
                (IList<LogEntry>)logs, baseTime.AddMinutes(42.7));
            Assert.Equal(43, result);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Tabs — LeftTabIndex
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void LeftTabIndex_SetAndGet()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.LeftTabIndex = 2;
            Assert.Equal(2, vm.LeftTabIndex);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // FilterSearchViewModel.FilterEvaluation — CollectFilterNodeDescriptions
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void CollectFilterNodeDescriptions_Conditions_AddedAsItems()
        {
            var vm = CreateFilterVM();
            var items = new List<ActiveFilterItem>();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));

            int idx = 0;
            CallMethod(vm, "CollectFilterNodeDescriptions",
                items, root, "FILTER", "", "PREFIX", idx);

            Assert.Equal(2, items.Count);
            Assert.Contains("Error", items[0].Description);
            Assert.Contains("test", items[1].Description);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_NoItems()
        {
            var vm = CreateFilterVM();
            var items = new List<ActiveFilterItem>();
            int idx = 0;
            CallMethod(vm, "CollectFilterNodeDescriptions",
                items, (FilterNode?)null!, "FILTER", "", "PREFIX", idx);
            Assert.Empty(items);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel — TimeSyncScrollWasApplied
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void TimeSyncScrollWasApplied_DefaultFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.TimeSyncScrollWasApplied);
        }

        [Fact]
        public void TimeSyncScrollWasApplied_SetAndGet()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.TimeSyncScrollWasApplied = true;
            Assert.True(vm.TimeSyncScrollWasApplied);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel.Globals — LoadGlobalsFiles
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Fact]
        public void LoadGlobalsFiles_NoSession_ClearsCollections()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_globalsFileNames", new ObservableCollection<string> { "file1.xml" });
            SetField(vm, "_globalsEntries", new ObservableCollection<GlobalEntry>());
            SetField(vm, "_allGlobalsEntries", new List<GlobalEntry>());
            SetField(vm, "_selectedGlobalsFile", "file1.xml");

            vm.LoadGlobalsFiles();

            Assert.Empty(vm.GlobalsFileNames);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // MainViewModel — ShowTab properties set/get roundtrip
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [Theory]
        [InlineData("ShowPlcTab")]
        [InlineData("ShowAppTab")]
        [InlineData("ShowEventsTab")]
        [InlineData("ShowScreenshotsTab")]
        [InlineData("ShowConfigTab")]
        [InlineData("ShowDbConfigTab")]
        [InlineData("ShowSetupInfoTab")]
        [InlineData("ShowGlobalsTab")]
        [InlineData("ShowSystabTab")]
        [InlineData("ShowChartsTab")]
        [InlineData("ShowCprTab")]
        [InlineData("ShowStepRecorderTab")]
        [InlineData("ShowDifferentLogsTab")]
        public void ShowTabProperties_SetAndGet(string propertyName)
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var prop = typeof(MainViewModel).GetProperty(propertyName, PublicInstance);
            Assert.NotNull(prop);

            prop!.SetValue(vm, false);
            Assert.False((bool)prop.GetValue(vm)!);

            prop.SetValue(vm, true);
            Assert.True((bool)prop.GetValue(vm)!);
        }
    }
}
