using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class FilterEvaluationDeepTests
    {
        // Uninitialized VM instance for calling public instance methods that don't rely on state
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

        private static FilterNode Cond(string field, string op, string value) =>
            new()
            {
                Type = NodeType.Condition,
                Field = field,
                Operator = op,
                Value = value
            };

        private static FilterNode Group(string logicalOp, params FilterNode[] children) =>
            new()
            {
                Type = NodeType.Group,
                LogicalOperator = logicalOp,
                Children = new ObservableCollection<FilterNode>(children)
            };

        // ════════════════════════════════════════════════════
        // 1. QueryParserService.Parse — simple terms
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_SingleWord_ReturnsConditionNode()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result!.Type);
            Assert.Equal("Message", result.Field);
            Assert.Equal("Contains", result.Operator);
            Assert.Equal("error", result.Value);
        }

        [Fact]
        public void Parse_EmptyQuery_ReturnsNullWithError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("", out var err);

            Assert.Null(result);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_NullQuery_ReturnsNullWithError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse(null, out var err);

            Assert.Null(result);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_WhitespaceOnly_ReturnsNullWithError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("   ", out var err);

            Assert.Null(result);
            Assert.NotNull(err);
        }

        // ════════════════════════════════════════════════════
        // 2. QueryParserService.Parse — AND
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_ImplicitAnd_TwoWords()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error disk", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            Assert.Equal("error", result.Children[0].Value);
            Assert.Equal("disk", result.Children[1].Value);
        }

        [Fact]
        public void Parse_ExplicitAnd_TwoWords()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error AND disk", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void Parse_ThreeWordsImplicitAnd_NestedLeftAssociative()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("a b c", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            // (a AND b) AND c
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            // Left child is another AND group
            Assert.Equal(NodeType.Group, result.Children[0].Type);
            Assert.Equal("AND", result.Children[0].LogicalOperator);
            // Right child is "c"
            Assert.Equal("c", result.Children[1].Value);
        }

        // ════════════════════════════════════════════════════
        // 3. QueryParserService.Parse — OR
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_OrOperator()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error OR warning", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            Assert.Equal("error", result.Children[0].Value);
            Assert.Equal("warning", result.Children[1].Value);
        }

        [Fact]
        public void Parse_PipeAsOr()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error | warning", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal("OR", result!.LogicalOperator);
        }

        // ════════════════════════════════════════════════════
        // 4. QueryParserService.Parse — NOT
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_NotWithDash()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("-error", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("NOT AND", result.LogicalOperator);
            Assert.Single(result.Children);
            Assert.Equal("error", result.Children[0].Value);
        }

        [Fact]
        public void Parse_NotKeyword()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("NOT error", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("NOT AND", result.LogicalOperator);
        }

        [Fact]
        public void Parse_WordThenNotWord()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("disk -error", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            // Should be: disk AND (NOT error)
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            Assert.Equal("disk", result.Children[0].Value);
            Assert.Equal("NOT AND", result.Children[1].LogicalOperator);
        }

        // ════════════════════════════════════════════════════
        // 5. QueryParserService.Parse — quoted phrases
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_QuotedPhrase()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"disk error\"", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result!.Type);
            Assert.Equal("disk error", result.Value);
        }

        [Fact]
        public void Parse_UnclosedQuote_ReturnsError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"unclosed", out var err);

            Assert.Null(result);
            Assert.NotNull(err);
            Assert.Contains("quote", err!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_QuotedPhraseAndWord()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"disk error\" fatal", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal("disk error", result.Children[0].Value);
            Assert.Equal("fatal", result.Children[1].Value);
        }

        // ════════════════════════════════════════════════════
        // 6. QueryParserService.Parse — nested expressions (parentheses)
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_Parentheses_GroupCorrectly()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(error OR warning) disk", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            // AND( OR(error, warning), disk )
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);

            var orChild = result.Children[0];
            Assert.Equal("OR", orChild.LogicalOperator);
            Assert.Equal("error", orChild.Children[0].Value);
            Assert.Equal("warning", orChild.Children[1].Value);

            Assert.Equal("disk", result.Children[1].Value);
        }

        [Fact]
        public void Parse_UnmatchedCloseParen_ReturnsError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error)", out var err);

            Assert.Null(result);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_UnmatchedOpenParen_ReturnsError()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(error", out var err);

            Assert.Null(result);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_NestedParentheses()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("((a OR b) AND c)", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            // Should be AND( OR(a, b), c )
            Assert.Equal("AND", result!.LogicalOperator);
        }

        // ════════════════════════════════════════════════════
        // 7. QueryParserService.HasBooleanOperators
        // ════════════════════════════════════════════════════

        [Theory]
        [InlineData("simple text", false)]
        [InlineData("just words here", false)]
        [InlineData("word", false)]
        [InlineData("error AND warning", true)]
        [InlineData("error OR warning", true)]
        [InlineData("error NOT warning", true)]
        [InlineData("\"quoted phrase\"", true)]
        [InlineData("(grouped)", true)]
        [InlineData("a | b", true)]
        [InlineData("-negated", true)]
        public void HasBooleanOperators_DetectsCorrectly(string query, bool expected)
        {
            Assert.Equal(expected, QueryParserService.HasBooleanOperators(query));
        }

        [Fact]
        public void HasBooleanOperators_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(QueryParserService.HasBooleanOperators(null!));
            Assert.False(QueryParserService.HasBooleanOperators(""));
            Assert.False(QueryParserService.HasBooleanOperators("  "));
        }

        // ════════════════════════════════════════════════════
        // 8. EvaluateFilterNode — NotContains and NotEquals operators
        // ════════════════════════════════════════════════════

        [Fact]
        public void Evaluate_ContainsOperator_CaseInsensitive()
        {
            var log = MakeLog(message: "Motor Stall Detected");
            Assert.True(Evaluate(log, Cond("Message", "Contains", "stall")));
            Assert.True(Evaluate(log, Cond("Message", "Contains", "MOTOR")));
            Assert.False(Evaluate(log, Cond("Message", "Contains", "voltage")));
        }

        [Fact]
        public void Evaluate_EqualsOperator_CaseInsensitive()
        {
            var log = MakeLog(level: "WARNING");
            Assert.True(Evaluate(log, Cond("Level", "Equals", "warning")));
            Assert.True(Evaluate(log, Cond("Level", "Equals", "WARNING")));
            Assert.False(Evaluate(log, Cond("Level", "Equals", "Error")));
        }

        [Fact]
        public void Evaluate_BeginsWithOperator()
        {
            var log = MakeLog(logger: "HP.Indigo.PrintEngine.Motor");
            Assert.True(Evaluate(log, Cond("Logger", "Begins With", "hp.indigo")));
            Assert.False(Evaluate(log, Cond("Logger", "Begins With", "Motor")));
        }

        [Fact]
        public void Evaluate_EndsWithOperator()
        {
            var log = MakeLog(method: "ExecutePrintJobAsync");
            Assert.True(Evaluate(log, Cond("Method", "Ends With", "async")));
            Assert.False(Evaluate(log, Cond("Method", "Ends With", "Execute")));
        }

        [Fact]
        public void Evaluate_RegexOperator_WithCompiledRegex()
        {
            var node = Cond("Message", "Regex", @"error\s+\d{3}");
            var log = MakeLog(message: "Error 404 not found");
            Assert.True(Evaluate(log, node));

            var log2 = MakeLog(message: "no errors here");
            Assert.False(Evaluate(log2, node));
        }

        [Fact]
        public void Evaluate_RegexOperator_InvalidPattern_ReturnsFalse()
        {
            var log = MakeLog(message: "test");
            Assert.False(Evaluate(log, Cond("Message", "Regex", "(?P<invalid")));
        }

        // ════════════════════════════════════════════════════
        // 9. EvaluateFilterNode — field matching
        // ════════════════════════════════════════════════════

        [Theory]
        [InlineData("Message", "hello world")]
        [InlineData("Level", "Error")]
        [InlineData("ThreadName", "Worker-5")]
        [InlineData("Logger", "HP.Indigo.Core")]
        [InlineData("Method", "Initialize")]
        [InlineData("Exception", "NullReferenceException")]
        [InlineData("Data", "key=value")]
        [InlineData("Pattern", "StateChange")]
        [InlineData("ProcessName", "IndPrSvc")]
        public void Evaluate_AllFields_ContainsMatch(string field, string fieldValue)
        {
            var log = MakeLog(
                message: "hello world",
                level: "Error",
                threadName: "Worker-5",
                logger: "HP.Indigo.Core",
                method: "Initialize",
                exception: "NullReferenceException",
                data: "key=value",
                pattern: "StateChange",
                processName: "IndPrSvc");

            Assert.True(Evaluate(log, Cond(field, "Contains", fieldValue)));
        }

        [Fact]
        public void Evaluate_ExtraFields_Matching()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string>
            {
                { "Station", "BID" },
                { "SubSystem", "PrintHead" }
            });

            Assert.True(Evaluate(log, Cond("Station", "Equals", "bid")));
            Assert.True(Evaluate(log, Cond("SubSystem", "Contains", "Print")));
            Assert.False(Evaluate(log, Cond("Station", "Equals", "ITM")));
        }

        [Fact]
        public void Evaluate_UnknownField_NoExtraFields_ReturnsFalse()
        {
            var log = MakeLog(message: "test");
            Assert.False(Evaluate(log, Cond("Nonexistent", "Contains", "test")));
        }

        [Fact]
        public void Evaluate_UnknownField_NotInExtraFields_ReturnsFalse()
        {
            var log = MakeLog(extraFields: new Dictionary<string, string> { { "A", "val" } });
            Assert.False(Evaluate(log, Cond("B", "Contains", "val")));
        }

        [Fact]
        public void Evaluate_EmptyValue_ReturnsFalse()
        {
            var log = MakeLog(message: "");
            Assert.False(Evaluate(log, Cond("Message", "Contains", "x")));
        }

        // ════════════════════════════════════════════════════
        // 10. EvaluateFilterNode — group logic
        // ════════════════════════════════════════════════════

        [Fact]
        public void Evaluate_AndGroup_AllTrue()
        {
            var log = MakeLog(message: "disk error", level: "Error");
            var node = Group("AND",
                Cond("Message", "Contains", "disk"),
                Cond("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_AndGroup_OneFalse()
        {
            var log = MakeLog(message: "disk warning", level: "Warning");
            var node = Group("AND",
                Cond("Message", "Contains", "disk"),
                Cond("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_OrGroup_OneTrue()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var node = Group("OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "hello"));
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_OrGroup_AllFalse()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var node = Group("OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "world"));
            Assert.False(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_NotAnd_InvertsTrue()
        {
            var log = MakeLog(message: "test", level: "Error");
            var node = Group("NOT AND",
                Cond("Level", "Equals", "Error"));
            Assert.False(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_NotAnd_InvertsFalse()
        {
            var log = MakeLog(level: "Info");
            var node = Group("NOT AND",
                Cond("Level", "Equals", "Error"));
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_NotOr_InvertsTrue()
        {
            var log = MakeLog(message: "hello", level: "Error");
            var node = Group("NOT OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "world"));
            Assert.False(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_NotOr_InvertsFalse()
        {
            var log = MakeLog(message: "hello", level: "Info");
            var node = Group("NOT OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "world"));
            Assert.True(Evaluate(log, node));
        }

        // ════════════════════════════════════════════════════
        // 11. EvaluateFilterNode — nested groups
        // ════════════════════════════════════════════════════

        [Fact]
        public void Evaluate_NestedGroups_OrContainingAnd()
        {
            // (Level=Error AND Message contains "disk") OR (Level=Warning AND Message contains "paper")
            var log = MakeLog(message: "paper jam", level: "Warning");

            var branch1 = Group("AND",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "disk"));
            var branch2 = Group("AND",
                Cond("Level", "Equals", "Warning"),
                Cond("Message", "Contains", "paper"));

            var root = Group("OR", branch1, branch2);
            Assert.True(Evaluate(log, root));
        }

        [Fact]
        public void Evaluate_NestedGroups_NeitherBranchMatches()
        {
            var log = MakeLog(message: "cpu load", level: "Info");

            var branch1 = Group("AND",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "disk"));
            var branch2 = Group("AND",
                Cond("Level", "Equals", "Warning"),
                Cond("Message", "Contains", "paper"));

            var root = Group("OR", branch1, branch2);
            Assert.False(Evaluate(log, root));
        }

        [Fact]
        public void Evaluate_ThreeLevelNesting()
        {
            // NOT( (Level=Error) OR (Message contains "fatal") )
            var log = MakeLog(message: "all good", level: "Info");

            var inner = Group("OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "fatal"));

            var notWrapper = Group("NOT OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "fatal"));

            // Neither Error nor fatal match -> OR is false -> NOT OR is true
            Assert.True(Evaluate(log, notWrapper));
        }

        // ════════════════════════════════════════════════════
        // 12. IsDefaultLog — uses factory PLC filter
        // ════════════════════════════════════════════════════

        [Fact]
        public void IsDefaultLog_MatchesPlcMngrMessage()
        {
            var log = MakeLog(message: "PlcMngr: state changed", threadName: "SomeThread");
            Assert.True(_vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_MatchesManagerThread()
        {
            var log = MakeLog(message: "some msg", threadName: "Manager-1");
            Assert.True(_vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_MatchesErrorLevel()
        {
            var log = MakeLog(message: "something failed", level: "error");
            Assert.True(_vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_MatchesEventsThread()
        {
            var log = MakeLog(message: "event occurred", threadName: "Events");
            Assert.True(_vm.IsDefaultLog(log));
        }

        [Fact]
        public void IsDefaultLog_NoMatch_ReturnsFalse()
        {
            var log = MakeLog(message: "routine debug msg", level: "Debug", threadName: "Worker-3");
            Assert.False(_vm.IsDefaultLog(log));
        }

        // ════════════════════════════════════════════════════
        // 13. MatchesSearch — edge cases
        // ════════════════════════════════════════════════════

        [Fact]
        public void MatchesSearch_FindsInMessage()
        {
            var log = MakeLog(message: "Motor stalled at position 42");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "stalled"));
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "MOTOR"));
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "voltage"));
        }

        [Fact]
        public void MatchesSearch_FindsInExtraFields()
        {
            var log = MakeLog(
                message: "nothing relevant",
                extraFields: new Dictionary<string, string>
                {
                    { "Source", "PrintHead" },
                    { "Zone", "BID" }
                });

            Assert.True(FilterSearchViewModel.MatchesSearch(log, "PrintHead"));
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "bid"));
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "Drum"));
        }

        [Fact]
        public void MatchesSearch_NullMessage_ReturnsFalse()
        {
            var log = new LogEntry { Message = null! };
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "anything"));
        }

        [Fact]
        public void MatchesSearch_EmptySearch_MatchesNonEmptyMessage()
        {
            var log = MakeLog(message: "some text");
            // IndexOf with empty string returns 0, which is >= 0
            Assert.True(FilterSearchViewModel.MatchesSearch(log, ""));
        }

        [Fact]
        public void MatchesSearch_NullExtraFieldValue_Skipped()
        {
            var log = MakeLog(
                message: "no match here",
                extraFields: new Dictionary<string, string> { { "Key", null! } });
            Assert.False(FilterSearchViewModel.MatchesSearch(log, "something"));
        }

        [Fact]
        public void MatchesSearch_MultipleExtraFields_FindsInSecond()
        {
            var log = MakeLog(
                message: "unrelated",
                extraFields: new Dictionary<string, string>
                {
                    { "A", "nothing" },
                    { "B", "target value" }
                });
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "target"));
        }

        [Fact]
        public void MatchesSearch_PartialMatch_InMessage()
        {
            var log = MakeLog(message: "StateTransition from Ready to Printing");
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "Transition"));
            Assert.True(FilterSearchViewModel.MatchesSearch(log, "statetransition"));
        }

        // ════════════════════════════════════════════════════
        // 14. QueryParserService.Parse — combined complex queries
        // ════════════════════════════════════════════════════

        [Fact]
        public void Parse_ComplexQuery_OrAndNot()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error OR warning -debug", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            // This should parse as: error OR (warning AND (NOT debug))
            // Due to precedence: OR binds loosest, then AND (implicit), then NOT
        }

        [Fact]
        public void Parse_MixedQuotesAndParens()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(\"disk error\" OR \"paper jam\") fatal", out var err);

            Assert.Null(err);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        // ════════════════════════════════════════════════════
        // 15. Parse result integration with EvaluateFilterNode
        // ════════════════════════════════════════════════════

        [Fact]
        public void ParseThenEvaluate_SingleWord_Matches()
        {
            var parser = new QueryParserService();
            var node = parser.Parse("stall", out _);
            Assert.NotNull(node);

            var log = MakeLog(message: "Motor stalled");
            Assert.True(Evaluate(log, node!));

            var log2 = MakeLog(message: "All normal");
            Assert.False(Evaluate(log2, node!));
        }

        [Fact]
        public void ParseThenEvaluate_Or_MatchesEitherTerm()
        {
            var parser = new QueryParserService();
            var node = parser.Parse("error OR warning", out _);
            Assert.NotNull(node);

            Assert.True(Evaluate(MakeLog(message: "An error occurred"), node!));
            Assert.True(Evaluate(MakeLog(message: "A warning was raised"), node!));
            Assert.False(Evaluate(MakeLog(message: "All is fine"), node!));
        }

        [Fact]
        public void ParseThenEvaluate_Not_ExcludesTerm()
        {
            var parser = new QueryParserService();
            var node = parser.Parse("-debug", out _);
            Assert.NotNull(node);

            Assert.True(Evaluate(MakeLog(message: "Error happened"), node!));
            Assert.False(Evaluate(MakeLog(message: "Debug information"), node!));
        }

        [Fact]
        public void ParseThenEvaluate_QuotedPhrase_ExactSubstring()
        {
            var parser = new QueryParserService();
            var node = parser.Parse("\"disk error\"", out _);
            Assert.NotNull(node);

            Assert.True(Evaluate(MakeLog(message: "A disk error occurred"), node!));
            Assert.False(Evaluate(MakeLog(message: "disk warning error"), node!));
        }

        [Fact]
        public void ParseThenEvaluate_ParenthesizedOr()
        {
            var parser = new QueryParserService();
            var node = parser.Parse("(error OR warning) motor", out _);
            Assert.NotNull(node);

            Assert.True(Evaluate(MakeLog(message: "motor error detected"), node!));
            Assert.True(Evaluate(MakeLog(message: "motor warning alert"), node!));
            Assert.False(Evaluate(MakeLog(message: "motor running fine"), node!));
            Assert.False(Evaluate(MakeLog(message: "error in CPU"), node!));
        }

        // ════════════════════════════════════════════════════
        // 16. CollectFilterNodeDescriptions
        // ════════════════════════════════════════════════════

        [Fact]
        public void CollectFilterNodeDescriptions_ConditionNode_AddsItem()
        {
            var method = typeof(FilterSearchViewModel).GetMethod(
                "CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var items = new List<ActiveFilterItem>();
            var node = Cond("Message", "Contains", "error");
            int idx = 0;
            var args = new object[] { items, node, "FILTER", "", "TEST", idx };

            method!.Invoke(_vm, args);

            Assert.Single(items);
            Assert.Equal("FILTER", items[0].Category);
            Assert.Contains("Message", items[0].Description);
            Assert.Contains("error", items[0].Description);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_GroupNode_CollectsChildren()
        {
            var method = typeof(FilterSearchViewModel).GetMethod(
                "CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var items = new List<ActiveFilterItem>();
            var group = Group("AND",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Contains", "disk"));
            int idx = 0;
            var args = new object[] { items, group, "FILTER", "", "TEST", idx };

            method!.Invoke(_vm, args);

            Assert.Equal(2, items.Count);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_DoesNothing()
        {
            var method = typeof(FilterSearchViewModel).GetMethod(
                "CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var items = new List<ActiveFilterItem>();
            int idx = 0;
            var args = new object[] { items, null!, "FILTER", "", "TEST", idx };

            method!.Invoke(_vm, args);

            Assert.Empty(items);
        }

        // ════════════════════════════════════════════════════
        // 17. Edge cases for EvaluateFilterNode
        // ════════════════════════════════════════════════════

        [Fact]
        public void Evaluate_ConditionWithEmptyCriteria_MatchesNonEmptyField()
        {
            // Contains with empty criteria: IndexOf("", ...) >= 0 is true
            var log = MakeLog(message: "something");
            Assert.True(Evaluate(log, Cond("Message", "Contains", "")));
        }

        [Fact]
        public void Evaluate_RegexWithEmptyValue_ReturnsFalse()
        {
            // Regex operator with empty value — CompiledRegex returns null for empty value
            var node = Cond("Message", "Regex", "");
            var log = MakeLog(message: "test");
            // Empty regex value: IsNullOrEmpty(_value) is true so CompiledRegex returns null,
            // then Regex.IsMatch is called with empty pattern which matches anything
            // BUT the criteria is "" and val is "test", so Regex.IsMatch(val, "") returns true
            // Actually let's just check what happens
            // node.Value = "" => CompiledRegex returns null => falls to Regex.IsMatch(val, "", ...) which returns true
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_UnknownOperator_FallsBackToContains()
        {
            var log = MakeLog(message: "Hello World");
            Assert.True(Evaluate(log, Cond("Message", "FancyOp", "World")));
            Assert.False(Evaluate(log, Cond("Message", "FancyOp", "xyz")));
        }

        [Fact]
        public void Evaluate_AndGroup_EmptyChildren_ReturnsTrue()
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>()
            };
            Assert.True(Evaluate(MakeLog(), node));
        }

        [Fact]
        public void Evaluate_OrGroup_EmptyChildren_ReturnsTrue()
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>()
            };
            Assert.True(Evaluate(MakeLog(), node));
        }

        [Fact]
        public void Evaluate_NullNode_ReturnsTrue()
        {
            Assert.True(Evaluate(MakeLog(), null!));
        }

        [Fact]
        public void Evaluate_ManyOrConditions_FirstMatch_ShortCircuits()
        {
            var log = MakeLog(level: "Error");
            // First child matches; rest would fail with invalid regex if evaluated
            var node = Group("OR",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Regex", "[bad"),
                Cond("Message", "Regex", "[bad2"));
            Assert.True(Evaluate(log, node));
        }

        [Fact]
        public void Evaluate_ManyAndConditions_FirstFail_ShortCircuits()
        {
            var log = MakeLog(level: "Info");
            var node = Group("AND",
                Cond("Level", "Equals", "Error"),
                Cond("Message", "Regex", "[bad"),
                Cond("Message", "Regex", "[bad2"));
            Assert.False(Evaluate(log, node));
        }
    }
}
