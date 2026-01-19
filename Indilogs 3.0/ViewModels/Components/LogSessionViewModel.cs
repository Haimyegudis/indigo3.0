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
                    // Check if file is actively being written (might grow)
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // If we can open with ReadWrite sharing, it's likely a live file
                            // Start live monitoring instead of loading as static file
                            _liveVM.StartLiveMonitoring(filePath);
                            return;
                        }
                    }
                    catch
                    {
                        // If file is locked or not accessible, treat as static file
                    }
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

                StatusMessage = "Applying Colors...";
                await _coloringService.ApplyDefaultColorsAsync(newSession.Logs, false);
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    await _coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true);

                LoadedSessions.Add(newSession);
                SelectedSession = newSession;

                // Update SessionVM with ALL loaded data
                Logs = newSession.Logs;
                AllLogsCache = newSession.Logs.ToList();
                AllAppLogsCache = newSession.AppDevLogs?.ToList() ?? new List<LogEntry>();

                // Update Events
                Events.Clear();
                if (newSession.Events != null)
                    foreach (var evt in newSession.Events)
                        Events.Add(evt);

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

                // Update FilterVM - copy data to filtered collections
                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();

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
            _allLogsCache?.Clear();
            _allAppLogsCache?.Clear();
            Logs = new List<LogEntry>();
            Events.Clear();
            Screenshots.Clear();
            LoadedFiles.Clear();
            LoadedSessions.Clear();
            SelectedSession = null;

            StatusMessage = "Logs cleared";
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
            _filterVM.BuildLoggerTree(AllAppLogsCache);

            // Don't reset search/filters when loading a case - will be restored by ApplyCaseSettings
            if (!_caseVM.IsLoadingCase)
            {
                _parent.SearchText = "";
                _filterVM.IsTimeFocusActive = false;
                _filterVM.IsAppTimeFocusActive = false;

                _filterVM.NegativeFilters.Clear();
                _filterVM.ActiveThreadFilters.Clear();

                _filterVM.MainFilterRoot = null;
                _filterVM.AppFilterRoot = null;
                _filterVM.LastFilteredAppCache = null;
                _filterVM.LastFilteredCache = null;

                _parent.ResetTreeFilters();

                // Apply default PLC filter to FilteredLogs - must be AFTER clearing filters
                var defaultFilteredLogs = session.Logs.Where(l => _filterVM.IsDefaultLog(l)).ToList();
                if (_filterVM?.FilteredLogs != null)
                {
                    _filterVM.FilteredLogs.ReplaceAll(defaultFilteredLogs);
                    _parent.NotifyPropertyChanged(nameof(_parent.FilteredLogs)); // Explicitly notify UI
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
