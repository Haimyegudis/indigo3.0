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

        // Buffering for performance
        private readonly List<LogEntry> _pendingLogs = new List<LogEntry>();
        private System.Threading.Timer _flushTimer;

        // Constants
        private const int POLLING_INTERVAL_MS = 2000; // Reduced from 5000 to 2000 for faster updates
        private const int INITIAL_LOAD_MINUTES = 2;
        private const int MIN_REFRESH_INTERVAL_MS = 2000; // Reduced from 5000 to 2000
        private const int MAX_LOGS_PER_BATCH = 5000; // Limit batch size to prevent UI freeze

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
            // 1. Cleanup old session
            StopLiveMonitoring();
            _parent.ClearCommand.Execute(null);

            // 2. UI Setup
            _sessionVM.LoadedFiles.Add(Path.GetFileName(path));
            _liveFilePath = path;

            // יצירת קולקציה חדשה וחיבור שלה ל-SessionVM כדי שהתצוגה תתעדכן
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            _sessionVM.AllLogsCache = _liveLogsCollection;
            _sessionVM.Logs = _liveLogsCollection;

            // Clear and prepare FilteredLogs for live mode
            if (_filterVM.FilteredLogs != null)
            {
                _filterVM.FilteredLogs.Clear();
            }

            IsLiveMode = true;
            IsRunning = true;
            _parent.WindowTitle = "IndiLogs 3.0 - LIVE MONITORING";

            // 3. Initialize Control Token
            _liveCts = new CancellationTokenSource();

            // 4. Reset Polling State (חשוב לאפס כדי שהקריאה הראשונה תהיה תקינה)
            _lastFileSize = 0;
            _lastStreamPosition = 0;
            _lastParsedLogCount = 0;

            // 5. Start the Polling Loop (זה החלק שהיה חסר!)
            // מפעיל את הלולאה שבודקת את הקובץ כל 5 שניות
            Task.Run(() => PollingLoop(_liveCts.Token));
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
            bool hasActiveFilter = _filterVM.IsMainFilterActive || hasSearch || _filterVM.ActivePlcThreadFilters.Any();

            // 3. If no filters active -> use default PLC filter (same as regular file loading)
            if (!hasActiveFilter)
            {
                return _filterVM.IsDefaultLog(log);
            }

            // 4. Check active filters

            // Thread Filter (PLC-specific)
            if (_filterVM.ActivePlcThreadFilters.Any())
            {
                if (!_filterVM.ActivePlcThreadFilters.Contains(log.ThreadName)) return false;
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

            try
            {
                // OPTIMIZATION: Remove unnecessary delay
                // await Task.Delay(100); - REMOVED

                var fileInfo = new FileInfo(_liveFilePath);
                long currentFileSize = fileInfo.Length;
                bool isFirstRun = _lastFileSize == 0;

                // אם לא גדל משמעותית - דלג (minimum 1KB change required)
                if (!isFirstRun && currentFileSize <= _lastFileSize + 1024)
                {
                    _isRefreshActive = false;
                    return;
                }

                _lastFileSize = currentFileSize;

                // קריאה ופארסינג ברקע - OPTIMIZED
                List<LogEntry> newLogs = await Task.Run(() =>
                {
                    try
                    {
                        using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // אופטימיזציה: אם זה טעינה ראשונית והקובץ ענק (>50MB), קרא רק את הסוף
                            if (isFirstRun)
                            {
                                long maxBytes = 50 * 1024 * 1024; // 50MB limit
                                if (fs.Length > maxBytes)
                                {
                                    fs.Seek(fs.Length - maxBytes, SeekOrigin.Begin);
                                    // דלג עד סוף השורה הקרובה כדי לא לקרוא זבל
                                    int b;
                                    while ((b = fs.ReadByte()) != -1 && b != '\n') { }
                                    System.Diagnostics.Debug.WriteLine($"[LIVE] Optimization: Initial load limited to last 50MB.");
                                }
                                else
                                {
                                    fs.Position = 0;
                                }
                            }
                            else
                            {
                                // ריצות המשך - המשך מאיפה שעצרנו
                                if (_lastStreamPosition > 0 && _lastStreamPosition < fs.Length)
                                    fs.Seek(_lastStreamPosition, SeekOrigin.Begin);
                                else
                                    fs.Position = 0;
                            }

                            var result = _logService.ParseLogStream(fs);
                            _lastStreamPosition = fs.Position;
                            return result.AllLogs ?? new List<LogEntry>();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LIVE READ ERROR] {ex.Message}");
                        return new List<LogEntry>();
                    }
                });

                // OPTIMIZATION: Limit batch size to prevent UI freeze
                if (newLogs != null && newLogs.Count > MAX_LOGS_PER_BATCH)
                {
                    System.Diagnostics.Debug.WriteLine($"[LIVE] Batch too large ({newLogs.Count}), limiting to {MAX_LOGS_PER_BATCH}");
                    newLogs = newLogs.Take(MAX_LOGS_PER_BATCH).ToList();
                }

                // עדכון UI
                if (newLogs != null && newLogs.Count > 0)
                {
                    // OPTIMIZATION: Sort and color in parallel
                    var sortedLogs = newLogs.OrderByDescending(l => l.Date).ToList();

                    // החלת צבעים ברקע - בלי await כדי לא לחסום
                    _ = Task.Run(async () =>
                    {
                        await _coloringService.ApplyDefaultColorsAsync(sortedLogs, false);
                        if (_caseVM.MainColoringRules != null && _caseVM.MainColoringRules.Any())
                            await _coloringService.ApplyCustomColoringAsync(sortedLogs, _caseVM.MainColoringRules);
                    });

                    // הוספה ל-UI מיידית (צבעים יתעדכנו אחרי)
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        lock (_collectionLock)
                        {
                            try
                            {
                                // OPTIMIZATION: Batch insert instead of individual adds
                                _liveLogsCollection.InsertRange(0, sortedLogs);

                                var filteredToAdd = sortedLogs.Where(l => ShouldShowInFilteredView(l)).ToList();

                                // Debug: Show sample log properties to understand GROUP A format
                                if (filteredToAdd.Count == 0 && sortedLogs.Count > 0)
                                {
                                    var samples = sortedLogs.Take(5).ToList();
                                    Debug.WriteLine($"[LIVE FILTER DEBUG] No logs passed filter. Sample logs:");
                                    foreach (var s in samples)
                                    {
                                        Debug.WriteLine($"  Level='{s.Level}' Thread='{s.ThreadName}' Logger='{s.Logger}' Msg='{s.Message?.Substring(0, Math.Min(80, s.Message?.Length ?? 0))}'");
                                    }
                                }

                                if (filteredToAdd.Count > 0)
                                {
                                    _filterVM.FilteredLogs.InsertRange(0, filteredToAdd);
                                }

                                _sessionVM.StatusMessage = $"Live: Added {newLogs.Count:N0} logs (Total: {_liveLogsCollection.Count:N0}, Filtered: {_filterVM.FilteredLogs.Count:N0})";
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LIVE UI ERROR] {ex.Message}");
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LIVE ERROR] {ex.Message}");
            }
            finally
            {
                _isRefreshActive = false;
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
