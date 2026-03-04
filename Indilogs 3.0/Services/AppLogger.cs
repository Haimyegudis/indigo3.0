using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Centralized application logger. Writes to a rotating log file and Debug output.
    /// Uses a buffered writer that flushes periodically for better I/O performance.
    /// Unlike Debug.WriteLine, file output is available in Release builds.
    /// </summary>
    internal static class AppLogger
    {
        private static readonly object _lock = new object();
        private static readonly string _logDir;
        private static string? _currentLogFile;
        private static DateTime _currentDate;
        private static StreamWriter? _writer;
        private static Timer? _flushTimer;
        private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB
        private const int FlushIntervalMs = 500; // Flush every 500ms

        static AppLogger()
        {
            _logDir = AppPaths.Root;
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppLogger: Cannot create log directory: {ex.Message}");
            }
            RotateIfNeeded();

            // Periodic flush timer
            _flushTimer = new Timer(_ => FlushBuffer(), null, FlushIntervalMs, FlushIntervalMs);

            // Flush on process exit
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown();
        }

        public static void Info(string message, [CallerMemberName] string? caller = null)
            => Write("INFO", message, caller);

        public static void Warn(string message, [CallerMemberName] string? caller = null)
            => Write("WARN", message, caller);

        public static void Error(string message, Exception? ex = null, [CallerMemberName] string? caller = null)
        {
            string full = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write("ERROR", full, caller);
        }

        public static void Error(Exception ex, [CallerMemberName] string? caller = null)
            => Write("ERROR", $"{ex.GetType().Name}: {ex.Message}", caller);

        private static void Write(string level, string message, string? caller)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"{timestamp} [{level}] [{caller}] {message}";

            Debug.WriteLine(line);

            lock (_lock)
            {
                try
                {
                    RotateIfNeeded();
                    _writer?.WriteLine(line);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AppLogger: Failed to write log: {ex.Message}");
                }
            }
        }

        private static void FlushBuffer()
        {
            lock (_lock)
            {
                try
                {
                    _writer?.Flush();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AppLogger: Failed to flush log buffer: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Flushes and closes the log writer. Called on app shutdown.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    _flushTimer?.Dispose();
                    _flushTimer = null;
                    _writer?.Flush();
                    _writer?.Dispose();
                    _writer = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AppLogger: Shutdown error: {ex.Message}");
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
                    if (_writer != null && _writer.BaseStream.Length < MaxLogFileSize)
                        return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AppLogger: Failed to check log file size: {ex.Message}");
                    return;
                }
            }

            // Close previous writer
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AppLogger: Failed to dispose previous writer: {ex.Message}"); }

            _currentDate = today;
            _currentLogFile = Path.Combine(_logDir,
                $"IndiLogs_{today:yyyy-MM-dd}.log");

            try
            {
                _writer = new StreamWriter(
                    new FileStream(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.Read, 4096),
                    System.Text.Encoding.UTF8)
                {
                    AutoFlush = false
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppLogger: Failed to open log file: {ex.Message}");
                _writer = null;
            }
        }
    }
}
