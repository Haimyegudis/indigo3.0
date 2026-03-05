using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel
    {
        private bool CanExport()
        {
            return IncludeLogStats ||
                   (IncludeEmStatistics && HasEmStatisticsData) ||
                   IOComponents.Any(x => x.IsSelected) ||
                   AxisComponents.Any(x => x.IsSelected) ||
                   CHStepComponents.Any(x => x.IsSelected) ||
                   ThreadItems.Any(x => x.IsSelected);
        }

        private async Task ExecuteExport()
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

                await _csvService.ExportLogsToCsvAsync(_sessionData.Logs, _sessionData.FileName, preset);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Export failed: {ex.Message}", "Error");
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
                    FileName = "ExportPreset.json",
                    InitialDirectory = AppPaths.Root
                };

                if (saveDialog.ShowDialog() == true)
                {
                    string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
                    File.WriteAllText(saveDialog.FileName, json, Encoding.UTF8);
                    _dialogService.ShowInfo("Preset saved successfully!", "Success");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to save preset: {ex.Message}", "Error");
            }
        }

        private void LoadPreset()
        {
            try
            {
                OpenFileDialog openDialog = new OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json",
                    InitialDirectory = AppPaths.Root
                };

                if (openDialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(openDialog.FileName, Encoding.UTF8);
                    var preset = JsonConvert.DeserializeObject<ExportPreset>(json, AppConstants.SafeJsonSettings);

                    if (preset != null)
                    {
                        ApplyPreset(preset);
                        _dialogService.ShowInfo("Preset loaded successfully!", "Success");
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to load preset: {ex.Message}", "Error");
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
    }
}
