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
    }
}
