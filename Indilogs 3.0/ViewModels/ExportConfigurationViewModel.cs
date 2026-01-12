using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
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
    public class ExportConfigurationViewModel : INotifyPropertyChanged
    {
        private readonly LogSessionData _sessionData;
        private readonly CsvExportService _csvService;

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

        public ExportConfigurationViewModel(LogSessionData sessionData, CsvExportService csvService)
        {
            _sessionData = sessionData;
            _csvService = csvService;

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

            ExportCommand = new RelayCommand(async _ => await ExecuteExport(), _ => CanExport());
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

            LoadComponentsAndThreads();
        }

        private async void LoadComponentsAndThreads()
        {
            if (_sessionData?.Logs == null) return;

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

                    // IO Components - optimized with faster checks
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
                        catch { }
                    }
                    // Axis Components - optimized
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
                        catch { }
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
                        catch { }
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

        private bool CanExport()
        {
            return IncludeLogStats ||
                   IOComponents.Any(x => x.IsSelected) ||
                   AxisComponents.Any(x => x.IsSelected) ||
                   CHStepComponents.Any(x => x.IsSelected) ||
                   ThreadItems.Any(x => x.IsSelected);
        }

        private async Task ExecuteExport()
        {
            try
            {
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

                await _csvService.ExportLogsToCsvAsync(_sessionData.Logs, _sessionData.FileName, preset);
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
                    var preset = JsonConvert.DeserializeObject<ExportPreset>(json);

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}