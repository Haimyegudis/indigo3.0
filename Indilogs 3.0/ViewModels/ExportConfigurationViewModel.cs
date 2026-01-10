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
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class ExportConfigurationViewModel : INotifyPropertyChanged
    {
        private readonly LogSessionData _sessionData;
        private readonly CsvExportService _csvService;

        // קולקציות לבחירה
        public ObservableCollection<SelectableItem> IOComponents { get; set; }
        public ObservableCollection<SelectableItem> AxisComponents { get; set; }
        public ObservableCollection<SelectableItem> ThreadItems { get; set; }

        // אופציות עמודות נוספות
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

        // פקודות
        public ICommand ExportCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand LoadPresetCommand { get; }
        public ICommand SelectAllIOCommand { get; }
        public ICommand SelectAllAxisCommand { get; }
        public ICommand SelectAllThreadsCommand { get; }
        public ICommand DeselectAllIOCommand { get; }
        public ICommand DeselectAllAxisCommand { get; }
        public ICommand DeselectAllThreadsCommand { get; }

        public ExportConfigurationViewModel(LogSessionData sessionData, CsvExportService csvService)
        {
            _sessionData = sessionData;
            _csvService = csvService;

            IOComponents = new ObservableCollection<SelectableItem>();
            AxisComponents = new ObservableCollection<SelectableItem>();
            ThreadItems = new ObservableCollection<SelectableItem>();

            // אתחול הפקודות
            ExportCommand = new RelayCommand(async _ => await ExecuteExport(), _ => CanExport());
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            LoadPresetCommand = new RelayCommand(_ => LoadPreset());
            SelectAllIOCommand = new RelayCommand(_ => SelectAll(IOComponents));
            SelectAllAxisCommand = new RelayCommand(_ => SelectAll(AxisComponents));
            SelectAllThreadsCommand = new RelayCommand(_ => SelectAll(ThreadItems));
            DeselectAllIOCommand = new RelayCommand(_ => DeselectAll(IOComponents));
            DeselectAllAxisCommand = new RelayCommand(_ => DeselectAll(AxisComponents));
            DeselectAllThreadsCommand = new RelayCommand(_ => DeselectAll(ThreadItems));

            // טעינת נתונים
            LoadComponentsAndThreads();
        }

        private void LoadComponentsAndThreads()
        {
            if (_sessionData?.Logs == null) return;

            var ioComponents = new HashSet<string>();
            var axisComponents = new HashSet<string>();
            var threads = new HashSet<string>();

            foreach (var log in _sessionData.Logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                string msg = log.Message.Trim();

                // זיהוי IO Components
                if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            string subsystem = parts[0].Trim();

                            for (int i = 1; i < parts.Length; i++)
                            {
                                string rawPair = parts[i].Trim();
                                int eqIndex = rawPair.IndexOf('=');

                                if (eqIndex > 0)
                                {
                                    string fullSymbolName = rawPair.Substring(0, eqIndex).Trim();
                                    string componentName;

                                    if (fullSymbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                    else if (fullSymbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                    else
                                        componentName = fullSymbolName;

                                    ioComponents.Add($"{subsystem}|{componentName}");
                                }
                            }
                        }
                    }
                    catch { }
                }

                // זיהוי Axis Components
                if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 3)
                        {
                            string subsystem = parts[0].Trim();
                            string motor = parts[1].Trim();
                            axisComponents.Add($"{subsystem}|{motor}");
                        }
                    }
                    catch { }
                }

                // זיהוי Threads
                if (!string.IsNullOrEmpty(log.ThreadName))
                {
                    threads.Add(log.ThreadName);
                }
            }

            // מילוי הקולקציות
            foreach (var io in ioComponents.OrderBy(x => x))
            {
                var parts = io.Split('|');
                IOComponents.Add(new SelectableItem
                {
                    Name = parts.Length > 1 ? parts[1] : io,
                    Category = parts.Length > 1 ? parts[0] : "Unknown",
                    IsSelected = true
                });
            }

            foreach (var axis in axisComponents.OrderBy(x => x))
            {
                var parts = axis.Split('|');
                AxisComponents.Add(new SelectableItem
                {
                    Name = parts.Length > 1 ? parts[1] : axis,
                    Category = parts.Length > 1 ? parts[0] : "Unknown",
                    IsSelected = true
                });
            }

            foreach (var thread in threads.OrderBy(x => x))
            {
                ThreadItems.Add(new SelectableItem
                {
                    Name = thread,
                    Category = "Thread",
                    IsSelected = false // לא נבחר כברירת מחדל
                });
            }
        }

        private bool CanExport()
        {
            return IOComponents.Any(x => x.IsSelected) ||
                   AxisComponents.Any(x => x.IsSelected) ||
                   ThreadItems.Any(x => x.IsSelected);
        }

        private async System.Threading.Tasks.Task ExecuteExport()
        {
            try
            {
                // יצירת ExportPreset מהבחירות הנוכחיות
                var preset = new ExportPreset
                {
                    IncludeUnixTime = IncludeUnixTime,
                    IncludeEvents = IncludeEvents,
                    SelectedIOComponents = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedAxisComponents = AxisComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedThreads = ThreadItems.Where(x => x.IsSelected)
                        .Select(x => x.Name).ToList()
                };

                // קריאה ל-CsvExportService עם הפרסט
                await _csvService.ExportLogsToCsvAsync(_sessionData.Logs, _sessionData.FileName, preset);

                MessageBox.Show("Export completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    SelectedIOComponents = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList(),
                    SelectedAxisComponents = AxisComponents.Where(x => x.IsSelected)
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

            // עדכון בחירות IO
            foreach (var item in IOComponents)
            {
                string key = $"{item.Category}|{item.Name}";
                item.IsSelected = preset.SelectedIOComponents.Contains(key);
            }

            // עדכון בחירות Axis
            foreach (var item in AxisComponents)
            {
                string key = $"{item.Category}|{item.Name}";
                item.IsSelected = preset.SelectedAxisComponents.Contains(key);
            }

            // עדכון בחירות Threads
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
