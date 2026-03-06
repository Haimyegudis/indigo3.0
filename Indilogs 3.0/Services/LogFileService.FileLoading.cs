using Indigo.Infra.ICL.Core.Logging;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        private void ClassifyLooseFile(
            string filePath,
            LogSessionData session,
            List<ZipEntryData> nonZipFiles,
            ref bool hasBinaryAppLogs,
            ref string detectedSwVersion,
            ref string detectedPlcVersion,
            ConcurrentBag<BitmapImage> screenshotsBag)
        {
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
            else if (IsEventsFile(lowerName, out var nzEvtType))
            {
                nonZipFiles.Add(new ZipEntryData { Name = filePath, Type = nzEvtType });
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
                    string? plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
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
                    ILogFilePlugin? plugin = FindPlugin(fileName, sample);
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

        private void ProcessLooseFilesParallel(
            List<ZipEntryData> nonZipFiles,
            StringPool stringPool,
            LogSessionData session,
            List<LogEntry> mergedLogs,
            List<LogEntry> mergedTrans,
            List<LogEntry> mergedFails,
            List<LogEntry> mergedApps,
            List<EventEntry> mergedEvts,
            IProgress<(double, string)> progress)
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
                        else if (item.Type == FileType.EventsXml)
                        {
                            fs.Position = 0;
                            string csvFromXml = ConvertEventsXmlToCsv(fs);
                            if (!string.IsNullOrEmpty(csvFromXml))
                            {
                                if (string.IsNullOrEmpty(session.EventsCsvRawContent))
                                    session.EventsCsvRawContent = csvFromXml;
                                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csvFromXml));
                                var evts = ParseEventsCsv(ms);
                                if (evts.Count > 0) nzLocalEvts.Add(evts);
                            }
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

        private async Task RunZipParsingPipeline(
            ZipArchive archive,
            List<ZipEntryData> filesToProcess,
            TabSelectionConfig sel,
            StringPool stringPool,
            LogSessionData session,
            List<LogEntry> mergedLogs,
            List<LogEntry> mergedTrans,
            List<LogEntry> mergedFails,
            List<LogEntry> mergedApps,
            List<EventEntry> mergedEvts,
            long currentFileSize,
            long processedBytesGlobal,
            long totalBytesAllFiles,
            System.Diagnostics.Stopwatch loadSw,
            IProgress<(double, string)> progress)
        {
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
                            DateTime tfStart = timeFilterActive ? sel.FilterStartTime!.Value : DateTime.MinValue;
                            DateTime tfEnd = timeFilterActive ? sel.FilterEndTime!.Value : DateTime.MaxValue;

                            if (item.Type == FileType.MainLog)
                            {
                                var result = ParseLogStream(item.Stream, stringPool);
                                var allLogs = result.AllLogs;
                                var transitions = result.Transitions;
                                var failures = result.Failures;
                                if (timeFilterActive)
                                {
                                    allLogs.RemoveAll(e => e.Date < tfStart || e.Date > tfEnd);
                                    transitions.RemoveAll(e => e.Date < tfStart || e.Date > tfEnd);
                                    failures.RemoveAll(e => e.Date < tfStart || e.Date > tfEnd);
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
                                    allLogs.RemoveAll(e => e.Date < tfStart || e.Date > tfEnd);
                                if (allLogs.Count > 0) localAppLists.Add(allLogs);
                                AppLogger.Info($"[Load] BIN  {item.Name}: {allLogs.Count:N0} entries{(timeFilterActive ? " (filtered)" : "")}, {streamLen / 1048576.0:F1}MB, {fileSw.Elapsed.TotalSeconds:F1}s");
                            }
                            else if (item.Type == FileType.AppDevLog)
                            {
                                var logs = ParseAppDevLogStream(item.Stream, stringPool);
                                if (timeFilterActive)
                                    logs.RemoveAll(e => e.Date < tfStart || e.Date > tfEnd);
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
                                    evts.RemoveAll(e => e.Time < tfStart || e.Time > tfEnd);
                                if (evts.Count > 0) localEvtLists.Add(evts);
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
                                    if (timeFilterActive)
                                        evts.RemoveAll(e => e.Time < tfStart || e.Time > tfEnd);
                                    if (evts.Count > 0) localEvtLists.Add(evts);
                                }
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
            await parseTask.ConfigureAwait(false);

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
        }
    }
}
