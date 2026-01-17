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
        private DateTime _lastLogTime = DateTime.MinValue;

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
                if (_lastLogTime != DateTime.MinValue)
                {
                    OnStatusChanged?.Invoke("Syncing...");
                    // סריקה מהירה עד לנקודה האחרונה הידועה
                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch { break; }
                        if (!hasMore) break;

                        if (reader.Current != null && reader.Current.Time > _lastLogTime)
                        {
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

                    while (!token.IsCancellationRequested)
                    {
                        bool hasMore = false;
                        try { hasMore = reader.MoveToNext(); } catch { break; }
                        if (!hasMore) break;

                        if (fs.Position < tailThreshold) continue;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            buffer.Enqueue(log);
                            if (buffer.Count > BATCH_SIZE_LIMIT) buffer.Dequeue();
                            _lastLogTime = log.Date;
                        }
                    }

                    if (buffer.Count > 0)
                    {
                        OnLogsReceived?.Invoke(buffer.ToList());
                    }
                    OnStatusChanged?.Invoke("Live monitoring active.");
                }

                // --- שלב האזנה שוטפת (Tail -f) ---
                int stuckCounter = 0;
                long lastKnownLength = fs.Length;

                while (!token.IsCancellationRequested)
                {
                    var newBatch = new List<LogEntry>();

                    while (true)
                    {
                        bool success = false;
                        try { success = reader.MoveToNext(); } catch { break; }
                        if (!success) break;

                        var log = MapToLogEntry(reader);
                        if (log != null)
                        {
                            // מניעת כפילויות
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
                        stuckCounter = 0; // איפוס מונה תקלות
                        lastKnownLength = fs.Length;
                    }
                    else
                    {
                        // --- לוגיקת תקיעה משופרת ורגועה ---
                        long currentLen = new FileInfo(filePath).Length; // בדיקה מול הדיסק

                        // אם הקובץ גדל משמעותית (מעל 100KB) וה-Reader לא זז
                        if (currentLen > fs.Position + 102400)
                        {
                            stuckCounter++;
                            // מחכים 10 שניות (10 איטרציות) לפני שמכריזים על תקיעה
                            if (stuckCounter > 10)
                            {
                                Debug.WriteLine($"[LiveMonitor] STUCK DETECTED (Persistent). Restarting Reader.");
                                return; // יציאה ל-Re-Sync
                            }
                        }
                        else
                        {
                            // אם ההפרש קטן, כנראה זה Buffering, נאפס את המונה
                            stuckCounter = 0;
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