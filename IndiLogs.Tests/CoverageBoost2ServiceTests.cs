using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Views;
using Xunit;

namespace IndiLogs.Tests
{
    public class CoverageBoost2ServiceTests
    {
        // ====================================================================
        //  Helpers
        // ====================================================================
        private static GlobalGrepService CreateGrepService()
            => (GlobalGrepService)RuntimeHelpers.GetUninitializedObject(typeof(GlobalGrepService));

        private static LogEntry MakeEntry(
            string? message = "test msg",
            string? level = "ERROR",
            string? thread = "Main",
            string? logger = "Some.Logger",
            string? method = "DoWork",
            string? data = null,
            string? exception = null,
            DateTime? date = null)
        {
            return new LogEntry
            {
                Message = message ?? "",
                Level = level ?? "INFO",
                ThreadName = thread ?? "",
                Logger = logger ?? "",
                Method = method ?? "",
                Data = data ?? "",
                Exception = exception ?? "",
                Date = date ?? DateTime.Now
            };
        }

        // ====================================================================
        //  1. GlobalGrepService.MultiLocationHelpers — EvaluateCriteria
        // ====================================================================

        [Fact]
        public void EvaluateCriteria_NullGroups_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria { Groups = null! };
            Assert.True(svc.EvaluateCriteria(MakeEntry(), criteria));
        }

