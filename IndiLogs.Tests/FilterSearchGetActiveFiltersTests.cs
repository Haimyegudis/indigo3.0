using IndiLogs_3._0;
using IndiLogs_3._0.Models;
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
    public class FilterSearchGetActiveFiltersTests
    {
        private static readonly BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;

        private (FilterSearchViewModel vm, MainViewModel parent) CreateVMPair(int tabIndex)
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_selectedTabIndex", NPI)?.SetValue(parent, tabIndex);

            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            typeof(FilterSearchViewModel).GetField("_parent", NPI)?.SetValue(vm, parent);

            // Initialize all collection fields to empty
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
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
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

            return (vm, parent);
        }

        private static void SetField(object obj, string fieldName, object? value)
        {
            var field = obj.GetType().GetField(fieldName, NPI);
            field?.SetValue(obj, value);
        }

        // ── PLC Tab Tests ──

        [Fact]
        public void GetActiveFilters_PLCTab_NoFilters_Empty()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }

        [Fact]
        public void GetActiveFilters_PLCTab_TimeFocus_Shown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_isTimeFocusActive", true);

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Category == "TIME RANGE" && r.Key == "MAIN_TIME_FOCUS");
        }

        [Fact]
        public void GetActiveFilters_PLCTab_ThreadFilters_Listed()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1", "Thread2" });

            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count(r => r.Category == "THREAD"));
            Assert.Contains(result, r => r.Key == "MAIN_THREAD:Thread1");
            Assert.Contains(result, r => r.Key == "MAIN_THREAD:Thread2");
        }

        [Fact]
        public void GetActiveFilters_PLCTab_NegativeFilters_MessageAndThread()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_negativeFilters", new List<string> { "THREAD:MainThread", "error message" });

            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count(r => r.Category == "FILTER OUT"));
            Assert.Contains(result, r => r.Description.Contains("Thread: MainThread"));
            Assert.Contains(result, r => r.Description.Contains("Message: \"error message\""));
        }

        [Fact]
        public void GetActiveFilters_PLCTab_MainFilterRoot_Shown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            var filterRoot = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "error" }
                }
            };
            SetField(vm, "_mainFilterRoot", filterRoot);

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Category == "FILTER" && r.Description.Contains("Message"));
        }

        [Fact]
        public void GetActiveFilters_PLCTab_TreeFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_plcTreeShowOnlyPrefix", "com.hp");
            SetField(vm, "_plcTreeShowOnlyLogger", "MainLogger");
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "Debug" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "com.test" });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "PLC_TREE_SHOW_ONLY_PREFIX");
            Assert.Contains(result, r => r.Key == "PLC_TREE_SHOW_ONLY_LOGGER");
            Assert.Contains(result, r => r.Key.StartsWith("PLC_TREE_HIDE_LOGGER:"));
            Assert.Contains(result, r => r.Key.StartsWith("PLC_TREE_HIDE_PREFIX:"));
        }

        // ── APP Tab Tests ──

        [Fact]
        public void GetActiveFilters_APPTab_NoFilters_Empty()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }

        [Fact]
        public void GetActiveFilters_APPTab_TimeFocus()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_isAppTimeFocusActive", true);

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "APP_TIME_FOCUS");
        }

        [Fact]
        public void GetActiveFilters_APPTab_ErrorFilter()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_isAppErrorFilterActive", true);

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "APP_ERROR_FILTER");
        }

        [Fact]
        public void GetActiveFilters_APPTab_ThreadFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "WorkerThread" });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "APP_THREAD:WorkerThread");
        }

        [Fact]
        public void GetActiveFilters_APPTab_NegativeFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_appNegativeFilters", new List<string> { "noise" });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Category == "FILTER OUT" && r.Key.StartsWith("APP_NEGATIVE:"));
        }

        [Fact]
        public void GetActiveFilters_APPTab_LoggerFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_activeLoggerFilters", new List<string> { "AppService", "Database" });

            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count(r => r.Category == "LOGGER"));
        }

        [Fact]
        public void GetActiveFilters_APPTab_MethodFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_activeMethodFilters", new List<string> { "Initialize" });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Category == "METHOD");
        }

        [Fact]
        public void GetActiveFilters_APPTab_TreeFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_treeShowOnlyLogger", "MainLogger");
            SetField(vm, "_treeShowOnlyPrefix", "com.hp");
            SetField(vm, "_treeHiddenLoggers", new HashSet<string> { "Debug" });
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string> { "com.test" });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "TREE_SHOW_ONLY_LOGGER");
            Assert.Contains(result, r => r.Key == "TREE_SHOW_ONLY_PREFIX");
        }

        [Fact]
        public void GetActiveFilters_APPTab_AppFilterRoot()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            var filterRoot = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Fatal" }
                }
            };
            SetField(vm, "_appFilterRoot", filterRoot);

            var result = vm.GetActiveFilters();
            Assert.Equal(2, result.Count(r => r.Category == "FILTER"));
        }

        // ── Shared filters ──

        [Fact]
        public void GetActiveFilters_GlobalTimeRange_Shown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)new DateTime(2024, 1, 1, 10, 0, 0));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)new DateTime(2024, 1, 1, 12, 0, 0));

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "GLOBAL_TIME_RANGE");
        }

        [Fact]
        public void GetActiveFilters_SearchText_Shown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_searchText", "error message");

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "SEARCH" && r.Description.Contains("error message"));
        }

        [Fact]
        public void GetActiveFilters_SearchText_TooShort_NotShown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_searchText", "e");

            var result = vm.GetActiveFilters();
            Assert.DoesNotContain(result, r => r.Key == "SEARCH");
        }

        [Fact]
        public void GetActiveFilters_RangeSelection_Shown()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", new LogEntry { Date = new DateTime(2024, 1, 1, 10, 0, 0) });

            var result = vm.GetActiveFilters();
            Assert.Contains(result, r => r.Key == "RANGE");
        }

        // ── CollectFilterNodeDescriptions ──

        [Fact]
        public void CollectFilterNodeDescriptions_NestedGroup_FlattensConditions()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);

            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "error" },
                    new FilterNode
                    {
                        Type = NodeType.Group,
                        LogicalOperator = "OR",
                        Children = new ObservableCollection<FilterNode>
                        {
                            new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Fatal" },
                            new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                        }
                    }
                }
            };

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI);
            method!.Invoke(vm, new object?[] { items, root, "FILTER", "", "TEST", idx });

            Assert.Equal(3, items.Count);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_SkipsThreadName_WhenActiveFilters()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1" });

            var root = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "ThreadName",
                Operator = "Equals",
                Value = "Thread1"
            };

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI);
            method!.Invoke(vm, new object?[] { items, root, "FILTER", "", "TEST", idx });

            Assert.Empty(items); // ThreadName condition skipped
        }

        // ── Combined scenario ──

        [Fact]
        public void GetActiveFilters_PLCTab_MultipleFilters_AllPresent()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_PLC);

            // Set up multiple filters simultaneously
            SetField(vm, "_isTimeFocusActive", true);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1" });
            SetField(vm, "_negativeFilters", new List<string> { "noise" });
            SetField(vm, "_isGlobalTimeRangeActive", true);
            SetField(vm, "_globalTimeRangeStart", DateTime.Now.AddHours(-1));
            SetField(vm, "_globalTimeRangeEnd", DateTime.Now);
            SetField(vm, "_searchText", "error query");
            SetField(vm, "_plcTreeShowOnlyPrefix", "com.hp");

            var result = vm.GetActiveFilters();

            Assert.Contains(result, r => r.Category == "TIME RANGE" && r.Key == "MAIN_TIME_FOCUS");
            Assert.Contains(result, r => r.Category == "THREAD");
            Assert.Contains(result, r => r.Category == "FILTER OUT");
            Assert.Contains(result, r => r.Key == "GLOBAL_TIME_RANGE");
            Assert.Contains(result, r => r.Key == "SEARCH");
            Assert.Contains(result, r => r.Key == "PLC_TREE_SHOW_ONLY_PREFIX");
        }

        [Fact]
        public void GetActiveFilters_APPTab_MultipleFilters_AllPresent()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_APP);

            SetField(vm, "_isAppTimeFocusActive", true);
            SetField(vm, "_isAppErrorFilterActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "Worker" });
            SetField(vm, "_appNegativeFilters", new List<string> { "THREAD:Debug" });
            SetField(vm, "_activeLoggerFilters", new List<string> { "Service" });
            SetField(vm, "_activeMethodFilters", new List<string> { "Init" });
            SetField(vm, "_treeShowOnlyLogger", "Main");

            var result = vm.GetActiveFilters();

            Assert.Contains(result, r => r.Key == "APP_TIME_FOCUS");
            Assert.Contains(result, r => r.Key == "APP_ERROR_FILTER");
            Assert.Contains(result, r => r.Category == "THREAD");
            Assert.Contains(result, r => r.Category == "FILTER OUT");
            Assert.Contains(result, r => r.Category == "LOGGER");
            Assert.Contains(result, r => r.Category == "METHOD");
        }

        // ── Non-log tab ──

        [Fact]
        public void GetActiveFilters_NonLogTab_Empty()
        {
            var (vm, _) = CreateVMPair(AppConstants.TAB_EVENTS); // Events tab
            var result = vm.GetActiveFilters();
            Assert.Empty(result);
        }
    }
}
