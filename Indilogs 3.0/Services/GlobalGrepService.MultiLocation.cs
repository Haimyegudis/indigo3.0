using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;
using Indigo.Infra.ICL.Core.Logging;

namespace IndiLogs_3._0.Services
{
    public partial class GlobalGrepService
    {
        // ====================================================================
        //  Multi-location + criteria-based search
        // ====================================================================

        /// <summary>
        /// Searches multiple locations in parallel using structured <see cref="SearchCriteria"/>.
        /// When <paramref name="onResult"/> is provided, each match is streamed immediately to the caller
        /// (for real-time UI updates). When null, results are collected and returned as a list (batch mode).
        /// </summary>
        public async Task<List<GrepResult>> SearchMultiLocationAsync(
            SearchCriteria criteria,
            IReadOnlyList<SearchLocation> locations,
            IProgress<(int current, int total, string status)>? progress,
            CancellationToken cancellationToken,
            Action<GrepResult>? onResult = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var activeLocations = locations
                .Where(l => l.IsActive && (criteria.LocationIds == null || criteria.LocationIds.Count == 0 || criteria.LocationIds.Contains(l.Id)))
                .ToList();

            int totalLocations = activeLocations.Count;
            int completedLocations = 0;
            int totalMatches = 0;

            // If no streaming callback, collect results into a thread-safe bag
            var collectBag = onResult == null ? new ConcurrentBag<GrepResult>() : null;
            Action<GrepResult> effectiveCallback = onResult ?? (r => collectBag!.Add(r));

            AppLogger.Info($"[Grep] SearchMultiLocation: {totalLocations} active location(s), PLC={criteria.SearchPLC}, APP={criteria.SearchAPP}");

            var tasks = activeLocations.Select(location => Task.Run(() =>
            {
                var locSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    progress?.Report((completedLocations, totalLocations, $"Searching: {location.Name}..."));
                    AppLogger.Info($"[Grep] Searching location \"{location.Name}\" at \"{location.BasePath}\"...");

                    // Wrap callback to tag results with location info
                    Action<GrepResult> locationCallback = r =>
                    {
                        r.LocationName = location.Name;
                        r.LocationAddress = location.Address ?? location.BasePath;
                        Interlocked.Increment(ref totalMatches);
                        effectiveCallback(r);
                    };

                    int matchCount = SearchLocationFiles(location, criteria, cancellationToken, locationCallback);
                    AppLogger.Info($"[Grep] Location \"{location.Name}\" done — {matchCount} result(s) in {locSw.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException)
                {
                    AppLogger.Info($"[Grep] Location \"{location.Name}\" search cancelled");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Grep] Failed to search location '{location.Name}': {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref completedLocations);
                    progress?.Report((completedLocations, totalLocations, $"Completed: {location.Name}"));
                }
            }));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var resultList = collectBag?.OrderBy(r => r.Timestamp).ToList() ?? new List<GrepResult>();
            int count = onResult != null ? totalMatches : resultList.Count;

            if (cancellationToken.IsCancellationRequested)
                AppLogger.Info($"[Grep] SearchMultiLocation cancelled — {count:N0} partial results — {sw.ElapsedMilliseconds}ms");
            else
                AppLogger.Info($"[Grep] SearchMultiLocation complete: {count:N0} results from {activeLocations.Count} locations — {sw.ElapsedMilliseconds}ms");
            return resultList;
        }

        /// <summary>
        /// Searches all matching files in a single location using the given criteria.
        /// Files are processed in parallel for better performance.
        /// Each match is streamed via the <paramref name="onResult"/> callback.
        /// Returns total match count.
        /// </summary>
        private int SearchLocationFiles(SearchLocation location, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            if (string.IsNullOrWhiteSpace(location.BasePath))
            {
                AppLogger.Warn($"[Grep] Location \"{location.Name}\" has empty BasePath — skipping");
                return 0;
            }

            // Gather files
            List<string> files;
            try
            {
                files = Directory.GetFiles(location.BasePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsLogFile(f, criteria.SearchPLC, criteria.SearchAPP))
                    .ToList();
                AppLogger.Info($"[Grep] Location \"{location.Name}\": found {files.Count} file(s) to search");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Grep] Cannot enumerate files at '{location.BasePath}': {ex.Message}");
                return 0;
            }

