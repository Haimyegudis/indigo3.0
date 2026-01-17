using IndiLogs_3._0.Models.Analysis;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Models
{
    public class LogSessionData
    {

        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public List<LogEntry> AppDevLogs { get; set; } = new List<LogEntry>();

        // --- אופטימיזציה: רשימות מוכנות מראש לניתוח מהיר ---
        public List<LogEntry> StateTransitions { get; set; } = new List<LogEntry>();
        public List<LogEntry> CriticalFailureEvents { get; set; } = new List<LogEntry>();
        // ----------------------------------------------------

        // >>> כאן התיקון: הוספנו את המילון שחסר לך <<<
        public Dictionary<string, string> ConfigurationFiles { get; set; } = new Dictionary<string, string>();

        // Binary DB files (SQLite) - stored as byte arrays
        public Dictionary<string, byte[]> DatabaseFiles { get; set; } = new Dictionary<string, byte[]>();

        public List<EventEntry> Events { get; set; } = new List<EventEntry>();
        public List<BitmapImage> Screenshots { get; set; } = new List<BitmapImage>();
        public ObservableCollection<LogEntry> MarkedLogs { get; set; } = new ObservableCollection<LogEntry>();

        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string SetupInfo { get; set; }
        public string PressConfiguration { get; set; }
        public string VersionsInfo { get; set; }

        public List<StateEntry> CachedStates { get; set; }
        public List<AnalysisResult> CachedAnalysis { get; set; }
    }
}