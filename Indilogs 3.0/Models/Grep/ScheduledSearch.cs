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
        public string Name { get; set; }

        /// <summary>
        /// The search criteria to execute.
        /// </summary>
        public SearchCriteria Criteria { get; set; }

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
        public List<DayOfWeek> RunDays { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// Repeat interval in minutes (for Interval schedule).
        /// </summary>
        public int RepeatIntervalMinutes { get; set; } = 60;

        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Directory where results are saved as JSON/CSV.
        /// </summary>
        public string OutputDirectory { get; set; }

        public DateTime? LastRunTime { get; set; }
        public string LastRunStatus { get; set; }

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

                return parts.Count > 0
                    ? string.Join(", ", parts) + logType
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
}
