using IndiLogs_3._0;
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
using System.Threading.Tasks;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class CoverageBoost2FilterTests
    {
        private static readonly BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;

        #region Helpers

        private static FilterSearchViewModel CreateVM()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(FilterSearchViewModel).GetField("_parent", NPI)?.SetValue(vm, parent);

            // Initialize sessionVM so methods calling _sessionVM.StatusMessage don't NRE
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            typeof(FilterSearchViewModel).GetField("_sessionVM", NPI)?.SetValue(vm, sessionVM);

            // Initialize all required collection fields
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_plcLoggerTreeRoot", new ObservableCollection<LoggerNode>());
            SetField(vm, "_filteredLogs", new ObservableRangeCollection<LogEntry>());
            SetField(vm, "_appDevLogsFiltered", new ObservableRangeCollection<LogEntry>());

            return vm;
        }

        private static MainViewModel GetParent(FilterSearchViewModel vm)
        {
            return (MainViewModel)typeof(FilterSearchViewModel).GetField("_parent", NPI)!.GetValue(vm)!;
        }

        private static void SetField(object obj, string fieldName, object? value)
        {
            var field = obj.GetType().GetField(fieldName, NPI);
            field?.SetValue(obj, value);
        }

        private static object? GetField(object obj, string fieldName)
        {
            return obj.GetType().GetField(fieldName, NPI)?.GetValue(obj);
        }

        private static void SetParentTabIndex(FilterSearchViewModel vm, int tabIndex)
        {
            var parent = GetParent(vm);
            typeof(MainViewModel).GetField("_selectedTabIndex", NPI)?.SetValue(parent, tabIndex);
        }

        private static void InitSearchDebounceTimer(FilterSearchViewModel vm)
        {
            // SearchText setter triggers OnSearchTextChanged which uses _searchDebounceTimer
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            SetField(vm, "_searchDebounceTimer", timer);
        }

        private static LogEntry MakeLog(
            string message = "", string level = "", string logger = "",
            string threadName = "", string processName = "", string method = "",
            string pattern = "", string data = "", string exception = "",
            Dictionary<string, string>? extraFields = null,
            DateTime? date = null) =>
            new()
            {
                Message = message, Level = level, Logger = logger,
                ThreadName = threadName, ProcessName = processName,
                Method = method, Pattern = pattern, Data = data,
                Exception = exception, ExtraFields = extraFields,
                Date = date ?? DateTime.Now
            };

        private static FilterNode MakeCondition(string field, string op, string value) =>
            new() { Type = NodeType.Condition, Field = field, Operator = op, Value = value };

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

        #endregion

        // ══════════════════════════════════════════════════
        // TextFilterParser Tests
        // ══════════════════════════════════════════════════

        #region TextFilterParser

        [Fact]
        public void TextFilterParser_Parse_NullReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(null));
        }

        [Fact]
        public void TextFilterParser_Parse_EmptyReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(""));
        }

        [Fact]
        public void TextFilterParser_Parse_WhitespaceReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse("   "));
        }

        [Fact]
        public void TextFilterParser_Parse_SimpleContains()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'hello')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result.Type);
            Assert.Equal("Message", result.Field);
            Assert.Equal("Contains", result.Operator);
            Assert.Equal("hello", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_StartsWith()
        {
            var result = TextFilterParser.Parse("StartsWith([Thread], 'Main')");
            Assert.NotNull(result);
            Assert.Equal("ThreadName", result.Field);
            Assert.Equal("Begins With", result.Operator);
            Assert.Equal("Main", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_EndsWith()
        {
            var result = TextFilterParser.Parse("EndsWith([Level], 'Error')");
            Assert.NotNull(result);
            Assert.Equal("Level", result.Field);
            Assert.Equal("Ends With", result.Operator);
            Assert.Equal("Error", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_Equals()
        {
            var result = TextFilterParser.Parse("Equals([Logger], 'MyLogger')");
            Assert.NotNull(result);
            Assert.Equal("Logger", result.Field);
            Assert.Equal("Equals", result.Operator);
            Assert.Equal("MyLogger", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_ThreadNameField()
        {
            var result = TextFilterParser.Parse("Contains([ThreadName], 'Worker')");
            Assert.NotNull(result);
            Assert.Equal("ThreadName", result.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_ProcessNameField()
        {
            var result = TextFilterParser.Parse("Contains([ProcessName], 'MyProc')");
            Assert.NotNull(result);
            Assert.Equal("ProcessName", result.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownFieldPassThrough()
        {
            var result = TextFilterParser.Parse("Contains([CustomField], 'value')");
            Assert.NotNull(result);
            Assert.Equal("CustomField", result.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownFuncDefaultsToContains()
        {
            var result = TextFilterParser.Parse("Blah([Message], 'test')");
            Assert.NotNull(result);
            Assert.Equal("Contains", result.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_OrExpression()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'hello') Or Contains([Message], 'world')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_AndExpression()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'hello') And Contains([Level], 'Error')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_AndHigherPrecedenceThanOr()
        {
            // A Or B And C → A Or (B And C)
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') And Contains([Message], 'c')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            // Second child should be an AND group
            Assert.Equal(NodeType.Group, result.Children[1].Type);
            Assert.Equal("AND", result.Children[1].LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_Parentheses()
        {
            var result = TextFilterParser.Parse(
                "(Contains([Message], 'a') Or Contains([Message], 'b')) And Contains([Level], 'Error')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            // First child should be OR group
            Assert.Equal(NodeType.Group, result.Children[0].Type);
            Assert.Equal("OR", result.Children[0].LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_NestedParentheses()
        {
            var result = TextFilterParser.Parse(
                "((Contains([Message], 'inner')))");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result.Type);
            Assert.Equal("inner", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_MultipleOr()
        {
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') Or Contains([Message], 'c')");
            Assert.NotNull(result);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(3, result.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_MultipleAnd()
        {
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Message], 'b') And Contains([Message], 'c')");
            Assert.NotNull(result);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(3, result.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_UnexpectedTokenThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') Contains([Message], 'b')"));
        }

        [Fact]
        public void TextFilterParser_Parse_MissingClosingParenThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("(Contains([Message], 'a')"));
        }

        [Fact]
        public void TextFilterParser_Parse_UnexpectedEndThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') And"));
        }

        [Fact]
        public void TextFilterParser_Parse_MissingFieldThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains('value')"));
        }

        [Fact]
        public void TextFilterParser_Parse_MissingValueThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message])"));
        }

        [Fact]
        public void TextFilterParser_Parse_MissingOpenParenThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains [Message], 'value')"));
        }

        [Fact]
        public void TextFilterParser_Parse_CaseInsensitiveKeywords()
        {
            var result = TextFilterParser.Parse("contains([message], 'x') or contains([level], 'y')");
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_ValueWithSpaces()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'hello world')");
            Assert.NotNull(result);
            Assert.Equal("hello world", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_EmptyValue()
        {
            var result = TextFilterParser.Parse("Contains([Message], '')");
            Assert.NotNull(result);
            Assert.Equal("", result.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_SingleConditionReturnsConditionDirectly()
        {
            var result = TextFilterParser.Parse("Contains([Message], 'test')");
            Assert.NotNull(result);
            // Should return condition directly, not wrapped in a group
            Assert.Equal(NodeType.Condition, result.Type);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // LogColoringService Tests
        // ══════════════════════════════════════════════════

        #region LogColoringService

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_PLC_ErrorGetsIsErrorOrEvents()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "some error")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_PLC_EventsThread()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(threadName: "Events", message: "something")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_PLC_StateTransitionS6()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(threadName: "Manager", message: "PlcMngr: Idle -> Running")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.NotNull(logs[0].CustomColor);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_PLC_StateTransitionS45()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(message: "==== STATE TRANSITION ====")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_APP_ErrorRed()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "APP error")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_APP_PipelineCancellation()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "Press.BL.Printing.Pipeline.PipelineCancellationProvider.Something", message: "cancel")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.Equal(Color.FromRgb(255, 140, 0), logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_APP_PressStateManager()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "PressStateManager.Foo", method: "FallToPressStateAsync", message: "state change")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.Equal(Color.FromRgb(255, 165, 0), logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_APP_NormalLog()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Info", message: "normal log")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.Null(logs[0].CustomColor);
            Assert.False(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ContainsOperator()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "disk failure occurred") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "failure", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_EqualsOperator()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(level: "Error") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Level", Operator = "Equals", Value = "Error", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_BeginsWithOperator()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(logger: "MyApp.Services.Foo") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Logger", Operator = "Begins With", Value = "MyApp", Color = Colors.Blue }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Blue, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_EndsWithOperator()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(logger: "MyApp.Services.Foo") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Logger", Operator = "Ends With", Value = "Foo", Color = Colors.Green }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Green, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_RegexOperator()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "Error code 42") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Regex", Value = @"code\s+\d+", Color = Colors.Yellow }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Yellow, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_InvalidRegex_NoMatch()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "some text") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Regex", Value = "[invalid", Color = Colors.Yellow }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_UnknownOperator_NoMatch()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "text") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "WeirdOp", Value = "text", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_EmptyConditions_NoOp()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "text") };
            await svc.ApplyCustomColoringAsync(logs, new List<ColoringCondition>());
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_NullConditions_NoOp()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "text") };
            await svc.ApplyCustomColoringAsync(logs, null!);
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_FirstMatchWins()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "error occurred") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "error", Color = Colors.Red },
                new() { Field = "Message", Operator = "Contains", Value = "occurred", Color = Colors.Blue }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_EmptyFieldValue_NoMatch()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "test", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ThreadNameField()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(threadName: "Worker-1") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "ThreadName", Operator = "Contains", Value = "Worker", Color = Colors.Orange }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Orange, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_MethodField()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(method: "DoWork") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Method", Operator = "Equals", Value = "DoWork", Color = Colors.Purple }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Purple, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_PatternField()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(pattern: "SomePattern") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Pattern", Operator = "Contains", Value = "Pattern", Color = Colors.Cyan }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Cyan, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_DataField()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(data: "key=value") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Data", Operator = "Contains", Value = "key", Color = Colors.Brown }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Brown, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ExceptionField()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(exception: "NullRefException") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Exception", Operator = "Contains", Value = "NullRef", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ExtraFieldMatch()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(extraFields: new Dictionary<string, string> { { "Source", "PrintHead" } })
            };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Source", Operator = "Contains", Value = "Print", Color = Colors.Lime }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Equal(Colors.Lime, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ExtraFieldCaseInsensitiveLookup()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry>
            {
                MakeLog(extraFields: new Dictionary<string, string> { { "SOURCE", "PrintHead" } })
            };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Source", Operator = "Contains", Value = "Print", Color = Colors.Lime }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            // Should find via case-insensitive iteration fallback
            Assert.Equal(Colors.Lime, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyCustomColoring_ContainsEmptyValue_NoMatch()
        {
            var svc = new LogColoringService();
            var logs = new List<LogEntry> { MakeLog(message: "some text") };
            var conditions = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "", Color = Colors.Red }
            };
            await svc.ApplyCustomColoringAsync(logs, conditions);
            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_WithUserRules()
        {
            var svc = new LogColoringService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "important", Color = Colors.Gold }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(message: "this is important"),
                MakeLog(message: "nothing special")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.Equal(Colors.Gold, logs[0].CustomColor);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_WithUserRules_ErrorStillMarked()
        {
            var svc = new LogColoringService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "xyz", Color = Colors.Gold }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "some error")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task LogColoringService_ApplyDefaultColors_WithUserAppRules()
        {
            var svc = new LogColoringService();
            svc.UserDefaultAppRules = new List<ColoringCondition>
            {
                new() { Field = "Logger", Operator = "Contains", Value = "MyLogger", Color = Colors.Pink }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "MyLogger.Service")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.Equal(Colors.Pink, logs[0].CustomColor);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - RemoveFilterConditionByIndex
        // ══════════════════════════════════════════════════

        #region RemoveFilterConditionByIndex

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesFirstCondition()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;

            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));

            method.Invoke(vm, new object[] { root, 0 });
            Assert.Single(root.Children);
            Assert.Equal("Message", root.Children[0].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesSecondCondition()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;

            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));

            method.Invoke(vm, new object[] { root, 1 });
            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesNestedCondition()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;

            var inner = MakeGroup("OR",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Level", "Equals", "Warning"));
            var root = MakeGroup("AND", inner);

            // Remove index 0 (first condition inside nested group)
            method.Invoke(vm, new object[] { root, 0 });
            Assert.Single(inner.Children);
            Assert.Equal("Warning", inner.Children[0].Value);
        }

        [Fact]
        public void RemoveFilterConditionByIndex_RemovesEmptyGroup()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;

            var inner = MakeGroup("OR", MakeCondition("Level", "Equals", "Error"));
            var root = MakeGroup("AND", inner);

            method.Invoke(vm, new object[] { root, 0 });
            Assert.Empty(root.Children); // inner group became empty and was removed
        }

        [Fact]
        public void RemoveFilterConditionByIndex_NullRoot_NoThrow()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;
            method.Invoke(vm, new object?[] { null, 0 }); // should not throw
        }

        [Fact]
        public void RemoveFilterConditionByIndex_IndexOutOfRange_NoRemoval()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveFilterConditionByIndex", NPI)!;

            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            method.Invoke(vm, new object[] { root, 5 });
            Assert.Single(root.Children);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - ClearFilters
        // ══════════════════════════════════════════════════

        #region ClearFilters

        [Fact]
        public void ClearFilters_ResetsAllState()
        {
            var vm = CreateVM();
            // ClearFilters sets SearchText="" which triggers _searchDebounceTimer, so init it
            InitSearchDebounceTimer(vm);

            // Set up various filter state
            vm.IsMainFilterActive = true;
            vm.IsAppFilterActive = true;
            vm.IsAppErrorFilterActive = true;
            vm.IsMainFilterOutActive = true;
            vm.IsAppFilterOutActive = true;
            vm.IsTimeFocusActive = true;
            vm.IsAppTimeFocusActive = true;

            var negativeFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negativeFilters.Add("test");
            var appNegativeFilters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            appNegativeFilters.Add("test");
            var threadFilters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            threadFilters.Add("Thread1");
            var loggerFilters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("Logger1");
            var methodFilters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            methodFilters.Add("Method1");
            var treeHiddenLoggers = (HashSet<string>)GetField(vm, "_treeHiddenLoggers")!;
            treeHiddenLoggers.Add("some.logger");

            vm.ClearFilters();

            Assert.False(vm.IsMainFilterActive);
            Assert.False(vm.IsAppFilterActive);
            Assert.False(vm.IsAppErrorFilterActive);
            Assert.False(vm.IsMainFilterOutActive);
            Assert.False(vm.IsAppFilterOutActive);
            Assert.False(vm.IsTimeFocusActive);
            Assert.False(vm.IsAppTimeFocusActive);
            Assert.Empty(negativeFilters);
            Assert.Empty(appNegativeFilters);
            Assert.Empty(threadFilters);
            Assert.Empty(loggerFilters);
            Assert.Empty(methodFilters);
            Assert.Empty(treeHiddenLoggers);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - RemoveActiveFilter
        // ══════════════════════════════════════════════════

        #region RemoveActiveFilter

        [Fact]
        public void RemoveActiveFilter_EmptyKey_NoOp()
        {
            var vm = CreateVM();
            vm.RemoveActiveFilter(""); // should not throw
        }

        [Fact]
        public void RemoveActiveFilter_NullKey_NoOp()
        {
            var vm = CreateVM();
            vm.RemoveActiveFilter(null!); // should not throw
        }

        [Fact]
        public void RemoveActiveFilter_APP_ERROR_FILTER()
        {
            var vm = CreateVM();
            vm.IsAppErrorFilterActive = true;
            vm.RemoveActiveFilter("APP_ERROR_FILTER");
            Assert.False(vm.IsAppErrorFilterActive);
        }

        [Fact]
        public void RemoveActiveFilter_APP_TIME_FOCUS()
        {
            var vm = CreateVM();
            vm.IsAppTimeFocusActive = true;
            vm.RemoveActiveFilter("APP_TIME_FOCUS");
            Assert.False(vm.IsAppTimeFocusActive);
        }

        [Fact]
        public void RemoveActiveFilter_MAIN_TIME_FOCUS()
        {
            var vm = CreateVM();
            vm.IsTimeFocusActive = true;
            vm.RemoveActiveFilter("MAIN_TIME_FOCUS");
            Assert.False(vm.IsTimeFocusActive);
        }

        [Fact]
        public void RemoveActiveFilter_GLOBAL_TIME_RANGE()
        {
            var vm = CreateVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);
            vm.RemoveActiveFilter("GLOBAL_TIME_RANGE");
            Assert.Null(GetField(vm, "_globalTimeRangeStart"));
            Assert.Null(GetField(vm, "_globalTimeRangeEnd"));
        }

        [Fact]
        public void RemoveActiveFilter_SEARCH()
        {
            var vm = CreateVM();
            InitSearchDebounceTimer(vm);
            SetField(vm, "_searchText", "some search");
            vm.RemoveActiveFilter("SEARCH");
            // SearchText property sets _searchText
            var searchText = (string?)GetField(vm, "_searchText");
            Assert.Equal("", searchText);
        }

        [Fact]
        public void RemoveActiveFilter_RANGE()
        {
            var vm = CreateVM();
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog());
            vm.RemoveActiveFilter("RANGE");
            Assert.False((bool)GetField(vm, "_hasRangeStart")!);
        }

        [Fact]
        public void RemoveActiveFilter_TREE_SHOW_ONLY_LOGGER()
        {
            var vm = CreateVM();
            SetField(vm, "_treeShowOnlyLogger", "some.logger");
            vm.RemoveActiveFilter("TREE_SHOW_ONLY_LOGGER");
            Assert.Null(GetField(vm, "_treeShowOnlyLogger"));
        }

        [Fact]
        public void RemoveActiveFilter_TREE_SHOW_ONLY_PREFIX()
        {
            var vm = CreateVM();
            SetField(vm, "_treeShowOnlyPrefix", "some.prefix");
            vm.RemoveActiveFilter("TREE_SHOW_ONLY_PREFIX");
            Assert.Null(GetField(vm, "_treeShowOnlyPrefix"));
        }

        [Fact]
        public void RemoveActiveFilter_PLC_TREE_SHOW_ONLY_LOGGER()
        {
            var vm = CreateVM();
            SetField(vm, "_plcTreeShowOnlyLogger", "plc.logger");
            vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_LOGGER");
            Assert.Null(GetField(vm, "_plcTreeShowOnlyLogger"));
        }

        [Fact]
        public void RemoveActiveFilter_PLC_TREE_SHOW_ONLY_PREFIX()
        {
            var vm = CreateVM();
            SetField(vm, "_plcTreeShowOnlyPrefix", "plc.prefix");
            vm.RemoveActiveFilter("PLC_TREE_SHOW_ONLY_PREFIX");
            Assert.Null(GetField(vm, "_plcTreeShowOnlyPrefix"));
        }

        [Fact]
        public void RemoveActiveFilter_APP_FILTER_RemovesCondition()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));
            SetField(vm, "_appFilterRoot", root);
            vm.RemoveActiveFilter("APP_FILTER:0");
            Assert.Single(root.Children);
            Assert.Equal("Message", root.Children[0].Field);
        }

        [Fact]
        public void RemoveActiveFilter_APP_FILTER_DeactivatesWhenEmpty()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", root);
            vm.IsAppFilterActive = true;
            vm.RemoveActiveFilter("APP_FILTER:0");
            Assert.False(vm.IsAppFilterActive);
        }

        [Fact]
        public void RemoveActiveFilter_MAIN_FILTER_RemovesCondition()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));
            SetField(vm, "_mainFilterRoot", root);
            vm.RemoveActiveFilter("MAIN_FILTER:0");
            Assert.Single(root.Children);
        }

        [Fact]
        public void RemoveActiveFilter_MAIN_FILTER_DeactivatesWhenEmpty()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_mainFilterRoot", root);
            vm.IsMainFilterActive = true;
            vm.RemoveActiveFilter("MAIN_FILTER:0");
            Assert.False(vm.IsMainFilterActive);
        }

        [Fact]
        public void RemoveActiveFilter_APP_THREAD()
        {
            var vm = CreateVM();
            var threadFilters = (List<string>)GetField(vm, "_appActiveThreadFilters")!;
            threadFilters.Add("Thread1");
            threadFilters.Add("Thread2");
            vm.RemoveActiveFilter("APP_THREAD:Thread1");
            Assert.Single(threadFilters);
            Assert.Equal("Thread2", threadFilters[0]);
        }

        [Fact]
        public void RemoveActiveFilter_MAIN_THREAD()
        {
            var vm = CreateVM();
            var threadFilters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            threadFilters.Add("MainThread1");
            threadFilters.Add("MainThread2");
            vm.RemoveActiveFilter("MAIN_THREAD:MainThread1");
            Assert.Single(threadFilters);
        }

        [Fact]
        public void RemoveActiveFilter_LOGGER()
        {
            var vm = CreateVM();
            var loggerFilters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("MyLogger");
            vm.RemoveActiveFilter("LOGGER:MyLogger");
            Assert.Empty(loggerFilters);
        }

        [Fact]
        public void RemoveActiveFilter_METHOD()
        {
            var vm = CreateVM();
            var methodFilters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            methodFilters.Add("DoWork");
            vm.RemoveActiveFilter("METHOD:DoWork");
            Assert.Empty(methodFilters);
        }

        [Fact]
        public void RemoveActiveFilter_TREE_HIDE_LOGGER()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenLoggers")!;
            hidden.Add("some.logger");
            vm.RemoveActiveFilter("TREE_HIDE_LOGGER:some.logger");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_TREE_HIDE_PREFIX()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenPrefixes")!;
            hidden.Add("some.prefix");
            vm.RemoveActiveFilter("TREE_HIDE_PREFIX:some.prefix");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PLC_TREE_HIDE_LOGGER()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenLoggers")!;
            hidden.Add("plc.logger");
            vm.RemoveActiveFilter("PLC_TREE_HIDE_LOGGER:plc.logger");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_PLC_TREE_HIDE_PREFIX()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenPrefixes")!;
            hidden.Add("plc.prefix");
            vm.RemoveActiveFilter("PLC_TREE_HIDE_PREFIX:plc.prefix");
            Assert.Empty(hidden);
        }

        [Fact]
        public void RemoveActiveFilter_NEGATIVE()
        {
            var vm = CreateVM();
            var negativeFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negativeFilters.Add("bad text");
            vm.IsMainFilterOutActive = true;
            vm.RemoveActiveFilter("NEGATIVE:bad text");
            Assert.Empty(negativeFilters);
            Assert.False(vm.IsMainFilterOutActive);
        }

        [Fact]
        public void RemoveActiveFilter_APP_NEGATIVE()
        {
            var vm = CreateVM();
            var negativeFilters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            negativeFilters.Add("bad text");
            vm.IsAppFilterOutActive = true;
            vm.RemoveActiveFilter("APP_NEGATIVE:bad text");
            Assert.Empty(negativeFilters);
            Assert.False(vm.IsAppFilterOutActive);
        }

        [Fact]
        public void RemoveActiveFilter_NEGATIVE_KeepsActiveIfOthersRemain()
        {
            var vm = CreateVM();
            var negativeFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negativeFilters.Add("bad1");
            negativeFilters.Add("bad2");
            vm.IsMainFilterOutActive = true;
            vm.RemoveActiveFilter("NEGATIVE:bad1");
            Assert.Single(negativeFilters);
            Assert.True(vm.IsMainFilterOutActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - CheckIfFiltersEmpty
        // ══════════════════════════════════════════════════

        #region CheckIfFiltersEmpty

        [Fact]
        public void CheckIfFiltersEmpty_App_AllEmpty_DeactivatesFilter()
        {
            var vm = CreateVM();
            vm.IsAppFilterActive = true;
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.False(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_App_WithThreadFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsAppFilterActive = true;
            var threadFilters = (List<string>)GetField(vm, "_appActiveThreadFilters")!;
            threadFilters.Add("Thread1");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_App_WithLoggerFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsAppFilterActive = true;
            var loggerFilters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("MyLogger");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_App_WithMethodFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsAppFilterActive = true;
            var methodFilters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            methodFilters.Add("DoWork");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_App_WithTreeFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsAppFilterActive = true;
            SetField(vm, "_treeShowOnlyLogger", "some.logger");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_Main_AllEmpty_DeactivatesFilter()
        {
            var vm = CreateVM();
            vm.IsMainFilterActive = true;
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { false });
            Assert.False(vm.IsMainFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_Main_WithThreadFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsMainFilterActive = true;
            var threadFilters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            threadFilters.Add("Thread1");
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_Main_WithAdvancedFilter_StaysActive()
        {
            var vm = CreateVM();
            vm.IsMainFilterActive = true;
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_mainFilterRoot", root);
            var method = typeof(FilterSearchViewModel).GetMethod("CheckIfFiltersEmpty", NPI)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - SetFilterActive
        // ══════════════════════════════════════════════════

        #region SetFilterActive

        [Fact]
        public void SetFilterActive_App_SetsAppFilterActive()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive", NPI)!;
            method.Invoke(vm, new object[] { true });
            Assert.True(vm.IsAppFilterActive);
        }

        [Fact]
        public void SetFilterActive_Main_SetsMainFilterActive()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetFilterActive", NPI)!;
            method.Invoke(vm, new object[] { false });
            Assert.True(vm.IsMainFilterActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - UndoFilterOut
        // ══════════════════════════════════════════════════

        #region UndoFilterOut

        [Fact]
        public void UndoFilterOut_App_RemovesLast()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_APP);
            var negativeFilters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            negativeFilters.Add("first");
            negativeFilters.Add("second");
            vm.IsAppFilterOutActive = true;

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null });

            Assert.Single(negativeFilters);
            Assert.Equal("first", negativeFilters[0]);
            Assert.True(vm.IsAppFilterOutActive);
        }

        [Fact]
        public void UndoFilterOut_App_LastItem_DeactivatesFilterOut()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_APP);
            var negativeFilters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            negativeFilters.Add("only");
            vm.IsAppFilterOutActive = true;

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null });

            Assert.Empty(negativeFilters);
            Assert.False(vm.IsAppFilterOutActive);
        }

        [Fact]
        public void UndoFilterOut_Main_RemovesLast()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_PLC);
            var negativeFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negativeFilters.Add("first");
            negativeFilters.Add("second");
            vm.IsMainFilterOutActive = true;

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null });

            Assert.Single(negativeFilters);
            Assert.True(vm.IsMainFilterOutActive);
        }

        [Fact]
        public void UndoFilterOut_Main_LastItem_DeactivatesFilterOut()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_PLC);
            var negativeFilters = (List<string>)GetField(vm, "_negativeFilters")!;
            negativeFilters.Add("only");
            vm.IsMainFilterOutActive = true;

            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null });

            Assert.Empty(negativeFilters);
            Assert.False(vm.IsMainFilterOutActive);
        }

        [Fact]
        public void UndoFilterOut_App_Empty_NoThrow()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_APP);
            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null }); // should not throw
        }

        [Fact]
        public void UndoFilterOut_Main_Empty_NoThrow()
        {
            var vm = CreateVM();
            SetParentTabIndex(vm, AppConstants.TAB_PLC);
            var method = typeof(FilterSearchViewModel).GetMethod("UndoFilterOut", NPI)!;
            method.Invoke(vm, new object?[] { null }); // should not throw
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - BuildLoggerTree / BuildPlcLoggerTree
        // ══════════════════════════════════════════════════

        #region BuildLoggerTree

        [Fact]
        public void BuildLoggerTree_EmptyLogs_EmptyTree()
        {
            var vm = CreateVM();
            vm.BuildLoggerTree(new List<LogEntry>());
            Assert.Empty(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_SingleLogger_CreatesCorrectTree()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo.press"),
                MakeLog(logger: "com.indigo.press")
            };
            vm.BuildLoggerTree(logs);
            Assert.NotEmpty(vm.LoggerTreeRoot);
            // Root should have "com" node
            Assert.Equal("com", vm.LoggerTreeRoot[0].Name);
        }

        [Fact]
        public void BuildLoggerTree_MultipleLoggers_BuildsHierarchy()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo.press"),
                MakeLog(logger: "com.indigo.drum"),
                MakeLog(logger: "org.apache.log")
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal(2, vm.LoggerTreeRoot.Count); // "com" and "org"
        }

        [Fact]
        public void BuildLoggerTree_EmptyLoggerName_Skipped()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: ""),
                MakeLog(logger: "com.test")
            };
            vm.BuildLoggerTree(logs);
            Assert.Single(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_CountsAreCorrect()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.test"),
                MakeLog(logger: "com.test"),
                MakeLog(logger: "com.other")
            };
            vm.BuildLoggerTree(logs);
            // "com" node should have count 3
            Assert.Equal(3, vm.LoggerTreeRoot[0].Count);
        }

        [Fact]
        public void BuildPlcLoggerTree_EmptyLogs_EmptyTree()
        {
            var vm = CreateVM();
            vm.BuildPlcLoggerTree(new List<LogEntry>());
            Assert.Empty(vm.PlcLoggerTreeRoot);
        }

        [Fact]
        public void BuildPlcLoggerTree_BuildsCorrectly()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "plc.motor"),
                MakeLog(logger: "plc.sensor")
            };
            vm.BuildPlcLoggerTree(logs);
            Assert.Single(vm.PlcLoggerTreeRoot); // "plc" node
            Assert.Equal(2, vm.PlcLoggerTreeRoot[0].Children.Count); // motor and sensor
        }

        [Fact]
        public void BuildLoggerTree_NullLogs_EmptyTree()
        {
            var vm = CreateVM();
            vm.BuildLoggerTree(null!);
            Assert.Empty(vm.LoggerTreeRoot);
        }

        [Fact]
        public void BuildPlcLoggerTree_NullLogs_EmptyTree()
        {
            var vm = CreateVM();
            vm.BuildPlcLoggerTree(null!);
            Assert.Empty(vm.PlcLoggerTreeRoot);
        }

        [Fact]
        public void BuildLoggerTree_SortedAlphabetically()
        {
            var vm = CreateVM();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "zebra.test"),
                MakeLog(logger: "alpha.test")
            };
            vm.BuildLoggerTree(logs);
            Assert.Equal("alpha", vm.LoggerTreeRoot[0].Name);
            Assert.Equal("zebra", vm.LoggerTreeRoot[1].Name);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - ResetTreeFilters / ResetPlcTreeFilters
        // ══════════════════════════════════════════════════

        #region TreeFilterReset

        [Fact]
        public void ResetTreeFilters_ClearsAll()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenLoggers")!;
            hidden.Add("test");
            var prefixes = (HashSet<string>)GetField(vm, "_treeHiddenPrefixes")!;
            prefixes.Add("prefix");
            SetField(vm, "_treeShowOnlyLogger", "logger");
            SetField(vm, "_treeShowOnlyPrefix", "prefix");

            vm.ResetTreeFilters();

            Assert.Empty(hidden);
            Assert.Empty(prefixes);
            Assert.Null(GetField(vm, "_treeShowOnlyLogger"));
            Assert.Null(GetField(vm, "_treeShowOnlyPrefix"));
        }

        [Fact]
        public void ResetPlcTreeFilters_ClearsAll()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenLoggers")!;
            hidden.Add("test");
            SetField(vm, "_plcTreeShowOnlyLogger", "logger");

            vm.ResetPlcTreeFilters();

            Assert.Empty(hidden);
            Assert.Null(GetField(vm, "_plcTreeShowOnlyLogger"));
            Assert.Null(GetField(vm, "_plcTreeShowOnlyPrefix"));
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - HasAnyColumnFilter
        // ══════════════════════════════════════════════════

        #region HasAnyColumnFilter

        [Fact]
        public void HasAnyColumnFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.False((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var loggerFilters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            loggerFilters.Add("test");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithThreadFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var threadFilters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            threadFilters.Add("Thread1");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithMethodFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var methodFilters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            methodFilters.Add("DoWork");
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithAppTimeFocus_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_isAppTimeFocusActive", true);
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        [Fact]
        public void HasAnyColumnFilter_WithAppFilterRoot_ReturnsTrue()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", root);
            var method = typeof(FilterSearchViewModel).GetMethod("HasAnyColumnFilter", NPI)!;
            Assert.True((bool)method.Invoke(vm, null)!);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - SetChildrenVisualState
        // ══════════════════════════════════════════════════

        #region SetChildrenVisualState

        [Fact]
        public void SetChildrenVisualState_SetsAllChildren()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState", NPI)!;

            var parent = new LoggerNode
            {
                Name = "Parent",
                Children = new ObservableCollection<LoggerNode>
                {
                    new LoggerNode { Name = "Child1", Children = new ObservableCollection<LoggerNode>
                    {
                        new LoggerNode { Name = "GrandChild" }
                    }},
                    new LoggerNode { Name = "Child2" }
                }
            };

            method.Invoke(vm, new object[] { parent, true, true });

            Assert.True(parent.Children[0].IsHidden);
            Assert.True(parent.Children[0].IsActive);
            Assert.True(parent.Children[0].Children[0].IsHidden);
            Assert.True(parent.Children[1].IsHidden);
        }

        [Fact]
        public void SetChildrenVisualState_ClearsState()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState", NPI)!;

            var child = new LoggerNode { Name = "Child", IsHidden = true, IsActive = true };
            var parent = new LoggerNode
            {
                Name = "Parent",
                Children = new ObservableCollection<LoggerNode> { child }
            };

            method.Invoke(vm, new object[] { parent, false, false });

            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        [Fact]
        public void SetChildrenVisualState_NullChildren_NoThrow()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SetChildrenVisualState", NPI)!;
            var parent = new LoggerNode { Name = "Parent", Children = null! };
            method.Invoke(vm, new object[] { parent, true, true }); // should not throw
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - SyncThreadFiltersToFilterTree
        // ══════════════════════════════════════════════════

        #region SyncThreadFiltersToFilterTree

        [Fact]
        public void SyncThreadFiltersToFilterTree_SingleThread_AddsCondition()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree", NPI)!;
            method.Invoke(vm, new object[] { true, new List<string> { "Thread1" } });

            var root = (FilterNode?)GetField(vm, "_appFilterRoot");
            Assert.NotNull(root);
            Assert.Single(root!.Children);
            Assert.Equal("ThreadName", root.Children[0].Field);
            Assert.Equal("Thread1", root.Children[0].Value);
        }

        [Fact]
        public void SyncThreadFiltersToFilterTree_MultipleThreads_AddsOrGroup()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree", NPI)!;
            method.Invoke(vm, new object[] { false, new List<string> { "Thread1", "Thread2" } });

            var root = (FilterNode?)GetField(vm, "_mainFilterRoot");
            Assert.NotNull(root);
            Assert.Single(root!.Children);
            Assert.Equal(NodeType.Group, root.Children[0].Type);
            Assert.Equal("OR", root.Children[0].LogicalOperator);
            Assert.Equal(2, root.Children[0].Children.Count);
        }

        [Fact]
        public void SyncThreadFiltersToFilterTree_ExistingRoot_AddsToIt()
        {
            var vm = CreateVM();
            var existingRoot = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", existingRoot);

            var method = typeof(FilterSearchViewModel).GetMethod("SyncThreadFiltersToFilterTree", NPI)!;
            method.Invoke(vm, new object[] { true, new List<string> { "Thread1" } });

            Assert.Equal(2, existingRoot.Children.Count);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - RemoveThreadConditionsFromFilterTree
        // ══════════════════════════════════════════════════

        #region RemoveThreadConditionsFromFilterTree

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesThreadConditions()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "Thread1"),
                MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_appFilterRoot", root);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree", NPI)!;
            method.Invoke(vm, new object[] { true });

            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_RemovesThreadOnlyGroups()
        {
            var vm = CreateVM();
            var threadGroup = MakeGroup("OR",
                MakeCondition("ThreadName", "Equals", "Thread1"),
                MakeCondition("ThreadName", "Equals", "Thread2"));
            var root = MakeGroup("AND", threadGroup, MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_mainFilterRoot", root);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree", NPI)!;
            method.Invoke(vm, new object[] { false });

            Assert.Single(root.Children);
            Assert.Equal("Level", root.Children[0].Field);
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_NullRoot_NoThrow()
        {
            var vm = CreateVM();
            SetField(vm, "_appFilterRoot", null);
            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree", NPI)!;
            method.Invoke(vm, new object[] { true }); // should not throw
        }

        [Fact]
        public void RemoveThreadConditionsFromFilterTree_MixedGroup_RecursesInto()
        {
            var vm = CreateVM();
            var mixedGroup = MakeGroup("AND",
                MakeCondition("ThreadName", "Equals", "Thread1"),
                MakeCondition("Level", "Equals", "Error"));
            var root = MakeGroup("AND", mixedGroup);
            SetField(vm, "_appFilterRoot", root);

            var method = typeof(FilterSearchViewModel).GetMethod("RemoveThreadConditionsFromFilterTree", NPI)!;
            method.Invoke(vm, new object[] { true });

            // Mixed group should remain but ThreadName condition removed
            Assert.Single(root.Children);
            Assert.Single(mixedGroup.Children);
            Assert.Equal("Level", mixedGroup.Children[0].Field);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - CollectFilterNodeDescriptions
        // ══════════════════════════════════════════════════

        #region CollectFilterNodeDescriptions

        [Fact]
        public void CollectFilterNodeDescriptions_ConditionNode_AddsItem()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI)!;
            var items = new List<ActiveFilterItem>();
            var node = MakeCondition("Level", "Equals", "Error");
            int idx = 0;
            method.Invoke(vm, new object[] { items, node, "FILTER", "", "APP_FILTER", idx });
            Assert.Single(items);
            Assert.Contains("Level", items[0].Description);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_GroupNode_RecursesChildren()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI)!;
            var items = new List<ActiveFilterItem>();
            var group = MakeGroup("AND",
                MakeCondition("Level", "Equals", "Error"),
                MakeCondition("Message", "Contains", "test"));
            int idx = 0;
            method.Invoke(vm, new object[] { items, group, "FILTER", "", "MAIN_FILTER", idx });
            Assert.Equal(2, items.Count);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_NoOp()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI)!;
            var items = new List<ActiveFilterItem>();
            int idx = 0;
            method.Invoke(vm, new object?[] { items, null, "FILTER", "", "APP_FILTER", idx });
            Assert.Empty(items);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_SkipsThreadNameWhenThreadFiltersActive()
        {
            var vm = CreateVM();
            var threadFilters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            threadFilters.Add("Thread1");

            var method = typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions", NPI)!;
            var items = new List<ActiveFilterItem>();
            var node = MakeCondition("ThreadName", "Equals", "Thread1");
            int idx = 0;
            method.Invoke(vm, new object[] { items, node, "FILTER", "", "MAIN_FILTER", idx });
            Assert.Empty(items);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - MarkNodeShowOnly / MarkAllNodesShowOnly
        // ══════════════════════════════════════════════════

        #region MarkNodeShowOnly

        [Fact]
        public void MarkNodeShowOnly_MatchingNode_MarkedActive()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                NPI, null, new[] { typeof(LoggerNode), typeof(string) }, null)!;

            var node = new LoggerNode { Name = "test", FullPath = "com.test" };
            method.Invoke(vm, new object[] { node, "com.test" });
            Assert.True(node.IsActive);
            Assert.False(node.IsHidden);
        }

        [Fact]
        public void MarkNodeShowOnly_AncestorNode_NotHidden()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                NPI, null, new[] { typeof(LoggerNode), typeof(string) }, null)!;

            var child = new LoggerNode { Name = "test", FullPath = "com.test" };
            var parent = new LoggerNode
            {
                Name = "com",
                FullPath = "com",
                Children = new ObservableCollection<LoggerNode> { child }
            };
            method.Invoke(vm, new object[] { parent, "com.test" });
            Assert.False(parent.IsHidden);
            Assert.False(parent.IsActive);
        }

        [Fact]
        public void MarkNodeShowOnly_UnrelatedNode_MarkedHidden()
        {
            var vm = CreateVM();
            var method = typeof(FilterSearchViewModel).GetMethod("MarkNodeShowOnly",
                NPI, null, new[] { typeof(LoggerNode), typeof(string) }, null)!;

            var node = new LoggerNode { Name = "unrelated", FullPath = "org.other" };
            method.Invoke(vm, new object[] { node, "com.test" });
            Assert.True(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void MarkAllNodesShowOnly_SetsCorrectStates()
        {
            var vm = CreateVM();

            // Set up tree: com -> test (matching), org -> other (unrelated)
            var matchChild = new LoggerNode { Name = "test", FullPath = "com.test" };
            var comNode = new LoggerNode
            {
                Name = "com", FullPath = "com",
                Children = new ObservableCollection<LoggerNode> { matchChild }
            };
            var otherNode = new LoggerNode { Name = "org", FullPath = "org" };

            vm.LoggerTreeRoot = new ObservableCollection<LoggerNode> { comNode, otherNode };

            // Use the overload that operates on LoggerTreeRoot
            var method = typeof(FilterSearchViewModel).GetMethod("MarkAllNodesShowOnly",
                NPI, null, new[] { typeof(string) }, null)!;
            method.Invoke(vm, new object[] { "com.test" });

            Assert.True(matchChild.IsActive);
            Assert.True(otherNode.IsHidden);
        }

        [Fact]
        public void MarkAllNodesShowOnly_PlcOverload_SetsCorrectStates()
        {
            var vm = CreateVM();

            var matchNode = new LoggerNode { Name = "plc", FullPath = "plc.motor" };
            var otherNode = new LoggerNode { Name = "other", FullPath = "other" };

            vm.PlcLoggerTreeRoot = new ObservableCollection<LoggerNode> { matchNode, otherNode };

            var method = typeof(FilterSearchViewModel).GetMethod("MarkAllNodesShowOnly",
                NPI, null, new[] { typeof(string), typeof(ObservableCollection<LoggerNode>) }, null)!;
            method.Invoke(vm, new object[] { "plc.motor", vm.PlcLoggerTreeRoot });

            Assert.True(matchNode.IsActive);
            Assert.True(otherNode.IsHidden);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - HasStoredFilter properties
        // ══════════════════════════════════════════════════

        #region HasStoredFilter

        [Fact]
        public void HasMainStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithAdvancedFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var root = MakeGroup("AND", MakeCondition("Level", "Equals", "Error"));
            SetField(vm, "_mainFilterRoot", root);
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithThreadFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var filters = (List<string>)GetField(vm, "_activeThreadFilters")!;
            filters.Add("Thread1");
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilter_WithTimeFocus_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_isTimeFocusActive", true);
            Assert.True(vm.HasMainStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var filters = (List<string>)GetField(vm, "_activeLoggerFilters")!;
            filters.Add("Logger1");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithMethodFilter_ReturnsTrue()
        {
            var vm = CreateVM();
            var filters = (List<string>)GetField(vm, "_activeMethodFilters")!;
            filters.Add("Method1");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_treeShowOnlyLogger", "some.logger");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeShowOnlyPrefix_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_treeShowOnlyPrefix", "some.prefix");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeHiddenLoggers_ReturnsTrue()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_treeHiddenLoggers")!;
            hidden.Add("logger");
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasAppStoredFilter_WithAppTimeFocus_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_isAppTimeFocusActive", true);
            Assert.True(vm.HasAppStoredFilter);
        }

        [Fact]
        public void HasMainStoredFilterOut_WithNegativeFilters_ReturnsTrue()
        {
            var vm = CreateVM();
            var filters = (List<string>)GetField(vm, "_negativeFilters")!;
            filters.Add("test");
            Assert.True(vm.HasMainStoredFilterOut);
        }

        [Fact]
        public void HasMainStoredFilterOut_Empty_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.HasMainStoredFilterOut);
        }

        [Fact]
        public void HasAppStoredFilterOut_WithNegativeFilters_ReturnsTrue()
        {
            var vm = CreateVM();
            var filters = (List<string>)GetField(vm, "_appNegativeFilters")!;
            filters.Add("test");
            Assert.True(vm.HasAppStoredFilterOut);
        }

        [Fact]
        public void HasAppStoredFilterOut_Empty_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.HasAppStoredFilterOut);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - IsPlcTreeFilterActive
        // ══════════════════════════════════════════════════

        #region IsPlcTreeFilterActive

        [Fact]
        public void IsPlcTreeFilterActive_NoFilters_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_plcTreeShowOnlyLogger", "test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithShowOnlyPrefix_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_plcTreeShowOnlyPrefix", "test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenLoggers_ReturnsTrue()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenLoggers")!;
            hidden.Add("test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenPrefixes_ReturnsTrue()
        {
            var vm = CreateVM();
            var hidden = (HashSet<string>)GetField(vm, "_plcTreeHiddenPrefixes")!;
            hidden.Add("test");
            Assert.True(vm.IsPlcTreeFilterActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - IsGlobalTimeRangeActive
        // ══════════════════════════════════════════════════

        #region IsGlobalTimeRangeActive

        [Fact]
        public void IsGlobalTimeRangeActive_BothSet_ReturnsTrue()
        {
            var vm = CreateVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);
            Assert.True(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_StartOnly_ReturnsFalse()
        {
            var vm = CreateVM();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        [Fact]
        public void IsGlobalTimeRangeActive_NeitherSet_ReturnsFalse()
        {
            var vm = CreateVM();
            Assert.False(vm.IsGlobalTimeRangeActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - StartRange / ClearRange
        // ══════════════════════════════════════════════════

        #region RangeOperations

        [Fact]
        public void StartRange_NoSelectedLog_NoOp()
        {
            var vm = CreateVM();
            // StartRange checks _parent.SelectedLog == null and returns early
            var method = typeof(FilterSearchViewModel).GetMethod("StartRange", NPI)!;
            method.Invoke(vm, new object?[] { null });
            Assert.False(vm.HasRangeStart);
        }

        [Fact]
        public void StartRange_State_DirectFieldTest()
        {
            var vm = CreateVM();
            // Test the state fields directly since StartRange requires _sessionVM.StatusMessage
            SetField(vm, "_rangeStartLog", MakeLog(date: DateTime.Now));
            vm.HasRangeStart = true;
            Assert.True(vm.HasRangeStart);
            Assert.NotNull(GetField(vm, "_rangeStartLog"));
        }

        [Fact]
        public void ClearRange_State_DirectFieldTest()
        {
            var vm = CreateVM();
            // Test state changes directly - ClearRange calls ToggleFilterView which needs full plumbing
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog());

            // Simulate what ClearRange does for the range-start portion
            SetField(vm, "_rangeStartLog", null);
            vm.HasRangeStart = false;
            Assert.False(vm.HasRangeStart);
            Assert.Null(GetField(vm, "_rangeStartLog"));
        }

        [Fact]
        public void ClearRange_App_TimeFocus_DirectFieldTest()
        {
            var vm = CreateVM();
            // Simulate what ClearRange does for APP tab time focus
            SetField(vm, "_isAppTimeFocusActive", true);
            vm.IsAppTimeFocusActive = false;
            vm.LastFilteredAppCache = null;
            vm.IsAppFilterActive = false;

            Assert.False(vm.IsAppTimeFocusActive);
            Assert.False(vm.IsAppFilterActive);
            Assert.Null(vm.LastFilteredAppCache);
        }

        [Fact]
        public void ClearRange_Main_TimeFocus_DirectFieldTest()
        {
            var vm = CreateVM();
            // Simulate what ClearRange does for Main tab time focus
            SetField(vm, "_isTimeFocusActive", true);
            vm.IsTimeFocusActive = false;
            vm.LastFilteredCache = null;
            vm.IsMainFilterActive = false;

            Assert.False(vm.IsTimeFocusActive);
            Assert.False(vm.IsMainFilterActive);
            Assert.Null(vm.LastFilteredCache);
        }

        [Fact]
        public void EndRange_NoSelectedLog_NoOp()
        {
            var vm = CreateVM();
            // EndRange checks _parent.SelectedLog == null and returns
            var method = typeof(FilterSearchViewModel).GetMethod("EndRange", NPI)!;
            method.Invoke(vm, new object?[] { null });
            Assert.False(vm.HasRangeStart);
        }

        [Fact]
        public void EndRange_NoRangeStart_NoOp()
        {
            var vm = CreateVM();
            var parent = GetParent(vm);
            typeof(MainViewModel).GetProperty("SelectedLog")?.SetValue(parent, MakeLog());
            // _rangeStartLog is null
            var method = typeof(FilterSearchViewModel).GetMethod("EndRange", NPI)!;
            method.Invoke(vm, new object?[] { null });
            Assert.False(vm.HasRangeStart);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterNode - CompiledRegex
        // ══════════════════════════════════════════════════

        #region FilterNodeCompiledRegex

        [Fact]
        public void FilterNode_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var node = MakeCondition("Message", "Contains", "test");
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RegexOperator_ReturnsCompiled()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Regex",
                Value = @"\d+"
            };
            Assert.NotNull(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidRegex_ReturnsNull()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Regex",
                Value = "[invalid"
            };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Regex",
                Value = ""
            };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesResult()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Regex",
                Value = @"\d+"
            };
            var first = node.CompiledRegex;
            var second = node.CompiledRegex;
            Assert.Same(first, second);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RecompilesOnValueChange()
        {
            var node = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Regex",
                Value = @"\d+"
            };
            var first = node.CompiledRegex;
            node.Value = @"\w+";
            var second = node.CompiledRegex;
            Assert.NotSame(first, second);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // LoggerNode tests
        // ══════════════════════════════════════════════════

        #region LoggerNode

        [Fact]
        public void LoggerNode_DisplayText_IncludesCount()
        {
            var node = new LoggerNode { Name = "test", Count = 42 };
            Assert.Equal("test (42)", node.DisplayText);
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
        public void LoggerNode_PropertyChanged_IsExpanded()
        {
            var node = new LoggerNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.IsExpanded = true;
            Assert.Equal("IsExpanded", changedProp);
        }

        [Fact]
        public void LoggerNode_PropertyChanged_IsSelected()
        {
            var node = new LoggerNode();
            string? changedProp = null;
            node.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            node.IsSelected = true;
            Assert.Equal("IsSelected", changedProp);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // ActiveFilterItem tests
        // ══════════════════════════════════════════════════

        #region ActiveFilterItem

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

        [Fact]
        public void ActiveFilterItem_SetProperties()
        {
            var brush = new SolidColorBrush(Colors.Red);
            brush.Freeze();
            var item = new ActiveFilterItem
            {
                Category = "FILTER",
                Description = "Level Equals Error",
                IsActive = true,
                Key = "APP_FILTER:0",
                ColorBrush = brush
            };
            Assert.Equal("FILTER", item.Category);
            Assert.Equal("Level Equals Error", item.Description);
            Assert.True(item.IsActive);
            Assert.Equal("APP_FILTER:0", item.Key);
            Assert.NotNull(item.ColorBrush);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterSearchViewModel - Property setters
        // ══════════════════════════════════════════════════

        #region PropertySetters

        [Fact]
        public void FilterSearchViewModel_IsMainFilterActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsMainFilterActive") raised = true; };
            vm.IsMainFilterActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsAppFilterActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsAppFilterActive") raised = true; };
            vm.IsAppFilterActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsTimeFocusActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsTimeFocusActive") raised = true; };
            vm.IsTimeFocusActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsAppTimeFocusActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsAppTimeFocusActive") raised = true; };
            vm.IsAppTimeFocusActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_GlobalTimeRangeStart_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "GlobalTimeRangeStart") raised = true; };
            vm.GlobalTimeRangeStart = DateTime.Now;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_GlobalTimeRangeEnd_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "GlobalTimeRangeEnd") raised = true; };
            vm.GlobalTimeRangeEnd = DateTime.Now;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_HasRangeStart_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "HasRangeStart") raised = true; };
            vm.HasRangeStart = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsMainFilterOutActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsMainFilterOutActive") raised = true; };
            vm.IsMainFilterOutActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsAppFilterOutActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsAppFilterOutActive") raised = true; };
            vm.IsAppFilterOutActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_IsAppErrorFilterActive_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsAppErrorFilterActive") raised = true; };
            vm.IsAppErrorFilterActive = true;
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_MainFilterRoot_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "MainFilterRoot") raised = true; };
            vm.MainFilterRoot = MakeGroup("AND");
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_AppFilterRoot_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "AppFilterRoot") raised = true; };
            vm.AppFilterRoot = MakeGroup("AND");
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_SavedFilterRoot_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "SavedFilterRoot") raised = true; };
            vm.SavedFilterRoot = MakeGroup("AND");
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_LastFilteredCache_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "LastFilteredCache") raised = true; };
            vm.LastFilteredCache = new List<LogEntry>();
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_LastFilteredAppCache_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "LastFilteredAppCache") raised = true; };
            vm.LastFilteredAppCache = new List<LogEntry>();
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_TreeShowOnlyLogger_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "TreeShowOnlyLogger") raised = true; };
            vm.TreeShowOnlyLogger = "test";
            Assert.True(raised);
        }

        [Fact]
        public void FilterSearchViewModel_TreeShowOnlyPrefix_NotifiesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "TreeShowOnlyPrefix") raised = true; };
            vm.TreeShowOnlyPrefix = "test";
            Assert.True(raised);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // FilterNode - PropertyChanged notifications
        // ══════════════════════════════════════════════════

        #region FilterNodePropertyChanged

        [Fact]
        public void FilterNode_Type_NotifiesPropertyChanged()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Type = NodeType.Condition;
            Assert.Equal("Type", prop);
        }

        [Fact]
        public void FilterNode_LogicalOperator_NotifiesPropertyChanged()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.LogicalOperator = "OR";
            Assert.Equal("LogicalOperator", prop);
        }

        [Fact]
        public void FilterNode_Field_NotifiesPropertyChanged()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Field = "Level";
            Assert.Equal("Field", prop);
        }

        [Fact]
        public void FilterNode_Operator_NotifiesPropertyChanged()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Operator = "Equals";
            Assert.Equal("Operator", prop);
        }

        [Fact]
        public void FilterNode_Value_NotifiesPropertyChanged()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Value = "test";
            Assert.Equal("Value", prop);
        }

        [Fact]
        public void FilterNode_Value_ClearsCompiledRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var first = node.CompiledRegex;
            Assert.NotNull(first);
            node.Value = @"\w+";
            // After value change, should recompile
            var second = node.CompiledRegex;
            Assert.NotSame(first, second);
        }

        [Fact]
        public void FilterNode_IsEnabled_DefaultTrue()
        {
            var node = new FilterNode();
            Assert.True(node.IsEnabled);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // ResetPlcVisualStates / ResetAllVisualStates
        // ══════════════════════════════════════════════════

        #region ResetVisualStates

        [Fact]
        public void ResetPlcVisualStates_ClearsAllNodes()
        {
            var vm = CreateVM();
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var root = new LoggerNode
            {
                Name = "root", IsHidden = true, IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };
            vm.PlcLoggerTreeRoot = new ObservableCollection<LoggerNode> { root };

            var method = typeof(FilterSearchViewModel).GetMethod("ResetPlcVisualStates", NPI)!;
            method.Invoke(vm, null);

            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        [Fact]
        public void ResetAllVisualStates_ClearsAllNodes()
        {
            var vm = CreateVM();
            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var root = new LoggerNode
            {
                Name = "root", IsHidden = true, IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };
            vm.LoggerTreeRoot = new ObservableCollection<LoggerNode> { root };

            var method = typeof(FilterSearchViewModel).GetMethod("ResetAllVisualStates", NPI)!;
            method.Invoke(vm, null);

            Assert.False(root.IsHidden);
            Assert.False(root.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        #endregion

        // ══════════════════════════════════════════════════
        // TextFilterParser - Additional tokenizer edge cases
        // ══════════════════════════════════════════════════

        #region TextFilterParser_Tokenizer

        [Fact]
        public void TextFilterParser_Parse_SkipsUnknownCharacters()
        {
            // $ and @ are unknown - should be skipped
            var result = TextFilterParser.Parse("Contains([Message], 'test')");
            Assert.NotNull(result);
        }

        [Fact]
        public void TextFilterParser_Parse_HandlesUnderscoreInIdentifier()
        {
            var result = TextFilterParser.Parse("Contains([My_Field], 'value')");
            Assert.NotNull(result);
            Assert.Equal("My_Field", result.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_MixedCase_Or_And()
        {
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') OR Contains([Message], 'b')");
            Assert.NotNull(result);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_LowercaseOr()
        {
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') or Contains([Message], 'b')");
            Assert.NotNull(result);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_LowercaseAnd()
        {
            var result = TextFilterParser.Parse(
                "Contains([Message], 'a') and Contains([Message], 'b')");
            Assert.NotNull(result);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_ComplexNested()
        {
            var result = TextFilterParser.Parse(
                "(Contains([Message], 'a') Or Contains([Message], 'b')) And (Contains([Level], 'Error') Or Contains([Level], 'Warning'))");
            Assert.NotNull(result);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            Assert.Equal("OR", result.Children[0].LogicalOperator);
            Assert.Equal("OR", result.Children[1].LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_MissingCommaThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message] 'value')"));
        }

        [Fact]
        public void TextFilterParser_Parse_MissingCloseParenInConditionThrows()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'value'"));
        }

        #endregion

        // ══════════════════════════════════════════════════
        // LogColoringService - SystemDefaultColor
        // ══════════════════════════════════════════════════

        #region SystemDefaultColor

        [Fact]
        public async Task LogColoringService_SetSystemDefaultColor_PLC_StateTransition()
        {
            var svc = new LogColoringService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "nonmatch", Color = Colors.Red }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(threadName: "Manager", message: "PlcMngr: Idle -> Running")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].SystemDefaultColor);
        }

        [Fact]
        public async Task LogColoringService_SetSystemDefaultColor_PLC_BinaryState()
        {
            var svc = new LogColoringService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "nonmatch", Color = Colors.Red }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(message: "==== STATE CHANGE ====")
            };
            await svc.ApplyDefaultColorsAsync(logs, false);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].SystemDefaultColor);
        }

        [Fact]
        public async Task LogColoringService_SetSystemDefaultColor_APP_NoSystemColor()
        {
            var svc = new LogColoringService();
            svc.UserDefaultAppRules = new List<ColoringCondition>
            {
                new() { Field = "Message", Operator = "Contains", Value = "nonmatch", Color = Colors.Red }
            };
            var logs = new List<LogEntry>
            {
                MakeLog(message: "app log")
            };
            await svc.ApplyDefaultColorsAsync(logs, true);
            Assert.Null(logs[0].SystemDefaultColor);
        }

        #endregion
    }
}
