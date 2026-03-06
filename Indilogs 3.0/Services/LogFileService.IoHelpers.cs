using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
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

        private BitmapImage? LoadBitmapFromZip(ZipArchiveEntry entry)
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
                ProcessName = string.IsNullOrEmpty(dto.ProcessName) ? "" : pool.Intern(dto.ProcessName),
                Method      = string.IsNullOrEmpty(dto.Method)      ? "" : pool.Intern(dto.Method),
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
                ProcessName = string.IsNullOrEmpty(dto.ProcessName) ? "" : dto.ProcessName,
                Method      = string.IsNullOrEmpty(dto.Method)      ? "" : dto.Method,
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
                    string? line;
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
                    string? line;
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
        private ILogFilePlugin? FindPlugin(string fileName, string[] sampleLines)
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Plugin parse failed: {ex.Message}");
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
