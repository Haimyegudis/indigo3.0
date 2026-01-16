using System;

namespace IndiLogs_3._0.Models
{
    public class WindowsEvent
    {
        public DateTime TimeCreated { get; set; }
        public string Level { get; set; }
        public string EventId { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string Category { get; set; }

        // Icon based on level
        public string LevelIcon
        {
            get
            {
                return Level switch
                {
                    "Critical" => "🔴",
                    "Error" => "❌",
                    "Warning" => "⚠️",
                    "Info" => "ℹ️",
                    "Verbose" => "📝",
                    _ => "•"
                };
            }
        }

        // Color based on level
        public string LevelColor
        {
            get
            {
                return Level switch
                {
                    "Critical" => "#E53935",
                    "Error" => "#F44336",
                    "Warning" => "#FFA726",
                    "Info" => "#42A5F5",
                    "Verbose" => "#78909C",
                    _ => "#9E9E9E"
                };
            }
        }
    }
}