using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    public class CustomLiveLogReader
    {
        public event Action<List<LogEntry>> OnLogsReceived;
        public event Action<string> OnStatusChanged;

        private const int BATCH_SIZE_LIMIT = 2000;
        private DateTime _lastLogTime = DateTime.MinValue; // מעקב אחרי הזמן האחרון שהוצג

        public async Task StartMonitoring(string filePath, CancellationToken token)
        {
            Debug.WriteLine($"[LiveMonitor] Init: {Path.GetFileName(filePath)}");
            OnStatusChanged?.Invoke("Initializing...");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MonitorLoop(filePath, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiveMonitor] Critical Loop Error: {ex.Message}");
                    OnStatusChanged?.Invoke($"Error: {ex.Message}. Retrying in 2s...");
                    await Task.Delay(2000, token);
                }
            }
        }

        private async Task MonitorLoop(string filePath, CancellationToken token)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);
                Debug.WriteLine("[LiveMonitor] Stream Opened.");

                // --- שלב הסנכרון (Re-Sync) ---
                // אם כבר יש לנו זמן אחרון (כלומר אנחנו אחרי קריסה/התאוששות), נרוץ מהר עד אליו
                if (_lastLogTime != DateTime.MinValue)
                {
                    OnStatusChanged?.Invoke("Re-syncing stream...");
                    Debug.WriteLine($"[LiveMonitor] Re-syncing to match time > {_lastLogTime:HH:mm:ss.fff}...");

                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch { break; }
                        if (!hasMore) break;

                        // דילוג מהיר ללא הקצאת זיכרון
                        if (reader.Current != null && reader.Current.Time > _lastLogTime)
                        {
                            // מצאנו את נקודת ההמשך!
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
                    // --- טעינה ראשונית (Smart Seek) ---
                    // טעינת ה-2MB האחרונים בלבד
                    long tailThreshold = Math.Max(0, fs.Length - (2 * 1024 * 1024));
                    var buffer = new Queue<LogEntry>(BATCH_SIZE_LIMIT);

                    OnStatusChanged?.Invoke("Scanning file...");

                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch { break; }
                        if (!hasMore) break;

                        // אופטימיזציה: דילוג על כל ההתחלה
                        if (fs.Position < tailThreshold) continue;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            buffer.Enqueue(log);
                            if (buffer.Count > BATCH_SIZE_LIMIT) buffer.Dequeue();
                            _lastLogTime = log.Date; // עדכון הזמן
                        }
                    }

                    if (buffer.Count > 0)
                    {
                        OnLogsReceived?.Invoke(buffer.ToList());
                    }
                    OnStatusChanged?.Invoke("Live monitoring active.");
                }

                // --- שלב האזנה שוטפת (Tail -f) ---
                long lastKnownPosition = fs.Position;
                int stuckCounter = 0;

                while (!token.IsCancellationRequested)
                {
                    var newBatch = new List<LogEntry>();

                    // קריאת חדשים
                    while (true)
                    {
                        bool success = false;
                        try { success = reader.MoveToNext(); } catch { break; }
                        if (!success) break;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            // סינון כפילויות במקרה של חפיפה במילי-שניות
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
                        stuckCounter = 0; // איפוס מונה תקלות
                    }
                    else
                    {
                        // --- זיהוי תקיעה והתאוששות ---
                        // אם הקובץ גדל משמעותית וה-Reader לא קורא כלום
                        if (fs.Length > fs.Position)
                        {
                            stuckCounter++;
                            // מחכים קצת כדי לוודא שזו לא סתם כתיבה איטית
                            if (stuckCounter > 2)
                            {
                                Debug.WriteLine($"[LiveMonitor] STUCK DETECTED! (FileLen: {fs.Length} > Pos: {fs.Position}). Triggering Full Re-open.");
                                return; // יציאה מהלולאה -> תגרום ל-StartMonitoring לקרוא ל-MonitorLoop מחדש
                            }
                        }
                    }

                    await Task.Delay(1000, token);
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