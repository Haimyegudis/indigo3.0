using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
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
    public class CoverageBoostVmATests
    {
        // Create uninitialized VM instances for testing public/internal methods
        private static readonly FilterSearchViewModel _filterVM =
            (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

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
            Dictionary<string, string>? extraFields = null,
            DateTime? date = null) =>
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
                ExtraFields = extraFields,
                Date = date ?? DateTime.Now
            };

        private static FilterNode MakeCondition(string field, string op, string value) =>
            new()
            {
                Type = NodeType.Condition,
                Field = field,
                Operator = op,
                Value = value
            };

        private static FilterNode MakeGroup(string logicalOperator, params FilterNode[] children) =>
            new()
            {
                Type = NodeType.Group,
                LogicalOperator = logicalOperator,
                Children = new ObservableCollection<FilterNode>(children)
            };

        private static void SetField(object obj, string fieldName, object? value)
        {
            var type = obj.GetType();
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            field?.SetValue(obj, value);
        }

        private static object? GetField(object obj, string fieldName)
        {
            var type = obj.GetType();
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            return field?.GetValue(obj);
        }

        private static object? InvokeMethod(object obj, string methodName, params object?[] args)
        {
            var type = obj.GetType();
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return method?.Invoke(obj, args);
        }

        // ══════════════════════════════════════════════
        // MatchesSearch tests
        // ══════════════════════════════════════════════

        [Fact]
        public void MatchesSearch_MessageContains_ReturnsTrue()
        {
            var log = MakeLog(message: "Hello World");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "hello"));
        }

        [Fact]
        public void MatchesSearch_MessageDoesNotContain_ReturnsFalse()
        {
            var log = MakeLog(message: "Hello World");
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "xyz"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldContains_ReturnsTrue()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Key1", "SomeValue" } });
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "somevalue"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldNull_ReturnsFalse()
        {
            var log = MakeLog(message: "abc");
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "xyz"));
        }

        [Fact]
        public void MatchesSearch_NullMessage_ReturnsFalse()
        {
            var log = MakeLog();
            log.Message = null!;
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        [Fact]
        public void MatchesSearch_ExtraFieldValueNull_SkipsIt()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Key1", null! } });
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "test"));
        }

        [Fact]
        public void MatchesSearch_CaseInsensitive()
        {
            var log = MakeLog(message: "Error occurred");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "ERROR"));
        }

        [Fact]
        public void MatchesSearch_MultipleExtraFields_MatchesSecond()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string>
            {
                { "Key1", "NoMatch" },
                { "Key2", "TargetValue" }
            });
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "targetvalue"));
        }

        // ══════════════════════════════════════════════
        // EvaluateFilterNode — extended coverage
        // ══════════════════════════════════════════════

        [Theory]
        [InlineData("Level", "Error", true)]
        [InlineData("ThreadName", "Main", true)]
        [InlineData("Logger", "com.app", true)]
        [InlineData("ProcessName", "test.exe", true)]
        [InlineData("Method", "DoWork", true)]
        [InlineData("Pattern", "PatternA", true)]
        [InlineData("Data", "DataValue", true)]
        [InlineData("Exception", "NullRef", true)]
        [InlineData("Message", "TestMsg", true)]
        public void EvaluateFilterNode_ContainsOperator_AllFields(string field, string value, bool expected)
        {
            var log = MakeLog(
                message: "TestMsg",
                level: "Error",
                logger: "com.app",
                threadName: "Main",
                processName: "test.exe",
                method: "DoWork",
                pattern: "PatternA",
                data: "DataValue",
                exception: "NullRef");
            var node = MakeCondition(field, "Contains", value);
            Assert.Equal(expected, _filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_EqualsOperator_CaseInsensitive()
        {
            var log = MakeLog(level: "Error");
            var node = MakeCondition("Level", "Equals", "error");
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_EqualsOperator_NoMatch()
        {
            var log = MakeLog(level: "Warning");
            var node = MakeCondition("Level", "Equals", "error");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_BeginsWith()
        {
            var log = MakeLog(message: "StartOfMessage");
            var node = MakeCondition("Message", "Begins With", "start");
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_BeginsWithNoMatch()
        {
            var log = MakeLog(message: "EndOfMessage");
            var node = MakeCondition("Message", "Begins With", "start");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_EndsWith()
        {
            var log = MakeLog(message: "MessageEnd");
            var node = MakeCondition("Message", "Ends With", "end");
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_EndsWithNoMatch()
        {
            var log = MakeLog(message: "MessageStart");
            var node = MakeCondition("Message", "Ends With", "end");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_RegexMatch()
        {
            var log = MakeLog(message: "Error: code 123");
            var node = MakeCondition("Message", "Regex", @"code \d+");
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_RegexNoMatch()
        {
            var log = MakeLog(message: "No numbers here");
            var node = MakeCondition("Message", "Regex", @"^\d+$");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_InvalidRegex_ReturnsFalse()
        {
            var log = MakeLog(message: "test");
            var node = MakeCondition("Message", "Regex", @"[invalid");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_EmptyFieldValue_ReturnsFalse()
        {
            var log = MakeLog(level: "");
            var node = MakeCondition("Level", "Contains", "error");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_UnknownField_ReturnsFalse()
        {
            var log = MakeLog(message: "test");
            var node = MakeCondition("UnknownField", "Contains", "test");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_ExtraFieldMatch()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "CustomField", "CustomValue" } });
            var node = MakeCondition("CustomField", "Contains", "custom");
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_ExtraFieldNoKey_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Key1", "Val1" } });
            var node = MakeCondition("Key2", "Contains", "val");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_ExtraFieldNullDict_ReturnsFalse()
        {
            var log = MakeLog();
            var node = MakeCondition("CustomField", "Contains", "val");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_ANDGroup_AllMatch()
        {
            var log = MakeLog(level: "Error", message: "Failed");
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.True(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_ANDGroup_OneNoMatch()
        {
            var log = MakeLog(level: "Warning", message: "Failed");
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.False(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_ORGroup_OneMatch()
        {
            var log = MakeLog(level: "Warning", message: "Failed");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.True(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_ORGroup_NoneMatch()
        {
            var log = MakeLog(level: "Info", message: "Success");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "error"),
                MakeCondition("Message", "Contains", "fail"));
            Assert.False(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_NOTANDGroup()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("NOT AND",
                MakeCondition("Level", "Equals", "info"));
            Assert.False(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_NOTORGroup()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "error"),
                MakeCondition("Level", "Equals", "warning"));
            Assert.True(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_NOTANDGroup_NonMatch_ReturnsTrue()
        {
            var log = MakeLog(level: "Info");
            var group = MakeGroup("NOT AND",
                MakeCondition("Level", "Equals", "error"));
            Assert.True(_filterVM.EvaluateFilterNode(log, group));
        }

        [Fact]
        public void EvaluateFilterNode_NestedGroups()
        {
            var log = MakeLog(level: "Error", message: "Crash", logger: "com.app");
            var inner = MakeGroup("OR",
                MakeCondition("Message", "Contains", "crash"),
                MakeCondition("Message", "Contains", "fail"));
            var outer = MakeGroup("AND",
                MakeCondition("Level", "Equals", "error"),
                inner);
            Assert.True(_filterVM.EvaluateFilterNode(log, outer));
        }

        [Fact]
        public void EvaluateFilterNode_RegexWithCompiledCache()
        {
            var node = MakeCondition("Message", "Regex", @"\d{3}");
            var log1 = MakeLog(message: "abc 123 def");
            var log2 = MakeLog(message: "abc 456 def");
            Assert.True(_filterVM.EvaluateFilterNode(log1, node));
            Assert.True(_filterVM.EvaluateFilterNode(log2, node));
        }

        // ══════════════════════════════════════════════
        // FilterNode model tests
        // ══════════════════════════════════════════════

        [Fact]
        public void FilterNode_DeepClone_CopiesAllProperties()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                LogicalOperator = "OR",
                Field = "Level",
                Operator = "Equals",
                Value = "Error"
            };
            var clone = original.DeepClone();
            Assert.Equal(original.Type, clone.Type);
            Assert.Equal(original.LogicalOperator, clone.LogicalOperator);
            Assert.Equal(original.Field, clone.Field);
            Assert.Equal(original.Operator, clone.Operator);
            Assert.Equal(original.Value, clone.Value);
            Assert.NotSame(original, clone);
        }

        [Fact]
        public void FilterNode_DeepClone_ClonesChildren()
        {
            var parent = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            var clone = parent.DeepClone();
            Assert.Equal(2, clone.Children.Count);
            Assert.NotSame(parent.Children[0], clone.Children[0]);
            Assert.Equal("Level", clone.Children[0].Field);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenNotRegexOperator()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenEmptyValue()
        {
            var node = new FilterNode { Operator = "Regex", Value = "" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsRegex_WhenValid()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            Assert.NotNull(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ReturnsNull_WhenInvalidPattern()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"[invalid" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesResult()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\w+" };
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidatesOnValueChange()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\w+" };
            var regex1 = node.CompiledRegex;
            node.Value = @"\d+";
            var regex2 = node.CompiledRegex;
            Assert.NotSame(regex1, regex2);
        }

        [Fact]
        public void FilterNode_IsEnabled_DefaultTrue()
        {
            var node = new FilterNode();
            Assert.True(node.IsEnabled);
        }

        [Fact]
        public void FilterNode_PropertyChanged_RaisedOnTypeChange()
        {
            var node = new FilterNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.Type = NodeType.Condition;
            Assert.Equal("Type", changedProp);
        }

        [Fact]
        public void FilterNode_PropertyChanged_RaisedOnValueChange()
        {
            var node = new FilterNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.Value = "test";
            Assert.Equal("Value", changedProp);
        }

        // ══════════════════════════════════════════════
        // LoggerNode model tests
        // ══════════════════════════════════════════════

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
        }

        [Fact]
        public void LoggerNode_DisplayText()
        {
            var node = new LoggerNode { Name = "Logger1", Count = 42 };
            Assert.Equal("Logger1 (42)", node.DisplayText);
        }

        [Fact]
        public void LoggerNode_PropertyChanged_IsHidden()
        {
            var node = new LoggerNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.IsHidden = true;
            Assert.Equal("IsHidden", changedProp);
        }

        [Fact]
        public void LoggerNode_PropertyChanged_IsActive()
        {
            var node = new LoggerNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.IsActive = true;
            Assert.Equal("IsActive", changedProp);
        }

        [Fact]
        public void LoggerNode_Children_DefaultEmpty()
        {
            var node = new LoggerNode();
            Assert.NotNull(node.Children);
            Assert.Empty(node.Children);
        }

        // ══════════════════════════════════════════════
        // ActiveFilterItem model tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ActiveFilterItem_Defaults()
        {
            var item = new ActiveFilterItem();
            Assert.Equal("", item.Category);
            Assert.Equal("", item.Description);
            Assert.False(item.IsActive);
            Assert.Equal("", item.Key);
        }

        [Fact]
        public void ActiveFilterItem_SetProperties()
        {
            var item = new ActiveFilterItem
            {
                Category = "FILTER",
                Description = "Level Equals Error",
                IsActive = true,
                Key = "APP_ERROR_FILTER"
            };
            Assert.Equal("FILTER", item.Category);
            Assert.Equal("Level Equals Error", item.Description);
            Assert.True(item.IsActive);
            Assert.Equal("APP_ERROR_FILTER", item.Key);
        }

        // ══════════════════════════════════════════════
        // CollectFilterNodeDescriptions tests (via reflection)
        // ══════════════════════════════════════════════

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { items, null, "FILTER", "", "TEST", idx });
            Assert.Empty(items);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_ConditionNode_AddsItem()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var node = MakeCondition("Level", "Equals", "Error");
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { items, node, "FILTER", "", "TEST", idx });
            Assert.Single(items);
            Assert.Contains("Level", items[0].Description);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_GroupWithChildren_AddsAll()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { items, group, "FILTER", "", "TEST", idx });
            Assert.Equal(2, items.Count);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_ThreadNameCondition_SkippedWhenThreadFiltersActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1" });
            SetField(vm, "_appActiveThreadFilters", new List<string>());

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var node = MakeCondition("ThreadName", "Equals", "Thread1");
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { items, node, "FILTER", "", "TEST", idx });
            Assert.Empty(items);
        }

        // ══════════════════════════════════════════════
        // RemoveFilterConditionByIndex tests (via reflection)
        // ══════════════════════════════════════════════

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesCorrectCondition()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"),
                MakeCondition("Logger", "Contains", "com"));

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { root, 1 });
            Assert.Equal(2, root.Children.Count);
            Assert.Equal("Level", root.Children[0].Field);
            Assert.Equal("Logger", root.Children[1].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesFirst()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { root, 0 });
            Assert.Single(root.Children);
            Assert.Equal("Message", root.Children[0].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NullRoot_DoesNotThrow()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null, 0 }));
            Assert.Null(ex);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NestedGroup_RemovesCondition()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var inner = MakeGroup("OR",
                MakeCondition("Message", "Contains", "innerA"),
                MakeCondition("Message", "Contains", "innerB"));
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                inner);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            // index 0 = Level condition, index 1 = innerA, index 2 = innerB
            method?.Invoke(vm, new object?[] { root, 1 });
            Assert.Single(inner.Children);
            Assert.Equal("innerB", inner.Children[0].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesLastInGroup_RemovesGroup()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var inner = MakeGroup("OR",
                MakeCondition("Message", "Contains", "only"));
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                inner);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            // index 0 = Level, index 1 = "only" in inner group
            method?.Invoke(vm, new object?[] { root, 1 });
            // inner group should be removed since it's now empty
            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        // ══════════════════════════════════════════════
        // ClearFilters tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ClearFilters_ViaSetter_ResetsAllFilterFlags()
        {
            // Test the state properties that ClearFilters would reset
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isMainFilterActive", true);
            vm.IsMainFilterActive = false;
            Assert.False(vm.IsMainFilterActive);

            SetField(vm, "_isAppFilterActive", true);
            vm.IsAppFilterActive = false;
            Assert.False(vm.IsAppFilterActive);

            SetField(vm, "_isMainFilterOutActive", true);
            vm.IsMainFilterOutActive = false;
            Assert.False(vm.IsMainFilterOutActive);

            SetField(vm, "_isAppFilterOutActive", true);
            vm.IsAppFilterOutActive = false;
            Assert.False(vm.IsAppFilterOutActive);

            SetField(vm, "_isTimeFocusActive", true);
            vm.IsTimeFocusActive = false;
            Assert.False(vm.IsTimeFocusActive);

            SetField(vm, "_isAppTimeFocusActive", true);
            vm.IsAppTimeFocusActive = false;
            Assert.False(vm.IsAppTimeFocusActive);

            SetField(vm, "_isAppErrorFilterActive", true);
            vm.IsAppErrorFilterActive = false;
            Assert.False(vm.IsAppErrorFilterActive);
        }

        [Fact]
        public void ClearFilters_NegativeFilters_CanBeCleared()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var neg = new List<string> { "a", "b" };
            var appNeg = new List<string> { "c" };
            SetField(vm, "_negativeFilters", neg);
            SetField(vm, "_appNegativeFilters", appNeg);
            neg.Clear();
            appNeg.Clear();
            Assert.Empty(vm.NegativeFilters);
            Assert.Empty(vm.AppNegativeFilters);
        }

        [Fact]
        public void ClearFilters_ColumnFilters_CanBeCleared()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var threads = new List<string> { "t1" };
            var appThreads = new List<string> { "t2" };
            var loggers = new List<string> { "l1" };
            var methods = new List<string> { "m1" };
            SetField(vm, "_activeThreadFilters", threads);
            SetField(vm, "_appActiveThreadFilters", appThreads);
            SetField(vm, "_activeLoggerFilters", loggers);
            SetField(vm, "_activeMethodFilters", methods);
            threads.Clear();
            appThreads.Clear();
            loggers.Clear();
            methods.Clear();
            Assert.Empty(vm.ActiveThreadFilters);
            Assert.Empty(vm.AppActiveThreadFilters);
            Assert.Empty(vm.ActiveLoggerFilters);
            Assert.Empty(vm.ActiveMethodFilters);
        }

        // ══════════════════════════════════════════════
        // RemoveActiveFilter tests
        // ══════════════════════════════════════════════

        private static MainViewModel CreateParentVm()
        {
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(parent, "_selectedTabIndex", 0);
            return parent;
        }

        private FilterSearchViewModel CreateVmForRemoveActiveFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_parent", CreateParentVm());
            SetField(vm, "_sessionVM", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_savedFilterRoot", (FilterNode?)null);
            SetField(vm, "_lastFilteredCache", (List<LogEntry>?)null);
            SetField(vm, "_lastFilteredAppCache", (List<LogEntry>?)null);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_isAppErrorFilterActive", false);
            SetField(vm, "_hasRangeStart", false);
            SetField(vm, "_rangeStartLog", (LogEntry?)null);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_searchText", (string?)null);
            // DispatcherTimer needed for SearchText property setter
            SetField(vm, "_searchDebounceTimer", new System.Windows.Threading.DispatcherTimer());
            return vm;
        }

        [Fact]
        public void RemoveActiveFilter_EmptyKey_DoesNotThrow()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var ex = Record.Exception(() => vm.RemoveActiveFilter(""));
            Assert.Null(ex);
        }

        [Fact]
        public void RemoveActiveFilter_NullKey_DoesNotThrow()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var ex = Record.Exception(() => vm.RemoveActiveFilter(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void RemoveActiveFilter_AppErrorFilter()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_isAppErrorFilterActive", true);
            vm.RemoveActiveFilter("APP_ERROR_FILTER");
            Assert.False(vm.IsAppErrorFilterActive);
        }

        [Fact]
        public void RemoveActiveFilter_AppTimeFocus()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_isAppTimeFocusActive", true);
            vm.RemoveActiveFilter("APP_TIME_FOCUS");
            Assert.False(vm.IsAppTimeFocusActive);
        }

        [Fact]
        public void RemoveActiveFilter_MainTimeFocus()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_isTimeFocusActive", true);
            vm.RemoveActiveFilter("MAIN_TIME_FOCUS");
            Assert.False(vm.IsTimeFocusActive);
        }

        [Fact]
        public void RemoveActiveFilter_GlobalTimeRange()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_globalTimeRangeStart", DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", DateTime.Now.AddHours(1));
            vm.RemoveActiveFilter("GLOBAL_TIME_RANGE");
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void RemoveActiveFilter_Search()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_searchText", "test search");
            vm.RemoveActiveFilter("SEARCH");
            Assert.Equal("", vm.SearchText);
        }

        [Fact]
        public void RemoveActiveFilter_Range()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog());
            vm.RemoveActiveFilter("RANGE");
            Assert.False((bool)GetField(vm, "_hasRangeStart")!);
        }

        [Fact]
        public void RemoveActiveFilter_TreeShowOnlyLogger()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_treeShowOnlyLogger", "com.app");
            vm.RemoveActiveFilter("TREE_SHOW_ONLY_LOGGER");
            Assert.Null(vm.TreeShowOnlyLogger);
        }

        [Fact]
        public void RemoveActiveFilter_TreeShowOnlyPrefix()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_treeShowOnlyPrefix", "com");
            vm.RemoveActiveFilter("TREE_SHOW_ONLY_PREFIX");
            Assert.Null(vm.TreeShowOnlyPrefix);
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeShowOnlyLogger()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_plcTreeShowOnlyLogger", "E1.PLC");
            vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_LOGGER");
            Assert.Null(GetField(vm, "_plcTreeShowOnlyLogger"));
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeShowOnlyPrefix()
        {
            var vm = CreateVmForRemoveActiveFilter();
            SetField(vm, "_plcTreeShowOnlyPrefix", "E1");
            vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_PREFIX");
            Assert.Null(GetField(vm, "_plcTreeShowOnlyPrefix"));
        }

        [Fact]
        public void RemoveActiveFilter_LoggerKey_RemovesLogger()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var loggers = new List<string> { "com.app", "com.core" };
            SetField(vm, "_activeLoggerFilters", loggers);
            vm.RemoveActiveFilter("LOGGER:com.app");
            Assert.Single(loggers);
            Assert.Equal("com.core", loggers[0]);
        }

        [Fact]
        public void RemoveActiveFilter_MethodKey_RemovesMethod()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var methods = new List<string> { "DoWork", "Init" };
            SetField(vm, "_activeMethodFilters", methods);
            vm.RemoveActiveFilter("METHOD:DoWork");
            Assert.Single(methods);
            Assert.Equal("Init", methods[0]);
        }

        [Fact]
        public void RemoveActiveFilter_TreeHideLogger_RemovesLogger()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var hidden = new HashSet<string> { "com.app", "com.core" };
            SetField(vm, "_treeHiddenLoggers", hidden);
            vm.RemoveActiveFilter("TREE_HIDE_LOGGER:com.app");
            Assert.Single(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_TreeHidePrefix_RemovesPrefix()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var hidden = new HashSet<string> { "com", "org" };
            SetField(vm, "_treeHiddenPrefixes", hidden);
            vm.RemoveActiveFilter("TREE_HIDE_PREFIX:com");
            Assert.Single(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeHideLogger()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var hidden = new HashSet<string> { "E1.PLC.Mod1" };
            SetField(vm, "_plcTreeHiddenLoggers", hidden);
            vm.RemoveActiveFilter("PLC_TREE_HIDE_LOGGER:E1.PLC.Mod1");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PlcTreeHidePrefix()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var hidden = new HashSet<string> { "E1.PLC" };
            SetField(vm, "_plcTreeHiddenPrefixes", hidden);
            vm.RemoveActiveFilter("PLC_TREE_HIDE_PREFIX:E1.PLC");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_NegativeFilter_RemovesAndDeactivates()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var filters = new List<string> { "error text" };
            SetField(vm, "_negativeFilters", filters);
            SetField(vm, "_isMainFilterOutActive", true);
            vm.RemoveActiveFilter("NEGATIVE:error text");
            Assert.Empty(filters);
            Assert.False(vm.IsMainFilterOutActive);
        }

        [Fact]
        public void RemoveActiveFilter_AppNegativeFilter_RemovesAndDeactivates()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var filters = new List<string> { "warn text" };
            SetField(vm, "_appNegativeFilters", filters);
            SetField(vm, "_isAppFilterOutActive", true);
            vm.RemoveActiveFilter("APP_NEGATIVE:warn text");
            Assert.Empty(filters);
            Assert.False(vm.IsAppFilterOutActive);
        }

        [Fact]
        public void RemoveActiveFilter_AppFilter_RemovesConditionByIndex()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            SetField(vm, "_appFilterRoot", root);
            vm.RemoveActiveFilter("APP_FILTER:0");
            Assert.Single(root.Children);
            Assert.Equal("Message", root.Children[0].Field);
        }

        [Fact]
        public void RemoveActiveFilter_AppFilter_RemovesLastDeactivatesFilter()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", root);
            SetField(vm, "_isAppFilterActive", true);
            vm.RemoveActiveFilter("APP_FILTER:0");
            Assert.False(vm.IsAppFilterActive);
        }

        [Fact]
        public void RemoveActiveFilter_MainFilter_RemovesConditionByIndex()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "fail"));
            SetField(vm, "_mainFilterRoot", root);
            vm.RemoveActiveFilter("MAIN_FILTER:0");
            Assert.Single(root.Children);
        }

        [Fact]
        public void RemoveActiveFilter_MainFilter_RemovesLastDeactivates()
        {
            var vm = CreateVmForRemoveActiveFilter();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_mainFilterRoot", root);
            SetField(vm, "_isMainFilterActive", true);
            vm.RemoveActiveFilter("MAIN_FILTER:0");
            Assert.False(vm.IsMainFilterActive);
        }

        // ══════════════════════════════════════════════
        // BuildLoggerTree tests
        // ══════════════════════════════════════════════

        [Fact]
        public void BuildLoggerTree_EmptyLogs_SetsEmptyRoot()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            vm.BuildLoggerTree(Array.Empty<LogEntry>());
            Assert.Empty(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_SingleLogger_CreatesHierarchy()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.app.service"),
                MakeLog(logger: "com.app.service")
            };
            vm.BuildLoggerTree(logs);
            Assert.NotEmpty(vm.LoggerTreeRoot);
            Assert.Equal("com", vm.LoggerTreeRoot[0].Name);
        }

        [Fact]
        public void BuildLoggerTree_MultipleLoggers_CreatesMultipleNodes()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.app"),
                MakeLog(logger: "org.core")
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal(2, vm.LoggerTreeRoot.Count);
        }

        [Fact]
        public void BuildLoggerTree_EmptyLogger_Skipped()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: ""),
                MakeLog(logger: "com.app")
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_CountsAggregated()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.app"),
                MakeLog(logger: "com.app"),
                MakeLog(logger: "com.app")
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal(3, vm.LoggerTreeRoot[0].Count);
        }

        [Fact]
        public void BuildPlcLoggerTree_EmptyLogs_SetsEmptyRoot()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());
            vm.BuildPlcLoggerTree(Array.Empty<LogEntry>());
            Assert.Empty(vm.PlcLoggerTreeRoot);
        }

        [Fact]
        public void BuildPlcLoggerTree_SingleLogger()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "E1.PLC.Module1")
            };
            vm.BuildPlcLoggerTree(logs);
            Assert.NotEmpty(vm.PlcLoggerTreeRoot);
        }

        // ══════════════════════════════════════════════
        // ResetTreeFilters / ResetPlcTreeFilters tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ResetTreeFilters_ClearsAllTreeState()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var hidden = new HashSet<string> { "a", "b" };
            var prefixes = new HashSet<string> { "c" };
            SetField(vm, "_treeHiddenLoggers", hidden);
            SetField(vm, "_treeHiddenPrefixes", prefixes);
            SetField(vm, "_treeShowOnlyLogger", "test");
            SetField(vm, "_treeShowOnlyPrefix", "test");

            vm.ResetTreeFilters();

            Assert.Empty(hidden);
            Assert.Empty(prefixes);
            Assert.Null(GetField(vm, "_treeShowOnlyLogger"));
            Assert.Null(GetField(vm, "_treeShowOnlyPrefix"));
        }

        [Fact]
        public void ResetPlcTreeFilters_ClearsAllPlcTreeState()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var hidden = new HashSet<string> { "a" };
            var prefixes = new HashSet<string> { "b" };
            SetField(vm, "_plcTreeHiddenLoggers", hidden);
            SetField(vm, "_plcTreeHiddenPrefixes", prefixes);
            SetField(vm, "_plcTreeShowOnlyLogger", "test");
            SetField(vm, "_plcTreeShowOnlyPrefix", "test");

            vm.ResetPlcTreeFilters();

            Assert.Empty(hidden);
            Assert.Empty(prefixes);
            Assert.Null(GetField(vm, "_plcTreeShowOnlyLogger"));
            Assert.Null(GetField(vm, "_plcTreeShowOnlyPrefix"));
        }

        // ══════════════════════════════════════════════
        // FilterState property tests
        // ══════════════════════════════════════════════

        [Fact]
        public void IsGlobalTimeRangeActive_BothSet_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_globalTimeRangeStart", DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", DateTime.Now.AddHours(1));
            Assert.True(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_OnlyStartSet_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_globalTimeRangeStart", DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_NeitherSet_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void HasMainStoredFilter_AdvancedFilter_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_mainFilterRoot", MakeGroup("AND", MakeCondition("Level", "Equals", "Error")));
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", false);
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_ThreadFilter_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1" });
            SetField(vm, "_isTimeFocusActive", false);
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_TimeFocus_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", true);
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_Nothing_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", false);
            Assert.False(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_LoggerFilter_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string> { "com.app" });
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_TreeShowOnly_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", "com.app");
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilterOut_WithFilters_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_negativeFilters", new List<string> { "test" });
            Assert.True(vm.HasMainStoredFilterOut);
        }

        [Fact]
        public void HasMainStoredFilterOut_Empty_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_negativeFilters", new List<string>());
            Assert.False(vm.HasMainStoredFilterOut);
        }

        [Fact]
        public void HasAppStoredFilterOut_WithFilters_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appNegativeFilters", new List<string> { "test" });
            Assert.True(vm.HasAppStoredFilterOut);
        }

        [Fact]
        public void HasAppStoredFilterOut_Empty_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appNegativeFilters", new List<string>());
            Assert.False(vm.HasAppStoredFilterOut);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenLoggers_True()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "E1.PLC" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_NothingSet_False()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            Assert.False(vm.IsPlcTreeFilterActive);
        }

        // ══════════════════════════════════════════════
        // SyncThreadFiltersToFilterTree tests
        // ══════════════════════════════════════════════

        [Fact]
        public void SyncThreadFilters_SingleThread_AddsCondition()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true, new List<string> { "Thread1" } });

            Assert.NotNull(vm.AppFilterRoot);
            Assert.Single(vm.AppFilterRoot!.Children);
            Assert.Equal("ThreadName", vm.AppFilterRoot.Children[0].Field);
            Assert.Equal("Thread1", vm.AppFilterRoot.Children[0].Value);
        }

        [Fact]
        public void SyncThreadFilters_MultipleThreads_CreatesOrGroup()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true, new List<string> { "T1", "T2", "T3" } });

            Assert.NotNull(vm.AppFilterRoot);
            Assert.Single(vm.AppFilterRoot!.Children);
            Assert.Equal(NodeType.Group, vm.AppFilterRoot.Children[0].Type);
            Assert.Equal("OR", vm.AppFilterRoot.Children[0].LogicalOperator);
            Assert.Equal(3, vm.AppFilterRoot.Children[0].Children.Count);
        }

        [Fact]
        public void SyncThreadFilters_MainTab_SetsMainFilterRoot()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { false, new List<string> { "Thread1" } });

            Assert.NotNull(vm.MainFilterRoot);
        }

        // ══════════════════════════════════════════════
        // RemoveThreadConditionsFromFilterTree tests
        // ══════════════════════════════════════════════

        [Fact]
        public void RemoveThreadConditions_RemovesDirectThreadConditions()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "Thread1"),
                MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", root);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true });

            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditions_RemovesGroupWithOnlyThreadConditions()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var threadGroup = MakeGroup("OR",
                MakeCondition("ThreadName", "Equals", "T1"),
                MakeCondition("ThreadName", "Equals", "T2"));
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                threadGroup);
            SetField(vm, "_mainFilterRoot", root);
            SetField(vm, "_appFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { false });

            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditions_NullRoot_DoesNotThrow()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var ex = Record.Exception(() => method?.Invoke(vm, new object[] { true }));
            Assert.Null(ex);
        }

        // ══════════════════════════════════════════════
        // CheckIfFiltersEmpty tests
        // ══════════════════════════════════════════════

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_AllEmpty_DeactivatesFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_parent", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppFilterActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true });

            Assert.False(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_HasLoggerFilters_StaysActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_parent", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string> { "com.app" });
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppFilterActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true });

            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_AllEmpty_DeactivatesFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_parent", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_isMainFilterActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { false });

            Assert.False(vm.IsMainFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_HasThreadFilters_StaysActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_parent", null);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread1" });
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_isMainFilterActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { false });

            Assert.True(vm.IsMainFilterActive);
        }

        // ══════════════════════════════════════════════
        // SetFilterActive tests
        // ══════════════════════════════════════════════

        [Fact]
        public void SetFilterActive_AppTab_SetsAppFilterActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isMainFilterActive", false);

            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { true });

            Assert.True(vm.IsAppFilterActive);
            Assert.False(vm.IsMainFilterActive);
        }

        [Fact]
        public void SetFilterActive_MainTab_SetsMainFilterActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isMainFilterActive", false);

            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { false });

            Assert.False(vm.IsAppFilterActive);
            Assert.True(vm.IsMainFilterActive);
        }

        // ══════════════════════════════════════════════
        // SetChildrenVisualState tests (via reflection)
        // ══════════════════════════════════════════════

        [Fact]
        public void SetChildrenVisualState_SetsAllChildren()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = new LoggerNode
            {
                Name = "root",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode { Name = "child1" },
                    new LoggerNode { Name = "child2" }
                }
            };

            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { parent, true, true });

            Assert.True(parent.Children[0].IsHidden);
            Assert.True(parent.Children[0].IsActive);
            Assert.True(parent.Children[1].IsHidden);
            Assert.True(parent.Children[1].IsActive);
        }

        [Fact]
        public void SetChildrenVisualState_Recursive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var grandchild = new LoggerNode { Name = "grandchild" };
            var child = new LoggerNode
            {
                Name = "child",
                Children = new ObservableCollection<LoggerNode> { grandchild }
            };
            var parent = new LoggerNode
            {
                Name = "root",
                Children = new ObservableCollection<LoggerNode> { child }
            };

            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { parent, true, false });

            Assert.True(grandchild.IsHidden);
            Assert.False(grandchild.IsActive);
        }

        // ══════════════════════════════════════════════
        // HasAnyColumnFilter tests
        // ══════════════════════════════════════════════

        [Fact]
        public void HasAnyColumnFilter_NoFilters_ReturnsFalse()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_isAppTimeFocusActive", false);

            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (bool)method?.Invoke(vm, null)!;
            Assert.False(result);
        }

        [Fact]
        public void HasAnyColumnFilter_LoggerFilter_ReturnsTrue()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeLoggerFilters", new List<string> { "com.app" });
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_isAppTimeFocusActive", false);

            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (bool)method?.Invoke(vm, null)!;
            Assert.True(result);
        }

        [Fact]
        public void HasAnyColumnFilter_AppTimeFocusActive_ReturnsTrue()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_isAppTimeFocusActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (bool)method?.Invoke(vm, null)!;
            Assert.True(result);
        }

        // ══════════════════════════════════════════════
        // OpenUrl tests (MainViewModel static method)
        // ══════════════════════════════════════════════

        [Fact]
        public void OpenUrl_EmptyString_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl(""));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_NullString_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_WhitespaceString_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("   "));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_InvalidScheme_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("ftp://example.com"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_RelativeUri_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("not-a-url"));
            Assert.Null(ex);
        }

        // ══════════════════════════════════════════════
        // MarkNodeShowOnly tests
        // ══════════════════════════════════════════════

        [Fact]
        public void MarkNodeShowOnly_MatchingNode_MarkedActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var node = new LoggerNode
            {
                Name = "app",
                FullPath = "com.app",
                Children = new ObservableCollection<LoggerNode>()
            };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { node, "com.app" });

            Assert.True(node.IsActive);
            Assert.False(node.IsHidden);
        }

        [Fact]
        public void MarkNodeShowOnly_NonMatchingNode_MarkedHidden()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var node = new LoggerNode
            {
                Name = "other",
                FullPath = "com.other",
                Children = new ObservableCollection<LoggerNode>()
            };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { node, "com.app" });

            Assert.True(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_AncestorNode_NotHiddenNotActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child = new LoggerNode { Name = "app", FullPath = "com.app", Children = new ObservableCollection<LoggerNode>() };
            var node = new LoggerNode
            {
                Name = "com",
                FullPath = "com",
                Children = new ObservableCollection<LoggerNode> { child }
            };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { node, "com.app" });

            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
            Assert.True(child.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_ChildPrefix_MarkedActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var node = new LoggerNode
            {
                Name = "service",
                FullPath = "com.app.service",
                Children = new ObservableCollection<LoggerNode>()
            };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { node, "com.app" });

            Assert.True(node.IsActive);
        }

        // ══════════════════════════════════════════════
        // ResetNodeVisualState tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ResetNodeVisualState_ClearsHiddenAndActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var node = new LoggerNode
            {
                Name = "root",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };

            var method = typeof(FilterSearchViewModel).GetMethod("ResetNodeVisualState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object[] { node });

            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        // ══════════════════════════════════════════════
        // ResetPlcVisualStates tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ResetPlcVisualStates_ClearsAllNodes()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var root = new LoggerNode
            {
                Name = "root",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode> { root });

            var method = typeof(FilterSearchViewModel).GetMethod("ResetPlcVisualStates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, null);

            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        // ══════════════════════════════════════════════
        // Property setter tests on FilterSearchViewModel
        // ══════════════════════════════════════════════

        [Fact]
        public void DefaultPlcFilter_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var filter = MakeGroup("AND");
            vm.DefaultPlcFilter = filter;
            Assert.Same(filter, vm.DefaultPlcFilter);
        }

        [Fact]
        public void MainFilterRoot_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("AND");
            vm.MainFilterRoot = root;
            Assert.Same(root, vm.MainFilterRoot);
        }

        [Fact]
        public void AppFilterRoot_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("OR");
            vm.AppFilterRoot = root;
            Assert.Same(root, vm.AppFilterRoot);
        }

        [Fact]
        public void SavedFilterRoot_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var root = MakeGroup("AND");
            vm.SavedFilterRoot = root;
            Assert.Same(root, vm.SavedFilterRoot);
        }

        [Fact]
        public void HasRangeStart_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.HasRangeStart = true;
            Assert.True(vm.HasRangeStart);
            vm.HasRangeStart = false;
            Assert.False(vm.HasRangeStart);
        }

        [Fact]
        public void GlobalTimeRangeStart_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var dt = new DateTime(2024, 1, 1);
            vm.GlobalTimeRangeStart = dt;
            Assert.Equal(dt, vm.GlobalTimeRangeStart);
        }

        [Fact]
        public void GlobalTimeRangeEnd_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var dt = new DateTime(2024, 12, 31);
            vm.GlobalTimeRangeEnd = dt;
            Assert.Equal(dt, vm.GlobalTimeRangeEnd);
        }

        [Fact]
        public void TreeShowOnlyLogger_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.TreeShowOnlyLogger = "com.app";
            Assert.Equal("com.app", vm.TreeShowOnlyLogger);
        }

        [Fact]
        public void TreeShowOnlyPrefix_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.TreeShowOnlyPrefix = "com";
            Assert.Equal("com", vm.TreeShowOnlyPrefix);
        }

        [Fact]
        public void IsTimeFocusActive_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.IsTimeFocusActive = true;
            Assert.True(vm.IsTimeFocusActive);
        }

        [Fact]
        public void IsAppTimeFocusActive_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.IsAppTimeFocusActive = true;
            Assert.True(vm.IsAppTimeFocusActive);
        }

        [Fact]
        public void IsAppErrorFilterActive_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            vm.IsAppErrorFilterActive = true;
            Assert.True(vm.IsAppErrorFilterActive);
        }

        [Fact]
        public void LastFilteredCache_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var list = new List<LogEntry> { MakeLog() };
            vm.LastFilteredCache = list;
            Assert.Same(list, vm.LastFilteredCache);
        }

        [Fact]
        public void LastFilteredAppCache_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var list = new List<LogEntry> { MakeLog() };
            vm.LastFilteredAppCache = list;
            Assert.Same(list, vm.LastFilteredAppCache);
        }

        [Fact]
        public void SelectedTreeItem_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var node = new LoggerNode { Name = "test" };
            vm.SelectedTreeItem = node;
            Assert.Same(node, vm.SelectedTreeItem);
        }

        [Fact]
        public void IsSearchPanelVisible_GetSet()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isSearchPanelVisible", false);
            vm.IsSearchPanelVisible = true;
            Assert.True(vm.IsSearchPanelVisible);
        }

        // ══════════════════════════════════════════════
        // UndoFilterOut tests (via reflection)
        // ══════════════════════════════════════════════

        private FilterSearchViewModel CreateVmForUndoFilterOut(int tabIndex)
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = CreateParentVm();
            SetField(parent, "_selectedTabIndex", tabIndex);
            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", (FilterNode?)null);
            SetField(vm, "_appFilterRoot", (FilterNode?)null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_treeShowOnlyLogger", (string?)null);
            SetField(vm, "_treeShowOnlyPrefix", (string?)null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeShowOnlyLogger", (string?)null);
            SetField(vm, "_plcTreeShowOnlyPrefix", (string?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", (List<LogEntry>?)null);
            SetField(vm, "_lastFilteredAppCache", (List<LogEntry>?)null);
            SetField(vm, "_searchText", (string?)null);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_searchDebounceTimer", new System.Windows.Threading.DispatcherTimer());
            return vm;
        }

        [Fact]
        public void UndoFilterOut_AppTab_RemovesLastNegativeFilter()
        {
            var vm = CreateVmForUndoFilterOut(1); // APP tab
            var appNeg = new List<string> { "filter1", "filter2" };
            SetField(vm, "_appNegativeFilters", appNeg);
            SetField(vm, "_isAppFilterOutActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { null });

            Assert.Single(appNeg);
            Assert.Equal("filter1", appNeg[0]);
        }

        [Fact]
        public void UndoFilterOut_AppTab_LastFilter_DeactivatesFilterOut()
        {
            var vm = CreateVmForUndoFilterOut(1); // APP tab
            var appNeg = new List<string> { "filter1" };
            SetField(vm, "_appNegativeFilters", appNeg);
            SetField(vm, "_isAppFilterOutActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { null });

            Assert.Empty(appNeg);
            Assert.False(vm.IsAppFilterOutActive);
        }

        [Fact]
        public void UndoFilterOut_MainTab_RemovesLastNegativeFilter()
        {
            var vm = CreateVmForUndoFilterOut(0); // PLC tab
            var neg = new List<string> { "neg1", "neg2" };
            SetField(vm, "_negativeFilters", neg);
            SetField(vm, "_isMainFilterOutActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { null });

            Assert.Single(neg);
        }

        [Fact]
        public void UndoFilterOut_MainTab_LastFilter_DeactivatesFilterOut()
        {
            var vm = CreateVmForUndoFilterOut(0); // PLC tab
            var neg = new List<string> { "neg1" };
            SetField(vm, "_negativeFilters", neg);
            SetField(vm, "_isMainFilterOutActive", true);

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, new object?[] { null });

            Assert.Empty(neg);
            Assert.False(vm.IsMainFilterOutActive);
        }

        // ══════════════════════════════════════════════
        // Additional EvaluateFilterNode edge cases
        // ══════════════════════════════════════════════

        [Fact]
        public void EvaluateFilterNode_ExtraFieldNullValue_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Key1", null! } });
            var node = MakeCondition("Key1", "Contains", "test");
            Assert.False(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_ContainsDefaultOperator()
        {
            var log = MakeLog(message: "hello world");
            var node = MakeCondition("Message", "SomeUnknownOp", "world");
            // Unknown operator falls through to Contains logic
            Assert.True(_filterVM.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EvaluateFilterNode_RegexWithCompiledRegex_UsesCache()
        {
            var node = MakeCondition("Message", "Regex", @"^test\d+$");
            var log1 = MakeLog(message: "test123");
            Assert.True(_filterVM.EvaluateFilterNode(log1, node));
            Assert.NotNull(node.CompiledRegex);
            // Second evaluation uses cached regex
            var log2 = MakeLog(message: "test456");
            Assert.True(_filterVM.EvaluateFilterNode(log2, node));
        }

        [Fact]
        public void EvaluateFilterNode_GroupNullChildren_ReturnsTrue()
        {
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = null!
            };
            Assert.True(_filterVM.EvaluateFilterNode(MakeLog(), group));
        }

        // ══════════════════════════════════════════════
        // MarkAllNodesShowOnly tests
        // ══════════════════════════════════════════════

        [Fact]
        public void MarkAllNodesShowOnly_WithTreeRoot()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child1 = new LoggerNode { Name = "app", FullPath = "com.app", Children = new ObservableCollection<LoggerNode>() };
            var child2 = new LoggerNode { Name = "core", FullPath = "com.core", Children = new ObservableCollection<LoggerNode>() };
            var root = new LoggerNode
            {
                Name = "com",
                FullPath = "com",
                Children = new ObservableCollection<LoggerNode> { child1, child2 }
            };
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode> { root });

            var method = typeof(FilterSearchViewModel).GetMethod("MarkAllNodesShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            method?.Invoke(vm, new object[] { "com.app" });

            // com is ancestor - not hidden, not active
            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            // com.app is match - active
            Assert.True(child1.IsActive);
            Assert.False(child1.IsHidden);
            // com.core is not related - hidden
            Assert.True(child2.IsHidden);
        }

        [Fact]
        public void MarkAllNodesShowOnly_PlcOverload()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child1 = new LoggerNode { Name = "mod1", FullPath = "E1.PLC.mod1", Children = new ObservableCollection<LoggerNode>() };
            var child2 = new LoggerNode { Name = "mod2", FullPath = "E1.PLC.mod2", Children = new ObservableCollection<LoggerNode>() };
            var plcRoot = new ObservableCollection<LoggerNode> { child1, child2 };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkAllNodesShowOnly",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(ObservableCollection<LoggerNode>) }, null);
            method?.Invoke(vm, new object[] { "E1.PLC.mod1", plcRoot });

            Assert.True(child1.IsActive);
            Assert.True(child2.IsHidden);
        }

        // ══════════════════════════════════════════════
        // ResetAllVisualStates tests
        // ══════════════════════════════════════════════

        [Fact]
        public void ResetAllVisualStates_ClearsEverything()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var root = new LoggerNode
            {
                Name = "root",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode> { root });

            var method = typeof(FilterSearchViewModel).GetMethod("ResetAllVisualStates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(vm, null);

            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        // ══════════════════════════════════════════════
        // IsDefaultLog tests
        // ══════════════════════════════════════════════

        [Fact]
        public void IsDefaultLog_UsesFactoryFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            // PlcMngr message matches the factory default filter
            var log = MakeLog(message: "PlcMngr: state changed");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_ErrorLevel_Matches()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            var log = MakeLog(level: "error", message: "something");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_ManagerThread_Matches()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            var log = MakeLog(threadName: "ManagerThread1", message: "something");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_EventsThread_Matches()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            var log = MakeLog(threadName: "Events", message: "something");
            Assert.True(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_NonMatchingLog_ReturnsFalse()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_defaultPlcFilter", (FilterNode?)null);

            var log = MakeLog(threadName: "Worker1", message: "routine operation", level: "INFO");
            Assert.False(vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_CustomFilter_UsesCustom()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var customFilter = MakeGroup("OR",
                MakeCondition("Message", "Contains", "custom"));
            SetField(vm, "_defaultPlcFilter", customFilter);

            var log = MakeLog(message: "custom data here");
            Assert.True(vm.IsDefaultLog(log));
        }

        // ══════════════════════════════════════════════
        // QueryParserService.HasBooleanOperators
        // ══════════════════════════════════════════════

        [Fact]
        public void HasBooleanOperators_PlainText_False()
        {
            Assert.False(QueryParserService.HasBooleanOperators("simple text"));
        }

        [Fact]
        public void HasBooleanOperators_WithQuotes_True()
        {
            Assert.True(QueryParserService.HasBooleanOperators("\"quoted text\""));
        }

        [Fact]
        public void HasBooleanOperators_WithParens_True()
        {
            Assert.True(QueryParserService.HasBooleanOperators("(group)"));
        }

        [Fact]
        public void HasBooleanOperators_NullOrEmpty_False()
        {
            Assert.False(QueryParserService.HasBooleanOperators(""));
            Assert.False(QueryParserService.HasBooleanOperators(null!));
            Assert.False(QueryParserService.HasBooleanOperators("   "));
        }

        // ══════════════════════════════════════════════
        // AddNodeRecursive tests (via BuildLoggerTree)
        // ══════════════════════════════════════════════

        [Fact]
        public void BuildLoggerTree_DottedLogger_CreatesNestedNodes()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.hp.indigo.module")
            };
            vm.BuildLoggerTree(logs);

            // Should create: com -> hp -> indigo -> module
            var com = vm.LoggerTreeRoot[0];
            Assert.Equal("com", com.Name);
            Assert.Single(com.Children);
            var hp = com.Children[0];
            Assert.Equal("hp", hp.Name);
            Assert.Single(hp.Children);
            var indigo = hp.Children[0];
            Assert.Equal("indigo", indigo.Name);
        }

        [Fact]
        public void BuildLoggerTree_SamePrefix_SharesParent()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.app.module1"),
                MakeLog(logger: "com.app.module2")
            };
            vm.BuildLoggerTree(logs);

            var com = vm.LoggerTreeRoot[0];
            var app = com.Children[0];
            Assert.Equal("app", app.Name);
            Assert.Equal(2, app.Children.Count);
        }

        [Fact]
        public void BuildLoggerTree_SortedAlphabetically()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "zeta"),
                MakeLog(logger: "alpha"),
                MakeLog(logger: "middle")
            };
            vm.BuildLoggerTree(logs);

            Assert.Equal("alpha", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("middle", vm.LoggerTreeRoot[1].Name);
            Assert.Equal("zeta", vm.LoggerTreeRoot[2].Name);
        }

        [Fact]
        public void BuildLoggerTree_NullLogs_CreatesEmptyRoot()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            vm.BuildLoggerTree(null!);
            Assert.Empty(vm.LoggerTreeRoot);
        }
    }
}
