using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class StepRecorderViewModel
    {
        // ─── Load ─────────────────────────────────────────────────────────────
        public async Task LoadFromZipAsync(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return;

            _zipPath = zipPath;
            StopPlay();
            IsLoading = true;
            StatusText = "Loading step frames…";

            // Quick ISR folder check before full load
            HasIsr = await Task.Run(() => ZipHasIsrFolder(zipPath));

            try
            {
                var frames = await Task.Run(() => ReadFramesFromZip(zipPath));

                _frames       = frames;
                _currentIndex = frames.Count > 0 ? 0 : -1;

                NotifyFrameChanged();

                StatusText = frames.Count > 0
                    ? $"Loaded {frames.Count} frames."
                    : "No step images found in IndigoLogs/ISR/Steps/.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading steps: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static List<StepFrame> ReadFramesFromZip(string zipPath)
        {
            var imageEntries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var textEntries  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            const string prefix = "IndigoLogs/ISR/Steps/";

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                // Normalize path separators
                string normalizedName = entry.FullName.Replace('\\', '/');
                if (!normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Relative filename inside Steps folder (no sub-folders expected)
                string relName = normalizedName.Substring(prefix.Length);
                if (relName.Contains('/'))
                    continue; // Skip sub-directories

                string ext = Path.GetExtension(relName).ToLowerInvariant();

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                {
                    using var ms = new MemoryStream();
                    using var stream = entry.Open();
                    stream.CopyTo(ms);
                    imageEntries[relName] = ms.ToArray();
                }
                else if (ext == ".txt")
                {
                    using var reader = new StreamReader(entry.Open());
                    textEntries[relName] = reader.ReadToEnd();
                }
            }

            var frames = new List<StepFrame>();

            foreach (var kv in imageEntries)
            {
                string imageFile = kv.Key;
                string baseName  = Path.GetFileNameWithoutExtension(imageFile);

                // Find matching text file (same base name, any txt extension)
                string? textKey = textEntries.Keys
                    .FirstOrDefault(k => string.Equals(
                        Path.GetFileNameWithoutExtension(k), baseName,
                        StringComparison.OrdinalIgnoreCase));

                string textContent = textKey != null ? textEntries[textKey] : string.Empty;

                DateTime ts = ParseTimestampFromFilename(baseName);

                frames.Add(new StepFrame
                {
                    FileName    = imageFile,
                    Timestamp   = ts,
                    ImageData   = kv.Value,
                    TextContent = textContent
                });
            }

            // Sort by timestamp, then by filename for stable ordering
            frames.Sort((a, b) =>
            {
                int cmp = a.Timestamp.CompareTo(b.Timestamp);
                return cmp != 0 ? cmp : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            return frames;
        }

        private static DateTime ParseTimestampFromFilename(string baseName)
        {
            // Try common timestamp formats embedded in filenames
            // Examples: "2024-01-15_10-30-45", "20240115_103045", "2024-01-15 10-30-45.123"
            string[] formats =
            {
                "yyyy-MM-dd_HH-mm-ss",
                "yyyy-MM-dd_HH-mm-ss.fffffff", "yyyy-MM-dd_HH-mm-ss.ffffff", "yyyy-MM-dd_HH-mm-ss.fff",
                "yyyyMMdd_HHmmss",
                "yyyyMMdd_HHmmss.fffffff", "yyyyMMdd_HHmmss.ffffff", "yyyyMMdd_HHmmss.fff",
                "yyyy-MM-dd HH-mm-ss",
                "yyyy-MM-dd HH-mm-ss.fffffff", "yyyy-MM-dd HH-mm-ss.ffffff", "yyyy-MM-dd HH-mm-ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ss.ffffff", "yyyy-MM-dd HH:mm:ss.fff",
            };

            // Try to extract a timestamp substring by sliding over candidate ranges
            foreach (string fmt in formats)
            {
                if (baseName.Length >= fmt.Length)
                {
                    // Try at the start, and slide through the string
                    for (int start = 0; start <= baseName.Length - fmt.Length; start++)
                    {
                        string candidate = baseName.Substring(start, fmt.Length);
                        if (DateTime.TryParseExact(candidate, fmt,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out DateTime dt))
                            return dt;
                    }
                }
            }

            return DateTime.MinValue; // fallback — will sort to front
        }

        private static bool ZipHasIsrFolder(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                return zip.Entries.Any(e =>
                    e.FullName.Replace('\\', '/').StartsWith("IndigoLogs/ISR/",
                        StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { AppLogger.Error("ZipHasIsrFolder failed", ex); return false; }
        }
    }
}
