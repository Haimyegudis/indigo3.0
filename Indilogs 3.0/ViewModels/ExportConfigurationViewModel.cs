using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

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

        private string _ioSearchText = string.Empty;
        public string IOSearchText
        {
            get => _ioSearchText;
            set
            {
                _ioSearchText = value;
                OnPropertyChanged(nameof(IOSearchText));
                UpdateIOFilter();
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
                UpdateAxisFilter();
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
                UpdateCHStepFilter();
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
                UpdateThreadFilter();
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

        private void LoadComponentsAndThreads()
        {
            if (_sessionData?.Logs == null) return;

            // Use HashSet for O(1) lookups instead of lists
            var ioComponents = new HashSet<string>();
            var axisComponents = new HashSet<string>();
            var chStepComponents = new HashSet<string>();
            var threads = new HashSet<string>();

            // Pre-compiled regex for CHStep (much faster than Regex.Match in loop)
            var chStepRegex = new Regex(@"CHStep:\s*([^,]+),\s*[^,]*,\s*State\s+\d+\s*<([^,]+),", RegexOptions.Compiled);

            foreach (var log in _sessionData.Logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                string msg = log.Message.Trim();

                // IO Components - optimized
                if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
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

                                    ioComponents.Add($"{subsystem}|{componentName}");
                                }
                            }
                        }
                    }
                    catch { }
                }
                // Axis Components - optimized
                else if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(',');

                        if (parts.Length >= 3)
                        {
                            string subsystem = parts[0].Trim();
                            string motor = parts[1].Trim();
                            axisComponents.Add($"{subsystem}|{motor}");
                        }
                    }
                    catch { }
                }
                // CHStep Components - using pre-compiled regex
                else if (msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var match = chStepRegex.Match(msg);

                        if (match.Success)
                        {
                            string chName = match.Groups[1].Value.Trim();
                            string chParentName = match.Groups[2].Value.Trim();

                            if (!chName.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase))
                            {
                                chStepComponents.Add($"{chParentName}|{chName}");
                            }
                        }
                    }
                    catch { }
                }

                // Threads
                if (!string.IsNullOrEmpty(log.ThreadName))
                {
                    threads.Add(log.ThreadName);
                }
            }

            // Fill collections - DEFAULT = FALSE (not selected)
            foreach (var io in ioComponents.OrderBy(x => x))
            {
                var parts = io.Split('|');
                IOComponents.Add(new SelectableItem
                {
                    Name = parts.Length > 1 ? parts[1] : io,
                    Category = parts.Length > 1 ? parts[0] : "Unknown",
                    IsSelected = false  // DEFAULT = FALSE
                });
            }

            foreach (var axis in axisComponents.OrderBy(x => x))
            {
                var parts = axis.Split('|');
                AxisComponents.Add(new SelectableItem
                {
                    Name = parts.Length > 1 ? parts[1] : axis,
                    Category = parts.Length > 1 ? parts[0] : "Unknown",
                    IsSelected = false  // DEFAULT = FALSE
                });
            }

            foreach (var chStep in chStepComponents.OrderBy(x => x))
            {
                var parts = chStep.Split('|');
                CHStepComponents.Add(new SelectableItem
                {
                    Name = parts.Length > 1 ? parts[1] : chStep,
                    Category = parts.Length > 1 ? parts[0] : "Unknown",
                    IsSelected = false  // DEFAULT = FALSE
                });
            }

            foreach (var thread in threads.OrderBy(x => x))
            {
                ThreadItems.Add(new SelectableItem
                {
                    Name = thread,
                    Category = "Thread",
                    IsSelected = false  // DEFAULT = FALSE
                });
            }
        }

        private bool CanExport()
        {
            return IOComponents.Any(x => x.IsSelected) ||
                   AxisComponents.Any(x => x.IsSelected) ||
                   CHStepComponents.Any(x => x.IsSelected) ||
                   ThreadItems.Any(x => x.IsSelected);
        }

        private async System.Threading.Tasks.Task ExecuteExport()
        {
            try
            {
                var preset = new ExportPreset
                {
                    IncludeUnixTime = IncludeUnixTime,
                    IncludeEvents = IncludeEvents,
                    IncludeMachineState = IncludeMachineState,
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