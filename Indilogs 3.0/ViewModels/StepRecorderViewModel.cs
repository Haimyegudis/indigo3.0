using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>A single step image frame with optional matching text file content.</summary>
    public class StepFrame
    {
        public string   FileName    { get; set; } = "";
        public DateTime Timestamp   { get; set; }
        public byte[]?  ImageData   { get; set; }
        public string   TextContent { get; set; } = "";

        private BitmapImage? _bitmap;
        public BitmapImage? Bitmap => _bitmap ??= CreateBitmap(ImageData);

        private static BitmapImage? CreateBitmap(byte[]? data)
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

    public partial class StepRecorderViewModel : ViewModelBase
    {
        // ─── Dependencies ────────────────────────────────────────────────────
        private readonly IDialogService _dialogService;

        // ─── State ───────────────────────────────────────────────────────────
        private List<StepFrame> _frames = new List<StepFrame>();
        private int _currentIndex = -1;
        private DispatcherTimer? _timer;
        private string? _zipPath;

        // ─── Properties ──────────────────────────────────────────────────────
        /// <summary>True when the loaded ZIP contains an IndigoLogs/ISR/ folder — drives tab visibility.</summary>
        private bool _hasIsr;
        public bool HasIsr
        {
            get => _hasIsr;
            private set { _hasIsr = value; OnPropertyChanged(); }
        }

        public bool HasFrames => _frames.Count > 0;

        public StepFrame? CurrentFrame =>
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
                    string? zipDir = Path.GetDirectoryName(_zipPath);
                    string zipNameNoExt = Path.GetFileNameWithoutExtension(_zipPath) ?? "unknown";
                    extractDir = Path.Combine(zipDir ?? "", zipNameNoExt, "IndigoLogs", "ISR", "Steps");
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{destPath}\"") { UseShellExecute = true });
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
