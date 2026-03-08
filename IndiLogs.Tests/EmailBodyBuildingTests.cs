using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class EmailBodyBuildingTests
    {
        private static ScheduledSearch CreateSchedule(string name = "TestSearch", ScanMode mode = ScanMode.SearchOnly)
        {
            return new ScheduledSearch { Name = name, ScanMode = mode };
        }

        // ── BuildPlainTextBody - Search Only ──

        [Fact]
        public void BuildPlainTextBody_NoResultsNoStats_ContainsReportHeader()
        {
            var schedule = CreateSchedule();
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, null);

            Assert.Contains("IndiLogs Search Report", body);
            Assert.Contains("TestSearch", body);
            Assert.Contains("SearchOnly", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithResults_ContainsMatchCount()
        {
            var schedule = CreateSchedule();
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "Error occurred", LogType = "PLC", LocationName = "Loc1",
                    ReferencedLogEntry = new LogEntry { Level = "Error" } },
                new GrepResult { PreviewText = "Another error", LogType = "APP", LocationName = "Loc1",
                    ReferencedLogEntry = new LogEntry { Level = "Warning" } },
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);

            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("Total matches: 2", body);
            Assert.Contains("By Location:", body);
            Assert.Contains("Loc1", body);
            Assert.Contains("By Log Level:", body);
        }

        [Fact]
        public void BuildPlainTextBody_WithResults_ShowsPreview()
        {
            var schedule = CreateSchedule();
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "Something happened", LogType = "PLC", LocationName = "Site1" },
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("Something happened", body);
        }

        [Fact]
        public void BuildPlainTextBody_MoreThan20Results_ShowsAndMore()
        {
            var schedule = CreateSchedule();
            var results = new List<GrepResult>();
            for (int i = 0; i < 25; i++)
                results.Add(new GrepResult { PreviewText = $"Result {i}", LogType = "PLC", LocationName = "Loc" });

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("and 5 more", body);
        }

        // ── BuildPlainTextBody - Statistics Only ──

        [Fact]
        public void BuildPlainTextBody_StatsOnly_ContainsStatsHeader()
        {
            var schedule = CreateSchedule(mode: ScanMode.StatisticsOnly);
            var stats = new LogStatisticsResult
            {
                TotalPlcLogs = 1000,
                TotalAppLogs = 500,
                TotalPlcErrors = 10,
                TotalAppErrors = 5,
                EarliestTimestamp = new DateTime(2024, 1, 15, 10, 0, 0),
                LatestTimestamp = new DateTime(2024, 1, 15, 12, 0, 0)
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);

            Assert.Contains("Statistics Report", body);
            Assert.Contains("PLC Logs:   1,000", body);
            Assert.Contains("APP Logs:   500", body);
            Assert.Contains("PLC Errors: 10", body);
            Assert.Contains("APP Errors: 5", body);
            Assert.Contains("Time span:", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithPlcTopErrors()
        {
            var schedule = CreateSchedule(mode: ScanMode.StatisticsOnly);
            var stats = new LogStatisticsResult
            {
                PlcTopErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "DiskError", Message = "Disk full", Count = 42 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("PLC TOP ERRORS", body);
            Assert.Contains("42", body);
            Assert.Contains("Disk full", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithThreadLoad()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                PlcThreadLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "Manager", Count = 500, Percentage = 45.2 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("PLC THREAD LOAD", body);
            Assert.Contains("Manager: 500", body);
            Assert.Contains("45.2%", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppLoggerErrors()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                AppLoggerErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "App.DB", Message = "Connection failed", Count = 7 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP ERRORS BY LOGGER", body);
            Assert.Contains("Connection failed", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithAppLoggerLoad()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                AppLoggerLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "Service.API", Count = 200, Percentage = 30.0 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP LOGGER LOAD", body);
            Assert.Contains("Service.API", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithMethodErrors()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                AppMethodErrors = new List<ErrorStat>
                {
                    new ErrorStat { Name = "Initialize", Message = "Init failed", Count = 3 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP ERRORS BY METHOD", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithMethodLoad()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                AppMethodLoad = new List<LoadStat>
                {
                    new LoadStat { Name = "ProcessData", Count = 100, Percentage = 20.0 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("APP METHOD LOAD", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithErrorsBySource()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                ErrorsBySource = new List<StatCount>
                {
                    new StatCount { Name = "PLC", Count = 15 },
                    new StatCount { Name = "APP", Count = 8 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("ERRORS BY SOURCE", body);
            Assert.Contains("PLC: 15", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithErrorsByState()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                ErrorsByState = new List<StatCount>
                {
                    new StatCount { Name = "Printing", Count = 5 }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("ERRORS BY PRINTER STATE", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithStateEntries()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                StateEntries = new List<StateEntry>
                {
                    new StateEntry
                    {
                        StateName = "Printing",
                        StartTime = new DateTime(2024, 1, 15, 10, 0, 0),
                        EndTime = new DateTime(2024, 1, 15, 10, 5, 0)
                    },
                    new StateEntry
                    {
                        StateName = "Idle",
                        StartTime = new DateTime(2024, 1, 15, 10, 5, 0),
                        EndTime = new DateTime(2024, 1, 15, 10, 10, 0)
                    }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("STATE DURATION SUMMARY", body);
            Assert.Contains("Printing", body);
        }

        [Fact]
        public void BuildPlainTextBody_StatsWithGaps()
        {
            var schedule = CreateSchedule();
            var stats = new LogStatisticsResult
            {
                PlcGaps = new List<GapInfo>
                {
                    new GapInfo
                    {
                        StartTime = new DateTime(2024, 1, 15, 10, 0, 0),
                        EndTime = new DateTime(2024, 1, 15, 10, 0, 5),
                        DurationText = "5s"
                    }
                },
                AppGaps = new List<GapInfo>
                {
                    new GapInfo
                    {
                        StartTime = new DateTime(2024, 1, 15, 11, 0, 0),
                        EndTime = new DateTime(2024, 1, 15, 11, 0, 3),
                        DurationText = "3s"
                    }
                }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("GAP ANALYSIS", body);
            Assert.Contains("PLC: 1 gap(s)", body);
            Assert.Contains("APP: 1 gap(s)", body);
        }

        // ── BuildPlainTextBody - Combined ──

        [Fact]
        public void BuildPlainTextBody_SearchAndStats_ContainsBothSections()
        {
            var schedule = CreateSchedule(mode: ScanMode.SearchAndStatistics);
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "Match", LogType = "PLC", LocationName = "Site" }
            };
            var stats = new LogStatisticsResult { TotalPlcLogs = 100 };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, stats);

            Assert.Contains("Search & Statistics Report", body);
            Assert.Contains("SEARCH RESULTS", body);
            Assert.Contains("LOG STATISTICS OVERVIEW", body);
        }

        [Fact]
        public void BuildPlainTextBody_AlwaysContainsFooter()
        {
            var schedule = CreateSchedule();
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, null);
            Assert.Contains("IndiLogs 3.0 scheduled scan", body);
        }

        // ── Truncate (private static) ──

        [Fact]
        public void BuildPlainTextBody_LongPreviewText_IsTruncated()
        {
            var schedule = CreateSchedule();
            var longMessage = new string('x', 200);
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = longMessage, LogType = "PLC", LocationName = "Loc" }
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("...", body);
        }

        [Fact]
        public void BuildPlainTextBody_ResultsByLevel_GroupedCorrectly()
        {
            var schedule = CreateSchedule();
            var results = new List<GrepResult>
            {
                new GrepResult { PreviewText = "e1", LogType = "PLC", LocationName = "A",
                    ReferencedLogEntry = new LogEntry { Level = "Error" } },
                new GrepResult { PreviewText = "e2", LogType = "PLC", LocationName = "A",
                    ReferencedLogEntry = new LogEntry { Level = "Error" } },
                new GrepResult { PreviewText = "w1", LogType = "PLC", LocationName = "B",
                    ReferencedLogEntry = new LogEntry { Level = "Warning" } },
            };

            var body = EmailNotificationService.BuildPlainTextBody(schedule, results, null);
            Assert.Contains("Error: 2", body);
            Assert.Contains("Warning: 1", body);
        }

        [Fact]
        public void BuildPlainTextBody_ManyGaps_ShowsAndMore()
        {
            var schedule = CreateSchedule();
            var gaps = new List<GapInfo>();
            for (int i = 0; i < 15; i++)
                gaps.Add(new GapInfo
                {
                    StartTime = new DateTime(2024, 1, 15, 10, i, 0),
                    EndTime = new DateTime(2024, 1, 15, 10, i, 5),
                    DurationText = "5s"
                });

            var stats = new LogStatisticsResult { PlcGaps = gaps };
            var body = EmailNotificationService.BuildPlainTextBody(schedule, null, stats);
            Assert.Contains("and 5 more", body);
        }
    }
}
