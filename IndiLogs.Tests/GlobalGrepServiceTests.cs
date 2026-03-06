using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class GlobalGrepServiceTests
    {
        private readonly GlobalGrepService _service = new GlobalGrepService();

        private LogEntry MakeEntry(
            string message = "test message",
            string level = "Info",
            string logger = "MyApp.Service",
            string threadName = "Main",
            string method = "DoWork",
            string? data = null,
            string? exception = null)
        {
            return new LogEntry
            {
                Message = message,
                Level = level,
                Logger = logger,
                ThreadName = threadName,
                Method = method,
                Data = data ?? "",
                Exception = exception ?? ""
            };
        }

        private SearchCondition Cond(
            SearchField field = SearchField.Message,
            SearchOperator op = SearchOperator.Contains,
            string value = "test",
            bool negate = false)
        {
            return new SearchCondition { Field = field, Operator = op, Value = value, Negate = negate };
        }

        private SearchCriteria SingleCriteria(SearchCondition condition, ConditionOperator groupOp = ConditionOperator.And)
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

        // ── EvaluateCondition ──

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
        public void EvaluateCondition_NullField_NoMatch()
        {
            var entry = MakeEntry(data: null);
            var cond = Cond(SearchField.Data, SearchOperator.Contains, "test");
            Assert.False(_service.EvaluateCondition(entry, cond));
        }

        // ── EvaluateGroup ──

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

        // ── EvaluateCriteria ──

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
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "error")
                        }
                    },
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
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
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            Cond(SearchField.Message, SearchOperator.Contains, "missing")
                        }
                    },
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
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
        public void EvaluateCriteria_NoGroups_ReturnsTrue()
        {
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };
            Assert.True(_service.EvaluateCriteria(MakeEntry(), criteria));
        }

        // ── DetermineMatchedFields ──

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
    }
}
