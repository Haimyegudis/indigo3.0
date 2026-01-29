using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
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
    public class LogSessionViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;
        private FilterSearchViewModel _filterVM;
        private CaseManagementViewModel _caseVM;
        private ConfigExplorerViewModel _configVM;
        private LiveMonitoringViewModel _liveVM;

        // Main data collections
        private IEnumerable<LogEntry> _logs;
        public IEnumerable<LogEntry> Logs
        {
            get => _logs;
            set
            {
                _logs = value;
                OnPropertyChanged();
                // Notify parent so UI updates
                _parent?.NotifyPropertyChanged(nameof(_parent.Logs));
            }
        }

        private IList<LogEntry> _allLogsCache;
        public IList<LogEntry> AllLogsCache
        {
            get => _allLogsCache;
            set
            {
                _allLogsCache = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.AllLogsCache));
            }
        }

        private IList<LogEntry> _allAppLogsCache;
        public IList<LogEntry> AllAppLogsCache
        {
            get => _allAppLogsCache;
            set
            {
                _allAppLogsCache = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.AllAppLogsCache));
            }
        }

        private ObservableCollection<EventEntry> _events;
        public ObservableCollection<EventEntry> Events
        {
            get => _events;
            set
            {
                _events = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.Events));
            }
        }

        // Cache for all events (before time filtering)
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

        private ObservableCollection<BitmapImage> _screenshots;
        public ObservableCollection<BitmapImage> Screenshots
        {
            get => _screenshots;
            set
            {
                _screenshots = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.Screenshots));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.LoadedFiles));
            }
        }

        private ObservableCollection<LogSessionData> _loadedSessions;
        public ObservableCollection<LogSessionData> LoadedSessions
        {
            get => _loadedSessions;
            set
            {
                _loadedSessions = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.LoadedSessions));
            }
        }

        private LogSessionData _selectedSession;
        public LogSessionData SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession != value)
                {
                    _selectedSession = value;
                    OnPropertyChanged();
                    _parent?.NotifyPropertyChanged(nameof(_parent.SelectedSession));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.CurrentProgress));
            }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.StatusMessage));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsBusy));
            }
        }

        // Commands
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }

        public LogSessionViewModel(MainViewModel parent, LogFileService logService, LogColoringService coloringService)
        {
            _parent = parent;
            _logService = logService;
            _coloringService = coloringService;

            // Initialize collections
            _allLogsCache = new List<LogEntry>();
            _logs = new List<LogEntry>();
            _events = new ObservableCollection<EventEntry>();
            _allEvents = new List<EventEntry>();
            _screenshots = new ObservableCollection<BitmapImage>();
            _loadedFiles = new ObservableCollection<string>();
            _loadedSessions = new ObservableCollection<LogSessionData>();

            // Initialize commands
            LoadCommand = new RelayCommand(LoadFile);
            ClearCommand = new RelayCommand(ClearLogs);
        }

        // Set dependent ViewModels after construction (circular dependency resolution)
        public void SetDependencies(FilterSearchViewModel filterVM, CaseManagementViewModel caseVM,
            ConfigExplorerViewModel configVM, LiveMonitoringViewModel liveVM)
        {
            _filterVM = filterVM;
            _caseVM = caseVM;
            _configVM = configVM;
            _liveVM = liveVM;
        }

        private void LoadFile(object obj)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "All Supported|*.zip;*.log|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                ProcessFiles(dialog.FileNames);
            }
        }

        public async void ProcessFiles(string[] filePaths, Action<LogSessionData> onLoadComplete = null)
        {
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
                    // Otherwise, continue to load as static file below
                }
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

                var newSession = await _logService.LoadSessionAsync(filePaths, progress);

                newSession.FileName = Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1) newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                // Parse APP logs to extract Pattern, Data, Exception fields (ASYNC for performance)
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Count > 0)
                {
                    StatusMessage = "Parsing APP logs...";
                    System.Diagnostics.Debug.WriteLine($"[LOG PARSER] Parsing {newSession.AppDevLogs.Count} APP logs...");
                    await Services.LogParserService.ParseLogEntriesAsync(newSession.AppDevLogs);
                    System.Diagnostics.Debug.WriteLine($"[LOG PARSER] Parsing complete");
                }

                StatusMessage = "Applying Colors...";
                System.Diagnostics.Debug.WriteLine($"[COLORING] Applying default colors to {newSession.Logs.Count} main logs and {newSession.AppDevLogs?.Count ?? 0} app logs...");
                await _coloringService.ApplyDefaultColorsAsync(newSession.Logs, false);
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    await _coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true);
                System.Diagnostics.Debug.WriteLine($"[COLORING] Color application complete");

                LoadedSessions.Add(newSession);
                SelectedSession = newSession;

                // Update SessionVM with ALL loaded data
                Logs = newSession.Logs;
                AllLogsCache = newSession.Logs.ToList();
                AllAppLogsCache = newSession.AppDevLogs?.ToList() ?? new List<LogEntry>();
                // Note: Parsing already done in LogFileService when logs were loaded

                // Update Events and cache
                Events.Clear();
                if (newSession.Events != null)
                {
                    // Sort events by time before storing
                    var sortedEvents = newSession.Events.OrderBy(e => e.Time).ToList();
                    AllEvents = sortedEvents; // Cache all events
                    foreach (var evt in sortedEvents)
                        Events.Add(evt);
                }
                else
                {
                    AllEvents = new List<EventEntry>();
                }

                // Update Screenshots
                Screenshots.Clear();
                if (newSession.Screenshots != null)
                    foreach (var screenshot in newSession.Screenshots)
                        Screenshots.Add(screenshot);

                // Update LoadedFiles
                LoadedFiles.Clear();
                LoadedFiles.Add(newSession.FileName);

                // Update Setup Info, Press Config, Versions through parent
                _parent.SetupInfo = newSession.SetupInfo;
                _parent.PressConfig = newSession.PressConfiguration;
                _parent.VersionsInfo = newSession.VersionsInfo;

                // Update Config files (if any)
                if (newSession.ConfigurationFiles != null && newSession.ConfigurationFiles.Any() ||
                    newSession.DatabaseFiles != null && newSession.DatabaseFiles.Any())
                {
                    _configVM.LoadConfigurationFiles();
                }

                // Update FilterVM - apply initial filters (this is the FIRST and MAIN filter application)
                // Subsequent filter calls happen only when user changes filter settings
                System.Diagnostics.Debug.WriteLine($"[FILTERING] Initial filter application starting...");
                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();
                System.Diagnostics.Debug.WriteLine($"[FILTERING] Initial filter application complete");

                CurrentProgress = 100;
                StatusMessage = "Logs Loaded. Running Analysis in Background...";
                IsBusy = false;

                _parent.StartBackgroundAnalysis(newSession);

                // Call callback after successful load
                onLoadComplete?.Invoke(newSession);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false;
            }
        }

        private void ClearLogs(object obj)
        {
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("[SESSION VM] ========== CLEAR CALLED ==========");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");

            // Clear logs
            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing AllLogsCache: {_allLogsCache?.Count ?? 0} items");
            if (_allLogsCache != null) _allLogsCache.Clear();

            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing AllAppLogsCache: {_allAppLogsCache?.Count ?? 0} items");
            if (_allAppLogsCache != null) _allAppLogsCache.Clear();

            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Setting Logs to empty list");
            Logs = new List<LogEntry>();
            OnPropertyChanged(nameof(Logs));

            // Clear events
            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing Events: {_events?.Count ?? 0} items");
            if (_events != null)
            {
                _events.Clear();
                OnPropertyChanged(nameof(Events));
            }

            // Clear screenshots
            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing Screenshots: {_screenshots?.Count ?? 0} items");
            if (_screenshots != null)
            {
                _screenshots.Clear();
                OnPropertyChanged(nameof(Screenshots));
            }

            // Clear files
            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing LoadedFiles: {_loadedFiles?.Count ?? 0} items");
            if (_loadedFiles != null)
            {
                _loadedFiles.Clear();
                OnPropertyChanged(nameof(LoadedFiles));
            }

            // Clear sessions
            System.Diagnostics.Debug.WriteLine($"[SESSION VM] Clearing LoadedSessions: {_loadedSessions?.Count ?? 0} items");
            if (_loadedSessions != null)
            {
                _loadedSessions.Clear();
                OnPropertyChanged(nameof(LoadedSessions));
            }

            System.Diagnostics.Debug.WriteLine("[SESSION VM] Resetting properties...");
            SelectedSession = null;
            CurrentProgress = 0;

            // Clear text info properties in parent
            _parent.SetupInfo = "";
            _parent.PressConfig = "";
            _parent.VersionsInfo = "";
            _parent.WindowTitle = "IndiLogs 3.0";

            // Clear FilterVM collections directly
            System.Diagnostics.Debug.WriteLine("[SESSION VM] Clearing FilterVM collections...");
            if (_filterVM != null)
            {
                // Reset tree filters first
                _filterVM.ResetTreeFilters();

                if (_filterVM.FilteredLogs != null)
                {
                    _filterVM.FilteredLogs.Clear();
                    _parent.OnPropertyChanged(nameof(_parent.FilteredLogs));
                }

                if (_filterVM.AppDevLogsFiltered != null)
                {
                    _filterVM.AppDevLogsFiltered.Clear();
                    _parent.OnPropertyChanged(nameof(_parent.AppDevLogsFiltered));
                }

                if (_filterVM.LoggerTreeRoot != null)
                {
                    _filterVM.LoggerTreeRoot.Clear();
                    _parent.OnPropertyChanged(nameof(_parent.LoggerTreeRoot));
                }
            }

            // Clear ConfigVM collections directly
            System.Diagnostics.Debug.WriteLine("[SESSION VM] Clearing ConfigVM collections...");
            if (_configVM != null)
            {
                _configVM.ClearConfigurationFiles();
                _parent.OnPropertyChanged(nameof(_parent.ConfigurationFiles));
                _parent.OnPropertyChanged(nameof(_parent.DbTreeNodes));
                _parent.OnPropertyChanged(nameof(_parent.SelectedConfigFile));
                _parent.OnPropertyChanged(nameof(_parent.ConfigFileContent));
                _parent.OnPropertyChanged(nameof(_parent.FilteredConfigContent));
                _parent.OnPropertyChanged(nameof(_parent.ConfigSearchText));
                _parent.OnPropertyChanged(nameof(_parent.IsDbFileSelected));
            }

            StatusMessage = "Logs cleared";

            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("[SESSION VM] ========== CLEAR COMPLETED ==========");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
        }

        private void SwitchToSession(LogSessionData session)
        {
            // Don't reset filters/search when loading a case file
            if (!_caseVM.IsLoadingCase)
            {
                _filterVM.IsMainFilterActive = false;
                _filterVM.IsAppFilterActive = false;
                _filterVM.IsMainFilterOutActive = false;
                _filterVM.IsAppFilterOutActive = false;
            }

            if (session == null) return;
            IsBusy = true;

            AllLogsCache = session.Logs;
            Logs = session.Logs;

            // Load configuration and database files through ConfigVM
            _configVM.LoadConfigurationFiles();
            _parent.NotifyPropertyChanged(nameof(_parent.ConfigurationFiles));
            _parent.NotifyPropertyChanged(nameof(_parent.SelectedConfigFile));

            // Update Events and Screenshots
            Events.Clear();
            foreach (var ev in session.Events) Events.Add(ev);
            _parent.NotifyPropertyChanged(nameof(_parent.Events));

            Screenshots.Clear();
            foreach (var screenshot in session.Screenshots) Screenshots.Add(screenshot);
            _parent.NotifyPropertyChanged(nameof(_parent.Screenshots));

            _parent.MarkedLogs = session.MarkedLogs;
            _parent.NotifyPropertyChanged(nameof(_parent.MarkedLogs));
            _parent.SetupInfo = session.SetupInfo;
            _parent.PressConfig = session.PressConfiguration;

            if (!string.IsNullOrEmpty(session.VersionsInfo))
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            AllAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();
            // Note: Parsing already done in LogFileService when case logs were loaded or when saving case

            _filterVM.BuildLoggerTree(AllAppLogsCache);

            // Don't reset search/filters when loading a case - will be restored by ApplyCaseSettings
            if (!_caseVM.IsLoadingCase)
            {
                _parent.SearchText = "";
                _filterVM.IsTimeFocusActive = false;
                _filterVM.IsAppTimeFocusActive = false;

                _filterVM.NegativeFilters.Clear();
                _filterVM.ActivePlcThreadFilters.Clear();
                _filterVM.ActiveAppThreadFilters.Clear();

                _filterVM.MainFilterRoot = null;
                _filterVM.AppFilterRoot = null;
                _filterVM.LastFilteredAppCache = null;
                _filterVM.LastFilteredCache = null;

                _parent.ResetTreeFilters();

                // Apply default PLC filter to FilteredLogs - must be AFTER clearing filters
                System.Diagnostics.Debug.WriteLine($"[SWITCH SESSION] session.Logs count: {session.Logs?.Count ?? 0}");
                var defaultFilteredLogs = session.Logs.Where(l => _filterVM.IsDefaultLog(l)).ToList();
                System.Diagnostics.Debug.WriteLine($"[SWITCH SESSION] defaultFilteredLogs count after IsDefaultLog filter: {defaultFilteredLogs.Count}");

                // Debug: Show sample logs to understand why filter might not match
                if (defaultFilteredLogs.Count == 0 && session.Logs.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SWITCH SESSION] WARNING: No logs passed default filter! Sample logs:");
                    foreach (var sample in session.Logs.Take(5))
                    {
                        System.Diagnostics.Debug.WriteLine($"  Level='{sample.Level}' Thread='{sample.ThreadName}' Logger='{sample.Logger}' Msg='{sample.Message?.Substring(0, Math.Min(80, sample.Message?.Length ?? 0))}'");
                    }
                }

                if (_filterVM?.FilteredLogs != null)
                {
                    _filterVM.FilteredLogs.ReplaceAll(defaultFilteredLogs);
                    _parent.NotifyPropertyChanged(nameof(_parent.FilteredLogs)); // Explicitly notify UI
                    System.Diagnostics.Debug.WriteLine($"[SWITCH SESSION] FilteredLogs updated, count: {_filterVM.FilteredLogs.Count}");
                }
                if (_parent.FilteredLogs != null && _parent.FilteredLogs.Count > 0)
                    _parent.SelectedLog = _parent.FilteredLogs[0];

                // Mark filter as inactive since this is just default filtering, not user-applied filter
                _filterVM.IsMainFilterActive = false;
                _filterVM.IsAppFilterActive = false;
            }

            _filterVM.ApplyAppLogsFilter();
            IsBusy = false;
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}