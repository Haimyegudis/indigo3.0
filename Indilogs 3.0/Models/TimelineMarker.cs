using System;
using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public enum TimelineMarkerType { Error, Event }

    public class TimelineMarker
    {
        public TimelineMarkerType Type { get; set; }
        public DateTime Time { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public Color Color { get; set; }

        // If this is an error - the original log will be here. If this is an event - it will be null.
        public LogEntry OriginalLog { get; set; }
    }
}