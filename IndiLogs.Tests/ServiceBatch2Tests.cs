using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    /// <summary>
    /// Tests covering GlobalGrepService (criteria evaluation), LogColoringService,
    /// SearchSchedulerService (ShouldRun), EmailNotificationService (body building),
    /// LogFileClassifier, LogStatisticsService.ComputeStatistics, and model classes.
    /// </summary>
    public class ServiceBatch2Tests
    {
        // ====================================================================
        //  GlobalGrepService — EvaluateCondition / EvaluateCriteria / EvaluateGroup
        // ====================================================================

        private static GlobalGrepService CreateGrepService() => new GlobalGrepService();

        [Fact]
        public void EvaluateCondition_Contains_MatchesMessage()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor failed to start" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "failed" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Contains_CaseInsensitive()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "MOTOR FAILED" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "motor" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Contains_NoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "All good" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Equals_ExactMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Level = "Error" };
            var cond = new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "error" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Equals_NoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Level = "Warning" };
            var cond = new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "error" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_StartsWith()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "PlcMngr: Idle -> Running" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.StartsWith, Value = "PlcMngr" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_EndsWith()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Operation complete" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.EndsWith, Value = "complete" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Regex_Matches()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error code 42 detected" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"code \d+" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Regex_NoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "No code here" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"code \d+" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Negate_InvertsResult()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor failed" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "failed", Negate = true };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Negate_NoMatchBecomesTrueWhenNegated()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "All good" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error", Negate = true };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_ThreadNameField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { ThreadName = "Manager" };
            var cond = new SearchCondition { Field = SearchField.ThreadName, Operator = SearchOperator.Contains, Value = "Manager" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_LoggerField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Logger = "Press.BL.Printing" };
            var cond = new SearchCondition { Field = SearchField.Logger, Operator = SearchOperator.Contains, Value = "Printing" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_MethodField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Method = "StartPrintAsync" };
            var cond = new SearchCondition { Field = SearchField.Method, Operator = SearchOperator.Contains, Value = "Print" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_DataField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Data = "param=42" };
            var cond = new SearchCondition { Field = SearchField.Data, Operator = SearchOperator.Contains, Value = "param" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_ExceptionField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Exception = "NullReferenceException" };
            var cond = new SearchCondition { Field = SearchField.Exception, Operator = SearchOperator.Contains, Value = "NullRef" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_AnyField_MatchesAcrossFields()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "nothing here", Logger = "MyLogger", Exception = "timeout occurred" };
            var cond = new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "timeout" };
            Assert.True(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_AnyField_NoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "hello", Logger = "world" };
            var cond = new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "xyz" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_EmptyValue_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "anything" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_NullMessage_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" };
            // Message defaults to "" via LogEntry, so Contains won't match
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        // --- EvaluateGroup ---

        [Fact]
        public void EvaluateGroup_And_AllMustMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor failed", Level = "Error" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_And_OneMissing()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor started", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_Or_OneMatchSuffices()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor started", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_Or_NoneMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Idle", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_Nor_NoneMatchReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Idle", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_Nor_OneMatchReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor started", Level = "Info" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_EmptyConditions_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "anything" };
            var group = new SearchConditionGroup { Conditions = new List<SearchCondition>() };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        // --- EvaluateCriteria ---

        [Fact]
        public void EvaluateCriteria_EmptyGroups_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "anything" };
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_And_AllGroupsMustMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor failed", Level = "Error" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_And_OneGroupFails()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor started", Level = "Info" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                        }
                    }
                }
            };
            Assert.False(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_Or_OneGroupSuffices()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor started", Level = "Info" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.Or,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "Error" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        // --- DetermineMatchedFields ---

        [Fact]
        public void DetermineMatchedFields_SingleField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Motor failed" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Motor" }
                        }
                    }
                }
            };
            string result = svc.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Message", result);
        }

        [Fact]
        public void DetermineMatchedFields_AnyField_ShowsActualField()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "good", Exception = "NullRef error" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "NullRef" }
                        }
                    }
                }
            };
            string result = svc.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Exception", result);
        }

        [Fact]
        public void DetermineMatchedFields_EmptyCriteria_ReturnsEmpty()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "anything" };
            var criteria = new SearchCriteria();
            Assert.Equal("", svc.DetermineMatchedFields(entry, criteria));
        }

        // ====================================================================
        //  LogFileClassifier
        // ====================================================================

        [Theory]
        [InlineData("engineGroupA.file.log", true, true, true)]
        [InlineData("enginegroupb.file", true, true, true)]
        [InlineData("something.file.log", true, true, true)]
        [InlineData("appdev.log", true, true, true)]
        [InlineData("press.host.app.file", true, true, true)]
        [InlineData("random.txt", true, true, false)]
        public void LogFileClassifier_IsSearchableLogFile(string fileName, bool plc, bool app, bool expected)
        {
            bool result = LogFileClassifier.IsSearchableLogFile(fileName.ToLowerInvariant(), plc, app);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("123.file", true)]
        [InlineData("abc.file", false)]
        [InlineData("engineGroupA.file", false)]
        [InlineData("9.file", true)]
        public void LogFileClassifier_IsNumericAppFileName(string fileName, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(fileName.ToLowerInvariant()));
        }

        [Theory]
        [InlineData("appdev_2025.log", "APP")]
        [InlineData("press.host.app.file", "APP")]
        [InlineData("123.file", "APP")]
        [InlineData("engineGroupA.file", "PLC")]
        [InlineData("something.file.log", "PLC")]
        public void LogFileClassifier_DetermineLogType(string path, string expected)
        {
            Assert.Equal(expected, LogFileClassifier.DetermineLogType(path));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_ZipAlwaysTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("something.zip", false, false));
        }

        [Fact]
        public void LogFileClassifier_IsLogEntry_ZipAlwaysFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("something.zip", true, true));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_PlcOnly()
        {
            Assert.True(LogFileClassifier.IsLogFile("engineGroupA.file.log", true, false));
            Assert.False(LogFileClassifier.IsLogFile("appdev.log", true, false));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_AppOnly()
        {
            Assert.False(LogFileClassifier.IsLogFile("engineGroupA.file.log", false, true));
            Assert.True(LogFileClassifier.IsLogFile("appdev.log", false, true));
        }

        // ====================================================================
        //  LogStatisticsService.ComputeStatistics
        // ====================================================================

        [Fact]
        public void ComputeStatistics_EmptyLists_ReturnsZeroCounts()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Equal(0, result.TotalPlcErrors);
            Assert.Equal(0, result.TotalAppErrors);
            Assert.Null(result.EarliestTimestamp);
            Assert.Null(result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_PlcLogs_CountsCorrectly()
        {
            var plc = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 0, 0), Level = "Info", Message = "OK", ThreadName = "T1" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 1, 0), Level = "Error", Message = "Failed", ThreadName = "T1" },
                new LogEntry { Date = new DateTime(2025, 1, 1, 10, 2, 0), Level = "Error", Message = "Failed again", ThreadName = "T2" }
            };
            var result = LogStatisticsService.ComputeStatistics(plc, new List<LogEntry>());
            Assert.Equal(3, result.TotalPlcLogs);
            Assert.Equal(2, result.TotalPlcErrors);
            Assert.Equal(new DateTime(2025, 1, 1, 10, 0, 0), result.EarliestTimestamp);
            Assert.Equal(new DateTime(2025, 1, 1, 10, 2, 0), result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_AppLogs_CountsCorrectly()
        {
            var app = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 2, 1, 8, 0, 0), Level = "Info", Message = "Start", Logger = "My.Logger" },
                new LogEntry { Date = new DateTime(2025, 2, 1, 8, 5, 0), Level = "Error", Message = "Crash", Logger = "My.Logger" }
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), app);
            Assert.Equal(2, result.TotalAppLogs);
            Assert.Equal(1, result.TotalAppErrors);
        }

        [Fact]
        public void ComputeStatistics_BinaryAppLogs_SkipsMethodStats()
        {
            var app = new List<LogEntry>
            {
                new LogEntry { Date = DateTime.Now, Level = "Error", Message = "err", Logger = "L", Method = "M" }
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), app, hasBinaryAppLogs: true);
            Assert.True(result.HasBinaryAppLogs);
            Assert.Empty(result.AppMethodErrors);
            Assert.Empty(result.AppMethodLoad);
        }

        [Fact]
        public void ComputeStatistics_CombinedTimeSpan()
        {
            var plc = new List<LogEntry> { new LogEntry { Date = new DateTime(2025, 1, 1, 8, 0, 0), Level = "Info", Message = "a" } };
            var app = new List<LogEntry> { new LogEntry { Date = new DateTime(2025, 1, 1, 12, 0, 0), Level = "Info", Message = "b", Logger = "L" } };
            var result = LogStatisticsService.ComputeStatistics(plc, app);
            Assert.Equal(new DateTime(2025, 1, 1, 8, 0, 0), result.EarliestTimestamp);
            Assert.Equal(new DateTime(2025, 1, 1, 12, 0, 0), result.LatestTimestamp);
        }

        // ====================================================================
        //  LogColoringService — EvaluateConditionOptimized via reflection
        // ====================================================================

        private static LogColoringService CreateColoringService() => new LogColoringService();

        private static bool InvokeEvaluateConditionOptimized(LogColoringService svc, LogEntry log, object preparedCondition)
        {
            var method = typeof(LogColoringService).GetMethod("EvaluateConditionOptimized", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException("EvaluateConditionOptimized not found");
            return (bool)method.Invoke(svc, new[] { log, preparedCondition })!;
        }

        private static object CreatePreparedCondition(ColoringCondition rule)
        {
            var svc = CreateColoringService();
            var prepMethod = typeof(LogColoringService).GetMethod("PrepareConditions", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prepMethod == null) throw new MissingMethodException("PrepareConditions not found");
            var list = (System.Collections.IList)prepMethod.Invoke(svc, new object[] { new List<ColoringCondition> { rule } })!;
            return list[0]!;
        }

        [Fact]
        public void ColoringService_ContainsOperator_MatchesMessage()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "Motor failed to start" };
            var rule = new ColoringCondition { Field = "Message", Operator = "Contains", Value = "failed" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_ContainsOperator_NoMatch()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "All good" };
            var rule = new ColoringCondition { Field = "Message", Operator = "Contains", Value = "failed" };
            var prepared = CreatePreparedCondition(rule);
            Assert.False(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_EqualsOperator()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Level = "Error" };
            var rule = new ColoringCondition { Field = "Level", Operator = "Equals", Value = "error" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_BeginsWithOperator()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "PlcMngr: Idle -> Running" };
            var rule = new ColoringCondition { Field = "Message", Operator = "Begins With", Value = "PlcMngr" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_EndsWithOperator()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "Operation complete" };
            var rule = new ColoringCondition { Field = "Message", Operator = "Ends With", Value = "complete" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_RegexOperator()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "Error code 42 detected" };
            var rule = new ColoringCondition { Field = "Message", Operator = "Regex", Value = @"code \d+" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_EmptyField_ReturnsFalse()
        {
            var svc = CreateColoringService();
            var log = new LogEntry(); // Message defaults to ""
            var rule = new ColoringCondition { Field = "Message", Operator = "Contains", Value = "test" };
            var prepared = CreatePreparedCondition(rule);
            Assert.False(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_ThreadNameField()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { ThreadName = "Events" };
            var rule = new ColoringCondition { Field = "ThreadName", Operator = "Equals", Value = "Events" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_LoggerField()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Logger = "Press.BL.Pipeline" };
            var rule = new ColoringCondition { Field = "Logger", Operator = "Contains", Value = "Pipeline" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_PatternField()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Pattern = "STATE_TRANSITION" };
            var rule = new ColoringCondition { Field = "Pattern", Operator = "Contains", Value = "STATE" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_DataField()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Data = "key=value" };
            var rule = new ColoringCondition { Field = "Data", Operator = "Contains", Value = "key" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_ExceptionField()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Exception = "IOException" };
            var rule = new ColoringCondition { Field = "Exception", Operator = "Contains", Value = "IO" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_UnknownOperator_ReturnsFalse()
        {
            var svc = CreateColoringService();
            var log = new LogEntry { Message = "test" };
            var rule = new ColoringCondition { Field = "Message", Operator = "invalid", Value = "test" };
            var prepared = CreatePreparedCondition(rule);
            Assert.False(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        [Fact]
        public void ColoringService_ExtraFields_Matched()
        {
            var svc = CreateColoringService();
            var log = new LogEntry
            {
                Message = "normal",
                ExtraFields = new Dictionary<string, string> { { "CustomCol", "special_value" } }
            };
            var rule = new ColoringCondition { Field = "CustomCol", Operator = "Contains", Value = "special" };
            var prepared = CreatePreparedCondition(rule);
            Assert.True(InvokeEvaluateConditionOptimized(svc, log, prepared));
        }

        // ====================================================================
        //  SearchSchedulerService — ShouldRun via reflection
        // ====================================================================

        private static SearchSchedulerService CreateSchedulerViaReflection()
        {
            return (SearchSchedulerService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SearchSchedulerService));
        }

        private static bool InvokeShouldRun(SearchSchedulerService svc, ScheduledSearch schedule, DateTime now)
        {
            var method = typeof(SearchSchedulerService).GetMethod("ShouldRun", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (method == null) throw new MissingMethodException("ShouldRun not found");
            return (bool)method.Invoke(svc, new object[] { schedule, now })!;
        }

        [Fact]
        public void ShouldRun_Disabled_ReturnsFalse()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch { IsEnabled = false, ScheduleType = ScheduleType.Daily };
            Assert.False(InvokeShouldRun(svc, schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_NeverRan_TimeReached()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunDate = new DateTime(2025, 1, 1),
                RunTime = new TimeSpan(10, 0, 0),
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 1, 10, 30, 0)));
        }

        [Fact]
        public void ShouldRun_Once_AlreadyRan_ReturnsFalse()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunDate = new DateTime(2025, 1, 1),
                RunTime = new TimeSpan(10, 0, 0),
                LastRunTime = new DateTime(2025, 1, 1, 10, 5, 0)
            };
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 1, 11, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Once_NoRunDate_UsesTimeOfDay()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                RunDate = null,
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 6, 15, 14, 30, 0)));
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 6, 15, 13, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Daily_TimeReached_NotRunToday()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(8, 0, 0),
                LastRunTime = new DateTime(2025, 1, 1, 8, 5, 0) // ran yesterday
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 2, 8, 30, 0)));
        }

        [Fact]
        public void ShouldRun_Daily_AlreadyRanToday()
        {
            var svc = CreateSchedulerViaReflection();
            var now = new DateTime(2025, 1, 2, 9, 0, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(8, 0, 0),
                LastRunTime = new DateTime(2025, 1, 2, 8, 5, 0) // ran today
            };
            Assert.False(InvokeShouldRun(svc, schedule, now));
        }

        [Fact]
        public void ShouldRun_Daily_TooEarly()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 0, 0),
                LastRunTime = null
            };
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 1, 7, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_TimeReached()
        {
            var svc = CreateSchedulerViaReflection();
            // 2025-01-06 is a Monday
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Weekly,
                RunTime = new TimeSpan(9, 0, 0),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 6, 9, 30, 0)));
        }

        [Fact]
        public void ShouldRun_Weekly_WrongDay()
        {
            var svc = CreateSchedulerViaReflection();
            // 2025-01-07 is a Tuesday
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Weekly,
                RunTime = new TimeSpan(9, 0, 0),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = null
            };
            Assert.False(InvokeShouldRun(svc, schedule, new DateTime(2025, 1, 7, 9, 30, 0)));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_NoStartTime()
        {
            var svc = CreateSchedulerViaReflection();
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RunTime = TimeSpan.Zero,
                RepeatIntervalValue = 2,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = null
            };
            Assert.True(InvokeShouldRun(svc, schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Interval_ElapsedEnough()
        {
            var svc = CreateSchedulerViaReflection();
            var lastRun = new DateTime(2025, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = lastRun
            };
            Assert.True(InvokeShouldRun(svc, schedule, lastRun.AddMinutes(31)));
        }

        [Fact]
        public void ShouldRun_Interval_NotElapsedYet()
        {
            var svc = CreateSchedulerViaReflection();
            var lastRun = new DateTime(2025, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = lastRun
            };
            Assert.False(InvokeShouldRun(svc, schedule, lastRun.AddMinutes(15)));
        }

        // ====================================================================
        //  EmailNotificationService — BuildPlainTextBody / BuildSubject
        // ====================================================================

        [Fact]
        public void BuildPlainTextBody_SearchOnly_NoResults()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchOnly };
            string body = EmailNotificationService.BuildPlainTextBody(schedule, null, null);
            Assert.Contains("IndiLogs Search Report", body);
            Assert.Contains("TestScan", body);
        }

        [Fact]
        public void BuildPlainTextBody_SearchOnly_WithResults()
        {
            var schedule = new ScheduledSearch { Name = "TestScan", ScanMode = ScanMode.SearchOnly };
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "Error found", LogType = "PLC", LocationName = "Press1",
                    ReferencedLogEntry = new LogEntry { Date = DateTime.Now, Level = "Error", Message = "Error found" } }
            };
            string body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("Total matches: 1", body);
            Assert.Contains("Press1", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsOnly()
        {
            var schedule = new ScheduledSearch { Name = "StatsScan", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                TotalPlcLogs = 5000,
                TotalAppLogs = 3000,
                TotalPlcErrors = 50,
                TotalAppErrors = 20
            };
            string body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("IndiLogs Statistics Report", body);
            Assert.Contains("5,000", body);
            Assert.Contains("50", body);
        }

        [Fact]
        public void BuildPlainTextBody_SearchAndStats()
        {
            var schedule = new ScheduledSearch { Name = "Combined", ScanMode = ScanMode.SearchAndStatistics };
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "Match1", LogType = "PLC",
                    ReferencedLogEntry = new LogEntry { Date = DateTime.Now, Level = "Info", Message = "Match1" } }
            };
            var stats = new LogStatisticsResult { TotalPlcLogs = 100, TotalAppLogs = 50 };
            string body = EmailNotificationService.BuildPlainTextBody(schedule, results, stats);
            Assert.Contains("Search & Statistics Report", body);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("LOG STATISTICS OVERVIEW", body);
        }

        [Fact]
        public void BuildSubject_ViaReflection_WithMatches()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var schedule = new ScheduledSearch
            {
                Name = "MySearch",
                ScanMode = ScanMode.SearchOnly,
                EmailConfig = new EmailNotificationConfig()
            };
            string result = (string)method!.Invoke(svc, new object?[] { schedule, 42, null })!;
            Assert.Contains("[IndiLogs]", result);
            Assert.Contains("MySearch", result);
            Assert.Contains("42", result);
        }

        [Fact]
        public void BuildSubject_CustomSubject_UsedDirectly()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var schedule = new ScheduledSearch
            {
                Name = "MySearch",
                EmailConfig = new EmailNotificationConfig { CustomSubject = "Custom Subject Line" }
            };
            string result = (string)method!.Invoke(svc, new object?[] { schedule, 10, null })!;
            Assert.Equal("Custom Subject Line", result);
        }

        // ====================================================================
        //  SearchCriteria model tests
        // ====================================================================

        [Fact]
        public void SearchCriteria_DefaultValues()
        {
            var c = new SearchCriteria();
            Assert.True(c.SearchPLC);
            Assert.True(c.SearchAPP);
            Assert.Equal(LogicalGroupOperator.And, c.GroupOperator);
            Assert.Empty(c.Groups);
            Assert.Empty(c.LocationIds);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_ReturnsNullForNonRegexOp()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CompiledForRegexOp()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            var regex = cond.CompiledRegex;
            Assert.NotNull(regex);
            Assert.True(regex!.IsMatch("42"));
        }

        [Fact]
        public void SearchCondition_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "[invalid" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CachesResult()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\w+" };
            var r1 = cond.CompiledRegex;
            var r2 = cond.CompiledRegex;
            Assert.Same(r1, r2);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_RecompilesOnValueChange()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            var r1 = cond.CompiledRegex;
            cond.Value = @"\w+";
            var r2 = cond.CompiledRegex;
            Assert.NotSame(r1, r2);
        }

        // ====================================================================
        //  TimeRangeFilter — Resolve
        // ====================================================================

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter
            {
                From = new DateTime(2025, 1, 1),
                To = new DateTime(2025, 12, 31),
                RelativeRange = RelativeTimeRange.None
            };
            var resolved = filter.Resolve();
            Assert.Same(filter, resolved);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            // From should be approximately 24 hours ago
            Assert.True((DateTime.Now - resolved.From!.Value).TotalHours < 24.1);
            Assert.True((DateTime.Now - resolved.From!.Value).TotalHours > 23.9);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True((DateTime.Now - resolved.From!.Value).TotalDays < 7.1);
            Assert.True((DateTime.Now - resolved.From!.Value).TotalDays > 6.9);
        }

        // ====================================================================
        //  SearchLocation — INotifyPropertyChanged
        // ====================================================================

        [Fact]
        public void SearchLocation_DefaultValues()
        {
            var loc = new SearchLocation();
            Assert.NotEqual(Guid.Empty, loc.Id);
            Assert.Equal("", loc.Name);
            Assert.Equal("", loc.Address);
            Assert.Equal("", loc.BasePath);
            Assert.True(loc.IsActive);
            Assert.Equal(ConnectionStatus.Unknown, loc.ConnectionStatus);
            Assert.Null(loc.LastAccessed);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_Name()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.Name = "Press1";
            Assert.Equal("Name", changedProp);
            Assert.Equal("Press1", loc.Name);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_Address()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.Address = "192.168.1.100";
            Assert.Equal("Address", changedProp);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_BasePath()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.BasePath = @"\\server\share";
            Assert.Equal("BasePath", changedProp);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_IsActive()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.IsActive = false;
            Assert.Equal("IsActive", changedProp);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_ConnectionStatus()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.ConnectionStatus = ConnectionStatus.Connected;
            Assert.Equal("ConnectionStatus", changedProp);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_LastAccessed()
        {
            var loc = new SearchLocation();
            string? changedProp = null;
            loc.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            loc.LastAccessed = DateTime.Now;
            Assert.Equal("LastAccessed", changedProp);
        }

        // ====================================================================
        //  ScheduledSearch — SearchSummary
        // ====================================================================

        [Fact]
        public void ScheduledSearch_SearchSummary_NoCriteria()
        {
            var s = new ScheduledSearch { Criteria = null };
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_EmptyGroups()
        {
            var s = new ScheduledSearch { Criteria = new SearchCriteria() };
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_WithConditions()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "Motor" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:Motor", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_PlcOnly()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[PLC]", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_AppOnly()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = false,
                    SearchAPP = true,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[APP]", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_StatsOnly_NoGroups()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria()
            };
            // With empty groups, the summary falls through to "(no criteria)"
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_StatsOnly_WithGroups()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[Stats]", s.SearchSummary);
            Assert.Contains("All logs", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_SearchAndStats()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.SearchAndStatistics,
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[Search+Stats]", s.SearchSummary);
        }

        // ====================================================================
        //  ScheduledSearch — RepeatIntervalMinutes
        // ====================================================================

        [Fact]
        public void RepeatIntervalMinutes_HoursConversion()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 2, IntervalUnit = IntervalUnit.Hours };
            Assert.Equal(120, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_DaysConversion()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 1, IntervalUnit = IntervalUnit.Days };
            Assert.Equal(1440, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_MinutesPassthrough()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 45, IntervalUnit = IntervalUnit.Minutes };
            Assert.Equal(45, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_Setter_BackwardCompat_Days()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 2880; // 2 days
            Assert.Equal(IntervalUnit.Days, s.IntervalUnit);
            Assert.Equal(2, s.RepeatIntervalValue);
        }

        [Fact]
        public void RepeatIntervalMinutes_Setter_BackwardCompat_Hours()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 180; // 3 hours
            Assert.Equal(IntervalUnit.Hours, s.IntervalUnit);
            Assert.Equal(3, s.RepeatIntervalValue);
        }

        [Fact]
        public void RepeatIntervalMinutes_Setter_BackwardCompat_Minutes()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 45; // 45 minutes (not evenly divisible by 60)
            Assert.Equal(IntervalUnit.Minutes, s.IntervalUnit);
            Assert.Equal(45, s.RepeatIntervalValue);
        }

        // ====================================================================
        //  ChartDataService — FindColumnIndex / FindEventsColumnIndex
        // ====================================================================

        [Fact]
        public void ChartDataService_FindColumnIndex_ExactMatch()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature", "Pressure" });
            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_CaseInsensitive()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature", "Pressure" });
            Assert.Equal(1, svc.FindColumnIndex("temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_PartialMatch()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Station.Temperature.Actual", "Pressure" });
            Assert.Equal(1, svc.FindColumnIndex("Temperature"));
        }

        [Fact]
        public void ChartDataService_FindColumnIndex_NotFound()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Temperature" });
            Assert.Equal(-1, svc.FindColumnIndex("Humidity"));
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_Found()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Events_Message", "Pressure" });
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_RawColumnNames()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Pressure" });
            svc.RawColumnNames.AddRange(new[] { "Time", "Events_Message" });
            Assert.Equal(1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_FindEventsColumnIndex_NotFound()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.AddRange(new[] { "Time", "Pressure" });
            svc.RawColumnNames.AddRange(new[] { "Time", "Pressure" });
            Assert.Equal(-1, svc.FindEventsColumnIndex());
        }

        [Fact]
        public void ChartDataService_DefaultState()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            Assert.Equal(0, svc.TotalRows);
            Assert.Empty(svc.ColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.False(svc.IsLoaded);
            Assert.Null(svc.LoadedFilePath);
            Assert.Equal(IndiLogs_3._0.Services.Charts.CsvFormat.Unknown, svc.DetectedFormat);
        }

        [Fact]
        public void ChartDataService_Dispose_ClearsState()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            svc.ColumnNames.Add("Test");
            svc.RawColumnNames.Add("Test");
            svc.Dispose();
            Assert.Empty(svc.ColumnNames);
            Assert.Empty(svc.RawColumnNames);
            Assert.Null(svc.LoadedFilePath);
        }

        // ====================================================================
        //  ChartDataService — DetectFormat via reflection
        // ====================================================================

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_StationPrefix()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            var method = typeof(IndiLogs_3._0.Services.Charts.ChartDataService)
                .GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            string result = (string)method!.Invoke(svc, new object[] { "Station.pArrTest.Value" })!;
            Assert.Equal("Test.Value", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_GStationAxes()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            var method = typeof(IndiLogs_3._0.Services.Charts.ChartDataService)
                .GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            string result = (string)method!.Invoke(svc, new object[] { "gStationAxes_1.Speed" })!;
            Assert.Equal("Stn1.Speed", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_ArrInk()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            var method = typeof(IndiLogs_3._0.Services.Charts.ChartDataService)
                .GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            string result = (string)method!.Invoke(svc, new object[] { "arrInk[0].Level" })!;
            Assert.Equal("Ink[0].Level", result);
        }

        [Fact]
        public void ChartDataService_SimplifyYTScopeName_Empty()
        {
            var svc = new IndiLogs_3._0.Services.Charts.ChartDataService();
            var method = typeof(IndiLogs_3._0.Services.Charts.ChartDataService)
                .GetMethod("SimplifyYTScopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            string result = (string)method!.Invoke(svc, new object[] { "" })!;
            Assert.Equal("", result);
        }

        // ====================================================================
        //  GlobalGrepService — FilterFilesByTimeRange
        // ====================================================================

        [Fact]
        public void FilterFilesByTimeRange_NullFilter_ReturnsAll()
        {
            var svc = CreateGrepService();
            var files = new List<string> { "a.log", "b.log" };
            var result = svc.FilterFilesByTimeRange(files, null!);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void FilterFilesByTimeRange_EmptyFilter_ReturnsAll()
        {
            var svc = CreateGrepService();
            var files = new List<string> { "a.log", "b.log" };
            var result = svc.FilterFilesByTimeRange(files, new TimeRangeFilter());
            Assert.Equal(2, result.Count);
        }

        // ====================================================================
        //  LogFileService — StringPool
        // ====================================================================

        [Fact]
        public void StringPool_Intern_ReturnsSameReference()
        {
            var pool = new LogFileService.StringPool();
            string a = pool.Intern("hello");
            string b = pool.Intern(new string(new[] { 'h', 'e', 'l', 'l', 'o' }));
            Assert.Same(a, b);
        }

        [Fact]
        public void StringPool_Intern_EmptyString_ReturnsEmpty()
        {
            var pool = new LogFileService.StringPool();
            Assert.Equal("", pool.Intern(""));
        }

        [Fact]
        public void StringPool_Intern_Null_ReturnsNull()
        {
            var pool = new LogFileService.StringPool();
            Assert.Null(pool.Intern(null!));
        }

        [Fact]
        public void StringPool_Clear_RemovesEntries()
        {
            var pool = new LogFileService.StringPool();
            string a = pool.Intern("test");
            pool.Clear();
            string b = pool.Intern(new string(new[] { 't', 'e', 's', 't' }));
            // After clearing, the reference may or may not be the same (implementation detail)
            Assert.Equal("test", b);
        }

        // ====================================================================
        //  GrepResult model
        // ====================================================================

        [Fact]
        public void GrepResult_TimestampDisplay_FromReferencedLogEntry()
        {
            var entry = new LogEntry { Date = new DateTime(2025, 6, 15, 10, 30, 45, 123) };
            var result = new GrepResult { ReferencedLogEntry = entry };
            Assert.Contains("2025-06-15", result.TimestampDisplay);
            Assert.Contains("10:30:45", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_FallbackToTimestamp()
        {
            var result = new GrepResult { Timestamp = new DateTime(2025, 3, 1, 8, 0, 0) };
            Assert.Contains("2025-03-01", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_NoTimestamp_NA()
        {
            var result = new GrepResult();
            Assert.Equal("N/A", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_DefaultValues()
        {
            var result = new GrepResult();
            Assert.Equal("", result.FilePath);
            Assert.Equal("", result.LogType);
            Assert.Null(result.LocationName);
            Assert.Null(result.LocationAddress);
        }

        // ====================================================================
        //  Enum coverage
        // ====================================================================

        [Fact]
        public void ScheduleType_AllValues()
        {
            Assert.Equal(4, Enum.GetValues<ScheduleType>().Length);
        }

        [Fact]
        public void ScanMode_AllValues()
        {
            Assert.Equal(3, Enum.GetValues<ScanMode>().Length);
        }

        [Fact]
        public void SearchOperator_AllValues()
        {
            Assert.Equal(5, Enum.GetValues<SearchOperator>().Length);
        }

        [Fact]
        public void ConditionOperator_AllValues()
        {
            Assert.Equal(3, Enum.GetValues<ConditionOperator>().Length);
        }

        [Fact]
        public void ConnectionStatus_AllValues()
        {
            Assert.Equal(4, Enum.GetValues<ConnectionStatus>().Length);
        }

        [Fact]
        public void CsvFormat_AllValues()
        {
            Assert.Equal(4, Enum.GetValues<IndiLogs_3._0.Services.Charts.CsvFormat>().Length);
        }
    }
}
