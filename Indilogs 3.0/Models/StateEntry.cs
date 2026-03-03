using System;
using System.Windows.Media; // Required for Brush

namespace IndiLogs_3._0.Models
{
    public class StateEntry
    {
        public string StateName { get; set; }       // The current state (e.g. GET_READY)
        public string TransitionTitle { get; set; } // The full title (e.g. OFF -> GET_READY)

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public LogEntry LogReference { get; set; }

        // New fields for status
        public string Status { get; set; }          // "Success", "Failed", or empty
        public Brush StatusColor { get; set; }      // Text color of the status

        public string Duration
        {
            get
            {
                if (EndTime.HasValue)
                {
                    var span = EndTime.Value - StartTime;
                    return span.ToString(@"hh\:mm\:ss\.fff");
                }
                return "Current...";
            }
        }
    }
}