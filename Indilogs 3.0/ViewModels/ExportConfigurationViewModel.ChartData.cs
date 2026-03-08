using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel
    {
        /// <summary>
        /// Opens data directly in the Charts tab without file export (In-Memory transfer)
        /// </summary>
        private async Task OpenInChartsTabAsync()
        {
            try
            {
                IsLoading = true;
                ChartDataPackage? dataPackage = null;

                if (_hasIoTerminalData && _ioDevices != null)
                {
                    // ── S4-5 IoTerminal: build from CSV data, clipped to log range, state from logs ──
                    LoadingProgress = 0;
                    LoadingMessage = "Building IO chart data...";
                    var selectedKeys = IOComponents.Where(x => x.IsSelected)
                        .Select(x => $"{x.Category}|{x.Name}").ToList();

                    var progress = new Progress<(double pct, string msg)>(p =>
                    {
                        LoadingProgress = p.pct;
                        LoadingMessage = p.msg;
                        OnPropertyChanged(nameof(IsProgressVisible));
                    });

                    await Task.Run(() =>
                    {
                        dataPackage = BuildIoTerminalChartPackage(_ioDevices, selectedKeys, _sessionData, progress);
                    });
                }
                else
                {
                    // ── S6 standard: build from session logs ─────────────────
                    LoadingProgress = 0;
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

                    var progress = new Progress<(double pct, string msg)>(p =>
                    {
                        LoadingProgress = p.pct;
                        LoadingMessage = p.msg;
                        OnPropertyChanged(nameof(IsProgressVisible));
                    });

                    // Per-signal progress: accumulate on background threads, batch-update UI via timer.
                    // Using DirectProgress (no SynchronizationContext marshaling) + ConcurrentDictionary
                    // avoids the ObservableCollection flooding that caused InvalidOperationException.
                    _signalProgressItems = new List<SignalProgressItem>();
                    OnPropertyChanged(nameof(SignalProgressItems));
                    OnPropertyChanged(nameof(HasSignalProgress));
                    var signalStatusMap = new ConcurrentDictionary<string, string>();

                    // Direct progress — called on background thread, no UI marshaling
                    IProgress<(string signal, string status)> signalProgress = new DirectProgress<(string signal, string status)>(p =>
                    {
                        signalStatusMap[p.signal] = p.status;
                    });

                    // Timer batches UI updates every 250ms (prevents dispatcher flooding)
                    var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    refreshTimer.Tick += (s, e) =>
                    {
                        var snapshot = signalStatusMap.ToArray();
                        int doneCount = 0;
                        var list = new List<SignalProgressItem>(snapshot.Length);
                        foreach (var kvp in snapshot)
                        {
                            list.Add(new SignalProgressItem { Name = kvp.Key, Status = kvp.Value });
                            if (kvp.Value == "done") doneCount++;
                        }
                        // Sort: in-progress first, then done
                        list.Sort((a, b) =>
                        {
                            int cmp = (a.Status == "done" ? 1 : 0).CompareTo(b.Status == "done" ? 1 : 0);
                            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        });
                        // Cap at 30 items for smooth rendering (no virtualization in ItemsControl)
                        _signalProgressItems = list.Count > 30 ? list.GetRange(0, 30) : list;
                        OnPropertyChanged(nameof(SignalProgressItems));
                        OnPropertyChanged(nameof(HasSignalProgress));
                        LoadingMessage = $"Parsing signals... {doneCount}/{snapshot.Length} done";
                    };
                    refreshTimer.Start();

                    var transferService = ChartDataTransferService.Instance;
                    await Task.Run(() =>
                    {
                        dataPackage = transferService.BuildDataPackage(
                            _sessionData.Logs,
                            preset,
                            _sessionData.FileName ?? "Session",
                            progress,
                            signalProgress);
                    });

                    refreshTimer.Stop();
                    _signalProgressItems = new List<SignalProgressItem>();
                    OnPropertyChanged(nameof(SignalProgressItems));
                    OnPropertyChanged(nameof(HasSignalProgress));
                }

                IsLoading = false;

                // Attach EM Statistics CSV if checkbox is checked
                if (IncludeEmStatistics && HasEmStatisticsData && dataPackage != null)
                    dataPackage.EmStatisticsCsvContent = _sessionData.EmStatisticsCsvContent;

                if (dataPackage == null || (dataPackage.Signals.Count == 0 && dataPackage.States.Count == 0
                    && string.IsNullOrEmpty(dataPackage.EmStatisticsCsvContent)))
                {
                    _dialogService.ShowWarning("No data to display. Please select at least one signal or state.", "No Data");
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
                _dialogService.ShowError($"Failed to open in Charts tab: {ex.Message}", "Error");
            }
        }

}
}