            // Filter files by time range
            if (criteria.FileTimeFilter != null)
            {
                int beforeFilter = files.Count;
                files = FilterFilesByTimeRange(files, criteria.FileTimeFilter);
                AppLogger.Info($"[Grep] Time filter applied: {beforeFilter} → {files.Count} file(s)");

                // Diagnostic: if all files were filtered out, log the date range of what was available
                if (files.Count == 0 && beforeFilter > 0)
                {
                    try
                    {
                        var dates = new List<DateTime>();
                        foreach (var f in Directory.EnumerateFiles(location.BasePath, "*.*", SearchOption.AllDirectories).Take(20))
                        {
                            try { dates.Add(File.GetLastWriteTime(f)); } catch (Exception ex) { AppLogger.Warn($"[Grep] File date access failed: {ex.Message}"); }
                        }
                        if (dates.Count > 0)
                        {
                            dates.Sort();
                            AppLogger.Warn($"[Grep] All {beforeFilter} files filtered out! Filter: From={criteria.FileTimeFilter.From:yyyy-MM-dd HH:mm}. " +
                                $"Sample file dates: oldest={dates.First():yyyy-MM-dd HH:mm}, newest={dates.Last():yyyy-MM-dd HH:mm}");
                        }
                    }
                    catch (Exception ex) { AppLogger.Warn($"Diagnostic file date sampling failed: {ex.Message}"); }
                }
            }

            if (files.Count == 0)
            {
                AppLogger.Info($"[Grep] Location \"{location.Name}\": no files to search after filtering");
                return 0;
            }

            // Search files in parallel (up to 4 concurrent) for better throughput
            int totalMatches = 0;
            int filesSearched = 0;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = CancellationToken.None // We check ct manually to avoid exceptions
            };

