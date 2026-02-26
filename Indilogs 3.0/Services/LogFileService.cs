using DocumentFormat.OpenXml.ExtendedProperties;
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class LogFileService : ILogFileService
    {
        // -----------------------------------------------------------------------
        // Plugin loader — injected via DI; null-safe (no plugins = graceful skip)
        // -----------------------------------------------------------------------
        private readonly IPluginLoader _pluginLoader;

        public LogFileService(IPluginLoader pluginLoader)
        {
            _pluginLoader = pluginLoader;
        }

        /// <summary>Exposes the plugin loader for external callers (e.g. dialog filter building).</summary>
        public IPluginLoader GetPluginLoader() => _pluginLoader;

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
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\x1e" +
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
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // Regex לפרסור לוגים של אפליקציה - פורמט חדש עם | כמפריד
        // Format: 2026-01-29 10:32:38,073 |Thread| |RootIFlowId| |IFlowId| |IFlowName| |Pattern| |Context| LEVEL  Logger
        // Next line: |Method|
        // Next lines: --> or <-- or message text, followed by optional data/JSON, ending with ||
        private readonly Regex _appDevRegexPipe = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\s*\|(?<Thread>[^|]*)\|\s*\|(?<RootIFlowId>[^|]*)\|\s*\|(?<IFlowId>[^|]*)\|\s*\|(?<IFlowName>[^|]*)\|\s*\|(?<Pattern>[^|]*)\|\s*\|(?<Context>[^|]*)\|\s*(?<Level>\w+)\s+(?<Logger>[^\r\n]*)[\r\n]+\|(?<Location>[^|]*)\|[\r\n]+(?<Message>.*?)\s*\|\|",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

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
                session.TerminalLogFiles = new Dictionary<string, string>(); // אותחל המילון לטרמינלים (.txt/.log כמחרוזות)
                session.TerminalCsvBytes = new Dictionary<string, byte[]>(); // CSV כ-byte[] לפענוח דחוי
                session.GlobalsFiles = new Dictionary<string, string>(); // אותחל המילון לגלובלים
                session.SystabFiles = new Dictionary<string, string>(); // אותחל המילון לסיסטאב

                if (filePaths == null || filePaths.Length == 0) return session;

                // הרחבת נתיבים לקבצים בודדים
                var expandedPaths = new List<string>();
                foreach (var p in filePaths)
                {
                    if (Directory.Exists(p))
                    {
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
                var nonInfoScreenshotsBag = new ConcurrentBag<BitmapImage>(); // Screenshots NOT from \Info path

                // רשימות למיזוג סופי
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
                bool hasBinaryAppLogs = false;
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
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                var filesToProcess = new List<ZipEntryData>();
                                var innerZipStreams = new List<(MemoryStream Stream, string Name)>(); // Nested ZIP support

                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.Length == 0) continue;

                                    string lowerName = entry.FullName.ToLower();

                                    // סינון אגרסיבי
                                    if (lowerName.Contains("/backup/") || lowerName.Contains("\\backup\\") ||
                                        lowerName.Contains("/old/") || lowerName.Contains("\\old\\") ||
                                        lowerName.Contains("/temp/") || lowerName.Contains("\\temp\\") ||
                                        lowerName.Contains("/archive/") || lowerName.Contains("\\archive\\"))
                                    {
                                        continue;
                                    }

                                    bool shouldProcess = false;
                                    var entryData = new ZipEntryData { Name = entry.Name };

                                    // 0. בדיקה לקבצי Globals XML בתיקיית DataManagement\eCommon\Globals (לפני Configuration)
                                    if (IsGlobalsXmlFile(lowerName))
                                    {
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);
                                            using (var ms = CopyToMemory(entry))
                                            using (var r = new StreamReader(ms))
                                            {
                                                string content = r.ReadToEnd();
                                                if (!session.GlobalsFiles.ContainsKey(fileNameOnly))
                                                {
                                                    session.GlobalsFiles.Add(fileNameOnly, content);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading globals XML file failed: {ex.Message}");
                                        }
                                        continue;
                                    }

                                    // 0b. Systab files in DiagnosticsLogs folder (systab_saved.txt, systab_default.txt, etc.)
                                    if (IsSystabFile(lowerName))
                                    {
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name).ToLower();
                                            string systabKey = null;
                                            if (fileNameOnly.Contains("saved")) systabKey = "saved";
                                            else if (fileNameOnly.Contains("default")) systabKey = "default";
                                            else if (fileNameOnly.Contains("minimum")) systabKey = "minimum";
                                            else if (fileNameOnly.Contains("maximum")) systabKey = "maximum";

                                            if (systabKey != null && !session.SystabFiles.ContainsKey(systabKey))
                                            {
                                                using (var ms = CopyToMemory(entry))
                                                {
                                                    // Try UTF-16 LE first (standard .reg format), then UTF-8
                                                    string content;
                                                    ms.Position = 0;
                                                    byte[] bom = new byte[2];
                                                    int bytesRead = ms.Read(bom, 0, 2);
                                                    ms.Position = 0;

                                                    if (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                                                    {
                                                        using (var sr = new StreamReader(ms, Encoding.Unicode, false, 1024, leaveOpen: true))
                                                            content = sr.ReadToEnd();
                                                    }
                                                    else
                                                    {
                                                        using (var sr = new StreamReader(ms, Encoding.UTF8, false, 1024, leaveOpen: true))
                                                            content = sr.ReadToEnd();
                                                    }

                                                    session.SystabFiles.Add(systabKey, content);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LogFileService] Systab extraction failed: {ex.Message}");
                                        }
                                        continue;
                                    }

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
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading configuration file failed: {ex.Message}");
                                        }
                                        continue;
                                    }

                                    // 1b. Terminal log files (קבצים בתיקיות TerminalLogs ו-LRS)
                                    bool isTerminalLog = lowerName.Contains("/terminallogs/") ||
                                                         lowerName.Contains("\\terminallogs\\") ||
                                                         lowerName.Contains("\\terminallogs/") ||
                                                         lowerName.Contains("/terminallogs\\") ||
                                                         lowerName.StartsWith("terminallogs/") ||
                                                         lowerName.StartsWith("terminallogs\\");

                                    // LRS folder — treat as terminal path, but exclude engineGroupA logs and nested ZIPs
                                    bool isLrsPath = lowerName.Contains("/lrs/") ||
                                                     lowerName.Contains("\\lrs\\") ||
                                                     lowerName.Contains("\\lrs/") ||
                                                     lowerName.Contains("/lrs\\") ||
                                                     lowerName.StartsWith("lrs/") ||
                                                     lowerName.StartsWith("lrs\\");
                                    if (isLrsPath &&
                                        entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) < 0 &&
                                        !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        isTerminalLog = true;
                                    }

                                    if (isTerminalLog)
                                    {
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);
                                            if (!string.IsNullOrEmpty(fileNameOnly))
                                            {
                                                string ext = Path.GetExtension(fileNameOnly);
                                                if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    // CSV files: store as raw bytes (deferred string conversion)
                                                    // Avoids expensive UTF-8 → string decode during ZIP loading
                                                    if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                                    {
                                                        using (var ms = CopyToMemory(entry))
                                                            session.TerminalCsvBytes.Add(fileNameOnly, ms.ToArray());
                                                    }
                                                }
                                                else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                                         ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    // Text/log files: store as string for TERMINALS tab display
                                                    if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                                    {
                                                        using (var ms = CopyToMemory(entry))
                                                        using (var r = new StreamReader(ms))
                                                            session.TerminalLogFiles.Add(fileNameOnly, r.ReadToEnd());
                                                    }
                                                }
                                                // Skip .arl and other non-essential formats entirely
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading terminal log file failed: {ex.Message}");
                                        }
                                        continue;
                                    }

                                    // 2. לוגים ראשיים (exclude .zip — those are nested archives, not log files)
                                    if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
                                    // 3b. APP binary logs
                                    else if (IsNumericAppFile(entry.Name))
                                    {
                                        entryData.Type = FileType.AppBinaryLog;
                                        shouldProcess = true;
                                        hasBinaryAppLogs = true;
                                    }
                                    // 4. Events CSV
                                    else if ((entry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) &&
                                              entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                                             (Path.GetFileName(entry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) &&
                                              entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        entryData.Type = FileType.EventsCsv;
                                        shouldProcess = true;
                                    }
                                    // 5. קבצי .db
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
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading database file from ZIP failed: {ex.Message}");
                                        }
                                        continue;
                                    }
                                    // 6. תמונות - split into Info vs non-Info for S4-5 filtering
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var bmp = LoadBitmapFromZip(entry);
                                        if (bmp != null)
                                        {
                                            bool isInfoPath = lowerName.Contains("/info/") || lowerName.Contains("\\info\\") ||
                                                              lowerName.StartsWith("info/") || lowerName.StartsWith("info\\");
                                            if (isInfoPath)
                                                screenshotsBag.Add(bmp);
                                            else
                                                nonInfoScreenshotsBag.Add(bmp);
                                        }
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
                                    // 8b. Nested ZIP — extract inner ZIP for recursive processing
                                    else if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try
                                        {
                                            var innerMs = CopyToMemory(entry);
                                            innerZipStreams.Add((innerMs, entry.FullName));
                                        }
                                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Extracting nested ZIP failed: {ex.Message}"); }
                                        continue;
                                    }
                                    else
                                    {
                                        // 8. Plugin fallback — offer unrecognised ZIP entries to registered plugins
                                        // OPTIMIZATION: only attempt plugin fallback for text-like extensions
                                        // to avoid expensive CopyToMemory on large binary files (.dll, .exe, .dat, .arl, etc.)
                                        if (_pluginLoader != null && _pluginLoader.Plugins.Count > 0 &&
                                            IsPluginCandidateExtension(entry.Name))
                                        {
                                            var ms = CopyToMemory(entry);
                                            string[] sample = ReadSampleLines(ms, 20);
                                            ILogFilePlugin plugin = FindPlugin(entry.Name, sample);
                                            if (plugin != null)
                                            {
                                                entryData.Type    = FileType.Plugin;
                                                entryData.Plugin  = plugin;
                                                entryData.Stream  = ms;  // ownership transferred
                                                entryData.Context = new ParseContext
                                                {
                                                    FileName     = entry.Name,
                                                    IsInsideZip  = true,
                                                    ZipEntryPath = entry.FullName
                                                };
                                                filesToProcess.Add(entryData);
                                                if (session.PluginColumns == null)
                                                {
                                                    try { session.PluginColumns = plugin.GetColumns(); }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Plugin GetColumns failed: {ex.Message}"); }
                                                }
                                            }
                                            else
                                            {
                                                ms.Dispose();  // no plugin claimed it — free the buffer
                                            }
                                        }
                                        continue;  // do NOT fall through to the shouldProcess block below
                                    }

                                    if (shouldProcess)
                                    {
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                }

                                // --- Nested ZIP processing: extract and classify entries from inner ZIPs ---
                                foreach (var (innerStream, innerZipName) in innerZipStreams)
                                {
                                    try
                                    {
                                        innerStream.Position = 0;
                                        using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false))
                                        {
                                            foreach (var innerEntry in innerArchive.Entries)
                                            {
                                                if (innerEntry.Length == 0) continue;
                                                string innerLower = innerEntry.FullName.ToLower();

                                                // Skip backup/temp/archive folders
                                                if (innerLower.Contains("/backup/") || innerLower.Contains("\\backup\\") ||
                                                    innerLower.Contains("/old/") || innerLower.Contains("\\old\\") ||
                                                    innerLower.Contains("/temp/") || innerLower.Contains("\\temp\\") ||
                                                    innerLower.Contains("/archive/") || innerLower.Contains("\\archive\\"))
                                                    continue;

                                                // Prefix with inner ZIP name for collision avoidance
                                                string prefixedName = $"{Path.GetFileNameWithoutExtension(innerZipName)}/{innerEntry.Name}";

                                                // Globals XML
                                                if (IsGlobalsXmlFile(innerLower))
                                                {
                                                    try
                                                    {
                                                        using (var ms = CopyToMemory(innerEntry))
                                                        using (var r = new StreamReader(ms))
                                                        {
                                                            string content = r.ReadToEnd();
                                                            string key = prefixedName;
                                                            if (!session.GlobalsFiles.ContainsKey(key))
                                                                session.GlobalsFiles.Add(key, content);
                                                        }
                                                    }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading inner ZIP globals file failed: {ex.Message}"); }
                                                    continue;
                                                }

                                                // Systab files
                                                if (IsSystabFile(innerLower))
                                                {
                                                    try
                                                    {
                                                        string fileNameOnly = Path.GetFileName(innerEntry.Name).ToLower();
                                                        string systabKey = null;
                                                        if (fileNameOnly.Contains("saved")) systabKey = "saved";
                                                        else if (fileNameOnly.Contains("default")) systabKey = "default";
                                                        else if (fileNameOnly.Contains("minimum")) systabKey = "minimum";
                                                        else if (fileNameOnly.Contains("maximum")) systabKey = "maximum";

                                                        if (systabKey != null && !session.SystabFiles.ContainsKey(systabKey))
                                                        {
                                                            using (var ms = CopyToMemory(innerEntry))
                                                            {
                                                                ms.Position = 0;
                                                                byte[] bom = new byte[2];
                                                                int bytesRead = ms.Read(bom, 0, 2);
                                                                ms.Position = 0;
                                                                string content = (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                                                                    ? new StreamReader(ms, Encoding.Unicode).ReadToEnd()
                                                                    : new StreamReader(ms, Encoding.UTF8).ReadToEnd();
                                                                session.SystabFiles.Add(systabKey, content);
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading inner ZIP systab file failed: {ex.Message}"); }
                                                    continue;
                                                }

                                                // Configuration files
                                                bool isConfig = innerLower.Contains("/configuration/") || innerLower.Contains("\\configuration\\") ||
                                                                innerLower.StartsWith("configuration/") || innerLower.StartsWith("configuration\\");
                                                if (isConfig)
                                                {
                                                    try
                                                    {
                                                        string fName = Path.GetFileName(innerEntry.Name);
                                                        if (fName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            using (var ms = CopyToMemory(innerEntry))
                                                            {
                                                                if (!session.DatabaseFiles.ContainsKey(fName))
                                                                    session.DatabaseFiles.Add(fName, ms.ToArray());
                                                            }
                                                        }
                                                        else
                                                        {
                                                            using (var ms = CopyToMemory(innerEntry))
                                                            using (var r = new StreamReader(ms))
                                                            {
                                                                string key = prefixedName;
                                                                if (!session.ConfigurationFiles.ContainsKey(key))
                                                                    session.ConfigurationFiles.Add(key, r.ReadToEnd());
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading inner ZIP configuration file failed: {ex.Message}"); }
                                                    continue;
                                                }

                                                // Terminal logs (TerminalLogs + LRS folders)
                                                bool isTerminal = innerLower.Contains("/terminallogs/") || innerLower.Contains("\\terminallogs\\") ||
                                                                  innerLower.StartsWith("terminallogs/") || innerLower.StartsWith("terminallogs\\");

                                                // LRS folder — treat as terminal, exclude engineGroupA and nested ZIPs
                                                bool innerIsLrs = innerLower.Contains("/lrs/") || innerLower.Contains("\\lrs\\") ||
                                                                  innerLower.StartsWith("lrs/") || innerLower.StartsWith("lrs\\");
                                                if (innerIsLrs &&
                                                    innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) < 0 &&
                                                    !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    isTerminal = true;
                                                }

                                                if (isTerminal)
                                                {
                                                    try
                                                    {
                                                        string fName = Path.GetFileName(innerEntry.Name);
                                                        if (!string.IsNullOrEmpty(fName))
                                                        {
                                                            string ext = Path.GetExtension(fName);
                                                            string key = prefixedName;
                                                            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                // CSV: store as raw bytes (deferred string conversion)
                                                                if (!session.TerminalCsvBytes.ContainsKey(key))
                                                                {
                                                                    using (var ms = CopyToMemory(innerEntry))
                                                                        session.TerminalCsvBytes.Add(key, ms.ToArray());
                                                                }
                                                            }
                                                            else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                                                     ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                // Text/log: store as string for display
                                                                if (!session.TerminalLogFiles.ContainsKey(key))
                                                                {
                                                                    using (var ms = CopyToMemory(innerEntry))
                                                                    using (var r = new StreamReader(ms))
                                                                        session.TerminalLogFiles.Add(key, r.ReadToEnd());
                                                                }
                                                            }
                                                            // Skip .arl and other formats
                                                        }
                                                    }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading inner ZIP terminal log failed: {ex.Message}"); }
                                                    continue;
                                                }

                                                // Main PLC logs
                                                var innerData = new ZipEntryData { Name = innerEntry.Name };
                                                bool innerShouldProcess = false;

                                                if (innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                    !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    innerData.Type = FileType.MainLog;
                                                    innerShouldProcess = true;
                                                }
                                                // APP dev logs
                                                else if ((innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                                         (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                                                {
                                                    innerData.Type = FileType.AppDevLog;
                                                    innerShouldProcess = true;
                                                }
                                                // APP binary logs
                                                else if (IsNumericAppFile(innerEntry.Name))
                                                {
                                                    innerData.Type = FileType.AppBinaryLog;
                                                    innerShouldProcess = true;
                                                    hasBinaryAppLogs = true;
                                                }
                                                // Events CSV
                                                else if ((innerEntry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) &&
                                                          innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                                                         (Path.GetFileName(innerEntry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) &&
                                                          innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                                                {
                                                    innerData.Type = FileType.EventsCsv;
                                                    innerShouldProcess = true;
                                                }
                                                // DB files
                                                else if (innerEntry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    try
                                                    {
                                                        string fName = Path.GetFileName(innerEntry.Name);
                                                        using (var ms = CopyToMemory(innerEntry))
                                                        {
                                                            if (!session.DatabaseFiles.ContainsKey(fName))
                                                                session.DatabaseFiles.Add(fName, ms.ToArray());
                                                        }
                                                    }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading inner ZIP database file failed: {ex.Message}"); }
                                                    continue;
                                                }
                                                // Screenshots
                                                else if (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                         innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    var bmp = LoadBitmapFromZip(innerEntry);
                                                    if (bmp != null)
                                                    {
                                                        bool isInfoPath = innerLower.Contains("/info/") || innerLower.Contains("\\info\\") ||
                                                                          innerLower.StartsWith("info/") || innerLower.StartsWith("info\\");
                                                        if (isInfoPath)
                                                            screenshotsBag.Add(bmp);
                                                        else
                                                            nonInfoScreenshotsBag.Add(bmp);
                                                    }
                                                    continue;
                                                }
                                                // Readme
                                                else if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    using (var ms = CopyToMemory(innerEntry))
                                                    using (var r = new StreamReader(ms))
                                                    {
                                                        if (string.IsNullOrEmpty(session.PressConfiguration))
                                                        {
                                                            session.PressConfiguration = r.ReadToEnd();
                                                            var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                                            if (sw != "Unknown") detectedSwVersion = sw;
                                                            if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                                                        }
                                                    }
                                                    continue;
                                                }

                                                if (innerShouldProcess)
                                                {
                                                    innerData.Stream = CopyToMemory(innerEntry);
                                                    filesToProcess.Add(innerData);
                                                }
                                            }
                                        }

                                        System.Diagnostics.Debug.WriteLine($"[Nested ZIP] Processed inner ZIP: {innerZipName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[Nested ZIP] Error processing {innerZipName}: {ex.Message}");
                                    }
                                }
                                // --- End nested ZIP processing ---

                                int totalFiles = filesToProcess.Count;
                                int processedCount = 0;

                                // עיבוד מקבילי
                                var localLogLists = new ConcurrentBag<List<LogEntry>>();
                                var localTransLists = new ConcurrentBag<List<LogEntry>>();
                                var localFailLists = new ConcurrentBag<List<LogEntry>>();
                                var localAppLists = new ConcurrentBag<List<LogEntry>>();
                                var localEvtLists = new ConcurrentBag<List<EventEntry>>();
                                var csvLock = new object();

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
                                            else if (item.Type == FileType.AppBinaryLog)
                                            {
                                                var result = ParseLogStream(item.Stream, stringPool);
                                                foreach (var log in result.AllLogs)
                                                    log.ProcessName = stringPool.Intern("APP");
                                                if (result.AllLogs.Count > 0) localAppLists.Add(result.AllLogs);
                                            }
                                            else if (item.Type == FileType.AppDevLog)
                                            {
                                                var logs = ParseAppDevLogStream(item.Stream, stringPool);
                                                if (logs.Count > 0) localAppLists.Add(logs);
                                            }
                                            else if (item.Type == FileType.EventsCsv)
                                            {
                                                // Save raw CSV content for full-column display
                                                item.Stream.Position = 0;
                                                using (var sr = new StreamReader(item.Stream, Encoding.UTF8, true, 1024, true))
                                                {
                                                    string rawCsv = sr.ReadToEnd();
                                                    lock (csvLock)
                                                    {
                                                        if (string.IsNullOrEmpty(session.EventsCsvRawContent))
                                                            session.EventsCsvRawContent = rawCsv;
                                                    }
                                                }
                                                item.Stream.Position = 0;
                                                var evts = ParseEventsCsv(item.Stream);
                                                if (evts.Count > 0) localEvtLists.Add(evts);
                                            }
                                            else if (item.Type == FileType.Plugin && item.Plugin != null)
                                            {
                                                // Plugin-parsed ZIP entry
                                                item.Stream.Position = 0;
                                                var plcLogs = new List<LogEntry>();
                                                var appLogs = new List<LogEntry>();
                                                DispatchPluginResults(item.Plugin, item.Stream, item.Context, stringPool, plcLogs, appLogs);
                                                if (plcLogs.Count > 0) localLogLists.Add(plcLogs);
                                                if (appLogs.Count > 0) localAppLists.Add(appLogs);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[LogFileService] Parallel log file processing failed: {ex.Message}");
                                    }
                                    finally
                                    {
                                        {
                                            int count = System.Threading.Interlocked.Increment(ref processedCount);
                                            if (count % 3 == 0)
                                            {
                                                double ratio = (double)count / totalFiles;
                                                double fileProg = (0.5 + (ratio * 0.5)) * currentFileSize;
                                                double totalP = ((processedBytesGlobal + fileProg) / totalBytesAllFiles) * 100;
                                                progress?.Report((Math.Min(99, totalP), $"Parsing files: {count}/{totalFiles}"));
                                            }
                                        }
                                    }
                                });

                                // Merge
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
                            }
                        }
                        else
                        {
                            // טיפול בקבצים שאינם ZIP
                            string lowerName = Path.GetFileName(filePath).ToLower();
                            string lowerPath = filePath.ToLower();

                            // === כאן הבדיקה לקבצי ה-TERMINAL המיוחדים בתיקייה רגילה ===
                            if (IsCustomTerminalLog(filePath)) // מעבירים את הנתיב, הפונקציה תחליט
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(filePath);
                                    string ext = Path.GetExtension(fileNameOnly);
                                    if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // CSV: store as raw bytes (deferred string conversion)
                                        if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                            session.TerminalCsvBytes.Add(fileNameOnly, File.ReadAllBytes(filePath));
                                    }
                                    else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                             ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                            session.TerminalLogFiles.Add(fileNameOnly, File.ReadAllText(filePath));
                                    }
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading loose terminal log file failed: {ex.Message}"); }
                            }
                            // ===========================================================

                            else if (lowerName.Contains("enginegroupa.file") ||
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
                            else if (IsNumericAppFile(lowerName))
                            {
                                nonZipFiles.Add(new ZipEntryData { Name = filePath, Type = FileType.AppBinaryLog });
                                hasBinaryAppLogs = true;
                            }
                            else if ((lowerName.StartsWith("event-history__from") || lowerName.StartsWith("pressevents.")) && lowerName.EndsWith(".csv"))
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
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Reading loose database file failed: {ex.Message}"); }
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
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Parsing readme.txt versions failed: {ex.Message}"); }
                            }
                            else if (lowerName.EndsWith("_setupinfo.json"))
                            {
                                try
                                {
                                    session.SetupInfo = File.ReadAllText(filePath);
                                    string plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                                    if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Parsing setup info JSON failed: {ex.Message}"); }
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
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Loading screenshot image failed: {ex.Message}"); }
                            }
                            else
                            {
                                // Plugin fallback for unrecognised non-ZIP files
                                // Only attempt for text-like extensions to avoid wasting I/O on binaries
                                if (_pluginLoader != null && _pluginLoader.Plugins.Count > 0 &&
                                    IsPluginCandidateExtension(filePath))
                                {
                                    string fileName = Path.GetFileName(filePath);
                                    string[] sample = ReadSampleLinesFromFile(filePath, 20);
                                    ILogFilePlugin plugin = FindPlugin(fileName, sample);
                                    if (plugin != null)
                                    {
                                        nonZipFiles.Add(new ZipEntryData
                                        {
                                            Name    = filePath,
                                            Type    = FileType.Plugin,
                                            Plugin  = plugin,
                                            Context = new ParseContext
                                            {
                                                FileName    = fileName,
                                                FilePath    = filePath,
                                                IsInsideZip = false
                                            }
                                        });
                                        // Capture the plugin's column layout (first plugin wins)
                                        if (session.PluginColumns == null)
                                        {
                                            try { session.PluginColumns = plugin.GetColumns(); }
                                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Plugin GetColumns for loose file failed: {ex.Message}"); }
                                        }
                                    }
                                }
                            }
                        }
                        processedBytesGlobal += currentFileSize;
                    }

                    // עיבוד מקבילי לקבצים רגילים
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
                                using (var fs = new FileStream(item.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144))
                                {
                                    if (item.Type == FileType.MainLog)
                                    {
                                        var result = ParseLogStream(fs, stringPool);
                                        nzLocalLogs.Add(result.AllLogs);
                                        if (result.Transitions.Count > 0) nzLocalTrans.Add(result.Transitions);
                                        if (result.Failures.Count > 0) nzLocalFails.Add(result.Failures);
                                    }
                                    else if (item.Type == FileType.AppBinaryLog)
                                    {
                                        var result = ParseLogStream(fs, stringPool);
                                        foreach (var log in result.AllLogs)
                                            log.ProcessName = stringPool.Intern("APP");
                                        if (result.AllLogs.Count > 0) nzLocalApps.Add(result.AllLogs);
                                    }
                                    else if (item.Type == FileType.AppDevLog)
                                    {
                                        var logs = ParseAppDevLogStream(fs, stringPool);
                                        if (logs.Count > 0) nzLocalApps.Add(logs);
                                    }
                                    else if (item.Type == FileType.EventsCsv)
                                    {
                                        // Save raw CSV content for full-column display
                                        fs.Position = 0;
                                        using (var sr = new StreamReader(fs, Encoding.UTF8, true, 1024, true))
                                        {
                                            string rawCsv = sr.ReadToEnd();
                                            if (string.IsNullOrEmpty(session.EventsCsvRawContent))
                                                session.EventsCsvRawContent = rawCsv;
                                        }
                                        fs.Position = 0;
                                        var evts = ParseEventsCsv(fs);
                                        if (evts.Count > 0) nzLocalEvts.Add(evts);
                                    }
                                    else if (item.Type == FileType.Plugin && item.Plugin != null)
                                    {
                                        // Plugin-parsed non-ZIP file
                                        var plcLogs = new List<LogEntry>();
                                        var appLogs = new List<LogEntry>();
                                        DispatchPluginResults(item.Plugin, fs, item.Context, stringPool, plcLogs, appLogs);
                                        if (plcLogs.Count > 0) nzLocalLogs.Add(plcLogs);
                                        if (appLogs.Count > 0) nzLocalApps.Add(appLogs);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        $"Error processing {item.Name}: {ex.GetType().Name}: {ex.Message}",
                                        "File Processing Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                });
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

                        foreach (var l in nzLocalLogs) mergedLogs.AddRange(l);
                        foreach (var l in nzLocalTrans) mergedTrans.AddRange(l);
                        foreach (var l in nzLocalFails) mergedFails.AddRange(l);
                        foreach (var l in nzLocalApps) mergedApps.AddRange(l);
                        foreach (var l in nzLocalEvts) mergedEvts.AddRange(l);
                    }

                    session.VersionsInfo = $"SW: {detectedSwVersion} | PLC: {detectedPlcVersion}";
                    session.HasBinaryAppLogs = hasBinaryAppLogs;

                    // --- S6 vs S4-5 auto-detection when no binary APP logs ---
                    // If no binary APP files were found AND no APP log entries exist,
                    // determine configuration type by checking PLC logs for ThreadName "Manager":
                    //   - "Manager" thread found → S6
                    //   - "Manager" thread NOT found → S4-5 (set HasBinaryAppLogs = true to switch UI)
                    if (!hasBinaryAppLogs && mergedApps.Count == 0)
                    {
                        bool hasManagerThread = false;
                        foreach (var log in mergedLogs)
                        {
                            if (log.ThreadName != null &&
                                log.ThreadName.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hasManagerThread = true;
                                break;
                            }
                        }

                        if (hasManagerThread)
                        {
                            // S6 configuration — keep HasBinaryAppLogs = false
                            session.ConfigurationType = "S6";
                        }
                        else
                        {
                            // S4-5 configuration — switch UI to TERMINALS / PLC-FW headers
                            session.HasBinaryAppLogs = true;
                            session.ConfigurationType = "S4-5";
                        }

                        System.Diagnostics.Debug.WriteLine(
                            $"[S6/S4-5 Detection] No APP logs found. Manager thread: {hasManagerThread} → {session.ConfigurationType}");
                    }

                    if (!logsBag.IsEmpty) { foreach (var l in logsBag) mergedLogs.Add(l); }
                    if (!appDevLogsBag.IsEmpty) { foreach (var l in appDevLogsBag) mergedApps.Add(l); }
                    if (!transitionsBag.IsEmpty) { foreach (var l in transitionsBag) mergedTrans.Add(l); }
                    if (!failuresBag.IsEmpty) { foreach (var l in failuresBag) mergedFails.Add(l); }
                    if (!eventsBag.IsEmpty) { foreach (var l in eventsBag) mergedEvts.Add(l); }

                    // מיון סופי — in-place sort + all 5 sorts in parallel for maximum throughput
                    progress?.Report((90, $"Sorting {mergedLogs.Count:N0} logs..."));

                    Comparison<LogEntry> dateComparer = (a, b) => a.Date.CompareTo(b.Date);
                    Comparison<EventEntry> eventComparer = (a, b) => a.Time.CompareTo(b.Time);

                    // In-place List.Sort (IntroSort) — avoids allocating duplicate lists
                    // Parallel.Invoke uses the thread pool efficiently without extra Task overhead
                    Parallel.Invoke(
                        () => mergedLogs.Sort(dateComparer),
                        () => mergedApps.Sort(dateComparer),
                        () => mergedTrans.Sort(dateComparer),
                        () => mergedFails.Sort(dateComparer),
                        () => mergedEvts.Sort(eventComparer)
                    );

                    session.Logs = mergedLogs;
                    session.AppDevLogs = mergedApps;
                    session.StateTransitions = mergedTrans;
                    session.CriticalFailureEvents = mergedFails;
                    session.Events = mergedEvts;
                    // For S4-5 (binary APP): only show screenshots from \Info path
                    // For S6 (non-binary): show all screenshots
                    if (!hasBinaryAppLogs)
                    {
                        foreach (var bmp in nonInfoScreenshotsBag)
                            screenshotsBag.Add(bmp);
                    }
                    session.Screenshots = screenshotsBag.ToList();

                    progress?.Report((100, "Done"));
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"Fatal error during file loading:\n\n{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                            "Loading Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }

                return session;
            });
        }





        // מתודת עזר לזיהוי קבצי טרמינל מיוחדים (קבצים שנמצאים מחוץ לתיקיית TerminalLogs אבל שייכים לטרמינלים)
        private bool IsCustomTerminalLog(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = Path.GetFileName(fileName);

            // קבצי טרמינל לפי שם ספציפי
            return name.StartsWith("whel3", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("ecm", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("COM1", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("0001", StringComparison.OrdinalIgnoreCase) ||
                   // קבצי IO sensor CSV (Io-BIM, Io-ECM, Io-PDC, Io-WHEL)
                   name.StartsWith("Io-", StringComparison.OrdinalIgnoreCase) ||
                   // קבצי Stability CSV
                   name.StartsWith("Stab-", StringComparison.OrdinalIgnoreCase) ||
                   // קבצי PRE/POST analysis
                   name.StartsWith("PRE_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("POST_", StringComparison.OrdinalIgnoreCase);
        }

        // מתודת עזר לזיהוי קבצי Systab בנתיב DiagnosticsLogs
        private static bool IsSystabFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inDiagPath = lowerFullName.Contains("/diagnosticslogs/") ||
                              lowerFullName.Contains("\\diagnosticslogs\\") ||
                              lowerFullName.StartsWith("diagnosticslogs/") ||
                              lowerFullName.StartsWith("diagnosticslogs\\");
            string fileName = Path.GetFileName(lowerFullName);
            return inDiagPath && fileName.StartsWith("systab_") && fileName.EndsWith(".txt");
        }

        /// <summary>
        /// Returns true if the file extension is a text/log format that plugins might handle.
        /// Avoids expensive CopyToMemory on binary files (.dll, .exe, .dat, .arl, etc.)
        /// that no plugin will ever parse.
        /// </summary>
        private static bool IsPluginCandidateExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) return false;
            switch (ext.ToLowerInvariant())
            {
                case ".log": case ".txt": case ".csv": case ".file":
                case ".json": case ".xml": case ".cfg": case ".ini":
                case ".config": case ".tsv":
                    return true;
                default:
                    return false;
            }
        }

        // מתודת עזר לזיהוי קבצי Globals XML בנתיב DataManagement\eCommon\Globals
        private static bool IsGlobalsXmlFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inGlobalsPath = lowerFullName.Contains("/datamanagement/ecommon/globals/") ||
                                 lowerFullName.Contains("\\datamanagement\\ecommon\\globals\\") ||
                                 lowerFullName.StartsWith("datamanagement/ecommon/globals/") ||
                                 lowerFullName.StartsWith("datamanagement\\ecommon\\globals\\");
            return inGlobalsPath && lowerFullName.EndsWith(".xml");
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] Parsing events CSV failed: {ex.Message}"); }
            return list;
        }

        public List<LogEntry> ParseLogStreamPartial(Stream stream)
        {
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
                System.Diagnostics.Debug.WriteLine($"[LogFileService] ParseLogStreamPartial failed: {ex.Message}");
            }

            return newLogs;
        }

        /// <summary>
        /// Parses a log stream, skipping the first <paramref name="skipCount"/> entries without
        /// creating LogEntry objects (fast iteration only). Returns only NEW entries after the skip.
        /// Also returns the total entry count for tracking.
        /// </summary>
        public (List<LogEntry> NewEntries, int TotalCount) ParseLogStreamSkipExisting(Stream stream, int skipCount)
        {
            var pool = new StringPool();
            var newEntries = new List<LogEntry>();
            int totalCount = 0;

            try
            {
                var logReader = new IndigoLogsReader(stream);

                while (logReader.MoveToNext())
                {
                    if (logReader.Current != null)
                    {
                        totalCount++;

                        // Fast skip: just advance the reader without creating LogEntry
                        if (totalCount <= skipCount)
                            continue;

                        // Only create LogEntry for NEW entries
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

                        newEntries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogFileService] ParseLogStreamSkipping failed: {ex.Message}");
            }

            return (newEntries, totalCount);
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
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"Error parsing log stream: {ex.GetType().Name}: {ex.Message}",
                        "Parse Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                });
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
                System.Diagnostics.Debug.WriteLine($"[LogFileService] Parsing app dev log failed: {ex.Message}");
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
            // Replace comma with period so TryParse handles variable fractional digits (3-7)
            string normalizedTs = timestampStr.Replace(',', '.');
            if (!DateTime.TryParse(normalizedTs,
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] LoadBitmapFromZip failed: {ex.Message}"); return null; }
        }

        private (string sw, string plc) ParseReadmeVersions(string content)
        {
            try
            {
                var sw = Regex.Match(content, @"Version[:=]\s*(.+)", RegexOptions.IgnoreCase);
                var plc = Regex.Match(content, @"PressPlcVersion[:=]\s*(.+)", RegexOptions.IgnoreCase);
                return (sw.Success ? sw.Groups[1].Value.Trim() : "Unknown", plc.Success ? plc.Groups[1].Value.Trim() : "Unknown");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] ParseReadmeVersions failed: {ex.Message}"); return ("Unknown", "Unknown"); }
        }

        private string ExtractPlcVersionFromSetupInfo(string jsonContent)
        {
            try
            {
                var match = Regex.Match(jsonContent, @"\""Name\""\s*:\s*\""press-content-mcs-plc\""[\s\S]*?\""Version\""\s*:\s*\""(?<ver>[^\""]+)\""", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups["ver"].Value.Trim();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] ExtractPlcVersionFromSetupInfo failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Detects numeric APP log files (e.g. "50300001.file", "50300001.file.log.8865")
        /// that use binary format but should go to the APP tab.
        /// Excludes engineGroup files which are PLC logs.
        /// </summary>
        private static bool IsNumericAppFile(string fileName)
        {
            string name = Path.GetFileName(fileName).ToLower();

            // Must not be an engineGroup file (those are PLC)
            if (name.Contains("enginegroup")) return false;

            // Match patterns: "50300001.file", "50300001.file.log.8865"
            int dotFileIdx = name.IndexOf(".file");
            if (dotFileIdx <= 0) return false;

            // Check that the part before ".file" ends with digits
            string prefix = name.Substring(0, dotFileIdx);
            return prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]);
        }

        private enum FileType { MainLog, AppDevLog, AppBinaryLog, EventsCsv, Plugin }

        private class ZipEntryData
        {
            public string Name;
            public FileType Type;
            public MemoryStream Stream;
            // Set when Type == Plugin:
            public ILogFilePlugin Plugin;
            public ParseContext Context;
        }

        private double CalculatePercent(long processed, long total) => total == 0 ? 0 : Math.Min(99, ((double)processed / total) * 100);

        // -----------------------------------------------------------------------
        // Plugin helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Maps a plugin-produced <see cref="LogEntryDto"/> to the application's
        /// internal <see cref="LogEntry"/> model.
        /// </summary>
        private static LogEntry MapDtoToLogEntry(LogEntryDto dto, StringPool pool)
        {
            return new LogEntry
            {
                Date        = dto.Date,
                Level       = pool.Intern(dto.Level ?? "Info"),
                Message     = dto.Message ?? "",
                ThreadName  = pool.Intern(dto.ThreadName ?? ""),
                Logger      = pool.Intern(dto.Logger ?? ""),
                ProcessName = string.IsNullOrEmpty(dto.ProcessName) ? null : pool.Intern(dto.ProcessName),
                Method      = string.IsNullOrEmpty(dto.Method)      ? null : pool.Intern(dto.Method),
                Data        = dto.Data,
                Exception   = dto.Exception,
                ExtraFields = dto.ExtraFields
            };
        }

        /// <summary>
        /// Public overload without StringPool — used by DifferentLogsViewModel.
        /// </summary>
        public static LogEntry MapDtoToLogEntry(LogEntryDto dto)
        {
            return new LogEntry
            {
                Date        = dto.Date,
                Level       = dto.Level ?? "Info",
                Message     = dto.Message ?? "",
                ThreadName  = dto.ThreadName ?? "",
                Logger      = dto.Logger ?? "",
                ProcessName = string.IsNullOrEmpty(dto.ProcessName) ? null : dto.ProcessName,
                Method      = string.IsNullOrEmpty(dto.Method)      ? null : dto.Method,
                Data        = dto.Data,
                Exception   = dto.Exception,
                ExtraFields = dto.ExtraFields
            };
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> text lines from a seekable stream
        /// and resets the stream position back to 0.
        /// </summary>
        private static string[] ReadSampleLines(Stream stream, int count)
        {
            var lines = new List<string>(count);
            try
            {
                long savedPos = stream.CanSeek ? stream.Position : -1;
                if (stream.CanSeek) stream.Position = 0;

                // leaveOpen = true so we don't close the MemoryStream
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
                {
                    string line;
                    while (lines.Count < count && (line = reader.ReadLine()) != null)
                        lines.Add(line);
                }

                if (stream.CanSeek) stream.Position = 0;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] ReadSampleLines failed: {ex.Message}"); }
            return lines.ToArray();
        }

        /// <summary>
        /// Opens a file on disk, reads up to <paramref name="count"/> lines, and closes it.
        /// </summary>
        private static string[] ReadSampleLinesFromFile(string filePath, int count)
        {
            var lines = new List<string>(count);
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while (lines.Count < count && (line = reader.ReadLine()) != null)
                        lines.Add(line);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LogFileService] ReadSampleLinesFromFile failed: {ex.Message}"); }
            return lines.ToArray();
        }

        /// <summary>
        /// Runs all registered plugins' <see cref="ILogFilePlugin.CanHandle"/> methods
        /// and returns the first one that returns <c>true</c>, or <c>null</c>.
        /// </summary>
        private ILogFilePlugin FindPlugin(string fileName, string[] sampleLines)
        {
            if (_pluginLoader == null || _pluginLoader.Plugins.Count == 0) return null;

            foreach (var plugin in _pluginLoader.Plugins)
            {
                try
                {
                    if (plugin.CanHandle(fileName, sampleLines))
                        return plugin;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogFileService] Plugin CanHandle check failed: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Invokes a plugin's <see cref="ILogFilePlugin.Parse"/> method and distributes
        /// the resulting entries between the PLC and APP lists based on ProcessName.
        /// </summary>
        private static void DispatchPluginResults(
            ILogFilePlugin plugin,
            Stream stream,
            ParseContext context,
            StringPool pool,
            List<LogEntry> plcTarget,
            List<LogEntry> appTarget)
        {
            IEnumerable<LogEntryDto> dtos;
            try
            {
                dtos = plugin.Parse(stream, context, progress: null, ct: CancellationToken.None);
            }
            catch (Exception)
            {
                return;
            }

            if (dtos == null) return;

            foreach (var dto in dtos)
            {
                var entry = MapDtoToLogEntry(dto, pool);
                if (string.Equals(dto.ProcessName, "APP", StringComparison.OrdinalIgnoreCase))
                    appTarget.Add(entry);
                else
                    plcTarget.Add(entry);
            }
        }
    }
}