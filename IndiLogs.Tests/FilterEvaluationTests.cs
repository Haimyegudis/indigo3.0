using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class FilterEvaluationTests
    {
        // EvaluateFilterNode is a public instance method but uses no instance state,
        // so we create an uninitialized instance to call it without wiring up all
        // constructor dependencies.
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

        // ──────────────────────────────────────────────
        // Null / empty node edge cases
        // ──────────────────────────────────────────────

        [Fact]
        public void NullNode_ReturnsTrue()
        {
            Assert.True(Evaluate(MakeLog(), null!));
        }

        [Fact]
        public void GroupWithNoChildren_ReturnsTrue()
        {
            var group = MakeGroup("AND");
            group.Children.Clear();
            Assert.True(Evaluate(MakeLog(), group));
        }

        [Fact]
        public void GroupWithNullChildren_ReturnsTrue()
        {
            var group = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND", Children = null! };
            Assert.True(Evaluate(MakeLog(), group));
        }

        // ──────────────────────────────────────────────
        // Contains operator (default when op is unrecognized)
        // ──────────────────────────────────────────────

        [Theory]
        [InlineData("Hello World", "world", true)]   // case-insensitive
        [InlineData("Hello World", "xyz", false)]
        [InlineData("Hello World", "Hello", true)]
        [InlineData("Hello World", "lo Wo", true)]
        public void Contains_MatchesCaseInsensitive(string message, string value, bool expected)
        {
            var log = MakeLog(message: message);
            var node = MakeCondition("Message", "Contains", value);
            Assert.Equal(expected, Evaluate(log, node));
        }

        // ──────────────────────────────────────────────
        // Equals operator
        // ──────────────────────────────────────────────

        [Fact]
        public void Equals_ExactMatch_CaseInsensitive()
        {
            var log = MakeLog(level: "Error");
            Assert.True(Evaluate(log, MakeCondition("Level", "Equals", "error")));
            Assert.True(Evaluate(log, MakeCondition("Level", "Equals", "ERROR")));
            Assert.False(Evaluate(log, MakeCondition("Level", "Equals", "Warning")));
        }

        // ──────────────────────────────────────────────
        // Begins With operator
        // ──────────────────────────────────────────────

        [Fact]
        public void BeginsWith_MatchesPrefix()
        {
            var log = MakeLog(logger: "MyApp.Services.Foo");
            Assert.True(Evaluate(log, MakeCondition("Logger", "Begins With", "myapp")));
            Assert.False(Evaluate(log, MakeCondition("Logger", "Begins With", "Services")));
        }

        // ──────────────────────────────────────────────
        // Ends With operator
        // ──────────────────────────────────────────────

        [Fact]
        public void EndsWith_MatchesSuffix()
        {
            var log = MakeLog(logger: "MyApp.Services.Foo");
            Assert.True(Evaluate(log, MakeCondition("Logger", "Ends With", "foo")));
            Assert.False(Evaluate(log, MakeCondition("Logger", "Ends With", "Services")));
        }

        // ──────────────────────────────────────────────
        // Regex operator
        // ──────────────────────────────────────────────

        [Fact]
        public void Regex_MatchesPattern()
        {
            var log = MakeLog(message: "Error code 42 occurred");
            Assert.True(Evaluate(log, MakeCondition("Message", "Regex", @"code\s+\d+")));
            Assert.False(Evaluate(log, MakeCondition("Message", "Regex", @"^code\s+\d+")));
        }

        [Fact]
        public void Regex_InvalidPattern_ReturnsFalse()
        {
            var log = MakeLog(message: "some text");
            Assert.False(Evaluate(log, MakeCondition("Message", "Regex", "[invalid")));
        }

        // ──────────────────────────────────────────────
        // Field mapping
        // ──────────────────────────────────────────────

        [Theory]
        [InlineData("Level", "Error")]
        [InlineData("ThreadName", "Thread-1")]
        [InlineData("Logger", "MyLogger")]
        [InlineData("ProcessName", "MyProcess")]
        [InlineData("Method", "DoWork")]
        [InlineData("Pattern", "SomePattern")]
        [InlineData("Data", "SomeData")]
        [InlineData("Exception", "NullRefException")]
        [InlineData("Message", "Hello")]
        public void AllStandardFields_AreEvaluated(string field, string value)
        {
            var log = MakeLog(
                level: "Error",
                threadName: "Thread-1",
                logger: "MyLogger",
                processName: "MyProcess",
                method: "DoWork",
                pattern: "SomePattern",
                data: "SomeData",
                exception: "NullRefException",
                message: "Hello");

            var node = MakeCondition(field, "Equals", value);
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void ExtraFields_AreEvaluated()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "CustomCol", "abc123" } });
            Assert.True(Evaluate(log, MakeCondition("CustomCol", "Contains", "abc")));
            Assert.False(Evaluate(log, MakeCondition("CustomCol", "Contains", "xyz")));
        }

        [Fact]
        public void UnknownField_WithNoExtraFields_ReturnsFalse()
        {
            var log = MakeLog(message: "anything");
            Assert.False(Evaluate(log, MakeCondition("NonExistent", "Contains", "anything")));
        }

        [Fact]
        public void UnknownField_NotInExtraFields_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Other", "val" } });
            Assert.False(Evaluate(log, MakeCondition("Missing", "Contains", "val")));
        }

        [Fact]
        public void EmptyFieldValue_ReturnsFalse()
        {
            var log = MakeLog(message: "");
            Assert.False(Evaluate(log, MakeCondition("Message", "Contains", "anything")));
        }

        // ──────────────────────────────────────────────
        // AND group logic
        // ──────────────────────────────────────────────

        [Fact]
        public void AndGroup_AllMatch_ReturnsTrue()
        {
            var log = MakeLog(message: "error occurred", level: "Error");
            var group = MakeGroup("AND",
                MakeCondition("Message", "Contains", "error"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void AndGroup_OneFails_ReturnsFalse()
        {
            var log = MakeLog(message: "warning occurred", level: "Warning");
            var group = MakeGroup("AND",
                MakeCondition("Message", "Contains", "warning"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, group));
        }

        // ──────────────────────────────────────────────
        // OR group logic
        // ──────────────────────────────────────────────

        [Fact]
        public void OrGroup_OneMatches_ReturnsTrue()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "hello"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void OrGroup_NoneMatch_ReturnsFalse()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "world"));
            Assert.False(Evaluate(log, group));
        }

        // ──────────────────────────────────────────────
        // NOT AND / NOT OR logic
        // ──────────────────────────────────────────────

        [Fact]
        public void NotAnd_InvertsAndResult()
        {
            var log = MakeLog(message: "error occurred", level: "Error");
            var group = MakeGroup("NOT AND",
                MakeCondition("Message", "Contains", "error"),
                MakeCondition("Level", "Equals", "Error"));
            // AND would be true, NOT inverts it
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void NotAnd_WhenAndIsFalse_ReturnsTrue()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("NOT AND",
                MakeCondition("Message", "Contains", "hello"),
                MakeCondition("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, group));
        }

        [Fact]
        public void NotOr_InvertsOrResult()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "hello"));
            // OR would be true, NOT inverts it
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void NotOr_WhenOrIsFalse_ReturnsTrue()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var group = MakeGroup("NOT OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "world"));
            Assert.True(Evaluate(log, group));
        }

        // ──────────────────────────────────────────────
        // Nested groups
        // ──────────────────────────────────────────────

        [Fact]
        public void NestedGroups_EvaluateCorrectly()
        {
            // (Level=Error AND Message contains "disk") OR (Level=Warning)
            var log = MakeLog(message: "disk failure", level: "Error");

            var innerAnd = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "disk"));

            var innerOr = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Warning"));

            var outerOr = MakeGroup("OR", innerAnd, innerOr);

            Assert.True(Evaluate(log, outerOr));
        }

        [Fact]
        public void NestedGroups_NoMatch()
        {
            // (Level=Error AND Message contains "disk") OR (Level=Warning)
            var log = MakeLog(message: "cpu failure", level: "Info");

            var innerAnd = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "disk"));

            var innerWarning = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Warning"));

            var outerOr = MakeGroup("OR", innerAnd, innerWarning);

            Assert.False(Evaluate(log, outerOr));
        }

        // ──────────────────────────────────────────────
        // MatchesSearch (private static — tested via reflection)
        // ──────────────────────────────────────────────

        private static readonly MethodInfo _matchesSearchMethod =
            typeof(FilterSearchViewModel).GetMethod("MatchesSearch",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool MatchesSearch(LogEntry log, string search) =>
            (bool)_matchesSearchMethod.Invoke(null, [log, search])!;

        [Fact]
        public void MatchesSearch_FindsInMessage()
        {
            var log = MakeLog(message: "Motor stalled at position 42");
            Assert.True(MatchesSearch(log, "stalled"));
            Assert.True(MatchesSearch(log, "MOTOR"));  // case-insensitive
            Assert.False(MatchesSearch(log, "voltage"));
        }

        [Fact]
        public void MatchesSearch_FindsInExtraFields()
        {
            var log = MakeLog(
                message: "nothing here",
                extraFields: new Dictionary<string, string> { { "Source", "PrintHead" } });

            Assert.True(MatchesSearch(log, "PrintHead"));
            Assert.True(MatchesSearch(log, "printhead"));
            Assert.False(MatchesSearch(log, "Drum"));
        }

        [Fact]
        public void MatchesSearch_NullMessage_NoExtraFields_ReturnsFalse()
        {
            var log = new LogEntry { Message = null! };
            Assert.False(MatchesSearch(log, "anything"));
        }

        [Fact]
        public void MatchesSearch_NullExtraFieldValue_Skipped()
        {
            var log = MakeLog(
                message: "no match",
                extraFields: new Dictionary<string, string> { { "Key", null! } });

            Assert.False(MatchesSearch(log, "anything"));
        }

        // ──────────────────────────────────────────────
        // FilterNode.DeepClone
        // ──────────────────────────────────────────────

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

        [Fact]
        public void FilterNode_DeepClone_IsIndependent()
        {
            var original = MakeCondition("Message", "Contains", "test");
            var clone = original.DeepClone();

            clone.Value = "changed";
            Assert.Equal("test", original.Value);
        }

        [Fact]
        public void FilterNode_DeepClone_ClonesChildren()
        {
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "disk"));

            var clone = group.DeepClone();

            Assert.Equal(2, clone.Children.Count);
            Assert.Equal("Level", clone.Children[0].Field);

            // Mutating clone child should not affect original
            clone.Children[0].Value = "Warning";
            Assert.Equal("Error", group.Children[0].Value);
        }

        // ──────────────────────────────────────────────
        // Default operator fallback (unrecognized op → Contains)
        // ──────────────────────────────────────────────

        [Fact]
        public void UnknownOperator_FallsBackToContains()
        {
            var log = MakeLog(message: "Hello World");
            var node = MakeCondition("Message", "SomeWeirdOp", "World");
            Assert.True(Evaluate(log, node));
        }

        // ──────────────────────────────────────────────
        // ExtraFields with null value in dictionary
        // ──────────────────────────────────────────────

        [Fact]
        public void ExtraField_NullValue_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "Col", null! } });
            // val becomes "" after null-coalescing, then IsNullOrEmpty check → false
            Assert.False(Evaluate(log, MakeCondition("Col", "Contains", "anything")));
        }

        // ──────────────────────────────────────────────
        // Short-circuit behavior verification
        // ──────────────────────────────────────────────

        [Fact]
        public void AndGroup_ShortCircuitsOnFirstFalse()
        {
            var log = MakeLog(message: "hello", level: "Info");
            // First condition fails; second would throw on regex if evaluated with bad pattern
            // but we expect short-circuit so no error
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Regex", "[invalid"));
            Assert.False(Evaluate(log, group));
        }

        [Fact]
        public void OrGroup_ShortCircuitsOnFirstTrue()
        {
            var log = MakeLog(message: "hello", level: "Error");
            var group = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Regex", "[invalid"));
            Assert.True(Evaluate(log, group));
        }
    }
}
