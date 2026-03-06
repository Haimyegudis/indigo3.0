using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Services.Charts
{
    /// <summary>
    /// Singleton service for transferring data between Logs and Charts without file I/O.
    /// Enables In-Memory data transfer for immediate chart visualization.
    /// </summary>
    public partial class ChartDataTransferService
    {
        private static ChartDataTransferService? _instance;
        private static readonly object _lock = new object();

        public static ChartDataTransferService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ChartDataTransferService();
                        }
                    }
                }
                return _instance;
            }
        }

        private ChartDataTransferService() { }

        /// <summary>
        /// Event fired when new data is ready for the Charts tab
        /// </summary>
        public event Action<ChartDataPackage>? OnDataReady;

        /// <summary>
        /// Event fired when user requests to switch to Charts tab
        /// </summary>
        public event Action? OnSwitchToChartsRequested;

        /// <summary>
        /// Event fired when log selection changes (for Log -> Chart sync)
        /// </summary>
        public event Action<DateTime>? OnLogTimeSelected;

        /// <summary>
        /// Event fired when chart cursor moves (for Chart -> Log sync)
        /// </summary>
        public event Action<DateTime>? OnChartTimeSelected;

        /// <summary>
        /// Current data package available for charts
        /// </summary>
        public ChartDataPackage? CurrentData { get; private set; }

        /// <summary>
        /// Transfers log data directly to charts without file export
        /// </summary>
        public void TransferDataToCharts(ChartDataPackage data)
        {
            CurrentData = data;
            OnDataReady?.Invoke(data);
        }

        /// <summary>
        /// Request to switch to Charts tab
        /// </summary>
        public void RequestSwitchToCharts()
        {
            OnSwitchToChartsRequested?.Invoke();
        }

        /// <summary>
        /// Notify that a log row was selected (for sync to chart)
        /// </summary>
        public void NotifyLogTimeSelected(DateTime time)
        {
            OnLogTimeSelected?.Invoke(time);
        }

        /// <summary>
        /// Notify that chart cursor moved (for sync to log)
        /// </summary>
        public void NotifyChartTimeSelected(DateTime time)
        {
            OnChartTimeSelected?.Invoke(time);
        }

        /// <summary>
        /// Release the CurrentData reference so stale data isn't picked up
        /// when the Charts tab re-loads after a session switch.
        /// </summary>
        public void ClearCurrentData()
        {
            CurrentData = null;
        }

        /// <summary>
        /// Build chart data package from logs and export preset
        /// </summary>
        public ChartDataPackage BuildDataPackage(
            IEnumerable<LogEntry> logs,
            ExportPreset preset,
            string sessionName,
            IProgress<(double pct, string msg)>? progress = null,
            IProgress<(string signal, string status)>? signalProgress = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AppLogger.Info($"[ChartBuild] Starting BuildDataPackage for '{sessionName}'");

            var package = new ChartDataPackage
            {
                SessionName = sessionName,
                CreatedAt = DateTime.Now,
                Signals = new List<SignalData>(),
                TimeStamps = new List<DateTime>(),
                States = new List<StateData>(),
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>()
            };

            progress?.Report((2, "Preparing log data..."));

            // Logs are already sorted by Date from loading — skip redundant OrderBy
            var sortedLogs = logs as List<LogEntry> ?? logs.ToList();
            if (sortedLogs.Count == 0)
            {
                AppLogger.Info("[ChartBuild] No logs — returning empty package");
                return package;
            }

            AppLogger.Info($"[ChartBuild] {sortedLogs.Count:N0} logs, range: {sortedLogs[0].Date:O} → {sortedLogs[sortedLogs.Count - 1].Date:O}");

            // ── Determine what we need ──────────────────────────────────────
            bool wantIO = preset.SelectedIOComponents?.Count > 0;
            bool wantAxis = preset.SelectedAxisComponents?.Count > 0;
            bool wantCHStep = preset.SelectedCHSteps?.Count > 0;
            bool wantThreads = preset.SelectedThreads?.Count > 0;
            bool wantState = preset.IncludeMachineState;
            bool wantEvents = preset.IncludeEvents;

            HashSet<string>? threadSet = wantThreads
                ? new HashSet<string>(preset.SelectedThreads!, StringComparer.OrdinalIgnoreCase)
                : null;

            progress?.Report((5, "Classifying logs (single pass)..."));

            // ── SINGLE-PASS CLASSIFICATION ──────────────────────────────────
            // Instead of 6 separate full scans (30M+ iterations), classify all
            // logs into pre-filtered lists in ONE pass (~5M iterations total).
            var ioLogs = wantIO ? new List<LogEntry>(sortedLogs.Count / 20) : null;
            var axisLogs = wantAxis ? new List<LogEntry>(sortedLogs.Count / 20) : null;
            var chStepLogs = wantCHStep ? new List<LogEntry>(sortedLogs.Count / 20) : null;
            var threadLogs = wantThreads ? new List<LogEntry>() : null;
            var eventLogs = wantEvents ? new List<LogEntry>() : null;
            var s6StateLogs = wantState ? new List<LogEntry>() : null;
            var s4StateLogs = wantState ? new List<LogEntry>() : null;

            // Build unique timestamps inline (sorted input → adjacent dupes)
            var timestamps = new List<DateTime>(sortedLogs.Count / 2);
            DateTime lastTs = DateTime.MinValue;

            int total = sortedLogs.Count;
            for (int i = 0; i < total; i++)
            {
                var log = sortedLogs[i];

                // Track unique timestamps (sorted → dupes are adjacent)
                if (log.Date != lastTs)
                {
                    timestamps.Add(log.Date);
                    lastTs = log.Date;
                }

                // Classify by message first char
                string msg = log.Message;
                if (!string.IsNullOrEmpty(msg))
                {
                    char fc = msg[0];
                    if (fc == 'I' || fc == 'i')
                    {
                        if (wantIO && (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase) ||
                                       msg.StartsWith("IO:", StringComparison.OrdinalIgnoreCase)))
                            ioLogs!.Add(log);
                    }
                    else if (fc == 'A' || fc == 'a')
                    {
                        if (wantAxis && (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase) ||
                                         msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase)))
                            axisLogs!.Add(log);
                    }
                    else if (fc == 'C' || fc == 'c')
                    {
                        if (wantCHStep && msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                            chStepLogs!.Add(log);
                    }
                    else if (wantState && (fc == 'P' || fc == 'p'))
                    {
                        if (msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) && msg.Contains("->"))
                        {
                            if (log.ThreadName != null && log.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase))
                                s6StateLogs!.Add(log);
                        }
                    }

                    // S4-5 state fallback: "==== STATE" anywhere in message
                    if (wantState && msg.Contains("==== STATE"))
                        s4StateLogs?.Add(log);
                }

                // Events by ThreadName
                if (wantEvents && !string.IsNullOrEmpty(msg) &&
                    string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    eventLogs!.Add(log);

                // Thread messages
                if (wantThreads && !string.IsNullOrEmpty(log.ThreadName) && threadSet!.Contains(log.ThreadName))
                    threadLogs!.Add(log);

                // Report progress every ~64K entries
                if ((i & 0xFFFF) == 0)
                {
                    double pct = 5.0 + ((double)i / total) * 10.0; // 5% → 15%
                    progress?.Report((pct, $"Classifying logs... {i:N0} / {total:N0}"));
                }
            }

            package.TimeStamps = timestamps;
            int dataLength = timestamps.Count;

            AppLogger.Info($"[ChartBuild] Classification done in {sw.Elapsed.TotalSeconds:F1}s: " +
                           $"{dataLength:N0} unique timestamps, " +
                           $"IO={ioLogs?.Count ?? 0}, Axis={axisLogs?.Count ?? 0}, CHStep={chStepLogs?.Count ?? 0}, " +
                           $"Threads={threadLogs?.Count ?? 0}, Events={eventLogs?.Count ?? 0}, " +
                           $"S6State={s6StateLogs?.Count ?? 0}, S4State={s4StateLogs?.Count ?? 0}");

            progress?.Report((16, $"Building time index ({dataLength:N0} timestamps)..."));

            // Build time index lookup (timestamps are unique and sorted, so no dupe check needed)
            var timeIndexLookup = new Dictionary<DateTime, int>(dataLength);
            for (int i = 0; i < dataLength; i++)
                timeIndexLookup[timestamps[i]] = i;

            // ── Parse IO + Axis in PARALLEL (independent data, independent lists) ──
            List<SignalData>? ioSignals = null;
            List<SignalData>? axisSignals = null;

            if (wantIO || wantAxis)
            {
                progress?.Report((20, $"Parsing {(wantIO ? preset.SelectedIOComponents!.Count : 0)} IO + {(wantAxis ? preset.SelectedAxisComponents!.Count : 0)} Axis signals..."));
                AppLogger.Info($"[ChartBuild] Parallel IO+Axis: IO={ioLogs?.Count ?? 0:N0} msgs, Axis={axisLogs?.Count ?? 0:N0} msgs");

                System.Threading.Tasks.Parallel.Invoke(
                    () =>
                    {
                        if (wantIO)
                            ioSignals = ParseIOSignals(ioLogs!, preset.SelectedIOComponents!, dataLength, timeIndexLookup, signalProgress);
                    },
                    () =>
                    {
                        if (wantAxis)
                            axisSignals = ParseAxisSignals(axisLogs!, preset.SelectedAxisComponents!, dataLength, timeIndexLookup, signalProgress);
                    }
                );

                if (ioSignals != null)
                {
                    package.Signals.AddRange(ioSignals);
                    AppLogger.Info($"[ChartBuild] IO result: {ioSignals.Count} signals");
                }
                if (axisSignals != null)
                {
                    package.Signals.AddRange(axisSignals);
                    AppLogger.Info($"[ChartBuild] Axis result: {axisSignals.Count} signals");
                }
            }

            if (wantCHStep)
            {
                progress?.Report((55, $"Parsing {preset.SelectedCHSteps!.Count} CHStep states from {chStepLogs!.Count:N0} msgs..."));
                AppLogger.Info($"[ChartBuild] Parsing CHStep: {preset.SelectedCHSteps!.Count} selected from {chStepLogs!.Count:N0} classified msgs");
                var states = ParseCHStepStates(chStepLogs, preset.SelectedCHSteps, timeIndexLookup);
                package.States.AddRange(states);
                AppLogger.Info($"[ChartBuild] CHStep result: {states.Count} state tracks");
            }

            if (wantThreads)
            {
                progress?.Report((68, $"Parsing {preset.SelectedThreads!.Count} threads from {threadLogs!.Count:N0} msgs..."));
                AppLogger.Info($"[ChartBuild] Parsing Threads: {preset.SelectedThreads!.Count} selected from {threadLogs!.Count:N0} classified msgs");
                var messages = ParseThreadMessages(threadLogs, preset.SelectedThreads, timeIndexLookup);
                package.ThreadMessages.AddRange(messages);
                AppLogger.Info($"[ChartBuild] Thread result: {messages.Count} messages");
            }

            if (wantState)
            {
                progress?.Report((80, "Parsing machine state..."));
                AppLogger.Info("[ChartBuild] Parsing Machine State");
                var machineStates = ParseMachineState(s6StateLogs!, s4StateLogs!, dataLength, timeIndexLookup);
                if (machineStates != null)
                {
                    package.States.Add(machineStates);
                    AppLogger.Info($"[ChartBuild] Machine State: {machineStates.Intervals.Count} intervals");
                }
                else
                {
                    AppLogger.Info("[ChartBuild] Machine State: no transitions found");
                }
            }

            if (wantEvents)
            {
                progress?.Report((90, $"Parsing events from {eventLogs!.Count:N0} msgs..."));
                AppLogger.Info("[ChartBuild] Parsing Events");
                var events = ParseEvents(eventLogs!, timeIndexLookup);
                package.Events.AddRange(events);
                AppLogger.Info($"[ChartBuild] Events: {events.Count} markers");
            }

            sw.Stop();
            var summary = $"{package.Signals.Count} signals, {package.States.Count} states, {package.Events.Count} events, {package.ThreadMessages.Count} msgs";
            progress?.Report((100, $"Done — {summary} ({sw.Elapsed.TotalSeconds:F1}s)"));
            AppLogger.Info($"[ChartBuild] Complete: {summary} — {sw.Elapsed.TotalSeconds:F1}s");

            return package;
        }
    }
}
