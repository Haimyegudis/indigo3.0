using System;
using System.ComponentModel;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Represents a single search result from the Global Grep feature.
    /// Contains information about where the match was found and preview of the content.
    /// </summary>
    public class GrepResult : INotifyPropertyChanged
    {
        /// <summary>
        /// Timestamp of the log entry (if successfully parsed)
        /// </summary>
        public DateTime? Timestamp { get; set; }

        /// <summary>
        /// Full path to the file or ZIP containing this result
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Line number within the file (1-based)
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Log origin: "PLC" or "APP"
        /// </summary>
        public string LogType { get; set; }

        /// <summary>
        /// Preview of the matched line (limited to 500 characters for UI performance)
        /// </summary>
        public string PreviewText { get; set; }

        /// <summary>
        /// Name of the session/file (for display purposes)
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        /// Reference to the actual LogEntry (only populated for in-memory searches)
        /// </summary>
        public LogEntry ReferencedLogEntry { get; set; }

        /// <summary>
        /// Index of the session in LoadedSessions collection (for navigation)
        /// </summary>
        public int SessionIndex { get; set; }

        /// <summary>
        /// Field that matched the search (Message, Exception, Method, Data)
        /// </summary>
        public string MatchedField { get; set; }

        private bool _isSelected;
        /// <summary>
        /// Whether this result is currently selected in the UI
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        /// <summary>
        /// Creates a formatted display string for the timestamp
        /// </summary>
        public string TimestampDisplay => Timestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "N/A";

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
