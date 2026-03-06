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
    public partial class LogSessionViewModel
    {
        /// <summary>
        /// Loads and processes log files into a new session, applying colors and updating the UI.
        /// </summary>
        public async Task ProcessFiles(string[] filePaths, Action<LogSessionData>? onLoadComplete = null)
        {
            bool isWatchableFile = false; // Track if we should start auto-refresh after loading

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
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"File access check failed: {ex.Message}");
                        isLiveFile = false;
                    }

                    if (isLiveFile)
                    {
                        _liveVM?.StartLiveMonitoring(filePath);
                        return;
                    }
                    // Otherwise, continue to load as static file below — but mark for auto-refresh
                    isWatchableFile = true;
                }
            }

            // --- Tab Selection Dialog: show for ZIP files before parsing ---
            TabSelectionConfig? tabSelection = null;
            TabSelectionConfig? preScan = null;
            bool hasZipFile = filePaths.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (hasZipFile)
            {
                string zipPath = filePaths.First(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                preScan = await Task.Run(() => _logService.PreScanZip(zipPath));

                // Show the dialog on the UI thread
                var dialog = _viewFactory.Create<Views.TabSelectionWindow>(preScan!);
                dialog.Owner = _windowOwner.GetOwner();
                if (dialog.ShowDialog() != true)
                {
                    // User cancelled — abort load
                    return;
                }
                tabSelection = dialog.ResultConfig;

                // Propagate tab visibility to parent VM
                _parent.ApplyTabSelection(tabSelection, preScan);
            }
            else
            {
                // Non-ZIP load: reset all tabs to visible
                _parent.ApplyTabSelection(null, null);
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

                var newSession = await _logService.LoadSessionAsync(filePaths, progress, tabSelection);

                newSession.FileName = Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1) newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                // Store tab selection configs for add-back feature
                if (hasZipFile && tabSelection != null)
                {
                    newSession.LoadTabSelection = tabSelection;
                    newSession.PreScanConfig = preScan;
                }

                // Run APP log parsing and color application in parallel for maximum speed
                StatusMessage = "Processing logs...";
                var postTasks = new List<Task>();

                // Parse APP logs (extracts Pattern, Data, Exception fields)
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Count > 0)
                {
                    postTasks.Add(Services.LogParserService.ParseLogEntriesAsync(newSession.AppDevLogs));
                }

                // Apply colors to main logs
                postTasks.Add(_coloringService.ApplyDefaultColorsAsync(newSession.Logs, false));
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    postTasks.Add(_coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true));

                await Task.WhenAll(postTasks);

                LoadedSessions.Add(newSession);

                // Bypass the SelectedSession setter to avoid SwitchToSession —
                // ProcessFiles already does all the setup work itself.
                // SwitchToSession is only needed when the user switches between
                // already-loaded sessions via the combo box.
                SaveFilterState(_selectedSession);
                _selectedSession = newSession;
                OnPropertyChanged(nameof(SelectedSession));
                _parent.NotifyPropertyChanged(nameof(_parent.HasSessionLoaded));

                // Update SessionVM with ALL loaded data
                // Share reference instead of copying to avoid doubling memory usage
                Logs = newSession.Logs;
                AllLogsCache = newSession.Logs;
                AllAppLogsCache = newSession.AppDevLogs ?? new List<LogEntry>();

                // Auto-calculate time sync offset between PLC and APP logs.
                // APP is the authoritative baseline (synced to PC/BIOS clock).
                //
                // Matching rule:
                //   APP:  Logger == PrcSymptomMapperManager  AND  Message starts with "--> eventID: <X>"
                //   PLC:  ThreadName == "Events"             AND  Message starts with "Send event <X>" or "Enqueue event <X>"
                //
                // Strategy: collect ALL matching APP↔PLC event pairs within ±2 minutes.
                // Use the pair with the MINIMUM absolute time difference as the anchor.
                // Minimum diff ≈ minimum processing delay ≈ best estimate of true clock offset.
                // offset = APP.Date - PLC.Date.
                _parent.HasTimeSyncData      = false;
                _parent.ShowSyncedTimeColumn = false;
                try
                {
                    if (newSession.Logs.Count > 0 && newSession.AppDevLogs != null && newSession.AppDevLogs.Count > 0)
                    {
                        const string appLogger   = "Press.BL.PrintCare.Symptoms.PrcSymptomMapperManager";
                        const string appPrefix   = "--> eventID: ";
                        const string plcThread   = "Events";
                        const double maxDiffMin  = 2.0;

                        // Track the best pair across ALL APP events (minimum time diff)
                        TimeSpan? bestSyncOffset = null;
                        double    bestAbsDiffSec = double.MaxValue;

                        // Pre-filter PLC Events logs once (already sorted by date from loading)
                        var plcEventLogs = newSession.Logs
                            .Where(l => string.Equals(l.ThreadName, plcThread, StringComparison.OrdinalIgnoreCase)
                                        && l.Message != null)
                            .ToList();

                        // Pre-extract PLC dates array for binary search — O(N) once
                        var plcDates = new DateTime[plcEventLogs.Count];
                        for (int pi = 0; pi < plcEventLogs.Count; pi++)
                            plcDates[pi] = plcEventLogs[pi].Date;

                        // Iterate ALL APP candidate logs to find the pair with minimum time diff
                        foreach (var appLog in newSession.AppDevLogs)
                        {
                            if (!string.Equals(appLog.Logger, appLogger, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (appLog.Message == null || !appLog.Message.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var afterPrefix = appLog.Message.Substring(appPrefix.Length).TrimStart();
                            var commaIdx    = afterPrefix.IndexOf(',');
                            var eventId     = (commaIdx >= 0 ? afterPrefix.Substring(0, commaIdx) : afterPrefix).Trim();
                            if (string.IsNullOrEmpty(eventId)) continue;

                            if (string.Equals(eventId, "PLC_FAILURE_STATE_CHANGE", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var sendPrefix    = "Send event "    + eventId;
                            var enqueuePrefix = "Enqueue event " + eventId;

                            // Binary search to find the nearest PLC log by time — O(log N) instead of O(N)
                            int idx = Array.BinarySearch(plcDates, appLog.Date);
                            if (idx < 0) idx = ~idx; // insertion point

                            LogEntry? bestPlcLog = null;
                            double    bestPairDiff = double.MaxValue;

                            // Scan outward from binary search point (only nearby entries within maxDiffMin)
                            for (int dir = -1; dir <= 1; dir += 2) // -1 = left, +1 = right
                            {
                                int start = dir < 0 ? idx - 1 : idx;
                                for (int j = start; j >= 0 && j < plcEventLogs.Count; j += dir)
                                {
                                    var diffMin = Math.Abs((appLog.Date - plcDates[j]).TotalMinutes);
                                    if (diffMin > maxDiffMin) break; // sorted → no closer matches further out

                                    var plcLog = plcEventLogs[j];
                                    bool isMatch = plcLog.Message!.StartsWith(sendPrefix, StringComparison.OrdinalIgnoreCase)
                                                || plcLog.Message.StartsWith(enqueuePrefix, StringComparison.OrdinalIgnoreCase);
                                    if (isMatch && diffMin < bestPairDiff)
                                    {
                                        bestPairDiff = diffMin;
                                        bestPlcLog = plcLog;
                                    }
                                }
                            }

                            if (bestPlcLog != null)
                            {
                                double absDiffSec = Math.Abs((appLog.Date - bestPlcLog.Date).TotalSeconds);
                                if (absDiffSec < bestAbsDiffSec)
                                {
                                    bestAbsDiffSec = absDiffSec;
                                    bestSyncOffset = appLog.Date - bestPlcLog.Date;
                                }
                            }
                        }

                        if (bestSyncOffset.HasValue && Math.Abs(bestSyncOffset.Value.TotalSeconds) > 1)
                        {
                            // Store offset as TimeSpan in session (tick-level precision for ms-accurate sync)
                            newSession.TimeSyncOffset = bestSyncOffset.Value;
                            newSession.HasTimeSyncData = true;

                            _parent.TimeSyncOffsetSeconds = bestSyncOffset.Value.TotalSeconds;

                            // Apply offset to every PLC log: SyncedTime == PLC raw time shifted to APP clock
                            foreach (var log in newSession.Logs)
                                log.SyncedTime = log.Date.Add(bestSyncOffset.Value);

                            _parent.HasTimeSyncData      = true;
                            _parent.ShowSyncedTimeColumn = true;
                            StatusMessage = $"⏱ Time sync: offset {bestSyncOffset.Value.TotalMilliseconds:F0}ms ({bestSyncOffset.Value.TotalSeconds:F1}s)";
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Time-sync calculation failed", ex);
                }

                // Update Events and cache (already sorted by LogFileService)
                if (newSession.Events != null && newSession.Events.Count > 0)
                {
                    AllEvents = newSession.Events; // Already sorted, share reference
                    // Single collection replacement — fires one PropertyChanged instead of N CollectionChanged
                    Events = new ObservableCollection<EventEntry>(newSession.Events);
                }
                else
                {
                    AllEvents = new List<EventEntry>();
                    Events = new ObservableCollection<EventEntry>();
                }

                _parent.LoadEventsDataView();

                // Update Screenshots — single collection replacement
                Screenshots = new ObservableCollection<BitmapImage>(newSession.Screenshots ?? new List<BitmapImage>());

                // Update LoadedFiles
                LoadedFiles.Clear();
                LoadedFiles.Add(newSession.FileName);

                // Update Setup Info, Press Config, Versions through parent
                _parent.SetupInfo = newSession.SetupInfo;
                _parent.PressConfig = newSession.PressConfiguration;
                _parent.VersionsInfo = newSession.VersionsInfo;

                // Update Config files (if any) or terminal logs
                if (newSession.ConfigurationFiles != null && newSession.ConfigurationFiles.Any() ||
                    newSession.DatabaseFiles != null && newSession.DatabaseFiles.Any() ||
                    newSession.TerminalLogFiles != null && newSession.TerminalLogFiles.Any() ||
                    newSession.TerminalCsvBytes != null && newSession.TerminalCsvBytes.Any())
                {
                    _configVM?.LoadConfigurationFiles();
                }
                _parent.CurrentPluginColumns = newSession.PluginColumns;
                _parent.NotifyPropertyChanged(nameof(_parent.DbConfigTabHeader));
                _parent.NotifyPropertyChanged(nameof(_parent.HasBinaryAppLogs));
                _parent.LoadGlobalsFiles();
                _parent.LoadSystabFiles();
                _parent.UpdateTabVisibilityAfterLoad();
                _parent.NotifyPropertyChanged(nameof(_parent.HasSkippedComponents));

                // Window title
                if (!string.IsNullOrEmpty(newSession.VersionsInfo))
                    _parent.WindowTitle = $"IndiLogs 3.0 - {newSession.FileName} ({newSession.VersionsInfo})";
                else
                    _parent.WindowTitle = $"IndiLogs 3.0 - {newSession.FileName}";

                // Build logger tree panels (must happen before filter application)
                _filterVM?.BuildLoggerTree(AllAppLogsCache);
                _filterVM?.BuildPlcLoggerTree(AllLogsCache);

                // Load saved configs for the dropdown
                _caseVM?.LoadSavedConfigs(newSession.HasBinaryAppLogs);

                // Restore chart state (null for new sessions = clear chart)
                _parent.ChartVM?.RestoreChartState(newSession.SavedChartState);

                // Apply initial filters (this is the FIRST and ONLY filter application)
                _filterVM?.ApplyMainLogsFilter();
                _filterVM?.ApplyAppLogsFilter();

                // Scroll all tabs to last row after loading
                // Use ApplicationIdle priority to ensure DataGrid virtualization is fully rendered
                // Use ScrollTabToBottom (not ScrollToLog) to directly target each grid,
                // because FindGridForLog always matches PLC first for shared log objects
                _dispatcher.Post(() =>
                    {
                        _parent.ScrollTabToBottom("PLC");
                        _parent.ScrollTabToBottom("FILTERED");
                        _parent.ScrollTabToBottom("APP");
                    }, DispatchPriority.ApplicationIdle);

                CurrentProgress = 100;
                StatusMessage = $"Logs Loaded ({newSession.Logs.Count:N0} PLC logs). Running Analysis in Background...";
                IsBusy = false;

                // Select PLC tab after loading
                _parent.SelectedTabIndex = 0;

                _parent.StartBackgroundAnalysis(newSession);

                // Call callback after successful load
                onLoadComplete?.Invoke(newSession);

                // Start auto-refresh for .file files so new logs are picked up automatically
                if (isWatchableFile && filePaths.Length == 1 && newSession.Logs.Count > 0)
                {
                    _liveVM?.StartFileWatcher(filePaths[0], newSession);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _dialogService.ShowError($"Error loading files: {ex.Message}", "Error");
                IsBusy = false;
            }
        }

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
