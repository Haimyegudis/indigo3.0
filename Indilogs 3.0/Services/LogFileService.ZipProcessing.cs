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
                                    string? systabKey = null;
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

                            // Events CSV or XML
                            if (sel.LoadEvents && IsEventsFile(entry.Name, out var outerEvtType))
                            {
                                filesToProcess.Add(new ZipEntryData { Name = entry.Name, EntryFullName = entry.FullName, Type = outerEvtType });
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

                            // Readme.txt — always loaded (press config + versions)
                            if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                            {
                                session.PressConfiguration = ReadTextFromEntry(entry);
                                continue;
                            }
                            // Setup Info (_setupInfo.json only)
                            if (sel.LoadSetupInfo && entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                            {
                                session.SetupInfo = ReadTextFromEntry(entry);
                                continue;
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
                                                string? systabKey = null;
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

                                        // Events CSV or XML from inner ZIP
                                        if (sel.LoadEvents && IsEventsFile(innerEntry.Name, out var innerEvtType))
                                        {
                                            var ms = CopyToMemory(innerEntry);
                                            filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = innerEvtType });
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

                                        // Readme from inner ZIP — always loaded
                                        if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
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
                                        else if (item.Type == FileType.EventsXml)
                                        {
                                            item.Stream.Position = 0;
                                            string csvFromXml = ConvertEventsXmlToCsv(item.Stream);
                                            if (!string.IsNullOrEmpty(csvFromXml))
                                            {
                                                lock (csvLock)
                                                {
                                                    if (string.IsNullOrEmpty(session.EventsCsvRawContent))
                                                        session.EventsCsvRawContent = csvFromXml;
                                                }
                                                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csvFromXml));
                                                var evts = ParseEventsCsv(ms);
                                                if (evts.Count > 0) eventsBag.Add(evts);
                                            }
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
                        await parseTask.ConfigureAwait(false);
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
            }).ConfigureAwait(false);
        }
    }
}
