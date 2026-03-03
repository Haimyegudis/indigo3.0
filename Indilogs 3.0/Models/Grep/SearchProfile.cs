using System.Collections.Generic;

namespace IndiLogs_3._0.Models.Grep
{
    /// <summary>
    /// Top-level profile that bundles locations, criteria, and schedules into a single
    /// JSON-serializable configuration file.
    /// </summary>
    public class SearchProfile
    {
        public string Name { get; set; } = "";
        public List<SearchLocation> Locations { get; set; } = new List<SearchLocation>();
        public SearchCriteria Criteria { get; set; } = new SearchCriteria();
        public List<ScheduledSearch> Schedules { get; set; } = new List<ScheduledSearch>();
    }
}
