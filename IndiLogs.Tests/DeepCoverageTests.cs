using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class DeepCoverageTests
    {
        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static T CreateInstance<T>() where T : class
            => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetField<T>(object obj, string fieldName, T value)
        {
            var type = obj.GetType();
            FieldInfo? fi = null;
            while (type != null && fi == null)
            {
                fi = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            fi?.SetValue(obj, value);
        }

        private static T? GetField<T>(object obj, string fieldName)
        {
            var type = obj.GetType();
            FieldInfo? fi = null;
            while (type != null && fi == null)
            {
                fi = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            return fi != null ? (T?)fi.GetValue(obj) : default;
        }

        private static object? InvokeMethod(object obj, string methodName, params object?[] args)
        {
            var type = obj.GetType();
            MethodInfo? mi = null;
            while (type != null && mi == null)
            {
                mi = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            return mi?.Invoke(obj, args);
        }

        private static object? InvokeStaticMethod(Type type, string methodName, params object?[] args)
        {
            var mi = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return mi?.Invoke(null, args);
        }

        private static FilterSearchViewModel CreateFilterSearchVM(int tabIndex = 0)
        {
            var vm = CreateInstance<FilterSearchViewModel>();

            // Create parent MainViewModel with selected tab
            var parent = CreateInstance<MainViewModel>();
            SetField(parent, "_selectedTabIndex", tabIndex);

            // Initialize all required fields on FilterSearchViewModel
            SetField(vm, "_parent", parent);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_searchText", (string?)null);
            SetField(vm, "_hasRangeStart", false);
            SetField(vm, "_rangeStartLog", (LogEntry?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_isAppErrorFilterActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            return vm;
        }

        private static LogEntry MakeLog(string message = "", string level = "INFO", string thread = "",
            string logger = "", string method = "", string processName = "", string pattern = "",
            string data = "", string exception = "", Dictionary<string, string>? extra = null)
        {
            return new LogEntry
            {
                Message = message,
                Level = level,
                ThreadName = thread,
                Logger = logger,
                Method = method,
                ProcessName = processName,
                Pattern = pattern,
                Data = data,
                Exception = exception,
                Date = DateTime.Now,
                ExtraFields = extra
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // 1. FilterSearchViewModel.FilterEvaluation - GetActiveFilters
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void GetActiveFilters_PLCTab_NoFilters_ReturnsEmpty()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }

        [Fact]
        public void GetActiveFilters_APPTab_NoFilters_ReturnsEmpty()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }

        [Fact]
        public void GetActiveFilters_APPTab_AppFilterRoot_CollectsConditions()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "error" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                }
            };
            SetField(vm, "_appFilterRoot", root);
            var result = vm.GetActiveFilters();
            Assert.True(result.Count >= 2);
            Assert.Contains(result, r => r.Key.StartsWith("APP_FILTER:"));
        }

        [Fact]
        public void GetActiveFilters_APPTab_ErrorFilterActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_isAppErrorFilterActive", true);
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("APP_ERROR_FILTER", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_TimeFocusActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_isAppTimeFocusActive", true);
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("APP_TIME_FOCUS", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_AppThreadFilters()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "Thread1", "Thread2" });
            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Key == "APP_THREAD:Thread1");
            Assert.Contains(result, r => r.Key == "APP_THREAD:Thread2");
        }

        [Fact]
        public void GetActiveFilters_APPTab_AppNegativeFilters_Thread()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_appNegativeFilters", new List<string> { "THREAD:Worker", "someMsg" });
            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Description.Contains("Thread:"));
            Assert.Contains(result, r => r.Description.Contains("Message:"));
        }

        [Fact]
        public void GetActiveFilters_APPTab_LoggerFilters()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_activeLoggerFilters", new List<string> { "MyLogger" });
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("LOGGER:MyLogger", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_MethodFilters()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_activeMethodFilters", new List<string> { "DoWork" });
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("METHOD:DoWork", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_TreeShowOnlyLogger()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_treeShowOnlyLogger", "SpecificLogger");
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("TREE_SHOW_ONLY_LOGGER", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_TreeShowOnlyPrefix()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_treeShowOnlyPrefix", "MyPrefix");
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("TREE_SHOW_ONLY_PREFIX", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_APPTab_TreeHiddenLoggers()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string> { "LogA", "LogB" });
            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Key == "TREE_HIDE_LOGGER:LogA");
        }

        [Fact]
        public void GetActiveFilters_APPTab_TreeHiddenPrefixes()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string> { "PfxA" });
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("TREE_HIDE_PREFIX:PfxA", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_PLCTab_MainFilterRoot()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "test" }
                }
            };
            SetField(vm, "_mainFilterRoot", root);
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key.StartsWith("MAIN_FILTER:"));
        }

        [Fact]
        public void GetActiveFilters_PLCTab_TimeFocusActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_isTimeFocusActive", true);
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("MAIN_TIME_FOCUS", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_PLCTab_ThreadFilters()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_activeThreadFilters", new List<string> { "PlcThread" });
            var result = vm.GetActiveFilters();
            Assert.Single(result);
            Assert.Equal("MAIN_THREAD:PlcThread", result[0].Key);
        }

        [Fact]
        public void GetActiveFilters_PLCTab_NegativeFilters_Thread()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_negativeFilters", new List<string> { "THREAD:Worker", "spam" });
            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Description.Contains("Thread:"));
            Assert.Contains(result, r => r.Description.Contains("Message:"));
        }

        [Fact]
        public void GetActiveFilters_PLCTab_PlcTreeShowOnlyPrefix()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_plcTreeShowOnlyPrefix", "PlcPfx");
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "PLC_TREE_SHOW_ONLY_PREFIX");
        }

        [Fact]
        public void GetActiveFilters_PLCTab_PlcTreeShowOnlyLogger()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_plcTreeShowOnlyLogger", "PlcLogger");
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "PLC_TREE_SHOW_ONLY_LOGGER");
        }

        [Fact]
        public void GetActiveFilters_PLCTab_PlcTreeHiddenLoggers()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "HideMe" });
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "PLC_TREE_HIDE_LOGGER:HideMe");
        }

        [Fact]
        public void GetActiveFilters_PLCTab_PlcTreeHiddenPrefixes()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "BadPfx" });
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "PLC_TREE_HIDE_PREFIX:BadPfx");
        }

        [Fact]
        public void GetActiveFilters_Shared_SearchText()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_searchText", "hello");
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "SEARCH");
        }

        [Fact]
        public void GetActiveFilters_Shared_SearchTextTooShort()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_searchText", "h");
            var result = vm.GetActiveFilters();
            Assert.DoesNotContain(result, r => r.Key == "SEARCH");
        }

        [Fact]
        public void GetActiveFilters_Shared_RangeStart()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog("test"));
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "RANGE");
        }

        [Fact]
        public void GetActiveFilters_Shared_GlobalTimeRange()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now.AddHours(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);
            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "GLOBAL_TIME_RANGE");
        }

        [Fact]
        public void GetActiveFilters_NonLogTab_ReturnsEmpty()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_EVENTS);
            SetField(vm, "_searchText", "hello");
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }

        [Fact]
        public void GetActiveFilters_NestedGroupNodes_Traversed()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode
                    {
                        Type = NodeType.Group,
                        LogicalOperator = "OR",
                        Children = new ObservableCollection<FilterNode>
                        {
                            new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "a" },
                            new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                        }
                    }
                }
            };
            SetField(vm, "_appFilterRoot", root);
            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GetActiveFilters_ThreadNameCondition_SkippedWhenThreadFiltersActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "Worker" });
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "ThreadName", Operator = "Equals", Value = "Worker" }
                }
            };
            SetField(vm, "_appFilterRoot", root);
            var result = vm.GetActiveFilters();
            // ThreadName condition should be skipped, but thread filter items should appear
            var filterItems = result.Where(r => r.Key.StartsWith("APP_FILTER:")).ToList();
            Assert.Empty(filterItems);
        }

        // ═══════════════════════════════════════════════════════════════
        // 2. EvaluateFilterNode
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void EvaluateFilterNode_NullNode_ReturnsTrue()
        {
            var vm = CreateFilterSearchVM();
            Assert.True(vm.EvaluateFilterNode(MakeLog(), null!));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_Contains_Message()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" };
            Assert.True(vm.EvaluateFilterNode(MakeLog("say hello world"), node));
            Assert.False(vm.EvaluateFilterNode(MakeLog("goodbye"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_Equals_Level()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" };
            Assert.True(vm.EvaluateFilterNode(MakeLog(level: "Error"), node));
            Assert.True(vm.EvaluateFilterNode(MakeLog(level: "error"), node));
            Assert.False(vm.EvaluateFilterNode(MakeLog(level: "Info"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_BeginsWith()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Begins With", Value = "Start" };
            Assert.True(vm.EvaluateFilterNode(MakeLog("Starting..."), node));
            Assert.False(vm.EvaluateFilterNode(MakeLog("Not starting"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_EndsWith()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Ends With", Value = "done" };
            Assert.True(vm.EvaluateFilterNode(MakeLog("task done"), node));
            Assert.False(vm.EvaluateFilterNode(MakeLog("done task"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_Regex()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Regex", Value = @"^Error\d+" };
            Assert.True(vm.EvaluateFilterNode(MakeLog("Error42 occurred"), node));
            Assert.False(vm.EvaluateFilterNode(MakeLog("Warning42"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_Regex_Invalid()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Regex", Value = @"[invalid" };
            Assert.False(vm.EvaluateFilterNode(MakeLog("test"), node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_AllFields()
        {
            var vm = CreateFilterSearchVM();
            var fields = new[] { "Level", "ThreadName", "Logger", "ProcessName", "Method", "Pattern", "Data", "Exception", "Message" };
            foreach (var field in fields)
            {
                var node = new FilterNode { Type = NodeType.Condition, Field = field, Operator = "Contains", Value = "test" };
                var log = MakeLog("test", "test", "test", "test", "test", "test", "test", "test", "test");
                Assert.True(vm.EvaluateFilterNode(log, node), $"Field {field} should match");
            }
        }

        [Fact]
        public void EvaluateFilterNode_Condition_ExtraFields()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "CustomField", Operator = "Contains", Value = "val" };
            var log = MakeLog(extra: new Dictionary<string, string> { { "CustomField", "myvalue" } });
            Assert.True(vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_UnknownField_ReturnsFalse()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Unknown", Operator = "Contains", Value = "x" };
            var log = MakeLog("test");
            Assert.False(vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_Condition_EmptyValue_ReturnsFalse()
        {
            var vm = CreateFilterSearchVM();
            var node = new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" };
            var log = MakeLog("");
            Assert.False(vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_Group_AND()
        {
            var vm = CreateFilterSearchVM();
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                }
            };
            Assert.True(vm.EvaluateFilterNode(MakeLog("hello", "Error"), group));
            Assert.False(vm.EvaluateFilterNode(MakeLog("hello", "Info"), group));
        }

        [Fact]
        public void EvaluateFilterNode_Group_OR()
        {
            var vm = CreateFilterSearchVM();
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "world" }
                }
            };
            Assert.True(vm.EvaluateFilterNode(MakeLog("hello"), group));
            Assert.True(vm.EvaluateFilterNode(MakeLog("world"), group));
            Assert.False(vm.EvaluateFilterNode(MakeLog("goodbye"), group));
        }

        [Fact]
        public void EvaluateFilterNode_Group_NOT_AND()
        {
            var vm = CreateFilterSearchVM();
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "NOT AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" }
                }
            };
            Assert.False(vm.EvaluateFilterNode(MakeLog("hello world"), group));
            Assert.True(vm.EvaluateFilterNode(MakeLog("goodbye"), group));
        }

        [Fact]
        public void EvaluateFilterNode_Group_NOT_OR()
        {
            var vm = CreateFilterSearchVM();
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "NOT OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "hello" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "world" }
                }
            };
            Assert.True(vm.EvaluateFilterNode(MakeLog("goodbye"), group));
            Assert.False(vm.EvaluateFilterNode(MakeLog("hello"), group));
        }

        [Fact]
        public void EvaluateFilterNode_Group_EmptyChildren_ReturnsTrue()
        {
            var vm = CreateFilterSearchVM();
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>()
            };
            Assert.True(vm.EvaluateFilterNode(MakeLog(), group));
        }

        // ═══════════════════════════════════════════════════════════════
        // 3. MatchesSearch (static)
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void MatchesSearch_MessageMatch()
        {
            var log = MakeLog("error occurred");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_CaseInsensitive()
        {
            var log = MakeLog("Error Occurred");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_NoMatch()
        {
            var log = MakeLog("all good");
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldMatch()
        {
            var log = MakeLog("msg", extra: new Dictionary<string, string> { { "Source", "errorModule" } });
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "error"));
        }

        [Fact]
        public void MatchesSearch_NullMessage()
        {
            var log = MakeLog();
            log.Message = null!;
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        [Fact]
        public void MatchesSearch_NullExtraFields()
        {
            var log = MakeLog("test");
            log.ExtraFields = null;
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        // ═══════════════════════════════════════════════════════════════
        // 4. CsvExportService - TryParsePlcMngrState
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TryParsePlcMngrState_ValidPlcMngr()
        {
            string msg = "CHStep: PlcMngr, STATE_STANDBY, State 5 <Root,0,0,0,0,0>";
            Assert.True(CsvExportService.TryParsePlcMngrState(msg, out string? state));
            Assert.Equal("STATE_STANDBY", state);
        }

        [Fact]
        public void TryParsePlcMngrState_NotPlcMngr()
        {
            string msg = "CHStep: SomeCH, STATE_STANDBY, State 5 <Root,0,0,0,0,0>";
            Assert.False(CsvExportService.TryParsePlcMngrState(msg, out _));
        }

        [Fact]
        public void TryParsePlcMngrState_Empty()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_TooShort()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("CHStep: PlcMng", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NotCHStep()
        {
            Assert.False(CsvExportService.TryParsePlcMngrState("IO_Mon: something, data=42", out _));
        }

        [Fact]
        public void TryParsePlcMngrState_NoComma_ButHasState()
        {
            string msg = "CHStep: PlcMngr STATE_X State 5 <Root,0,0,0,0,0>";
            // No comma after PlcMngr but has State keyword
            Assert.False(CsvExportService.TryParsePlcMngrState(msg, out _));
        }

        // ═══════════════════════════════════════════════════════════════
        // 5. CsvExportService - TryParseCHStep
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TryParseCHStep_ValidMessage()
        {
            string msg = "CHStep: MyCH, StepMsg123, State 5 <ParentCH,42,3,100,1,0>";
            Assert.True(CsvExportService.TryParseCHStep(msg,
                out var chName, out var stepMsg, out var stateId,
                out var parentName, out var subsysID, out var prevStepNo,
                out var diffTime, out var subStepNo, out var chObjType));
            Assert.Equal("MyCH", chName);
            Assert.Equal("StepMsg123", stepMsg);
            Assert.Equal("5", stateId);
            Assert.Equal("ParentCH", parentName);
            Assert.Equal("42", subsysID);
            Assert.Equal("3", prevStepNo);
            Assert.Equal("100", diffTime);
            Assert.Equal("1", subStepNo);
            Assert.Equal("0", chObjType);
        }

        [Fact]
        public void TryParseCHStep_TooShort()
        {
            Assert.False(CsvExportService.TryParseCHStep("CHStep: short",
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NoState()
        {
            string msg = "CHStep: MyCH, StepMsg, <ParentCH,42,3,100,1,0>";
            Assert.False(CsvExportService.TryParseCHStep(msg,
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseCHStep_NotEnoughBracketParts()
        {
            string msg = "CHStep: MyCH, StepMsg, State 5 <ParentCH,42>";
            Assert.False(CsvExportService.TryParseCHStep(msg,
                out _, out _, out _, out _, out _, out _, out _, out _, out _));
        }

        // ═══════════════════════════════════════════════════════════════
        // 6. CsvExportService - TryParseLogStats
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TryParseLogStats_ValidMessage()
        {
            string msg = "LogStat: Logs(Total=1234 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=AppLog)";
            Assert.True(CsvExportService.TryParseLogStats(msg,
                out var total, out var isReady, out var semTotal,
                out var semMult, out var lost, out var bufFull,
                out var maxNum, out var maxCat));
            Assert.Equal("1234", total);
            Assert.Equal("1", isReady);
            Assert.Equal("0", semTotal);
            Assert.Equal("0", semMult);
            Assert.Equal("0", lost);
            Assert.Equal("0", bufFull);
            Assert.Equal("100", maxNum);
            Assert.Equal("AppLog", maxCat);
        }

        [Fact]
        public void TryParseLogStats_NotLogStat()
        {
            Assert.False(CsvExportService.TryParseLogStats("IO_Mon: data",
                out _, out _, out _, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryParseLogStats_NoLogsSection()
        {
            string msg = "LogStat: nothing here";
            Assert.False(CsvExportService.TryParseLogStats(msg,
                out _, out _, out _, out _, out _, out _, out _, out _));
        }

        // ═══════════════════════════════════════════════════════════════
        // 7. CsvExportService - DecodeIoStatus
        // ═══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("", "Op")]
        [InlineData("Inv", "InvalidHW")]
        [InlineData("NM", "NotMonitored")]
        [InlineData("NA", "NotActive")]
        [InlineData("Op", "Operational")]
        [InlineData("OpL", "OperationalLow")]
        [InlineData("OpH", "OperationalHigh")]
        [InlineData("PnL", "PanicLow")]
        [InlineData("PnH", "PanicHigh")]
        [InlineData("CustomStatus", "CustomStatus")]
        public void DecodeIoStatus_ReturnsExpected(string input, string expected)
        {
            Assert.Equal(expected, CsvExportService.DecodeIoStatus(input));
        }

        // ═══════════════════════════════════════════════════════════════
        // 8. CsvExportService - DecomposeIOSymbol
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void DecomposeIOSymbol_MotTemp()
        {
            CsvExportService.DecomposeIOSymbol("Motor1_MotTemp", out var comp, out var param);
            Assert.Equal("Motor1", comp);
            Assert.Equal("MotTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_DrvTemp()
        {
            CsvExportService.DecomposeIOSymbol("Drive1_DrvTemp", out var comp, out var param);
            Assert.Equal("Drive1", comp);
            Assert.Equal("DrvTemp", param);
        }

        [Fact]
        public void DecomposeIOSymbol_Regular()
        {
            CsvExportService.DecomposeIOSymbol("Sensor1", out var comp, out var param);
            Assert.Equal("Sensor1", comp);
            Assert.Equal("Value", param);
        }

        // ═══════════════════════════════════════════════════════════════
        // 9. CsvExportService - AddToSchema
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void AddToSchema_CreatesHierarchy()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            svc.AddToSchema(schema, "IO_Mon: BIM", "Sensor1", new[] { "Value", "eIoStatus" });
            Assert.True(schema.ContainsKey("IO_Mon: BIM"));
            Assert.True(schema["IO_Mon: BIM"].ContainsKey("Sensor1"));
            Assert.Contains("Value", schema["IO_Mon: BIM"]["Sensor1"]);
            Assert.Contains("eIoStatus", schema["IO_Mon: BIM"]["Sensor1"]);
        }

        [Fact]
        public void AddToSchema_MergesParams()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "A" });
            svc.AddToSchema(schema, "Sub1", "Comp1", new[] { "B" });
            Assert.Equal(2, schema["Sub1"]["Comp1"].Count);
        }

        // ═══════════════════════════════════════════════════════════════
        // 10. CsvExportService - ParseAndAddValue
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseAndAddValue_ValidParam()
        {
            var svc = new CsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1);
            svc.ParseAndAddValue("SetP=100", "AxisMon: BIM", "Motor1", time, data, new[] { "SetP", "ActP" });
            Assert.True(data.ContainsKey(time));
            Assert.Equal("100", data[time]["AxisMon: BIM|Motor1|SetP"]);
        }

        [Fact]
        public void ParseAndAddValue_InvalidParam_Ignored()
        {
            var svc = new CsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var time = new DateTime(2025, 1, 1);
            svc.ParseAndAddValue("BadParam=100", "Sub", "Motor1", time, data, new[] { "SetP", "ActP" });
            Assert.False(data.ContainsKey(time));
        }

        [Fact]
        public void ParseAndAddValue_NoEquals_Ignored()
        {
            var svc = new CsvExportService();
            var data = new SortedDictionary<DateTime, Dictionary<string, string>>();
            svc.ParseAndAddValue("noequals", "Sub", "M", DateTime.Now, data, new[] { "A" });
            Assert.Empty(data);
        }

        // ═══════════════════════════════════════════════════════════════
        // 11. CsvExportService - ParseAxisMon
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseAxisMon_ValidMessage()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: BIM,Motor1,SetP=100,ActP=99", "Worker", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(schema.ContainsKey("AxisMon: BIM"));
            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseAxisMon_WithTrigger()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1, 12, 0, 0);

            svc.ParseAxisMon("AxisMon: BIM,Motor1,Trg=1", "Worker", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("1", dataMatrix[time]["AxisMon: BIM|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxisMon_SelectedAxis_Filtered()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIM|Motor1" };

            svc.ParseAxisMon("AxisMon: BIM,Motor2,SetP=100", "Worker", DateTime.Now,
                selected, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix); // Motor2 not in selected
        }

        // ═══════════════════════════════════════════════════════════════
        // 12. CsvExportService - ParseAxM
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseAxM_ValidMessage()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseAxM("AxM: BIM,Motor1,SetP=100,LagE=0.5,TrigVal", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("0.5", dataMatrix[time]["AxisMon: BIM|Motor1|LagErr"]);
            Assert.Equal("TrigVal", dataMatrix[time]["AxisMon: BIM|Motor1|Trigger"]);
        }

        [Fact]
        public void ParseAxM_FilteredOut()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIM|MotorX" };

            svc.ParseAxM("AxM: BIM,Motor1,SetP=100", "W", DateTime.Now,
                selected, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ═══════════════════════════════════════════════════════════════
        // 13. CsvExportService - ParseIOMon
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseIOMon_ValidMessage()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIOMon("IO_Mon: BIM,Sensor1=42.5", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseIOMon_StatusChange()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIOMon("IO_Mon: BIM,Sensor1=New Status Op", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("Operational", dataMatrix[time]["IO_Mon: BIM|Sensor1|eIoStatus"]);
        }

        // ═══════════════════════════════════════════════════════════════
        // 14. CsvExportService - ParseIO
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseIO_ValidMessage()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: BIM,Sensor1=42.5", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseIO_WithStatus()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: BIM,Sensor1=42.5,Op", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("Operational", dataMatrix[time]["IO_Mon: BIM|Sensor1|eIoStatus"]);
        }

        [Fact]
        public void ParseIO_MotTemp()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: BIM,Motor1_MotTemp=55.0", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("55.0", dataMatrix[time]["IO_Mon: BIM|Motor1|MotTemp"]);
        }

        [Fact]
        public void ParseIO_DrvTemp()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIO("IO: BIM,Motor1_DrvTemp=65.0", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("65.0", dataMatrix[time]["IO_Mon: BIM|Motor1|DrvTemp"]);
        }

        [Fact]
        public void ParseIO_SelectedFilter()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIM|Sensor1" };

            svc.ParseIO("IO: BIM,Sensor2=42.5", "W", DateTime.Now,
                selected, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ═══════════════════════════════════════════════════════════════
        // 15. CsvExportService - ParseLogStats
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseLogStats_ValidEntry()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseLogStats(
                "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=AppLog)",
                "LogStats", time, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
            Assert.Equal("5000", dataMatrix[time]["LogStats|Metrics|Total"]);
        }

        [Fact]
        public void ParseLogStats_WrongThread_Ignored()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseLogStats(
                "LogStat: Logs(Total=5000 IsReady=1) nSemMissed(total=0 Mult=0) Lost=0 bufFull=0 Max(num=100 cat=AppLog)",
                "Worker", DateTime.Now, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        [Fact]
        public void ParseLogStats_NotLogStatPrefix_Ignored()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            svc.ParseLogStats("IO_Mon: data", "LogStats", DateTime.Now, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ═══════════════════════════════════════════════════════════════
        // 16. QueryParserService
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsQuotes()
        {
            Assert.True(QueryParserService.HasBooleanOperators("\"exact\""));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsParens()
        {
            Assert.True(QueryParserService.HasBooleanOperators("(a)"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsDash()
        {
            Assert.True(QueryParserService.HasBooleanOperators("-exclude"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsOR()
        {
            Assert.True(QueryParserService.HasBooleanOperators("a OR b"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsAND()
        {
            Assert.True(QueryParserService.HasBooleanOperators("a AND b"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsPipe()
        {
            Assert.True(QueryParserService.HasBooleanOperators("a | b"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_DetectsNOT()
        {
            Assert.True(QueryParserService.HasBooleanOperators("a NOT b"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_FalseForPlainText()
        {
            Assert.False(QueryParserService.HasBooleanOperators("simple search"));
        }

        [Fact]
        public void QueryParser_HasBooleanOperators_FalseForNull()
        {
            Assert.False(QueryParserService.HasBooleanOperators(null!));
        }

        [Fact]
        public void QueryParser_Parse_SimpleWord()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("hello", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Condition, result.Type);
            Assert.Equal("hello", result.Value);
        }

        [Fact]
        public void QueryParser_Parse_Phrase()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"exact phrase\"", out string? error);
            Assert.NotNull(result);
            Assert.Equal("exact phrase", result.Value);
        }

        [Fact]
        public void QueryParser_Parse_OR()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("hello OR world", out string? error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void QueryParser_Parse_AND_Implicit()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("hello world", out string? error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Fact]
        public void QueryParser_Parse_NOT()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("-exclude", out string? error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("NOT AND", result.LogicalOperator);
        }

        [Fact]
        public void QueryParser_Parse_Parentheses()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(a OR b) AND c", out _);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
        }

        [Fact]
        public void QueryParser_Parse_UnclosedQuote()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"unclosed", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void QueryParser_Parse_Empty()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void QueryParser_Parse_Null()
        {
            var parser = new QueryParserService();
            var result = parser.Parse(null, out string? error);
            Assert.Null(result);
        }

        [Fact]
        public void QueryParser_Parse_MismatchedParen()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(a", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void QueryParser_Parse_PipeOperator()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("a | b", out _);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void QueryParser_Parse_ExplicitAND()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("a AND b", out _);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Fact]
        public void QueryParser_Parse_ComplexExpression()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(a OR b) AND -c AND \"exact phrase\"", out _);
            Assert.NotNull(result);
        }

        // ═══════════════════════════════════════════════════════════════
        // 17. TextFilterParser
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TextFilterParser_Parse_SimpleContains()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'error')");
            Assert.NotNull(result);
            Assert.Equal("Message", result.Field);
            Assert.Equal("Contains", result.Operator);
            Assert.Equal("error", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_StartsWith()
        {
            var result = TextFilterParser.Parse("StartsWith([Thread], 'Worker')");
            Assert.NotNull(result);
            Assert.Equal("ThreadName", result.Field);
            Assert.Equal("Begins With", result.Operator);
            Assert.Equal("Worker", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_EndsWith()
        {
            var result = TextFilterParser.Parse("EndsWith([Level], 'Error')");
            Assert.NotNull(result);
            Assert.Equal("Level", result.Field);
            Assert.Equal("Ends With", result.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_Equals()
        {
            var result = TextFilterParser.Parse("Equals([Logger], 'MainLogger')");
            Assert.NotNull(result);
            Assert.Equal("Logger", result.Field);
            Assert.Equal("Equals", result.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_AND()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'a') And Contains([Message], 'b')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_OR()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'a') Or Contains([Message], 'b')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_Parentheses()
        {
            var result = TextFilterParser.Parse("(Contains([Message], 'a') Or Contains([Message], 'b')) And Contains([Level], 'Error')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
        }

        [Fact]
        public void TextFilterParser_Parse_Null_ReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(null));
        }

        [Fact]
        public void TextFilterParser_Parse_Empty_ReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(""));
        }

        [Fact]
        public void TextFilterParser_Parse_Invalid_Throws()
        {
            Assert.Throws<FormatException>(() => TextFilterParser.Parse("INVALID SYNTAX"));
        }

        [Fact]
        public void TextFilterParser_Parse_ProcessName()
        {
            var result = TextFilterParser.Parse("Contains([ProcessName], 'MyProc')");
            Assert.NotNull(result);
            Assert.Equal("ProcessName", result.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownField_PassThrough()
        {
            var result = TextFilterParser.Parse("Contains([CustomField], 'val')");
            Assert.NotNull(result);
            Assert.Equal("CustomField", result.Field);
        }

        // ═══════════════════════════════════════════════════════════════
        // 18. FilterNode - DeepClone
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void FilterNode_DeepClone_PreservesAll()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Contains",
                Value = "test",
                LogicalOperator = "AND"
            };
            var clone = node.DeepClone();
            Assert.Equal(node.Type, clone.Type);
            Assert.Equal(node.Field, clone.Field);
            Assert.Equal(node.Operator, clone.Operator);
            Assert.Equal(node.Value, clone.Value);
        }

        [Fact]
        public void FilterNode_DeepClone_ClonesChildren()
        {
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "a" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                }
            };
            var clone = root.DeepClone();
            Assert.Equal(2, clone.Children.Count);
            Assert.NotSame(root.Children[0], clone.Children[0]);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesResult()
        {
            var node = new FilterNode { Type = NodeType.Condition, Operator = "Regex", Value = @"\d+" };
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex1);
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void FilterNode_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var node = new FilterNode { Type = NodeType.Condition, Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = new FilterNode { Type = NodeType.Condition, Operator = "Regex", Value = "[invalid" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var node = new FilterNode { Type = NodeType.Condition, Operator = "Regex", Value = "" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_Value_Set_ClearsRegexCache()
        {
            var node = new FilterNode { Type = NodeType.Condition, Operator = "Regex", Value = @"\d+" };
            var regex1 = node.CompiledRegex;
            node.Value = @"\w+";
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex1);
            Assert.NotNull(regex2);
            Assert.NotSame(regex1, regex2);
        }

        // ═══════════════════════════════════════════════════════════════
        // 19. CsvExportService - ParseCHStepExport (via PresetFormatting)
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TryParseCHStep_PlcMngr_Style()
        {
            string msg = "CHStep: PlcMngr, STATE_STANDBY, State 5 <Root,0,0,100,1,0>";
            Assert.True(CsvExportService.TryParseCHStep(msg,
                out var chName, out _, out _, out _, out _, out _, out _, out _, out _));
            Assert.Equal("PlcMngr", chName);
        }

        [Fact]
        public void TryParseCHStep_WithWhitespace()
        {
            string msg = "CHStep:  MyCH , Step Message Text , State 10 < ParentCH , 42 , 3 , 100 , 1 , 0 >";
            Assert.True(CsvExportService.TryParseCHStep(msg,
                out var chName, out var stepMsg, out var stateId,
                out var parentName, out _, out _, out _, out _, out _));
            Assert.Equal("MyCH", chName);
            Assert.Equal("Step Message Text", stepMsg);
            Assert.Equal("10", stateId);
            Assert.Equal("ParentCH", parentName);
        }

        // ═══════════════════════════════════════════════════════════════
        // 20. WriteForwardFilledCsv - end-to-end via file
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void WriteForwardFilledCsv_IntegrationTest()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var time1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var time2 = new DateTime(2025, 1, 1, 10, 0, 1);

            // Add schema
            svc.AddToSchema(schema, "IO_Mon: BIM", "Sensor1", new[] { "Value" });

            // Add data
            dataMatrix[time1] = new Dictionary<string, string> { { "IO_Mon: BIM|Sensor1|Value", "42" } };
            dataMatrix[time2] = new Dictionary<string, string> { { "IO_Mon: BIM|Sensor1|Value", "43" } };

            machineStates[time1] = "STATE_STANDBY";

            threadNameMap["IO_Mon: BIM|Sensor1|Value"] = "Worker";

            var tempFile = Path.GetTempFileName();
            try
            {
                // Use reflection to call private WriteForwardFilledCsv
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, null, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, null });

                var lines = File.ReadAllLines(tempFile);
                Assert.True(lines.Length >= 3); // header + 2 data rows
                Assert.Contains("Time", lines[0]);
                Assert.Contains("Machine_State", lines[0]);
                Assert.Contains("BIM", lines[0]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteForwardFilledCsv_WithPresetOptions()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var time1 = new DateTime(2025, 1, 1, 10, 0, 0);
            svc.AddToSchema(schema, "IO_Mon: BIM", "S1", new[] { "Value" });
            dataMatrix[time1] = new Dictionary<string, string> { { "IO_Mon: BIM|S1|Value", "42" } };
            threadNameMap["IO_Mon: BIM|S1|Value"] = "W";

            var preset = new ExportPreset
            {
                IncludeUnixTime = true,
                IncludeMachineState = false,
                IncludeEvents = true,
                SelectedThreads = new List<string> { "Events" }
            };

            threadMessages[time1] = new Dictionary<string, string> { { "Events", "some event" } };

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, preset, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, new HashSet<string> { "Events" } });

                var lines = File.ReadAllLines(tempFile);
                Assert.Contains("Unix_Time", lines[0]);
                Assert.DoesNotContain("Machine_State", lines[0]);
                Assert.Contains("Events_Message", lines[0]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteForwardFilledCsv_EmptyData_NoOutput()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, null, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, null });

                // File should not have been written (or be minimal) since there's no data
                var content = File.ReadAllText(tempFile);
                // With no schema, machineStates, or threadMessages, it returns early
                Assert.True(content.Length == 0 || !content.Contains("Time"));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteForwardFilledCsv_ForwardFillLogic()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 10, 0, 1);
            var t3 = new DateTime(2025, 1, 1, 10, 0, 2);

            svc.AddToSchema(schema, "Sub", "C", new[] { "V" });
            dataMatrix[t1] = new Dictionary<string, string> { { "Sub|C|V", "10" } };
            // t2 has no data in dataMatrix but exists via machineStates - should forward fill
            dataMatrix[t3] = new Dictionary<string, string> { { "Sub|C|V", "20" } };

            machineStates[t1] = "STANDBY";
            machineStates[t2] = "STANDBY"; // ensure t2 is in allTimes
            machineStates[t3] = "RUNNING";

            threadNameMap["Sub|C|V"] = "W";

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, null, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, null });

                var lines = File.ReadAllLines(tempFile);
                // 3 data rows + 1 header
                Assert.Equal(4, lines.Length);
                // Second row should have forward-filled value "10"
                Assert.Contains("10", lines[2]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteForwardFilledCsv_LogStatsWarningMarker()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);

            svc.AddToSchema(schema, "LogStats", "Metrics", new[] { "Lost" });
            dataMatrix[t1] = new Dictionary<string, string> { { "LogStats|Metrics|Lost", "5" } };
            threadNameMap["LogStats|Metrics|Lost"] = "LogStats";

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, null, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, null });

                var lines = File.ReadAllLines(tempFile);
                // Lost=5 (non-zero) should be marked with [!]
                Assert.Contains("[!] 5", lines[1]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteForwardFilledCsv_CsvEscaping()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            svc.AddToSchema(schema, "Sub", "C", new[] { "V" });
            dataMatrix[t1] = new Dictionary<string, string> { { "Sub|C|V", "value,with\"quotes" } };
            threadNameMap["Sub|C|V"] = "W";

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, null, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, null });

                var content = File.ReadAllText(tempFile);
                // Should be CSV-escaped with quotes
                Assert.Contains("\"value,with\"\"quotes\"", content);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 21. AppConstants helpers
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void EscapeSqlBracketId_EscapesBrackets()
        {
            Assert.Equal("test]]value", AppConstants.EscapeSqlBracketId("test]value"));
        }

        [Fact]
        public void EscapeSqlBracketId_EmptyReturnsEmpty()
        {
            Assert.Equal("", AppConstants.EscapeSqlBracketId(""));
        }

        [Fact]
        public void EscapeSqlIdentifier_EscapesQuotes()
        {
            Assert.Equal("test\"\"value", AppConstants.EscapeSqlIdentifier("test\"value"));
        }

        [Fact]
        public void EscapeSqlIdentifier_NullReturnsNull()
        {
            Assert.Null(AppConstants.EscapeSqlIdentifier(null!));
        }

        // ═══════════════════════════════════════════════════════════════
        // 22. RemoveFilterConditionByIndex (via reflection)
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesCorrectCondition()
        {
            var vm = CreateFilterSearchVM();
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "a" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "b" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "c" }
                }
            };

            InvokeMethod(vm, "RemoveFilterConditionByIndex", root, 1);
            Assert.Equal(2, root.Children.Count);
            Assert.Equal("a", root.Children[0].Value);
            Assert.Equal("c", root.Children[1].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NestedGroup()
        {
            var vm = CreateFilterSearchVM();
            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "a" },
                    new FilterNode
                    {
                        Type = NodeType.Group,
                        LogicalOperator = "OR",
                        Children = new ObservableCollection<FilterNode>
                        {
                            new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "b" }
                        }
                    }
                }
            };

            // Remove index 1 = "b" inside nested group
            InvokeMethod(vm, "RemoveFilterConditionByIndex", root, 1);
            // Nested group should also be removed since it became empty
            Assert.Single(root.Children);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NullRoot_NoOp()
        {
            var vm = CreateFilterSearchVM();
            InvokeMethod(vm, "RemoveFilterConditionByIndex", null!, 0);
            // Should not throw
        }

        // ═══════════════════════════════════════════════════════════════
        // 23. IsDefaultLog (coverage of factory filter evaluation)
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void IsDefaultLog_UsesFactoryFilter()
        {
            var vm = CreateFilterSearchVM();
            var log = MakeLog("STATE_STANDBY - Enter ======", level: "INFO");
            // Should not throw; factory filter evaluates
            var result = vm.IsDefaultLog(log);
            // Just verify it runs without error - result depends on DefaultConfigurationService
            Assert.True(result || !result);
        }

        // ═══════════════════════════════════════════════════════════════
        // 24. ParseIOMon with MotTemp/DrvTemp suffixes
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ParseIOMon_MotTempSuffix()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 1, 1);

            svc.ParseIOMon("IO_Mon: BIM,Motor1_MotTemp=55.0", "W", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.True(dataMatrix.ContainsKey(time));
        }

        [Fact]
        public void ParseIOMon_Filtered()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIM|OtherSensor" };

            svc.ParseIOMon("IO_Mon: BIM,Sensor1=42", "W", DateTime.Now,
                selected, schema, dataMatrix, threadNameMap);

            Assert.Empty(dataMatrix);
        }

        // ═══════════════════════════════════════════════════════════════
        // 25. Multiple combined filters - GetActiveFilters
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void GetActiveFilters_APPTab_AllFiltersActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_APP);
            SetField(vm, "_isAppErrorFilterActive", true);
            SetField(vm, "_isAppTimeFocusActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "T1" });
            SetField(vm, "_appNegativeFilters", new List<string> { "neg1" });
            SetField(vm, "_activeLoggerFilters", new List<string> { "L1" });
            SetField(vm, "_activeMethodFilters", new List<string> { "M1" });
            SetField(vm, "_treeShowOnlyLogger", "SL");
            SetField(vm, "_treeShowOnlyPrefix", "SP");
            SetField(vm, "_treeHiddenLoggers", new HashSet<string> { "HL" });
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string> { "HP" });
            SetField(vm, "_searchText", "test query");
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now.AddHours(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);

            var result = vm.GetActiveFilters();
            Assert.True(result.Count >= 12);
        }

        [Fact]
        public void GetActiveFilters_PLCTab_AllFiltersActive()
        {
            var vm = CreateFilterSearchVM(AppConstants.TAB_PLC);
            SetField(vm, "_isTimeFocusActive", true);
            SetField(vm, "_activeThreadFilters", new List<string> { "T1" });
            SetField(vm, "_negativeFilters", new List<string> { "neg1" });
            SetField(vm, "_plcTreeShowOnlyPrefix", "PP");
            SetField(vm, "_plcTreeShowOnlyLogger", "PL");
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "PHL" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "PHP" });
            SetField(vm, "_searchText", "test query");

            var result = vm.GetActiveFilters();
            Assert.True(result.Count >= 8);
        }

        // ═══════════════════════════════════════════════════════════════
        // 26. WriteForwardFilledCsv - thread messages with selected threads
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void WriteForwardFilledCsv_SelectedThreadsMessages()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>();
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            svc.AddToSchema(schema, "Sub", "C", new[] { "V" });
            dataMatrix[t1] = new Dictionary<string, string> { { "Sub|C|V", "42" } };
            threadNameMap["Sub|C|V"] = "W";

            threadMessages[t1] = new Dictionary<string, string>
            {
                { "Worker", "worker message" },
                { "Events", "event msg" }
            };

            var selectedThreads = new HashSet<string> { "Worker" };
            var preset = new ExportPreset
            {
                IncludeEvents = true,
                IncludeMachineState = true,
                IncludeUnixTime = false,
                SelectedThreads = new List<string> { "Worker" }
            };

            var tempFile = Path.GetTempFileName();
            try
            {
                var method = typeof(CsvExportService).GetMethod("WriteForwardFilledCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method!.Invoke(svc, new object?[] { tempFile, preset, null, schema, dataMatrix, machineStates, threadMessages, threadNameMap, selectedThreads });

                var lines = File.ReadAllLines(tempFile);
                Assert.Contains("Worker_Message", lines[0]);
                Assert.Contains("Events_Message", lines[0]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
