using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class SearchSchedulerServiceTests
    {
        private readonly SearchSchedulerService _scheduler;

        public SearchSchedulerServiceTests()
        {
            // The constructor loads schedules from disk and creates a timer,
            // but ShouldRun is pure logic that doesn't need infrastructure.
            _scheduler = new SearchSchedulerService(new IndiLogs_3._0.Services.GlobalGrepService(), new SearchLocationService());
        }

        // ── Once schedule ──

        [Fact]
        public void ShouldRun_Once_Disabled_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = false,
                RunDate = DateTime.Today,
                RunTime = TimeSpan.FromHours(10)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(12)));
        }

        [Fact]
        public void ShouldRun_Once_AlreadyRan_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = true,
                RunDate = DateTime.Today,
                RunTime = TimeSpan.FromHours(10),
                LastRunTime = DateTime.Today.AddHours(10)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(12)));
        }

        [Fact]
        public void ShouldRun_Once_WithDate_TargetReached_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = true,
                RunDate = new DateTime(2026, 3, 4),
                RunTime = TimeSpan.FromHours(14)
            };
            Assert.True(_scheduler.ShouldRun(schedule, new DateTime(2026, 3, 4, 14, 0, 0)));
        }

        [Fact]
        public void ShouldRun_Once_WithDate_BeforeTarget_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = true,
                RunDate = new DateTime(2026, 3, 4),
                RunTime = TimeSpan.FromHours(14)
            };
            Assert.False(_scheduler.ShouldRun(schedule, new DateTime(2026, 3, 4, 13, 59, 0)));
        }

        [Fact]
        public void ShouldRun_Once_WithoutDate_TimeReached_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = true,
                RunDate = null,
                RunTime = TimeSpan.FromHours(9)
            };
            Assert.True(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(9)));
        }

        [Fact]
        public void ShouldRun_Once_WithoutDate_BeforeTime_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                IsEnabled = true,
                RunDate = null,
                RunTime = TimeSpan.FromHours(9)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(8)));
        }

        // ── Daily schedule ──

        [Fact]
        public void ShouldRun_Daily_TimeReached_NoPreviousRun_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(8)
            };
            Assert.True(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(8)));
        }

        [Fact]
        public void ShouldRun_Daily_BeforeTime_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(8)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(7)));
        }

        [Fact]
        public void ShouldRun_Daily_AlreadyRanToday_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(8),
                LastRunTime = DateTime.Today.AddHours(8)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(12)));
        }

        [Fact]
        public void ShouldRun_Daily_RanYesterday_TimeReached_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(8),
                LastRunTime = DateTime.Today.AddDays(-1).AddHours(8)
            };
            Assert.True(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(9)));
        }

        // ── Weekly schedule ──

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_TimeReached_ReturnsTrue()
        {
            // Find next Monday
            var monday = DateTime.Today;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);

            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(10),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday }
            };
            Assert.True(_scheduler.ShouldRun(schedule, monday.AddHours(10)));
        }

        [Fact]
        public void ShouldRun_Weekly_WrongDay_ReturnsFalse()
        {
            // Find next Tuesday
            var tuesday = DateTime.Today;
            while (tuesday.DayOfWeek != DayOfWeek.Tuesday) tuesday = tuesday.AddDays(1);

            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(10),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday }
            };
            Assert.False(_scheduler.ShouldRun(schedule, tuesday.AddHours(10)));
        }

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_BeforeTime_ReturnsFalse()
        {
            var monday = DateTime.Today;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);

            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(10),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday }
            };
            Assert.False(_scheduler.ShouldRun(schedule, monday.AddHours(9)));
        }

        [Fact]
        public void ShouldRun_Weekly_AlreadyRanToday_ReturnsFalse()
        {
            var monday = DateTime.Today;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);

            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(10),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = monday.AddHours(10)
            };
            Assert.False(_scheduler.ShouldRun(schedule, monday.AddHours(12)));
        }

        // ── Interval schedule ──

        [Fact]
        public void ShouldRun_Interval_FirstRun_NoStartTime_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                IsEnabled = true,
                RunTime = TimeSpan.Zero, // No start time
                RepeatIntervalValue = 2,
                IntervalUnit = IntervalUnit.Hours
            };
            Assert.True(_scheduler.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_BeforeStartTime_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                IsEnabled = true,
                RunTime = TimeSpan.FromHours(14),
                RepeatIntervalValue = 1,
                IntervalUnit = IntervalUnit.Hours
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Today.AddHours(13)));
        }

        [Fact]
        public void ShouldRun_Interval_IntervalElapsed_ReturnsTrue()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                IsEnabled = true,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = DateTime.Now.AddMinutes(-31)
            };
            Assert.True(_scheduler.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Interval_IntervalNotElapsed_ReturnsFalse()
        {
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                IsEnabled = true,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = DateTime.Now.AddMinutes(-10)
            };
            Assert.False(_scheduler.ShouldRun(schedule, DateTime.Now));
        }
    }
}
