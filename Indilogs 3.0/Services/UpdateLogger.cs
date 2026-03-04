using System;
using System.IO;

namespace IndiLogs_3._0.Services
{
    public static class UpdateLogger
    {
        private static string LogPath => AppPaths.UpdateLog;

        public static void Log(string message)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, logEntry);
            }
            catch (Exception ex)
            {
                // Swallow log-write errors to avoid crashing the application
                System.Diagnostics.Debug.WriteLine($"UpdateLogger: Failed to write log entry: {ex.Message}");
            }
        }

        public static void Log(string context, Exception ex)
        {
            Log($"[ERROR] {context}: {ex.Message}\nStack: {ex.StackTrace}");
        }
    }
}