using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    public class CustomLiveLogReader
    {
        public event Action<List<LogEntry>> OnLogsReceived;
        public event Action<string> OnStatusChanged;

        private const int BATCH_SIZE_LIMIT = 2000;
        private DateTime _lastLogTime = DateTime.MinValue; // Track the last displayed time

        public async Task StartMonitoring(string filePath, CancellationToken token)
        {
            OnStatusChanged?.Invoke("Initializing...");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MonitorLoop(filePath, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Error: {ex.Message}. Retrying in 2s...");
                    await Task.Delay(2000, token).ConfigureAwait(false);
                }
            }
        }

        private async Task MonitorLoop(string filePath, CancellationToken token)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);

                // --- Re-Sync phase ---
                // If we already have a last time (i.e. after a crash/recovery), fast-forward to it
                if (_lastLogTime != DateTime.MinValue)
                {
                    OnStatusChanged?.Invoke("Re-syncing stream...");

                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch (Exception ex) { AppLogger.Error("MoveToNext failed during re-sync", ex); break; }
                        if (!hasMore) break;

                        // Fast skip without memory allocation
                        if (reader.Current != null && reader.Current.Time > _lastLogTime)
                        {
                            // Found the resume point!
                            var log = MapToLogEntry(reader);
                            if (log != null)
                            {
                                OnLogsReceived?.Invoke(new List<LogEntry> { log });
                                _lastLogTime = log.Date;
                            }
                            break;
                        }
                    }
                    OnStatusChanged?.Invoke("Live (Resumed)");
                }
                else
                {
                    // --- Initial load (Smart Seek) ---
                    // Load only the last 2MB
                    long tailThreshold = Math.Max(0, fs.Length - (2 * 1024 * 1024));
                    var buffer = new Queue<LogEntry>(BATCH_SIZE_LIMIT);

                    OnStatusChanged?.Invoke("Scanning file...");

                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch (Exception ex) { AppLogger.Error("MoveToNext failed during initial load", ex); break; }
                        if (!hasMore) break;

                        // Optimization: skip the entire beginning
                        if (fs.Position < tailThreshold) continue;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            buffer.Enqueue(log);
                            if (buffer.Count > BATCH_SIZE_LIMIT) buffer.Dequeue();
                            _lastLogTime = log.Date; // Update the time
                        }
                    }

                    if (buffer.Count > 0)
                    {
                        OnLogsReceived?.Invoke(buffer.ToList());
                    }
                    OnStatusChanged?.Invoke("Live monitoring active.");
                }

                // --- Continuous listening phase (Tail -f) ---
                long lastKnownPosition = fs.Position;
                int stuckCounter = 0;

                while (!token.IsCancellationRequested)
                {
                    var newBatch = new List<LogEntry>();

                    // Read new entries
                    while (true)
                    {
                        bool success = false;
                        try { success = reader.MoveToNext(); } catch (Exception ex) { AppLogger.Error("MoveToNext failed during tail", ex); break; }
                        if (!success) break;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            // Filter duplicates in case of millisecond overlap
                            if (log.Date > _lastLogTime || (log.Date == _lastLogTime && !string.IsNullOrEmpty(log.Message)))
                            {
                                newBatch.Add(log);
                                _lastLogTime = log.Date;
                            }
                        }
                        if (newBatch.Count >= 2000) break;
                    }

                    if (newBatch.Count > 0)
                    {
                        OnLogsReceived?.Invoke(newBatch);
                        lastKnownPosition = fs.Position;
                        stuckCounter = 0; // Reset stuck counter
                    }
                    else
                    {
                        // --- Stuck detection and recovery ---
                        // If the file has grown significantly and the Reader isn't reading anything
                        if (fs.Length > fs.Position)
                        {
                            stuckCounter++;
                            // Wait a bit to make sure it's not just slow writing
                            if (stuckCounter > 2)
                            {
                                return; // Exit the loop -> causes StartMonitoring to call MonitorLoop again
                            }
                        }
                    }

                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }

        private LogEntry MapToLogEntry(IndigoLogsReader reader)
        {
            if (reader.Current == null) return null;

            return new LogEntry
            {
                Level = reader.Current.Level?.ToString() ?? "Info",
                Date = reader.Current.Time,
                Message = reader.Current.Message ?? "",
                ThreadName = reader.Current.ThreadName ?? "",
                Logger = reader.Current.LoggerName ?? "",
                ProcessName = reader.Current["ProcessName"]?.ToString() ?? ""
            };
        }
    }
}