using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class LogSessionViewModel
    {
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
                    _configVM?.LoadConfigurationFiles();
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
                    _filterVM?.BuildPlcLoggerTree(AllLogsCache);
                if (componentName == "App")
                    _filterVM?.BuildLoggerTree(AllAppLogsCache);

                // Re-apply filters
                _filterVM?.ApplyMainLogsFilter();
                _filterVM?.ApplyAppLogsFilter();

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
    }
}
