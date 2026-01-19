using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
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
    /// Manages live monitoring of log files - real-time updates via FileSystemWatcher and polling
    /// </summary>
    public class LiveMonitoringViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly FilterSearchViewModel _filterVM;
        private readonly CaseManagementViewModel _caseVM;
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;

        // Live monitoring state
        private bool _isLiveMode;
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set
            {
                _isLiveMode = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsLiveMode));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsRunning));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsPaused));
            }
        }

        // File watching infrastructure
        private FileSystemWatcher _fileWatcher;
        private CustomLiveLogReader _customReader;
        private CancellationTokenSource _liveCts;
        private string _liveFilePath;
        private ObservableRangeCollection<LogEntry> _liveLogsCollection;
        private int _lastParsedLogCount = 0;

        // Polling state
        private bool _isPollingActive;
        private bool _isBackgroundLoadingActive;
        private bool _isRefreshActive;
        private DateTime _lastFileCheckTime = DateTime.MinValue;
        private long _lastFileSize = 0;
        private long _lastStreamPosition = 0;

        // Lock for thread-safe collection access
        private readonly object _collectionLock = new object();

        // Constants
        private const int POLLING_INTERVAL_MS = 5000;
        private const int INITIAL_LOAD_MINUTES = 2;
        private const int MIN_REFRESH_INTERVAL_MS = 5000;

        // Commands
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }

        public LiveMonitoringViewModel(MainViewModel parent, LogSessionViewModel sessionVM,
            FilterSearchViewModel filterVM, CaseManagementViewModel caseVM,
            LogFileService logService, LogColoringService coloringService)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _filterVM = filterVM;
            _caseVM = caseVM;
            _logService = logService;
            _coloringService = coloringService;

            // Initialize commands
            LivePlayCommand = new RelayCommand(LivePlay);
            LivePauseCommand = new RelayCommand(LivePause);
            LiveClearCommand = new RelayCommand(LiveClear);

            _customReader = new CustomLiveLogReader();
        }

        public void StartLiveMonitoring(string path)
        {
            // Cleanup
            StopLiveMonitoring();
            _parent.ClearCommand.Execute(null);

            // UI Setup
            _sessionVM.LoadedFiles.Add(Path.GetFileName(path));
            _liveFilePath = path;
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            _sessionVM.AllLogsCache = _liveLogsCollection;
            _sessionVM.Logs = _liveLogsCollection;

            IsLiveMode = true;
            IsRunning = true;
            _parent.WindowTitle = "IndiLogs 3.0 - LIVE MONITORING (Custom)";

            // Initialize new mechanism
            _liveCts = new CancellationTokenSource();
            _customReader = new CustomLiveLogReader();

            // Register events
            _customReader.OnStatusChanged += (msg) =>
            {
                Application.Current.Dispatcher.Invoke(() => _sessionVM.StatusMessage = msg);
            };

            _customReader.OnLogsReceived += (newLogs) =>
            {
                Task.Run(async () =>
                {
                    // 1. Apply colors (default + custom if exists)
                    await _coloringService.ApplyDefaultColorsAsync(newLogs, false);

                    // If there are active custom coloring rules
                    if (_caseVM.MainColoringRules != null && _caseVM.MainColoringRules.Any())
                    {
                        await _coloringService.ApplyCustomColoringAsync(newLogs, _caseVM.MainColoringRules);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        lock (_collectionLock)
                        {
                            foreach (var log in newLogs) // Maintain chronological order
                            {
                                // A. Always add to main tab (PLC) and memory
                                _liveLogsCollection.Insert(0, log);

                                // B. Add to filtered tab (PLC FILTERED) only if meets conditions!
                                if (ShouldShowInFilteredView(log))
                                {
                                    _filterVM.FilteredLogs.Insert(0, log);
                                }
                            }

                            _sessionVM.StatusMessage = $"Live: {_filterVM.FilteredLogs.Count:N0} filtered / {_liveLogsCollection.Count:N0} total";
                        }
                    });
                });
            };

            // Start in background
            Task.Run(() => _customReader.StartMonitoring(path, _liveCts.Token));
        }

        public void StopLiveMonitoring()
        {
            // Cancel ongoing operations
            _liveCts?.Cancel();
            _liveCts?.Dispose();
            _liveCts = null;

            // Stop file watcher
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }

            // Reset state
            _isPollingActive = false;
            _isBackgroundLoadingActive = false;
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
            bool hasSearch = !string.IsNullOrWhiteSpace(_parent.SearchText);
            bool hasActiveFilter = _filterVM.IsMainFilterActive || hasSearch || _filterVM.ActiveThreadFilters.Any();

            // 3. If no filters active -> use default logic (Default Colors/Filter)
            // This makes "PLC Filtered" tab look like normal loading
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
                if (log.Message == null || log.Message.IndexOf(_parent.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Advanced Tree / Condition Filter
            if (_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children != null && _filterVM.MainFilterRoot.Children.Count > 0)
            {
                if (!_filterVM.EvaluateFilterNode(log, _filterVM.MainFilterRoot)) return false;
            }

            return true;
        }

        public async Task RefreshLogsOptimized()
        {
            if (_isRefreshActive) return;

            _isRefreshActive = true;
            Debug.WriteLine($">>> RefreshLogsOptimized STARTED");

            try
            {
                await Task.Delay(100); // Wait for write to complete

                long currentFileSize;
                try
                {
                    var fileInfo = new FileInfo(_liveFilePath);
                    currentFileSize = fileInfo.Length;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  ❌ Cannot access file: {ex.Message}");
                    return;
                }

                const long MIN_GROWTH = 5120; // 5KB
                long growth = currentFileSize - _lastFileSize;

                if (growth < MIN_GROWTH)
                {
                    Debug.WriteLine($"  ℹ️ File grew by only {growth:N0} bytes - skipping");
                    return;
                }

                _lastFileSize = currentFileSize;
                Debug.WriteLine($"  File grew by {growth:N0} bytes");

                // STRATEGY 1: Try to seek to last position (FAST!)
                Debug.WriteLine($"  🎯 Attempting OPTIMIZED read from position {_lastStreamPosition:N0}...");

                bool optimizedSuccess = false;
                List<LogEntry> newLogs = null;

                try
                {
                    newLogs = await Task.Run(() =>
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // Try seeking to last known position
                            if (_lastStreamPosition > 0 && _lastStreamPosition < fs.Length)
                            {
                                Debug.WriteLine($"    Seeking to position {_lastStreamPosition:N0}...");
                                fs.Seek(_lastStreamPosition, SeekOrigin.Begin);

                                Debug.WriteLine($"    Creating reader from seeked position...");
                                var result = _logService.ParseLogStream(fs);

                                if (result.AllLogs != null && result.AllLogs.Count > 0)
                                {
                                    Debug.WriteLine($"    ✅ SUCCESS! Parsed {result.AllLogs.Count:N0} new logs from seeked position!");

                                    // Update position
                                    _lastStreamPosition = fs.Position;

                                    optimizedSuccess = true;
                                    return result.AllLogs;
                                }
                                else
                                {
                                    Debug.WriteLine($"    ⚠️ No logs from seeked position, falling back...");
                                }
                            }

                            return null;
                        }
                    });
                }
                catch (Exception seekEx)
                {
                    Debug.WriteLine($"    ❌ Seek strategy failed: {seekEx.Message}");
                }

                // STRATEGY 2: Fallback - parse entire file (SLOW)
                if (!optimizedSuccess || newLogs == null)
                {
                    Debug.WriteLine($"  ⚙️ Falling back to full file parse...");

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    newLogs = await Task.Run(() =>
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var ms = new MemoryStream())
                        {
                            fs.CopyTo(ms);
                            ms.Position = 0;

                            var result = _logService.ParseLogStream(ms);
                            var allLogs = result.AllLogs;

                            if (allLogs != null && allLogs.Count > _lastParsedLogCount)
                            {
                                var deltaLogs = allLogs.Skip(_lastParsedLogCount).ToList();
                                _lastParsedLogCount = allLogs.Count;
                                _lastStreamPosition = fs.Position; // Update position

                                return deltaLogs;
                            }

                            return null;
                        }
                    });

                    sw.Stop();
                    Debug.WriteLine($"  Full parse took {sw.ElapsedMilliseconds:N0}ms");
                }

                // Add new logs to UI
                if (newLogs != null && newLogs.Count > 0)
                {
                    await _coloringService.ApplyDefaultColorsAsync(newLogs, false);
                    if (_caseVM.MainColoringRules != null && _caseVM.MainColoringRules.Count > 0)
                        await _coloringService.ApplyCustomColoringAsync(newLogs, _caseVM.MainColoringRules);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        lock (_collectionLock)
                        {
                            foreach (var log in newLogs.OrderByDescending(l => l.Date))
                            {
                                _liveLogsCollection.Insert(0, log);
                                _filterVM.FilteredLogs.Insert(0, log);
                            }

                            if (_parent.SelectedLog == null && _filterVM.FilteredLogs.Count > 0)
                                _parent.SelectedLog = _filterVM.FilteredLogs[0];

                            _sessionVM.StatusMessage = $"Live: {_filterVM.FilteredLogs.Count:N0} shown (+{newLogs.Count} new) | {_liveLogsCollection.Count:N0} total";
                        }
                    });

                    string method = optimizedSuccess ? "OPTIMIZED SEEK" : "full parse";
                    Debug.WriteLine($"  ✅ Added {newLogs.Count:N0} new logs via {method}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ RefreshLogsOptimized error: {ex.Message}");
            }
            finally
            {
                _isRefreshActive = false;
                Debug.WriteLine($">>> RefreshLogsOptimized FINISHED");
            }
        }

        public async Task PollingLoop(CancellationToken token)
        {
            Debug.WriteLine($">>> PollingLoop STARTED");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsRunning && !string.IsNullOrEmpty(_liveFilePath) && File.Exists(_liveFilePath))
                    {
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Polling trigger...");
                        await RefreshLogsOptimized();
                    }
                    else
                    {
                        if (!IsRunning)
                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Polling skipped (paused)");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ PollingLoop error: {ex.Message}");
                }

                await Task.Delay(POLLING_INTERVAL_MS, token);
            }

            Debug.WriteLine($">>> PollingLoop STOPPED");
        }

        public void Cleanup()
        {
            StopLiveMonitoring();
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
