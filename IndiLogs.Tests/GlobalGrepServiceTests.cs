using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class GlobalGrepServiceTests
    {
        private readonly GlobalGrepService _service = new GlobalGrepService();

        private static LogEntry MakeEntry(
            string message = "test message",
            string level = "INFO",
            string logger = "MyApp.Service",
            string threadName = "Main",
            string method = "DoWork",
            string? data = null,
            string? exception = null,
            DateTime? date = null)
        {
            return new LogEntry
            {
                Message = message,
                Level = level,
                Logger = logger,
                ThreadName = threadName,
                Method = method,
                Data = data ?? "",
                Exception = exception ?? "",
                Date = date ?? DateTime.Now,
                Pattern = "already-parsed" // prevent LogParserService.ParseLogEntry from being called
            };
        }

        private static SearchCondition Cond(
            SearchField field = SearchField.Message,
            SearchOperator op = SearchOperator.Contains,
            string value = "test",
            bool negate = false)
        {
            return new SearchCondition { Field = field, Operator = op, Value = value, Negate = negate };
        }

        private static SearchCriteria SingleCriteria(SearchCondition condition,
            ConditionOperator groupOp = ConditionOperator.And)
        {
            return new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = groupOp,
                        Conditions = new List<SearchCondition> { condition }
                    }
                }
            };
        }

        private static LogSessionData MakeSession(string fileName,
            List<LogEntry>? plcLogs = null, List<LogEntry>? appLogs = null)
        {
            return new LogSessionData
            {
                FileName = fileName,
                FilePath = $@"C:\test\{fileName}",
                Logs = plcLogs ?? new List<LogEntry>(),
                AppDevLogs = appLogs ?? new List<LogEntry>()
            };
        }

        // ====================================================================
        //  EvaluateCondition (public)
        // ====================================================================

        [Fact]
        public void EvaluateCondition_Contains_Match()
        {
            var entry = MakeEntry("Error occurred in module");
            var cond = Cond(SearchField.Message, SearchOperator.Contains, "error");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Contains_NoMatch()
        {
            var entry = MakeEntry("All good");
            var cond = Cond(SearchField.Message, SearchOperator.Contains, "error");
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Equals_CaseInsensitive()
        {
            var entry = MakeEntry(level: "ERROR");
            var cond = Cond(SearchField.Level, SearchOperator.Equals, "error");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Equals_NoMatch()
        {
            var entry = MakeEntry(level: "Warning");
            var cond = Cond(SearchField.Level, SearchOperator.Equals, "Error");
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_StartsWith_Match()
        {
            var entry = MakeEntry(logger: "MyApp.Services.Logging");
            var cond = Cond(SearchField.Logger, SearchOperator.StartsWith, "myapp");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_EndsWith_Match()
        {
            var entry = MakeEntry(method: "ProcessData");
            var cond = Cond(SearchField.Method, SearchOperator.EndsWith, "data");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Regex_Match()
        {
            var entry = MakeEntry("Error code: 404");
            var cond = Cond(SearchField.Message, SearchOperator.Regex, @"code:\s*\d+");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Regex_InvalidPattern_ReturnsFalse()
        {
            var entry = MakeEntry("test message");
            var cond = Cond(SearchField.Message, SearchOperator.Regex, "[invalid");
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Negate_InvertsResult()
        {
            var entry = MakeEntry("Error occurred");
            var cond = Cond(SearchField.Message, SearchOperator.Contains, "error", negate: true);
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_Negate_NoMatch_ReturnsTrue()
        {
            var entry = MakeEntry("All good");
            var cond = Cond(SearchField.Message, SearchOperator.Contains, "error", negate: true);
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_SearchFieldAny_MatchesAnyField()
        {
            var entry = MakeEntry(message: "normal", exception: "NullRef error");
            var cond = Cond(SearchField.Any, SearchOperator.Contains, "NullRef");
            Assert.True(_service.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_EmptyValue_ReturnsFalse()
        {
            var entry = MakeEntry(data: "some data");
            var cond = Cond(SearchField.Data, SearchOperator.Contains, "");
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        [Theory]
        [InlineData(SearchField.Message, "test message", true)]
        [InlineData(SearchField.Level, "INFO", true)]
        [InlineData(SearchField.ThreadName, "Main", true)]
        [InlineData(SearchField.Logger, "MyApp", true)]
        [InlineData(SearchField.Method, "DoWork", true)]
        [InlineData(SearchField.Data, "somedata", true)]
        [InlineData(SearchField.Exception, "NullRef", true)]
        [InlineData(SearchField.Message, "missing", false)]
        public void EvaluateCondition_SpecificFields(SearchField field, string value, bool expected)
        {
            var entry = MakeEntry("test message", level: "INFO", threadName: "Main",
                logger: "MyApp.Service", method: "DoWork", data: "somedata", exception: "NullRef");
            var cond = Cond(field, SearchOperator.Contains, value);
            Assert.Equal(expected, _service.EvaluateCondition(entry, cond));
        }

        // ====================================================================
        //  EvaluateGroup (public)
        // ====================================================================

        [Fact]
        public void EvaluateGroup_AndOperator_AllMustMatch()
        {
            var entry = MakeEntry("Error in module", level: "Error");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "error"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.True(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_AndOperator_OneFails()
        {
            var entry = MakeEntry("Error in module", level: "Info");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "error"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.False(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_AnyMatches()
        {
            var entry = MakeEntry("All good", level: "Error");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "missing"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.True(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_NoneMatches()
        {
            var entry = MakeEntry("All good", level: "Info");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "missing"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.False(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_NoneMatch()
        {
            var entry = MakeEntry("All good", level: "Info");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "error"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.True(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_OneMatches_ReturnsFalse()
        {
            var entry = MakeEntry("Error occurred", level: "Info");
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    Cond(SearchField.Message, SearchOperator.Contains, "error"),
                    Cond(SearchField.Level, SearchOperator.Equals, "Error")
                }
            };
            Assert.False(_service.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_EmptyConditions_ReturnsTrue()
        {
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>()
            };
            Assert.True(_service.EvaluateGroup(MakeEntry(), group));
        }

        // ====================================================================
        //  EvaluateCriteria (public)
        // ====================================================================

        [Fact]
        public void EvaluateCriteria_NoGroups_ReturnsTrue()
        {
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };
            Assert.True(_service.EvaluateCriteria(MakeEntry(), criteria));
        }

        [Fact]
        public void EvaluateCriteria_AndBetweenGroups_BothMatch()
        {
            var entry = MakeEntry("Error in module", level: "Error");
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "error")
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Level, SearchOperator.Equals, "Error")
                        }
                    }
                }
            };
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_AndBetweenGroups_OneFails()
        {
            var entry = MakeEntry("Error in module", level: "Info");
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "error")
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Level, SearchOperator.Equals, "Error")
                        }
                    }
                }
            };
            Assert.False(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_OrBetweenGroups_OneMatches()
        {
            var entry = MakeEntry("All good", level: "Error");
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.Or,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "missing")
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Level, SearchOperator.Equals, "Error")
                        }
                    }
                }
            };
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NorGroupConditions()
        {
            var entry = MakeEntry("Safe operation");
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Nor,
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Error"),
                            Cond(SearchField.Message, SearchOperator.Contains, "Warning")
                        }
                    }
                }
            };
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_AnyField_MatchesAcrossFields()
        {
            var entry = MakeEntry("normal msg", data: "special_data_value");
            var criteria = SingleCriteria(
                Cond(SearchField.Any, SearchOperator.Contains, "special_data"));
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_StartsWithOperator()
        {
            var entry = MakeEntry("Motor started");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.StartsWith, "Motor"));
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_EndsWithOperator()
        {
            var entry = MakeEntry("Motor started");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.EndsWith, "started"));
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_RegexOperator()
        {
            var entry = MakeEntry("Error code 42");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.Regex, @"code \d+"));
            Assert.True(_service.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NegateCondition()
        {
            var entry = MakeEntry("Motor started");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.Contains, "Motor", negate: true));
            Assert.False(_service.EvaluateCriteria(entry, criteria));
        }

        // ====================================================================
        //  DetermineMatchedFields (public)
        // ====================================================================

        [Fact]
        public void DetermineMatchedFields_ReturnsMatchingFieldNames()
        {
            var entry = MakeEntry("Error occurred", exception: "NullRef");
            var criteria = SingleCriteria(
                Cond(SearchField.Any, SearchOperator.Contains, "error"));
            string fields = _service.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Message", fields);
        }

        [Fact]
        public void DetermineMatchedFields_NoCriteria_ReturnsEmpty()
        {
            var criteria = new SearchCriteria { Groups = null! };
            Assert.Equal("", _service.DetermineMatchedFields(MakeEntry(), criteria));
        }

        [Fact]
        public void DetermineMatchedFields_EmptyGroups_ReturnsEmpty()
        {
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };
            Assert.Equal("", _service.DetermineMatchedFields(MakeEntry(), criteria));
        }

        [Fact]
        public void DetermineMatchedFields_MessageField()
        {
            var entry = MakeEntry("Motor started");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.Contains, "Motor"));
            Assert.Equal("Message", _service.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_MultipleFields()
        {
            var entry = MakeEntry("ErrorData", data: "ErrorData");
            var criteria = SingleCriteria(
                Cond(SearchField.Any, SearchOperator.Contains, "ErrorData"));
            var result = _service.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Message", result);
            Assert.Contains("Data", result);
        }

        [Fact]
        public void DetermineMatchedFields_WhitespaceValue_Skipped()
        {
            var entry = MakeEntry("test");
            var criteria = SingleCriteria(
                Cond(SearchField.Message, SearchOperator.Contains, "  "));
            Assert.Equal("", _service.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_ExceptionField()
        {
            var entry = MakeEntry("normal", exception: "StackOverflow");
            var criteria = SingleCriteria(
                Cond(SearchField.Exception, SearchOperator.Contains, "StackOverflow"));
            Assert.Equal("Exception", _service.DetermineMatchedFields(entry, criteria));
        }

        // ====================================================================
        //  SearchLoadedSessionsAsync
        // ====================================================================

        [Fact]
        public async Task SearchLoadedSessionsAsync_SimpleText_FindsMatches()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Motor started successfully"),
                    MakeEntry("Sensor check passed"),
                    MakeEntry("Motor stopped")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "Motor", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Contains("Motor", r.PreviewText));
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_Regex_FindsMatches()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Error code 123"),
                    MakeEntry("Error code 456"),
                    MakeEntry("No error here")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, @"Error code \d+", useRegex: true,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_EmptySessions_ReturnsEmpty()
        {
            var results = await _service.SearchLoadedSessionsAsync(
                Enumerable.Empty<LogSessionData>(), "test", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_Cancellation_ThrowsOrReturnsPartial()
        {
            var sessions = Enumerable.Range(0, 100).Select(i =>
                MakeSession($"session{i}.zip",
                    plcLogs: new List<LogEntry> { MakeEntry("test data") })).ToList();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Task.Run with an already-cancelled token throws TaskCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await _service.SearchLoadedSessionsAsync(
                    sessions, "test", useRegex: false,
                    searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                    progress: null, cancellationToken: cts.Token));
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_SearchesExceptionField()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Normal message", exception: "NullReferenceException at line 42"),
                    MakeEntry("Another message")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "NullReference", useRegex: false,
                searchMessage: false, searchException: true, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_SearchesMethodField()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("msg1", method: "InitializeComponent"),
                    MakeEntry("msg2", method: "Dispose")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "Initialize", useRegex: false,
                searchMessage: false, searchException: false, searchMethod: true, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_SearchesDataField()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("msg1", data: "key=value123"),
                    MakeEntry("msg2", data: "other=data")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "value123", useRegex: false,
                searchMessage: false, searchException: false, searchMethod: false, searchData: true,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_SearchesAppDevLogs()
        {
            var session = MakeSession("test.zip",
                appLogs: new List<LogEntry>
                {
                    MakeEntry("APP log entry with target"),
                    MakeEntry("APP log entry without match")
                });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "target", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Single(results);
            Assert.Equal("APP", results[0].LogType);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_CaseInsensitive()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry> { MakeEntry("Hello World") });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "hello world", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_MultipleSessions_AggregatesResults()
        {
            var sessions = new[]
            {
                MakeSession("session1.zip", plcLogs: new List<LogEntry> { MakeEntry("Motor started") }),
                MakeSession("session2.zip", plcLogs: new List<LogEntry> { MakeEntry("Motor stopped") }),
                MakeSession("session3.zip", plcLogs: new List<LogEntry> { MakeEntry("Sensor active") })
            };

            var results = await _service.SearchLoadedSessionsAsync(
                sessions, "Motor", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_BothPLCAndAPP()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry> { MakeEntry("Motor PLC log") },
                appLogs: new List<LogEntry> { MakeEntry("Motor APP log") });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "Motor", useRegex: false,
                searchMessage: true, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.LogType == "PLC");
            Assert.Contains(results, r => r.LogType == "APP");
        }

        [Fact]
        public async Task SearchLoadedSessionsAsync_NoFieldsSelected_NoResults()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry> { MakeEntry("Motor started") });

            var results = await _service.SearchLoadedSessionsAsync(
                new[] { session }, "Motor", useRegex: false,
                searchMessage: false, searchException: false, searchMethod: false, searchData: false,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Empty(results);
        }

        // ====================================================================
        //  SearchLoadedSessionsWithCriteriaAsync
        // ====================================================================

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_SimpleContains_FindsMatches()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Motor started"),
                    MakeEntry("Sensor active"),
                    MakeEntry("Motor stopped")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Motor")
                        }
                    }
                }
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("PLC", r.LogType));
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_SearchAPPOnly()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry> { MakeEntry("PLC match target") },
                appLogs: new List<LogEntry> { MakeEntry("APP match target") });

            var criteria = new SearchCriteria
            {
                SearchPLC = false,
                SearchAPP = true,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "target")
                        }
                    }
                }
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Single(results);
            Assert.Equal("APP", results[0].LogType);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_TimeFilter_ExcludesOutOfRange()
        {
            var baseTime = new DateTime(2025, 6, 15, 12, 0, 0);
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("early", date: baseTime.AddHours(-2)),
                    MakeEntry("in range", date: baseTime),
                    MakeEntry("late", date: baseTime.AddHours(2))
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                ResultTimeFilter = new TimeRangeFilter
                {
                    From = baseTime.AddHours(-1),
                    To = baseTime.AddHours(1)
                },
                Groups = new List<SearchConditionGroup>()
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Single(results);
            Assert.Equal("in range", results[0].PreviewText);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_EmptyGroups_MatchesAll()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("entry1"),
                    MakeEntry("entry2")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>()
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_StreamingCallback_InvokesPerResult()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Motor started"),
                    MakeEntry("Motor stopped")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Motor")
                        }
                    }
                }
            };

            var streamed = new List<GrepResult>();
            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None,
                onResult: r => streamed.Add(r));

            Assert.Equal(2, streamed.Count);
            Assert.Empty(results); // when onResult is provided, return list is empty
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_OrWithinGroup()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Motor started"),
                    MakeEntry("Sensor active"),
                    MakeEntry("Pump running")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Motor"),
                            Cond(SearchField.Message, SearchOperator.Contains, "Sensor")
                        }
                    }
                }
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_SetsLineNumberAndSessionIndex()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("skip"),
                    MakeEntry("Motor found"),
                    MakeEntry("skip again")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Motor")
                        }
                    }
                }
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Single(results);
            Assert.Equal(2, results[0].LineNumber); // 1-based
            Assert.Equal(0, results[0].SessionIndex);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_SetsMatchedField()
        {
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("Motor started")
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "Motor")
                        }
                    }
                }
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Single(results);
            Assert.Contains("Message", results[0].MatchedField);
        }

        [Fact]
        public async Task SearchLoadedSessionsWithCriteriaAsync_TimeFilterFromOnly()
        {
            var baseTime = new DateTime(2025, 6, 15, 12, 0, 0);
            var session = MakeSession("test.zip",
                plcLogs: new List<LogEntry>
                {
                    MakeEntry("early", date: baseTime.AddHours(-2)),
                    MakeEntry("later", date: baseTime.AddHours(2))
                });

            var criteria = new SearchCriteria
            {
                SearchPLC = true,
                SearchAPP = false,
                ResultTimeFilter = new TimeRangeFilter { From = baseTime },
                Groups = new List<SearchConditionGroup>()
            };

            var results = await _service.SearchLoadedSessionsWithCriteriaAsync(
                new[] { session }, criteria,
                new Progress<(int, int, string)>(), CancellationToken.None);

            Assert.Single(results);
            Assert.Equal("later", results[0].PreviewText);
        }

        // ====================================================================
        //  CreateMatchPredicate (private) via reflection
        // ====================================================================

        private Func<string, bool> InvokeCreateMatchPredicate(string query, bool useRegex)
        {
            var method = typeof(GlobalGrepService).GetMethod("CreateMatchPredicate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (Func<string, bool>)method!.Invoke(_service, new object[] { query, useRegex })!;
        }

        [Fact]
        public void CreateMatchPredicate_SimpleText_CaseInsensitive()
        {
            var predicate = InvokeCreateMatchPredicate("hello", false);
            Assert.True(predicate("Hello World"));
            Assert.True(predicate("say hello"));
            Assert.False(predicate("goodbye"));
        }

        [Fact]
        public void CreateMatchPredicate_Regex_MatchesPattern()
        {
            var predicate = InvokeCreateMatchPredicate(@"\d{3}", true);
            Assert.True(predicate("code 123"));
            Assert.False(predicate("code 12"));
        }

        [Fact]
        public void CreateMatchPredicate_InvalidRegex_FallsBackToContains()
        {
            var predicate = InvokeCreateMatchPredicate("[invalid", true);
            Assert.True(predicate("[invalid"));
            Assert.False(predicate("something else"));
        }

        [Fact]
        public void CreateMatchPredicate_NullOrEmptyInput_ReturnsFalse()
        {
            var predicate = InvokeCreateMatchPredicate("test", false);
            Assert.False(predicate(""));
            Assert.False(predicate(null!));
        }

        [Fact]
        public void CreateMatchPredicate_BooleanAnd()
        {
            var predicate = InvokeCreateMatchPredicate("Motor AND started", false);
            Assert.True(predicate("Motor started successfully"));
            Assert.False(predicate("Motor stopped"));
            Assert.False(predicate("Sensor started"));
        }

        [Fact]
        public void CreateMatchPredicate_BooleanOr()
        {
            var predicate = InvokeCreateMatchPredicate("Motor OR Sensor", false);
            Assert.True(predicate("Motor active"));
            Assert.True(predicate("Sensor active"));
            Assert.False(predicate("Pump active"));
        }

        // ====================================================================
        //  EvaluateQueryOnText (private) via reflection
        // ====================================================================

        private bool InvokeEvaluateQueryOnText(string text, FilterNode? node)
        {
            var method = typeof(GlobalGrepService).GetMethod("EvaluateQueryOnText",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (bool)method!.Invoke(_service, new object?[] { text, node })!;
        }

        [Fact]
        public void EvaluateQueryOnText_NullNode_ReturnsFalse()
        {
            Assert.False(InvokeEvaluateQueryOnText("some text", null));
        }

        [Fact]
        public void EvaluateQueryOnText_EmptyText_ReturnsFalse()
        {
            var node = new FilterNode { Type = NodeType.Condition, Value = "test" };
            Assert.False(InvokeEvaluateQueryOnText("", node));
        }

        [Fact]
        public void EvaluateQueryOnText_SimpleCondition_Match()
        {
            var node = new FilterNode { Type = NodeType.Condition, Value = "hello" };
            Assert.True(InvokeEvaluateQueryOnText("hello world", node));
        }

        [Fact]
        public void EvaluateQueryOnText_SimpleCondition_NoMatch()
        {
            var node = new FilterNode { Type = NodeType.Condition, Value = "xyz" };
            Assert.False(InvokeEvaluateQueryOnText("hello world", node));
        }

        [Fact]
        public void EvaluateQueryOnText_NotCondition()
        {
            var node = new FilterNode { Type = NodeType.Condition, Value = "xyz", LogicalOperator = "NOT" };
            Assert.True(InvokeEvaluateQueryOnText("hello world", node));
        }

        [Fact]
        public void EvaluateQueryOnText_OrGroup()
        {
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Value = "motor" },
                    new FilterNode { Type = NodeType.Condition, Value = "sensor" }
                }
            };

            Assert.True(InvokeEvaluateQueryOnText("motor running", group));
            Assert.True(InvokeEvaluateQueryOnText("sensor active", group));
            Assert.False(InvokeEvaluateQueryOnText("pump running", group));
        }

        [Fact]
        public void EvaluateQueryOnText_AndGroup()
        {
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Value = "motor" },
                    new FilterNode { Type = NodeType.Condition, Value = "started" }
                }
            };

            Assert.True(InvokeEvaluateQueryOnText("motor started successfully", group));
            Assert.False(InvokeEvaluateQueryOnText("motor stopped", group));
        }

        [Fact]
        public void EvaluateQueryOnText_GroupWithNullChildren_ReturnsFalse()
        {
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = null!
            };

            Assert.False(InvokeEvaluateQueryOnText("test", group));
        }

        // ====================================================================
        //  SearchLogCollection (private) via reflection
        // ====================================================================

        private List<GrepResult> InvokeSearchLogCollection(
            IEnumerable<LogEntry> logs, Func<string, bool> predicate,
            bool msg, bool exc, bool meth, bool data,
            string path, string type, string name, int idx, CancellationToken ct)
        {
            var method = typeof(GlobalGrepService).GetMethod("SearchLogCollection",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (List<GrepResult>)method!.Invoke(_service, new object[]
            {
                logs, predicate, msg, exc, meth, data, path, type, name, idx, ct
            })!;
        }

        [Fact]
        public void SearchLogCollection_MatchesMessageField()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry("Motor started"),
                MakeEntry("Sensor check"),
                MakeEntry("Motor stopped")
            };

            Func<string, bool> predicate = s => s.Contains("Motor", StringComparison.OrdinalIgnoreCase);

            var result = InvokeSearchLogCollection(logs, predicate,
                true, false, false, false,
                @"C:\test\file.zip", "PLC", "test", 0, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal("PLC", r.LogType));
        }

        [Fact]
        public void SearchLogCollection_MatchesExceptionField()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry("msg1", exception: "NullReferenceException"),
                MakeEntry("msg2", exception: "")
            };

            Func<string, bool> predicate = s => s.Contains("NullReference", StringComparison.OrdinalIgnoreCase);

            var result = InvokeSearchLogCollection(logs, predicate,
                false, true, false, false,
                @"C:\test\file.zip", "PLC", "test", 0, CancellationToken.None);

            Assert.Single(result);
        }

        [Fact]
        public void SearchLogCollection_MatchesMethodField()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry("msg1", method: "InitializeComponent"),
                MakeEntry("msg2", method: "Dispose")
            };

            Func<string, bool> predicate = s => s.Contains("Initialize", StringComparison.OrdinalIgnoreCase);

            var result = InvokeSearchLogCollection(logs, predicate,
                false, false, true, false,
                @"C:\test\file.zip", "PLC", "test", 0, CancellationToken.None);

            Assert.Single(result);
        }

        [Fact]
        public void SearchLogCollection_MatchesDataField()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry("msg1", data: "key=value123"),
                MakeEntry("msg2", data: "other=stuff")
            };

            Func<string, bool> predicate = s => s.Contains("value123", StringComparison.OrdinalIgnoreCase);

            var result = InvokeSearchLogCollection(logs, predicate,
                false, false, false, true,
                @"C:\test\file.zip", "PLC", "test", 0, CancellationToken.None);

            Assert.Single(result);
        }

        [Fact]
        public void SearchLogCollection_Cancellation_StopsEarly()
        {
            var logs = Enumerable.Range(0, 1000)
                .Select(i => MakeEntry($"entry {i}")).ToList();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<string, bool> predicate = _ => true;

            var result = InvokeSearchLogCollection(logs, predicate,
                true, false, false, false,
                @"C:\test\file.zip", "PLC", "test", 0, cts.Token);

            Assert.True(result.Count < 1000);
        }

        [Fact]
        public void SearchLogCollection_NoFieldEnabled_NoResults()
        {
            var logs = new List<LogEntry>
            {
                MakeEntry("Motor started", method: "Init", data: "key=val", exception: "Ex")
            };

            Func<string, bool> predicate = _ => true;

            var result = InvokeSearchLogCollection(logs, predicate,
                false, false, false, false,
                @"C:\test\file.zip", "PLC", "test", 0, CancellationToken.None);

            Assert.Empty(result);
        }

        // ====================================================================
        //  IsLineMatch (private) via reflection
        // ====================================================================

        private bool InvokeIsLineMatch(string line, string query,
            System.Text.RegularExpressions.Regex? regex, bool useRegex)
        {
            var method = typeof(GlobalGrepService).GetMethod("IsLineMatch",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (bool)method!.Invoke(_service, new object?[] { line, query, regex, useRegex })!;
        }

        [Fact]
        public void IsLineMatch_SimpleString_CaseInsensitive()
        {
            Assert.True(InvokeIsLineMatch("Hello World", "hello", null, false));
            Assert.False(InvokeIsLineMatch("Goodbye", "hello", null, false));
        }

        [Fact]
        public void IsLineMatch_Regex_Matches()
        {
            var regex = new System.Text.RegularExpressions.Regex(@"\d{3}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Assert.True(InvokeIsLineMatch("code 123", @"\d{3}", regex, true));
            Assert.False(InvokeIsLineMatch("code 12", @"\d{3}", regex, true));
        }

        [Fact]
        public void IsLineMatch_BooleanQuery_And()
        {
            Assert.True(InvokeIsLineMatch("Motor started successfully", "Motor AND started", null, false));
            Assert.False(InvokeIsLineMatch("Motor stopped", "Motor AND started", null, false));
        }

        [Fact]
        public void IsLineMatch_BooleanQuery_Or()
        {
            Assert.True(InvokeIsLineMatch("Motor active", "Motor OR Sensor", null, false));
            Assert.True(InvokeIsLineMatch("Sensor active", "Motor OR Sensor", null, false));
            Assert.False(InvokeIsLineMatch("Pump active", "Motor OR Sensor", null, false));
        }

        [Fact]
        public void IsLineMatch_RegexWithNullRegex_FallsBackToContains()
        {
            // useRegex=true but regex=null => falls through to boolean/contains check
            Assert.True(InvokeIsLineMatch("Hello World", "hello", null, true));
            Assert.False(InvokeIsLineMatch("Hello World", "missing", null, true));
        }
    }
}
