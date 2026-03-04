using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages log data sessions - loading files, storing logs, switching between sessions
    /// </summary>
    public class LogSessionViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private FilterSearchViewModel? _filterVM;
        private CaseManagementViewModel? _caseVM;
        private ConfigExplorerViewModel? _configVM;
        private LiveMonitoringViewModel? _liveVM;

        /// <summary>
        /// Current PLC/main log entries bound to the UI DataGrid.
        /// </summary>
        private IEnumerable<LogEntry> _logs;
        public IEnumerable<LogEntry> Logs
        {
            get => _logs;
            set
            {
                _logs = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Complete unfiltered cache of all PLC/main log entries for the current session.
        /// </summary>
        private IList<LogEntry> _allLogsCache;
        public IList<LogEntry> AllLogsCache
        {
            get => _allLogsCache;
            set
            {
                _allLogsCache = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Complete unfiltered cache of all APP developer log entries for the current session.
        /// </summary>
        private IList<LogEntry> _allAppLogsCache;
        public IList<LogEntry> AllAppLogsCache
        {
            get => _allAppLogsCache;
            set
            {
                _allAppLogsCache = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// PLC event entries extracted from the loaded session.
        /// </summary>
        private ObservableCollection<EventEntry> _events;
        public ObservableCollection<EventEntry> Events
        {
            get => _events;
            set
            {
                _events = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Complete cache of all event entries before any time filtering is applied.
        /// </summary>
        private List<EventEntry> _allEvents;
        public List<EventEntry> AllEvents
        {
            get => _allEvents;
            set
            {
                _allEvents = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Screenshots extracted from the loaded ZIP session file.
        /// </summary>
        private ObservableCollection<BitmapImage> _screenshots;
        public ObservableCollection<BitmapImage> Screenshots
        {
            get => _screenshots;
            set
            {
                _screenshots = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<string> _loadedFiles;
        public ObservableCollection<string> LoadedFiles
        {
            get => _loadedFiles;
            set
            {
                _loadedFiles = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// All loaded log sessions available for switching between.
        /// </summary>
        private ObservableCollection<LogSessionData> _loadedSessions;
        public ObservableCollection<LogSessionData> LoadedSessions
        {
            get => _loadedSessions;
            set
            {
                _loadedSessions = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Currently active log session; setting triggers session switch and data reload.
        /// </summary>
        private LogSessionData? _selectedSession;
        public LogSessionData? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession != value)
                {
                    // Save filter state of the outgoing session before switching
                    SaveFilterState(_selectedSession);

                    _selectedSession = value;
                    OnPropertyChanged();
                    _parent?.NotifyPropertyChanged(nameof(_parent.HasSessionLoaded));
                    SwitchToSession(_selectedSession);
                }
            }
        }

        // Progress tracking
        private double _currentProgress;
        public double CurrentProgress
        {
            get => _currentProgress;
            set
            {
                _currentProgress = value;
                OnPropertyChanged();
            }
        }

        private string? _statusMessage;
        public string? StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        // Commands
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand RemoveSessionCommand { get; }

        public LogSessionViewModel(MainViewModel parent, ILogFileService logService, ILogColoringService coloringService, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher)
        {
            _parent = parent;
            _logService = logService;
            _coloringService = coloringService;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;

            // Initialize collections
            _allLogsCache = new List<LogEntry>();
            _logs = new List<LogEntry>();
            _events = new ObservableCollection<EventEntry>();
            _allEvents = new List<EventEntry>();
            _screenshots = new ObservableCollection<BitmapImage>();
            _loadedFiles = new ObservableCollection<string>();
            _loadedSessions = new ObservableCollection<LogSessionData>();

            // Initialize commands
            LoadCommand = new RelayCommand(o => _ = LoadFile(o));
            ClearCommand = new RelayCommand(ClearLogs);
            RemoveSessionCommand = new RelayCommand(RemoveSession);
        }

        /// <summary>
        /// Injects dependent ViewModels after construction to resolve circular dependencies.
        /// </summary>
        public void SetDependencies(FilterSearchViewModel filterVM, CaseManagementViewModel caseVM,
            ConfigExplorerViewModel configVM, LiveMonitoringViewModel liveVM)
        {
            _filterVM = filterVM;
            _caseVM = caseVM;
            _configVM = configVM;
            _liveVM = liveVM;
        }

        /// <summary>
        /// Builds the OpenFileDialog Filter string dynamically, adding an entry for
        /// every file extension declared by loaded plugins.
        /// </summary>
        private string BuildFileDialogFilter()
        {
            // Collect unique globs from all loaded plugins (e.g. "*.csvlog", "*.devicelog")
            var pluginExts = new List<string>();
            try
            {
                var loader = _parent.GetPluginLoader();
                if (loader != null)
                {
                    foreach (var p in loader.Plugins)
                    {
                        var exts = p.SupportedExtensions;
                        if (exts != null) pluginExts.AddRange(exts);
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("BuildFileDialogFilter plugin extensions failed", ex); }

            // Remove extensions already covered by standard built-in filter groups
            // so they don't create a redundant "Plugin Files" entry for common types
            var standardExts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "*.log", "*.txt", "*.err", "*.error", "*.out",
                "*.json", "*.jsonl", "*.csv", "*.xml", "*.zip"
            };
            pluginExts = pluginExts
                .Distinct()
                .Where(e => !standardExts.Contains(e))
                .ToList();

            string pluginGlobAll = pluginExts.Count > 0 ? string.Join(";", pluginExts) : "";

            // Base set includes all common log types + plugin-specific extensions
            const string baseGlobs = "*.zip;*.log;*.txt;*.json;*.jsonl;*.csv;*.xml;*.err;*.error;*.out";
            string allSupportedGlobs = baseGlobs + (pluginGlobAll.Length > 0 ? ";" + pluginGlobAll : "");

            var filter = new System.Text.StringBuilder();
            filter.Append($"All Supported Files ({allSupportedGlobs})|{allSupportedGlobs}");
            filter.Append("|Text Log Files (*.log;*.txt;*.err;*.error;*.out)|*.log;*.txt;*.err;*.error;*.out");
            filter.Append("|JSON Logs (*.json;*.jsonl)|*.json;*.jsonl");
            filter.Append("|CSV Files (*.csv)|*.csv");
            filter.Append("|XML / log4net (*.xml)|*.xml");
            filter.Append("|Log Archives (*.zip)|*.zip");
            if (pluginGlobAll.Length > 0)
                filter.Append($"|Plugin Files ({pluginGlobAll})|{pluginGlobAll}");
            filter.Append("|All files (*.*)|*.*");

            return filter.ToString();
        }

        private async Task LoadFile(object obj)
        {
            try
            {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = BuildFileDialogFilter()
            };

            if (dialog.ShowDialog() == true)
            {
                // Route CSV files to CPR tab instead of log processing
                var csvFiles = dialog.FileNames.Where(f => Path.GetExtension(f).ToLower() == ".csv").ToArray();
                var logFiles = dialog.FileNames.Where(f => Path.GetExtension(f).ToLower() != ".csv").ToArray();

                if (csvFiles.Length > 0)
                {
                    _parent.SelectedTabIndex = 10; // CPR tab
                    _parent.CprVM?.LoadFileDirect(csvFiles[0]);
                }

                if (logFiles.Length > 0)
                {
                    // For single non-session files, try routing to Different Logs tab
                    if (logFiles.Length == 1 && _parent.DifferentLogsVM != null)
                    {
                        var ext = Path.GetExtension(logFiles[0]).ToLower();
                        bool isKnownSessionExt = ext == ".zip" || ext == ".log" || ext == ".file";
                        if (!isKnownSessionExt)
                        {
                            bool handled = await _parent.DifferentLogsVM.LoadFileAsync(logFiles[0]);
                            if (handled)
                            {
                                _parent.SelectedTabIndex = 12; // DIFFERENT LOGS tab
                                return;
                            }
                        }
                    }
                    ProcessFiles(logFiles);
                }
            }
            }
            catch (Exception ex) { AppLogger.Error("LoadFile failed", ex); }
        }

        /// <summary>
        /// Loads and processes log files into a new session, applying colors and updating the UI.
        /// </summary>
        public async Task ProcessFiles(string[] filePaths, Action<LogSessionData> onLoadComplete = null)
        {
            bool isWatchableFile = false; // Track if we should start auto-refresh after loading

            // Check if this is a live log file (active file being written to)
            if (filePaths.Length == 1 && File.Exists(filePaths[0]))
            {
                var filePath = filePaths[0];
                var fileName = Path.GetFileName(filePath);
                var ext = Path.GetExtension(filePath).ToLower();

                // Detect live log files: .log or .file extension, or specific patterns like "no-sn.engineGroupA.file"
                if (ext == ".log" || ext == ".file" ||
                    fileName.IndexOf("engineGroup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fileName.IndexOf("no-sn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Check if file is actively being written by another process
                    // Try to open with exclusive access - if it fails, the file is locked (live)
                    bool isLiveFile = false;
                    try
                    {
                        // Try to open with exclusive write access
                        // If another process is writing to it, this will fail
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we CAN open exclusively, file is NOT being written to - load as static
                            isLiveFile = false;
                        }
                    }
                    catch (IOException)
                    {
                        // File is locked by another process - it's a live file
                        isLiveFile = true;
                    }
                    catch
                    {
                        // Other errors (permissions etc.) - treat as static file
                        isLiveFile = false;
                    }

                    if (isLiveFile)
                    {
                        _liveVM.StartLiveMonitoring(filePath);
                        return;
                    }
                    // Otherwise, continue to load as static file below — but mark for auto-refresh
                    isWatchableFile = true;
                }
            }

            // --- Tab Selection Dialog: show for ZIP files before parsing ---
            TabSelectionConfig tabSelection = null;
            TabSelectionConfig preScan = null;
            bool hasZipFile = filePaths.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (hasZipFile)
            {
                string zipPath = filePaths.First(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                preScan = await Task.Run(() => _logService.PreScanZip(zipPath));

                // Show the dialog on the UI thread
                var dialog = _viewFactory.Create<Views.TabSelectionWindow>(preScan);
                dialog.Owner = Application.Current.MainWindow;
                if (dialog.ShowDialog() != true)
                {
                    // User cancelled — abort load
                    return;
                }
                tabSelection = dialog.ResultConfig;

                // Propagate tab visibility to parent VM
                _parent.ApplyTabSelection(tabSelection, preScan);
            }
            else
            {
                // Non-ZIP load: reset all tabs to visible
                _parent.ApplyTabSelection(null, null);
            }

            IsBusy = true;
            StatusMessage = "Processing files...";

            try
            {
                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                var newSession = await _logService.LoadSessionAsync(filePaths, progress, tabSelection);

                newSession.FileName = Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1) newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                // Store tab selection configs for add-back feature
                if (hasZipFile && tabSelection != null)
                {
                    newSession.LoadTabSelection = tabSelection;
                    newSession.PreScanConfig = preScan;
                }

                // Run APP log parsing and color application in parallel for maximum speed
                StatusMessage = "Processing logs...";
                var postTasks = new List<Task>();

                // Parse APP logs (extracts Pattern, Data, Exception fields)
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Count > 0)
                {
                    postTasks.Add(Services.LogParserService.ParseLogEntriesAsync(newSession.AppDevLogs));
                }

                // Apply colors to main logs
                postTasks.Add(_coloringService.ApplyDefaultColorsAsync(newSession.Logs, false));
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    postTasks.Add(_coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true));

                await Task.WhenAll(postTasks);

                LoadedSessions.Add(newSession);

                // Bypass the SelectedSession setter to avoid SwitchToSession —
                // ProcessFiles already does all the setup work itself.
                // SwitchToSession is only needed when the user switches between
                // already-loaded sessions via the combo box.
                SaveFilterState(_selectedSession);
                _selectedSession = newSession;
                OnPropertyChanged(nameof(SelectedSession));
                _parent?.NotifyPropertyChanged(nameof(_parent.HasSessionLoaded));

                // Update SessionVM with ALL loaded data
                // Share reference instead of copying to avoid doubling memory usage
                Logs = newSession.Logs;
                AllLogsCache = newSession.Logs;
                AllAppLogsCache = newSession.AppDevLogs ?? new List<LogEntry>();

                // Auto-calculate time sync offset between PLC and APP logs.
                // APP is the authoritative baseline (synced to PC/BIOS clock).
                //
                // Matching rule:
                //   APP:  Logger == PrcSymptomMapperManager  AND  Message starts with "--> eventID: <X>"
                //   PLC:  ThreadName == "Events"             AND  Message starts with "Send event <X>" or "Enqueue event <X>"
                //
                // Search order: iterate APP logs earliest-first; for each candidate extract <X> and
                // find the PLC entry with the same event name whose raw timestamp is CLOSEST to the
                // APP log AND within ±2 minutes.  Using "closest" instead of "first" prevents
                // accidentally syncing to an earlier repeated occurrence of the same event name.
                // The very first valid pair becomes the anchor; offset = APP.Date - PLC.Date.
                _parent.HasTimeSyncData      = false;
                _parent.ShowSyncedTimeColumn = false;
                try
                {
                    if (newSession.Logs.Count > 0 && newSession.AppDevLogs != null && newSession.AppDevLogs.Count > 0)
                    {
                        const string appLogger   = "Press.BL.PrintCare.Symptoms.PrcSymptomMapperManager";
                        const string appPrefix   = "--> eventID: ";
                        const string plcThread   = "Events";
                        const double maxDiffMin  = 2.0;

                        TimeSpan? syncOffset = null;

                        // Pre-filter PLC Events logs once (already sorted by date from loading)
                        var plcEventLogs = newSession.Logs
                            .Where(l => string.Equals(l.ThreadName, plcThread, StringComparison.OrdinalIgnoreCase)
                                        && l.Message != null)
                            .ToList();

                        // Pre-extract PLC dates array for binary search — O(N) once
                        var plcDates = new DateTime[plcEventLogs.Count];
                        for (int pi = 0; pi < plcEventLogs.Count; pi++)
                            plcDates[pi] = plcEventLogs[pi].Date;

                        // Iterate APP logs in chronological order (earliest = most reliable anchor)
                        foreach (var appLog in newSession.AppDevLogs)
                        {
                            if (!string.Equals(appLog.Logger, appLogger, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (appLog.Message == null || !appLog.Message.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var afterPrefix = appLog.Message.Substring(appPrefix.Length).TrimStart();
                            var commaIdx    = afterPrefix.IndexOf(',');
                            var eventId     = (commaIdx >= 0 ? afterPrefix.Substring(0, commaIdx) : afterPrefix).Trim();
                            if (string.IsNullOrEmpty(eventId)) continue;

                            if (string.Equals(eventId, "PLC_FAILURE_STATE_CHANGE", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var sendPrefix    = "Send event "    + eventId;
                            var enqueuePrefix = "Enqueue event " + eventId;

                            // Binary search to find the nearest PLC log by time — O(log N) instead of O(N)
                            int idx = Array.BinarySearch(plcDates, appLog.Date);
                            if (idx < 0) idx = ~idx; // insertion point

                            LogEntry bestEnqueueLog = null;
                            LogEntry bestSendLog    = null;
                            double   bestEnqueueDiff = double.MaxValue;
                            double   bestSendDiff    = double.MaxValue;

                            // Scan outward from binary search point (only nearby entries within maxDiffMin)
                            for (int dir = -1; dir <= 1; dir += 2) // -1 = left, +1 = right
                            {
                                int start = dir < 0 ? idx - 1 : idx;
                                for (int j = start; j >= 0 && j < plcEventLogs.Count; j += dir)
                                {
                                    var diffMin = Math.Abs((appLog.Date - plcDates[j]).TotalMinutes);
                                    if (diffMin > maxDiffMin) break; // sorted → no closer matches further out

                                    var plcLog = plcEventLogs[j];
                                    if (plcLog.Message.StartsWith(enqueuePrefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (diffMin < bestEnqueueDiff) { bestEnqueueDiff = diffMin; bestEnqueueLog = plcLog; }
                                    }
                                    else if (plcLog.Message.StartsWith(sendPrefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (diffMin < bestSendDiff) { bestSendDiff = diffMin; bestSendLog = plcLog; }
                                    }
                                }
                            }

                            var bestPlcLog = bestEnqueueLog ?? bestSendLog;

                            if (bestPlcLog != null)
                            {
                                syncOffset = appLog.Date - bestPlcLog.Date;
                                break; // First valid APP anchor wins
                            }
                        }

                        if (syncOffset.HasValue && Math.Abs(syncOffset.Value.TotalSeconds) > 1)
                        {
                            _parent.TimeSyncOffsetSeconds = syncOffset.Value.TotalSeconds;

                            // Apply offset to every PLC log: SyncedTime == PLC raw time shifted to APP clock
                            foreach (var log in newSession.Logs)
                                log.SyncedTime = log.Date.Add(syncOffset.Value);

                            _parent.HasTimeSyncData      = true;
                            _parent.ShowSyncedTimeColumn = true;
                            StatusMessage = $"⏱ Time sync: offset {syncOffset.Value.TotalMilliseconds:F0}ms ({syncOffset.Value.TotalSeconds:F1}s)";
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Time-sync calculation failed", ex);
                }

                // Update Events and cache (already sorted by LogFileService)
                if (newSession.Events != null && newSession.Events.Count > 0)
                {
                    AllEvents = newSession.Events; // Already sorted, share reference
                    // Single collection replacement — fires one PropertyChanged instead of N CollectionChanged
                    Events = new ObservableCollection<EventEntry>(newSession.Events);
                }
                else
                {
                    AllEvents = new List<EventEntry>();
                    Events = new ObservableCollection<EventEntry>();
                }

                _parent.LoadEventsDataView();

                // Update Screenshots — single collection replacement
                Screenshots = new ObservableCollection<BitmapImage>(newSession.Screenshots ?? new List<BitmapImage>());

                // Update LoadedFiles
                LoadedFiles.Clear();
                LoadedFiles.Add(newSession.FileName);

                // Update Setup Info, Press Config, Versions through parent
                _parent.SetupInfo = newSession.SetupInfo;
                _parent.PressConfig = newSession.PressConfiguration;
                _parent.VersionsInfo = newSession.VersionsInfo;

                // Update Config files (if any) or terminal logs
                if (newSession.ConfigurationFiles != null && newSession.ConfigurationFiles.Any() ||
                    newSession.DatabaseFiles != null && newSession.DatabaseFiles.Any() ||
                    newSession.TerminalLogFiles != null && newSession.TerminalLogFiles.Any() ||
                    newSession.TerminalCsvBytes != null && newSession.TerminalCsvBytes.Any())
                {
                    _configVM.LoadConfigurationFiles();
                }
                _parent.CurrentPluginColumns = newSession.PluginColumns;
                _parent.NotifyPropertyChanged(nameof(_parent.DbConfigTabHeader));
                _parent.NotifyPropertyChanged(nameof(_parent.HasBinaryAppLogs));
                _parent.LoadGlobalsFiles();
                _parent.LoadSystabFiles();
                _parent.UpdateTabVisibilityAfterLoad();
                _parent.NotifyPropertyChanged(nameof(_parent.HasSkippedComponents));

                // Window title
                if (!string.IsNullOrEmpty(newSession.VersionsInfo))
                    _parent.WindowTitle = $"IndiLogs 3.0 - {newSession.FileName} ({newSession.VersionsInfo})";
                else
                    _parent.WindowTitle = $"IndiLogs 3.0 - {newSession.FileName}";

                // Build logger tree panels (must happen before filter application)
                _filterVM.BuildLoggerTree(AllAppLogsCache);
                _filterVM.BuildPlcLoggerTree(AllLogsCache);

                // Load saved configs for the dropdown
                _caseVM.LoadSavedConfigs(newSession.HasBinaryAppLogs);

                // Restore chart state (null for new sessions = clear chart)
                _parent.ChartVM?.RestoreChartState(newSession.SavedChartState);

                // Apply initial filters (this is the FIRST and ONLY filter application)
                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();

                // Scroll all tabs to last row after loading
                // Use ApplicationIdle priority to ensure DataGrid virtualization is fully rendered
                // Use ScrollTabToBottom (not ScrollToLog) to directly target each grid,
                // because FindGridForLog always matches PLC first for shared log objects
                _dispatcher.Post(() =>
                    {
                        _parent.ScrollTabToBottom("PLC");
                        _parent.ScrollTabToBottom("FILTERED");
                        _parent.ScrollTabToBottom("APP");
                    }, DispatchPriority.ApplicationIdle);

                CurrentProgress = 100;
                StatusMessage = $"Logs Loaded ({newSession.Logs.Count:N0} PLC logs). Running Analysis in Background...";
                IsBusy = false;

                // Select PLC tab after loading
                _parent.SelectedTabIndex = 0;

                _parent.StartBackgroundAnalysis(newSession);

                // Call callback after successful load
                onLoadComplete?.Invoke(newSession);

                // Start auto-refresh for .file files so new logs are picked up automatically
                if (isWatchableFile && filePaths.Length == 1 && newSession.Logs.Count > 0)
                {
                    _liveVM.StartFileWatcher(filePaths[0], newSession);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _dialogService.ShowError($"Error loading files: {ex.Message}", "Error");
                IsBusy = false;
            }
        }

        /// <summary>
        /// Reloads a single previously-skipped component from the ZIP and merges it into the current session.
        /// </summary>
        public async Task AddBackComponentAsync(string componentName)
        {
            var session = SelectedSession;
            if (session == null || string.IsNullOrEmpty(session.FilePath)) return;

            IsBusy = true;
            StatusMessage = $"Adding {componentName}...";

            try
            {
                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                await _logService.ReloadComponentAsync(session, componentName, progress);

                // Post-processing: apply colors to newly added logs
                var postTasks = new List<Task>();
                if (componentName == "App" && session.AppDevLogs != null && session.AppDevLogs.Count > 0)
                {
                    postTasks.Add(Services.LogParserService.ParseLogEntriesAsync(session.AppDevLogs));
                    postTasks.Add(_coloringService.ApplyDefaultColorsAsync(session.AppDevLogs, true));
                }
                if (componentName == "Plc" || componentName == "ManagerThread")
                {
                    postTasks.Add(_coloringService.ApplyDefaultColorsAsync(session.Logs, false));
                }
                if (postTasks.Count > 0) await Task.WhenAll(postTasks);

                // Refresh UI bindings
                Logs = session.Logs;
                AllLogsCache = session.Logs;
                AllAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();

                if (componentName == "Events")
                {
                    AllEvents = session.Events;
                    Events = new ObservableCollection<EventEntry>(session.Events);
                    _parent.LoadEventsDataView();
                }

                if (componentName == "Screenshots")
                {
                    Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots ?? new List<BitmapImage>());
                }

                if (componentName == "Configuration" || componentName == "TerminalLogs" || componentName == "Lrs")
                {
                    _configVM.LoadConfigurationFiles();
                    _parent.NotifyPropertyChanged(nameof(_parent.DbConfigTabHeader));
                }

                if (componentName == "Globals")
                {
                    _parent.LoadGlobalsFiles();
                }

                if (componentName == "Systab")
                {
                    _parent.LoadSystabFiles();
                }

                if (componentName == "SetupInfo")
                {
                    _parent.SetupInfo = session.SetupInfo;
                    _parent.PressConfig = session.PressConfiguration;
                }

                // Update tab visibility for the added component
                var sel = session.LoadTabSelection;
                var preScan = session.PreScanConfig;
                switch (componentName)
                {
                    case "App": _parent.ShowAppTab = true; break;
                    case "Plc": _parent.ShowPlcTab = true; break;
                    case "Events": _parent.ShowEventsTab = true; break;
                    case "Screenshots": _parent.ShowScreenshotsTab = true; break;
                    case "TerminalLogs":
                    case "Configuration":
                    case "Lrs":
                        _parent.ShowDbConfigTab = true;
                        if (componentName == "Configuration") _parent.ShowConfigTab = true;
                        break;
                    case "Systab": _parent.ShowSystabTab = true; break;
                    case "Globals": _parent.ShowGlobalsTab = true; break;
                    case "SetupInfo": _parent.ShowSetupInfoTab = true; break;

                    // Tool tabs — no data reload needed, just toggle visibility
                    case "Charts":
                        if (sel != null) sel.ShowCharts = true;
                        _parent.ApplyTabSelection(sel, preScan);
                        break;
                    case "CPR":
                        if (sel != null) sel.ShowCpr = true;
                        _parent.ApplyTabSelection(sel, preScan);
                        break;
                    case "Step Recorder":
                        if (sel != null) sel.ShowStepRecorder = true;
                        _parent.ApplyTabSelection(sel, preScan);
                        break;
                    case "Different Logs":
                        if (sel != null) sel.ShowDifferentLogs = true;
                        _parent.ApplyTabSelection(sel, preScan);
                        break;
                }

                _parent.UpdateTabVisibilityAfterLoad();
                _parent.NotifyPropertyChanged(nameof(_parent.HasSkippedComponents));

                // Rebuild logger trees if log data changed
                if (componentName == "Plc" || componentName == "ManagerThread")
                    _filterVM.BuildPlcLoggerTree(AllLogsCache);
                if (componentName == "App")
                    _filterVM.BuildLoggerTree(AllAppLogsCache);

                // Re-apply filters
                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();

                StatusMessage = $"{componentName} loaded successfully.";
                CurrentProgress = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading {componentName}: {ex.Message}";
                AppLogger.Error($"AddBackComponentAsync({componentName}) failed", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ClearLogs(object obj)
        {
            // Clear logs
            if (_allLogsCache != null) _allLogsCache.Clear();
            if (_allAppLogsCache != null) _allAppLogsCache.Clear();

            Logs = new List<LogEntry>();
            OnPropertyChanged(nameof(Logs));

            // Clear events
            if (_events != null)
            {
                _events.Clear();
                OnPropertyChanged(nameof(Events));
            }

            // Clear screenshots
            if (_screenshots != null)
            {
                _screenshots.Clear();
                OnPropertyChanged(nameof(Screenshots));
            }

            // Clear files
            if (_loadedFiles != null)
            {
                _loadedFiles.Clear();
                OnPropertyChanged(nameof(LoadedFiles));
            }

            // Clear sessions
            if (_loadedSessions != null)
            {
                _loadedSessions.Clear();
                OnPropertyChanged(nameof(LoadedSessions));
            }

            SelectedSession = null;
            CurrentProgress = 0;

            // Clear chart state
            _parent.ChartVM?.RestoreChartState(null);

            // Clear text info properties in parent
            _parent.SetupInfo = "";
            _parent.PressConfig = "";
            _parent.VersionsInfo = "";
            _parent.WindowTitle = "IndiLogs 3.0";

            // Clear FilterVM collections directly
            if (_filterVM != null)
            {
                // Clear ALL filters (thread, logger, method, time focus, search, negative, etc.)
                _filterVM.ClearFilters();

                if (_filterVM.FilteredLogs != null)
                {
                    _filterVM.FilteredLogs.Clear();
                }

                if (_filterVM.AppDevLogsFiltered != null)
                {
                    _filterVM.AppDevLogsFiltered.Clear();
                }

                if (_filterVM.LoggerTreeRoot != null)
                {
                    _filterVM.LoggerTreeRoot.Clear();
                }
                if (_filterVM.PlcLoggerTreeRoot != null)
                {
                    _filterVM.PlcLoggerTreeRoot.Clear();
                    _parent.NotifyPropertyChanged(nameof(_parent.ActiveLoggerTree));
                }

                // Notify ACTIVE FILTERS panel to update
                _parent.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
                _parent.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            }

            // Clear ConfigVM collections directly
            if (_configVM != null)
            {
                _configVM.ClearConfigurationFiles();
            }

            StatusMessage = "Logs cleared";
        }

        private void RemoveSession(object obj)
        {
            if (obj is LogSessionData session && _loadedSessions != null && _loadedSessions.Contains(session))
            {
                bool wasSelected = (session == _selectedSession);
                _loadedSessions.Remove(session);
                OnPropertyChanged(nameof(LoadedSessions));

                if (wasSelected)
                {
                    // If removed session was selected, switch to the first remaining session or clear
                    if (_loadedSessions.Count > 0)
                    {
                        SelectedSession = _loadedSessions[0];
                    }
                    else
                    {
                        SelectedSession = null;
                        // Clear everything since no sessions remain
                        ClearLogs(null);
                    }
                }
            }
        }

        private void SwitchToSession(LogSessionData session)
        {
            if (session == null) return;
            IsBusy = true;

            AllLogsCache = session.Logs;
            Logs = session.Logs;
            _parent.CurrentPluginColumns = session.PluginColumns;

            // Load configuration and database files through ConfigVM
            _configVM.LoadConfigurationFiles();
            _parent.NotifyPropertyChanged(nameof(_parent.DbConfigTabHeader));
            _parent.NotifyPropertyChanged(nameof(_parent.HasBinaryAppLogs));
            _parent.LoadGlobalsFiles();

            // Restore tab visibility from the session's saved selection
            _parent.ApplyTabSelection(session.LoadTabSelection, session.PreScanConfig);
            _parent.UpdateTabVisibilityAfterLoad();

            // Update Events and Screenshots — single collection replacement instead of per-item Add
            Events = new ObservableCollection<EventEntry>(session.Events);
            _parent.LoadEventsDataView();

            Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots);

            _caseVM.MarkedLogs = session.MarkedLogs;
            _parent.SetupInfo = session.SetupInfo;
            _parent.PressConfig = session.PressConfiguration;

            if (!string.IsNullOrEmpty(session.VersionsInfo))
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            AllAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();
            // Note: Parsing already done in LogFileService when case logs were loaded or when saving case

            _filterVM.BuildLoggerTree(AllAppLogsCache);
            _filterVM.BuildPlcLoggerTree(AllLogsCache);

            // Don't reset search/filters when loading a case - will be restored by ApplyCaseSettings
            if (!_caseVM.IsLoadingCase)
            {
                // Reload saved configs filtered by session type (show only the matching default)
                _caseVM.LoadSavedConfigs(session.HasBinaryAppLogs);

                // Try to restore previously saved filter state for this session
                if (!RestoreFilterState(session))
                {
                    // First time opening this session — start with no filters
                    _filterVM.SearchText = "";
                    _filterVM.IsTimeFocusActive = false;
                    _filterVM.IsAppTimeFocusActive = false;

                    _filterVM.NegativeFilters.Clear();
                    _filterVM.ActiveThreadFilters.Clear();

                    _filterVM.MainFilterRoot = null;
                    _filterVM.AppFilterRoot = null;
                    _filterVM.LastFilteredAppCache = null;
                    _filterVM.LastFilteredCache = null;

                    _parent.ResetTreeFilters();

                    _filterVM.IsMainFilterActive = false;
                    _filterVM.IsAppFilterActive = false;
                    _filterVM.IsMainFilterOutActive = false;
                    _filterVM.IsAppFilterOutActive = false;

                    _filterVM.ApplyAppLogsFilter();
                }
            }
            else
            {
                _filterVM.ApplyAppLogsFilter();
            }

            // Restore chart state for this session (or clear if no saved state)
            _parent.ChartVM?.RestoreChartState(session.SavedChartState);

            IsBusy = false;
        }

        /// <summary>
        /// Saves the current filter state into the session so it persists across switches.
        /// </summary>
        private void SaveFilterState(LogSessionData session)
        {
            if (session == null) return;

            session.SavedFilterState = new SessionFilterState
            {
                MainFilterRoot = _filterVM.MainFilterRoot?.DeepClone(),
                AppFilterRoot = _filterVM.AppFilterRoot?.DeepClone(),
                IsMainFilterActive = _filterVM.IsMainFilterActive,
                IsAppFilterActive = _filterVM.IsAppFilterActive,
                IsMainFilterOutActive = _filterVM.IsMainFilterOutActive,
                IsAppFilterOutActive = _filterVM.IsAppFilterOutActive,
                IsTimeFocusActive = _filterVM.IsTimeFocusActive,
                IsAppTimeFocusActive = _filterVM.IsAppTimeFocusActive,
                NegativeFilters = _filterVM.NegativeFilters.ToList(),
                ActiveThreadFilters = _filterVM.ActiveThreadFilters.ToList(),
                SearchText = _filterVM.SearchText,
                LastFilteredCache = _filterVM.LastFilteredCache,
                LastFilteredAppCache = _filterVM.LastFilteredAppCache
            };

            // Save chart state for this session
            session.SavedChartState = _parent.ChartVM?.SaveChartState();
        }

        /// <summary>
        /// Restores a previously saved filter state for the session.
        /// Returns true if state was restored, false if no saved state exists.
        /// </summary>
        private bool RestoreFilterState(LogSessionData session)
        {
            var state = session?.SavedFilterState;
            if (state == null) return false;

            _filterVM.SearchText = state.SearchText ?? "";
            _filterVM.IsTimeFocusActive = state.IsTimeFocusActive;
            _filterVM.IsAppTimeFocusActive = state.IsAppTimeFocusActive;

            _filterVM.NegativeFilters.Clear();
            foreach (var nf in state.NegativeFilters) _filterVM.NegativeFilters.Add(nf);

            _filterVM.ActiveThreadFilters.Clear();
            foreach (var tf in state.ActiveThreadFilters) _filterVM.ActiveThreadFilters.Add(tf);

            _filterVM.MainFilterRoot = state.MainFilterRoot?.DeepClone();
            _filterVM.AppFilterRoot = state.AppFilterRoot?.DeepClone();
            _filterVM.LastFilteredCache = state.LastFilteredCache;
            _filterVM.LastFilteredAppCache = state.LastFilteredAppCache;

            _filterVM.IsMainFilterActive = state.IsMainFilterActive;
            _filterVM.IsAppFilterActive = state.IsAppFilterActive;
            _filterVM.IsMainFilterOutActive = state.IsMainFilterOutActive;
            _filterVM.IsAppFilterOutActive = state.IsAppFilterOutActive;

            _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
            _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));

            _filterVM.ApplyMainLogsFilter();
            _filterVM.ApplyAppLogsFilter();

            return true;
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}