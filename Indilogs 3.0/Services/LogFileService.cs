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
        // --- אופטימיזציה: מחלקת StringPool לאיחוד מחרוזות (Thread-Safe) ---
        public class StringPool
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cache
                = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

            public string Intern(string value)
            {
                // אם הערך ריק או null, אין מה לשמור ב-Cache
                if (string.IsNullOrEmpty(value)) return value;

                // ConcurrentDictionary.GetOrAdd is thread-safe
                return _cache.GetOrAdd(value, value);
            }

            public void Clear()
            {
                _cache.Clear();
            }
        }
        // ------------------------------------------------------

        // Regex לפרסור לוגים של אפליקציה - פורמט ישן עם \x1e כמפריד
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

        // Regex לפרסור לוגים של אפליקציה - פורמט חדש עם | כמפריד
        // Format: 2026-01-29 10:32:38,073 |Thread| |RootIFlowId| |IFlowId| |IFlowName| |Pattern| |Context| LEVEL  Logger
        // Next line: |Method|
        // Next lines: --> or <-- or message text, followed by optional data/JSON, ending with ||
        private readonly Regex _appDevRegexPipe = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})\s*\|(?<Thread>[^|]*)\|\s*\|(?<RootIFlowId>[^|]*)\|\s*\|(?<IFlowId>[^|]*)\|\s*\|(?<IFlowName>[^|]*)\|\s*\|(?<Pattern>[^|]*)\|\s*\|(?<Context>[^|]*)\|\s*(?<Level>\w+)\s+(?<Logger>[^\r\n]*)[\r\n]+\|(?<Location>[^|]*)\|[\r\n]+(?<Message>.*?)\s*\|\|",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}", RegexOptions.Compiled);

        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress)
        {
            return await Task.Run(() =>
            {
                // יצירת Pool אחד לכל הסשן
                var stringPool = new StringPool();

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

                                    // 1. זיהוי קבצי Configuration
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

                                            if (fileNameOnly.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                            {
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
                                    // 5. קבצי .db מכל תיקייה (לא רק Configuration)
                                    else if (entry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);
                                            using (var ms = CopyToMemory(entry))
                                            {
                                                byte[] dbBytes = ms.ToArray();
                                                if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                                {
                                                    session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                                                    Debug.WriteLine($"✅ Loaded DB file (non-config): {fileNameOnly} from {entry.FullName} ({dbBytes.Length} bytes)");
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Failed to read DB file {entry.Name}: {ex.Message}");
                                        }
                                        continue;
                                    }
                                    // 6. תמונות
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var bmp = LoadBitmapFromZip(entry);
                                        if (bmp != null) screenshotsBag.Add(bmp);
                                        continue;
                                    }
                                    // 7. קבצי מידע
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
                                        // טוען את הקובץ לזיכרון (חייבים לעשות זאת בתוך using block של archive)
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                }

                                int totalFiles = filesToProcess.Count;
                                int processedCount = 0;
                                object progressLock = new object();

                                // עיבוד מקבילי - StringPool כעת Thread-Safe עם ConcurrentDictionary
                                // שימוש ב-Parallel.ForEach לאופטימיזציה מקסימלית
                                Parallel.ForEach(filesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
                                {
                                    try
                                    {
                                        // עובד עם ה-Stream שכבר נטען
                                        using (item.Stream)
                                        {
                                            if (item.Type == FileType.MainLog)
                                            {
                                                // מעבירים את ה-Pool
                                                var result = ParseLogStream(item.Stream, stringPool);
                                                foreach (var l in result.AllLogs) logsBag.Add(l);
                                                foreach (var t in result.Transitions) transitionsBag.Add(t);
                                                foreach (var f in result.Failures) failuresBag.Add(f);
                                            }
                                            else if (item.Type == FileType.AppDevLog)
                                            {
                                                var logs = ParseAppDevLogStream(item.Stream, stringPool);
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
                                        lock (progressLock)
                                        {
                                            processedCount++;
                                            if (processedCount % 3 == 0)
                                            {
                                                double ratio = (double)processedCount / totalFiles;
                                                double fileProg = (0.5 + (ratio * 0.5)) * currentFileSize;
                                                double totalP = ((processedBytesGlobal + fileProg) / totalBytesAllFiles) * 100;
                                                progress?.Report((Math.Min(99, totalP), $"Parsing files: {processedCount}/{totalFiles}"));
                                            }
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
                                    var logs = ParseAppDevLogStream(ms, stringPool);
                                    foreach (var l in logs) appDevLogsBag.Add(l);
                                }
                                else
                                {
                                    var result = ParseLogStream(ms, stringPool);
                                    foreach (var l in result.AllLogs) logsBag.Add(l);
                                    foreach (var t in result.Transitions) transitionsBag.Add(t);
                                    foreach (var f in result.Failures) failuresBag.Add(f);
                                }
                            }
                        }
                        processedBytesGlobal += currentFileSize;
                    }

                    session.VersionsInfo = $"SW: {detectedSwVersion} | PLC: {detectedPlcVersion}";

                    // המרה ל-List סופי - מיון מהישן לחדש (חדשים למטה)
                    session.Logs = logsBag.OrderBy(x => x.Date).ToList();
                    session.StateTransitions = transitionsBag.OrderBy(x => x.Date).ToList();
                    session.CriticalFailureEvents = failuresBag.OrderBy(x => x.Date).ToList();
                    session.AppDevLogs = appDevLogsBag.OrderBy(x => x.Date).ToList();
                    session.Events = eventsBag.OrderBy(x => x.Time).ToList();
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
                    Debug.WriteLine($"[EVENTS CSV] Headers found: {string.Join(", ", headers)}");

                    int timeIdx = Array.FindIndex(headers, h => h.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  h.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  h.IndexOf("Timestamp", StringComparison.OrdinalIgnoreCase) >= 0);
                    int nameIdx = Array.FindIndex(headers, h => h.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("EventName", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("Event", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);
                    int stateIdx = Array.FindIndex(headers, h => h.Equals("State", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("EventState", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("Status", StringComparison.OrdinalIgnoreCase));
                    int severityIdx = Array.FindIndex(headers, h => h.Equals("Severity", StringComparison.OrdinalIgnoreCase) ||
                                                                     h.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                                                                     h.Equals("Priority", StringComparison.OrdinalIgnoreCase));
                    int parametersIdx = Array.FindIndex(headers, h => h.IndexOf("Parameters", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                       h.IndexOf("Params", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                       h.IndexOf("Args", StringComparison.OrdinalIgnoreCase) >= 0);
                    int descriptionIdx = Array.FindIndex(headers, h => h.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Description", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Subsystem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0);

                    Debug.WriteLine($"[EVENTS CSV] Column indices - Time:{timeIdx}, Name:{nameIdx}, State:{stateIdx}, Severity:{severityIdx}, Params:{parametersIdx}, Desc:{descriptionIdx}");

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = SplitCsvLine(line);

                        if (parts.Count > timeIdx && DateTime.TryParse(parts[timeIdx].Trim('"'), out DateTime time))
                        {
                            list.Add(new EventEntry
                            {
                                Time = time,
                                Name = (nameIdx >= 0 && parts.Count > nameIdx) ? parts[nameIdx].Trim('"') : "Unknown",
                                State = (stateIdx >= 0 && parts.Count > stateIdx) ? parts[stateIdx].Trim('"') : string.Empty,
                                Severity = (severityIdx >= 0 && parts.Count > severityIdx) ? parts[severityIdx].Trim('"') : string.Empty,
                                Parameters = (parametersIdx >= 0 && parts.Count > parametersIdx) ? parts[parametersIdx].Trim('"') : string.Empty,
                                Description = (descriptionIdx >= 0 && parts.Count > descriptionIdx) ? parts[descriptionIdx].Trim('"') : string.Empty
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
            // פונקציה זו משמשת ל-Live Monitoring (קבצים קטנים יחסית),
            // לכן אפשר ליצור Pool מקומי אם רוצים, או לוותר עליו.
            // לצורך עקביות, ניצור מקומי.
            var pool = new StringPool();
            var newLogs = new List<LogEntry>();

            try
            {
                var logReader = new IndigoLogsReader(stream);

                while (logReader.MoveToNext())
                {
                    if (logReader.Current != null)
                    {
                        string processName = logReader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            // שימוש ב-Intern
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = pool.Intern(logReader.Current.Message ?? ""),
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),

                            // אופטימיזציה: null אם ריק
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
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

        public (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream, StringPool pool = null)
        {
            // אם לא הועבר Pool (למשל בקריאות ישנות), צור אחד מקומי
            pool = pool ?? new StringPool();

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
                        string processName = reader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            // --- אופטימיזציה: שימוש ב-StringPool ---
                            Level = pool.Intern(reader.Current.Level?.ToString() ?? "Info"),
                            Date = reader.Current.Time,
                            Message = pool.Intern(reader.Current.Message ?? ""),
                            ThreadName = pool.Intern(reader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(reader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
                        };

                        allLogs.Add(entry);

                        // לוגיקה לזיהוי מעברים - נשארת זהה
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

        private List<LogEntry> ParseAppDevLogStream(Stream stream, StringPool pool = null)
        {
            pool = pool ?? new StringPool();
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
                                var logEntry = ProcessAppDevBuffer(buffer.ToString(), pool);
                                if (logEntry != null) list.Add(logEntry);
                                buffer.Clear();
                            }
                        }
                        buffer.AppendLine(line);
                    }

                    if (buffer.Length > 0)
                    {
                        var logEntry = ProcessAppDevBuffer(buffer.ToString(), pool);
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

        private LogEntry ProcessAppDevBuffer(string rawText, StringPool pool)
        {
            // נסה קודם את הפורמט הישן עם \x1e
            var match = _appDevRegex.Match(rawText);

            // אם לא הצליח, נסה את הפורמט החדש עם |
            if (!match.Success)
            {
                match = _appDevRegexPipe.Match(rawText);
            }

            if (!match.Success) return null;

            string timestampStr = match.Groups["Timestamp"].Value;
            if (!DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                DateTime.TryParse(timestampStr, out date);
            }

            string message = match.Groups["Message"].Success ? match.Groups["Message"].Value.Trim() : "";
            string exception = match.Groups["Exception"].Success ? match.Groups["Exception"].Value.Trim() : "";
            string data = match.Groups["Data"].Success ? match.Groups["Data"].Value.Trim() : "";
            string pattern = match.Groups["Pattern"].Success ? match.Groups["Pattern"].Value.Trim() : "";
            string location = match.Groups["Location"].Success ? match.Groups["Location"].Value.Trim() : "";

            // בפורמט החדש, ה-Direction (-->/<--) יכול לשמש כחלק מההודעה
            string direction = match.Groups["Direction"].Success ? match.Groups["Direction"].Value.Trim() : "";

            // בפורמט החדש, אם אין Message נפרד, נשתמש ב-Direction + Data כהודעה
            if (string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(direction))
            {
                message = direction;
                if (!string.IsNullOrEmpty(data))
                {
                    // אם ה-Data הוא JSON, נשמור אותו בשדה Data
                    if (!data.TrimStart().StartsWith("{") && !data.TrimStart().StartsWith("["))
                    {
                        message = $"{direction} {data}";
                        data = "";
                    }
                }
            }

            return new LogEntry
            {
                Date = date,
                // --- אופטימיזציה: שימוש ב-StringPool ---
                ThreadName = pool.Intern(match.Groups["Thread"].Value),
                Level = pool.Intern(match.Groups["Level"].Value.ToUpper()),
                Logger = pool.Intern(match.Groups["Logger"].Value.Trim()),
                Message = pool.Intern(message),
                ProcessName = pool.Intern("APP"),
                Method = pool.Intern(location),
                Pattern = string.IsNullOrEmpty(pattern) ? null : pool.Intern(pattern),
                Data = string.IsNullOrEmpty(data) ? null : pool.Intern(data),
                Exception = string.IsNullOrEmpty(exception) ? null : pool.Intern(exception)
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
        private class ZipEntryData
        {
            public string Name;
            public FileType Type;
            public MemoryStream Stream;
        }
        private double CalculatePercent(long processed, long total) => total == 0 ? 0 : Math.Min(99, ((double)processed / total) * 100);
    }
}