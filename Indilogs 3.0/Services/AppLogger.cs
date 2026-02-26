using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Centralized application logger. Writes to a rotating log file and Debug output.
    /// Unlike Debug.WriteLine, file output is available in Release builds.
    /// </summary>
    internal static class AppLogger
    {
        private static readonly object _lock = new object();
        private static readonly string _logDir;
        private static string _currentLogFile;
        private static DateTime _currentDate;
        private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB

        static AppLogger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndiLogs3", "Logs");
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch
            {
                // If we can't create the log directory, file logging will be silently disabled
            }
            RotateIfNeeded();
        }

        public static void Info(string message, [CallerMemberName] string caller = null)
            => Write("INFO", message, caller);

        public static void Warn(string message, [CallerMemberName] string caller = null)
            => Write("WARN", message, caller);

        public static void Error(string message, Exception ex = null, [CallerMemberName] string caller = null)
        {
            string full = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write("ERROR", full, caller);
        }

        public static void Error(Exception ex, [CallerMemberName] string caller = null)
            => Write("ERROR", $"{ex.GetType().Name}: {ex.Message}", caller);

        private static void Write(string level, string message, string caller)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"{timestamp} [{level}] [{caller}] {message}";

            Debug.WriteLine(line);

            lock (_lock)
            {
                try
                {
                    RotateIfNeeded();
                    if (_currentLogFile != null)
                        File.AppendAllText(_currentLogFile, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must never crash the application
                }
            }
        }

        private static void RotateIfNeeded()
        {
            var today = DateTime.Today;
            if (_currentLogFile != null && _currentDate == today)
            {
                try
                {
                    if (new FileInfo(_currentLogFile).Length < MaxLogFileSize)
                        return;
                }
                catch { return; }
            }

            _currentDate = today;
            _currentLogFile = Path.Combine(_logDir,
                $"IndiLogs_{today:yyyy-MM-dd}.log");
        }
    }
}
