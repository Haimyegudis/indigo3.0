#nullable disable
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Interfaces;
using System;
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
    public partial class LogFileService : ILogFileService
    {
        // -----------------------------------------------------------------------
        // Plugin loader — injected via DI; null-safe (no plugins = graceful skip)
        // -----------------------------------------------------------------------
        private readonly IPluginLoader _pluginLoader;
        private readonly Interfaces.IDialogService _dialogService;

        public LogFileService(IPluginLoader pluginLoader, Interfaces.IDialogService dialogService = null)
        {
            _pluginLoader = pluginLoader;
            _dialogService = dialogService;
        }

        /// <summary>Exposes the plugin loader for external callers (e.g. dialog filter building).</summary>
        public IPluginLoader GetPluginLoader() => _pluginLoader;

        // --- Optimization: StringPool class for string interning (Thread-Safe) ---
        public class StringPool
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cache
                = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

            public string Intern(string value)
            {
                // If the value is empty or null, nothing to store in Cache
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

        // Regex for parsing application logs - old format with \x1e as separator
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

        // Regex for parsing application logs - new format with | as separator
        // Format: 2026-01-29 10:32:38,073 |Thread| |RootIFlowId| |IFlowId| |IFlowName| |Pattern| |Context| LEVEL  Logger
        // Next line: |Method|
        // Next lines: --> or <-- or message text, followed by optional data/JSON, ending with ||
        private readonly Regex _appDevRegexPipe = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\s*\|(?<Thread>[^|]*)\|\s*\|(?<RootIFlowId>[^|]*)\|\s*\|(?<IFlowId>[^|]*)\|\s*\|(?<IFlowName>[^|]*)\|\s*\|(?<Pattern>[^|]*)\|\s*\|(?<Context>[^|]*)\|\s*(?<Level>\w+)\s+(?<Logger>[^\r\n]*)[\r\n]+\|(?<Location>[^|]*)\|[\r\n]+(?<Message>.*?)\s*\|\|",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress, TabSelectionConfig tabSelection = null)
        {
            return await Task.Run(async () =>
            {
                var loadSw = System.Diagnostics.Stopwatch.StartNew();
                // Tab selection config — when null, load everything (backwards compatible)
                var sel = tabSelection ?? new TabSelectionConfig();
                // Create a single Pool for the entire session
                var stringPool = new StringPool();

                var session = new LogSessionData();
                // Initialize all dictionaries
                session.ConfigurationFiles = new Dictionary<string, string>();
                session.DatabaseFiles = new Dictionary<string, byte[]>();
                session.TerminalLogFiles = new Dictionary<string, string>(); // Initialize dictionary for terminals (.txt/.log as strings)
                session.TerminalCsvBytes = new Dictionary<string, byte[]>(); // CSV as byte[] for deferred decoding
                session.GlobalsFiles = new Dictionary<string, string>(); // Initialize dictionary for globals
                session.SystabFiles = new Dictionary<string, string>(); // Initialize dictionary for systab

                if (filePaths == null || filePaths.Length == 0) return session;

                // Expand paths to individual files
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

                // Lists for final merging
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

                                    // Aggressive filtering
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

                                    // 0. Check for Globals XML files in DataManagement\eCommon\Globals folder (before Configuration)
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

                                    // 1. Identify Configuration files
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

                                    // 1b. Terminal log files (files in TerminalLogs and LRS folders)
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

                                    // 2. Main logs (exclude .zip — those are nested archives, not log files)
                                    if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!sel.LoadPlc) continue;
                                        entryData.Type = FileType.MainLog;
                                        shouldProcess = true;
                                    }
                                    // 3. Application logs
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
                                    // 5. .db files (skip if user unchecked Configuration)
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
                                    // 6. Screenshots - split into Info vs non-Info for S4-5 filtering
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
                                    // 7. Info files
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
                                await parseTask;

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
                            // Handle non-ZIP files
                            string lowerName = Path.GetFileName(filePath).ToLower();
                            string lowerPath = filePath.ToLower();

                            // === Check for special TERMINAL files in a regular folder ===
                            if (IsCustomTerminalLog(filePath)) // Pass the path, the function will decide
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

                    // Parallel processing for regular files
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
                                _dialogService?.ShowWarning(
                                    $"Errors processing {nzErrors.Count} file(s):\n\n{allErrors}",
                                    "File Processing Errors")));
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

                    // Final sort — in-place sort + all 5 sorts in parallel for maximum throughput
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
                        _dialogService?.ShowError(
                            $"An error occurred during file loading:\n\n{ex.GetType().Name}: {ex.Message}\n\nPlease check the application log for details.",
                            "Loading Error")));
                }

                return session;
            });
        }
    }
}
