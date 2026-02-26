using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public class TimelineState
    {
        public string Name { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public Color Color { get; set; }
        public bool Success { get; set; }
        public int ErrorCount { get; set; }
        public int EventCount { get; set; }
        public string Status { get; set; } // SUCCESS / FAILED / WARNING / RUNNING

        // רשימת הלוגים ששייכים לסטייט הזה (עבור הטבלה למטה)
        public List<LogEntry> RelatedLogs { get; set; } = new List<LogEntry>();
    }
}
