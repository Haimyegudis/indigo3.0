using Indigo.Infra.ICL.Core.Logging;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>
        /// Fast scan of a ZIP file to detect which component types it contains.
        /// No extraction or parsing — just reads entry names.
        /// </summary>
        public TabSelectionConfig PreScanZip(string zipPath)
        {
            var config = new TabSelectionConfig();

            try
            {
                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    // Track first/last log entries for time range detection
                    string firstPlcEntry = null, lastPlcEntry = null;
                    string firstAppBinEntry = null, lastAppBinEntry = null;
                    string firstAppTextEntry = null, lastAppTextEntry = null;

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length == 0) continue;
                        string lowerName = entry.FullName.ToLower();

                        if (ZipClassificationHelpers.ShouldSkipEntry(lowerName)) continue;

                        // Globals
                        if (ZipClassificationHelpers.IsGlobalsXmlFile(lowerName))
                        { config.HasGlobals = true; continue; }

                        // Systab
                        if (ZipClassificationHelpers.IsSystabFile(lowerName))
                        { config.HasSystab = true; continue; }

                        // Configuration
                        if (ZipClassificationHelpers.IsConfigurationPath(lowerName))
                        { config.HasConfiguration = true; continue; }

                        // Terminal logs
                        if (ZipClassificationHelpers.IsTerminalLogsPath(lowerName))
                        { config.HasTerminalLogs = true; continue; }

                        // LRS
                        if (ZipClassificationHelpers.IsLrsPath(lowerName))
                        { config.HasLrs = true; continue; }

                        // PLC logs
                        if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            config.HasPlc = true;
                            if (firstPlcEntry == null) firstPlcEntry = entry.FullName;
                            lastPlcEntry = entry.FullName;
                            continue;
                        }

                        // APP text logs (S6)
                        if ((entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                        {
                            config.HasApp = true; config.IsS6 = true;
                            if (firstAppTextEntry == null) firstAppTextEntry = entry.FullName;
                            lastAppTextEntry = entry.FullName;
                            continue;
                        }

                        // APP binary logs (S4-5)
                        if (IsNumericAppFile(entry.Name))
                        {
                            config.HasApp = true;
                            if (firstAppBinEntry == null) firstAppBinEntry = entry.FullName;
                            lastAppBinEntry = entry.FullName;
                            continue;
                        }

                        // Events CSV
                        if ((entry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) &&
                             entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                            (Path.GetFileName(entry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) &&
                             entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                        { config.HasEvents = true; continue; }

                        // Screenshots
                        if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                        { config.HasScreenshots = true; continue; }

                        // Setup Info (Readme.txt, _setupInfo.json)
                        if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                        { config.HasSetupInfo = true; continue; }

                        // Nested ZIP — scan it too for components
                        if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var innerStream = CopyToMemory(entry))
                                using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false))
                                {
                                    foreach (var innerEntry in innerArchive.Entries)
                                    {
                                        if (innerEntry.Length == 0) continue;
                                        string innerLower = innerEntry.FullName.ToLower();
                                        if (ZipClassificationHelpers.ShouldSkipEntry(innerLower)) continue;

                                        if (ZipClassificationHelpers.IsGlobalsXmlFile(innerLower)) config.HasGlobals = true;
                                        else if (ZipClassificationHelpers.IsSystabFile(innerLower)) config.HasSystab = true;
                                        else if (ZipClassificationHelpers.IsConfigurationPath(innerLower)) config.HasConfiguration = true;
                                        else if (ZipClassificationHelpers.IsTerminalLogsPath(innerLower)) config.HasTerminalLogs = true;
                                        else if (ZipClassificationHelpers.IsLrsPath(innerLower)) config.HasLrs = true;
                                        else if (innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                 !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                            config.HasPlc = true;
                                        else if ((innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                                 (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                                        { config.HasApp = true; config.IsS6 = true; }
                                        else if (IsNumericAppFile(innerEntry.Name))
                                            config.HasApp = true;
                                        else if ((innerEntry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) &&
                                                  innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                                                 (Path.GetFileName(innerEntry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) &&
                                                  innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                                            config.HasEvents = true;
                                        else if (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                 innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                            config.HasScreenshots = true;
                                        else if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase) ||
                                                 innerEntry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                            config.HasSetupInfo = true;
                                    }
                                }
                            }
                            catch { /* nested ZIP scan failure is non-fatal */ }
                        }
                    }

                    // Scan time bounds from first/last log entries
                    ScanTimeBounds(archive, config, firstPlcEntry, lastPlcEntry,
                        firstAppBinEntry, lastAppBinEntry, firstAppTextEntry, lastAppTextEntry);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("PreScanZip failed", ex);
            }

            return config;
        }

        /// <summary>
        /// Reads timestamps from the first and last log entries to determine the available time range.
        /// Non-fatal — if anything fails, the time range properties stay null and the UI hides the section.
        /// </summary>
        private void ScanTimeBounds(ZipArchive archive, TabSelectionConfig config,
            string firstPlcEntry, string lastPlcEntry,
            string firstAppBinEntry, string lastAppBinEntry,
            string firstAppTextEntry, string lastAppTextEntry)
        {
            try
            {
                DateTime? earliest = null;
                DateTime? latest = null;

                // Helper to update min/max
                void Track(DateTime dt)
                {
                    if (dt == DateTime.MinValue) return;
                    if (!earliest.HasValue || dt < earliest.Value) earliest = dt;
                    if (!latest.HasValue || dt > latest.Value) latest = dt;
                }

                // --- Binary logs (PLC and APP S4-5): use IndigoLogsReader ---
                void ScanBinaryEntry(string entryName, bool first)
                {
                    if (entryName == null) return;
                    var zipEntry = archive.GetEntry(entryName);
                    if (zipEntry == null) return;
                    using (var ms = CopyToMemory(zipEntry))
                    {
                        var reader = new IndigoLogsReader(ms);
                        if (first)
                        {
                            // Just read the first entry
                            if (reader.MoveToNext() && reader.Current != null)
                                Track(reader.Current.Time);
                        }
                        else
                        {
                            // Iterate to find the last entry
                            DateTime lastTime = DateTime.MinValue;
                            while (reader.MoveToNext())
                            {
                                if (reader.Current != null)
                                    lastTime = reader.Current.Time;
                            }
                            Track(lastTime);
                        }
                    }
                }

                ScanBinaryEntry(firstPlcEntry, true);
                ScanBinaryEntry(lastPlcEntry, false);
                ScanBinaryEntry(firstAppBinEntry, true);
                ScanBinaryEntry(lastAppBinEntry, false);

                // --- Text APP logs (S6): read first/last lines with IsDateStart ---
                void ScanTextEntry(string entryName, bool first)
                {
                    if (entryName == null) return;
                    var zipEntry = archive.GetEntry(entryName);
                    if (zipEntry == null) return;
                    using (var ms = CopyToMemory(zipEntry))
                    using (var sr = new StreamReader(ms, Encoding.UTF8, true, 65536))
                    {
                        if (first)
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (IsDateStart(line))
                                {
                                    Track(ParseTimestampFast(line));
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Iterate all lines to find the last valid timestamp
                            DateTime lastTime = DateTime.MinValue;
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (IsDateStart(line))
                                    lastTime = ParseTimestampFast(line);
                            }
                            Track(lastTime);
                        }
                    }
                }

                ScanTextEntry(firstAppTextEntry, true);
                ScanTextEntry(lastAppTextEntry, false);

                if (earliest.HasValue && latest.HasValue && earliest.Value < latest.Value)
                {
                    config.AvailableStartTime = earliest.Value;
                    config.AvailableEndTime = latest.Value;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ScanTimeBounds failed (non-fatal)", ex);
            }
        }

        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress, TabSelectionConfig tabSelection = null)
        {
            return await Task.Run(() =>
            {
                var loadSw = System.Diagnostics.Stopwatch.StartNew();
                // Tab selection config — when null, load everything (backwards compatible)
                var sel = tabSelection ?? new TabSelectionConfig();
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
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                var filesToProcess = new List<ZipEntryData>();
                                var innerZipEntryNames = new List<string>(); // Deferred nested ZIP — don't extract until needed

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

                                    // 0-em. EM_Statistics CSV (Gantt chart)
                                    if (ZipClassificationHelpers.IsEmStatisticsFile(entry.FullName))
                                    {
                                        try
                                        {
                                            if (string.IsNullOrEmpty(session.EmStatisticsCsvContent))
                                            {
                                                using (var emStream = entry.Open())
                                                using (var emReader = new StreamReader(emStream))
                                                    session.EmStatisticsCsvContent = emReader.ReadToEnd();
                                            }
                                        }
                                        catch (Exception ex) { AppLogger.Error("Reading EM_Statistics CSV failed", ex); }
                                        continue;
                                    }

                                    // 0. בדיקה לקבצי Globals XML בתיקיית DataManagement\eCommon\Globals (לפני Configuration)
                                    if (IsGlobalsXmlFile(lowerName))
                                    {
                                        if (!sel.LoadGlobals) continue;
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);
                                            string content = ReadTextFromEntry(entry);
                                            if (!session.GlobalsFiles.ContainsKey(fileNameOnly))
                                                session.GlobalsFiles.Add(fileNameOnly, content);
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Reading globals XML file failed", ex);
                                        }
                                        continue;
                                    }

                                    // 0b. Systab files in DiagnosticsLogs folder (systab_saved.txt, systab_default.txt, etc.)
                                    if (IsSystabFile(lowerName))
                                    {
                                        if (!sel.LoadSystab) continue;
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
                                                // ReadTextFromEntry auto-detects UTF-16 LE BOM (.reg format)
                                                string content = ReadTextFromEntry(entry);
                                                session.SystabFiles.Add(systabKey, content);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Systab extraction failed", ex);
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
                                        if (!sel.LoadConfiguration) continue;
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);

                                            if (fileNameOnly.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                            {
                                                byte[] dbBytes = ReadBytesFromEntry(entry);
                                                if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                                    session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                                            }
                                            else
                                            {
                                                string content = ReadTextFromEntry(entry);
                                                if (!session.ConfigurationFiles.ContainsKey(fileNameOnly))
                                                    session.ConfigurationFiles.Add(fileNameOnly, content);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Reading configuration file failed", ex);
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

                                    // Skip terminal logs if user unchecked them
                                    if (isTerminalLog && !sel.LoadTerminalLogs) continue;

                                    // LRS folder — treat as terminal path, but exclude engineGroupA logs and nested ZIPs
                                    bool isLrsPath = lowerName.Contains("/lrs/") ||
                                                     lowerName.Contains("\\lrs\\") ||
                                                     lowerName.Contains("\\lrs/") ||
                                                     lowerName.Contains("/lrs\\") ||
                                                     lowerName.StartsWith("lrs/") ||
                                                     lowerName.StartsWith("lrs\\");

                                    // Skip LRS files if user unchecked them
                                    if (isLrsPath && !sel.LoadLrs) continue;

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
                                                    if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                                        session.TerminalCsvBytes.Add(fileNameOnly, ReadBytesFromEntry(entry));
                                                }
                                                else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                                         ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    // Text/log files: store as string for TERMINALS tab display
                                                    if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                                        session.TerminalLogFiles.Add(fileNameOnly, ReadTextFromEntry(entry));
                                                }
                                                // Skip .arl and other non-essential formats entirely
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Reading terminal log file failed", ex);
                                        }
                                        continue;
                                    }

                                    // 2. לוגים ראשיים (exclude .zip — those are nested archives, not log files)
                                    if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!sel.LoadPlc) continue;
                                        entryData.Type = FileType.MainLog;
                                        shouldProcess = true;
                                    }
                                    // 3. לוגים של אפליקציה
                                    else if ((entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                             (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                                    {
                                        if (!sel.LoadApp) continue;
                                        entryData.Type = FileType.AppDevLog;
                                        shouldProcess = true;
                                    }
                                    // 3b. APP binary logs
                                    else if (IsNumericAppFile(entry.Name))
                                    {
                                        if (!sel.LoadApp) continue;
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
                                        if (!sel.LoadEvents) continue;
                                        entryData.Type = FileType.EventsCsv;
                                        shouldProcess = true;
                                    }
                                    // 5. קבצי .db (skip if user unchecked Configuration)
                                    else if (entry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!sel.LoadConfiguration) continue;
                                        try
                                        {
                                            string fileNameOnly = Path.GetFileName(entry.Name);
                                            byte[] dbBytes = ReadBytesFromEntry(entry);
                                            if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                                session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Reading database file from ZIP failed", ex);
                                        }
                                        continue;
                                    }
                                    // 6. תמונות - split into Info vs non-Info for S4-5 filtering
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!sel.LoadScreenshots) continue;
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
                                        if (!sel.LoadSetupInfo) continue;
                                        session.PressConfiguration = ReadTextFromEntry(entry);
                                        var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                        if (sw != "Unknown") detectedSwVersion = sw;
                                        if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                                        continue;
                                    }
                                    else if (entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!sel.LoadSetupInfo) continue;
                                        session.SetupInfo = ReadTextFromEntry(entry);
                                        string plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                                        if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                                        continue;
                                    }
                                    // 8b. Nested ZIP — record name for deferred extraction (saves 400MB+ RAM)
                                    else if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        innerZipEntryNames.Add(entry.FullName);
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
                                                    catch (Exception ex) { AppLogger.Error("Plugin GetColumns failed", ex); }
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
                                        // Defer extraction — pipeline will CopyToMemory while parsing runs
                                        entryData.EntryFullName = entry.FullName;
                                        filesToProcess.Add(entryData);
                                    }
                                }

                                // --- Determine what the outer ZIP already provides ---
                                bool outerHasMainLog = filesToProcess.Any(f => f.Type == FileType.MainLog);
                                bool outerHasAppLogs = filesToProcess.Any(f => f.Type == FileType.AppDevLog || f.Type == FileType.AppBinaryLog);
                                if (outerHasMainLog)
                                    AppLogger.Info("[Load] Outer ZIP has PLC logs — will skip PLC logs from nested ZIPs to avoid date mixing");
                                if (outerHasAppLogs)
                                    AppLogger.Info("[Load] Outer ZIP has APP logs — will skip APP logs from nested ZIPs to avoid date mixing");

                                // --- Nested ZIP processing: extract one-at-a-time from the open archive ---
                                // Deferred extraction saves 400MB+ RAM — only one inner ZIP in memory at a time
                                foreach (var innerZipName in innerZipEntryNames)
                                {
                                    try
                                    {
                                        var outerEntry = archive.GetEntry(innerZipName);
                                        if (outerEntry == null) continue;
                                        using (var innerStream = CopyToMemory(outerEntry))
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

                                                // EM_Statistics CSV (Gantt chart)
                                                if (ZipClassificationHelpers.IsEmStatisticsFile(innerEntry.FullName))
                                                {
                                                    try
                                                    {
                                                        if (string.IsNullOrEmpty(session.EmStatisticsCsvContent))
                                                        {
                                                            using (var emStream = innerEntry.Open())
                                                            using (var emReader = new StreamReader(emStream))
                                                                session.EmStatisticsCsvContent = emReader.ReadToEnd();
                                                        }
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP EM_Statistics CSV failed", ex); }
                                                    continue;
                                                }

                                                // Globals XML
                                                if (IsGlobalsXmlFile(innerLower))
                                                {
                                                    if (!sel.LoadGlobals) continue;
                                                    try
                                                    {
                                                        string content = ReadTextFromEntry(innerEntry);
                                                        if (!session.GlobalsFiles.ContainsKey(prefixedName))
                                                            session.GlobalsFiles.Add(prefixedName, content);
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP globals file failed", ex); }
                                                    continue;
                                                }

                                                // Systab files
                                                if (IsSystabFile(innerLower))
                                                {
                                                    if (!sel.LoadSystab) continue;
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
                                                            // ReadTextFromEntry auto-detects UTF-16 LE BOM (.reg format)
                                                            string content = ReadTextFromEntry(innerEntry);
                                                            session.SystabFiles.Add(systabKey, content);
                                                        }
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP systab file failed", ex); }
                                                    continue;
                                                }

                                                // Configuration files
                                                bool isConfig = innerLower.Contains("/configuration/") || innerLower.Contains("\\configuration\\") ||
                                                                innerLower.StartsWith("configuration/") || innerLower.StartsWith("configuration\\");
                                                if (isConfig)
                                                {
                                                    if (!sel.LoadConfiguration) continue;
                                                    try
                                                    {
                                                        string fName = Path.GetFileName(innerEntry.Name);
                                                        if (fName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            if (!session.DatabaseFiles.ContainsKey(fName))
                                                                session.DatabaseFiles.Add(fName, ReadBytesFromEntry(innerEntry));
                                                        }
                                                        else
                                                        {
                                                            if (!session.ConfigurationFiles.ContainsKey(prefixedName))
                                                                session.ConfigurationFiles.Add(prefixedName, ReadTextFromEntry(innerEntry));
                                                        }
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP configuration file failed", ex); }
                                                    continue;
                                                }

                                                // Terminal logs (TerminalLogs + LRS folders)
                                                bool isTerminal = innerLower.Contains("/terminallogs/") || innerLower.Contains("\\terminallogs\\") ||
                                                                  innerLower.StartsWith("terminallogs/") || innerLower.StartsWith("terminallogs\\");

                                                // Skip terminal logs if user unchecked them
                                                if (isTerminal && !sel.LoadTerminalLogs) continue;

                                                // LRS folder — treat as terminal, exclude engineGroupA and nested ZIPs
                                                bool innerIsLrs = innerLower.Contains("/lrs/") || innerLower.Contains("\\lrs\\") ||
                                                                  innerLower.StartsWith("lrs/") || innerLower.StartsWith("lrs\\");

                                                // Skip LRS files if user unchecked them
                                                if (innerIsLrs && !sel.LoadLrs) continue;

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
                                                                    session.TerminalCsvBytes.Add(key, ReadBytesFromEntry(innerEntry));
                                                            }
                                                            else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                                                     ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                // Text/log: store as string for display
                                                                if (!session.TerminalLogFiles.ContainsKey(key))
                                                                    session.TerminalLogFiles.Add(key, ReadTextFromEntry(innerEntry));
                                                            }
                                                            // Skip .arl and other formats
                                                        }
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP terminal log failed", ex); }
                                                    continue;
                                                }

                                                // Main PLC logs — SKIP if outer ZIP already has PLC logs (avoid date mixing)
                                                var innerData = new ZipEntryData { Name = innerEntry.Name };
                                                bool innerShouldProcess = false;

                                                if (innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                    !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!sel.LoadPlc) continue;
                                                    if (outerHasMainLog)
                                                    {
                                                        AppLogger.Info($"[Load] Skipping inner ZIP PLC log: {innerEntry.Name} (outer ZIP already has PLC logs)");
                                                        continue;
                                                    }
                                                    innerData.Type = FileType.MainLog;
                                                    innerShouldProcess = true;
                                                }
                                                // APP dev logs — SKIP if outer ZIP already has APP logs
                                                else if ((innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                                         (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                                                {
                                                    if (!sel.LoadApp) continue;
                                                    if (outerHasAppLogs)
                                                    {
                                                        continue; // Skip — outer ZIP's APP logs take priority
                                                    }
                                                    innerData.Type = FileType.AppDevLog;
                                                    innerShouldProcess = true;
                                                }
                                                // APP binary logs — SKIP if outer ZIP already has APP logs
                                                else if (IsNumericAppFile(innerEntry.Name))
                                                {
                                                    if (!sel.LoadApp) continue;
                                                    if (outerHasAppLogs)
                                                    {
                                                        continue; // Skip — outer ZIP's APP logs take priority
                                                    }
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
                                                    if (!sel.LoadEvents) continue;
                                                    innerData.Type = FileType.EventsCsv;
                                                    innerShouldProcess = true;
                                                }
                                                // DB files (skip if user unchecked Configuration)
                                                else if (innerEntry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!sel.LoadConfiguration) continue;
                                                    try
                                                    {
                                                        string fName = Path.GetFileName(innerEntry.Name);
                                                        if (!session.DatabaseFiles.ContainsKey(fName))
                                                            session.DatabaseFiles.Add(fName, ReadBytesFromEntry(innerEntry));
                                                    }
                                                    catch (Exception ex) { AppLogger.Error("Reading inner ZIP database file failed", ex); }
                                                    continue;
                                                }
                                                // Screenshots
                                                else if (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                         innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!sel.LoadScreenshots) continue;
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
                                                    if (!sel.LoadSetupInfo) continue;
                                                    if (string.IsNullOrEmpty(session.PressConfiguration))
                                                    {
                                                        session.PressConfiguration = ReadTextFromEntry(innerEntry);
                                                        var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                                        if (sw != "Unknown") detectedSwVersion = sw;
                                                        if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
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

                                        AppLogger.Info($"Processed inner ZIP: {innerZipName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        AppLogger.Error($"Error processing nested ZIP {innerZipName}", ex);
                                    }
                                }
                                // --- End nested ZIP processing ---

                                // Pipeline: start consumer AFTER nested ZIP extraction completes.
                                // Sequential approach avoids CPU/GC contention between extraction and parsing.
                                var localLogLists = new ConcurrentBag<List<LogEntry>>();
                                var localTransLists = new ConcurrentBag<List<LogEntry>>();
                                var localFailLists = new ConcurrentBag<List<LogEntry>>();
                                var localAppLists = new ConcurrentBag<List<LogEntry>>();
                                var localEvtLists = new ConcurrentBag<List<EventEntry>>();
                                var csvLock = new object();

                                int totalFiles = filesToProcess.Count;
                                int processedCount = 0;

                                AppLogger.Info($"[Load] ZIP scan done: {totalFiles} files to parse, {loadSw.Elapsed.TotalSeconds:F1}s elapsed");

                                var pipeline = new BlockingCollection<ZipEntryData>();

                                // Consumer: parallel parsing of MemoryStreams (no archive access)
                                var parseTask = Task.Run(() =>
                                {
                                    Parallel.ForEach(pipeline.GetConsumingEnumerable(),
                                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                                        item =>
                                    {
                                        try
                                        {
                                            using (item.Stream)
                                            {
                                                var fileSw = System.Diagnostics.Stopwatch.StartNew();
                                                long streamLen = item.Stream.CanSeek ? item.Stream.Length : -1;

                                                // Time filter helper — returns filtered list or original if no filter active
                                                bool timeFilterActive = sel.UseTimeFilter && sel.FilterStartTime.HasValue && sel.FilterEndTime.HasValue;
                                                DateTime tfStart = timeFilterActive ? sel.FilterStartTime.Value : DateTime.MinValue;
                                                DateTime tfEnd = timeFilterActive ? sel.FilterEndTime.Value : DateTime.MaxValue;

                                                if (item.Type == FileType.MainLog)
                                                {
                                                    var result = ParseLogStream(item.Stream, stringPool);
                                                    var allLogs = result.AllLogs;
                                                    var transitions = result.Transitions;
                                                    var failures = result.Failures;
                                                    if (timeFilterActive)
                                                    {
                                                        allLogs = allLogs.Where(e => e.Date >= tfStart && e.Date <= tfEnd).ToList();
                                                        transitions = transitions.Where(e => e.Date >= tfStart && e.Date <= tfEnd).ToList();
                                                        failures = failures.Where(e => e.Date >= tfStart && e.Date <= tfEnd).ToList();
                                                    }
                                                    localLogLists.Add(allLogs);
                                                    if (transitions.Count > 0) localTransLists.Add(transitions);
                                                    if (failures.Count > 0) localFailLists.Add(failures);
                                                    AppLogger.Info($"[Load] PLC  {item.Name}: {allLogs.Count:N0} entries{(timeFilterActive ? " (filtered)" : "")}, {streamLen / 1048576.0:F1}MB, {fileSw.Elapsed.TotalSeconds:F1}s");
                                                }
                                                else if (item.Type == FileType.AppBinaryLog)
                                                {
                                                    var result = ParseLogStream(item.Stream, stringPool);
                                                    foreach (var log in result.AllLogs)
                                                        log.ProcessName = stringPool.Intern("APP");
                                                    var allLogs = result.AllLogs;
                                                    if (timeFilterActive)
                                                        allLogs = allLogs.Where(e => e.Date >= tfStart && e.Date <= tfEnd).ToList();
                                                    if (allLogs.Count > 0) localAppLists.Add(allLogs);
                                                    AppLogger.Info($"[Load] BIN  {item.Name}: {allLogs.Count:N0} entries{(timeFilterActive ? " (filtered)" : "")}, {streamLen / 1048576.0:F1}MB, {fileSw.Elapsed.TotalSeconds:F1}s");
                                                }
                                                else if (item.Type == FileType.AppDevLog)
                                                {
                                                    var logs = ParseAppDevLogStream(item.Stream, stringPool);
                                                    if (timeFilterActive)
                                                        logs = logs.Where(e => e.Date >= tfStart && e.Date <= tfEnd).ToList();
                                                    if (logs.Count > 0) localAppLists.Add(logs);
                                                    AppLogger.Info($"[Load] APP  {item.Name}: {logs.Count:N0} entries{(timeFilterActive ? " (filtered)" : "")}, {streamLen / 1048576.0:F1}MB, {fileSw.Elapsed.TotalSeconds:F1}s");
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
                                                    if (timeFilterActive)
                                                        evts = evts.Where(e => e.Time >= tfStart && e.Time <= tfEnd).ToList();
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
                                            AppLogger.Error("Parallel log file processing failed", ex);
                                        }
                                        finally
                                        {
                                            int count = System.Threading.Interlocked.Increment(ref processedCount);
                                            if (count % 3 == 0)
                                            {
                                                double ratio = totalFiles > 0 ? (double)count / totalFiles : 0;
                                                double fileProg = (0.5 + (ratio * 0.5)) * currentFileSize;
                                                double totalP = ((processedBytesGlobal + fileProg) / totalBytesAllFiles) * 100;
                                                progress?.Report((Math.Min(99, totalP), $"Parsing files: {count}/{totalFiles}"));
                                            }
                                        }
                                    });
                                });

                                // Producer: extract deferred outer items from archive (this thread has exclusive access).
                                // Items from nested ZIPs already have Stream set; outer deferred items need extraction.
                                foreach (var item in filesToProcess)
                                {
                                    if (item.Stream == null && !string.IsNullOrEmpty(item.EntryFullName))
                                    {
                                        try
                                        {
                                            var entry = archive.GetEntry(item.EntryFullName);
                                            if (entry != null)
                                                item.Stream = CopyToMemory(entry);
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error($"Deferred extraction failed: {item.EntryFullName}", ex);
                                            continue;
                                        }
                                    }
                                    if (item.Stream != null)
                                        pipeline.Add(item);
                                }
                                pipeline.CompleteAdding();
                                parseTask.Wait();

                                // Merge — then release intermediate bags to reduce GC pressure
                                AppLogger.Info($"[Load] Parallel parsing done: {loadSw.Elapsed.TotalSeconds:F1}s elapsed");
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

                                // Release intermediate bags — their lists are merged, no longer needed
                                // This frees references to the per-file List<LogEntry> objects for GC
                                localLogLists = null;
                                localTransLists = null;
                                localFailLists = null;
                                localAppLists = null;
                                localEvtLists = null;
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
                                catch (Exception ex) { AppLogger.Error("Reading loose terminal log file failed", ex); }
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
                                catch (Exception ex) { AppLogger.Error("Reading loose database file failed", ex); }
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
                                catch (Exception ex) { AppLogger.Error("Parsing readme.txt versions failed", ex); }
                            }
                            else if (lowerName.EndsWith("_setupinfo.json"))
                            {
                                try
                                {
                                    session.SetupInfo = File.ReadAllText(filePath);
                                    string plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                                    if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                                }
                                catch (Exception ex) { AppLogger.Error("Parsing setup info JSON failed", ex); }
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
                                catch (Exception ex) { AppLogger.Error("Loading screenshot image failed", ex); }
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
                                            catch (Exception ex) { AppLogger.Error("Plugin GetColumns for loose file failed", ex); }
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
                        var nzErrors = new ConcurrentBag<string>(); // Collect errors instead of blocking UI

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
                                // Collect errors non-blocking instead of Dispatcher.Invoke which stalls parallel threads
                                nzErrors.Add($"{item.Name}: {ex.GetType().Name}: {ex.Message}");
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

                        // Show collected errors after parallel loop completes (non-blocking during parsing)
                        if (!nzErrors.IsEmpty)
                        {
                            string allErrors = string.Join("\n", nzErrors);
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                System.Windows.MessageBox.Show(
                                    $"Errors processing {nzErrors.Count} file(s):\n\n{allErrors}",
                                    "File Processing Errors", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
                        }

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

                        AppLogger.Info($"No APP logs found. Manager thread: {hasManagerThread} → {session.ConfigurationType}");
                    }

                    if (!logsBag.IsEmpty) { foreach (var l in logsBag) mergedLogs.Add(l); }
                    if (!appDevLogsBag.IsEmpty) { foreach (var l in appDevLogsBag) mergedApps.Add(l); }
                    if (!transitionsBag.IsEmpty) { foreach (var l in transitionsBag) mergedTrans.Add(l); }
                    if (!failuresBag.IsEmpty) { foreach (var l in failuresBag) mergedFails.Add(l); }
                    if (!eventsBag.IsEmpty) { foreach (var l in eventsBag) mergedEvts.Add(l); }

                    // מיון סופי — in-place sort + all 5 sorts in parallel for maximum throughput
                    AppLogger.Info($"[Load] Pre-sort: PLC={mergedLogs.Count:N0}, APP={mergedApps.Count:N0}, Trans={mergedTrans.Count:N0}, Events={mergedEvts.Count:N0} — {loadSw.Elapsed.TotalSeconds:F1}s elapsed");
                    progress?.Report((88, $"Preparing sort ({mergedLogs.Count:N0} + {mergedApps.Count:N0} entries)..."));

                    // Clear garbage before sorting so Gen2 collections don't stall the sort.
                    // Use non-compacting collection (faster) — SustainedLowLatency below
                    // suppresses Gen2 during the sort itself.
                    GC.Collect(2, GCCollectionMode.Forced, true, false);
                    GC.WaitForPendingFinalizers();

                    var previousLatency = System.Runtime.GCSettings.LatencyMode;
                    try
                    {
                        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

                        progress?.Report((90, $"Sorting {mergedLogs.Count:N0} logs..."));

                        Comparison<LogEntry> dateComparer = (a, b) => a.Date.CompareTo(b.Date);
                        Comparison<EventEntry> eventComparer = (a, b) => a.Time.CompareTo(b.Time);

                        // Cache-friendly sort for large lists: extract Date.Ticks into contiguous array
                        // so comparisons access sequential memory (vs random object-pointer chasing).
                        // Small lists (transitions, failures, events) use regular sort.
                        // SortLogEntriesCacheFriendly sorts in-place via cycle permutation.
                        Parallel.Invoke(
                            () => SortLogEntriesCacheFriendly(mergedLogs),
                            () => SortLogEntriesCacheFriendly(mergedApps),
                            () => mergedTrans.Sort(dateComparer),
                            () => mergedFails.Sort(dateComparer),
                            () => mergedEvts.Sort(eventComparer)
                        );
                    }
                    finally
                    {
                        System.Runtime.GCSettings.LatencyMode = previousLatency;
                    }

                    AppLogger.Info($"[Load] Sort done — {loadSw.Elapsed.TotalSeconds:F1}s elapsed");
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

                    loadSw.Stop();
                    AppLogger.Info($"[Load] TOTAL: {loadSw.Elapsed.TotalSeconds:F1}s — PLC={mergedLogs.Count:N0}, APP={mergedApps.Count:N0}");
                    progress?.Report((100, "Done"));
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Fatal error during file loading", ex);
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        System.Windows.MessageBox.Show(
                            $"An error occurred during file loading:\n\n{ex.GetType().Name}: {ex.Message}\n\nPlease check the application log for details.",
                            "Loading Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error)));
                }

                return session;
            });
        }

        /// <summary>
        /// Reloads a single component from the session's ZIP file and merges results into the existing session.
        /// Only extracts/parses entries matching the requested component — everything else is skipped.
        /// </summary>
        public async Task ReloadComponentAsync(LogSessionData session, string componentName, IProgress<(double, string)> progress)
        {
            if (session == null || string.IsNullOrEmpty(session.FilePath))
                throw new InvalidOperationException("Session has no ZIP file path.");

            if (!session.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ReloadComponent only works with ZIP sessions.");

            await Task.Run(() =>
            {
                var stringPool = new StringPool();
                progress?.Report((5, $"Reloading {componentName}..."));

                // Build a TabSelectionConfig with ONLY the requested component enabled
                var sel = new TabSelectionConfig
                {
                    LoadApp = componentName == "App",
                    LoadPlc = componentName == "Plc",
                    LoadTerminalLogs = componentName == "TerminalLogs",
                    LoadConfiguration = componentName == "Configuration",
                    LoadSystab = componentName == "Systab",
                    LoadGlobals = componentName == "Globals",
                    LoadLrs = componentName == "Lrs",
                    LoadEvents = componentName == "Events",
                    LoadScreenshots = componentName == "Screenshots",
                    LoadSetupInfo = componentName == "SetupInfo",
                    LoadManagerThread = componentName == "ManagerThread"
                };

                var logsBag = new ConcurrentBag<List<LogEntry>>();
                var appLogsBag = new ConcurrentBag<List<LogEntry>>();
                var eventsBag = new ConcurrentBag<List<EventEntry>>();
                var screenshotsList = new List<BitmapImage>();

                try
                {
                    using (var fs = new FileStream(session.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304))
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var filesToProcess = new List<ZipEntryData>();
                        var innerZipEntryNames = new List<string>();
                        bool hasBinaryAppLogs = session.HasBinaryAppLogs;

                        progress?.Report((10, $"Scanning ZIP for {componentName}..."));

                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Length == 0) continue;
                            string lowerName = entry.FullName.ToLower();

                            if (lowerName.Contains("/backup/") || lowerName.Contains("\\backup\\") ||
                                lowerName.Contains("/old/") || lowerName.Contains("\\old\\") ||
                                lowerName.Contains("/temp/") || lowerName.Contains("\\temp\\") ||
                                lowerName.Contains("/archive/") || lowerName.Contains("\\archive\\"))
                                continue;

                            // Globals
                            if (sel.LoadGlobals && IsGlobalsXmlFile(lowerName))
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(entry.Name);
                                    string content = ReadTextFromEntry(entry);
                                    if (!session.GlobalsFiles.ContainsKey(fileNameOnly))
                                        session.GlobalsFiles[fileNameOnly] = content;
                                }
                                catch (Exception ex) { AppLogger.Error("Reload: globals failed", ex); }
                                continue;
                            }

                            // Systab
                            if (sel.LoadSystab && IsSystabFile(lowerName))
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
                                        session.SystabFiles[systabKey] = ReadTextFromEntry(entry);
                                }
                                catch (Exception ex) { AppLogger.Error("Reload: systab failed", ex); }
                                continue;
                            }

                            // Configuration
                            bool isConfigFile = lowerName.Contains("/configuration/") || lowerName.Contains("\\configuration\\") ||
                                                lowerName.StartsWith("configuration/") || lowerName.StartsWith("configuration\\");
                            if (sel.LoadConfiguration && isConfigFile)
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(entry.Name);
                                    if (fileNameOnly.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                            session.DatabaseFiles[fileNameOnly] = ReadBytesFromEntry(entry);
                                    }
                                    else
                                    {
                                        if (!session.ConfigurationFiles.ContainsKey(fileNameOnly))
                                            session.ConfigurationFiles[fileNameOnly] = ReadTextFromEntry(entry);
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reload: config failed", ex); }
                                continue;
                            }

                            // Terminal logs
                            bool isTerminalLog = ZipClassificationHelpers.IsTerminalLogsPath(lowerName);
                            if (sel.LoadTerminalLogs && isTerminalLog)
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(entry.Name);
                                    if (!string.IsNullOrEmpty(fileNameOnly))
                                    {
                                        string ext = Path.GetExtension(fileNameOnly);
                                        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                                session.TerminalCsvBytes[fileNameOnly] = ReadBytesFromEntry(entry);
                                        }
                                        else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                                session.TerminalLogFiles[fileNameOnly] = ReadTextFromEntry(entry);
                                        }
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reload: terminal log failed", ex); }
                                continue;
                            }

                            // LRS
                            bool isLrsPath = ZipClassificationHelpers.IsLrsPath(lowerName);
                            if (sel.LoadLrs && isLrsPath &&
                                entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) < 0 &&
                                !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(entry.Name);
                                    if (!string.IsNullOrEmpty(fileNameOnly))
                                    {
                                        string ext = Path.GetExtension(fileNameOnly);
                                        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                                session.TerminalCsvBytes[fileNameOnly] = ReadBytesFromEntry(entry);
                                        }
                                        else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                                session.TerminalLogFiles[fileNameOnly] = ReadTextFromEntry(entry);
                                        }
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reload: LRS failed", ex); }
                                continue;
                            }

                            // PLC logs
                            if (sel.LoadPlc && entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                filesToProcess.Add(new ZipEntryData { Name = entry.Name, EntryFullName = entry.FullName, Type = FileType.MainLog });
                                continue;
                            }

                            // APP text logs (S6)
                            if (sel.LoadApp && (entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                            {
                                filesToProcess.Add(new ZipEntryData { Name = entry.Name, EntryFullName = entry.FullName, Type = FileType.AppDevLog });
                                continue;
                            }

                            // APP binary logs (S4-5)
                            if (sel.LoadApp && IsNumericAppFile(entry.Name))
                            {
                                filesToProcess.Add(new ZipEntryData { Name = entry.Name, EntryFullName = entry.FullName, Type = FileType.AppBinaryLog });
                                continue;
                            }

                            // Events CSV
                            if (sel.LoadEvents &&
                                ((entry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                                 (Path.GetFileName(entry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))))
                            {
                                filesToProcess.Add(new ZipEntryData { Name = entry.Name, EntryFullName = entry.FullName, Type = FileType.EventsCsv });
                                continue;
                            }

                            // Screenshots
                            if (sel.LoadScreenshots && (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                            {
                                var bmp = LoadBitmapFromZip(entry);
                                if (bmp != null)
                                {
                                    bool isInfoPath = lowerName.Contains("/info/") || lowerName.Contains("\\info\\");
                                    // For S4-5, only Info screenshots; for S6, all screenshots
                                    if (!hasBinaryAppLogs || isInfoPath)
                                        screenshotsList.Add(bmp);
                                }
                                continue;
                            }

                            // Setup Info
                            if (sel.LoadSetupInfo)
                            {
                                if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    session.PressConfiguration = ReadTextFromEntry(entry);
                                    continue;
                                }
                                if (entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                {
                                    session.SetupInfo = ReadTextFromEntry(entry);
                                    continue;
                                }
                            }

                            // Nested ZIP — check for components inside
                            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                innerZipEntryNames.Add(entry.FullName);
                            }
                        }

                        // Process nested ZIPs
                        foreach (var innerZipName in innerZipEntryNames)
                        {
                            try
                            {
                                var outerEntry = archive.GetEntry(innerZipName);
                                if (outerEntry == null) continue;
                                using (var innerStream = CopyToMemory(outerEntry))
                                using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false))
                                {
                                    foreach (var innerEntry in innerArchive.Entries)
                                    {
                                        if (innerEntry.Length == 0) continue;
                                        string innerLower = innerEntry.FullName.ToLower();
                                        if (innerLower.Contains("/backup/") || innerLower.Contains("\\backup\\") ||
                                            innerLower.Contains("/old/") || innerLower.Contains("\\old\\"))
                                            continue;

                                        string prefixedName = $"{Path.GetFileNameWithoutExtension(innerZipName)}/{innerEntry.Name}";

                                        // Globals
                                        if (sel.LoadGlobals && IsGlobalsXmlFile(innerLower))
                                        {
                                            try
                                            {
                                                string content = ReadTextFromEntry(innerEntry);
                                                if (!session.GlobalsFiles.ContainsKey(prefixedName))
                                                    session.GlobalsFiles[prefixedName] = content;
                                            }
                                            catch { }
                                            continue;
                                        }

                                        // Systab
                                        if (sel.LoadSystab && IsSystabFile(innerLower))
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
                                                    session.SystabFiles[systabKey] = ReadTextFromEntry(innerEntry);
                                            }
                                            catch { }
                                            continue;
                                        }

                                        // Configuration
                                        bool innerIsConfig = innerLower.Contains("/configuration/") || innerLower.Contains("\\configuration\\") ||
                                                             innerLower.StartsWith("configuration/") || innerLower.StartsWith("configuration\\");
                                        if (sel.LoadConfiguration && innerIsConfig)
                                        {
                                            try
                                            {
                                                string fName = Path.GetFileName(innerEntry.Name);
                                                if (fName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!session.DatabaseFiles.ContainsKey(fName))
                                                        session.DatabaseFiles[fName] = ReadBytesFromEntry(innerEntry);
                                                }
                                                else
                                                {
                                                    if (!session.ConfigurationFiles.ContainsKey(prefixedName))
                                                        session.ConfigurationFiles[prefixedName] = ReadTextFromEntry(innerEntry);
                                                }
                                            }
                                            catch { }
                                            continue;
                                        }

                                        // Terminal logs
                                        bool innerIsTerminal = innerLower.Contains("/terminallogs/") || innerLower.Contains("\\terminallogs\\") ||
                                                               innerLower.StartsWith("terminallogs/") || innerLower.StartsWith("terminallogs\\");
                                        if (sel.LoadTerminalLogs && innerIsTerminal)
                                        {
                                            try
                                            {
                                                string fName = Path.GetFileName(innerEntry.Name);
                                                string ext = Path.GetExtension(fName);
                                                string key = prefixedName;
                                                if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!session.TerminalCsvBytes.ContainsKey(key))
                                                        session.TerminalCsvBytes[key] = ReadBytesFromEntry(innerEntry);
                                                }
                                                else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (!session.TerminalLogFiles.ContainsKey(key))
                                                        session.TerminalLogFiles[key] = ReadTextFromEntry(innerEntry);
                                                }
                                            }
                                            catch { }
                                            continue;
                                        }

                                        // PLC logs from inner ZIP
                                        if (sel.LoadPlc && innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                            !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var ms = CopyToMemory(innerEntry);
                                            filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.MainLog });
                                            continue;
                                        }

                                        // APP text logs from inner ZIP
                                        if (sel.LoadApp && (innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                            (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                                        {
                                            var ms = CopyToMemory(innerEntry);
                                            filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.AppDevLog });
                                            continue;
                                        }

                                        // APP binary logs from inner ZIP
                                        if (sel.LoadApp && IsNumericAppFile(innerEntry.Name))
                                        {
                                            var ms = CopyToMemory(innerEntry);
                                            filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.AppBinaryLog });
                                            continue;
                                        }

                                        // Events CSV from inner ZIP
                                        if (sel.LoadEvents &&
                                            ((innerEntry.Name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) && innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
                                             (Path.GetFileName(innerEntry.Name).StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase) && innerEntry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))))
                                        {
                                            var ms = CopyToMemory(innerEntry);
                                            filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.EventsCsv });
                                            continue;
                                        }

                                        // Screenshots from inner ZIP
                                        if (sel.LoadScreenshots && (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            var bmp = LoadBitmapFromZip(innerEntry);
                                            if (bmp != null)
                                            {
                                                bool isInfoPath = innerLower.Contains("/info/") || innerLower.Contains("\\info\\");
                                                if (!hasBinaryAppLogs || isInfoPath)
                                                    screenshotsList.Add(bmp);
                                            }
                                            continue;
                                        }

                                        // Setup Info from inner ZIP
                                        if (sel.LoadSetupInfo && innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (string.IsNullOrEmpty(session.PressConfiguration))
                                                session.PressConfiguration = ReadTextFromEntry(innerEntry);
                                            continue;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) { AppLogger.Error($"Reload: nested ZIP {innerZipName} failed", ex); }
                        }

                        progress?.Report((40, $"Parsing {componentName}..."));

                        // Parse collected files
                        var csvLock = new object();
                        int totalFiles = filesToProcess.Count;
                        int processedCount = 0;

                        var pipeline = new BlockingCollection<ZipEntryData>();

                        var parseTask = Task.Run(() =>
                        {
                            Parallel.ForEach(pipeline.GetConsumingEnumerable(),
                                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                                item =>
                            {
                                try
                                {
                                    using (item.Stream)
                                    {
                                        if (item.Type == FileType.MainLog)
                                        {
                                            var result = ParseLogStream(item.Stream, stringPool);
                                            logsBag.Add(result.AllLogs);
                                        }
                                        else if (item.Type == FileType.AppBinaryLog)
                                        {
                                            var result = ParseLogStream(item.Stream, stringPool);
                                            foreach (var log in result.AllLogs)
                                                log.ProcessName = stringPool.Intern("APP");
                                            if (result.AllLogs.Count > 0) appLogsBag.Add(result.AllLogs);
                                        }
                                        else if (item.Type == FileType.AppDevLog)
                                        {
                                            var logs = ParseAppDevLogStream(item.Stream, stringPool);
                                            if (logs.Count > 0) appLogsBag.Add(logs);
                                        }
                                        else if (item.Type == FileType.EventsCsv)
                                        {
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
                                            if (evts.Count > 0) eventsBag.Add(evts);
                                        }
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error($"Reload: parsing {item.Name} failed", ex); }
                                finally
                                {
                                    int count = Interlocked.Increment(ref processedCount);
                                    double pct = 40 + (50.0 * count / Math.Max(1, totalFiles));
                                    progress?.Report((pct, $"Parsing: {count}/{totalFiles}"));
                                }
                            });
                        });

                        // Producer: extract deferred items
                        foreach (var item in filesToProcess)
                        {
                            if (item.Stream == null && !string.IsNullOrEmpty(item.EntryFullName))
                            {
                                try
                                {
                                    var entry = archive.GetEntry(item.EntryFullName);
                                    if (entry != null)
                                        item.Stream = CopyToMemory(entry);
                                }
                                catch { continue; }
                            }
                            if (item.Stream != null)
                                pipeline.Add(item);
                        }
                        pipeline.CompleteAdding();
                        parseTask.Wait();
                    }

                    progress?.Report((90, "Merging results..."));

                    // Merge parsed data into existing session
                    if (sel.LoadPlc || sel.LoadManagerThread)
                    {
                        var newLogs = new List<LogEntry>();
                        foreach (var l in logsBag) newLogs.AddRange(l);
                        if (newLogs.Count > 0)
                        {
                            SortLogEntriesCacheFriendly(newLogs);
                            var merged = new List<LogEntry>(session.Logs.Count + newLogs.Count);
                            merged.AddRange(session.Logs);
                            merged.AddRange(newLogs);
                            SortLogEntriesCacheFriendly(merged);
                            session.Logs = merged;
                        }
                    }

                    if (sel.LoadApp)
                    {
                        var newApps = new List<LogEntry>();
                        foreach (var l in appLogsBag) newApps.AddRange(l);
                        if (newApps.Count > 0)
                        {
                            SortLogEntriesCacheFriendly(newApps);
                            var merged = new List<LogEntry>(session.AppDevLogs.Count + newApps.Count);
                            merged.AddRange(session.AppDevLogs);
                            merged.AddRange(newApps);
                            SortLogEntriesCacheFriendly(merged);
                            session.AppDevLogs = merged;
                        }
                    }

                    if (sel.LoadEvents)
                    {
                        var newEvents = new List<EventEntry>();
                        foreach (var l in eventsBag) newEvents.AddRange(l);
                        if (newEvents.Count > 0)
                        {
                            newEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
                            var merged = new List<EventEntry>(session.Events.Count + newEvents.Count);
                            merged.AddRange(session.Events);
                            merged.AddRange(newEvents);
                            merged.Sort((a, b) => a.Time.CompareTo(b.Time));
                            session.Events = merged;
                        }
                    }

                    if (sel.LoadScreenshots && screenshotsList.Count > 0)
                    {
                        session.Screenshots.AddRange(screenshotsList);
                    }

                    // Update LoadTabSelection to mark this component as now loaded
                    if (session.LoadTabSelection != null)
                    {
                        switch (componentName)
                        {
                            case "App": session.LoadTabSelection.LoadApp = true; break;
                            case "Plc": session.LoadTabSelection.LoadPlc = true; break;
                            case "Events": session.LoadTabSelection.LoadEvents = true; break;
                            case "Screenshots": session.LoadTabSelection.LoadScreenshots = true; break;
                            case "TerminalLogs": session.LoadTabSelection.LoadTerminalLogs = true; break;
                            case "Configuration": session.LoadTabSelection.LoadConfiguration = true; break;
                            case "Systab": session.LoadTabSelection.LoadSystab = true; break;
                            case "Globals": session.LoadTabSelection.LoadGlobals = true; break;
                            case "Lrs": session.LoadTabSelection.LoadLrs = true; break;
                            case "SetupInfo": session.LoadTabSelection.LoadSetupInfo = true; break;
                            case "ManagerThread": session.LoadTabSelection.LoadManagerThread = true; break;
                        }
                    }

                    progress?.Report((100, $"{componentName} loaded successfully."));
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ReloadComponentAsync({componentName}) failed", ex);
                    throw;
                }
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
            catch (Exception ex) { AppLogger.Error("Parsing events CSV failed", ex); }
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
                AppLogger.Error("ParseLogStreamPartial failed", ex);
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
                AppLogger.Error("ParseLogStreamSkipping failed", ex);
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
                // Use non-blocking BeginInvoke to avoid stalling parallel worker threads
                AppLogger.Error("Error parsing log stream", ex);
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    System.Windows.MessageBox.Show(
                        $"Error parsing log stream: {ex.GetType().Name}: {ex.Message}",
                        "Parse Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
            }
            return (allLogs, transitions, failures);
        }

        private List<LogEntry> ParseAppDevLogStream(Stream stream, StringPool pool = null)
        {
            pool = pool ?? new StringPool();
            // Pre-allocate based on ~1KB per entry
            int estimatedEntries = stream.CanSeek ? (int)Math.Min(stream.Length / 1024, 500000) : 10000;
            var list = new List<LogEntry>(estimatedEntries);
            try
            {
                if (stream.CanSeek && stream.Position != 0) stream.Position = 0;
                // 64KB reader buffer — much better throughput for large files (default is 1KB)
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 65536))
                {
                    string line;
                    var buffer = new StringBuilder(4096);

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 7 && line == "!!![V2]") continue;

                        if (IsDateStart(line))
                        {
                            // Flush previous buffered entry (new-format multi-line)
                            if (buffer.Length > 0)
                            {
                                var logEntry = ProcessAppDevBufferFast(buffer.ToString(), pool);
                                if (logEntry != null) list.Add(logEntry);
                                buffer.Clear();
                            }

                            // Fast-path: old-format single-line entry (has \x1e separators)
                            // Skips StringBuilder + Split overhead (~6 fewer allocations per entry)
                            if (line.IndexOf('\x1e') > 0)
                            {
                                var entry = ParseOldFormatDirect(line, pool);
                                if (entry != null)
                                {
                                    list.Add(entry);
                                    continue; // parsed — don't buffer
                                }
                                // Incomplete (multi-line message) — fall through to buffer
                            }
                        }
                        buffer.AppendLine(line);
                    }

                    if (buffer.Length > 0)
                    {
                        var logEntry = ProcessAppDevBufferFast(buffer.ToString(), pool);
                        if (logEntry != null) list.Add(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Parsing app dev log failed", ex);
            }
            return list;
        }

        /// <summary>
        /// Parses old-format AppDev log entry directly from a line using IndexOf chain.
        /// Avoids Split (saves array + 12 substrings) and StringBuilder buffer.
        /// Returns null if the line doesn't have enough \x1e separators (multi-line entry).
        /// </summary>
        private LogEntry ParseOldFormatDirect(string line, StringPool pool)
        {
            // Fields: [0]Timestamp [1]Thread [2]RootIFlowId [3]IFlowId [4]IFlowName
            //         [5]Pattern [6]Context [7]LevelLogger [8]Location [9]Message [10]Exception [11]Data
            int e0 = line.IndexOf('\x1e');
            if (e0 < 20) return null;

            int e1 = line.IndexOf('\x1e', e0 + 1);  if (e1 < 0) return null;
            int e2 = line.IndexOf('\x1e', e1 + 1);  if (e2 < 0) return null;
            int e3 = line.IndexOf('\x1e', e2 + 1);  if (e3 < 0) return null;
            int e4 = line.IndexOf('\x1e', e3 + 1);  if (e4 < 0) return null;
            int e5 = line.IndexOf('\x1e', e4 + 1);  if (e5 < 0) return null;
            int e6 = line.IndexOf('\x1e', e5 + 1);  if (e6 < 0) return null;
            int e7 = line.IndexOf('\x1e', e6 + 1);  if (e7 < 0) return null;
            int e8 = line.IndexOf('\x1e', e7 + 1);  if (e8 < 0) return null;
            int e9 = line.IndexOf('\x1e', e8 + 1);  if (e9 < 0) return null;

            // Timestamp — ParseTimestampFast reads from position 0, stops at first non-digit after comma
            DateTime date = ParseTimestampFast(line);
            if (date == DateTime.MinValue) return null;

            // Level + Logger from field [7]: "LEVEL Logger"
            string level = "INFO";
            string logger = "";
            int s7 = e6 + 1;
            int lvlSpace = line.IndexOf(' ', s7);
            if (lvlSpace > 0 && lvlSpace < e7)
            {
                level = line.Substring(s7, lvlSpace - s7).ToUpper();
                logger = line.Substring(lvlSpace + 1, e7 - lvlSpace - 1).Trim();
            }

            // Message [9]
            string message = line.Substring(e8 + 1, e9 - e8 - 1).Trim();

            // Exception [10] — optional
            string exception = null;
            int e10 = line.IndexOf('\x1e', e9 + 1);
            if (e10 > e9 + 1)
            {
                string exc = line.Substring(e9 + 1, e10 - e9 - 1).Trim();
                if (exc.Length > 0) exception = exc;
            }

            // Data [11] — optional
            string data = null;
            if (e10 > 0)
            {
                int e11 = line.IndexOf('\x1e', e10 + 1);
                int dataEnd = e11 > 0 ? e11 : line.Length;
                if (dataEnd > e10 + 1)
                {
                    string d = line.Substring(e10 + 1, dataEnd - e10 - 1).Trim();
                    if (d.Length > 0) data = d;
                }
            }

            // Pattern [5]
            string pattern = null;
            if (e5 > e4 + 1)
            {
                string p = line.Substring(e4 + 1, e5 - e4 - 1).Trim();
                if (p.Length > 0) pattern = p;
            }

            return new LogEntry
            {
                Date = date,
                ThreadName = pool.Intern(line.Substring(e0 + 1, e1 - e0 - 1)),
                Level = pool.Intern(level),
                Logger = pool.Intern(logger),
                Message = message,
                ProcessName = pool.Intern("APP"),
                Method = pool.Intern(line.Substring(e7 + 1, e8 - e7 - 1).Trim()),
                Pattern = pattern,
                Data = data,
                Exception = exception
            };
        }

        /// <summary>Fast inline check: "YYYY-MM-DD HH:MM:SS,ddd" — replaces _dateStartPattern regex.</summary>
        private static bool IsDateStart(string line)
        {
            if (line.Length < 23) return false;
            return line[4] == '-' && line[7] == '-' && line[10] == ' '
                && line[13] == ':' && line[16] == ':' && line[19] == ','
                && (uint)(line[0] - '0') <= 9 && (uint)(line[5] - '0') <= 9
                && (uint)(line[8] - '0') <= 9 && (uint)(line[11] - '0') <= 9;
        }

        /// <summary>Fast manual timestamp parse: "YYYY-MM-DD HH:MM:SS,ddddddd" — avoids DateTime.TryParse overhead.</summary>
        private static DateTime ParseTimestampFast(string ts)
        {
            // ts is at least 23 chars (checked by IsDateStart)
            int year = (ts[0] - '0') * 1000 + (ts[1] - '0') * 100 + (ts[2] - '0') * 10 + (ts[3] - '0');
            int month = (ts[5] - '0') * 10 + (ts[6] - '0');
            int day = (ts[8] - '0') * 10 + (ts[9] - '0');
            int hour = (ts[11] - '0') * 10 + (ts[12] - '0');
            int minute = (ts[14] - '0') * 10 + (ts[15] - '0');
            int second = (ts[17] - '0') * 10 + (ts[18] - '0');

            // Fractional: parse 3-7 digits after comma at position 20
            long ticks = 0;
            int digits = 0;
            for (int i = 20; i < ts.Length && (uint)(ts[i] - '0') <= 9; i++)
            {
                ticks = ticks * 10 + (ts[i] - '0');
                digits++;
            }
            // Normalize to 100ns ticks: 3 digits=ms→*10000, 7 digits=ticks directly
            switch (digits)
            {
                case 3: ticks *= 10000; break;
                case 4: ticks *= 1000; break;
                case 5: ticks *= 100; break;
                case 6: ticks *= 10; break;
                case 7: break;
                default: ticks = 0; break;
            }

            try
            {
                return new DateTime(year, month, day, hour, minute, second).AddTicks(ticks);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Fast manual parser replacing regex for AppDev log entries.
        /// Old format: fields separated by \x1e (record separator).
        /// New format: pipe-separated multi-line — falls back to regex only if needed.
        /// </summary>
        private LogEntry ProcessAppDevBufferFast(string rawText, StringPool pool)
        {
            // ── Old format: \x1e separated ──
            // Fields: Timestamp\x1eThread\x1eRootIFlowId\x1eIFlowId\x1eIFlowName\x1ePattern\x1eContext\x1e"Level Logger"\x1eLocation\x1eMessage\x1eException\x1eData
            int firstSep = rawText.IndexOf('\x1e');
            if (firstSep > 0)
            {
                var parts = rawText.Split('\x1e');
                if (parts.Length < 10) return null;

                DateTime date = ParseTimestampFast(parts[0].Trim());
                if (date == DateTime.MinValue) return null;

                // [7] = "Level Logger" — split on first space
                string levelLogger = parts[7];
                string level = "INFO";
                string logger = "";
                int spaceIdx = levelLogger.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    level = levelLogger.Substring(0, spaceIdx).ToUpper();
                    logger = levelLogger.Substring(spaceIdx + 1).Trim();
                }

                string message = parts[9].Trim();
                string exception = parts.Length > 10 ? parts[10].Trim() : "";
                string data = parts.Length > 11 ? parts[11].Trim() : "";
                string pattern = parts[5].Trim();
                string location = parts[8].Trim();

                return new LogEntry
                {
                    Date = date,
                    ThreadName = pool.Intern(parts[1]),
                    Level = pool.Intern(level),
                    Logger = pool.Intern(logger),
                    Message = message,
                    ProcessName = pool.Intern("APP"),
                    Method = pool.Intern(location),
                    Pattern = string.IsNullOrEmpty(pattern) ? null : pattern,
                    Data = string.IsNullOrEmpty(data) ? null : data,
                    Exception = string.IsNullOrEmpty(exception) ? null : exception
                };
            }

            // ── New format: pipe-separated multi-line ──
            // First line: Timestamp |Thread| |Root| |Flow| |Name| |Pattern| |Context| LEVEL Logger
            // Second line: |Method|
            // Remaining lines until ||: Message
            int firstNl = rawText.IndexOf('\n');
            if (firstNl < 0) return null;

            string firstLine = firstNl > 0 && rawText[firstNl - 1] == '\r'
                ? rawText.Substring(0, firstNl - 1) : rawText.Substring(0, firstNl);

            int firstPipe = firstLine.IndexOf('|');
            if (firstPipe < 0) return null;

            DateTime pipeDate = ParseTimestampFast(firstLine.Substring(0, firstPipe).Trim());
            if (pipeDate == DateTime.MinValue) return null;

            // Extract |Field| groups
            var pipeFields = new List<string>(8);
            int pos = firstPipe;
            while (pos < firstLine.Length && firstLine[pos] == '|')
            {
                int endPipe = firstLine.IndexOf('|', pos + 1);
                if (endPipe < 0) break;
                pipeFields.Add(firstLine.Substring(pos + 1, endPipe - pos - 1));
                pos = endPipe + 1;
                while (pos < firstLine.Length && firstLine[pos] == ' ') pos++;
            }

            if (pipeFields.Count < 6) return null;

            string pThread = pipeFields[0];
            string pPattern = pipeFields.Count > 4 ? pipeFields[4] : "";

            // Remainder is "LEVEL  Logger"
            string pLevelLogger = pos < firstLine.Length ? firstLine.Substring(pos).Trim() : "";
            string pLevel = "INFO";
            string pLogger = "";
            int pSpIdx = pLevelLogger.IndexOf(' ');
            if (pSpIdx > 0)
            {
                pLevel = pLevelLogger.Substring(0, pSpIdx).Trim().ToUpper();
                pLogger = pLevelLogger.Substring(pSpIdx).Trim();
            }

            // Second line: |Method|
            string pLocation = "";
            int secondStart = firstNl + 1;
            if (secondStart < rawText.Length)
            {
                int secondNl = rawText.IndexOf('\n', secondStart);
                if (secondNl > secondStart)
                {
                    string secondLine = rawText[secondNl - 1] == '\r'
                        ? rawText.Substring(secondStart, secondNl - secondStart - 1)
                        : rawText.Substring(secondStart, secondNl - secondStart);
                    if (secondLine.Length > 2 && secondLine[0] == '|' && secondLine[secondLine.Length - 1] == '|')
                        pLocation = secondLine.Substring(1, secondLine.Length - 2);

                    // Message: everything after second line until trailing ||
                    int msgStart = secondNl + 1;
                    if (msgStart < rawText.Length)
                    {
                        int terminator = rawText.LastIndexOf("||");
                        string pMessage = terminator > msgStart
                            ? rawText.Substring(msgStart, terminator - msgStart).Trim()
                            : rawText.Substring(msgStart).Trim();

                        return new LogEntry
                        {
                            Date = pipeDate,
                            ThreadName = pool.Intern(pThread),
                            Level = pool.Intern(pLevel),
                            Logger = pool.Intern(pLogger),
                            Message = pMessage,
                            ProcessName = pool.Intern("APP"),
                            Method = pool.Intern(pLocation),
                            Pattern = string.IsNullOrEmpty(pPattern) ? null : pPattern
                        };
                    }
                }
            }

            return null;
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

        // Maximum decompressed size per ZIP entry (512 MB) — prevents ZIP bomb attacks
        private const long MaxEntryDecompressedBytes = 512L * 1024 * 1024;

        private MemoryStream CopyToMemory(ZipArchiveEntry entry)
        {
            // Reject entries whose declared size exceeds the safety cap
            if (entry.Length > MaxEntryDecompressedBytes)
                throw new InvalidDataException($"ZIP entry '{entry.Name}' exceeds maximum allowed size ({entry.Length:N0} bytes > {MaxEntryDecompressedBytes:N0} limit).");

            // Pre-allocate with known size to avoid resizing
            var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            using (var stream = entry.Open())
            {
                // Rent from ArrayPool to avoid repeated 1MB LOH allocations that cause Gen2 GC pauses
                byte[] buffer = ArrayPool<byte>.Shared.Rent(1048576);
                try
                {
                    long totalRead = 0;
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, 1048576)) > 0)
                    {
                        totalRead += bytesRead;
                        if (totalRead > MaxEntryDecompressedBytes)
                            throw new InvalidDataException($"ZIP entry '{entry.Name}' decompressed data exceeds {MaxEntryDecompressedBytes:N0} byte limit (possible ZIP bomb).");
                        ms.Write(buffer, 0, bytesRead);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            ms.Position = 0;
            return ms;
        }

        /// <summary>Reads entry content directly to string — skips CopyToMemory intermediate buffer.</summary>
        private static string ReadTextFromEntry(ZipArchiveEntry entry)
        {
            using (var s = entry.Open())
            // detectEncodingFromByteOrderMarks handles UTF-16 LE BOM (systab .reg files) automatically
            using (var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536))
                return r.ReadToEnd();
        }

        /// <summary>Reads entry content directly to byte[] — avoids MemoryStream + ToArray double copy.</summary>
        private static byte[] ReadBytesFromEntry(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxEntryDecompressedBytes)
                throw new InvalidDataException($"ZIP entry '{entry.Name}' exceeds maximum allowed size.");

            int declaredLen = (int)Math.Min(entry.Length, int.MaxValue);
            var result = new byte[declaredLen];
            using (var s = entry.Open())
            {
                int offset = 0;
                while (offset < declaredLen)
                {
                    int read = s.Read(result, offset, declaredLen - offset);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset < declaredLen)
                    Array.Resize(ref result, offset);
            }
            return result;
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
            catch (Exception ex) { AppLogger.Error("LoadBitmapFromZip failed", ex); return null; }
        }

        private (string sw, string plc) ParseReadmeVersions(string content)
        {
            try
            {
                var sw = Regex.Match(content, @"Version[:=]\s*(.+)", RegexOptions.IgnoreCase);
                var plc = Regex.Match(content, @"PressPlcVersion[:=]\s*(.+)", RegexOptions.IgnoreCase);
                return (sw.Success ? sw.Groups[1].Value.Trim() : "Unknown", plc.Success ? plc.Groups[1].Value.Trim() : "Unknown");
            }
            catch (Exception ex) { AppLogger.Error("ParseReadmeVersions failed", ex); return ("Unknown", "Unknown"); }
        }

        private string ExtractPlcVersionFromSetupInfo(string jsonContent)
        {
            try
            {
                var match = Regex.Match(jsonContent, @"\""Name\""\s*:\s*\""press-content-mcs-plc\""[\s\S]*?\""Version\""\s*:\s*\""(?<ver>[^\""]+)\""", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups["ver"].Value.Trim();
            }
            catch (Exception ex) { AppLogger.Error("ExtractPlcVersionFromSetupInfo failed", ex); }
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
            public string EntryFullName; // For deferred CopyToMemory (pipeline extraction)
            public FileType Type;
            public MemoryStream Stream;
            // Set when Type == Plugin:
            public ILogFilePlugin Plugin;
            public ParseContext Context;
        }

        /// <summary>
        /// Cache-friendly sort: extracts Date.Ticks into a contiguous long[] array so
        /// comparisons access sequential memory instead of chasing object pointers.
        /// 4-8x faster than List.Sort for millions of entries due to CPU cache locality.
        /// </summary>
        private static List<LogEntry> SortLogEntriesCacheFriendly(List<LogEntry> list)
        {
            int count = list.Count;
            if (count <= 1) return list;

            var ticks = new long[count];
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                ticks[i] = list[i].Date.Ticks;
                indices[i] = i;
            }

            Array.Sort(ticks, indices);
            // indices[i] = original position of item that belongs at sorted position i.
            // To apply this forward permutation in-place we need the inverse:
            // inverse[originalPos] = sortedPos
            var inverse = new int[count];
            for (int i = 0; i < count; i++)
                inverse[indices[i]] = i;

            // In-place cycle-chase using the inverse permutation
            for (int i = 0; i < count; i++)
            {
                while (inverse[i] != i)
                {
                    int j = inverse[i];
                    var temp = list[i];
                    list[i] = list[j];
                    list[j] = temp;
                    inverse[i] = inverse[j];
                    inverse[j] = j;
                }
            }

            return list;
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
            catch (Exception ex) { AppLogger.Error("ReadSampleLines failed", ex); }
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
            catch (Exception ex) { AppLogger.Error("ReadSampleLinesFromFile failed", ex); }
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
                    AppLogger.Error("Plugin CanHandle check failed", ex);
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