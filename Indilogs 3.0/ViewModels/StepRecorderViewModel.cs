#nullable disable
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>A single step image frame with optional matching text file content.</summary>
    public class StepFrame
    {
        public string   FileName    { get; set; }
        public DateTime Timestamp   { get; set; }
        public byte[]   ImageData   { get; set; }
        public string   TextContent { get; set; }

        private BitmapImage _bitmap;
        public BitmapImage Bitmap => _bitmap ??= CreateBitmap(ImageData);

        private static BitmapImage CreateBitmap(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(data);
            bmp.BeginInit();
            bmp.StreamSource    = ms;
            bmp.CacheOption     = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }

    public class StepRecorderViewModel : ViewModelBase
    {
        // ─── Dependencies ────────────────────────────────────────────────────
        private readonly IDialogService _dialogService;

        // ─── State ───────────────────────────────────────────────────────────
        private List<StepFrame> _frames = new List<StepFrame>();
        private int _currentIndex = -1;
        private DispatcherTimer _timer;
        private string _zipPath; // Stores the ZIP file path for extracting ISR files alongside it

        // ─── Properties ──────────────────────────────────────────────────────
        /// <summary>True when the loaded ZIP contains an IndigoLogs/ISR/ folder — drives tab visibility.</summary>
        private bool _hasIsr;
        public bool HasIsr
        {
            get => _hasIsr;
            private set { _hasIsr = value; OnPropertyChanged(); }
        }

        public bool HasFrames => _frames.Count > 0;

        public StepFrame CurrentFrame =>
            (_currentIndex >= 0 && _currentIndex < _frames.Count) ? _frames[_currentIndex] : null;

        public int CurrentIndex => HasFrames ? _currentIndex + 1 : 0;
        public int TotalFrames  => _frames.Count;

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            private set { _isPlaying = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _statusText = "No step data loaded.";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        // ─── Commands ─────────────────────────────────────────────────────────
        public ICommand PreviousCommand   { get; }
        public ICommand NextCommand       { get; }
        public ICommand PlayCommand       { get; }
        public ICommand StopCommand       { get; }
        public ICommand ScreenshotCommand { get; }
        public ICommand OpenFolderCommand { get; }

        // ─── Constructor ─────────────────────────────────────────────────────
        public StepRecorderViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
            PreviousCommand   = new RelayCommand(_ => MovePrevious(),   _ => HasFrames && _currentIndex > 0);
            NextCommand       = new RelayCommand(_ => MoveNext(),       _ => HasFrames && _currentIndex < _frames.Count - 1);
            PlayCommand       = new RelayCommand(_ => StartPlay(),      _ => HasFrames && !IsPlaying);
            StopCommand       = new RelayCommand(_ => StopPlay(),       _ => IsPlaying);
            ScreenshotCommand = new RelayCommand(_ => CopyToClipboard(),_ => CurrentFrame?.Bitmap != null);
            OpenFolderCommand = new RelayCommand(_ => OpenInFolder(),   _ => CurrentFrame != null);
        }

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
                string textKey = textEntries.Keys
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

        // ─── Navigation ───────────────────────────────────────────────────────
        private void MovePrevious()
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                NotifyFrameChanged();
            }
        }

        private void MoveNext()
        {
            if (_currentIndex < _frames.Count - 1)
            {
                _currentIndex++;
                NotifyFrameChanged();
            }
            else
            {
                // Reached end — stop playback
                StopPlay();
            }
        }

        // ─── Playback ─────────────────────────────────────────────────────────
        private void StartPlay()
        {
            if (!HasFrames || IsPlaying) return;

            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _timer.Tick += (s, e) => MoveNext();
            }

            IsPlaying = true;
            _timer.Start();
        }

        private void StopPlay()
        {
            _timer?.Stop();
            IsPlaying = false;
        }

        // ─── Screenshot ───────────────────────────────────────────────────────
        private void CopyToClipboard()
        {
            var bmp = CurrentFrame?.Bitmap;
            if (bmp == null) return;
            try { Clipboard.SetImage(bmp); }
            catch (Exception ex) { AppLogger.Warn($"Clipboard copy failed (may be locked): {ex.Message}"); }
        }

        // ─── Open in Explorer ─────────────────────────────────────────────────
        private void OpenInFolder()
        {
            var frame = CurrentFrame;
            if (frame == null || frame.ImageData == null) return;

            try
            {
                // Extract alongside the ZIP file: {ZipDir}\{ZipName}\IndigoLogs\ISR\Steps\
                string extractDir;
                if (!string.IsNullOrEmpty(_zipPath) && File.Exists(_zipPath))
                {
                    string zipDir = Path.GetDirectoryName(_zipPath);
                    string zipNameNoExt = Path.GetFileNameWithoutExtension(_zipPath);
                    extractDir = Path.Combine(zipDir, zipNameNoExt, "IndigoLogs", "ISR", "Steps");
                }
                else
                {
                    extractDir = Path.Combine(Path.GetTempPath(), "IndiLogs_ISR");
                }

                Directory.CreateDirectory(extractDir);

                string safeFileName = Path.GetFileName(frame.FileName); // Prevent path traversal
                string destPath = Path.Combine(extractDir, safeFileName);
                File.WriteAllBytes(destPath, frame.ImageData);

                // Open Explorer with the file selected
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{destPath}\"");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Could not open folder:\n{ex.Message}", "Error");
            }
        }

        // ─── Clear ────────────────────────────────────────────────────────────
        public void Clear()
        {
            StopPlay();
            _frames       = new List<StepFrame>();
            _currentIndex = -1;
            HasIsr        = false;
            NotifyFrameChanged();
            StatusText = "No step data loaded.";
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

        // ─── Helpers ──────────────────────────────────────────────────────────
        private void NotifyFrameChanged()
        {
            OnPropertyChanged(nameof(CurrentFrame));
            OnPropertyChanged(nameof(CurrentIndex));
            OnPropertyChanged(nameof(TotalFrames));
            OnPropertyChanged(nameof(HasFrames));
            OnPropertyChanged(nameof(StatusText));

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (!HasFrames) return;
            var frame = CurrentFrame;
            StatusText = frame != null
                ? $"Frame {CurrentIndex} / {TotalFrames}  |  {frame.Timestamp:HH:mm:ss.ffffff}  |  {frame.FileName}"
                : StatusText;
        }

        // INotifyPropertyChanged inherited from ViewModelBase

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
