using DocumentFormat.OpenXml.ExtendedProperties;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
namespace IndiLogs_3._0.Services
{
    public class LogFileService
    {
        // Regex לפרסור לוגים של אפליקציה
        private readonly Regex _appDevRegex = new Regex(
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})\x1e" +
            @"(?<Thread>[^\x1e]*)\x1e" +
            @"(?<RootIFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowName>[^\x1e]*)\x1e" +
            @"(?<Pattern>[^\x1e]*)\x1e" +
            @"(?<Context>[^\x1e]*)\x1e" +
            @"(?<Level>\w+)\s(?<Logger>[^\x1e]*)\x1e" +
            @"(?<Location>[^\x1e]*)\x1e" +
            @"(?<Message>.*?)\x1e" +
            @"(?<Exception>.*?)\x1e" +
            @"(?<Data>.*?)(\x1e|$)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}", RegexOptions.Compiled);

        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress)
        {
            return await Task.Run(() =>
            {
                var session = new LogSessionData();
                // אתחול כל המילונים
                session.ConfigurationFiles = new Dictionary<string, string>();
                session.DatabaseFiles = new Dictionary<string, byte[]>();

                if (filePaths == null || filePaths.Length == 0) return session;

                var logsBag = new ConcurrentBag<LogEntry>();
                var transitionsBag = new ConcurrentBag<LogEntry>();
                var failuresBag = new ConcurrentBag<LogEntry>();
                var appDevLogsBag = new ConcurrentBag<LogEntry>();
                var eventsBag = new ConcurrentBag<EventEntry>();
                var screenshotsBag = new ConcurrentBag<BitmapImage>();

                long totalBytesAllFiles = 0;
                foreach (var p in filePaths)
                    if (File.Exists(p)) totalBytesAllFiles += new FileInfo(p).Length;

                long processedBytesGlobal = 0;
                string detectedSwVersion = "Unknown";
                string detectedPlcVersion = "Unknown";

                try
                {
                    foreach (var filePath in filePaths)
                    {
                        if (!File.Exists(filePath)) continue;

                        long currentFileSize = new FileInfo(filePath).Length;
                        string extension = Path.GetExtension(filePath).ToLower();

                        progress?.Report((CalculatePercent(processedBytesGlobal, totalBytesAllFiles), $"Opening {Path.GetFileName(filePath)}..."));

                        if (extension == ".zip")
                        {
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                var filesToProcess = new List<ZipEntryData>();

                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.Length == 0) continue;

                                    string lowerName = entry.FullName.ToLower();

                                    // --- סינון אגרסיבי ---
                                    if (lowerName.Contains("/backup/") || lowerName.Contains("\\backup\\") ||
                                        lowerName.Contains("/old/") || lowerName.Contains("\\old\\") ||
                                        lowerName.Contains("/temp/") || lowerName.Contains("\\temp\\") ||
                                        lowerName.Contains("/archive/") || lowerName.Contains("\\archive\\"))
                                    {
                                        continue;
                                    }

                                    bool shouldProcess = false;
                                    var entryData = new ZipEntryData { Name = entry.Name };

                                    // 1. זיהוי קבצי Configuration - בודק אם הנתיב מכיל תיקיית Configuration
                                    bool isConfigFile = lowerName.Contains("/configuration/") ||
                                                        lowerName.Contains("\\configuration\\") ||
                                                        lowerName.Contains("\\configuration/") ||
                                                        lowerName.Contains("/configuration\\") ||
                                                        lowerName.StartsWith("configuration/") ||
                                                        lowerName.StartsWith("configuration\\");

                                    if (isConfigFile)
                                    {
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);

                                            // Check if this is a SQLite database file
                                            if (fileNameOnly.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                            {
                                                // Read as binary for SQLite databases
                                                using (var ms = CopyToMemory(entry))
                                                {
                                                    byte[] dbBytes = ms.ToArray();
                                                    if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                                    {
                                                        session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                                                        Debug.WriteLine($"✅ Loaded DB file: {fileNameOnly} ({dbBytes.Length} bytes)");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Read as text for JSON and other text files
                                                using (var ms = CopyToMemory(entry))
                                                using (var r = new StreamReader(ms))
                                                {
                                                    string content = r.ReadToEnd();
                                                    if (!session.ConfigurationFiles.ContainsKey(fileNameOnly))
                                                    {
                                                        session.ConfigurationFiles.Add(fileNameOnly, content);
                                                        Debug.WriteLine($"✅ Loaded config file: {fileNameOnly}");
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Failed to read config file {entry.Name}: {ex.Message}");
                                        }
                                        continue;
                                    }

                                    // 2. לוגים ראשיים
                                    if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        entryData.Type = FileType.MainLog;
                                        shouldProcess = true;
                                    }
                                    // 3. לוגים של אפליקציה
                                    else if ((entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                             (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                                    {
                                        entryData.Type = FileType.AppDevLog;
                                        shouldProcess = true;
                                    }
                                    // 4. Events CSV
                                    else if (entry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) &&
                                             entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                                    {
                                        entryData.Type = FileType.EventsCsv;
                                        shouldProcess = true;
                                    }
                                    // 5. תמונות
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var bmp = LoadBitmapFromZip(entry);
                                        if (bmp != null) screenshotsBag.Add(bmp);
                                        continue;
                                    }
                                    // 6. קבצי מידע
                                    else if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                        using (var r = new StreamReader(ms))
                                        {
                                            session.PressConfiguration = r.ReadToEnd();
                                            var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                            if (sw != "Unknown") detectedSwVersion = sw;
                                            if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                                        }
                                        continue;
                                    }
                                    else if (entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                        using (var r = new StreamReader(ms))
                                        {
                                            session.SetupInfo = r.ReadToEnd();
                                            string plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                                            if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                                        }
                                        continue;
                                    }

                                    if (shouldProcess)
                                    {
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                }

                                Debug.WriteLine($"📦 ZIP Summary:");
                                Debug.WriteLine($"   Config files: {session.ConfigurationFiles.Count}");
                                Debug.WriteLine($"   Database files: {session.DatabaseFiles.Count}");
                                Debug.WriteLine($"   Log files to process: {filesToProcess.Count}");

                                int totalFiles = filesToProcess.Count;
                                int processedCount = 0;

                                Parallel.ForEach(filesToProcess, item =>
                                {
                                    try
                                    {
                                        using (item.Stream)
                                        {
                                            if (item.Type == FileType.MainLog)
                                            {
                                                var result = ParseLogStream(item.Stream);
                                                foreach (var l in result.AllLogs) logsBag.Add(l);
                                                foreach (var t in result.Transitions) transitionsBag.Add(t);
                                                foreach (var f in result.Failures) failuresBag.Add(f);
                                            }
                                            else if (item.Type == FileType.AppDevLog)
                                            {
                                                var logs = ParseAppDevLogStream(item.Stream);
                                                foreach (var l in logs) appDevLogsBag.Add(l);
                                            }
                                            else if (item.Type == FileType.EventsCsv)
                                            {
                                                var evts = ParseEventsCsv(item.Stream);
                                                foreach (var e in evts) eventsBag.Add(e);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error processing file {item.Name}: {ex.Message}");
                                    }
                                    finally
                                    {
                                        int c = Interlocked.Increment(ref processedCount);
                                        if (c % 3 == 0)
                                        {
                                            double ratio = (double)c / totalFiles;
                                            double fileProg = (0.5 + (ratio * 0.5)) * currentFileSize;
                                            double totalP = ((processedBytesGlobal + fileProg) / totalBytesAllFiles) * 100;
                                            progress?.Report((Math.Min(99, totalP), $"Parsing files: {c}/{totalFiles}"));
                                        }
                                    }
                                });
                            }
                        }
                        else
                        {
                            // קובץ בודד
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var ms = new MemoryStream())
                            {
                                fs.CopyTo(ms);
                                ms.Position = 0;

                                if (filePath.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    var logs = ParseAppDevLogStream(ms);
                                    foreach (var l in logs) appDevLogsBag.Add(l);
                                }
                                else
                                {
                                    var result = ParseLogStream(ms);
                                    foreach (var l in result.AllLogs) logsBag.Add(l);
                                    foreach (var t in result.Transitions) transitionsBag.Add(t);
                                    foreach (var f in result.Failures) failuresBag.Add(f);
                                }
                            }
                        }
                        processedBytesGlobal += currentFileSize;
                    }

                    progress?.Report((98, "Finalizing..."));

                    session.VersionsInfo = $"SW: {detectedSwVersion} | PLC: {detectedPlcVersion}";
                    session.Logs = logsBag.OrderByDescending(x => x.Date).ToList();
                    session.StateTransitions = transitionsBag.OrderBy(x => x.Date).ToList();
                    session.CriticalFailureEvents = failuresBag.OrderBy(x => x.Date).ToList();
                    session.AppDevLogs = appDevLogsBag.OrderByDescending(x => x.Date).ToList();
                    session.Events = eventsBag.OrderByDescending(x => x.Time).ToList();
                    session.Screenshots = screenshotsBag.ToList();

                    progress?.Report((100, "Done"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CRITICAL ERROR] LoadSessionAsync: {ex.Message}");
                }

                return session;
            });
        }
        // Indilogs 3.0/Services/LogFileService.cs
        // וודא שהפונקציה מחזירה List<LogEntry> ושגם ה-results מוגדר כך
        public List<EventEntry> ParseEventsCsv(Stream stream)
        {

            var list = new List<EventEntry>();
            try
            {
                if (stream.Position != 0) stream.Position = 0;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string header = reader.ReadLine();
                    if (header == null) return list;

                    var headers = header.Split(',').Select(h => h.Trim().Trim('"')).ToArray();
                    int timeIdx = Array.FindIndex(headers, h => h.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0);
                    int nameIdx = Array.FindIndex(headers, h => h.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);

                    // לוגיקת CSV מקוצרת לחיסכון במקום (הקוד המקורי שלך היה תקין)
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = SplitCsvLine(line); // ודא שיש לך את פונקציית העזר הזו למטה

                        if (parts.Count > timeIdx && DateTime.TryParse(parts[timeIdx].Trim('"'), out DateTime time))
                        {
                            list.Add(new EventEntry
                            {
                                Time = time,
                                Name = (nameIdx >= 0 && parts.Count > nameIdx) ? parts[nameIdx] : "Unknown"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"ParseEventsCsv Error: {ex.Message}"); }
            return list;
        }
        public List<LogEntry> ParseLogStreamPartial(Stream stream)
        {
            var newLogs = new List<LogEntry>();

            try
            {
                // ⚠️ קריטי: אנחנו לא מאפסים את stream.Position ל-0!
                // הקריאה תתבצע מהמקום בו הסמן נמצא כרגע (אחרי ה-Seek).

                var logReader = new IndigoLogsReader(stream);

                // שימוש בלוגיקה ששלחת: קריאה סדרתית מהנקודה הנוכחית
                while (logReader.MoveToNext())
                {
                    if (logReader.Current != null)
                    {
                        var entry = new LogEntry
                        {
                            // המרות בסיסיות ל-Model שלך
                            Level = logReader.Current.Level?.ToString() ?? "Info",
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = logReader.Current.ThreadName ?? "",
                            Logger = logReader.Current.LoggerName ?? "",

                            // שליפת שדות נוספים לפי המיפוי ששלחת
                            ProcessName = logReader.Current["ProcessName"]?.ToString() ?? "",
                            // ניתן להוסיף כאן עוד שדות ל-LogEntry אם תרצה (PID, FlowId וכו')
                        };

                        newLogs.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseLogStreamPartial Error: {ex.Message}");
            }

            return newLogs;
        }
        public (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream)
        {
            var allLogs = new List<LogEntry>();
            var transitions = new List<LogEntry>();
            var failures = new List<LogEntry>();

            try
            {
                if (stream.Position != 0) stream.Position = 0;
                var reader = new IndigoLogsReader(stream);

                while (reader.MoveToNext())
                {
                    if (reader.Current != null)
                    {
                        var entry = new LogEntry
                        {
                            Level = reader.Current.Level?.ToString() ?? "Info",
                            Date = reader.Current.Time,
                            Message = reader.Current.Message ?? "",
                            ThreadName = reader.Current.ThreadName ?? "",
                            Logger = reader.Current.LoggerName ?? "",
                            ProcessName = reader.Current["ProcessName"]?.ToString() ?? ""
                        };

                        allLogs.Add(entry);

                        if (entry.ThreadName == "Manager" &&
                            entry.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                            entry.Message.Contains("->"))
                        {
                            transitions.Add(entry);
                        }
                        else if (entry.ThreadName == "Events" &&
                                 entry.Message.Contains("PLC_FAILURE_STATE_CHANGE"))
                        {
                            failures.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseLogStream Error: {ex.GetType().Name} - {ex.Message}");
                Debug.WriteLine($"Stack: {ex.StackTrace}");
            }
            return (allLogs, transitions, failures);
        }

        private List<LogEntry> ParseAppDevLogStream(Stream stream)
        {
            var list = new List<LogEntry>();
            try
            {
                if (stream.Position != 0) stream.Position = 0;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    StringBuilder buffer = new StringBuilder();

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line == "!!![V2]") continue;

                        if (_dateStartPattern.IsMatch(line))
                        {
                            if (buffer.Length > 0)
                            {
                                var logEntry = ProcessAppDevBuffer(buffer.ToString());
                                if (logEntry != null) list.Add(logEntry);
                                buffer.Clear();
                            }
                        }
                        buffer.AppendLine(line);
                    }

                    if (buffer.Length > 0)
                    {
                        var logEntry = ProcessAppDevBuffer(buffer.ToString());
                        if (logEntry != null) list.Add(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseAppDevLogStream Error: {ex.Message}");
            }
            return list;
        }

        private LogEntry ProcessAppDevBuffer(string rawText)
        {
            var match = _appDevRegex.Match(rawText);
            if (!match.Success) return null;

            string timestampStr = match.Groups["Timestamp"].Value;
            if (!DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                DateTime.TryParse(timestampStr, out date);
            }

            string message = match.Groups["Message"].Value.Trim();
            string exception = match.Groups["Exception"].Value.Trim();
            string data = match.Groups["Data"].Value.Trim();

            if (!string.IsNullOrEmpty(exception)) message += $"\n[EXC]: {exception}";
            if (!string.IsNullOrEmpty(data)) message += $"\n[DATA]: {data}";

            return new LogEntry
            {
                Date = date,
                ThreadName = match.Groups["Thread"].Value,
                Level = match.Groups["Level"].Value.ToUpper(),
                Logger = match.Groups["Logger"].Value,
                Message = message,
                ProcessName = "APP",
                Method = match.Groups["Location"].Value
            };
        }

        
        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private MemoryStream CopyToMemory(ZipArchiveEntry entry)
        {
            var ms = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
        }

        private BitmapImage LoadBitmapFromZip(ZipArchiveEntry entry)
        {
            try
            {
                using (var ms = CopyToMemory(entry))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { return null; }
        }

        private (string sw, string plc) ParseReadmeVersions(string content)
        {
            try
            {
                var sw = Regex.Match(content, @"Version[:=]\s*(.+)", RegexOptions.IgnoreCase);
                var plc = Regex.Match(content, @"PressPlcVersion[:=]\s*(.+)", RegexOptions.IgnoreCase);
                return (sw.Success ? sw.Groups[1].Value.Trim() : "Unknown", plc.Success ? plc.Groups[1].Value.Trim() : "Unknown");
            }
            catch { return ("Unknown", "Unknown"); }
        }

        private string ExtractPlcVersionFromSetupInfo(string jsonContent)
        {
            try
            {
                var match = Regex.Match(jsonContent, @"\""Name\""\s*:\s*\""press-content-mcs-plc\""[\s\S]*?\""Version\""\s*:\s*\""(?<ver>[^\""]+)\""", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups["ver"].Value.Trim();
            }
            catch { }
            return null;
        }

        private enum FileType { MainLog, AppDevLog, EventsCsv }
        private class ZipEntryData { public string Name; public FileType Type; public MemoryStream Stream; }
        private double CalculatePercent(long processed, long total) => total == 0 ? 0 : Math.Min(99, ((double)processed / total) * 100);
    }
}