using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class ScheduledSearchModelTests
    {
        // ── RepeatIntervalMinutes getter ──

        [Fact]
        public void RepeatIntervalMinutes_Hours_ReturnsMultipliedBy60()
        {
            var schedule = new ScheduledSearch
            {
                RepeatIntervalValue = 2,
                IntervalUnit = IntervalUnit.Hours
            };
            Assert.Equal(120, schedule.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_Days_ReturnsMultipliedBy1440()
        {
            var schedule = new ScheduledSearch
            {
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Days
            };
            Assert.Equal(1440, schedule.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_Minutes_ReturnsRawValue()
        {
            var schedule = new ScheduledSearch
            {
                RepeatIntervalValue = 45,
                IntervalUnit = IntervalUnit.Minutes
            };
            Assert.Equal(45, schedule.RepeatIntervalMinutes);
        }

        // ── RepeatIntervalMinutes setter (backward compat) ──

        [Fact]
        public void RepeatIntervalMinutes_Set1440_ParsesAsDays()
        {
            var schedule = new ScheduledSearch();
            schedule.RepeatIntervalMinutes = 1440;
            Assert.Equal(1, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Days, schedule.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_Set120_ParsesAsHours()
        {
            var schedule = new ScheduledSearch();
            schedule.RepeatIntervalMinutes = 120;
            Assert.Equal(2, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Hours, schedule.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_Set45_ParsesAsMinutes()
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
            var schedule = new ScheduledSearch();
            Assert.Equal("(no criteria)", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_WithConditions_FormatsCorrectly()
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
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:error", schedule.SearchSummary);
            Assert.Contains("[PLC]", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_StatisticsOnly_ShowsStatsPrefix()
        {
            var schedule = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
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
            Assert.StartsWith("[Stats]", schedule.SearchSummary);
        }

        [Fact]
        public void SearchSummary_BothLogTypes_NoSuffix()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
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
            Assert.DoesNotContain("[PLC]", schedule.SearchSummary);
            Assert.DoesNotContain("[APP]", schedule.SearchSummary);
        }

        // ── Default values ──

        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var schedule = new ScheduledSearch();
            Assert.NotEqual(Guid.Empty, schedule.Id);
            Assert.Equal("", schedule.Name);
            Assert.True(schedule.IsEnabled);
            Assert.Equal(ScheduleType.Once, schedule.ScheduleType);
            Assert.Equal(ScanMode.SearchOnly, schedule.ScanMode);
            Assert.Equal(1, schedule.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Hours, schedule.IntervalUnit);
            Assert.Empty(schedule.RunDays);
        }
    }
}
