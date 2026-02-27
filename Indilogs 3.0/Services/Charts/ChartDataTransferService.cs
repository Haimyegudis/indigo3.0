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
    public class ChartDataTransferService
    {
        private static ChartDataTransferService _instance;
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
        public event Action<ChartDataPackage> OnDataReady;

        /// <summary>
        /// Event fired when user requests to switch to Charts tab
        /// </summary>
        public event Action OnSwitchToChartsRequested;

        /// <summary>
        /// Event fired when log selection changes (for Log -> Chart sync)
        /// </summary>
        public event Action<DateTime> OnLogTimeSelected;

        /// <summary>
        /// Event fired when chart cursor moves (for Chart -> Log sync)
        /// </summary>
        public event Action<DateTime> OnChartTimeSelected;

        /// <summary>
        /// Current data package available for charts
        /// </summary>
        public ChartDataPackage CurrentData { get; private set; }

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
        /// Build chart data package from logs and export preset
        /// </summary>
        public ChartDataPackage BuildDataPackage(
            IEnumerable<LogEntry> logs,
            ExportPreset preset,
            string sessionName,
            IProgress<(double pct, string msg)> progress = null,
            IProgress<(string signal, string status)> signalProgress = null)
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

            HashSet<string> threadSet = wantThreads
                ? new HashSet<string>(preset.SelectedThreads, StringComparer.OrdinalIgnoreCase)
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
                            ioLogs.Add(log);
                    }
                    else if (fc == 'A' || fc == 'a')
                    {
                        if (wantAxis && (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase) ||
                                         msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase)))
                            axisLogs.Add(log);
                    }
                    else if (fc == 'C' || fc == 'c')
                    {
                        if (wantCHStep && msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                            chStepLogs.Add(log);
                    }
                    else if (wantState && (fc == 'P' || fc == 'p'))
                    {
                        if (msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) && msg.Contains("->"))
                        {
                            if (log.ThreadName != null && log.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase))
                                s6StateLogs.Add(log);
                        }
                    }

                    // S4-5 state fallback: "==== STATE" anywhere in message
                    if (wantState && msg.Contains("==== STATE"))
                        s4StateLogs?.Add(log);
                }

                // Events by ThreadName
                if (wantEvents && !string.IsNullOrEmpty(msg) &&
                    string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    eventLogs.Add(log);

                // Thread messages
                if (wantThreads && !string.IsNullOrEmpty(log.ThreadName) && threadSet.Contains(log.ThreadName))
                    threadLogs.Add(log);

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

            // Build time index lookup
            var timeIndexLookup = new Dictionary<DateTime, int>(dataLength);
            for (int i = 0; i < timestamps.Count; i++)
            {
                if (!timeIndexLookup.ContainsKey(timestamps[i]))
                    timeIndexLookup[timestamps[i]] = i;
            }

            // ── Parse IO + Axis in PARALLEL (independent data, independent lists) ──
            List<SignalData> ioSignals = null;
            List<SignalData> axisSignals = null;

            if (wantIO || wantAxis)
            {
                progress?.Report((20, $"Parsing {(wantIO ? preset.SelectedIOComponents.Count : 0)} IO + {(wantAxis ? preset.SelectedAxisComponents.Count : 0)} Axis signals..."));
                AppLogger.Info($"[ChartBuild] Parallel IO+Axis: IO={ioLogs?.Count ?? 0:N0} msgs, Axis={axisLogs?.Count ?? 0:N0} msgs");

                System.Threading.Tasks.Parallel.Invoke(
                    () =>
                    {
                        if (wantIO)
                            ioSignals = ParseIOSignals(ioLogs, preset.SelectedIOComponents, dataLength, timeIndexLookup, signalProgress);
                    },
                    () =>
                    {
                        if (wantAxis)
                            axisSignals = ParseAxisSignals(axisLogs, preset.SelectedAxisComponents, dataLength, timeIndexLookup, signalProgress);
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
                progress?.Report((55, $"Parsing {preset.SelectedCHSteps.Count} CHStep states from {chStepLogs.Count:N0} msgs..."));
                AppLogger.Info($"[ChartBuild] Parsing CHStep: {preset.SelectedCHSteps.Count} selected from {chStepLogs.Count:N0} classified msgs");
                var states = ParseCHStepStates(chStepLogs, preset.SelectedCHSteps, timeIndexLookup);
                package.States.AddRange(states);
                AppLogger.Info($"[ChartBuild] CHStep result: {states.Count} state tracks");
            }

            if (wantThreads)
            {
                progress?.Report((68, $"Parsing {preset.SelectedThreads.Count} threads from {threadLogs.Count:N0} msgs..."));
                AppLogger.Info($"[ChartBuild] Parsing Threads: {preset.SelectedThreads.Count} selected from {threadLogs.Count:N0} classified msgs");
                var messages = ParseThreadMessages(threadLogs, preset.SelectedThreads, timeIndexLookup);
                package.ThreadMessages.AddRange(messages);
                AppLogger.Info($"[ChartBuild] Thread result: {messages.Count} messages");
            }

            if (wantState)
            {
                progress?.Report((80, "Parsing machine state..."));
                AppLogger.Info("[ChartBuild] Parsing Machine State");
                var machineStates = ParseMachineState(s6StateLogs, s4StateLogs, dataLength, timeIndexLookup);
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
                progress?.Report((90, $"Parsing events from {eventLogs.Count:N0} msgs..."));
                AppLogger.Info("[ChartBuild] Parsing Events");
                var events = ParseEvents(eventLogs, timeIndexLookup);
                package.Events.AddRange(events);
                AppLogger.Info($"[ChartBuild] Events: {events.Count} markers");
            }

            sw.Stop();
            var summary = $"{package.Signals.Count} signals, {package.States.Count} states, {package.Events.Count} events, {package.ThreadMessages.Count} msgs";
            progress?.Report((100, $"Done — {summary} ({sw.Elapsed.TotalSeconds:F1}s)"));
            AppLogger.Info($"[ChartBuild] Complete: {summary} — {sw.Elapsed.TotalSeconds:F1}s");

            return package;
        }

        private List<SignalData> ParseIOSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup,
            IProgress<(string signal, string status)> signalProgress = null)
        {
            var signals = new Dictionary<string, SignalData>();
            var selectedSet = new HashSet<string>(selectedComponents, StringComparer.OrdinalIgnoreCase);
            AppLogger.Info($"[ChartBuild] ParseIOSignals: {selectedSet.Count} selected keys");
            int ioMatchCount = 0;

            // NaN template: allocate once, Array.Copy for each new signal (fast memcpy)
            var nanTemplate = new double[dataLength];
            for (int j = 0; j < dataLength; j++) nanTemplate[j] = double.NaN;

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                char fc = log.Message[0];
                if (fc != 'I' && fc != 'i') continue;

                bool isIoMon = log.Message.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase);
                bool isIoOpt = !isIoMon && log.Message.StartsWith("IO:", StringComparison.OrdinalIgnoreCase);
                if (!isIoMon && !isIoOpt) continue;
                ioMatchCount++;

                try
                {
                    int colonIndex = log.Message.IndexOf(':');
                    if (colonIndex < 0) continue;

                    string content = log.Message.Substring(colonIndex + 1);
                    var parts = content.Split(',');
                    if (parts.Length < 2) continue;

                    string subsystem = parts[0].Trim();
                    int dataEnd = isIoOpt ? Math.Min(2, parts.Length) : parts.Length;

                    for (int i = 1; i < dataEnd; i++)
                    {
                        int eqIndex = parts[i].IndexOf('=');
                        if (eqIndex <= 0) continue;

                        string symbolName = parts[i].Substring(0, eqIndex).Trim();
                        string valueStr = parts[i].Substring(eqIndex + 1).Trim();

                        if (valueStr.StartsWith("New Status", StringComparison.OrdinalIgnoreCase)) continue;

                        string componentName;
                        string paramName;
                        if (symbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                        {
                            componentName = symbolName.Substring(0, symbolName.Length - 8);
                            paramName = "MotTemp";
                        }
                        else if (symbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                        {
                            componentName = symbolName.Substring(0, symbolName.Length - 8);
                            paramName = "DrvTemp";
                        }
                        else
                        {
                            componentName = symbolName;
                            paramName = "Value";
                        }

                        string selectionKey = $"{subsystem}|{componentName}";
                        if (!selectedSet.Contains(selectionKey)) continue;

                        string cleanValue = valueStr.Split(' ')[0];
                        if (!double.TryParse(cleanValue, out double value)) continue;

                        string signalKey = $"{subsystem}|{symbolName}";

                        if (!signals.TryGetValue(signalKey, out var signal))
                        {
                            string displayName = $"{subsystem}-{componentName}-{paramName}";

                            signal = new SignalData
                            {
                                Name = displayName,
                                Category = "IO",
                                SignalType = SignalType.Analog,
                                Data = new double[dataLength]
                            };
                            Array.Copy(nanTemplate, signal.Data, dataLength); // fast memcpy instead of loop
                            signals[signalKey] = signal;
                            signalProgress?.Report((displayName, "parsing"));
                        }

                        if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                        {
                            signal.Data[idx] = value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ParseIOSignals message parsing failed", ex);
                }
            }

            // Parallel forward-fill — each signal is independent
            var signalValues = signals.Values.ToList();
            System.Threading.Tasks.Parallel.ForEach(signalValues,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                signal =>
                {
                    ForwardFillNaN(signal.Data);
                    signalProgress?.Report((signal.Name, "done"));
                });

            AppLogger.Info($"[ChartBuild] ParseIOSignals: {logs.Count:N0} logs, {ioMatchCount} matches, {signals.Count} signals");

            return signalValues;
        }

        private List<SignalData> ParseAxisSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup,
            IProgress<(string signal, string status)> signalProgress = null)
        {
            var signals = new Dictionary<string, SignalData>();
            var selectedSet = new HashSet<string>(selectedComponents, StringComparer.OrdinalIgnoreCase);
            int axisMatchCount = 0;

            // NaN template: allocate once, Array.Copy for each new signal
            var nanTemplate = new double[dataLength];
            for (int j = 0; j < dataLength; j++) nanTemplate[j] = double.NaN;

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                char fc = log.Message[0];
                if (fc != 'A' && fc != 'a') continue;

                bool isAxisMon = log.Message.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase);
                bool isAxM = !isAxisMon && log.Message.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase);
                if (!isAxisMon && !isAxM) continue;
                axisMatchCount++;

                try
                {
                    int colonIndex = log.Message.IndexOf(':');
                    if (colonIndex < 0) continue;

                    string content = log.Message.Substring(colonIndex + 1);
                    var parts = content.Split(',');
                    if (parts.Length < 3) continue;

                    string subsystem = parts[0].Trim();
                    string motor = parts[1].Trim();
                    string key = $"{subsystem}|{motor}";

                    if (!selectedSet.Contains(key)) continue;

                    for (int i = 2; i < parts.Length; i++)
                    {
                        string rawPart = parts[i].Trim();
                        int eqIndex = rawPart.IndexOf('=');

                        string paramName;
                        string valueStr;

                        if (eqIndex > 0)
                        {
                            paramName = rawPart.Substring(0, eqIndex).Trim();
                            valueStr = rawPart.Substring(eqIndex + 1).Trim();

                            if (paramName.Equals("LagE", StringComparison.OrdinalIgnoreCase))
                                paramName = "LagErr";
                            else if (paramName.Equals("Trg", StringComparison.OrdinalIgnoreCase))
                                paramName = "Trigger";
                        }
                        else if (isAxM && i == parts.Length - 1)
                        {
                            paramName = "Trigger";
                            valueStr = rawPart;
                        }
                        else
                        {
                            continue;
                        }

                        if (!double.TryParse(valueStr, out double value)) continue;

                        string signalKey = $"{key}_{paramName}";

                        if (!signals.TryGetValue(signalKey, out var signal))
                        {
                            signal = new SignalData
                            {
                                Name = $"{motor}_{paramName}",
                                Category = "Axis",
                                SignalType = SignalType.Analog,
                                Data = new double[dataLength]
                            };
                            Array.Copy(nanTemplate, signal.Data, dataLength); // fast memcpy
                            signals[signalKey] = signal;
                            signalProgress?.Report(($"{motor}_{paramName}", "parsing"));
                        }

                        if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                        {
                            signal.Data[idx] = value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ParseAxisSignals message parsing failed", ex);
                }
            }

            // Parallel forward-fill — each signal is independent
            var signalValues = signals.Values.ToList();
            System.Threading.Tasks.Parallel.ForEach(signalValues,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                signal =>
                {
                    ForwardFillNaN(signal.Data);
                    signalProgress?.Report((signal.Name, "done"));
                });

            AppLogger.Info($"[ChartBuild] ParseAxisSignals: {logs.Count:N0} logs, {axisMatchCount} matches, {signals.Count} signals");

            return signalValues;
        }

        private List<StateData> ParseCHStepStates(
            List<LogEntry> logs,
            List<string> selectedComponents,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var states = new Dictionary<string, StateData>();
            var selectedSet = new HashSet<string>(selectedComponents, StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                // Fast first-char filter
                char fc = log.Message[0];
                if (fc != 'C' && fc != 'c') continue;
                if (!log.Message.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    // Parse CHStep message
                    int firstComma = log.Message.IndexOf(',', 7);
                    if (firstComma < 0) continue;

                    string chName = log.Message.Substring(7, firstComma - 7).Trim();

                    int statePos = log.Message.IndexOf("State ", firstComma, StringComparison.OrdinalIgnoreCase);
                    if (statePos < 0) continue;

                    int openBracket = log.Message.IndexOf('<', statePos);
                    if (openBracket < 0) continue;

                    int nextComma = log.Message.IndexOf(',', openBracket);
                    if (nextComma < 0) continue;

                    string chParentName = log.Message.Substring(openBracket + 1, nextComma - openBracket - 1).Trim();

                    // Extract state number
                    int stateStart = statePos + 6;
                    int stateEnd = log.Message.IndexOf(' ', stateStart);
                    if (stateEnd < 0) stateEnd = openBracket;

                    string stateNumStr = log.Message.Substring(stateStart, stateEnd - stateStart).Trim();
                    if (!int.TryParse(stateNumStr, out int stateNum)) continue;

                    string key = $"{chParentName}|{chName}";
                    if (!selectedSet.Contains(key)) continue;

                    if (!states.TryGetValue(key, out var stateData))
                    {
                        stateData = new StateData
                        {
                            Name = chName,
                            Category = chParentName,
                            Intervals = new List<StateInterval>()
                        };
                        states[key] = stateData;
                    }

                    if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                    {
                        // Extract StepMessage and full bracket data for rich tooltip
                        string stepMessage = "";
                        string subsysID = "", prevStepNo = "", diffTime = "", subStepNo = "", chObjType = "";

                        try
                        {
                            // Extract StepMessage (between first and second comma)
                            int secondComma = log.Message.IndexOf(',', firstComma + 1);
                            if (secondComma > firstComma + 1)
                                stepMessage = log.Message.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();

                            // Extract bracket content <Parent, SubsysID, PrevStepNo, DiffTime, SubStepNo, CHObjType>
                            int closeBracket = log.Message.IndexOf('>', openBracket);
                            if (closeBracket > openBracket)
                            {
                                string bracketContent = log.Message.Substring(openBracket + 1, closeBracket - openBracket - 1);
                                string[] parts = bracketContent.Split(',');
                                if (parts.Length >= 6)
                                {
                                    subsysID = parts[1].Trim();
                                    prevStepNo = parts[2].Trim();
                                    diffTime = parts[3].Trim();
                                    subStepNo = parts[4].Trim();
                                    chObjType = parts[5].Trim();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Extracting CHStep bracket data failed", ex);
                        }

                        // Build rich tooltip
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"CHStep: {chName}");
                        if (!string.IsNullOrEmpty(stepMessage))
                            sb.AppendLine($"Step: {stepMessage}");
                        sb.AppendLine($"State: {stateNum}");
                        sb.AppendLine($"Parent: {chParentName}");
                        if (!string.IsNullOrEmpty(subsysID))
                            sb.AppendLine($"SubsysID: {subsysID}");
                        if (!string.IsNullOrEmpty(prevStepNo))
                            sb.AppendLine($"PrevStepNo: {prevStepNo}");
                        if (!string.IsNullOrEmpty(diffTime))
                            sb.AppendLine($"DiffTime: {diffTime}");
                        if (!string.IsNullOrEmpty(subStepNo))
                            sb.AppendLine($"SubStepNo: {subStepNo}");
                        if (!string.IsNullOrEmpty(chObjType))
                        {
                            string objTypeText = chObjType == "0" ? "action" : (chObjType == "1" ? "component" : chObjType);
                            sb.AppendLine($"CHObjType: {objTypeText}");
                        }

                        stateData.Intervals.Add(new StateInterval
                        {
                            StartIndex = idx,
                            EndIndex = idx,
                            StateId = stateNum,
                            StateName = stepMessage,
                            TooltipText = sb.ToString().TrimEnd()
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ParseCHStepStates message parsing failed", ex);
                }
            }

            // Merge consecutive intervals with same state
            foreach (var stateData in states.Values)
            {
                MergeStateIntervals(stateData.Intervals);
            }

            return states.Values.ToList();
        }

        /// <summary>Regex for S4-5 binary PLC state transitions: "STATE_XXX - Enter/Exit ======"</summary>
        private static readonly System.Text.RegularExpressions.Regex _s4StateRegex =
            new System.Text.RegularExpressions.Regex(
                @"STATE_(\w+)\s*-\s*(Enter|Exit)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled,
                AppConstants.RegexTimeout);

        /// <summary>
        /// Parses machine state from pre-filtered log lists (no full-scan needed).
        /// S6 transitions: PlcMngr: STATE1 -> STATE2 (already filtered during classification).
        /// S4-5 fallback: ==== STATE_XXX - Enter (already filtered during classification).
        /// </summary>
        private StateData ParseMachineState(
            List<LogEntry> s6TransitionLogs,
            List<LogEntry> s4StateLogs,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var stateData = new StateData
            {
                Name = "MachineState",
                Category = "PlcMngr",
                Intervals = new List<StateInterval>()
            };

            // ── S6 path: pre-filtered PlcMngr transitions with -> ──
            if (s6TransitionLogs != null && s6TransitionLogs.Count > 0)
            {
                // Already in chronological order from single-pass classification
                for (int i = 0; i < s6TransitionLogs.Count; i++)
                {
                    var currentLog = s6TransitionLogs[i];
                    var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    string toStateRaw = parts[1].Trim();
                    int stateId = ChartStateConfig.GetId(toStateRaw);

                    if (!timeIndexLookup.TryGetValue(currentLog.Date, out int startIdx))
                        continue;

                    int endIdx;
                    if (i < s6TransitionLogs.Count - 1)
                    {
                        if (timeIndexLookup.TryGetValue(s6TransitionLogs[i + 1].Date, out int nextIdx))
                            endIdx = nextIdx - 1;
                        else
                            endIdx = startIdx;
                    }
                    else
                    {
                        endIdx = dataLength - 1;
                    }

                    stateData.Intervals.Add(new StateInterval
                    {
                        StartIndex = startIdx,
                        EndIndex = Math.Max(startIdx, endIdx),
                        StateId = stateId,
                        StateName = toStateRaw
                    });
                }
            }

            // ── S4-5 fallback if no S6 transitions found ──
            if (stateData.Intervals.Count == 0 && s4StateLogs != null && s4StateLogs.Count > 0)
            {
                // Use only "Enter" logs as transition points (like S6's "-> STATE")
                var enterLogs = new List<(LogEntry Log, string StateName)>();
                foreach (var log in s4StateLogs)
                {
                    var match = _s4StateRegex.Match(log.Message);
                    if (match.Success && match.Groups[2].Value.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                    {
                        enterLogs.Add((log, match.Groups[1].Value.ToUpperInvariant()));
                    }
                }

                for (int i = 0; i < enterLogs.Count; i++)
                {
                    var (currentLog, stateName) = enterLogs[i];
                    int stateId = ChartStateConfig.GetId(stateName);

                    if (!timeIndexLookup.TryGetValue(currentLog.Date, out int startIdx))
                        continue;

                    int endIdx;
                    if (i < enterLogs.Count - 1)
                    {
                        if (timeIndexLookup.TryGetValue(enterLogs[i + 1].Log.Date, out int nextIdx))
                            endIdx = nextIdx - 1;
                        else
                            endIdx = startIdx;
                    }
                    else
                    {
                        endIdx = dataLength - 1;
                    }

                    stateData.Intervals.Add(new StateInterval
                    {
                        StartIndex = startIdx,
                        EndIndex = Math.Max(startIdx, endIdx),
                        StateId = stateId,
                        StateName = stateName
                    });
                }
            }

            if (stateData.Intervals.Count == 0) return null;

            return stateData;
        }

        private List<ThreadMessageData> ParseThreadMessages(
            List<LogEntry> logs,
            List<string> selectedThreads,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var messages = new List<ThreadMessageData>();
            var selectedSet = new HashSet<string>(selectedThreads, StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.ThreadName)) continue;
                if (!selectedSet.Contains(log.ThreadName)) continue;

                if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                {
                    messages.Add(new ThreadMessageData
                    {
                        TimeIndex = idx,
                        ThreadName = log.ThreadName,
                        Message = log.Message ?? "",
                        TimeStamp = log.Date
                    });
                }
            }

            return messages;
        }

        /// <summary>
        /// Parse Events from logs (ThreadName = "Events")
        /// Format: "Enqueue event EVENT_NAME from SUBSYSTEM ParamName=Value [Severity]"
        /// </summary>
        private List<EventMarkerData> ParseEvents(
            List<LogEntry> logs,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var events = new List<EventMarkerData>();

            foreach (var log in logs)
            {
                // Events are identified by ThreadName = "Events"
                if (!string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(log.Message)) continue;

                if (!timeIndexLookup.TryGetValue(log.Date, out int idx))
                    continue;

                // Parse event message: "Enqueue event EVENT_NAME from SUBSYSTEM ..."
                string eventName = "";
                string severity = "";
                string state = "";
                string description = "";
                string parameters = "";

                try
                {
                    string msg = log.Message;

                    // Try to extract event name
                    int eventStart = msg.IndexOf("event ", StringComparison.OrdinalIgnoreCase);
                    if (eventStart >= 0)
                    {
                        eventStart += 6; // "event ".Length
                        int eventEnd = msg.IndexOf(' ', eventStart);
                        if (eventEnd > eventStart)
                            eventName = msg.Substring(eventStart, eventEnd - eventStart);
                        else
                            eventName = msg.Substring(eventStart);
                    }
                    else
                    {
                        eventName = msg.Length > 50 ? msg.Substring(0, 50) + "..." : msg;
                    }

                    // Try to extract "from SUBSYSTEM"
                    int fromIdx = msg.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                    if (fromIdx > 0)
                    {
                        int subStart = fromIdx + 6;
                        int subEnd = msg.IndexOf(' ', subStart);
                        if (subEnd > subStart)
                            description = msg.Substring(subStart, subEnd - subStart);
                        else
                            description = msg.Substring(subStart);
                    }

                    // Try to extract severity [...]
                    int bracketStart = msg.LastIndexOf('[');
                    int bracketEnd = msg.LastIndexOf(']');
                    if (bracketStart > 0 && bracketEnd > bracketStart)
                    {
                        severity = msg.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                    }

                    // Extract state from event name if it contains STATE keywords
                    if (eventName.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        state = eventName;
                    }

                    // Everything after "from X" is parameters
                    if (fromIdx > 0)
                    {
                        int paramsStart = msg.IndexOf(' ', fromIdx + 6);
                        if (paramsStart > 0 && paramsStart < msg.Length - 1)
                        {
                            parameters = msg.Substring(paramsStart + 1).Trim();
                            // Remove the [Severity] part
                            if (bracketStart > paramsStart)
                                parameters = parameters.Substring(0, bracketStart - paramsStart - 1).Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ParseEvents message parsing failed", ex);
                }

                events.Add(new EventMarkerData
                {
                    TimeIndex = idx,
                    TimeStamp = log.Date,
                    Name = eventName,
                    State = state,
                    Severity = severity,
                    Description = description,
                    Parameters = parameters
                });
            }

            return events;
        }

        private void ForwardFillNaN(double[] data)
        {
            double lastValue = double.NaN;
            for (int i = 0; i < data.Length; i++)
            {
                if (double.IsNaN(data[i]))
                {
                    data[i] = lastValue;
                }
                else
                {
                    lastValue = data[i];
                }
            }
        }

        private void MergeStateIntervals(List<StateInterval> intervals)
        {
            if (intervals.Count < 2) return;

            intervals.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));

            var merged = new List<StateInterval>();
            var current = intervals[0];

            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].StateId == current.StateId &&
                    intervals[i].StartIndex <= current.EndIndex + 1)
                {
                    // Merge consecutive same-state intervals
                    current.EndIndex = Math.Max(current.EndIndex, intervals[i].EndIndex);
                    // Preserve tooltip from whichever interval has it
                    if (string.IsNullOrEmpty(current.TooltipText) && !string.IsNullOrEmpty(intervals[i].TooltipText))
                    {
                        current.TooltipText = intervals[i].TooltipText;
                        current.StateName = intervals[i].StateName;
                    }
                }
                else
                {
                    // Different state - fill gap and start new interval
                    current.EndIndex = intervals[i].StartIndex - 1;
                    merged.Add(current);
                    current = intervals[i];
                }
            }
            merged.Add(current);

            intervals.Clear();
            intervals.AddRange(merged);
        }
    }

    /// <summary>
    /// Package containing all chart data for In-Memory transfer
    /// </summary>
    public class ChartDataPackage
    {
        public string SessionName { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<DateTime> TimeStamps { get; set; }
        public List<SignalData> Signals { get; set; }
        public List<StateData> States { get; set; }
        public List<ThreadMessageData> ThreadMessages { get; set; }
        public List<EventMarkerData> Events { get; set; }
        /// <summary>When true, ChartTabControl skips gap detection (used for IO terminal data).</summary>
        public bool SuppressGapDetection { get; set; }
    }

    /// <summary>
    /// Event marker for overlay display on charts (red markers)
    /// </summary>
    public class EventMarkerData
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
        public string Parameters { get; set; }
        public DateTime TimeStamp { get; set; }
        public int TimeIndex { get; set; }
    }

    /// <summary>
    /// Signal data for charting
    /// </summary>
    public class SignalData
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public SignalType SignalType { get; set; }
        public double[] Data { get; set; }
    }

    /// <summary>
    /// State data for Gantt visualization
    /// </summary>
    public class StateData
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public List<StateInterval> Intervals { get; set; }
    }

    /// <summary>
    /// Thread message for overlay markers
    /// </summary>
    public class ThreadMessageData
    {
        public int TimeIndex { get; set; }
        public string ThreadName { get; set; }
        public string Message { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    public enum SignalType
    {
        Analog,
        Digital,
        State
    }
}
