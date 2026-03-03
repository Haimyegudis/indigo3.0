#nullable disable
using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
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
                            catch (Exception ex) { AppLogger.Warn($"Nested ZIP scan failed for '{entry.Name}': {ex.Message}"); }
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

            await Task.Run(async () =>
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
                                            catch (Exception ex) { AppLogger.Warn($"Failed to read globals file '{innerEntry.Name}': {ex.Message}"); }
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
                                            catch (Exception ex) { AppLogger.Warn($"Failed to read systab file '{innerEntry.Name}': {ex.Message}"); }
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
                                            catch (Exception ex) { AppLogger.Warn($"Failed to read config file '{innerEntry.Name}': {ex.Message}"); }
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
                                            catch (Exception ex) { AppLogger.Warn($"Failed to read terminal log '{innerEntry.Name}': {ex.Message}"); }
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
                                catch (Exception ex) { AppLogger.Warn($"Failed to extract deferred entry '{item.EntryFullName}': {ex.Message}"); continue; }
                            }
                            if (item.Stream != null)
                                pipeline.Add(item);
                        }
                        pipeline.CompleteAdding();
                        await parseTask;
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

        // Helper method to identify special terminal files (files located outside the TerminalLogs folder but belonging to terminals)
        private bool IsCustomTerminalLog(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = Path.GetFileName(fileName);

            // Terminal files by specific name
            return name.StartsWith("whel3", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("ecm", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("COM1", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("0001", StringComparison.OrdinalIgnoreCase) ||
                   // IO sensor CSV files (Io-BIM, Io-ECM, Io-PDC, Io-WHEL)
                   name.StartsWith("Io-", StringComparison.OrdinalIgnoreCase) ||
                   // Stability CSV files
                   name.StartsWith("Stab-", StringComparison.OrdinalIgnoreCase) ||
                   // PRE/POST analysis files
                   name.StartsWith("PRE_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("POST_", StringComparison.OrdinalIgnoreCase);
        }

        // Helper method to identify Systab files in the DiagnosticsLogs path
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

        // Helper method to identify Globals XML files in the DataManagement\eCommon\Globals path
        private static bool IsGlobalsXmlFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inGlobalsPath = lowerFullName.Contains("/datamanagement/ecommon/globals/") ||
                                 lowerFullName.Contains("\\datamanagement\\ecommon\\globals\\") ||
                                 lowerFullName.StartsWith("datamanagement/ecommon/globals/") ||
                                 lowerFullName.StartsWith("datamanagement\\ecommon\\globals\\");
            return inGlobalsPath && lowerFullName.EndsWith(".xml");
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
    }
}
