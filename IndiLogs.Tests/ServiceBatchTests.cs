using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Views;
using Xunit;

namespace IndiLogs.Tests
{
    public class ServiceBatchTests
    {
        // ===================================================================
        // AppConstants tests
        // ===================================================================

        [Fact]
        public void AppConstants_RegexTimeout_IsTwoSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(2), AppConstants.RegexTimeout);
        }

        [Fact]
        public void AppConstants_JsonMaxDepth_Is64()
        {
            Assert.Equal(64, AppConstants.JsonMaxDepth);
        }

        [Fact]
        public void AppConstants_SafeJsonSettings_HasCorrectMaxDepth()
        {
            Assert.Equal(64, AppConstants.SafeJsonSettings.MaxDepth);
        }

        [Fact]
        public void AppConstants_SafeJsonDocumentOptions_HasCorrectMaxDepth()
        {
            Assert.Equal(64, AppConstants.SafeJsonDocumentOptions.MaxDepth);
        }

        [Fact]
        public void AppConstants_ZipExtension_IsCorrect()
        {
            Assert.Equal(".zip", AppConstants.ZipExtension);
        }

        [Fact]
        public void AppConstants_CsvExtension_IsCorrect()
        {
            Assert.Equal(".csv", AppConstants.CsvExtension);
        }

        [Fact]
        public void AppConstants_UiUpdateBatchSize_Is500()
        {
            Assert.Equal(500, AppConstants.UiUpdateBatchSize);
        }

        [Fact]
        public void AppConstants_SystabPrefix_IsCorrect()
        {
            Assert.Equal("systab_", AppConstants.SystabPrefix);
        }

        [Fact]
        public void AppConstants_SystabExtension_IsCorrect()
        {
            Assert.Equal(".txt", AppConstants.SystabExtension);
        }

        [Fact]
        public void AppConstants_TabIndices_AreSequential()
        {
            Assert.Equal(0, AppConstants.TAB_PLC);
            Assert.Equal(1, AppConstants.TAB_APP);
            Assert.Equal(2, AppConstants.TAB_EVENTS);
            Assert.Equal(3, AppConstants.TAB_SCREENSHOTS);
            Assert.Equal(4, AppConstants.TAB_CONFIG);
            Assert.Equal(5, AppConstants.TAB_TERMINALS);
            Assert.Equal(6, AppConstants.TAB_SETUP_INFO);
            Assert.Equal(7, AppConstants.TAB_GLOBALS);
            Assert.Equal(8, AppConstants.TAB_SYSTAB);
            Assert.Equal(9, AppConstants.TAB_CHARTS);
            Assert.Equal(10, AppConstants.TAB_CPR);
            Assert.Equal(11, AppConstants.TAB_STEP_RECORDER);
            Assert.Equal(12, AppConstants.TAB_DIFFERENT_LOGS);
        }

        [Fact]
        public void AppConstants_BuiltInFields_ContainsExpectedFields()
        {
            Assert.True(AppConstants.BuiltInFields.Contains("Date"));
            Assert.True(AppConstants.BuiltInFields.Contains("Level"));
            Assert.True(AppConstants.BuiltInFields.Contains("Message"));
            Assert.True(AppConstants.BuiltInFields.Contains("ThreadName"));
            Assert.True(AppConstants.BuiltInFields.Contains("Logger"));
        }

        [Fact]
        public void AppConstants_ErrorLevels_ContainsErrorAndFatal()
        {
            Assert.True(AppConstants.ErrorLevels.Contains("Error"));
            Assert.True(AppConstants.ErrorLevels.Contains("Fatal"));
            Assert.Equal(2, AppConstants.ErrorLevels.Count);
        }

        [Fact]
        public void AppConstants_ErrorLevels_CaseInsensitive()
        {
            Assert.True(AppConstants.ErrorLevels.Contains("error"));
            Assert.True(AppConstants.ErrorLevels.Contains("FATAL"));
        }

        [Fact]
        public void AppConstants_EscapeSqlBracketId_EscapesBrackets()
        {
            Assert.Equal("test]]value", AppConstants.EscapeSqlBracketId("test]value"));
        }

        [Fact]
        public void AppConstants_EscapeSqlBracketId_NullOrEmpty_ReturnsInput()
        {
            Assert.Null(AppConstants.EscapeSqlBracketId(null!));
            Assert.Equal("", AppConstants.EscapeSqlBracketId(""));
        }

        [Fact]
        public void AppConstants_EscapeSqlBracketId_NoSpecialChars_ReturnsInput()
        {
            Assert.Equal("normalId", AppConstants.EscapeSqlBracketId("normalId"));
        }

        [Fact]
        public void AppConstants_EscapeSqlIdentifier_EscapesDoubleQuotes()
        {
            Assert.Equal("test\"\"value", AppConstants.EscapeSqlIdentifier("test\"value"));
        }

        [Fact]
        public void AppConstants_EscapeSqlIdentifier_NullOrEmpty_ReturnsInput()
        {
            Assert.Null(AppConstants.EscapeSqlIdentifier(null!));
            Assert.Equal("", AppConstants.EscapeSqlIdentifier(""));
        }

        [Fact]
        public void AppConstants_S4StateRegex_MatchesEnter()
        {
            var match = AppConstants.S4StateRegex().Match("==== STATE_STANDBY - Enter ======");
            Assert.True(match.Success);
            Assert.Equal("STANDBY", match.Groups[1].Value);
            Assert.Equal("Enter", match.Groups[2].Value);
        }

        [Fact]
        public void AppConstants_S4StateRegex_MatchesExit()
        {
            var match = AppConstants.S4StateRegex().Match("==== STATE_SERVICE - Exit ======");
            Assert.True(match.Success);
            Assert.Equal("SERVICE", match.Groups[1].Value);
            Assert.Equal("Exit", match.Groups[2].Value);
        }

        [Fact]
        public void AppConstants_S4StateRegex_NoMatch_ReturnsFalse()
        {
            var match = AppConstants.S4StateRegex().Match("some random log message");
            Assert.False(match.Success);
        }

        [Fact]
        public void AppConstants_FileTimestampRegex_MatchesTimestamp()
        {
            var match = AppConstants.FileTimestampRegex().Match("logs_2024-01-15T14-30-00");
            Assert.True(match.Success);
            Assert.Equal("2024", match.Groups[1].Value);
            Assert.Equal("01", match.Groups[2].Value);
            Assert.Equal("15", match.Groups[3].Value);
            Assert.Equal("14", match.Groups[4].Value);
            Assert.Equal("30", match.Groups[5].Value);
            Assert.Equal("00", match.Groups[6].Value);
        }

        [Fact]
        public void AppConstants_FileTimestampRegex_MatchesUnderscoreSeparator()
        {
            var match = AppConstants.FileTimestampRegex().Match("2024-03-20_08-15-30");
            Assert.True(match.Success);
            Assert.Equal("2024", match.Groups[1].Value);
        }

        // ===================================================================
        // AppPaths tests
        // ===================================================================

        [Fact]
        public void AppPaths_Root_ContainsIndiLogs()
        {
            Assert.Contains("IndiLogs3.0", AppPaths.Root);
        }

        [Fact]
        public void AppPaths_ConfigPrefix_IsCorrect()
        {
            Assert.Equal("config_", AppPaths.ConfigPrefix);
        }

        [Fact]
        public void AppPaths_SearchProfilePrefix_IsCorrect()
        {
            Assert.Equal("searchprofile_", AppPaths.SearchProfilePrefix);
        }

        [Fact]
        public void AppPaths_DbColumnPrefix_IsCorrect()
        {
            Assert.Equal("dbcol_", AppPaths.DbColumnPrefix);
        }

        // ===================================================================
        // ChartStateConfig tests
        // ===================================================================

        [Theory]
        [InlineData("INIT", 1)]
        [InlineData("STANDBY", 6)]
        [InlineData("PRINT", 10)]
        [InlineData("READY", 8)]
        [InlineData("OFF", 3)]
        public void ChartStateConfig_GetId_KnownNames(string name, int expectedId)
        {
            Assert.Equal(expectedId, ChartStateConfig.GetId(name));
        }

        [Fact]
        public void ChartStateConfig_GetId_NumericString_ReturnsNumber()
        {
            Assert.Equal(5, ChartStateConfig.GetId("5"));
        }

        [Fact]
        public void ChartStateConfig_GetId_EmptyString_ReturnsZero()
        {
            Assert.Equal(0, ChartStateConfig.GetId(""));
        }

        [Fact]
        public void ChartStateConfig_GetId_Null_ReturnsZero()
        {
            Assert.Equal(0, ChartStateConfig.GetId(null!));
        }

        [Fact]
        public void ChartStateConfig_GetId_UnknownName_ReturnsZero()
        {
            Assert.Equal(0, ChartStateConfig.GetId("COMPLETELY_UNKNOWN"));
        }

        [Fact]
        public void ChartStateConfig_GetId_CaseInsensitive()
        {
            Assert.Equal(10, ChartStateConfig.GetId("print"));
            Assert.Equal(10, ChartStateConfig.GetId("Print"));
        }

        [Fact]
        public void ChartStateConfig_GetId_PartialMatch()
        {
            // "STATE_INIT" contains "INIT"
            Assert.Equal(1, ChartStateConfig.GetId("STATE_INIT"));
        }

        [Fact]
        public void ChartStateConfig_GetName_KnownId_ReturnsName()
        {
            Assert.Equal("PRINT", ChartStateConfig.GetName(10));
            Assert.Equal("INIT", ChartStateConfig.GetName(1));
        }

        [Fact]
        public void ChartStateConfig_GetName_UnknownId_ReturnsIdString()
        {
            Assert.Equal("99", ChartStateConfig.GetName(99));
        }

        [Fact]
        public void ChartStateConfig_GetColor_KnownId_ReturnsNonTransparent()
        {
            var color = ChartStateConfig.GetColor(10);
            Assert.Equal(60, color.Alpha);
        }

        [Fact]
        public void ChartStateConfig_GetColor_UnknownId_ReturnsTransparent()
        {
            var color = ChartStateConfig.GetColor(999);
            Assert.Equal(SkiaSharp.SKColors.Transparent, color);
        }

        [Fact]
        public void ChartStateConfig_GetSolidColor_KnownId_ReturnsColor()
        {
            var color = ChartStateConfig.GetSolidColor(10);
            Assert.NotEqual(SkiaSharp.SKColors.Gray, color);
        }

        [Fact]
        public void ChartStateConfig_GetSolidColor_UnknownId_ReturnsGray()
        {
            Assert.Equal(SkiaSharp.SKColors.Gray, ChartStateConfig.GetSolidColor(999));
        }

        [Fact]
        public void ChartStateConfig_StateNameToId_HasAllEntries()
        {
            Assert.Equal(ChartStateConfig.StateNames.Count, ChartStateConfig.StateNameToId.Count);
        }

        // ===================================================================
        // TextFilterParser - additional complex tests
        // ===================================================================

        [Fact]
        public void TextFilterParser_TripleOr_CreatesOrGroupWith3Children()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') Or Contains([Message], 'c')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_TripleAnd_CreatesAndGroupWith3Children()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Level], 'b') And Contains([Logger], 'c')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_NestedParens_ParsesCorrectly()
        {
            var node = TextFilterParser.Parse(
                "((Contains([Message], 'a')))");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("a", node.Value);
        }

        [Fact]
        public void TextFilterParser_ProcessNameField_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("Contains([ProcessName], 'test')");
            Assert.NotNull(node);
            Assert.Equal("ProcessName", node.Field);
        }

        [Fact]
        public void TextFilterParser_ConditionNode_HasAndLogicalOperator()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'x')");
            Assert.NotNull(node);
            Assert.Equal("AND", node.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_ValueWithSpaces_PreservedCorrectly()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'hello world test')");
            Assert.NotNull(node);
            Assert.Equal("hello world test", node.Value);
        }

        [Fact]
        public void TextFilterParser_MissingField_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains(, 'value')"));
        }

        [Fact]
        public void TextFilterParser_MissingValue_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], )"));
        }

        [Fact]
        public void TextFilterParser_ComplexExpression_OrAndCombination()
        {
            // (A Or B) And (C Or D)
            var node = TextFilterParser.Parse(
                "(Contains([Message], 'a') Or Contains([Message], 'b')) And (Contains([Level], 'c') Or Contains([Level], 'd'))");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
            Assert.Equal("OR", node.Children[0].LogicalOperator);
            Assert.Equal("OR", node.Children[1].LogicalOperator);
        }

        // ===================================================================
        // ChartDataTransferService.Parsing - TryParseDoubleFast tests
        // ===================================================================

        private static bool InvokeTryParseDoubleFast(string s, int start, int length, out double result)
        {
            var method = typeof(ChartDataTransferService).GetMethod("TryParseDoubleFast",
                BindingFlags.NonPublic | BindingFlags.Static);
            var args = new object[] { s, start, length, 0.0 };
            bool ret = (bool)method!.Invoke(null, args)!;
            result = (double)args[3];
            return ret;
        }

        [Fact]
        public void TryParseDoubleFast_Integer_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("12345", 0, 5, out double result));
            Assert.Equal(12345.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_Decimal_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("3.14", 0, 4, out double result));
            Assert.Equal(3.14, result, 2);
        }

        [Fact]
        public void TryParseDoubleFast_Negative_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("-42.5", 0, 5, out double result));
            Assert.Equal(-42.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_PositiveSign_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("+7", 0, 2, out double result));
            Assert.Equal(7.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_Substring_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("abc123.5def", 3, 5, out double result));
            Assert.Equal(123.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_ScientificNotation_FallsBackToDouble()
        {
            Assert.True(InvokeTryParseDoubleFast("1.5e3", 0, 5, out double result));
            Assert.Equal(1500.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_Empty_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("", 0, 0, out _));
        }

        [Fact]
        public void TryParseDoubleFast_OnlySign_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("-", 0, 1, out _));
        }

        [Fact]
        public void TryParseDoubleFast_NonNumeric_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("abc", 0, 3, out _));
        }

        [Fact]
        public void TryParseDoubleFast_Zero_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast("0", 0, 1, out double result));
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void TryParseDoubleFast_DecimalOnly_ParsesCorrectly()
        {
            Assert.True(InvokeTryParseDoubleFast(".5", 0, 2, out double result));
            Assert.Equal(0.5, result, 1);
        }

        [Fact]
        public void TryParseDoubleFast_TrailingChars_ReturnsFalse()
        {
            Assert.False(InvokeTryParseDoubleFast("12x", 0, 3, out _));
        }

        // ===================================================================
        // ChartDataTransferService.Parsing - InternString tests
        // ===================================================================

        private static string InvokeInternString(Dictionary<string, string> pool, string source, int start, int length)
        {
            var method = typeof(ChartDataTransferService).GetMethod("InternString",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (string)method!.Invoke(null, new object[] { pool, source, start, length })!;
        }

        [Fact]
        public void InternString_FirstCall_AddsToPool()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var result = InvokeInternString(pool, "hello world", 0, 5);
            Assert.Equal("hello", result);
            Assert.Single(pool);
        }

        [Fact]
        public void InternString_SecondCall_ReturnsCachedInstance()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var first = InvokeInternString(pool, "hello world", 0, 5);
            var second = InvokeInternString(pool, "  hello  test", 2, 5);
            Assert.Same(first, second);
        }

        [Fact]
        public void InternString_Trims_Whitespace()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var result = InvokeInternString(pool, "  test  ", 0, 8);
            Assert.Equal("test", result);
        }

        // ===================================================================
        // GlobalGrepService - EvaluateCriteria tests
        // ===================================================================

        [Fact]
        public void EvaluateCriteria_EmptyGroups_ReturnsTrue()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };

            Assert.True(service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NullGroups_ReturnsTrue()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = null! };

            Assert.True(service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_SingleMatchingCondition_ReturnsTrue()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello world" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello" }
                        }
                    }
                }
            };

            Assert.True(service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NonMatchingCondition_ReturnsFalse()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello world" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "xyz" }
                        }
                    }
                }
            };

            Assert.False(service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateGroup_AndOperator_AllMustMatch()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello world", Level = "Error" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "Error" }
                }
            };

            Assert.True(service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_AnyCanMatch()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "Error" }
                }
            };

            Assert.True(service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_NoneCanMatch()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "xyz" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "Error" }
                }
            };

            Assert.True(service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_AnyMatch_ReturnsFalse()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello" }
                }
            };

            Assert.False(service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_EmptyConditions_ReturnsTrue()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "test" };
            var group = new SearchConditionGroup { Conditions = new List<SearchCondition>() };

            Assert.True(service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateCondition_EqualsOperator()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Level = "Error" };
            var condition = new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" };

            Assert.True(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_StartsWithOperator()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "Starting process..." };
            var condition = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.StartsWith, Value = "Starting" };

            Assert.True(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_EndsWithOperator()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "Process completed" };
            var condition = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.EndsWith, Value = "completed" };

            Assert.True(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_RegexOperator()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "Error code: 42" };
            var condition = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"code:\s*\d+" };

            Assert.True(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_Negated_InvertsResult()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello" };
            var condition = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello", Negate = true };

            Assert.False(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_AnyField_MatchesMultipleFields()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "nothing", Level = "Info", Logger = "MyLogger" };
            var condition = new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "MyLogger" };

            Assert.True(service.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCriteria_OrGroupOperator_AnyGroupCanMatch()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "test", Level = "Info" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.Or,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" }
                        }
                    }
                }
            };

            Assert.True(service.EvaluateCriteria(entry, criteria));
        }

        // ===================================================================
        // GlobalGrepService - DetermineMatchedFields tests
        // ===================================================================

        [Fact]
        public void DetermineMatchedFields_EmptyGroups_ReturnsEmpty()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = null! };

            Assert.Equal("", service.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_MessageMatch_ReturnsMessage()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "hello" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "hello" }
                        }
                    }
                }
            };

            Assert.Equal("Message", service.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_AnyField_ReportsSpecificField()
        {
            var service = new GlobalGrepService();
            var entry = new LogEntry { Message = "nope", Logger = "target" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "target" }
                        }
                    }
                }
            };

            Assert.Contains("Logger", service.DetermineMatchedFields(entry, criteria));
        }

        // ===================================================================
        // LogStatisticsService tests
        // ===================================================================

        [Fact]
        public void LogStatisticsService_ComputeStatistics_EmptyLogs_ReturnsZeros()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());

            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Equal(0, result.TotalPlcErrors);
            Assert.Equal(0, result.TotalAppErrors);
        }

        [Fact]
        public void LogStatisticsService_ComputeStatistics_CountsLogs()
        {
            var plc = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Info", Message = "msg1" },
                new LogEntry { Date = DateTime.Now, Level = "Error", Message = "err1" }
            };
            var app = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Info", Message = "msg2", Logger = "TestLogger" }
            };

            var result = LogStatisticsService.ComputeStatistics(plc, app);

            Assert.Equal(2, result.TotalPlcLogs);
            Assert.Equal(1, result.TotalAppLogs);
            Assert.Equal(1, result.TotalPlcErrors);
            Assert.Equal(0, result.TotalAppErrors);
        }

        [Fact]
        public void LogStatisticsService_GetErrorLogs_FiltersErrors()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Level = "Info" },
                new LogEntry { Level = "Error" },
                new LogEntry { Level = "Fatal" },
                new LogEntry { Level = "Debug" }
            };

            var errors = LogStatisticsService.GetErrorLogs(logs);
            Assert.Equal(2, errors.Count);
        }

        [Fact]
        public void LogStatisticsService_TopN_ReturnsTopItems()
        {
            var dict = new Dictionary<string, int>
            {
                { "a", 5 }, { "b", 10 }, { "c", 3 }, { "d", 8 }, { "e", 1 }
            };

            var top = LogStatisticsService.TopN(dict, 3);

            Assert.Equal(3, top.Count);
            Assert.Equal("b", top[0].Key);
            Assert.Equal(10, top[0].Value);
            Assert.Equal("d", top[1].Key);
            Assert.Equal(8, top[1].Value);
            Assert.Equal("a", top[2].Key);
            Assert.Equal(5, top[2].Value);
        }

        [Fact]
        public void LogStatisticsService_TopN_FewerItemsThanN()
        {
            var dict = new Dictionary<string, int> { { "a", 5 }, { "b", 3 } };
            var top = LogStatisticsService.TopN(dict, 10);

            Assert.Equal(2, top.Count);
            Assert.Equal("a", top[0].Key);
        }

        [Fact]
        public void LogStatisticsService_TopN_EmptyDict_ReturnsEmpty()
        {
            var top = LogStatisticsService.TopN(new Dictionary<string, int>(), 5);
            Assert.Empty(top);
        }

        [Fact]
        public void LogStatisticsService_FindGaps_DetectsGaps()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime, Message = "first" },
                new LogEntry { Date = baseTime.AddSeconds(0.5), Message = "second" },
                new LogEntry { Date = baseTime.AddSeconds(5), Message = "after gap" }
            };

            var gaps = LogStatisticsService.FindGaps(logs);

            Assert.Single(gaps);
            Assert.Equal(1, gaps[0].Index);
            Assert.True(gaps[0].Duration.TotalSeconds >= 2);
        }

        [Fact]
        public void LogStatisticsService_FindGaps_NoGaps_ReturnsEmpty()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime, Message = "a" },
                new LogEntry { Date = baseTime.AddSeconds(0.1), Message = "b" },
                new LogEntry { Date = baseTime.AddSeconds(0.2), Message = "c" }
            };

            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void LogStatisticsService_FindGaps_SingleLog_ReturnsEmpty()
        {
            var logs = new List<LogEntry> { new LogEntry { Date = DateTime.Now, Message = "only" } };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void LogStatisticsService_FindGaps_NullLogs_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(null!));
        }

        [Fact]
        public void LogStatisticsService_TruncateMessage_ShortMessage_ReturnsAsIs()
        {
            Assert.Equal("hello", LogStatisticsService.TruncateMessage("hello", 100));
        }

        [Fact]
        public void LogStatisticsService_TruncateMessage_LongMessage_Truncates()
        {
            var result = LogStatisticsService.TruncateMessage("a very long message that exceeds the limit", 10);
            Assert.Equal("a very lon...", result);
        }

        [Fact]
        public void LogStatisticsService_TruncateMessage_Empty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 10));
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 10));
        }

        [Fact]
        public void LogStatisticsService_FormatDuration_Seconds()
        {
            Assert.Equal("30.0 sec", LogStatisticsService.FormatDuration(TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public void LogStatisticsService_FormatDuration_Minutes()
        {
            Assert.Equal("2.5 min", LogStatisticsService.FormatDuration(TimeSpan.FromMinutes(2.5)));
        }

        [Fact]
        public void LogStatisticsService_GetShortLoggerName_ShortLogger_ReturnsFull()
        {
            Assert.Equal("My.Logger", LogStatisticsService.GetShortLoggerName("My.Logger"));
        }

        [Fact]
        public void LogStatisticsService_GetShortLoggerName_LongLogger_ReturnsLast2()
        {
            Assert.Equal("Pipeline.Engine", LogStatisticsService.GetShortLoggerName("Press.BL.Pipeline.Engine"));
        }

        [Fact]
        public void LogStatisticsService_GetShortLoggerName_Null_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
        }

        [Fact]
        public void LogStatisticsService_GetShortLoggerName_Empty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        [Fact]
        public void LogStatisticsService_CalculateErrorHistogram_EmptyErrors_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 10));
        }

        [Fact]
        public void LogStatisticsService_CalculateErrorHistogram_GroupsByMessage()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Message = "Error A", Level = "Error" },
                new LogEntry { Message = "Error A", Level = "Error" },
                new LogEntry { Message = "Error B", Level = "Error" }
            };

            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void LogStatisticsService_CalculateLoadDistribution_GroupsByKey()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { ThreadName = "ThreadA", Message = "m" },
                new LogEntry { ThreadName = "ThreadA", Message = "m" },
                new LogEntry { ThreadName = "ThreadB", Message = "m" }
            };

            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName!, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void LogStatisticsService_CalculateLoadDistribution_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateLoadDistribution(new List<LogEntry>(), l => l.ThreadName!, 10));
        }

        [Fact]
        public void LogStatisticsService_CalculateStateEntries_S6Transitions()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime, Message = "some log", ThreadName = "Test" },
                new LogEntry { Date = baseTime.AddSeconds(1), Message = "PlcMngr: INIT -> STANDBY", ThreadName = "Manager" },
                new LogEntry { Date = baseTime.AddSeconds(5), Message = "PlcMngr: STANDBY -> READY", ThreadName = "Manager" },
                new LogEntry { Date = baseTime.AddSeconds(10), Message = "last log", ThreadName = "Test" }
            };

            var states = LogStatisticsService.CalculateStateEntries(plcLogs);

            Assert.True(states.Count >= 2);
        }

        [Fact]
        public void LogStatisticsService_CalculateStateEntries_S4Transitions()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime, Message = "==== STATE_STANDBY - Enter ======", ThreadName = "PLC" },
                new LogEntry { Date = baseTime.AddSeconds(5), Message = "==== STATE_READY - Enter ======", ThreadName = "PLC" }
            };

            var states = LogStatisticsService.CalculateStateEntries(plcLogs);
            Assert.Equal(2, states.Count);
        }

        [Fact]
        public void LogStatisticsService_CalculateStateEntries_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        [Fact]
        public void LogStatisticsService_MapErrorsToStates_MapsCorrectly()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "STANDBY", StartTime = baseTime, EndTime = baseTime.AddSeconds(10) },
                new StateEntry { StateName = "PRINT", StartTime = baseTime.AddSeconds(10), EndTime = baseTime.AddSeconds(20) }
            };
            var errors = new List<LogEntry>
            {
                new LogEntry { Date = baseTime.AddSeconds(5), Level = "Error" },
                new LogEntry { Date = baseTime.AddSeconds(15), Level = "Error" },
                new LogEntry { Date = baseTime.AddSeconds(16), Level = "Error" }
            };

            var result = LogStatisticsService.MapErrorsToStates(errors, states);
            Assert.Equal(2, result.Count);
            var printErrors = result.First(r => r.Name == "PRINT");
            Assert.Equal(2, printErrors.Count);
        }

        [Fact]
        public void LogStatisticsService_MapErrorsToStates_EmptyErrors_ReturnsEmpty()
        {
            var states = new List<StateEntry> { new StateEntry { StateName = "X" } };
            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), states));
        }

        [Fact]
        public void LogStatisticsService_MapErrorsToStates_EmptyStates_ReturnsEmpty()
        {
            var errors = new List<LogEntry> { new LogEntry { Level = "Error" } };
            Assert.Empty(LogStatisticsService.MapErrorsToStates(errors, new List<StateEntry>()));
        }

        // ===================================================================
        // CsvExportService - additional LogParsing tests
        // ===================================================================

        [Fact]
        public void TryParsePlcMngrState_StateWithSpaces_ExtractsCorrectly()
        {
            string msg = "CHStep: PlcMngr, Go To Off State 3 <Parent, 1, 0, 0, 0, 0>";
            var result = CsvExportService.TryParsePlcMngrState(msg, out string? stateName);
            Assert.True(result);
            Assert.NotNull(stateName);
        }

        [Fact]
        public void TryParseCHStep_MultipleCommasInStep_ParsesCorrectly()
        {
            string msg = "CHStep: MyComp, Step doing stuff, State 99 <MyParent, 5, 2, 300, 3, 1>";
            var result = CsvExportService.TryParseCHStep(msg,
                out string? chName, out string? stepMessage, out string? stateId,
                out string? chParent, out string? subsysID, out string? prevStepNo,
                out string? diffTime, out string? subStepNo, out string? chObjType);

            Assert.True(result);
            Assert.Equal("MyComp", chName);
            Assert.Equal("Step doing stuff", stepMessage);
            Assert.Equal("99", stateId);
            Assert.Equal("MyParent", chParent);
            Assert.Equal("5", subsysID);
            Assert.Equal("2", prevStepNo);
            Assert.Equal("300", diffTime);
            Assert.Equal("3", subStepNo);
            Assert.Equal("1", chObjType);
        }

        // ===================================================================
        // SearchCriteria model tests
        // ===================================================================

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var resolved = filter.Resolve();

            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True(resolved.From!.Value > DateTime.Now.AddHours(-25));
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var resolved = filter.Resolve();

            Assert.NotNull(resolved.From);
            Assert.True(resolved.From!.Value > DateTime.Now.AddDays(-8));
        }

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter { From = DateTime.Now, RelativeRange = RelativeTimeRange.None };
            var resolved = filter.Resolve();

            Assert.Same(filter, resolved);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_ValidPattern_ReturnsCompiled()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            Assert.NotNull(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "[invalid" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "" };
            Assert.Null(cond.CompiledRegex);
        }

        // ===================================================================
        // FilterNode model tests
        // ===================================================================

        [Fact]
        public void FilterNode_DeepClone_ClonesAllProperties()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Contains",
                Value = "test",
                LogicalOperator = "AND"
            };
            original.Children.Add(new FilterNode { Type = NodeType.Condition, Value = "child" });

            var clone = original.DeepClone();

            Assert.Equal(original.Type, clone.Type);
            Assert.Equal(original.Field, clone.Field);
            Assert.Equal(original.Operator, clone.Operator);
            Assert.Equal(original.Value, clone.Value);
            Assert.Equal(original.LogicalOperator, clone.LogicalOperator);
            Assert.Single(clone.Children);
            Assert.NotSame(original.Children[0], clone.Children[0]);
            Assert.Equal("child", clone.Children[0].Value);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RegexOperator_ReturnsCompiled()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            Assert.NotNull(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_NonRegex_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "[bad" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_IsEnabled_DefaultTrue()
        {
            var node = new FilterNode();
            Assert.True(node.IsEnabled);
        }

        [Fact]
        public void FilterNode_SetValue_ClearsCompiledRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var first = node.CompiledRegex;
            Assert.NotNull(first);

            node.Value = @"\w+";
            var second = node.CompiledRegex;
            Assert.NotNull(second);
            // Pattern changed, so regex should be recompiled
            Assert.NotSame(first, second);
        }

        // ===================================================================
        // CsvFormat enum tests
        // ===================================================================

        [Fact]
        public void CsvFormat_HasExpectedValues()
        {
            Assert.Equal(0, (int)CsvFormat.Unknown);
            Assert.Equal(1, (int)CsvFormat.PlcIos);
            Assert.Equal(2, (int)CsvFormat.YTScope);
            Assert.Equal(3, (int)CsvFormat.Legacy);
        }

        // ===================================================================
        // ChartDataService - FindColumnIndex tests (via reflection)
        // ===================================================================

        [Fact]
        public void ChartDataService_FindColumnIndex_ExactMatch()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.AddRange(new[] { "Time", "Temperature", "Pressure" });

            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_PartialMatch()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.AddRange(new[] { "Time", "Station.Temperature.Sensor1" });

            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_CaseInsensitive()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.AddRange(new[] { "time", "TEMPERATURE" });

            Assert.Equal(1, svc.FindColumnIndex("temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_NotFound()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.AddRange(new[] { "Time", "Data" });

            Assert.Equal(-1, svc.FindColumnIndex("NonExistent"));
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_ExactMatch()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.AddRange(new[] { "Time", "Events_Message", "Data" });

            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_InRawColumnNames()
        {
            var svc = new ChartDataService();
            var rawColumnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("RawColumnNames")!.GetValue(svc)!;
            rawColumnNames.AddRange(new[] { "Time", "Some.Events_Message.Data" });

            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_NotFound()
        {
            var svc = new ChartDataService();
            Assert.Equal(-1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_Defaults()
        {
            var svc = new ChartDataService();

            Assert.Equal(0, svc.TotalRows);
            Assert.False(svc.IsLoaded);
            Assert.Null(svc.LoadedFilePath);
            Assert.Empty(svc.ColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.Equal(3, svc.DataStartRow);
            Assert.Equal(CsvFormat.Unknown, svc.DetectedFormat);
        }

        [Fact]
        public void ChartDataService_Dispose_ClearsState()
        {
            var svc = new ChartDataService();
            var columnNames = (List<string>)typeof(ChartDataService)
                .GetProperty("ColumnNames")!.GetValue(svc)!;
            columnNames.Add("Test");

            svc.Dispose();

            Assert.Empty(svc.ColumnNames);
            Assert.Null(svc.LoadedFilePath);
        }

        // ===================================================================
        // PatternChartService - additional tests
        // ===================================================================

        [Fact]
        public void PatternChartService_SearchField_ThreadName()
        {
            var baseTime = new DateTime(2025, 1, 1);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "irrelevant", ThreadName = "Worker_42", Date = baseTime, Level = "INFO" },
                new LogEntry { Message = "irrelevant", ThreadName = "Worker_99", Date = baseTime.AddSeconds(1), Level = "INFO" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"Worker_(\d+)", "Workers", PatternSearchField.ThreadName, false, "Test");

            Assert.Equal(2, package.Signals[0].SparsePoints!.Count);
        }

        [Fact]
        public void PatternChartService_NullLogger_NoException()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "test", Logger = null, Date = DateTime.Now, Level = "INFO" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"(\d+)", "Test", PatternSearchField.Logger, false, "Test");

            Assert.Empty(package.Signals[0].SparsePoints!);
        }

        [Fact]
        public void PatternChartService_IncludeState_S4StateTransitions()
        {
            var baseTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "Temp: 25 C", Date = baseTime, Level = "INFO", ThreadName = "Test" },
                new LogEntry { Message = "==== STATE_STANDBY - Enter ======", Date = baseTime.AddSeconds(1), Level = "INFO", ThreadName = "PLC" },
                new LogEntry { Message = "==== STATE_READY - Enter ======", Date = baseTime.AddSeconds(5), Level = "INFO", ThreadName = "PLC" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"Temp:\s*(\d+)", "Temp", PatternSearchField.Message, true, "Test");

            Assert.NotEmpty(package.States);
        }

        [Fact]
        public void PatternChartService_PackageMetadata()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "Val: 1", Date = DateTime.Now, Level = "INFO", ThreadName = "T" }
            };

            var package = PatternChartService.BuildFromPattern(
                logs, @"Val:\s*(\d+)", "MyChart", PatternSearchField.Message, false, "Session1");

            Assert.Equal("Session1", package.SessionName);
            Assert.Equal("Pattern", package.Signals[0].Category);
            Assert.NotNull(package.Events);
            Assert.NotNull(package.ThreadMessages);
        }

        // ===================================================================
        // IoTerminalDataService - additional tests
        // ===================================================================

        [Fact]
        public void ExtractDeviceName_CaseInsensitive_IoPrefix()
        {
            Assert.Equal("BIM[0]", IoTerminalDataService.ExtractDeviceName("io-BIM[0]_extra.csv"));
        }

        [Fact]
        public void ExtractDeviceName_NoExtension()
        {
            Assert.Equal("DEV", IoTerminalDataService.ExtractDeviceName("Io-DEV_rest"));
        }

        // ===================================================================
        // LogColoringService - EvaluateConditionOptimized (via reflection)
        // ===================================================================

        [Fact]
        public void LogColoringService_PrepareConditions_CompilesCachedRegex()
        {
            var svc = new LogColoringService();
            var prepareMethod = typeof(LogColoringService).GetMethod("PrepareConditions",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var conditions = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Regex", Value = @"\d+" }
            };

            var result = prepareMethod!.Invoke(svc, new object[] { conditions });
            Assert.NotNull(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_ContainsMatch()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Message = "hello world" };

            // Use PreparedCondition struct via reflection
            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            // Set Rule
            var ruleField = preparedType!.GetField("Rule");
            ruleField!.SetValue(prepared, new ColoringCondition { Field = "Message", Operator = "Contains", Value = "hello" });

            // Set FieldLower
            var fieldLowerField = preparedType.GetField("FieldLower");
            fieldLowerField!.SetValue(prepared, "message");

            // Set OpLower
            var opLowerField = preparedType.GetField("OpLower");
            opLowerField!.SetValue(prepared, "contains");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.True(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_EqualsMatch()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Level = "Error" };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "Level", Operator = "Equals", Value = "Error" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "level");
            preparedType.GetField("OpLower")!.SetValue(prepared, "equals");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.True(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_EmptyText_ReturnsFalse()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Message = null };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "x" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "message");
            preparedType.GetField("OpLower")!.SetValue(prepared, "contains");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.False(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_BeginsWithMatch()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Message = "Starting engine" };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "Message", Operator = "Begins With", Value = "Starting" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "message");
            preparedType.GetField("OpLower")!.SetValue(prepared, "begins with");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.True(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_EndsWithMatch()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Logger = "Press.Engine" };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "Logger", Operator = "Ends With", Value = "Engine" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "logger");
            preparedType.GetField("OpLower")!.SetValue(prepared, "ends with");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.True(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_UnknownOp_ReturnsFalse()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry { Message = "test" };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "Message", Operator = "Unknown", Value = "test" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "message");
            preparedType.GetField("OpLower")!.SetValue(prepared, "unknown");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.False(result);
        }

        [Fact]
        public void LogColoringService_EvaluateConditionOptimized_ExtraFieldMatch()
        {
            var svc = new LogColoringService();
            var evalMethod = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var log = new LogEntry
            {
                Message = "test",
                ExtraFields = new Dictionary<string, string> { { "CustomCol", "SomeValue" } }
            };

            var preparedType = typeof(LogColoringService).GetNestedType("PreparedCondition",
                BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(preparedType!);

            preparedType!.GetField("Rule")!.SetValue(prepared,
                new ColoringCondition { Field = "CustomCol", Operator = "Contains", Value = "SomeValue" });
            preparedType.GetField("FieldLower")!.SetValue(prepared, "customcol");
            preparedType.GetField("OpLower")!.SetValue(prepared, "contains");

            bool result = (bool)evalMethod!.Invoke(svc, new object[] { log, prepared! })!;
            Assert.True(result);
        }

        [Fact]
        public void LogColoringService_Contains_Helper()
        {
            var svc = new LogColoringService();
            var containsMethod = typeof(LogColoringService).GetMethod("Contains",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.True((bool)containsMethod!.Invoke(svc, new object[] { "Hello World", "hello" })!);
            Assert.False((bool)containsMethod!.Invoke(svc, new object[] { "Hello World", "xyz" })!);
            Assert.False((bool)containsMethod!.Invoke(svc, new object[] { "", "test" })!);
            Assert.False((bool)containsMethod!.Invoke(svc, new object?[] { null!, "test" })!);
        }

        // ===================================================================
        // ComputeStatistics with timestamps
        // ===================================================================

        [Fact]
        public void LogStatisticsService_ComputeStatistics_TracksTimeSpan()
        {
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 11, 0, 0);

            var plc = new List<LogEntry>
            {
                new LogEntry { Date = t1, Level = "Info", Message = "a" },
                new LogEntry { Date = t2, Level = "Info", Message = "b" }
            };

            var result = LogStatisticsService.ComputeStatistics(plc, new List<LogEntry>());

            Assert.Equal(t1, result.EarliestTimestamp);
            Assert.Equal(t2, result.LatestTimestamp);
        }

        [Fact]
        public void LogStatisticsService_ComputeStatistics_ErrorsBySource()
        {
            var plcErrors = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Error", Message = "err", ThreadName = "Events" },
                new LogEntry { Date = DateTime.Now, Level = "Error", Message = "err", ThreadName = "Events" }
            };

            var result = LogStatisticsService.ComputeStatistics(plcErrors, new List<LogEntry>());

            Assert.NotNull(result.ErrorsBySource);
            Assert.True(result.ErrorsBySource!.Count > 0);
            Assert.Contains(result.ErrorsBySource, s => s.Name.Contains("[PLC]"));
        }

        // ===================================================================
        // CsvExportService - ParseIOMon with DrvTemp
        // ===================================================================

        [Fact]
        public void ParseIOMon_DrvTempSuffix_DecomposesCorrectly()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            svc.ParseIOMon("IO_Mon: SubSys1, Motor1_DrvTemp=75", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("75", dataMatrix[time]["IO_Mon: SubSys1|Motor1|DrvTemp"]);
        }

        // ===================================================================
        // CsvExportService - ParseIO with DrvTemp
        // ===================================================================

        [Fact]
        public void ParseIO_DrvTempSymbol_DecomposesCorrectly()
        {
            var svc = new CsvExportService();
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();
            var time = new DateTime(2025, 3, 7, 10, 0, 0);

            svc.ParseIO("IO: Sub1, Valve_DrvTemp=65", "Thread1", time,
                null, schema, dataMatrix, threadNameMap);

            Assert.Equal("65", dataMatrix[time]["IO_Mon: Sub1|Valve|DrvTemp"]);
        }
    }
}
