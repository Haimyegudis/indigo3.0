using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IndiLogs_3._0.Models.Analysis
{
    public enum AnalysisStatus { Success, Warning, Failure }

    /// <summary>A single clickable error item in the FAILURES report — bundles display text with its source LogEntry.</summary>
    public class FailureErrorItem
    {
        public string   DisplayText     { get; set; }
        /// <summary>The log row this item represents. Null when there is no matching PLC row to jump to.</summary>
        public LogEntry LogEntry        { get; set; }
        public bool     IsNavigable     => LogEntry != null;
    }

    public class AnalysisResult
    {
        public string ProcessName { get; set; }
        public AnalysisStatus Status { get; set; }
        public string Summary { get; set; }
        public DateTime FailureTime { get; set; }   // used for chronological sort (earliest first)
        public List<AnalysisStep> Steps { get; set; } = new List<AnalysisStep>();
        public List<string> ErrorsFound { get; set; } = new List<string>();   // kept for back-compat
        /// <summary>Clickable error items — replaces the plain ErrorsFound strings for the UI.</summary>
        public List<FailureErrorItem> ErrorItems { get; set; } = new List<FailureErrorItem>();
        /// <summary>The exact log message that triggered this failure (PLC_FAILURE_STATE_CHANGE event).</summary>
        public string CriticalEventMessage { get; set; }
        public bool HasCriticalEvent => !string.IsNullOrEmpty(CriticalEventMessage);

        // UI helper
        public Brush StatusColor => Status == AnalysisStatus.Success ? Brushes.Green :
                                    Status == AnalysisStatus.Warning ? Brushes.Orange : Brushes.Red;
    }

    public class AnalysisStep
    {
        public string StepName { get; set; }     // e.g.: "Init Motors"
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationMs => (EndTime - StartTime).TotalMilliseconds;
        public string Status { get; set; }       // "OK", "TIMEOUT", "ERROR"
        public LogEntry StartLog { get; set; }   // Points to the log line that started the step
        public LogEntry EndLog { get; set; }     // Points to the log line that ended the step
    }
}