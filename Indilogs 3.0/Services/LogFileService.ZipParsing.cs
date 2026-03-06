using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
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
