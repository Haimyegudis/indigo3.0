using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Represents a Case File - a lightweight metadata container for investigations.
    /// Does not contain actual log data, only references and annotations.
    /// </summary>
    public class CaseFile
    {
        public CaseMetadata Meta { get; set; }
        public List<CaseResource> Resources { get; set; }
        public CaseViewState ViewState { get; set; }
        public List<LogAnnotation> Annotations { get; set; }

        // Coloring rules for main and app logs
        public List<ColoringCondition> MainColoringRules { get; set; }
        public List<ColoringCondition> AppColoringRules { get; set; }

        public CaseFile()
        {
            Meta = new CaseMetadata();
            Resources = new List<CaseResource>();
            ViewState = new CaseViewState();
            Annotations = new List<LogAnnotation>();
            MainColoringRules = new List<ColoringCondition>();
            AppColoringRules = new List<ColoringCondition>();
        }
    }

    /// <summary>
    /// Metadata about the case investigation
    /// </summary>
    public class CaseMetadata
    {
        public string Version { get; set; } = "1.0";
        public string Author { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Reference to an external log file
    /// </summary>
    public class CaseResource
    {
        public string FileName { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } // Optional: partial hash for verification
    }

    /// <summary>
    /// Saved view state including filters and search
    /// </summary>
    public class CaseViewState
    {
        public FilterNode ActiveFilters { get; set; }
        public string QuickSearchText { get; set; }
        public string SelectedTab { get; set; }
        public List<string> ActiveThreadFilters { get; set; }
        public List<string> NegativeFilters { get; set; }

        public CaseViewState()
        {
            ActiveThreadFilters = new List<string>();
            NegativeFilters = new List<string>();
        }
    }

    /// <summary>
    /// Annotation (comment) on a specific log entry.
    /// Uses "soft linking" - identifies log by timestamp, logger, thread instead of index.
    /// </summary>
    public class LogAnnotation
    {
        public LogTarget TargetLog { get; set; }
        public string Content { get; set; }
        public string Color { get; set; } = "#FFFF00"; // Default yellow
        public DateTime CreatedAt { get; set; }
        public string Author { get; set; }

        public LogAnnotation()
        {
            CreatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// Soft link to identify a log entry across different sessions
    /// </summary>
    public class LogTarget
    {
        public DateTime Timestamp { get; set; }
        public string Logger { get; set; }
        public string Thread { get; set; }
        public string Snippet { get; set; } // First 100 chars of message for verification
        public string Level { get; set; }
    }
}
