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
    public partial class LogSessionViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private readonly IWindowOwnerProvider _windowOwner;
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

        public LogSessionViewModel(MainViewModel parent, ILogFileService logService, ILogColoringService coloringService, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher, IWindowOwnerProvider windowOwner)
        {
            _parent = parent;
            _logService = logService;
            _coloringService = coloringService;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowOwner = windowOwner;

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

        private async Task LoadFile(object? obj)
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

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}