        [Fact]
        public void EvaluateCriteria_EmptyGroups_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria();
            Assert.True(svc.EvaluateCriteria(MakeEntry(), criteria));
        }

        [Fact]
        public void EvaluateCriteria_AndOperator_AllGroupsMustMatch()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.And,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.And,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "ERROR" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(MakeEntry(message: "test msg", level: "ERROR"), criteria));
            Assert.False(svc.EvaluateCriteria(MakeEntry(message: "test msg", level: "INFO"), criteria));
        }

        [Fact]
        public void EvaluateCriteria_OrOperator_AnyGroupCanMatch()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.Or,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "NOTFOUND" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "ERROR" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(MakeEntry(level: "ERROR"), criteria));
        }

        // ====================================================================
        //  2. EvaluateGroup — And/Or/Nor
        // ====================================================================

        [Fact]
        public void EvaluateGroup_NullConditions_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var group = new SearchConditionGroup { Conditions = null! };
            Assert.True(svc.EvaluateGroup(MakeEntry(), group));
        }

        [Fact]
        public void EvaluateGroup_EmptyConditions_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var group = new SearchConditionGroup();
            Assert.True(svc.EvaluateGroup(MakeEntry(), group));
        }

        [Fact]
        public void EvaluateGroup_AndOperator_AllMustMatch()
        {
            var svc = CreateGrepService();
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "ERROR" }
                }
            };
            Assert.True(svc.EvaluateGroup(MakeEntry(message: "test", level: "ERROR"), group));
            Assert.False(svc.EvaluateGroup(MakeEntry(message: "test", level: "INFO"), group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_AnyCanMatch()
        {
            var svc = CreateGrepService();
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "NOTFOUND" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Contains, Value = "ERROR" }
                }
            };
            Assert.True(svc.EvaluateGroup(MakeEntry(level: "ERROR"), group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_NoneCanMatch()
        {
            var svc = CreateGrepService();
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "NOTFOUND" }
                }
            };
            Assert.True(svc.EvaluateGroup(MakeEntry(message: "hello"), group));
            Assert.False(svc.EvaluateGroup(MakeEntry(message: "NOTFOUND here"), group));
        }

        // ====================================================================
        //  3. EvaluateCondition — field types & operators
        // ====================================================================

        [Theory]
        [InlineData(SearchField.Message, "test", true)]
        [InlineData(SearchField.Level, "ERROR", true)]
        [InlineData(SearchField.ThreadName, "Main", true)]
        [InlineData(SearchField.Logger, "Some.Logger", true)]
        [InlineData(SearchField.Method, "DoWork", true)]
        [InlineData(SearchField.Data, "dataval", true)]
        [InlineData(SearchField.Exception, "excval", true)]
        [InlineData(SearchField.Any, "test", true)]
        [InlineData(SearchField.Message, "MISSING", false)]
        public void EvaluateCondition_FieldContains(SearchField field, string value, bool expected)
        {
            var svc = CreateGrepService();
            var entry = MakeEntry(message: "test msg", level: "ERROR", thread: "Main",
                logger: "Some.Logger", method: "DoWork", data: "dataval", exception: "excval");
            var cond = new SearchCondition { Field = field, Operator = SearchOperator.Contains, Value = value };
            Assert.Equal(expected, svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Negate_InvertsResult()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test", Negate = true };
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "test msg"), cond));
            Assert.True(svc.EvaluateCondition(MakeEntry(message: "hello"), cond));
        }

        [Fact]
        public void EvaluateCondition_EqualsOperator()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "ERROR" };
            Assert.True(svc.EvaluateCondition(MakeEntry(level: "ERROR"), cond));
            Assert.False(svc.EvaluateCondition(MakeEntry(level: "ERROR2"), cond));
        }

        [Fact]
        public void EvaluateCondition_StartsWithOperator()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.StartsWith, Value = "hel" };
            Assert.True(svc.EvaluateCondition(MakeEntry(message: "hello world"), cond));
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "world hello"), cond));
        }

        [Fact]
        public void EvaluateCondition_EndsWithOperator()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.EndsWith, Value = "world" };
            Assert.True(svc.EvaluateCondition(MakeEntry(message: "hello world"), cond));
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "world hello"), cond));
        }

        [Fact]
        public void EvaluateCondition_RegexOperator()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"hel+o" };
            Assert.True(svc.EvaluateCondition(MakeEntry(message: "helllo world"), cond));
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "xyz"), cond));
        }

        [Fact]
        public void EvaluateCondition_RegexOperator_InvalidPattern_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"[invalid" };
            // CompiledRegex will be null due to bad pattern; falls through to Regex.IsMatch which will fail
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "test"), cond));
        }

        [Fact]
        public void EvaluateCondition_EmptyValue_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "" };
            Assert.False(svc.EvaluateCondition(MakeEntry(message: "test"), cond));
        }

        [Fact]
        public void EvaluateCondition_EmptyText_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" };
            Assert.False(svc.EvaluateCondition(MakeEntry(message: ""), cond));
        }

        // ====================================================================
        //  4. DetermineMatchedFields
        // ====================================================================

        [Fact]
        public void DetermineMatchedFields_NullGroups_ReturnsEmpty()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria { Groups = null! };
            Assert.Equal("", svc.DetermineMatchedFields(MakeEntry(), criteria));
        }

        [Fact]
        public void DetermineMatchedFields_EmptyGroups_ReturnsEmpty()
        {
            var svc = CreateGrepService();
            Assert.Equal("", svc.DetermineMatchedFields(MakeEntry(), new SearchCriteria()));
        }

        [Fact]
        public void DetermineMatchedFields_SpecificField_ReturnsFieldName()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" }
                        }
                    }
                }
            };
            var result = svc.DetermineMatchedFields(MakeEntry(message: "test msg"), criteria);
            Assert.Contains("Message", result);
        }

        [Fact]
        public void DetermineMatchedFields_AnyField_ReturnsAllMatchingFields()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "shared" }
                        }
                    }
                }
            };
            var entry = MakeEntry(message: "shared text", data: "shared data");
            var result = svc.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Message", result);
            Assert.Contains("Data", result);
        }

        [Fact]
        public void DetermineMatchedFields_WhitespaceValue_Skipped()
        {
            var svc = CreateGrepService();
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "  " }
                        }
                    }
                }
            };
            Assert.Equal("", svc.DetermineMatchedFields(MakeEntry(), criteria));
        }

        // ====================================================================
        //  5. LogStatisticsService.Computation — TopN
        // ====================================================================

        [Fact]
        public void TopN_EmptyDict_ReturnsEmpty()
        {
            var result = LogStatisticsService.TopN(new Dictionary<string, int>(), 5);
            Assert.Empty(result);
        }

        [Fact]
        public void TopN_FewerThanN_ReturnsSortedDesc()
        {
            var dict = new Dictionary<string, int> { { "a", 3 }, { "b", 10 }, { "c", 1 } };
            var result = LogStatisticsService.TopN(dict, 5);
            Assert.Equal(3, result.Count);
            Assert.Equal("b", result[0].Key);
            Assert.Equal("a", result[1].Key);
            Assert.Equal("c", result[2].Key);
        }

        [Fact]
        public void TopN_ExactlyN_ReturnsSortedDesc()
        {
            var dict = new Dictionary<string, int> { { "a", 3 }, { "b", 10 }, { "c", 1 } };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, result.Count);
            Assert.Equal("b", result[0].Key);
        }

        [Fact]
        public void TopN_MoreThanN_ReturnsTopItems()
        {
            var dict = new Dictionary<string, int>
            {
                { "a", 1 }, { "b", 5 }, { "c", 3 }, { "d", 10 }, { "e", 7 }, { "f", 2 }
            };
            var result = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, result.Count);
            Assert.Equal(10, result[0].Value);
            Assert.Equal(7, result[1].Value);
            Assert.Equal(5, result[2].Value);
        }

        // ====================================================================
        //  6. LogStatisticsService — GetErrorLogs
        // ====================================================================

        [Fact]
        public void GetErrorLogs_FiltersOnlyErrors()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(level: "ERROR"),
                MakeEntry(level: "INFO"),
                MakeEntry(level: "WARN"),
                MakeEntry(level: "FATAL"),
                MakeEntry(level: "DEBUG"),
            };
            var result = LogStatisticsService.GetErrorLogs(logs);
            Assert.Equal(2, result.Count); // ERROR and FATAL
        }

        [Fact]
        public void GetErrorLogs_NullLevel_Skipped()
        {
            var logs = new List<LogEntry> { new LogEntry { Level = null! } };
            var result = LogStatisticsService.GetErrorLogs(logs);
            Assert.Empty(result);
        }

        // ====================================================================
        //  7. LogStatisticsService — FindGaps
        // ====================================================================

        [Fact]
        public void FindGaps_NoLogs_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry>()));
        }

        [Fact]
        public void FindGaps_SingleLog_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry> { MakeEntry() }));
        }

        [Fact]
        public void FindGaps_SmallGap_NotDetected()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                MakeEntry(date: now),
                MakeEntry(date: now.AddSeconds(1))
            };
            Assert.Empty(LogStatisticsService.FindGaps(logs));
        }

        [Fact]
        public void FindGaps_LargeGap_Detected()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                MakeEntry(date: now, message: "before gap"),
                MakeEntry(date: now.AddSeconds(5))
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Single(gaps);
            Assert.Equal(1, gaps[0].Index);
            Assert.Contains("before gap", gaps[0].LastMessageBeforeGap);
        }

        [Fact]
        public void FindGaps_MultipleGaps()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                MakeEntry(date: now),
                MakeEntry(date: now.AddSeconds(3)),
                MakeEntry(date: now.AddSeconds(4)),
                MakeEntry(date: now.AddSeconds(10))
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Equal(2, gaps.Count);
        }

        // ====================================================================
        //  8. LogStatisticsService — FormatDuration
        // ====================================================================

        [Fact]
        public void FormatDuration_Seconds()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromSeconds(30));
            Assert.Contains("sec", result);
        }

        [Fact]
        public void FormatDuration_Minutes()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromMinutes(2.5));
            Assert.Contains("min", result);
        }

        // ====================================================================
        //  9. LogStatisticsService — TruncateMessage
        // ====================================================================

        [Fact]
        public void TruncateMessage_Null_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 100));
        }

        [Fact]
        public void TruncateMessage_Empty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 100));
        }

        [Fact]
        public void TruncateMessage_Short_ReturnsOriginal()
        {
            Assert.Equal("hello", LogStatisticsService.TruncateMessage("hello", 100));
        }

        [Fact]
        public void TruncateMessage_Long_Truncated()
        {
            string long_msg = new string('x', 200);
            var result = LogStatisticsService.TruncateMessage(long_msg, 50);
            Assert.Equal(53, result.Length); // 50 + "..."
            Assert.EndsWith("...", result);
        }

        // ====================================================================
        //  10. LogStatisticsService — GetShortLoggerName
        // ====================================================================

        [Fact]
        public void GetShortLoggerName_Null_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
        }

        [Fact]
        public void GetShortLoggerName_Empty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        [Fact]
        public void GetShortLoggerName_Short_ReturnsFull()
        {
            Assert.Equal("MyLogger", LogStatisticsService.GetShortLoggerName("MyLogger"));
        }

        [Fact]
        public void GetShortLoggerName_TwoParts_ReturnsFull()
        {
            Assert.Equal("Some.Logger", LogStatisticsService.GetShortLoggerName("Some.Logger"));
        }

        [Fact]
        public void GetShortLoggerName_ThreeOrMoreParts_ReturnsLastTwo()
        {
            Assert.Equal("Inner.Logger", LogStatisticsService.GetShortLoggerName("Outer.Inner.Logger"));
        }

        [Fact]
        public void GetShortLoggerName_FourParts_ReturnsLastTwo()
        {
            Assert.Equal("C.D", LogStatisticsService.GetShortLoggerName("A.B.C.D"));
        }

        // ====================================================================
        //  11. LogStatisticsService — CalculateErrorHistogram
        // ====================================================================

        [Fact]
        public void CalculateErrorHistogram_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 10));
        }

        [Fact]
        public void CalculateErrorHistogram_GroupsByMessage()
        {
            var errors = new List<LogEntry>
            {
                MakeEntry(message: "error A"),
                MakeEntry(message: "error A"),
                MakeEntry(message: "error B"),
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void CalculateErrorHistogram_CustomKeySelector()
        {
            var errors = new List<LogEntry>
            {
                MakeEntry(logger: "A.B.C"),
                MakeEntry(logger: "A.B.C"),
                MakeEntry(logger: "X.Y"),
            };
            var result = LogStatisticsService.CalculateErrorHistogram(errors, 10, l => l.Logger);
            Assert.Equal(2, result.Count);
        }

        // ====================================================================
        //  12. LogStatisticsService — CalculateLoadDistribution
        // ====================================================================

        [Fact]
        public void CalculateLoadDistribution_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateLoadDistribution(new List<LogEntry>(), l => l.ThreadName, 10));
        }

        [Fact]
        public void CalculateLoadDistribution_GroupsByThread()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(thread: "T1"),
                MakeEntry(thread: "T1"),
                MakeEntry(thread: "T2"),
            };
            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);
            Assert.Equal(2, result.Count);
            Assert.Equal("T1", result[0].Name);
            Assert.True(result[0].Percentage > 60);
        }

        [Fact]
        public void CalculateLoadDistribution_EmptyKey_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(thread: ""),
                MakeEntry(thread: "T1"),
            };
            var result = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 10);
            Assert.Single(result);
        }

        [Fact]
        public void CalculateLoadDistribution_WithFullNameSelector()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry(logger: "A.B.C"),
                MakeEntry(logger: "A.B.C"),
            };
            var result = LogStatisticsService.CalculateLoadDistribution(
                logs, l => LogStatisticsService.GetShortLoggerName(l.Logger), 10, l => l.Logger);
            Assert.Single(result);
            Assert.Equal("A.B.C", result[0].FullName);
        }

        // ====================================================================
        //  13. LogStatisticsService — CalculateStateEntries (S6 path)
        // ====================================================================

        [Fact]
        public void CalculateStateEntries_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        [Fact]
        public void CalculateStateEntries_S6_PlcMngrTransitions()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                MakeEntry(thread: "Manager", message: "PlcMngr: OFF -> GET_READY", date: now),
                MakeEntry(thread: "Manager", message: "PlcMngr: GET_READY -> RUNNING", date: now.AddSeconds(10)),
                MakeEntry(thread: "Worker", message: "some other log", date: now.AddSeconds(15)),
            };
            var states = LogStatisticsService.CalculateStateEntries(plcLogs);
            Assert.True(states.Count >= 2); // initial OFF + GET_READY + RUNNING
        }

        [Fact]
        public void CalculateStateEntries_S45_StateEnter()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                MakeEntry(thread: "Worker", message: "==== STATE_IDLE - Enter ======", date: now),
                MakeEntry(thread: "Worker", message: "==== STATE_RUNNING - Enter ======", date: now.AddSeconds(5)),
            };
            var states = LogStatisticsService.CalculateStateEntries(plcLogs);
            Assert.Equal(2, states.Count);
            Assert.Equal("IDLE", states[0].StateName);
            Assert.Equal("RUNNING", states[1].StateName);
        }

        // ====================================================================
        //  14. LogStatisticsService — MapErrorsToStates
        // ====================================================================

        [Fact]
        public void MapErrorsToStates_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), new List<StateEntry>()));
        }

        [Fact]
        public void MapErrorsToStates_MapsCorrectly()
        {
            var now = DateTime.Now;
            var states = new List<StateEntry>
            {
                new StateEntry { StateName = "IDLE", StartTime = now, EndTime = now.AddSeconds(10) },
                new StateEntry { StateName = "RUNNING", StartTime = now.AddSeconds(10), EndTime = now.AddSeconds(20) },
            };
            var errors = new List<LogEntry>
            {
                MakeEntry(date: now.AddSeconds(3)),
                MakeEntry(date: now.AddSeconds(5)),
                MakeEntry(date: now.AddSeconds(12)),
            };
            var result = LogStatisticsService.MapErrorsToStates(errors, states);
            Assert.Equal(2, result.Count);
            var idle = result.First(r => r.Name == "IDLE");
            Assert.Equal(2, idle.Count);
            var running = result.First(r => r.Name == "RUNNING");
            Assert.Equal(1, running.Count);
        }

        // ====================================================================
        //  15. LogStatisticsService — ComputeStatistics
        // ====================================================================

        [Fact]
        public void ComputeStatistics_Empty_ReturnsZeroCounts()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Null(result.EarliestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_WithPlcLogs_ComputesStats()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                MakeEntry(level: "INFO", thread: "T1", date: now),
                MakeEntry(level: "ERROR", thread: "T1", message: "err1", date: now.AddSeconds(1)),
                MakeEntry(level: "ERROR", thread: "T2", message: "err2", date: now.AddSeconds(2)),
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());
            Assert.Equal(3, result.TotalPlcLogs);
            Assert.Equal(2, result.TotalPlcErrors);
            Assert.NotNull(result.EarliestTimestamp);
            Assert.NotNull(result.LatestTimestamp);
            Assert.NotEmpty(result.PlcTopErrors);
            Assert.NotEmpty(result.PlcThreadLoad);
        }

        [Fact]
        public void ComputeStatistics_WithAppLogs_TextMode()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                MakeEntry(level: "INFO", logger: "A.B.C", method: "Method1", date: now),
                MakeEntry(level: "ERROR", logger: "A.B.C", method: "Method1", date: now.AddSeconds(1)),
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs, hasBinaryAppLogs: false);
            Assert.Equal(2, result.TotalAppLogs);
            Assert.NotEmpty(result.AppLoggerLoad);
            Assert.NotEmpty(result.AppMethodLoad);
        }

        [Fact]
        public void ComputeStatistics_WithAppLogs_BinaryMode_NoMethodStats()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                MakeEntry(level: "ERROR", logger: "X.Y", date: now),
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs, hasBinaryAppLogs: true);
            Assert.True(result.HasBinaryAppLogs);
            Assert.Empty(result.AppMethodErrors);
            Assert.Empty(result.AppMethodLoad);
        }

        // ====================================================================
        //  16. LogStatisticsService.Computation — BuildErrorsBySource (via reflection)
        // ====================================================================

        [Fact]
        public void BuildErrorsBySource_ViaReflection()
        {
            var method = typeof(LogStatisticsService).GetMethod("BuildErrorsBySource",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var plcErrors = new List<LogEntry> { MakeEntry(thread: "Manager"), MakeEntry(thread: "Manager") };
            var appErrors = new List<LogEntry> { MakeEntry(logger: "A.B") };
            var result = (List<StatCount>)method!.Invoke(null, new object[] { plcErrors, appErrors })!;
            Assert.True(result.Count >= 2);
        }

        // ====================================================================
        //  17. SearchSchedulerService — ShouldRun (all schedule types)
        // ====================================================================

        private static SearchSchedulerService CreateScheduler()
        {
            return (SearchSchedulerService)RuntimeHelpers.GetUninitializedObject(typeof(SearchSchedulerService));
        }

        [Fact]
        public void ShouldRun_Disabled_ReturnsFalse()
        {
            var svc = CreateScheduler();
            var schedule = new ScheduledSearch { IsEnabled = false };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_AlreadyRan_ReturnsFalse()
        {
            var svc = CreateScheduler();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                LastRunTime = DateTime.Now.AddHours(-1)
            };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_WithRunDate_TargetReached()
        {
            var svc = CreateScheduler();
            var past = DateTime.Now.AddHours(-1);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunDate = past.Date,
                RunTime = past.TimeOfDay,
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_WithRunDate_NotYet()
        {
            var svc = CreateScheduler();
            var future = DateTime.Now.AddHours(2);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunDate = future.Date,
                RunTime = future.TimeOfDay,
                LastRunTime = null
            };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_NoRunDate_TimeReached()
        {
            var svc = CreateScheduler();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunTime = TimeSpan.FromHours(0),
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Daily_BeforeRunTime_ReturnsFalse()
        {
            var svc = CreateScheduler();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(23)
            };
            var now = DateTime.Today.AddHours(10);
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Daily_AlreadyRanToday_ReturnsFalse()
        {
            var svc = CreateScheduler();
            var now = DateTime.Today.AddHours(15);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(10),
                LastRunTime = now.AddHours(-2) // ran today
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Daily_NeverRan_TimeReached_ReturnsTrue()
        {
            var svc = CreateScheduler();
            var now = DateTime.Today.AddHours(15);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(10),
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_WrongDay_ReturnsFalse()
        {
            var svc = CreateScheduler();
            var now = new DateTime(2026, 3, 8, 15, 0, 0); // Sunday
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                RunTime = TimeSpan.FromHours(10)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_RightDay_TimeReached()
        {
            var svc = CreateScheduler();
            var now = new DateTime(2026, 3, 9, 15, 0, 0); // Monday
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                RunTime = TimeSpan.FromHours(10),
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_RightDay_BeforeTime()
        {
            var svc = CreateScheduler();
            var now = new DateTime(2026, 3, 9, 8, 0, 0); // Monday 8am
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                RunTime = TimeSpan.FromHours(10)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_AlreadyRanToday()
        {
            var svc = CreateScheduler();
            var now = new DateTime(2026, 3, 9, 15, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                RunTime = TimeSpan.FromHours(10),
                LastRunTime = now.AddHours(-1)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_NoStartTime()
        {
            var svc = CreateScheduler();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RunTime = TimeSpan.Zero,
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_BeforeStartTime()
        {
            var svc = CreateScheduler();
            var now = DateTime.Today.AddHours(8);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RunTime = TimeSpan.FromHours(20),
                LastRunTime = null
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_ElapsedEnough()
        {
            var svc = CreateScheduler();
            var now = DateTime.Now;
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = now.AddHours(-2)
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_NotElapsed()
        {
            var svc = CreateScheduler();
            var now = DateTime.Now;
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 2,
                IntervalUnit = IntervalUnit.Hours,
                LastRunTime = now.AddMinutes(-30)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        // ====================================================================
        //  18. SearchSchedulerService.Execution — Escape (private static)
        // ====================================================================

        [Fact]
        public void Escape_NullOrEmpty_ReturnsEmpty()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.Equal("", method!.Invoke(null, new object?[] { null }));
            Assert.Equal("", method.Invoke(null, new object[] { "" }));
        }

        [Fact]
        public void Escape_NoSpecialChars_ReturnsAsIs()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal("hello", method.Invoke(null, new object[] { "hello" }));
        }

        [Fact]
        public void Escape_WithComma_QuotesField()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (string)method.Invoke(null, new object[] { "a,b" })!;
            Assert.StartsWith("\"", result);
            Assert.EndsWith("\"", result);
        }

        [Fact]
        public void Escape_WithQuote_DoublesQuotes()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (string)method.Invoke(null, new object[] { "say \"hello\"" })!;
            Assert.Contains("\"\"", result);
        }

        [Fact]
        public void Escape_WithNewline_QuotesField()
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (string)method.Invoke(null, new object[] { "line1\nline2" })!;
            Assert.StartsWith("\"", result);
        }

        // ====================================================================
        //  19. SearchSchedulerService.Execution — WriteCsv (via reflection)
        // ====================================================================

        [Fact]
        public void WriteCsv_WritesHeaderAndRows()
        {
            var svc = CreateScheduler();
            var method = typeof(SearchSchedulerService).GetMethod("WriteCsv",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var results = new List<GrepResult>
            {
                new GrepResult
                {
                    Timestamp = DateTime.Now,
                    LocationName = "Loc1",
                    LocationAddress = "addr",
                    FilePath = "file.log",
                    LineNumber = 42,
                    LogType = "PLC",
                    MatchedField = "Message",
                    PreviewText = "error text"
                }
            };

            string tmpPath = Path.GetTempFileName();
            try
            {
                method.Invoke(svc, new object[] { tmpPath, results });
                var lines = File.ReadAllLines(tmpPath);
                Assert.True(lines.Length >= 2);
                Assert.Contains("Timestamp", lines[0]);
                Assert.Contains("error text", lines[1]);
            }
            finally
            {
                File.Delete(tmpPath);
            }
        }

        // ====================================================================
        //  20. EmailNotificationService.BodyBuilding — BuildPlainTextBody
        // ====================================================================

        [Fact]
        public void BuildPlainTextBody_SearchOnly_NoResults()
        {
            var schedule = new ScheduledSearch { Name = "Test", ScanMode = ScanMode.SearchOnly };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, null);
            Assert.Contains("Search Report", body);
            Assert.Contains("Test", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithResults()
        {
            var schedule = new ScheduledSearch { Name = "Test", ScanMode = ScanMode.SearchOnly };
            var results = new List<GrepResult>
            {
                new GrepResult { LocationName = "Loc1", PreviewText = "match text",
                    ReferencedLogEntry = MakeEntry(level: "ERROR") }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("Loc1", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithStats()
        {
            var schedule = new ScheduledSearch { Name = "Test", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                TotalPlcLogs = 100,
                TotalAppLogs = 50,
                TotalPlcErrors = 5,
                TotalAppErrors = 3,
                EarliestTimestamp = DateTime.Now.AddHours(-1),
                LatestTimestamp = DateTime.Now,
                PlcTopErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "err", Message = "err msg", Count = 5 }
                },
                PlcThreadLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "T1", Count = 80, Percentage = 80 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("STATISTICS", body);
            Assert.Contains("100", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithGaps()
        {
            var now = DateTime.Now;
            var schedule = new ScheduledSearch { Name = "Test", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                PlcGaps = new List<GapInfo>
                {
                    new GapInfo { StartTime = now, EndTime = now.AddSeconds(5), DurationText = "5.0 sec" }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("GAP ANALYSIS", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppLoggerErrors()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                AppLoggerErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "L", Message = "msg", Count = 2 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP ERRORS BY LOGGER", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppLoggerLoad()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                AppLoggerLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "L", Count = 10, Percentage = 50 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP LOGGER LOAD", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppMethodErrors()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                AppMethodErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "M", Message = "msg", Count = 1 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP ERRORS BY METHOD", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppMethodLoad()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                AppMethodLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "M", Count = 5, Percentage = 25 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP METHOD LOAD", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithErrorsBySource()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                ErrorsBySource = new List<StatCount>
                {
                    new StatCount { Name = "[PLC] Manager", Count = 3 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("ERRORS BY SOURCE", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithErrorsByState()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                ErrorsByState = new List<StatCount>
                {
                    new StatCount { Name = "RUNNING", Count = 2 }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("ERRORS BY PRINTER STATE", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithStateEntries()
        {
            var now = DateTime.Now;
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                StateEntries = new List<StateEntry>
                {
                    new StateEntry { StateName = "IDLE", StartTime = now, EndTime = now.AddSeconds(10) },
                    new StateEntry { StateName = "IDLE", StartTime = now.AddSeconds(20), EndTime = now.AddSeconds(30) },
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("STATE DURATION SUMMARY", body);
        }

        [Fact]
        public void BuildPlainTextBody_SearchAndStats()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.SearchAndStatistics };
            var stats = new LogStatisticsResult { TotalPlcLogs = 10 };
            var results = new List<GrepResult>
            {
                new GrepResult { LocationName = "L", PreviewText = "x" }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, stats);
            Assert.Contains("Search & Statistics", body);
        }

        [Fact]
        public void BuildPlainTextBody_ManyResults_ShowsMore()
        {
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.SearchOnly };
            var results = Enumerable.Range(0, 25).Select(i =>
                new GrepResult { LocationName = "L", PreviewText = $"match {i}" }).ToList();
            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("and 5", body); // 25 - 20 = 5 more
        }

        [Fact]
        public void BuildPlainTextBody_AppGaps()
        {
            var now = DateTime.Now;
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                AppGaps = new List<GapInfo>
                {
                    new GapInfo { StartTime = now, EndTime = now.AddSeconds(3), DurationText = "3.0 sec" }
                }
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP:", body);
        }

        [Fact]
        public void BuildPlainTextBody_ManyPlcGaps_ShowsMore()
        {
            var now = DateTime.Now;
            var schedule = new ScheduledSearch { Name = "T", ScanMode = ScanMode.StatisticsOnly };
            var stats = new LogStatisticsResult
            {
                PlcGaps = Enumerable.Range(0, 15).Select(i =>
                    new GapInfo { StartTime = now.AddSeconds(i * 10), EndTime = now.AddSeconds(i * 10 + 5), DurationText = "5 sec" }).ToList()
            };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("and 5 more", body);
        }

        // ====================================================================
        //  21. EmailNotificationService — BuildSubject (private, via reflection)
        // ====================================================================

        [Fact]
        public void BuildSubject_CustomSubject_UsesIt()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var schedule = new ScheduledSearch
            {
                Name = "Test",
                ScanMode = ScanMode.SearchOnly,
                EmailConfig = new EmailNotificationConfig { CustomSubject = "My Custom" }
            };
            var result = (string)method.Invoke(svc, new object?[] { schedule, 5, null })!;
            Assert.Equal("My Custom", result);
            svc.Dispose();
        }

        [Fact]
        public void BuildSubject_NoCustom_SearchOnly_WithMatches()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var schedule = new ScheduledSearch
            {
                Name = "Test",
                ScanMode = ScanMode.SearchOnly,
                EmailConfig = new EmailNotificationConfig()
            };
            var result = (string)method.Invoke(svc, new object?[] { schedule, 10, null })!;
            Assert.Contains("10", result);
            Assert.Contains("matches", result);
            svc.Dispose();
        }

        [Fact]
        public void BuildSubject_NoCustom_SearchOnly_NoMatches()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var schedule = new ScheduledSearch
            {
                Name = "Test",
                ScanMode = ScanMode.SearchOnly,
                EmailConfig = new EmailNotificationConfig()
            };
            var result = (string)method.Invoke(svc, new object?[] { schedule, 0, null })!;
            Assert.Contains("No matches", result);
            svc.Dispose();
        }

        [Fact]
        public void BuildSubject_WithStats()
        {
            var svc = new EmailNotificationService();
            var method = typeof(EmailNotificationService).GetMethod("BuildSubject",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var schedule = new ScheduledSearch
            {
                Name = "Test",
                ScanMode = ScanMode.StatisticsOnly,
                EmailConfig = new EmailNotificationConfig()
            };
            var stats = new LogStatisticsResult { TotalPlcLogs = 100, TotalAppLogs = 50, TotalPlcErrors = 3, TotalAppErrors = 2 };
            var result = (string)method.Invoke(svc, new object?[] { schedule, 0, stats })!;
            Assert.Contains("150", result);
            Assert.Contains("errors", result);
            svc.Dispose();
        }

        // ====================================================================
        //  22. EmailNotificationService — Truncate (private static)
        // ====================================================================

        [Fact]
        public void EmailTruncate_Null_ReturnsEmpty()
        {
            var method = typeof(EmailNotificationService).GetMethod("Truncate",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal("", method.Invoke(null, new object[] { (string)null!, 100 }));
        }

        [Fact]
        public void EmailTruncate_Short_ReturnsOriginal()
        {
            var method = typeof(EmailNotificationService).GetMethod("Truncate",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal("abc", method.Invoke(null, new object[] { "abc", 100 }));
        }

        [Fact]
        public void EmailTruncate_Long_Truncates()
        {
            var method = typeof(EmailNotificationService).GetMethod("Truncate",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (string)method.Invoke(null, new object[] { new string('x', 200), 50 })!;
            Assert.Equal(53, result.Length);
        }

        // ====================================================================
        //  23. LogFileService.Helpers — IsDateStart (private static, via reflection)
        // ====================================================================

        [Fact]
        public void IsDateStart_ValidTimestamp_ReturnsTrue()
        {
            var method = typeof(LogFileService).GetMethod("IsDateStart",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True((bool)method.Invoke(null, new object[] { "2026-03-08 14:30:22,123 rest of line" })!);
        }

        [Fact]
        public void IsDateStart_TooShort_ReturnsFalse()
        {
            var method = typeof(LogFileService).GetMethod("IsDateStart",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.False((bool)method.Invoke(null, new object[] { "2026-03-08" })!);
        }

        [Fact]
        public void IsDateStart_WrongFormat_ReturnsFalse()
        {
            var method = typeof(LogFileService).GetMethod("IsDateStart",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.False((bool)method.Invoke(null, new object[] { "xxxx-xx-xx xx:xx:xx,xxx" })!);
        }

        // ====================================================================
        //  24. LogFileService.Helpers — ParseTimestampFast (private static)
        // ====================================================================

        [Fact]
        public void ParseTimestampFast_3Digits()
        {
            var method = typeof(LogFileService).GetMethod("ParseTimestampFast",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (DateTime)method.Invoke(null, new object[] { "2026-03-08 14:30:22,123" })!;
            Assert.Equal(2026, result.Year);
            Assert.Equal(3, result.Month);
            Assert.Equal(8, result.Day);
            Assert.Equal(14, result.Hour);
            Assert.Equal(30, result.Minute);
            Assert.Equal(22, result.Second);
            Assert.Equal(123, result.Millisecond);
        }

        [Fact]
        public void ParseTimestampFast_7Digits()
        {
            var method = typeof(LogFileService).GetMethod("ParseTimestampFast",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (DateTime)method.Invoke(null, new object[] { "2026-03-08 14:30:22,1234567" })!;
            Assert.Equal(2026, result.Year);
        }

        [Theory]
        [InlineData("2026-03-08 14:30:22,1234")]
        [InlineData("2026-03-08 14:30:22,12345")]
        [InlineData("2026-03-08 14:30:22,123456")]
        public void ParseTimestampFast_VariousDigits(string ts)
        {
            var method = typeof(LogFileService).GetMethod("ParseTimestampFast",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (DateTime)method.Invoke(null, new object[] { ts })!;
            Assert.Equal(2026, result.Year);
        }

        // ====================================================================
        //  25. LogFileService.Helpers — SplitCsvLine (private instance)
        // ====================================================================

        [Fact]
        public void SplitCsvLine_SimpleLine()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("SplitCsvLine",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (List<string>)method.Invoke(svc, new object[] { "a,b,c" })!;
            Assert.Equal(3, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsvLine_QuotedField()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("SplitCsvLine",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (List<string>)method.Invoke(svc, new object[] { "a,\"b,c\",d" })!;
            Assert.Equal(3, result.Count);
            Assert.Equal("b,c", result[1]);
        }

        // ====================================================================
        //  26. LogFileService.Helpers — ParseReadmeVersions (private instance)
        // ====================================================================

        [Fact]
        public void ParseReadmeVersions_MatchesBoth()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = ((string sw, string plc))method.Invoke(svc,
                new object[] { "Version: 1.2.3\nPressPlcVersion: 4.5.6" })!;
            Assert.Equal("1.2.3", result.sw);
            Assert.Equal("4.5.6", result.plc);
        }

        [Fact]
        public void ParseReadmeVersions_NoMatch()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = ((string sw, string plc))method.Invoke(svc,
                new object[] { "no version info here" })!;
            Assert.Equal("Unknown", result.sw);
            Assert.Equal("Unknown", result.plc);
        }

        // ====================================================================
        //  27. LogFileService.Helpers — ExtractPlcVersionFromSetupInfo
        // ====================================================================

        [Fact]
        public void ExtractPlcVersionFromSetupInfo_MatchesVersion()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("ExtractPlcVersionFromSetupInfo",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            string json = @"{""Name"": ""press-content-mcs-plc"", ""Version"": ""7.8.9""}";
            var result = (string?)method.Invoke(svc, new object[] { json });
            Assert.Equal("7.8.9", result);
        }

        [Fact]
        public void ExtractPlcVersionFromSetupInfo_NoMatch()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("ExtractPlcVersionFromSetupInfo",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (string?)method.Invoke(svc, new object[] { @"{""Name"": ""other""}" });
            Assert.Null(result);
        }

        // ====================================================================
        //  28. LogFileService.Helpers — SortLogEntriesCacheFriendly
        // ====================================================================

        [Fact]
        public void SortLogEntriesCacheFriendly_EmptyList()
        {
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var list = new List<LogEntry>();
            var result = (List<LogEntry>)method.Invoke(null, new object[] { list })!;
            Assert.Empty(result);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_SingleItem()
        {
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var list = new List<LogEntry> { MakeEntry() };
            var result = (List<LogEntry>)method.Invoke(null, new object[] { list })!;
            Assert.Single(result);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_SortsCorrectly()
        {
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var now = DateTime.Now;
            var list = new List<LogEntry>
            {
                MakeEntry(date: now.AddSeconds(3)),
                MakeEntry(date: now.AddSeconds(1)),
                MakeEntry(date: now.AddSeconds(2)),
            };
            var result = (List<LogEntry>)method.Invoke(null, new object[] { list })!;
            Assert.True(result[0].Date < result[1].Date);
            Assert.True(result[1].Date < result[2].Date);
        }

        // ====================================================================
        //  29. LogFileService.Helpers — CalculatePercent
        // ====================================================================

        [Fact]
        public void CalculatePercent_ZeroTotal_ReturnsZero()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("CalculatePercent",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (double)method.Invoke(svc, new object[] { 50L, 0L })!;
            Assert.Equal(0, result);
        }

        [Fact]
        public void CalculatePercent_NormalValues()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("CalculatePercent",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (double)method.Invoke(svc, new object[] { 50L, 100L })!;
            Assert.Equal(50, result);
        }

        [Fact]
        public void CalculatePercent_CapsAt99()
        {
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("CalculatePercent",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (double)method.Invoke(svc, new object[] { 100L, 100L })!;
            Assert.True(result <= 99);
        }

        // ====================================================================
        //  30. LogFileService.Helpers — IsEventsFile (private static)
        // ====================================================================

        [Theory]
        [InlineData("event-history__From2024.csv", true)]
        [InlineData("pressEvents.csv", true)]
        [InlineData("event-history__From2024.xml", true)]
        [InlineData("pressEvents.xml", true)]
        [InlineData("random.csv", false)]
        [InlineData("event-history__From2024.txt", false)]
        public void IsEventsFile_VariousNames(string name, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var args = new object[] { name, null! };
            var result = (bool)method.Invoke(null, args)!;
            Assert.Equal(expected, result);
        }

        // ====================================================================
        //  31. LogFileService.ZipClassification — IsNumericAppFile (private static)
        // ====================================================================

        [Theory]
        [InlineData("50300001.file", true)]
        [InlineData("50300001.file.log.8865", true)]
        [InlineData("engineGroupA.file", false)]
        [InlineData("readme.txt", false)]
        [InlineData("abc.file", false)]
        public void IsNumericAppFile_Tests(string name, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsNumericAppFile",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(expected, (bool)method.Invoke(null, new object[] { name })!);
        }

        // ====================================================================
        //  32. LogFileService.ZipClassification — IsSystabFile
        // ====================================================================

        [Theory]
        [InlineData("diagnosticslogs/systab_saved.txt", true)]
        [InlineData("foo/diagnosticslogs/systab_default.txt", true)]
        [InlineData("logs/systab_saved.txt", false)]
        [InlineData("diagnosticslogs/other.txt", false)]
        [InlineData("", false)]
        public void IsSystabFile_Tests(string path, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsSystabFile",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(expected, (bool)method.Invoke(null, new object[] { path })!);
        }

        // ====================================================================
        //  33. LogFileService.ZipClassification — IsGlobalsXmlFile
        // ====================================================================

        [Theory]
        [InlineData("datamanagement/ecommon/globals/params.xml", true)]
        [InlineData("foo/datamanagement/ecommon/globals/x.xml", true)]
        [InlineData("datamanagement/ecommon/globals/params.txt", false)]
        [InlineData("other/globals/params.xml", false)]
        [InlineData("", false)]
        public void IsGlobalsXmlFile_Tests(string path, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsGlobalsXmlFile",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(expected, (bool)method.Invoke(null, new object[] { path })!);
        }

        // ====================================================================
        //  34. LogFileService.ZipClassification — IsPluginCandidateExtension
        // ====================================================================

        [Theory]
        [InlineData("file.log", true)]
        [InlineData("file.txt", true)]
        [InlineData("file.csv", true)]
        [InlineData("file.json", true)]
        [InlineData("file.xml", true)]
        [InlineData("file.cfg", true)]
        [InlineData("file.ini", true)]
        [InlineData("file.config", true)]
        [InlineData("file.tsv", true)]
        [InlineData("file.file", true)]
        [InlineData("file.dll", false)]
        [InlineData("file.exe", false)]
        [InlineData("file.dat", false)]
        [InlineData("file", false)]
        public void IsPluginCandidateExtension_Tests(string name, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsPluginCandidateExtension",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(expected, (bool)method.Invoke(null, new object[] { name })!);
        }

        // ====================================================================
        //  35. LogFileService.ZipClassification — IsCustomTerminalLog
        // ====================================================================

        [Theory]
        [InlineData("whel3_data.csv", true)]
        [InlineData("ecm_log.txt", true)]
        [InlineData("COM1_data.log", true)]
        [InlineData("0001_data.csv", true)]
        [InlineData("Io-BIM.csv", true)]
        [InlineData("Stab-test.csv", true)]
        [InlineData("PRE_analysis.csv", true)]
        [InlineData("POST_analysis.csv", true)]
        [InlineData("random.txt", false)]
        [InlineData("", false)]
        public void IsCustomTerminalLog_Tests(string name, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsCustomTerminalLog",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var svc = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            Assert.Equal(expected, (bool)method.Invoke(svc, new object[] { name })!);
        }

        // ====================================================================
        //  36. ZipClassificationHelpers — static methods
        // ====================================================================

        [Theory]
        [InlineData("whel3.csv", true)]
        [InlineData("ecm_data.log", true)]
        [InlineData("random.txt", false)]
        [InlineData("", false)]
        public void ZipClassHelpers_IsCustomTerminalLog(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsCustomTerminalLog(name));
        }

        [Theory]
        [InlineData("diagnosticslogs/systab_saved.txt", true)]
        [InlineData("other/systab_saved.txt", false)]
        [InlineData("", false)]
        public void ZipClassHelpers_IsSystabFile(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsSystabFile(path));
        }

        [Theory]
        [InlineData("file.log", true)]
        [InlineData("file.dll", false)]
        [InlineData("file", false)]
        public void ZipClassHelpers_IsPluginCandidateExtension(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsPluginCandidateExtension(name));
        }

        [Theory]
        [InlineData("datamanagement/ecommon/globals/f.xml", true)]
        [InlineData("other/f.xml", false)]
        public void ZipClassHelpers_IsGlobalsXmlFile(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsGlobalsXmlFile(path));
        }

        [Theory]
        [InlineData("foo/terminallogs/file.csv", true)]
        [InlineData("terminallogs/file.csv", true)]
        [InlineData("other/file.csv", false)]
        public void ZipClassHelpers_IsTerminalLogsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsTerminalLogsPath(path));
        }

        [Theory]
        [InlineData("foo/lrs/file.csv", true)]
        [InlineData("lrs/file.csv", true)]
        [InlineData("other/file.csv", false)]
        public void ZipClassHelpers_IsLrsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsLrsPath(path));
        }

        [Theory]
        [InlineData("foo/configuration/file.db", true)]
        [InlineData("configuration/file.db", true)]
        [InlineData("other/file.db", false)]
        public void ZipClassHelpers_IsConfigurationPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsConfigurationPath(path));
        }

        [Theory]
        [InlineData("Indigo.Infra.EM_Statistics.csv", true)]
        [InlineData("random.csv", false)]
        [InlineData("", false)]
        public void ZipClassHelpers_IsEmStatisticsFile(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsEmStatisticsFile(name));
        }

        [Theory]
        [InlineData("foo/backup/file.log", true)]
        [InlineData("foo/old/file.log", true)]
        [InlineData("foo/temp/file.log", true)]
        [InlineData("foo/archive/file.log", true)]
        [InlineData("foo/logs/file.log", false)]
        public void ZipClassHelpers_ShouldSkipEntry(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.ShouldSkipEntry(path));
        }

        // ====================================================================
        //  37. TextFilterParser — Parse
        // ====================================================================

        [Fact]
        public void TextFilterParser_Parse_Null_ReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(null));
        }

        [Fact]
        public void TextFilterParser_Parse_Empty_ReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(""));
            Assert.Null(TextFilterParser.Parse("   "));
        }

        [Fact]
        public void TextFilterParser_Parse_SimpleContains()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'hello')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node!.Type);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("hello", node.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_StartsWith()
        {
            var node = TextFilterParser.Parse("StartsWith([Thread], 'Main')");
            Assert.NotNull(node);
            Assert.Equal("ThreadName", node!.Field);
            Assert.Equal("Begins With", node.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_EndsWith()
        {
            var node = TextFilterParser.Parse("EndsWith([Level], 'ROR')");
            Assert.NotNull(node);
            Assert.Equal("Level", node!.Field);
            Assert.Equal("Ends With", node.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_Equals()
        {
            var node = TextFilterParser.Parse("Equals([Logger], 'MyLogger')");
            Assert.NotNull(node);
            Assert.Equal("Equals", node!.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_AndCombination()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'a') And Contains([Level], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_OrCombination()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'a') Or Contains([Level], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_Parentheses()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'a') And (Contains([Level], 'b') Or Contains([Level], 'c'))");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("AND", node.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownField_PassThrough()
        {
            var node = TextFilterParser.Parse("Contains([CustomField], 'val')");
            Assert.NotNull(node);
            Assert.Equal("CustomField", node!.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownFunction_DefaultsToContains()
        {
            var node = TextFilterParser.Parse("CustomFunc([Message], 'val')");
            Assert.NotNull(node);
            Assert.Equal("Contains", node!.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_MissingCloseParen_Throws()
        {
            Assert.Throws<FormatException>(() => TextFilterParser.Parse("Contains([Message], 'val'"));
        }

        [Fact]
        public void TextFilterParser_Parse_UnexpectedToken_Throws()
        {
            Assert.Throws<FormatException>(() => TextFilterParser.Parse("And"));
        }

        [Fact]
        public void TextFilterParser_Parse_ExtraTokensAfter_Throws()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') Contains([Level], 'b')"));
        }

        // ====================================================================
        //  38. TimeRangeFilter — Resolve
        // ====================================================================

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter { From = DateTime.Now, RelativeRange = RelativeTimeRange.None };
            var resolved = filter.Resolve();
            Assert.Same(filter, resolved);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True((DateTime.Now - resolved.From!.Value).TotalHours < 25);
            Assert.Null(resolved.To);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True((DateTime.Now - resolved.From!.Value).TotalDays < 8);
        }

        // ====================================================================
        //  39. ScheduledSearch — RepeatIntervalMinutes getter/setter
        // ====================================================================

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Hours()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 2, IntervalUnit = IntervalUnit.Hours };
            Assert.Equal(120, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Days()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 1, IntervalUnit = IntervalUnit.Days };
            Assert.Equal(1440, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Minutes()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 30, IntervalUnit = IntervalUnit.Minutes };
            Assert.Equal(30, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_SetterBackwardCompat_Days()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 2880;
            Assert.Equal(IntervalUnit.Days, s.IntervalUnit);
            Assert.Equal(2, s.RepeatIntervalValue);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_SetterBackwardCompat_Hours()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 120;
            Assert.Equal(IntervalUnit.Hours, s.IntervalUnit);
            Assert.Equal(2, s.RepeatIntervalValue);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_SetterBackwardCompat_Minutes()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 45;
            Assert.Equal(IntervalUnit.Minutes, s.IntervalUnit);
            Assert.Equal(45, s.RepeatIntervalValue);
        }

        // ====================================================================
        //  40. ScheduledSearch — SearchSummary
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
        public void ScheduledSearch_SearchSummary_StatsOnly()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true }
            };
            Assert.Contains("no criteria", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_WithConditions_PlcOnly()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.SearchOnly,
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
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:error", s.SearchSummary);
            Assert.Contains("[PLC]", s.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_AppOnly()
        {
            var s = new ScheduledSearch
            {
                ScanMode = ScanMode.SearchOnly,
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
                                new SearchCondition { Field = SearchField.Message, Value = "err" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[APP]", s.SearchSummary);
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
                                new SearchCondition { Field = SearchField.Message, Value = "err" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[Search+Stats]", s.SearchSummary);
        }

        // ====================================================================
        //  41. SearchCondition — CompiledRegex caching
        // ====================================================================

        [Fact]
        public void SearchCondition_CompiledRegex_NullForNonRegex()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CompilesForRegex()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            Assert.NotNull(cond.CompiledRegex);
            Assert.True(cond.CompiledRegex!.IsMatch("abc123"));
        }

        [Fact]
        public void SearchCondition_CompiledRegex_NullForInvalidPattern()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"[invalid" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CachesWhenValueUnchanged()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            var r1 = cond.CompiledRegex;
            var r2 = cond.CompiledRegex;
            Assert.Same(r1, r2);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_NullForEmptyValue()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "" };
            Assert.Null(cond.CompiledRegex);
        }

        // ====================================================================
        //  42. FilterNode — DeepClone and CompiledRegex
        // ====================================================================

        [Fact]
        public void FilterNode_DeepClone_ClonesTree()
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new System.Collections.ObjectModel.ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "test" }
                }
            };
            var clone = node.DeepClone();
            Assert.NotSame(node, clone);
            Assert.Equal(node.Type, clone.Type);
            Assert.Equal(node.LogicalOperator, clone.LogicalOperator);
            Assert.Single(clone.Children);
            Assert.NotSame(node.Children[0], clone.Children[0]);
            Assert.Equal("test", clone.Children[0].Value);
        }

        [Fact]
        public void FilterNode_CompiledRegex_NullForNonRegex()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CompilesForRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            Assert.NotNull(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_ClearedOnValueChange()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var r1 = node.CompiledRegex;
            Assert.NotNull(r1);
            node.Value = @"\w+";
            var r2 = node.CompiledRegex;
            Assert.NotSame(r1, r2);
        }

        // ====================================================================
        //  43. GrepResult — TimestampDisplay
        // ====================================================================

        [Fact]
        public void GrepResult_TimestampDisplay_WithReferencedEntry()
        {
            var entry = MakeEntry(date: new DateTime(2026, 1, 15, 10, 30, 45));
            var gr = new GrepResult { ReferencedLogEntry = entry };
            Assert.Contains("2026-01-15", gr.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_WithTimestamp()
        {
            var gr = new GrepResult { Timestamp = new DateTime(2026, 2, 20, 8, 0, 0) };
            Assert.Contains("2026-02-20", gr.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_NoTimestamp()
        {
            var gr = new GrepResult();
            Assert.Equal("N/A", gr.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_IsSelected_RaisesPropertyChanged()
        {
            var gr = new GrepResult();
            bool raised = false;
            gr.PropertyChanged += (s, e) => { if (e.PropertyName == "IsSelected") raised = true; };
            gr.IsSelected = true;
            Assert.True(raised);
        }

        // ====================================================================
        //  44. StateEntry — Duration property
        // ====================================================================

        [Fact]
        public void StateEntry_Duration_WithEndTime()
        {
            var s = new StateEntry
            {
                StartTime = new DateTime(2026, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2026, 1, 1, 10, 1, 30, 500)
            };
            Assert.Contains("01:30", s.Duration);
        }

        [Fact]
        public void StateEntry_Duration_NoEndTime()
        {
            var s = new StateEntry { StartTime = DateTime.Now };
            Assert.Equal("Current...", s.Duration);
        }

        // ====================================================================
        //  45. TabSelectionConfig — CreateForConfiguration
        // ====================================================================

        [Fact]
        public void TabSelectionConfig_CreateForConfiguration_S6()
        {
            var config = TabSelectionConfig.CreateForConfiguration(true);
            Assert.True(config.IsS6);
            Assert.True(config.LoadApp);
            Assert.True(config.LoadPlc);
        }

        [Fact]
        public void TabSelectionConfig_CreateForConfiguration_S45()
        {
            var config = TabSelectionConfig.CreateForConfiguration(false);
            Assert.False(config.IsS6);
        }

        // ====================================================================
        //  46. SearchLocation — PropertyChanged events
        // ====================================================================

        [Fact]
        public void SearchLocation_PropertyChanged()
        {
            var loc = new SearchLocation();
            var changed = new List<string>();
            loc.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            loc.Name = "Test";
            loc.Address = "1.2.3.4";
            loc.BasePath = @"\\server\share";
            loc.IsActive = false;
            loc.ConnectionStatus = ConnectionStatus.Connected;
            loc.LastAccessed = DateTime.Now;

            Assert.Contains("Name", changed);
            Assert.Contains("Address", changed);
            Assert.Contains("BasePath", changed);
            Assert.Contains("IsActive", changed);
            Assert.Contains("ConnectionStatus", changed);
            Assert.Contains("LastAccessed", changed);
        }

        // ====================================================================
        //  47. LogFileClassifier — DetermineLogType
        // ====================================================================

        [Theory]
        [InlineData("appdev.log", "APP")]
        [InlineData("press.host.app.log", "APP")]
        [InlineData("50300001.file", "APP")]
        [InlineData("engineGroupA.file.log", "PLC")]
        [InlineData("other.file.log", "PLC")]
        public void LogFileClassifier_DetermineLogType(string path, string expected)
        {
            Assert.Equal(expected, LogFileClassifier.DetermineLogType(path));
        }

        // ====================================================================
        //  48. LogFileClassifier — IsLogEntry
        // ====================================================================

        [Theory]
        [InlineData("engineGroupA.file.log", true, false, true)]
        [InlineData("test.zip", true, false, false)]
        [InlineData("appdev.log", false, true, true)]
        public void LogFileClassifier_IsLogEntry(string name, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsLogEntry(name, plc, app));
        }

        // ====================================================================
        //  49. LogFileClassifier — IsSearchableLogFile with paths
        // ====================================================================

        [Theory]
        [InlineData("foo/bar/enginegroupa.file.log", true, false, true)]
        [InlineData("foo\\bar\\appdev.log", false, true, true)]
        [InlineData("no-sn_some_file.log", true, false, true)]
        public void LogFileClassifier_IsSearchableLogFile_WithPaths(string path, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsSearchableLogFile(path, plc, app));
        }

        // ====================================================================
        //  50. LogFileClassifier — IsNumericAppFileName
        // ====================================================================

        [Theory]
        [InlineData("50300001.file", true)]
        [InlineData("enginegroupa.file", false)]
        [InlineData("abc.file", false)]
        [InlineData("test1.file.log", true)]
        [InlineData("noextension", false)]
        public void LogFileClassifier_IsNumericAppFileName(string name, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(name));
        }

        // ====================================================================
        //  51. MergeReloadResults via reflection
        // ====================================================================

        [Fact]
        public void MergeReloadResults_PlcLogs()
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var session = new LogSessionData();
            var sel = new TabSelectionConfig { LoadPlc = true };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var now = DateTime.Now;
            logsBag.Add(new List<LogEntry> { MakeEntry(date: now.AddSeconds(2)), MakeEntry(date: now) });
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var evtBag = new ConcurrentBag<List<EventEntry>>();
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            method.Invoke(null, new object[] { session, sel, "Plc", logsBag, appBag, evtBag, screenshots });
            Assert.Equal(2, session.Logs.Count);
            Assert.True(session.Logs[0].Date <= session.Logs[1].Date);
        }

        [Fact]
        public void MergeReloadResults_AppLogs()
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var session = new LogSessionData();
            var sel = new TabSelectionConfig { LoadApp = true };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            appBag.Add(new List<LogEntry> { MakeEntry() });
            var evtBag = new ConcurrentBag<List<EventEntry>>();
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            method.Invoke(null, new object[] { session, sel, "App", logsBag, appBag, evtBag, screenshots });
            Assert.Single(session.AppDevLogs);
        }

        [Fact]
        public void MergeReloadResults_Events()
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var session = new LogSessionData();
            var sel = new TabSelectionConfig { LoadEvents = true };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var evtBag = new ConcurrentBag<List<EventEntry>>();
            evtBag.Add(new List<EventEntry> { new EventEntry { Time = DateTime.Now } });
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            method.Invoke(null, new object[] { session, sel, "Events", logsBag, appBag, evtBag, screenshots });
            Assert.Single(session.Events);
        }

        [Fact]
        public void MergeReloadResults_UpdatesLoadTabSelection()
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var session = new LogSessionData { LoadTabSelection = new TabSelectionConfig() };
            var sel = new TabSelectionConfig { LoadGlobals = true };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var evtBag = new ConcurrentBag<List<EventEntry>>();
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            method.Invoke(null, new object[] { session, sel, "Globals", logsBag, appBag, evtBag, screenshots });
            Assert.True(session.LoadTabSelection.LoadGlobals);
        }

        [Theory]
        [InlineData("App", "LoadApp")]
        [InlineData("Plc", "LoadPlc")]
        [InlineData("Events", "LoadEvents")]
        [InlineData("Screenshots", "LoadScreenshots")]
        [InlineData("TerminalLogs", "LoadTerminalLogs")]
        [InlineData("Configuration", "LoadConfiguration")]
        [InlineData("Systab", "LoadSystab")]
        [InlineData("Lrs", "LoadLrs")]
        [InlineData("SetupInfo", "LoadSetupInfo")]
        [InlineData("ManagerThread", "LoadManagerThread")]
        public void MergeReloadResults_SetsCorrectLoadFlag(string componentName, string propertyName)
        {
            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var tabSel = new TabSelectionConfig();
            var session = new LogSessionData { LoadTabSelection = tabSel };
            var sel = new TabSelectionConfig();
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var evtBag = new ConcurrentBag<List<EventEntry>>();
            var screenshots = new List<System.Windows.Media.Imaging.BitmapImage>();

            method.Invoke(null, new object[] { session, sel, componentName, logsBag, appBag, evtBag, screenshots });

            var prop = typeof(TabSelectionConfig).GetProperty(propertyName)!;
            Assert.True((bool)prop.GetValue(tabSel)!);
        }
    }
}
