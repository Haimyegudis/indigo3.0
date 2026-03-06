using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress, TabSelectionConfig? tabSelection = null)
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

                                ClassifyZipEntries(archive, session, sel, filesToProcess, innerZipEntryNames,
                                    screenshotsBag, nonInfoScreenshotsBag,
                                    ref hasBinaryAppLogs, ref detectedSwVersion, ref detectedPlcVersion);

                                // --- Determine what the outer ZIP already provides ---
                                bool outerHasMainLog = filesToProcess.Any(f => f.Type == FileType.MainLog);
                                bool outerHasAppLogs = filesToProcess.Any(f => f.Type == FileType.AppDevLog || f.Type == FileType.AppBinaryLog);
                                if (outerHasMainLog)
                                    AppLogger.Info("[Load] Outer ZIP has PLC logs — will skip PLC logs from nested ZIPs to avoid date mixing");
                                if (outerHasAppLogs)
                                    AppLogger.Info("[Load] Outer ZIP has APP logs — will skip APP logs from nested ZIPs to avoid date mixing");

                                // --- Nested ZIP processing: extract one-at-a-time from the open archive ---
                                // Deferred extraction saves 400MB+ RAM — only one inner ZIP in memory at a time
                                ProcessNestedZipEntries(archive, innerZipEntryNames, filesToProcess, session, sel,
                                    outerHasMainLog, outerHasAppLogs, ref hasBinaryAppLogs,
                                    ref detectedSwVersion, ref detectedPlcVersion,
                                    screenshotsBag, nonInfoScreenshotsBag);
                                // --- End nested ZIP processing ---

                                // Pipeline: parse extracted streams in parallel, merge results
                                await RunZipParsingPipeline(archive, filesToProcess, sel, stringPool, session,
                                    mergedLogs, mergedTrans, mergedFails, mergedApps, mergedEvts,
                                    currentFileSize, processedBytesGlobal, totalBytesAllFiles, loadSw, progress!);
                            }
                        }
                        else
                        {
                            // Handle non-ZIP files
                            ClassifyLooseFile(filePath, session, nonZipFiles, ref hasBinaryAppLogs,
                                ref detectedSwVersion, ref detectedPlcVersion, screenshotsBag);
                        }
                        processedBytesGlobal += currentFileSize;
                    }

                    // Parallel processing for regular files
                    if (nonZipFiles.Count > 0)
                    {
                        ProcessLooseFilesParallel(nonZipFiles, stringPool, session,
                            mergedLogs, mergedTrans, mergedFails, mergedApps, mergedEvts, progress!);
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
