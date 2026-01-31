using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Debug logger for comparison window features.
    /// Enable/disable logging via IsEnabled property.
    /// </summary>
    public static class ComparisonDebugLogger
    {
        private static readonly object _lock = new object();
        private static string _logPath;
        private static readonly List<string> _recentLogs = new List<string>();
        private const int MaxRecentLogs = 100;

        /// <summary>
        /// Whether debug logging is enabled.
        /// </summary>
        public static bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Gets the most recent log entries (for display in UI).
        /// </summary>
        public static IReadOnlyList<string> RecentLogs => _recentLogs;

        /// <summary>
        /// Gets the log file path.
        /// </summary>
        public static string LogPath
        {
            get
            {
                if (_logPath == null)
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var dir = Path.Combine(appData, "IndiLogs", "Logs");
                    Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, $"comparison_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                return _logPath;
            }
        }

        /// <summary>
        /// Logs a debug message with category prefix.
        /// </summary>
        public static void Log(string category, string message)
        {
            if (!IsEnabled) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{category,-10}] {message}";

            // Output to Debug console
            Debug.WriteLine(logLine);

            // Keep in memory for UI display
            lock (_lock)
            {
                _recentLogs.Add(logLine);
                while (_recentLogs.Count > MaxRecentLogs)
                    _recentLogs.RemoveAt(0);
            }

            // Also write to file
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogPath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Ignore file write errors
            }
        }

        /// <summary>
        /// Logs a separator line for readability.
        /// </summary>
        public static void LogSeparator(string title = null)
        {
            if (!IsEnabled) return;

            if (string.IsNullOrEmpty(title))
                Log("=====", new string('=', 60));
            else
                Log("=====", $"===== {title} " + new string('=', Math.Max(0, 50 - title.Length)));
        }

        /// <summary>
        /// Logs scroll synchronization events.
        /// </summary>
        public static void LogScroll(string sourcePane, DateTime sourceTime, int targetIndex, DateTime? targetTime, string additionalInfo = null)
        {
            if (!IsEnabled) return;

            var msg = $"Source={sourcePane}, SourceTime={sourceTime:HH:mm:ss.fff}, TargetIndex={targetIndex}";
            if (targetTime.HasValue)
                msg += $", TargetTime={targetTime:HH:mm:ss.fff}, Delta={Math.Abs((sourceTime - targetTime.Value).TotalMilliseconds):F0}ms";
            if (!string.IsNullOrEmpty(additionalInfo))
                msg += $" | {additionalInfo}";

            Log("SCROLL_SYNC", msg);
        }

        /// <summary>
        /// Logs diff calculation events.
        /// </summary>
        public static void LogDiff(int leftIndex, int rightIndex, string leftMessage, string rightMessage, bool hasDiff, int segmentCount)
        {
            if (!IsEnabled) return;

            var leftPreview = leftMessage?.Length > 50 ? leftMessage.Substring(0, 50) + "..." : leftMessage;
            var rightPreview = rightMessage?.Length > 50 ? rightMessage.Substring(0, 50) + "..." : rightMessage;

            Log("SHOW_DIFF", $"LeftIdx={leftIndex}, RightIdx={rightIndex}, HasDiff={hasDiff}, Segments={segmentCount}");
            Log("SHOW_DIFF", $"  Left: {leftPreview}");
            Log("SHOW_DIFF", $"  Right: {rightPreview}");
        }

        /// <summary>
        /// Logs ignore pattern events.
        /// </summary>
        public static void LogIgnorePattern(string pattern, bool isValid, string testInput = null, string maskedResult = null)
        {
            if (!IsEnabled) return;

            Log("IGNORE_PATTERN", $"Pattern=\"{pattern}\", IsValid={isValid}");
            if (!string.IsNullOrEmpty(testInput))
            {
                Log("IGNORE_PATTERN", $"  TestInput: {testInput}");
                Log("IGNORE_PATTERN", $"  MaskedResult: {maskedResult}");
            }
        }

        /// <summary>
        /// Logs selection synchronization events.
        /// </summary>
        public static void LogSelection(string sourcePane, DateTime selectedTime, int targetIndex, DateTime? targetTime)
        {
            if (!IsEnabled) return;

            var msg = $"Source={sourcePane}, SelectedTime={selectedTime:HH:mm:ss.fff}, TargetIndex={targetIndex}";
            if (targetTime.HasValue)
                msg += $", TargetTime={targetTime:HH:mm:ss.fff}, Delta={Math.Abs((selectedTime - targetTime.Value).TotalMilliseconds):F0}ms";

            Log("SELECTION_SYNC", msg);
        }

        /// <summary>
        /// Clears the log file.
        /// </summary>
        public static void ClearLog()
        {
            try
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
                _logPath = null; // Reset to generate new filename
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
