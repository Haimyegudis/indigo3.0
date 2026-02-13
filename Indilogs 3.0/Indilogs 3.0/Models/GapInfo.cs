using System;

namespace IndiLogs_3._0.Models
{
    public class GapInfo
    {
        public int Index { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string DurationText { get; set; }
        public string LastMessageBeforeGap { get; set; }
        public LogEntry LastLogBeforeGap { get; set; }
    }
}
