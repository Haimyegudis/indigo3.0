using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Models.Grep
{
    /// <summary>
    /// Defines an automated search that runs on a schedule.
    /// </summary>
    public class ScheduledSearch
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";

        /// <summary>
        /// The search criteria to execute.
        /// </summary>
        public SearchCriteria? Criteria { get; set; }

        public ScheduleType ScheduleType { get; set; } = ScheduleType.Once;

        /// <summary>
        /// Specific date to run (for Once schedule type).
        /// </summary>
        public DateTime? RunDate { get; set; }

        /// <summary>
        /// Time of day to run (for Once/Daily/Weekly schedules).
        /// </summary>
        public TimeSpan RunTime { get; set; }

        /// <summary>
        /// Days of week to run (for Weekly schedule).
        /// </summary>
        public HashSet<DayOfWeek> RunDays { get; set; } = new HashSet<DayOfWeek>();

        /// <summary>
        /// Repeat interval value (unit determined by IntervalUnit).
        /// </summary>
        public int RepeatIntervalValue { get; set; } = 1;

        /// <summary>
        /// Unit for the repeat interval.
        /// </summary>
        public IntervalUnit IntervalUnit { get; set; } = IntervalUnit.Hours;

        /// <summary>
        /// Interval in minutes. Getter computes from RepeatIntervalValue + IntervalUnit.
        /// Setter handles backward compat: old JSON had only RepeatIntervalMinutes.
        /// </summary>
        public int RepeatIntervalMinutes
        {
            get
            {
                switch (IntervalUnit)
                {
                    case IntervalUnit.Hours: return RepeatIntervalValue * 60;
                    case IntervalUnit.Days: return RepeatIntervalValue * 1440;
                    default: return RepeatIntervalValue;
                }
            }
            set
            {
                // Only apply if RepeatIntervalValue hasn't been explicitly set (backward compat)
                if (RepeatIntervalValue <= 1 && IntervalUnit == IntervalUnit.Hours && value != 60)
                {
                    if (value >= 1440 && value % 1440 == 0) { RepeatIntervalValue = value / 1440; IntervalUnit = IntervalUnit.Days; }
                    else if (value >= 60 && value % 60 == 0) { RepeatIntervalValue = value / 60; IntervalUnit = IntervalUnit.Hours; }
                    else { RepeatIntervalValue = value; IntervalUnit = IntervalUnit.Minutes; }
                }
            }
        }

        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Directory where results are saved as JSON/CSV.
        /// </summary>
        public string? OutputDirectory { get; set; }

        /// <summary>
        /// What the scheduled scan does: search only, statistics only, or both.
        /// </summary>
        public ScanMode ScanMode { get; set; } = ScanMode.SearchOnly;

        /// <summary>
        /// Email notification configuration. Null or IsEnabled=false means no email.
        /// </summary>
        public EmailNotificationConfig? EmailConfig { get; set; }

        public DateTime? LastRunTime { get; set; }
        public string? LastRunStatus { get; set; }

        /// <summary>
        /// Display-only: summary of what this schedule searches for.
        /// </summary>
        [JsonIgnore]
        public string SearchSummary
        {
            get
            {
                if (Criteria?.Groups == null || Criteria.Groups.Count == 0)
                    return "(no criteria)";

                var parts = new List<string>();
                foreach (var group in Criteria.Groups)
                {
                    if (group.Conditions == null) continue;
                    foreach (var c in group.Conditions)
                    {
                        if (string.IsNullOrWhiteSpace(c.Value)) continue;
                        parts.Add($"{c.Field}:{c.Value}");
                    }
                }

                string logType = (Criteria.SearchPLC && Criteria.SearchAPP) ? ""
                    : Criteria.SearchPLC ? " [PLC]"
                    : Criteria.SearchAPP ? " [APP]"
                    : " [None]";

                string modePrefix = ScanMode == ScanMode.StatisticsOnly ? "[Stats] "
                    : ScanMode == ScanMode.SearchAndStatistics ? "[Search+Stats] "
                    : "";

                if (ScanMode == ScanMode.StatisticsOnly)
                    return modePrefix + "All logs" + logType;

                return parts.Count > 0
                    ? modePrefix + string.Join(", ", parts) + logType
                    : "(no criteria)";
            }
        }
    }

    public enum ScheduleType
    {
        Once,
        Daily,
        Weekly,
        Interval
    }

    public enum ScanMode
    {
        SearchOnly,
        StatisticsOnly,
        SearchAndStatistics
    }

    public enum IntervalUnit
    {
        Minutes,
        Hours,
        Days
    }
}
