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
    public class LiveMonitoringViewModel : ViewModelBase
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

        private void LivePlay(object obj)
        {
            if (!IsLiveMode || string.IsNullOrEmpty(_liveFilePath))
                return;

            IsRunning = true;
            IsPaused = false;
            _sessionVM.StatusMessage = "Live monitoring active.";
        }

        private void LivePause(object obj)
        {
            IsRunning = false;
            IsPaused = true;
            _sessionVM.StatusMessage = "Live monitoring paused.";
        }

        private void LiveClear(object obj)
        {
            // Clear logs and restart live monitoring
            _sessionVM.ClearCommand.Execute(null);

            if (!string.IsNullOrEmpty(_liveFilePath))
            {
                StartLiveMonitoring(_liveFilePath);
            }
        }

        private bool ShouldShowInFilteredView(LogEntry log)
        {
            // 1. Check Negative Filters (Filter Out) - always active if defined
            if (_filterVM.IsMainFilterOutActive && _filterVM.NegativeFilters.Any())
            {
                foreach (var f in _filterVM.NegativeFilters)
                {
                    if (f.StartsWith("THREAD:"))
                    {
                        if (log.ThreadName != null && log.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                    else
                    {
                        if (log.Message != null && log.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                }
            }

            // 2. Are there active filters? (search, trees, Threads)
            bool hasSearch = !string.IsNullOrWhiteSpace(_filterVM.SearchText);
            bool hasActiveFilter = _filterVM.IsMainFilterActive || hasSearch || _filterVM.ActiveThreadFilters.Any();

            // 3. If no filters active -> use default PLC filter (same as regular file loading)
            if (!hasActiveFilter)
            {
                return _filterVM.IsDefaultLog(log);
            }

            // 4. Check active filters

            // Thread Filter
            if (_filterVM.ActiveThreadFilters.Any())
            {
                if (!_filterVM.ActiveThreadFilters.Contains(log.ThreadName)) return false;
            }

            // Search Text
            if (hasSearch)
            {
                if (log.Message == null || log.Message.IndexOf(_filterVM.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Advanced Tree / Condition Filter
            if (_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children != null && _filterVM.MainFilterRoot.Children.Count > 0)
            {
                if (!_filterVM.EvaluateFilterNode(log, _filterVM.MainFilterRoot)) return false;
            }

            return true;
        }

        /// <summary>
        /// Incrementally reads new bytes from the live file, parses new log entries, and updates the UI.
        /// </summary>
        public async Task RefreshLogsOptimized()
        {
            if (_isRefreshActive) return;
            _isRefreshActive = true;

            try
            {
                long currentFileSize;
                try
                {
                    var fileInfo = new FileInfo(_liveFilePath);
                    currentFileSize = fileInfo.Length;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Cannot access file", ex);
                    return;
                }

                bool isFirstRun = _cachedStream == null;

                // Skip if file hasn't grown since last check
                if (!isFirstRun && currentFileSize <= _lastFileSize)
                {
                    return;
                }

                long deltaBytes = isFirstRun ? currentFileSize : (currentFileSize - _lastFileSize);
                var sw = Stopwatch.StartNew();

                // ── Build/update local cache ──────────────────────────────────
                // IndigoLogsReader binary format REQUIRES reading from position 0.
                // Re-reading the entire file (85 MB+) from a network share every 5 s is too slow.
                // Instead we keep a local MemoryStream and only fetch the NEW bytes each poll.
                bool cacheUpdated = false;
                await Task.Run(() =>
                {
                    try
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144))
                        {
                            if (isFirstRun)
                            {
                                // First run: read the entire file into a local MemoryStream
                                _cachedStream = new MemoryStream((int)fs.Length + 1024 * 1024);
                                fs.CopyTo(_cachedStream);
                                cacheUpdated = true;
                            }
                            else
                            {
                                // Incremental: read only the new bytes and append to cache
                                long oldSize = _lastFileSize;
                                if (fs.Length > oldSize)
                                {
                                    fs.Seek(oldSize, SeekOrigin.Begin);
                                    _cachedStream.Seek(0, SeekOrigin.End);
                                    fs.CopyTo(_cachedStream);
                                    cacheUpdated = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Cache update error", ex);
                    }
                });

                if (!cacheUpdated)
                {
                    AppLogger.Info("Cache not updated (network error or no new data)");
                    return;
                }

                _lastFileSize = currentFileSize;
                long cacheMs = sw.ElapsedMilliseconds;

                // ── Parse the cached stream ───────────────────────────────────
                if (_cachedStream == null || _cachedStream.Length == 0) return;

                List<LogEntry> newLogs = null;
                int totalParsed = 0;

                await Task.Run(() =>
                {
                    try
                    {
                        _cachedStream.Position = 0;

                        if (isFirstRun)
                        {
                            // First run: parse everything (no entries to skip)
                            var allLogs = _logService.ParseLogStreamPartial(_cachedStream);
                            totalParsed = allLogs.Count;
                            newLogs = allLogs;
                            _lastParsedLogCount = totalParsed;
                        }
                        else
                        {
                            // Incremental: skip already-seen entries, only create LogEntry for new ones
                            var result = _logService.ParseLogStreamSkipExisting(_cachedStream, _lastParsedLogCount);
                            totalParsed = result.TotalCount;
                            newLogs = result.NewEntries;
                            _lastParsedLogCount = totalParsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Parse error", ex);
                        newLogs = new List<LogEntry>();
                    }
                });

                long parseMs = sw.ElapsedMilliseconds - cacheMs;
                AppLogger.Info($"Cache: {cacheMs}ms ({deltaBytes:N0} bytes) | Parse: {parseMs}ms (total={totalParsed:N0}, new={newLogs?.Count ?? 0})");

                // ── Update UI ─────────────────────────────────────────────────
                if (newLogs != null && newLogs.Count > 0)
                {
                    // Apply coloring in background (fire-and-forget)
                    var logsForColoring = newLogs;
                    _ = ApplyColoringInBackgroundAsync(logsForColoring);

                    var logsToAdd = newLogs;
                    var wasFirstRun = isFirstRun;

                    _dispatcher.Post(() =>
                    {
                        lock (_collectionLock)
                        {
                            try
                            {
                                _liveLogsCollection.AddRange(logsToAdd);

                                // Also update filtered view
                                var filteredToAdd = logsToAdd.Where(l => ShouldShowInFilteredView(l)).ToList();
                                if (filteredToAdd.Count > 0 && _filterVM.FilteredLogs != null)
                                {
                                    _filterVM.FilteredLogs.AddRange(filteredToAdd);
                                }

                                if (wasFirstRun)
                                    _sessionVM.StatusMessage = $"Live: Loaded {_liveLogsCollection.Count:N0} logs (watching for new data...)";
                                else
                                    _sessionVM.StatusMessage = $"Live: +{logsToAdd.Count:N0} new (Total: {_liveLogsCollection.Count:N0})";

                                // Auto-scroll to bottom so user sees new entries
                                _parent.ScrollTabToBottom("PLC");
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Error("UI update error", ex);
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.DataBind);
                }
                else if (isFirstRun)
                {
                    _dispatcher.Post(() =>
                    {
                        _sessionVM.StatusMessage = "Live: File loaded but 0 logs parsed. Watching for new data...";
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("RefreshLogsOptimized error", ex);
            }
            finally
            {
                _isRefreshActive = false;
            }
        }

        private async Task ApplyColoringInBackgroundAsync(List<LogEntry> logs)
        {
            try
            {
                await _coloringService.ApplyDefaultColorsAsync(logs, false).ConfigureAwait(false);
                if (_caseVM.MainColoringRules != null && _caseVM.MainColoringRules.Any())
                    await _coloringService.ApplyCustomColoringAsync(logs, _caseVM.MainColoringRules).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Error("Coloring failed", ex); }
        }

        /// <summary>
        /// Main polling loop that periodically calls RefreshLogsOptimized until cancelled.
        /// </summary>
        public async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsRunning && !string.IsNullOrEmpty(_liveFilePath))
                    {
                        await RefreshLogsOptimized();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Polling error", ex);
                }

                try
                {
                    await Task.Delay(POLLING_INTERVAL_MS, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Starts auto-refresh polling for a statically-loaded .file.
        /// Called after ProcessFiles completes the initial static load.
        /// Pre-fills local cache from the existing session data (no extra network read).
        /// </summary>
        public void StartFileWatcher(string filePath, LogSessionData session)
        {
            // Don't start if already in live mode
            if (IsLiveMode) return;

            // Set IsLiveMode FIRST to prevent filter operations from overwriting Logs
            IsLiveMode = true;

            _liveFilePath = filePath;
            _liveSession = session;

            // Create an observable collection that wraps the existing session logs
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            if (session.Logs != null && session.Logs.Count > 0)
            {
                _liveLogsCollection.AddRange(session.Logs);
            }

            // Point UI to the live collection
            _sessionVM.AllLogsCache = _liveLogsCollection;
            _sessionVM.Logs = _liveLogsCollection;

            _lastParsedLogCount = _liveLogsCollection.Count;

            IsRunning = true;
            _parent.WindowTitle = $"IndiLogs 3.0 - AUTO-REFRESH: {Path.GetFileName(filePath)}";

            // Pre-fill local cache in background to avoid blocking the UI thread.
            // The initial file read from network can take 20-30 seconds for large files.
            _liveCts = new CancellationTokenSource();
            _ = PreFillCacheAndStartPollingAsync(filePath, _liveCts.Token);

            _sessionVM.StatusMessage = $"Auto-refresh active ({_liveLogsCollection.Count:N0} logs loaded, watching for new data...)";
        }

        /// <summary>
        /// Stops live monitoring and releases all resources.
        /// </summary>
        public void Cleanup()
        {
            StopLiveMonitoring();
        }

        private async Task PreFillCacheAndStartPollingAsync(string filePath, CancellationToken token)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144))
                {
                    _cachedStream = new MemoryStream((int)fs.Length + 1024 * 1024);
                    await fs.CopyToAsync(_cachedStream, token).ConfigureAwait(false);
                    _lastFileSize = fs.Length;
                }
                AppLogger.Info($"Cache pre-filled: {_lastFileSize:N0} bytes in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cache pre-fill error", ex);
                _lastFileSize = 0;
                // Cache will be built on first poll cycle instead
            }

            // Start polling after cache is ready
            await PollingLoop(token).ConfigureAwait(false);
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
