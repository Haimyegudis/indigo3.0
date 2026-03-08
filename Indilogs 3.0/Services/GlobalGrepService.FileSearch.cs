using System;
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
        public async Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken)
        {
            var results = new List<GrepResult>();
            if (string.IsNullOrWhiteSpace(path)) return results;

            Regex? regex = useRegex ? new Regex(searchQuery, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)) : null;
            bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() => {
                if (isZip) SearchZipFile(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
                else if (Directory.Exists(path)) SearchDirectory(path, searchQuery, regex, useRegex, searchPLC, searchAPP, results, progress, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);

            return results.OrderBy(r => r.Timestamp).ToList();
        }

        private void SearchStream(Stream stream, string filePath, string fileName, string logType, string searchQuery, Regex? regex, bool useRegex, List<GrepResult> results, CancellationToken cancellationToken)
        {
            int lineNumber = 0;

            try
            {
                // IndigoLogsReader requires a seekable stream, so copy to MemoryStream if needed
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
                    // Use IndigoLogsReader for proper parsing
                    var logReader = new IndigoLogsReader(seekableStream);

                    while (logReader.MoveToNext())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    lineNumber++;

                    var currentLog = logReader.Current;
                    if (currentLog == null) continue;

                    // Convert IndigoLog to LogEntry
                    var entry = new LogEntry
                    {
                        Date = currentLog.Time,
                        Level = currentLog.Level?.ToString() ?? "INFO",
                        ThreadName = currentLog.ThreadName ?? "",
                        Logger = currentLog.LoggerName ?? "",
                        Message = currentLog.Message ?? ""
                    };

                    // Parse Pattern, Data, Method, Exception from Message
                    LogParserService.ParseLogEntry(entry);

                    // Check if this log matches the search query
                    bool isMatch = false;
                    if (useRegex && regex != null)
                    {
                        isMatch = (!string.IsNullOrEmpty(entry.Message) && regex.IsMatch(entry.Message)) ||
                                  (!string.IsNullOrEmpty(entry.Exception) && regex.IsMatch(entry.Exception)) ||
                                  (!string.IsNullOrEmpty(entry.Method) && regex.IsMatch(entry.Method)) ||
                                  (!string.IsNullOrEmpty(entry.Data) && regex.IsMatch(entry.Data));
                    }
                    else
                    {
                        isMatch = (!string.IsNullOrEmpty(entry.Message) && entry.Message.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Exception) && entry.Exception.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Method) && entry.Method.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(entry.Data) && entry.Data.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    if (isMatch)
                    {
                        results.Add(new GrepResult
                        {
                            Timestamp = entry.Date,
                            FilePath = filePath,
                            LineNumber = lineNumber,
                            LogType = logType,
                            PreviewText = entry.Message,
                            SessionName = fileName,
                            ReferencedLogEntry = entry,
                            SessionIndex = -1
                        });
                    }
                }
                }

                finally
                {
                    memoryStream?.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Searching ZIP entry failed", ex);
            }
        }

        private void SearchZipFile(string zipPath, string q, Regex? r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)>? prog, CancellationToken ct)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var allEntries = archive.Entries.ToList();
                for (int i = 0; i < allEntries.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    var entry = allEntries[i];
                    if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                    string entryLower = entry.FullName.ToLowerInvariant();

                    // Nested ZIP — extract to memory and recurse
                    if (entryLower.EndsWith(".zip"))
                    {
                        try
                        {
                            using (var entryStream = entry.Open())
                            using (var ms = new MemoryStream())
                            {
                                entryStream.CopyTo(ms);
                                ms.Position = 0;
                                using (var innerArchive = new ZipArchive(ms, ZipArchiveMode.Read))
                                {
                                    var innerEntries = innerArchive.Entries
                                        .Where(e => IsLogEntry(e.FullName, plc, app)).ToList();
                                    foreach (var innerEntry in innerEntries)
                                    {
                                        if (ct.IsCancellationRequested) break;
                                        prog?.Report((i, allEntries.Count, $"Scanning: {entry.Name}/{innerEntry.Name}"));
                                        using (var s = innerEntry.Open())
                                        {
                                            SearchStream(s, zipPath, $"{entry.Name}/{innerEntry.Name}",
                                                DetermineLogType(innerEntry.FullName), q, r, u, res, ct);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            AppLogger.Warn($"[Grep] Error reading nested ZIP '{entry.Name}': {ex.Message}");
                        }
                        continue;
                    }

                    if (!IsLogEntry(entry.FullName, plc, app)) continue;

                    prog?.Report((i, allEntries.Count, $"Scanning: {entry.Name}"));
                    using (var s = entry.Open())
                    {
                        SearchStream(s, zipPath, entry.Name, DetermineLogType(entry.FullName), q, r, u, res, ct);
                    }
                }
            }
        }

        private void SearchDirectory(string path, string q, Regex? r, bool u, bool plc, bool app, List<GrepResult> res, IProgress<(int, int, string)>? prog, CancellationToken ct)
        {
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(f => IsLogFile(f, plc, app)).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                prog?.Report((i, files.Count, $"Scanning: {Path.GetFileName(files[i])}"));
                using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    SearchStream(fs, files[i], Path.GetFileName(files[i]), DetermineLogType(files[i]), q, r, u, res, ct);
                }
            }
        }

        private bool IsLogFile(string p, bool plc, bool app) => LogFileClassifier.IsLogFile(p, plc, app);
        private bool IsLogEntry(string entryName, bool plc, bool app) => LogFileClassifier.IsLogEntry(entryName, plc, app);
        private string DetermineLogType(string p) => LogFileClassifier.DetermineLogType(p);
    }
}
