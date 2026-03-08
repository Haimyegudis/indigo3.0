using IndiLogs_3._0.Models.Grep;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace IndiLogs.Tests
{
    public class GrepModelExtendedTests
    {
        // ── SearchCriteria defaults ──

        [Fact]
        public void SearchCriteria_Defaults()
        {
            var c = new SearchCriteria();
            Assert.Equal("", c.Name);
            Assert.Empty(c.Groups);
            Assert.Equal(LogicalGroupOperator.And, c.GroupOperator);
            Assert.Null(c.FileTimeFilter);
            Assert.Null(c.ResultTimeFilter);
            Assert.Empty(c.LocationIds);
            Assert.True(c.SearchPLC);
            Assert.True(c.SearchAPP);
        }

        // ── SearchConditionGroup defaults ──

        [Fact]
        public void SearchConditionGroup_Defaults()
        {
            var g = new SearchConditionGroup();
            Assert.Empty(g.Conditions);
            Assert.Equal(ConditionOperator.And, g.Operator);
        }

        // ── SearchCondition defaults ──

        [Fact]
        public void SearchCondition_Defaults()
        {
            var c = new SearchCondition();
            Assert.Equal(SearchField.Any, c.Field);
            Assert.Equal(SearchOperator.Contains, c.Operator);
            Assert.Equal("", c.Value);
            Assert.False(c.Negate);
        }

        // ── SearchCondition CompiledRegex ──

        [Fact]
        public void SearchCondition_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var c = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(c.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var c = new SearchCondition { Operator = SearchOperator.Regex, Value = "" };
            Assert.Null(c.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_ValidPattern_ReturnsRegex()
        {
            var c = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            var rx = c.CompiledRegex;
            Assert.NotNull(rx);
            Assert.True(rx!.IsMatch("123"));
        }

        [Fact]
        public void SearchCondition_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var c = new SearchCondition { Operator = SearchOperator.Regex, Value = "[invalid" };
            Assert.Null(c.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CachesResult()
        {
            var c = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\w+" };
            var rx1 = c.CompiledRegex;
            var rx2 = c.CompiledRegex;
            Assert.Same(rx1, rx2);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_RecompilesOnValueChange()
        {
            var c = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            var rx1 = c.CompiledRegex;
            c.Value = @"\w+";
            var rx2 = c.CompiledRegex;
            Assert.NotSame(rx1, rx2);
        }

        // ── TimeRangeFilter ──

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var f = new TimeRangeFilter { From = DateTime.Now, To = DateTime.Now.AddHours(1) };
            var resolved = f.Resolve();
            Assert.Same(f, resolved);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours_SetsFrom()
        {
            var f = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var before = DateTime.Now.AddHours(-24);
            var resolved = f.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True(resolved.From >= before.AddSeconds(-1));
            Assert.Null(resolved.To);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek_SetsFrom()
        {
            var f = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var before = DateTime.Now.AddDays(-7);
            var resolved = f.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True(resolved.From >= before.AddSeconds(-1));
            Assert.Null(resolved.To);
        }

        // ── ScheduledSearch defaults ──

        [Fact]
        public void ScheduledSearch_Defaults()
        {
            var s = new ScheduledSearch();
            Assert.NotEqual(Guid.Empty, s.Id);
            Assert.Equal("", s.Name);
            Assert.Null(s.Criteria);
            Assert.Equal(ScheduleType.Once, s.ScheduleType);
            Assert.Null(s.RunDate);
            Assert.Empty(s.RunDays);
            Assert.Equal(1, s.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Hours, s.IntervalUnit);
            Assert.True(s.IsEnabled);
            Assert.Null(s.OutputDirectory);
            Assert.Equal(ScanMode.SearchOnly, s.ScanMode);
            Assert.Null(s.EmailConfig);
            Assert.Null(s.LastRunTime);
            Assert.Null(s.LastRunStatus);
        }

        // ── RepeatIntervalMinutes getter ──

        [Fact]
        public void RepeatIntervalMinutes_Hours_Returns60PerHour()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 2, IntervalUnit = IntervalUnit.Hours };
            Assert.Equal(120, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_Days_Returns1440PerDay()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 1, IntervalUnit = IntervalUnit.Days };
            Assert.Equal(1440, s.RepeatIntervalMinutes);
        }

        [Fact]
        public void RepeatIntervalMinutes_Minutes_ReturnsDirectValue()
        {
            var s = new ScheduledSearch { RepeatIntervalValue = 30, IntervalUnit = IntervalUnit.Minutes };
            Assert.Equal(30, s.RepeatIntervalMinutes);
        }

        // ── RepeatIntervalMinutes setter (backward compat) ──

        [Fact]
        public void RepeatIntervalMinutes_SetDays_ParsesCorrectly()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 2880; // 2 days
            Assert.Equal(2, s.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Days, s.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_SetHours_ParsesCorrectly()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 120; // 2 hours
            Assert.Equal(2, s.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Hours, s.IntervalUnit);
        }

        [Fact]
        public void RepeatIntervalMinutes_SetMinutes_ParsesCorrectly()
        {
            var s = new ScheduledSearch();
            s.RepeatIntervalMinutes = 45; // 45 min (not divisible by 60)
            Assert.Equal(45, s.RepeatIntervalValue);
            Assert.Equal(IntervalUnit.Minutes, s.IntervalUnit);
        }

        // ── SearchSummary ──

        [Fact]
        public void SearchSummary_NoCriteria_ReturnsNoCriteria()
        {
            var s = new ScheduledSearch();
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_EmptyGroups_ReturnsNoCriteria()
        {
            var s = new ScheduledSearch { Criteria = new SearchCriteria() };
            Assert.Equal("(no criteria)", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_WithCondition_ShowsFieldAndValue()
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
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:error", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_PLCOnly_ShowsPLCTag()
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
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[PLC]", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_APPOnly_ShowsAPPTag()
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
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[APP]", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_NeitherPLCNorAPP_ShowsNoneTag()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = false,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[None]", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_BothPLCAndAPP_NoTag()
        {
            var s = new ScheduledSearch
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
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.DoesNotContain("[PLC]", s.SearchSummary);
            Assert.DoesNotContain("[APP]", s.SearchSummary);
            Assert.DoesNotContain("[None]", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_StatisticsOnly_ShowsStatsPrefix()
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
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.StartsWith("[Stats] All logs", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_SearchAndStats_ShowsPrefix()
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
                                new SearchCondition { Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.StartsWith("[Search+Stats]", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_MultipleConditions_JoinedByComma()
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
                                new SearchCondition { Field = SearchField.Message, Value = "error" },
                                new SearchCondition { Field = SearchField.Level, Value = "FATAL" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:error", s.SearchSummary);
            Assert.Contains("Level:FATAL", s.SearchSummary);
        }

        [Fact]
        public void SearchSummary_EmptyValues_Skipped()
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
                                new SearchCondition { Value = "" },
                                new SearchCondition { Value = "real" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Any:real", s.SearchSummary);
            Assert.DoesNotContain("Any:", s.SearchSummary.Replace("Any:real", ""));
        }

        // ── SearchLocation ──

        [Fact]
        public void SearchLocation_Defaults()
        {
            var l = new SearchLocation();
            Assert.NotEqual(Guid.Empty, l.Id);
            Assert.True(l.IsActive);
            Assert.Equal(ConnectionStatus.Unknown, l.ConnectionStatus);
        }

        [Fact]
        public void SearchLocation_PropertyChanged()
        {
            var l = new SearchLocation();
            var changed = new List<string>();
            l.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            l.Name = "Test";
            l.Address = "10.0.0.1";
            l.BasePath = @"\\server\share";
            l.IsActive = false;
            l.ConnectionStatus = ConnectionStatus.Connected;

            Assert.Contains("Name", changed);
            Assert.Contains("Address", changed);
            Assert.Contains("BasePath", changed);
            Assert.Contains("IsActive", changed);
            Assert.Contains("ConnectionStatus", changed);
        }

        // ── EmailNotificationConfig ──

        [Fact]
        public void EmailNotificationConfig_Defaults()
        {
            var e = new EmailNotificationConfig();
            Assert.False(e.IsEnabled);
            Assert.Empty(e.Recipients);
            Assert.Equal(EmailTiming.Immediately, e.Timing);
            Assert.Null(e.CustomSubject);
        }
    }
}
