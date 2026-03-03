using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class SearchCriteriaTests
    {
        // ── TimeRangeFilter.Resolve ──

        [Fact]
        public void Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter
            {
                From = new DateTime(2025, 1, 1),
                To = new DateTime(2025, 12, 31),
                RelativeRange = RelativeTimeRange.None
            };
            var resolved = filter.Resolve();
            Assert.Same(filter, resolved); // Returns exact same object
        }

        [Fact]
        public void Resolve_Last24Hours_SetsFromTo24HoursAgo()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var before = DateTime.Now.AddHours(-24);
            var resolved = filter.Resolve();
            var after = DateTime.Now.AddHours(-24);

            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True(resolved.From.Value >= before.AddSeconds(-1));
            Assert.True(resolved.From.Value <= after.AddSeconds(1));
        }

        [Fact]
        public void Resolve_LastWeek_SetsFromTo7DaysAgo()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var before = DateTime.Now.AddDays(-7);
            var resolved = filter.Resolve();

            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True(resolved.From.Value >= before.AddSeconds(-1));
        }

        // ── SearchCriteria defaults ──

        [Fact]
        public void SearchCriteria_DefaultValues()
        {
            var criteria = new SearchCriteria();
            Assert.True(criteria.SearchPLC);
            Assert.True(criteria.SearchAPP);
            Assert.Equal(LogicalGroupOperator.And, criteria.GroupOperator);
            Assert.NotNull(criteria.Groups);
            Assert.Empty(criteria.Groups);
        }

        [Fact]
        public void SearchConditionGroup_DefaultValues()
        {
            var group = new SearchConditionGroup();
            Assert.Equal(ConditionOperator.And, group.Operator);
            Assert.NotNull(group.Conditions);
            Assert.Empty(group.Conditions);
        }

        [Fact]
        public void SearchCondition_DefaultValues()
        {
            var condition = new SearchCondition();
            Assert.Equal(SearchField.Any, condition.Field);
            Assert.Equal(SearchOperator.Contains, condition.Operator);
            Assert.False(condition.Negate);
            Assert.Empty(condition.Value);
        }
    }

    public class ScheduledSearchTests
    {
        // ── RepeatIntervalMinutes getter ──

        [Theory]
        [InlineData(1, IntervalUnit.Hours, 60)]
        [InlineData(2, IntervalUnit.Hours, 120)]
        [InlineData(1, IntervalUnit.Days, 1440)]
        [InlineData(2, IntervalUnit.Days, 2880)]
        [InlineData(30, IntervalUnit.Minutes, 30)]
        [InlineData(90, IntervalUnit.Minutes, 90)]
        public void RepeatIntervalMinutes_Getter_ComputesCorrectly(int value, IntervalUnit unit, int expectedMinutes)
        {
            var schedule = new ScheduledSearch
            {
                RepeatIntervalValue = value,
                IntervalUnit = unit
            };
            Assert.Equal(expectedMinutes, schedule.RepeatIntervalMinutes);
        }

        // ── RepeatIntervalMinutes setter (backward compat) ──

        [Fact]
        public void RepeatIntervalMinutes_Setter_1440_SetsDays()
        {
            var schedule = new ScheduledSearch();
            schedule.RepeatIntervalMinutes = 1440;
            Assert.Equal(1, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Days, schedule.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_Setter_120_SetsHours()
        {
            var schedule = new ScheduledSearch();
            schedule.RepeatIntervalMinutes = 120;
            Assert.Equal(2, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Hours, schedule.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_Setter_45_SetsMinutes()
        {
            var schedule = new ScheduledSearch();
            schedule.RepeatIntervalMinutes = 45;
            Assert.Equal(45, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Minutes, schedule.IntervalUnit);
        }

        // ── SearchSummary ──

        [Fact]
        public void SearchSummary_NoCriteria_ReturnsNoCriteria()
        {
            var schedule = new ScheduledSearch { Criteria = null };
            Assert.Equal("(no criteria)", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_EmptyGroups_ReturnsNoCriteria()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() }
            };
            Assert.Equal("(no criteria)", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_WithConditions_ContainsFieldAndValue()
        {
            var schedule = new ScheduledSearch
            {
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
            Assert.Contains("Message:error", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_StatisticsOnly_HasStatsPrefix()
        {
            var schedule = new ScheduledSearch
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
            Assert.StartsWith("[Stats]", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_PLCOnly_HasPLCTag()
        {
            var schedule = new ScheduledSearch
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
            Assert.Contains("[PLC]", schedule.SearchSummary);
        }

        // ── Default values ──

        [Fact]
        public void ScheduledSearch_DefaultsAreCorrect()
        {
            var schedule = new ScheduledSearch();
            Assert.NotEqual(Guid.Empty, schedule.Id);
            Assert.True(schedule.IsEnabled);
            Assert.Equal(ScanMode.SearchOnly, schedule.ScanMode);
            Assert.Equal(IntervalUnit.Hours, schedule.IntervalUnit);
        }
    }
}
