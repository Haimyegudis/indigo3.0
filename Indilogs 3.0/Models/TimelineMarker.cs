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

        // אם זה שגיאה - יהיה כאן הלוג המקורי. אם זה איוונט - זה יהיה null.
        public LogEntry OriginalLog { get; set; }
    }
}