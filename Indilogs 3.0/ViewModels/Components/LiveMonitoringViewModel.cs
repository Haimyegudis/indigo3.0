using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages live monitoring of log files - real-time updates via polling.
    /// Uses a local MemoryStream cache to avoid re-reading the entire file from network each poll.
    /// IndigoLogsReader REQUIRES reading from position 0 (binary format), so we re-parse
    /// the cached stream each cycle but SKIP already-seen entries for performance.
    /// </summary>
    public partial class LiveMonitoringViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly FilterSearchViewModel _filterVM;
        private readonly CaseManagementViewModel _caseVM;
        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly IDispatcher _dispatcher;

        // Live monitoring state
        private bool _isLiveMode;
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set
            {
                _isLiveMode = value;
                OnPropertyChanged();
            }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPaused));
            }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        // File watching infrastructure
        private CancellationTokenSource? _liveCts;
        private string? _liveFilePath;
        private ObservableRangeCollection<LogEntry>? _liveLogsCollection;
        private LogSessionData? _liveSession;
        private int _lastParsedLogCount = 0;

        // Polling state
        private volatile bool _isRefreshActive;
        private long _lastFileSize = 0;

        // Local cache: avoids re-reading the entire file from network on every poll.
        // We keep a local copy and only fetch new bytes from the network each poll.
        private MemoryStream? _cachedStream;

        // Lock for thread-safe access to _cachedStream, _lastFileSize, _lastParsedLogCount
        private readonly object _streamLock = new object();

        // Lock for thread-safe collection access
        private readonly object _collectionLock = new object();

        // Polling interval: re-parse from MemoryStream takes 3-7 seconds for large files,
        // so polling more frequently than 5 seconds is wasteful.
        private const int POLLING_INTERVAL_MS = 5000;

        // Commands
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }

        public LiveMonitoringViewModel(MainViewModel parent, LogSessionViewModel sessionVM,
            FilterSearchViewModel filterVM, CaseManagementViewModel caseVM,
            ILogFileService logService, ILogColoringService coloringService,
            IDispatcher dispatcher)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _filterVM = filterVM;
            _caseVM = caseVM;
            _logService = logService;
            _coloringService = coloringService;
            _dispatcher = dispatcher;

            LivePlayCommand = new RelayCommand(LivePlay);
            LivePauseCommand = new RelayCommand(LivePause);
            LiveClearCommand = new RelayCommand(LiveClear);
        }

        /// <summary>
        /// Starts live monitoring of a log file, polling for new entries on a timer.
        /// </summary>
        public void StartLiveMonitoring(string path)
        {
            // 1. Cleanup old session
            StopLiveMonitoring();
            _parent.ClearCommand.Execute(null);

            // 2. Set IsLiveMode FIRST to prevent filter operations from overwriting Logs
            IsLiveMode = true;

            // 3. UI Setup
            _sessionVM.LoadedFiles.Add(Path.GetFileName(path));
            _liveFilePath = path;

            // Create a synthetic LogSessionData so HasSessionLoaded becomes true
            // and all tabs (PLC, APP, etc.) become visible.
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            _liveSession = new LogSessionData
            {
                FileName = Path.GetFileName(path),
                FilePath = path,
            };
            _sessionVM.LoadedSessions.Add(_liveSession);
            _sessionVM.SelectedSession = _liveSession;
            // SwitchToSession overwrites AllLogsCache/Logs with session.Logs (empty List).
            // Re-assign our live observable collection so UI binds to it.
            _sessionVM.AllLogsCache = _liveLogsCollection;
            _sessionVM.Logs = _liveLogsCollection;

            // Clear and prepare FilteredLogs for live mode
            if (_filterVM.FilteredLogs != null)
            {
                _filterVM.FilteredLogs.Clear();
            }

            IsRunning = true;
            _parent.WindowTitle = "IndiLogs 3.0 - LIVE MONITORING";

            // 4. Initialize Control Token
            _liveCts = new CancellationTokenSource();

            // 5. Reset Polling State
            _lastFileSize = 0;
            _lastParsedLogCount = 0;

            // 6. Start the Polling Loop
            _sessionVM.StatusMessage = "Live: Connecting to file...";
            Task.Run(() => PollingLoop(_liveCts.Token));
        }

        /// <summary>
        /// Stops live monitoring, cancels polling, and disposes cached resources.
        /// </summary>
        public void StopLiveMonitoring()
        {
            // Cancel ongoing operations
            _liveCts?.Cancel();
            _liveCts?.Dispose();
            _liveCts = null;

            // Dispose cached stream (synchronized with polling loop)
            lock (_streamLock)
            {
                if (_cachedStream != null)
                {
                    _cachedStream.Dispose();
                    _cachedStream = null;
                }
            }

            // Remove synthetic live session
            if (_liveSession != null)
            {
                _sessionVM.LoadedSessions.Remove(_liveSession);
                if (_sessionVM.SelectedSession == _liveSession)
                    _sessionVM.SelectedSession = null;
                _liveSession = null;
            }

            // Reset state
            _isRefreshActive = false;
            IsRunning = false;
            IsPaused = false;
            IsLiveMode = false;

            _sessionVM.StatusMessage = "Live monitoring stopped";
        }

        private void LivePlay(object? obj)
        {
            if (!IsLiveMode || string.IsNullOrEmpty(_liveFilePath))
                return;

            IsRunning = true;
            IsPaused = false;
            _sessionVM.StatusMessage = "Live monitoring active.";
        }

        private void LivePause(object? obj)
        {
            IsRunning = false;
            IsPaused = true;
            _sessionVM.StatusMessage = "Live monitoring paused.";
        }

        private void LiveClear(object? obj)
        {
            // Clear logs and restart live monitoring
            _sessionVM.ClearCommand.Execute(null);

            if (!string.IsNullOrEmpty(_liveFilePath))
            {
                StartLiveMonitoring(_liveFilePath);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _liveCts?.Cancel();
                _liveCts?.Dispose();
                _liveCts = null;

                lock (_streamLock)
                {
                    _cachedStream?.Dispose();
                    _cachedStream = null;
                }
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