            Parallel.ForEach(files, parallelOptions, (file, loopState) =>
            {
                if (ct.IsCancellationRequested) { loopState.Break(); return; }
                Interlocked.Increment(ref filesSearched);
                try
                {
                    int fileMatches;
                    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        fileMatches = SearchZipWithCriteria(file, criteria, ct, onResult);
                    }
                    else
                    {
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fileMatches = SearchStreamWithCriteria(fs, file, Path.GetFileName(file),
                                DetermineLogType(file), criteria, ct, onResult);
                        }
                    }
                    if (fileMatches > 0)
                    {
                        Interlocked.Add(ref totalMatches, fileMatches);
                        AppLogger.Info($"[Grep] File \"{Path.GetFileName(file)}\": {fileMatches} match(es)");
                    }
                }
                catch (OperationCanceledException)
                {
                    AppLogger.Info($"[Grep] Search cancelled while reading '{Path.GetFileName(file)}'");
                    loopState.Break();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Grep] Error reading '{file}': {ex.Message}");
                }
            });

            AppLogger.Info($"[Grep] Location \"{location.Name}\": searched {filesSearched}/{files.Count} file(s), {totalMatches} total match(es)");
            return totalMatches;
        }

        private int SearchZipWithCriteria(string zipPath, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            int totalMatches = 0;
            AppLogger.Info($"[Grep] Opening ZIP: \"{Path.GetFileName(zipPath)}\"");
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entries = archive.Entries.ToList();
                    AppLogger.Info($"[Grep] ZIP \"{Path.GetFileName(zipPath)}\": {entries.Count} entries");

                    foreach (var entry in entries)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                        string entryLower = entry.FullName.ToLowerInvariant();

                        // Nested ZIP — extract to memory and recurse
                        if (entryLower.EndsWith(".zip"))
                        {
                            AppLogger.Info($"[Grep] Nested ZIP found: \"{entry.Name}\" — extracting...");
                            try
                            {
                                using (var entryStream = entry.Open())
                                using (var ms = new MemoryStream())
                                {
                                    entryStream.CopyTo(ms);
                                    ms.Position = 0;
                                    using (var innerArchive = new ZipArchive(ms, ZipArchiveMode.Read))
                                    {
                                        var innerEntries = innerArchive.Entries.ToList();
                                        int matchable = innerEntries.Count(e => !string.IsNullOrWhiteSpace(e.Name) && IsLogEntry(e.FullName, criteria.SearchPLC, criteria.SearchAPP));
                                        AppLogger.Info($"[Grep] Nested ZIP \"{entry.Name}\": {innerEntries.Count} entries, {matchable} searchable log(s)");

                                        foreach (var innerEntry in innerEntries)
                                        {
                                            if (ct.IsCancellationRequested) break;
                                            if (string.IsNullOrWhiteSpace(innerEntry.Name)) continue;
                                            if (!IsLogEntry(innerEntry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                                            using (var s = innerEntry.Open())
                                            {
                                                totalMatches += SearchStreamWithCriteria(s,
                                                    zipPath, $"{entry.Name}/{innerEntry.Name}",
                                                    DetermineLogType(innerEntry.FullName), criteria, ct, onResult);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            {
                                AppLogger.Warn($"[Grep] Error reading nested ZIP '{entry.Name}' in '{zipPath}': {ex.Message}");
                            }
                            continue;
                        }

                        // Regular log file entry
                        if (!IsLogEntry(entry.FullName, criteria.SearchPLC, criteria.SearchAPP)) continue;

                        using (var s = entry.Open())
                        {
                            totalMatches += SearchStreamWithCriteria(s, zipPath, entry.Name,
                                DetermineLogType(entry.FullName), criteria, ct, onResult);
                        }
                    }
                }
                AppLogger.Info($"[Grep] ZIP \"{Path.GetFileName(zipPath)}\" done — {totalMatches} result(s)");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Grep] Error opening ZIP '{zipPath}': {ex.Message}");
            }
            return totalMatches;
        }

        /// <summary>
        /// Parses a log stream and evaluates criteria. Each match is streamed via <paramref name="onResult"/>.
        /// Returns total match count.
        /// </summary>
        private int SearchStreamWithCriteria(Stream stream, string filePath, string fileName,
            string logType, SearchCriteria criteria, CancellationToken ct, Action<GrepResult> onResult)
        {
            int matchCount = 0;
            int lineNumber = 0;

            try
            {
                Stream seekableStream = stream;
                MemoryStream? memoryStream = null;
                if (!stream.CanSeek)
                {
                    memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    seekableStream = memoryStream;
                }

                try
                {
                    var logReader = new IndigoLogsReader(seekableStream);
                    while (logReader.MoveToNext())
                    {
                        if (ct.IsCancellationRequested) break;
                        lineNumber++;

                        var currentLog = logReader.Current;
                        if (currentLog == null) continue;

                        var entry = new LogEntry
                        {
                            Date = currentLog.Time,
                            Level = currentLog.Level?.ToString() ?? "INFO",
                            ThreadName = currentLog.ThreadName ?? "",
                            Logger = currentLog.LoggerName ?? "",
                            Message = currentLog.Message ?? ""
                        };
                        LogParserService.ParseLogEntry(entry);

                        // Apply result time filter
                        if (criteria.ResultTimeFilter != null)
                        {
                            if (criteria.ResultTimeFilter.From.HasValue && entry.Date < criteria.ResultTimeFilter.From.Value) continue;
                            if (criteria.ResultTimeFilter.To.HasValue && entry.Date > criteria.ResultTimeFilter.To.Value) continue;
                        }

                        // Evaluate structured criteria
                        if (EvaluateCriteria(entry, criteria))
                        {
                            matchCount++;
                            onResult(new GrepResult
                            {
                                Timestamp = entry.Date,
                                FilePath = filePath,
                                LineNumber = lineNumber,
                                LogType = logType,
                                PreviewText = entry.Message,
                                SessionName = fileName,
                                ReferencedLogEntry = entry,
                                SessionIndex = -1,
                                MatchedField = DetermineMatchedFields(entry, criteria)
                            });
                        }
                    }
                }
                finally
                {
                    memoryStream?.Dispose();
                }

                if (ct.IsCancellationRequested)
                    AppLogger.Info($"[Grep] Stream \"{fileName}\" ({logType}): cancelled after {lineNumber} entries, {matchCount} match(es) so far");
                else if (lineNumber > 0 && matchCount > 0)
                    AppLogger.Info($"[Grep] Stream \"{fileName}\" ({logType}): parsed {lineNumber} entries, {matchCount} match(es)");
                else if (lineNumber == 0)
                    AppLogger.Warn($"[Grep] Stream \"{fileName}\" ({logType}): 0 entries parsed (empty or unrecognized format)");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLogger.Warn($"[Grep] Error parsing stream '{fileName}': {ex.Message}");
            }

            return matchCount;
        }

    }
}
