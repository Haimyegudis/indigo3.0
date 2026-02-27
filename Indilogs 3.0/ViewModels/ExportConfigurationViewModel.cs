using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Interfaces;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public class ExportConfigurationViewModel : ViewModelBase
    {
        private readonly LogSessionData _sessionData;
        private readonly ICsvExportService _csvService;

        // S4-5 mode: binary APP — hides AXIS, CHStep, Thread columns (show only IO)
        public bool IsBinaryApp { get; private set; }

        // IoTerminal data (S4-5 with Io-*.csv files in TerminalLogs)
        private bool _hasIoTerminalData;
        private List<IoDeviceData> _ioDevices;

        public ObservableCollection<SelectableItem> IOComponents { get; set; }
        public ObservableCollection<SelectableItem> AxisComponents { get; set; }
        public ObservableCollection<SelectableItem> CHStepComponents { get; set; }
        public ObservableCollection<SelectableItem> ThreadItems { get; set; }

        // Cached filtered lists for performance
        private List<SelectableItem> _cachedIOFiltered;
        private List<SelectableItem> _cachedAxisFiltered;
        private List<SelectableItem> _cachedCHStepFiltered;
        private List<SelectableItem> _cachedThreadFiltered;

        // Debounce timer for search - prevents lag while typing
        private DispatcherTimer _searchDebounceTimer;
        private const int SEARCH_DEBOUNCE_MS = 300;
        private bool _ioSearchPending = false;
        private bool _axisSearchPending = false;
        private bool _chStepSearchPending = false;
        private bool _threadSearchPending = false;

        private bool _includeUnixTime = true;
        public bool IncludeUnixTime
        {
            get => _includeUnixTime;
            set { _includeUnixTime = value; OnPropertyChanged(nameof(IncludeUnixTime)); }
        }

        private bool _includeEvents = true;
        public bool IncludeEvents
        {
            get => _includeEvents;
            set { _includeEvents = value; OnPropertyChanged(nameof(IncludeEvents)); }
        }

        private bool _includeMachineState = true;
        public bool IncludeMachineState
        {
            get => _includeMachineState;
            set { _includeMachineState = value; OnPropertyChanged(nameof(IncludeMachineState)); }
        }

        private bool _includeLogStats = false;
        public bool IncludeLogStats
        {
            get => _includeLogStats;
            set
            {
                _includeLogStats = value;
                OnPropertyChanged(nameof(IncludeLogStats));
                // CommandManager will automatically refresh - no manual trigger needed
            }
        }

        private string _ioSearchText = string.Empty;
        public string IOSearchText
        {
            get => _ioSearchText;
            set
            {
                _ioSearchText = value;
                OnPropertyChanged(nameof(IOSearchText));

                // Debounced search - mark as pending and restart timer
                _ioSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _axisSearchText = string.Empty;
        public string AxisSearchText
        {
            get => _axisSearchText;
            set
            {
                _axisSearchText = value;
                OnPropertyChanged(nameof(AxisSearchText));

                _axisSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _chStepSearchText = string.Empty;
        public string CHStepSearchText
        {
            get => _chStepSearchText;
            set
            {
                _chStepSearchText = value;
                OnPropertyChanged(nameof(CHStepSearchText));

                _chStepSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _threadSearchText = string.Empty;
        public string ThreadSearchText
        {
            get => _threadSearchText;
            set
            {
                _threadSearchText = value;
                OnPropertyChanged(nameof(ThreadSearchText));

                _threadSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        private string _loadingMessage = "";
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                _loadingMessage = value;
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }

        public IEnumerable<SelectableItem> FilteredIOComponents =>
            _cachedIOFiltered != null ? (IEnumerable<SelectableItem>)_cachedIOFiltered : IOComponents;
        public IEnumerable<SelectableItem> FilteredAxisComponents =>
            _cachedAxisFiltered != null ? (IEnumerable<SelectableItem>)_cachedAxisFiltered : AxisComponents;
        public IEnumerable<SelectableItem> FilteredCHStepComponents =>
            _cachedCHStepFiltered != null ? (IEnumerable<SelectableItem>)_cachedCHStepFiltered : CHStepComponents;
        public IEnumerable<SelectableItem> FilteredThreadItems =>
            _cachedThreadFiltered != null ? (IEnumerable<SelectableItem>)_cachedThreadFiltered : ThreadItems;

        private void UpdateIOFilter()
        {
            if (string.IsNullOrWhiteSpace(IOSearchText))
            {
                _cachedIOFiltered = null;
            }
            else
            {
                var search = IOSearchText.ToLowerInvariant();
                _cachedIOFiltered = IOComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredIOComponents));
        }

        private void UpdateAxisFilter()
        {
            if (string.IsNullOrWhiteSpace(AxisSearchText))
            {
                _cachedAxisFiltered = null;
            }
            else
            {
                var search = AxisSearchText.ToLowerInvariant();
                _cachedAxisFiltered = AxisComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredAxisComponents));
        }

        private void UpdateCHStepFilter()
        {
            if (string.IsNullOrWhiteSpace(CHStepSearchText))
            {
                _cachedCHStepFiltered = null;
            }
            else
            {
                var search = CHStepSearchText.ToLowerInvariant();
                _cachedCHStepFiltered = CHStepComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredCHStepComponents));
        }

        private void UpdateThreadFilter()
        {
            if (string.IsNullOrWhiteSpace(ThreadSearchText))
            {
                _cachedThreadFiltered = null;
            }
            else
            {
                var search = ThreadSearchText.ToLowerInvariant();
                _cachedThreadFiltered = ThreadItems.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredThreadItems));
        }

        // Timer tick - execute pending searches
        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            // Execute only pending searches
            if (_ioSearchPending)
            {
                _ioSearchPending = false;
                UpdateIOFilter();
            }

            if (_axisSearchPending)
            {
                _axisSearchPending = false;
                UpdateAxisFilter();
            }

            if (_chStepSearchPending)
            {
                _chStepSearchPending = false;
                UpdateCHStepFilter();
            }

            if (_threadSearchPending)
            {
                _threadSearchPending = false;
                UpdateThreadFilter();
            }
        }

        public ICommand ExportCommand { get; }
        public ICommand OpenInViewerCommand { get; }
        public ICommand OpenInChartsTabCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand LoadPresetCommand { get; }
        public ICommand SelectAllIOCommand { get; }
        public ICommand SelectAllAxisCommand { get; }
        public ICommand SelectAllCHStepsCommand { get; }
        public ICommand SelectAllThreadsCommand { get; }
        public ICommand DeselectAllIOCommand { get; }
        public ICommand DeselectAllAxisCommand { get; }
        public ICommand DeselectAllCHStepsCommand { get; }
        public ICommand DeselectAllThreadsCommand { get; }

        private string _lastExportedFilePath;
        public string LastExportedFilePath
        {
            get => _lastExportedFilePath;
            set
            {
                _lastExportedFilePath = value;
                OnPropertyChanged(nameof(LastExportedFilePath));
                OnPropertyChanged(nameof(CanOpenInViewer));
            }
        }

        public bool CanOpenInViewer => !string.IsNullOrEmpty(LastExportedFilePath) && File.Exists(LastExportedFilePath);

        public ExportConfigurationViewModel(LogSessionData sessionData, ICsvExportService csvService)
        {
            _sessionData = sessionData;
            _csvService = csvService;

            // S4-5: binary APP — only IO column visible in export window
            IsBinaryApp = sessionData?.HasBinaryAppLogs == true;

            IOComponents = new ObservableCollection<SelectableItem>();
            AxisComponents = new ObservableCollection<SelectableItem>();
            CHStepComponents = new ObservableCollection<SelectableItem>();
            ThreadItems = new ObservableCollection<SelectableItem>();

            // Initialize debounce timer for search
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SEARCH_DEBOUNCE_MS)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            ExportCommand = new RelayCommand(async _ => await ExecuteExport(false), _ => CanExport());
            OpenInViewerCommand = new RelayCommand(async _ => await ExecuteExport(true), _ => CanExport());
            OpenInChartsTabCommand = new RelayCommand(async _ => await OpenInChartsTabAsync(), _ => CanExport());
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            LoadPresetCommand = new RelayCommand(_ => LoadPreset());
            SelectAllIOCommand = new RelayCommand(_ => SelectAll(IOComponents));
            SelectAllAxisCommand = new RelayCommand(_ => SelectAll(AxisComponents));
            SelectAllCHStepsCommand = new RelayCommand(_ => SelectAll(CHStepComponents));
            SelectAllThreadsCommand = new RelayCommand(_ => SelectAll(ThreadItems));
            DeselectAllIOCommand = new RelayCommand(_ => DeselectAll(IOComponents));
            DeselectAllAxisCommand = new RelayCommand(_ => DeselectAll(AxisComponents));
            DeselectAllCHStepsCommand = new RelayCommand(_ => DeselectAll(CHStepComponents));
            DeselectAllThreadsCommand = new RelayCommand(_ => DeselectAll(ThreadItems));

            _ = LoadComponentsAndThreads();
        }

        private async Task LoadComponentsAndThreads()
        {
            try
            {
            if (_sessionData == null) return;

            // ── S4-5 with Io-*.csv: load IO components from TerminalLogs ────
            bool hasIoCsv = (_sessionData.TerminalCsvBytes != null &&
                             _sessionData.TerminalCsvBytes.Keys.Any(
                                 k => k.StartsWith("Io-", StringComparison.OrdinalIgnoreCase))) ||
                            (_sessionData.TerminalLogFiles != null &&
                             _sessionData.TerminalLogFiles.Keys.Any(
                                 k => k.StartsWith("Io-", StringComparison.OrdinalIgnoreCase)));

            if (_sessionData.HasBinaryAppLogs && hasIoCsv)
            {
                _hasIoTerminalData = true;

                IsLoading = true;
                LoadingMessage = "Loading IO components from TerminalLogs...";

                await Task.Run(() =>
                {
                    var svc = new IoTerminalDataService();
                    _ioDevices = svc.ParseIoFiles(_sessionData.TerminalLogFiles, _sessionData.TerminalCsvBytes);
                    var items = svc.GetAllComponents(_ioDevices);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IOComponents.Clear();
                        foreach (var item in items)
                            IOComponents.Add(item);

                        IsLoading = false;
                        LoadingMessage = $"Found {IOComponents.Count} IO components";
                    });
                });
                return; // S4-5 only needs IO — skip log scanning
            }

            // ── S6 (and S4-5 without terminal CSVs): scan from session logs ──
            if (_sessionData.Logs == null) return;

            // Show loading indicator
            IsLoading = true;
            LoadingMessage = "Scanning logs for components...";

            await Task.Run(() =>
            {
                // Use ConcurrentDictionary for thread-safe parallel processing
                var ioComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var axisComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var chStepComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var threads = new ConcurrentDictionary<string, byte>();

                int processedLogs = 0;
                int totalLogs = _sessionData.Logs.Count;

                // Process logs in parallel for better performance
                Parallel.ForEach(_sessionData.Logs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, log =>
                {
                    if (string.IsNullOrEmpty(log.Message)) return;

                    string msg = log.Message;

                    // Update progress every 10000 logs
                    int current = System.Threading.Interlocked.Increment(ref processedLogs);
                    if (current % 10000 == 0)
                    {
                        double progress = (double)current / totalLogs * 100;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            LoadingMessage = $"Scanning logs... {progress:F1}% ({current:N0} / {totalLogs:N0})";
                        }));
                    }

                    // Early filtering - skip lines that are definitely not relevant
                    char firstChar = msg.Length > 0 ? msg[0] : ' ';
                    if (firstChar != 'I' && firstChar != 'i' &&
                        firstChar != 'A' && firstChar != 'a' &&
                        firstChar != 'C' && firstChar != 'c')
                    {
                        // Still check threads
                        if (!string.IsNullOrEmpty(log.ThreadName))
                            threads.TryAdd(log.ThreadName, 0);
                        return;
                    }

                    // IO Components - current IO_Mon pattern
                    if (msg.Length > 7 && (msg[0] == 'I' || msg[0] == 'i') &&
                        msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 2)
                            {
                                string subsystem = parts[0].Trim();

                                for (int i = 1; i < parts.Length; i++)
                                {
                                    int eqIndex = parts[i].IndexOf('=');
                                    if (eqIndex > 0)
                                    {
                                        string fullSymbolName = parts[i].Substring(0, eqIndex).Trim();
                                        string componentName;

                                        if (fullSymbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                            componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8);
                                        else if (fullSymbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                            componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8);
                                        else
                                            componentName = fullSymbolName;

                                        ioComponents.TryAdd($"{subsystem}|{componentName}", 0);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing IO_Mon component failed", ex);
                        }
                    }
                    // IO Components - optimized IO: pattern (20.01.2026)
                    else if (msg.Length > 3 && (msg[0] == 'I' || msg[0] == 'i') &&
                             msg.StartsWith("IO:", StringComparison.OrdinalIgnoreCase) &&
                             !msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 2)
                            {
                                string subsystem = parts[0].Trim();
                                string pair = parts[1].Trim();
                                int eqIndex = pair.IndexOf('=');
                                if (eqIndex > 0)
                                {
                                    string fullSymbolName = pair.Substring(0, eqIndex).Trim();
                                    string componentName;

                                    if (fullSymbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8);
                                    else if (fullSymbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8);
                                    else
                                        componentName = fullSymbolName;

                                    ioComponents.TryAdd($"{subsystem}|{componentName}", 0);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing IO component failed", ex);
                        }
                    }
                    // Axis Components - current AxisMon pattern
                    else if (msg.Length > 8 && (msg[0] == 'A' || msg[0] == 'a') &&
                             msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 3)
                            {
                                string subsystem = parts[0].Trim();
                                string motor = parts[1].Trim();
                                axisComponents.TryAdd($"{subsystem}|{motor}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing AxisMon component failed", ex);
                        }
                    }
                    // Axis Components - optimized AxM: pattern (20.01.2026)
                    else if (msg.Length > 4 && (msg[0] == 'A' || msg[0] == 'a') &&
                             msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 3)
                            {
                                string subsystem = parts[0].Trim();
                                string motor = parts[1].Trim();
                                axisComponents.TryAdd($"{subsystem}|{motor}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing AxM component failed", ex);
                        }
                    }
                    // CHStep Components - optimized with faster string parsing
                    else if (msg.Length > 7 && (msg[0] == 'C' || msg[0] == 'c') &&
                             msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Fast path: use IndexOf instead of regex
                            int firstComma = msg.IndexOf(',', 7);
                            if (firstComma < 0) return;

                            int statePos = msg.IndexOf("State ", firstComma, StringComparison.OrdinalIgnoreCase);
                            if (statePos < 0) return;

                            int openBracket = msg.IndexOf('<', statePos);
                            if (openBracket < 0) return;

                            // Extract CHName (between "CHStep:" and first comma)
                            string chName = msg.Substring(7, firstComma - 7).Trim();

                            // Extract CHParentName (first item after '<')
                            int nextComma = msg.IndexOf(',', openBracket);
                            if (nextComma < 0) return;

                            string chParentName = msg.Substring(openBracket + 1, nextComma - openBracket - 1).Trim();

                            if (!chName.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase))
                            {
                                chStepComponents.TryAdd($"{chParentName}|{chName}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing CHStep component failed", ex);
                        }
                    }

                    // Threads
                    if (!string.IsNullOrEmpty(log.ThreadName))
                    {
                        threads.TryAdd(log.ThreadName, 0);
                    }
                });

                // Build lists (not yet added to ObservableCollection)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadingMessage = "Building component lists...";
                }));

                var ioList = ioComponents.Keys.OrderBy(x => x).Select(io =>
                {
                    var parts = io.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : io,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var axisList = axisComponents.Keys.OrderBy(x => x).Select(axis =>
                {
                    var parts = axis.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : axis,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var chStepList = chStepComponents.Keys.OrderBy(x => x).Select(ch =>
                {
                    var parts = ch.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : ch,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var threadList = threads.Keys.OrderBy(x => x).Select(thread =>
                    new SelectableItem
                    {
                        Name = thread,
                        Category = "Thread",
                        IsSelected = false  // DEFAULT = FALSE
                    }).ToList();

                // Add to UI on UI thread - NON-BLOCKING
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadingMessage = "Populating UI...";

                    // Clear and add all at once (much faster than individual adds)
                    IOComponents.Clear();
                    foreach (var item in ioList)
                        IOComponents.Add(item);

                    AxisComponents.Clear();
                    foreach (var item in axisList)
                        AxisComponents.Add(item);

                    CHStepComponents.Clear();
                    foreach (var item in chStepList)
                        CHStepComponents.Add(item);

                    ThreadItems.Clear();
                    foreach (var item in threadList)
                        ThreadItems.Add(item);

                    // Initialize cached lists
                    _cachedIOFiltered = IOComponents.ToList();
                    _cachedAxisFiltered = AxisComponents.ToList();
                    _cachedCHStepFiltered = CHStepComponents.ToList();
                    _cachedThreadFiltered = ThreadItems.ToList();

                    IsLoading = false;
                    LoadingMessage = $"Found {IOComponents.Count} IO, {AxisComponents.Count} Axis, {CHStepComponents.Count} CHSteps, {ThreadItems.Count} Threads";
                }));
            });
            }
            catch (Exception ex) { AppLogger.Error("LoadComponentsAndThreads failed", ex); }
        }

        private bool CanExport()
        {
            return IncludeLogStats ||
                   IOComponents.Any(x => x.IsSelected) ||
                   AxisComponents.Any(x => x.IsSelected) ||
                   CHStepComponents.Any(x => x.IsSelected) ||
                   ThreadItems.Any(x => x.IsSelected);
        }

        private async Task ExecuteExport(bool openInViewer = false)
        {
            try
            {
                // ── IoTerminal export (S4-5 with Io-*.csv) ──────────────────────────
                if (_hasIoTerminalData && _ioDevices != null)
                {
                    var saveDialog = new SaveFileDialog
                    {
                        Filter = "CSV Files (*.csv)|*.csv",
                        FileName = $"IoExport_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                    };
                    if (saveDialog.ShowDialog() != true) return;

                    IsLoading = true;
                    LoadingMessage = "Exporting IO terminal data...";

                    var selectedKeys = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList();

                    var svc = new IoTerminalDataService();
                    var prog = new Progress<double>(p => LoadingMessage = $"Exporting... {p:F0}%");
                    await svc.ExportMergedCsvAsync(_ioDevices, selectedKeys, saveDialog.FileName,
                                                   prog, System.Threading.CancellationToken.None);

                    IsLoading = false;
                    LoadingMessage = string.Empty;
                    LastExportedFilePath = saveDialog.FileName;

                    if (openInViewer) OpenInViewer();
                    return;
                }

                // ── Standard export path (S6 and S4-5 without terminal CSVs) ────────
                var preset = new ExportPreset
                {
                    IncludeUnixTime = IncludeUnixTime,
                    IncludeEvents = IncludeEvents,
                    IncludeMachineState = IncludeMachineState,
                    IncludeLogStats = IncludeLogStats,
                    SelectedIOComponents = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedAxisComponents = AxisComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedCHSteps = CHStepComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedThreads = ThreadItems.Where(x => x.IsSelected)
                        .Select(x => x.Name).ToList()
                };

                string exportedPath = await _csvService.ExportLogsToCsvAsync(_sessionData.Logs, _sessionData.FileName, preset);

                if (!string.IsNullOrEmpty(exportedPath))
                {
                    LastExportedFilePath = exportedPath;

                    if (openInViewer)
                    {
                        OpenInViewer();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePreset()
        {
            try
            {
                var preset = new ExportPreset
                {
                    Name = "Custom Preset",
                    CreatedDate = DateTime.Now,
                    IncludeUnixTime = IncludeUnixTime,
                    IncludeEvents = IncludeEvents,
                    IncludeMachineState = IncludeMachineState,
                    IncludeLogStats = IncludeLogStats,
                    SelectedIOComponents = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedAxisComponents = AxisComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedCHSteps = CHStepComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedThreads = ThreadItems.Where(x => x.IsSelected)
                        .Select(x => x.Name).ToList()
                };

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json",
                    FileName = "ExportPreset.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
                    File.WriteAllText(saveDialog.FileName, json, Encoding.UTF8);
                    MessageBox.Show("Preset saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPreset()
        {
            try
            {
                OpenFileDialog openDialog = new OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json"
                };

                if (openDialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(openDialog.FileName, Encoding.UTF8);
                    var preset = JsonConvert.DeserializeObject<ExportPreset>(json, new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });

                    if (preset != null)
                    {
                        ApplyPreset(preset);
                        MessageBox.Show("Preset loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyPreset(ExportPreset preset)
        {
            IncludeUnixTime = preset.IncludeUnixTime;
            IncludeEvents = preset.IncludeEvents;
            IncludeMachineState = preset.IncludeMachineState;
            IncludeLogStats = preset.IncludeLogStats;

            foreach (var item in IOComponents)
            {
                string key = $"{item.Category}|{item.Name}";
                item.IsSelected = preset.SelectedIOComponents.Contains(key);
            }

            foreach (var item in AxisComponents)
            {
                string key = $"{item.Category}|{item.Name}";
                item.IsSelected = preset.SelectedAxisComponents.Contains(key);
            }

            foreach (var item in CHStepComponents)
            {
                string key = $"{item.Category}|{item.Name}";
                item.IsSelected = preset.SelectedCHSteps.Contains(key);
            }

            foreach (var item in ThreadItems)
            {
                item.IsSelected = preset.SelectedThreads.Contains(item.Name);
            }
        }

        private void SelectAll(ObservableCollection<SelectableItem> collection)
        {
            foreach (var item in collection)
                item.IsSelected = true;
        }

        private void DeselectAll(ObservableCollection<SelectableItem> collection)
        {
            foreach (var item in collection)
                item.IsSelected = false;
        }

        private void OpenInViewer()
        {
            if (string.IsNullOrEmpty(LastExportedFilePath) || !File.Exists(LastExportedFilePath))
            {
                MessageBox.Show("No exported file available to open.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Look for Flow CSV Viewer in common installation paths
                string flowViewerPath = null;
                string[] searchPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Flow CSV Viewer", "Flow CSV Viewer.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Flow CSV Viewer", "Flow CSV Viewer.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Flow CSV Viewer", "Flow CSV Viewer.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flow CSV Viewer.exe"),
                };

                foreach (var path in searchPaths)
                {
                    if (File.Exists(path))
                    {
                        flowViewerPath = path;
                        break;
                    }
                }

                if (flowViewerPath != null)
                {
                    System.Diagnostics.Process.Start(flowViewerPath, $"\"{LastExportedFilePath}\"");
                }
                else
                {
                    // Fallback: open with default CSV application
                    System.Diagnostics.Process.Start(LastExportedFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open viewer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Opens data directly in the Charts tab without file export (In-Memory transfer)
        /// </summary>
        private async Task OpenInChartsTabAsync()
        {
            try
            {
                IsLoading = true;
                ChartDataPackage dataPackage = null;

                if (_hasIoTerminalData && _ioDevices != null)
                {
                    // ── S4-5 IoTerminal: build from CSV data, clipped to log range, state from logs ──
                    LoadingMessage = "Building IO chart data...";
                    var selectedKeys = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList();

                    await Task.Run(() =>
                    {
                        dataPackage = BuildIoTerminalChartPackage(_ioDevices, selectedKeys, _sessionData);
                    });
                }
                else
                {
                    // ── S6 standard: build from session logs ─────────────────
                    LoadingMessage = "Building chart data...";
                    var preset = new ExportPreset
                    {
                        IncludeUnixTime = IncludeUnixTime,
                        IncludeEvents = IncludeEvents,
                        IncludeMachineState = IncludeMachineState,
                        IncludeLogStats = IncludeLogStats,
                        SelectedIOComponents = IOComponents.Where(x => x.IsSelected)
                            .Select(x => $"{x.Category}|{x.Name}").ToList(),
                        SelectedAxisComponents = AxisComponents.Where(x => x.IsSelected)
                            .Select(x => $"{x.Category}|{x.Name}").ToList(),
                        SelectedCHSteps = CHStepComponents.Where(x => x.IsSelected)
                            .Select(x => $"{x.Category}|{x.Name}").ToList(),
                        SelectedThreads = ThreadItems.Where(x => x.IsSelected)
                            .Select(x => x.Name).ToList()
                    };

                    var transferService = ChartDataTransferService.Instance;
                    await Task.Run(() =>
                    {
                        dataPackage = transferService.BuildDataPackage(
                            _sessionData.Logs,
                            preset,
                            _sessionData.FileName ?? "Session");
                    });
                }

                IsLoading = false;

                if (dataPackage == null || (dataPackage.Signals.Count == 0 && dataPackage.States.Count == 0))
                {
                    MessageBox.Show("No data to display. Please select at least one signal or state.",
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Transfer data and switch to Charts tab
                var svc = ChartDataTransferService.Instance;
                svc.TransferDataToCharts(dataPackage);
                svc.RequestSwitchToCharts();

                CloseWindow?.Invoke();
            }
            catch (Exception ex)
            {
                IsLoading = false;
                MessageBox.Show($"Failed to open in Charts tab: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Builds chart package from IoTerminal CSV data, clipped to the PLC log time range.
        /// MachineState is parsed from PlcMngr transitions in session.Logs (same as States window).
        /// </summary>
        private static ChartDataPackage BuildIoTerminalChartPackage(
            List<IoDeviceData> devices,
            List<string> selectedKeys,
            LogSessionData sessionData)
        {
            var empty = new ChartDataPackage
            {
                SessionName = "IO Terminal",
                CreatedAt = DateTime.Now,
                TimeStamps = new List<DateTime>(),
                Signals = new List<SignalData>(),
                States = new List<StateData>(),
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>()
            };

            if (devices == null || !devices.Any() || !selectedKeys.Any()) return empty;

            // ── 1. Build merged timeline, clipped to PLC log range ──────────
            //    IoTerminal CSV "Timestamp" is time-only (HH:mm:ss.fff) →
            //    row.Timestamp has date 0001-01-01.  Prefer RawTime (Unix ns)
            //    for proper full DateTime; fallback: combine log date + CSV time.
            DateTime logStart = DateTime.MinValue, logEnd = DateTime.MaxValue;
            if (sessionData?.Logs != null && sessionData.Logs.Count > 0)
            {
                var sorted = sessionData.Logs.OrderBy(l => l.Date).ToList();
                logStart = sorted.First().Date;
                logEnd = sorted.Last().Date;
            }

            DateTime logDate = logStart > DateTime.MinValue ? logStart.Date : DateTime.Today;

            var allRowsRaw = devices
                .SelectMany(d => d.Rows.Select(r =>
                {
                    // Best: full DateTime from rawTime (Unix nanoseconds)
                    var fullDt = RawTimeToDateTime(r.RawTime);
                    // Fallback: combine PLC log date with CSV time-of-day
                    if (fullDt == DateTime.MinValue && r.Timestamp > DateTime.MinValue)
                        fullDt = logDate + r.Timestamp.TimeOfDay;
                    return (device: d, row: r, fullDt: fullDt);
                }))
                .OrderBy(x => x.fullDt)
                .ToList();

            // Clip to PLC log range (but keep everything if clipping empties the list)
            var allRows = allRowsRaw;
            if (logStart > DateTime.MinValue && logEnd < DateTime.MaxValue)
            {
                var clipped = allRowsRaw
                    .Where(x => x.fullDt >= logStart && x.fullDt <= logEnd)
                    .ToList();
                if (clipped.Any()) allRows = clipped;
            }

            if (!allRows.Any()) return empty;

            int dataLength = allRows.Count;
            var timestamps = allRows.Select(x => x.fullDt).ToList();

            // ── 2. Initialise signal arrays with NaN ────────────────────────
            var keyParts = new Dictionary<string, (string Dev, string Col)>(StringComparer.OrdinalIgnoreCase);
            var signalArr = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in selectedKeys)
            {
                int sep = key.IndexOf('|');
                if (sep < 0) continue;
                keyParts[key] = (key.Substring(0, sep), key.Substring(sep + 1));
                var arr = new double[dataLength];
                for (int i = 0; i < dataLength; i++) arr[i] = double.NaN;
                signalArr[key] = arr;
            }

            // ── 3. Fill values at each merged timeline index ────────────────
            for (int i = 0; i < allRows.Count; i++)
            {
                var (device, row, _) = allRows[i];
                foreach (var key in selectedKeys)
                {
                    if (!keyParts.TryGetValue(key, out var kp)) continue;
                    if (!kp.Dev.Equals(device.DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (row.Values.TryGetValue(kp.Col, out string strVal) &&
                        double.TryParse(strVal, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double dval))
                    {
                        signalArr[key][i] = dval;
                    }
                }
            }

            // ── 4. Forward-fill NaN gaps per signal ─────────────────────────
            foreach (var arr in signalArr.Values)
            {
                double last = double.NaN;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!double.IsNaN(arr[i])) last = arr[i];
                    else if (!double.IsNaN(last)) arr[i] = last;
                }
            }

            // ── 5. Build SignalData objects ──────────────────────────────────
            var signals = new List<SignalData>();
            foreach (var kvp in signalArr)
            {
                var (cat, name) = keyParts[kvp.Key];
                bool isDigital = kvp.Value
                    .Where(v => !double.IsNaN(v))
                    .All(v => v == 0.0 || v == 1.0);
                signals.Add(new SignalData
                {
                    Name = $"{cat}-{name}",
                    Category = "IO",
                    SignalType = isDigital ? SignalType.Digital : SignalType.Analog,
                    Data = kvp.Value
                });
            }

            // ── 6. Build MachineState from CSV MachineState column ─────────
            //    Each IoDataRow already has MachineState parsed from the CSV.
            var states = new List<StateData>();
            {
                var stateIntervals = new List<StateInterval>();
                string currentState = null;
                int intervalStart = 0;

                for (int i = 0; i < allRows.Count; i++)
                {
                    string rowState = allRows[i].row.MachineState;
                    if (string.IsNullOrEmpty(rowState)) rowState = currentState; // forward-fill

                    if (rowState != currentState)
                    {
                        // Close previous interval
                        if (currentState != null)
                        {
                            stateIntervals.Add(new StateInterval
                            {
                                StartIndex = intervalStart,
                                EndIndex = Math.Max(intervalStart, i - 1),
                                StateId = Models.Charts.ChartStateConfig.GetId(currentState),
                                StateName = currentState
                            });
                        }
                        currentState = rowState;
                        intervalStart = i;
                    }
                }

                // Close last interval
                if (currentState != null)
                {
                    stateIntervals.Add(new StateInterval
                    {
                        StartIndex = intervalStart,
                        EndIndex = dataLength - 1,
                        StateId = Models.Charts.ChartStateConfig.GetId(currentState),
                        StateName = currentState
                    });
                }

                if (stateIntervals.Count > 0)
                {
                    states.Add(new StateData
                    {
                        Name = "MachineState",
                        Category = "PlcMngr",
                        Intervals = stateIntervals
                    });
                }
            }

            return new ChartDataPackage
            {
                SessionName = sessionData?.FileName ?? "IO Terminal",
                CreatedAt = DateTime.Now,
                TimeStamps = timestamps,
                Signals = signals,
                States = states,
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>(),
                SuppressGapDetection = true
            };
        }

        /// <summary>Converts IoTerminal rawTime (Unix nanoseconds) to local DateTime.</summary>
        private static DateTime RawTimeToDateTime(long rawTimeNs)
        {
            if (rawTimeNs <= 0) return DateTime.MinValue;
            try
            {
                long seconds = rawTimeNs / 1_000_000_000L;
                long remainNs = rawTimeNs % 1_000_000_000L;
                var dt = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                // Add sub-second ticks (1 tick = 100 ns)
                return dt.AddTicks(remainNs / 100L);
            }
            catch (Exception ex) { AppLogger.Error("RawTimeToDateTime failed", ex); return DateTime.MinValue; }
        }

        /// <summary>
        /// Action to close the window (set by the view)
        /// </summary>
        public Action CloseWindow { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                    _searchDebounceTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}