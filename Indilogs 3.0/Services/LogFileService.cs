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
                var perfTotal = Stopwatch.StartNew();
                var perfPhase = new Stopwatch();

                // יצירת Pool אחד לכל הסשן
                var stringPool = new StringPool();

                var session = new LogSessionData();
                // אתחול כל המילונים
                session.ConfigurationFiles = new Dictionary<string, string>();
                session.DatabaseFiles = new Dictionary<string, byte[]>();

                if (filePaths == null || filePaths.Length == 0) return session;

                // Expand folder paths to individual files
                var expandedPaths = new List<string>();
                foreach (var p in filePaths)
                {
                    if (Directory.Exists(p))
                    {
                        // Enumerate all relevant files in folder (recursive)
                        expandedPaths.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories));
                    }
                    else
                    {
                        expandedPaths.Add(p);
                    }
                }
                filePaths = expandedPaths.ToArray();

                var logsBag = new ConcurrentBag<LogEntry>();
                var transitionsBag = new ConcurrentBag<LogEntry>();
                var failuresBag = new ConcurrentBag<LogEntry>();
                var appDevLogsBag = new ConcurrentBag<LogEntry>();
                var eventsBag = new ConcurrentBag<EventEntry>();
                var screenshotsBag = new ConcurrentBag<BitmapImage>();

                // Merged result lists (populated from parallel processing, avoids per-item ConcurrentBag overhead)
                var mergedLogs = new List<LogEntry>();
                var mergedTrans = new List<LogEntry>();
                var mergedFails = new List<LogEntry>();
                var mergedApps = new List<LogEntry>();
                var mergedEvts = new List<EventEntry>();

                long totalBytesAllFiles = 0;
                foreach (var p in filePaths)
                    if (File.Exists(p)) totalBytesAllFiles += new FileInfo(p).Length;

                long processedBytesGlobal = 0;
                string detectedSwVersion = "Unknown";
                string detectedPlcVersion = "Unknown";
                var nonZipFiles = new List<ZipEntryData>();

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
                            perfPhase.Restart();
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                var filesToProcess = new List<ZipEntryData>();
                                int totalEntriesScanned = 0;
                                int entriesSkipped = 0;

                                foreach (var entry in archive.Entries)
                                {
                                    totalEntriesScanned++;
                                    if (entry.Length == 0) { entriesSkipped++; continue; }

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

                                Debug.WriteLine($"[PERF] Zip entry enumeration: {perfPhase.ElapsedMilliseconds}ms ({totalEntriesScanned} entries scanned, {entriesSkipped} skipped, {filesToProcess.Count} to process)");
                                perfPhase.Restart();

                                int totalFiles = filesToProcess.Count;
                                int processedCount = 0;
                                object progressLock = new object();

                                // עיבוד מקבילי - StringPool כעת Thread-Safe עם ConcurrentDictionary
                                // שימוש ב-Parallel.ForEach עם thread-local lists לביצועים מקסימליים
                                var localLogLists = new ConcurrentBag<List<LogEntry>>();
                                var localTransLists = new ConcurrentBag<List<LogEntry>>();
                                var localFailLists = new ConcurrentBag<List<LogEntry>>();
                                var localAppLists = new ConcurrentBag<List<LogEntry>>();
                                var localEvtLists = new ConcurrentBag<List<EventEntry>>();

                                Parallel.ForEach(filesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
                                {
                                    try
                                    {
                                        using (item.Stream)
                                        {
                                            if (item.Type == FileType.MainLog)
                                            {
                                                var result = ParseLogStream(item.Stream, stringPool);
                                                localLogLists.Add(result.AllLogs);
                                                if (result.Transitions.Count > 0) localTransLists.Add(result.Transitions);
                                                if (result.Failures.Count > 0) localFailLists.Add(result.Failures);
                                            }
                                            else if (item.Type == FileType.AppDevLog)
                                            {
                                                var logs = ParseAppDevLogStream(item.Stream, stringPool);
                                                if (logs.Count > 0) localAppLists.Add(logs);
                                            }
                                            else if (item.Type == FileType.EventsCsv)
                                            {
                                                var evts = ParseEventsCsv(item.Stream);
                                                if (evts.Count > 0) localEvtLists.Add(evts);
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

                                Debug.WriteLine($"[PERF] Parallel file parsing: {perfPhase.ElapsedMilliseconds}ms ({totalFiles} files)");
                                perfPhase.Restart();

                                // Merge thread-local lists into outer-scope merged lists
                                progress?.Report((85, "Merging results..."));
                                int totalLogCount = 0;
                                foreach (var l in localLogLists) totalLogCount += l.Count;
                                mergedLogs.Capacity = Math.Max(mergedLogs.Capacity, mergedLogs.Count + totalLogCount);
                                foreach (var l in localLogLists) mergedLogs.AddRange(l);

                                foreach (var l in localTransLists) mergedTrans.AddRange(l);
                                foreach (var l in localFailLists) mergedFails.AddRange(l);

                                int totalAppCount = 0;
                                foreach (var l in localAppLists) totalAppCount += l.Count;
                                mergedApps.Capacity = Math.Max(mergedApps.Capacity, mergedApps.Count + totalAppCount);
                                foreach (var l in localAppLists) mergedApps.AddRange(l);

                                foreach (var l in localEvtLists) mergedEvts.AddRange(l);

                                Debug.WriteLine($"[PERF] Merge phase: {perfPhase.ElapsedMilliseconds}ms (logs={mergedLogs.Count}, apps={mergedApps.Count}, events={mergedEvts.Count})");
                            }
                        }
                        else
                        {
                            // Collect non-ZIP files for batch parallel processing below
                            string lowerName = Path.GetFileName(filePath).ToLower();
                            string lowerPath = filePath.ToLower();

                            if (lowerName.Contains("enginegroupa.file") ||
                                lowerName.Contains("enginegroupb.file") ||
                                lowerName.EndsWith(".file.log", StringComparison.OrdinalIgnoreCase) ||
                                (lowerName.Contains("no-sn") && lowerName.Contains("file")))
                            {
                                nonZipFiles.Add(new ZipEntryData { Name = filePath, Type = FileType.MainLog });
                            }
                            else if ((lowerName.Contains("appdev") || lowerName.Contains("press.host.app")) &&
                                     (lowerPath.Contains("indigologs") || lowerPath.Contains("logger files")))
                            {
                                nonZipFiles.Add(new ZipEntryData { Name = filePath, Type = FileType.AppDevLog });
                            }
                            else if (lowerName.StartsWith("event-history__from") && lowerName.EndsWith(".csv"))
                            {
                                nonZipFiles.Add(new ZipEntryData { Name = filePath, Type = FileType.EventsCsv });
                            }
                            else if (lowerName.EndsWith(".db"))
                            {
                                try
                                {
                                    byte[] dbBytes = File.ReadAllBytes(filePath);
                                    string fileNameOnly = Path.GetFileName(filePath);
                                    if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                        session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                                }
                                catch { }
                            }
                            else if (lowerName.Equals("readme.txt"))
                            {
                                try
                                {
                                    session.PressConfiguration = File.ReadAllText(filePath);
                                    var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                    if (sw != "Unknown") detectedSwVersion = sw;
                                    if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                                }
                                catch { }
                            }
                            else if (lowerName.EndsWith("_setupinfo.json"))
                            {
                                try
                                {
                                    session.SetupInfo = File.ReadAllText(filePath);
                                    string plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                                    if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                                }
                                catch { }
                            }
                            else if (lowerName.EndsWith(".png") || lowerName.EndsWith(".jpg"))
                            {
                                try
                                {
                                    var bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.UriSource = new Uri(filePath);
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    screenshotsBag.Add(bmp);
                                }
                                catch { }
                            }
                            // Skip other file types (nginx logs, iVision logs, etc.)
                        }
                        processedBytesGlobal += currentFileSize;
                    }

                    // Process collected non-ZIP files in parallel
                    if (nonZipFiles.Count > 0)
                    {
                        int nzProcessed = 0;
                        int nzTotal = nonZipFiles.Count;
                        object nzLock = new object();

                        var nzLocalLogs = new ConcurrentBag<List<LogEntry>>();
                        var nzLocalTrans = new ConcurrentBag<List<LogEntry>>();
                        var nzLocalFails = new ConcurrentBag<List<LogEntry>>();
                        var nzLocalApps = new ConcurrentBag<List<LogEntry>>();
                        var nzLocalEvts = new ConcurrentBag<List<EventEntry>>();

                        Parallel.ForEach(nonZipFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
                        {
                            try
                            {
                                using (var fs = new FileStream(item.Name, FileMode.Open, FileAccess.Read, FileShare.Read, 262144))
                                {
                                    if (item.Type == FileType.MainLog)
                                    {
                                        var result = ParseLogStream(fs, stringPool);
                                        nzLocalLogs.Add(result.AllLogs);
                                        if (result.Transitions.Count > 0) nzLocalTrans.Add(result.Transitions);
                                        if (result.Failures.Count > 0) nzLocalFails.Add(result.Failures);
                                    }
                                    else if (item.Type == FileType.AppDevLog)
                                    {
                                        var logs = ParseAppDevLogStream(fs, stringPool);
                                        if (logs.Count > 0) nzLocalApps.Add(logs);
                                    }
                                    else if (item.Type == FileType.EventsCsv)
                                    {
                                        var evts = ParseEventsCsv(fs);
                                        if (evts.Count > 0) nzLocalEvts.Add(evts);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing file {item.Name}: {ex.Message}");
                            }
                            finally
                            {
                                lock (nzLock)
                                {
                                    nzProcessed++;
                                    if (nzProcessed % 5 == 0)
                                        progress?.Report((85.0 + (15.0 * nzProcessed / nzTotal), $"Parsing files: {nzProcessed}/{nzTotal}"));
                                }
                            }
                        });

                        // Merge non-ZIP results
                        foreach (var l in nzLocalLogs) mergedLogs.AddRange(l);
                        foreach (var l in nzLocalTrans) mergedTrans.AddRange(l);
                        foreach (var l in nzLocalFails) mergedFails.AddRange(l);
                        foreach (var l in nzLocalApps) mergedApps.AddRange(l);
                        foreach (var l in nzLocalEvts) mergedEvts.AddRange(l);
                    }

                    session.VersionsInfo = $"SW: {detectedSwVersion} | PLC: {detectedPlcVersion}";

                    // Merge any remaining ConcurrentBag entries (from non-ZIP single file paths)
                    // mergedLogs etc. are only populated from ZIP path; bags from single file path
                    if (!logsBag.IsEmpty) { foreach (var l in logsBag) mergedLogs.Add(l); }
                    if (!appDevLogsBag.IsEmpty) { foreach (var l in appDevLogsBag) mergedApps.Add(l); }
                    if (!transitionsBag.IsEmpty) { foreach (var l in transitionsBag) mergedTrans.Add(l); }
                    if (!failuresBag.IsEmpty) { foreach (var l in failuresBag) mergedFails.Add(l); }
                    if (!eventsBag.IsEmpty) { foreach (var l in eventsBag) mergedEvts.Add(l); }

                    // המרה ל-List סופי - מיון מהישן לחדש (חדשים למטה)
                    progress?.Report((90, $"Sorting {mergedLogs.Count:N0} logs..."));
                    perfPhase.Restart();

                    // Sort the 2 biggest lists in parallel using cached Comparison delegates
                    Comparison<LogEntry> dateComparer = (a, b) => a.Date.CompareTo(b.Date);
                    Comparison<EventEntry> eventComparer = (a, b) => a.Time.CompareTo(b.Time);

                    var sortTask1 = Task.Run(() =>
                    {
                        mergedLogs.Sort(dateComparer);
                        return mergedLogs;
                    });
                    var sortTask2 = Task.Run(() =>
                    {
                        mergedApps.Sort(dateComparer);
                        return mergedApps;
                    });

                    // Sort small lists on current thread while big ones sort in parallel
                    mergedTrans.Sort(dateComparer);
                    mergedFails.Sort(dateComparer);
                    mergedEvts.Sort(eventComparer);

                    session.Logs = sortTask1.Result;
                    session.AppDevLogs = sortTask2.Result;
                    session.StateTransitions = mergedTrans;
                    session.CriticalFailureEvents = mergedFails;
                    session.Events = mergedEvts;
                    session.Screenshots = screenshotsBag.ToList();

                    Debug.WriteLine($"[PERF] Sort phase: {perfPhase.ElapsedMilliseconds}ms");
                    Debug.WriteLine($"[PERF] === LoadSessionAsync TOTAL: {perfTotal.ElapsedMilliseconds}ms ===");
                    Debug.WriteLine($"[PERF]   PLC logs: {session.Logs.Count:N0}, APP logs: {session.AppDevLogs?.Count ?? 0:N0}, Events: {session.Events?.Count ?? 0:N0}, Screenshots: {session.Screenshots?.Count ?? 0:N0}");
                    Debug.WriteLine($"[PERF]   Transitions: {session.StateTransitions?.Count ?? 0:N0}, Failures: {session.CriticalFailureEvents?.Count ?? 0:N0}");
                    Debug.WriteLine($"[PERF]   Total file size: {totalBytesAllFiles / 1024.0 / 1024.0:F1} MB");

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
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),
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

            // Pre-allocate based on estimated entries (~200 bytes per log entry in binary format)
            int estimatedEntries = stream.CanSeek ? (int)Math.Min(stream.Length / 200, 500000) : 10000;
            var allLogs = new List<LogEntry>(estimatedEntries);
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

                        string message = reader.Current.Message ?? "";
                        string threadName = pool.Intern(reader.Current.ThreadName ?? "");

                        var entry = new LogEntry
                        {
                            // Only intern repetitive fields (Level, ThreadName, Logger, ProcessName)
                            // Message is unique per log - interning wastes ConcurrentDictionary overhead
                            Level = pool.Intern(reader.Current.Level?.ToString() ?? "Info"),
                            Date = reader.Current.Time,
                            Message = message,
                            ThreadName = threadName,
                            Logger = pool.Intern(reader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
                        };

                        allLogs.Add(entry);

                        if (threadName == "Manager" &&
                            message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                            message.Contains("->"))
                        {
                            transitions.Add(entry);
                        }
                        else if (threadName == "Events" &&
                                 message.Contains("PLC_FAILURE_STATE_CHANGE"))
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
                // Only intern repetitive fields, not unique content (Message, Data, Exception)
                ThreadName = pool.Intern(match.Groups["Thread"].Value),
                Level = pool.Intern(match.Groups["Level"].Value.ToUpper()),
                Logger = pool.Intern(match.Groups["Logger"].Value.Trim()),
                Message = message,
                ProcessName = pool.Intern("APP"),
                Method = pool.Intern(location),
                Pattern = string.IsNullOrEmpty(pattern) ? null : pattern,
                Data = string.IsNullOrEmpty(data) ? null : data,
                Exception = string.IsNullOrEmpty(exception) ? null : exception
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
            // Pre-allocate with known size to avoid resizing, use 128KB buffer for speed
            var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            using (var stream = entry.Open())
            {
                stream.CopyTo(ms, 131072);
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