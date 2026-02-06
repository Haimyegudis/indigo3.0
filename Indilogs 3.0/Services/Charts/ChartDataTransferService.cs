using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;

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
            string sessionName)
        {
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

            // Group logs by timestamp for time series extraction
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();
            if (!sortedLogs.Any()) return package;

            // Extract timestamps
            package.TimeStamps = sortedLogs.Select(l => l.Date).Distinct().ToList();
            int dataLength = package.TimeStamps.Count;

            // Create time index lookup for fast access
            var timeIndexLookup = new Dictionary<DateTime, int>();
            for (int i = 0; i < package.TimeStamps.Count; i++)
            {
                if (!timeIndexLookup.ContainsKey(package.TimeStamps[i]))
                    timeIndexLookup[package.TimeStamps[i]] = i;
            }

            // Parse IO signals
            if (preset.SelectedIOComponents?.Any() == true)
            {
                var ioSignals = ParseIOSignals(sortedLogs, preset.SelectedIOComponents, dataLength, timeIndexLookup);
                package.Signals.AddRange(ioSignals);
            }

            // Parse Axis signals
            if (preset.SelectedAxisComponents?.Any() == true)
            {
                var axisSignals = ParseAxisSignals(sortedLogs, preset.SelectedAxisComponents, dataLength, timeIndexLookup);
                package.Signals.AddRange(axisSignals);
            }

            // Parse CHStep states (for Gantt visualization)
            if (preset.SelectedCHSteps?.Any() == true)
            {
                var states = ParseCHStepStates(sortedLogs, preset.SelectedCHSteps, timeIndexLookup);
                package.States.AddRange(states);
            }

            // Parse Thread messages
            if (preset.SelectedThreads?.Any() == true)
            {
                var messages = ParseThreadMessages(sortedLogs, preset.SelectedThreads, timeIndexLookup);
                package.ThreadMessages.AddRange(messages);
            }

            // Add Machine State if requested
            if (preset.IncludeMachineState)
            {
                var machineStates = ParseMachineState(sortedLogs, dataLength, timeIndexLookup);
                if (machineStates != null)
                    package.States.Add(machineStates);
            }

            // Parse Events if requested
            if (preset.IncludeEvents)
            {
                var events = ParseEvents(sortedLogs, timeIndexLookup);
                package.Events.AddRange(events);
            }

            return package;
        }

        private List<SignalData> ParseIOSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var signals = new Dictionary<string, SignalData>();
            var selectedSet = new HashSet<string>(selectedComponents, StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;
                if (!log.Message.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    int colonIndex = log.Message.IndexOf(':');
                    if (colonIndex < 0) continue;

                    string content = log.Message.Substring(colonIndex + 1);
                    var parts = content.Split(',');
                    if (parts.Length < 2) continue;

                    string subsystem = parts[0].Trim();

                    for (int i = 1; i < parts.Length; i++)
                    {
                        int eqIndex = parts[i].IndexOf('=');
                        if (eqIndex <= 0) continue;

                        string symbolName = parts[i].Substring(0, eqIndex).Trim();
                        string valueStr = parts[i].Substring(eqIndex + 1).Trim();

                        // Get the component name for selection check (same logic as ExportConfigurationViewModel)
                        // This handles _MotTemp and _DrvTemp suffixes which are stripped in the UI
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

                        // Check if this component is selected (using componentName for selection lookup)
                        string selectionKey = $"{subsystem}|{componentName}";
                        if (!selectedSet.Contains(selectionKey)) continue;

                        // Parse value - handle both numeric and string values
                        string cleanValue = valueStr.Split(' ')[0]; // Remove any suffix after space
                        if (!double.TryParse(cleanValue, out double value)) continue;

                        // Use full signal key (subsystem|symbolName) to keep each signal separate
                        string signalKey = $"{subsystem}|{symbolName}";

                        // Get or create signal
                        if (!signals.TryGetValue(signalKey, out var signal))
                        {
                            // Create display name matching CSV format: Subsystem-ComponentName-Param
                            string displayName = $"{subsystem}-{componentName}-{paramName}";

                            signal = new SignalData
                            {
                                Name = displayName,
                                Category = "IO", // Mark as IO signal
                                SignalType = SignalType.Analog,
                                Data = new double[dataLength]
                            };
                            // Initialize with NaN
                            for (int j = 0; j < dataLength; j++)
                                signal.Data[j] = double.NaN;
                            signals[signalKey] = signal;
                        }

                        // Set value at time index
                        if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                        {
                            signal.Data[idx] = value;
                        }
                    }
                }
                catch { }
            }

            // Forward-fill NaN values
            foreach (var signal in signals.Values)
            {
                ForwardFillNaN(signal.Data);
            }

            return signals.Values.ToList();
        }

        private List<SignalData> ParseAxisSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var signals = new Dictionary<string, SignalData>();
            var selectedSet = new HashSet<string>(selectedComponents, StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;
                if (!log.Message.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase)) continue;

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

                    // Parse position value (usually 3rd part)
                    for (int i = 2; i < parts.Length; i++)
                    {
                        int eqIndex = parts[i].IndexOf('=');
                        if (eqIndex <= 0) continue;

                        string paramName = parts[i].Substring(0, eqIndex).Trim();
                        string valueStr = parts[i].Substring(eqIndex + 1).Trim();

                        if (!double.TryParse(valueStr, out double value)) continue;

                        string signalKey = $"{key}_{paramName}";

                        if (!signals.TryGetValue(signalKey, out var signal))
                        {
                            signal = new SignalData
                            {
                                Name = $"{motor}_{paramName}",
                                Category = "Axis", // Mark as Axis signal
                                SignalType = SignalType.Analog,
                                Data = new double[dataLength]
                            };
                            for (int j = 0; j < dataLength; j++)
                                signal.Data[j] = double.NaN;
                            signals[signalKey] = signal;
                        }

                        if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                        {
                            signal.Data[idx] = value;
                        }
                    }
                }
                catch { }
            }

            foreach (var signal in signals.Values)
            {
                ForwardFillNaN(signal.Data);
            }

            return signals.Values.ToList();
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
                        stateData.Intervals.Add(new StateInterval
                        {
                            StartIndex = idx,
                            EndIndex = idx,
                            StateId = stateNum
                        });
                    }
                }
                catch { }
            }

            // Merge consecutive intervals with same state
            foreach (var stateData in states.Values)
            {
                MergeStateIntervals(stateData.Intervals);
            }

            return states.Values.ToList();
        }

        private StateData ParseMachineState(
            List<LogEntry> logs,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var stateData = new StateData
            {
                Name = "MachineState",
                Category = "PlcMngr",
                Intervals = new List<StateInterval>()
            };

            // Find state transitions from Manager thread with format: PlcMngr: STATE1 -> STATE2
            var transitionLogs = logs.Where(l => l.ThreadName != null &&
                                                 l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message != null &&
                                                 l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message.Contains("->"))
                                     .OrderBy(l => l.Date)
                                     .ToList();

            if (transitionLogs.Count == 0) return null;

            for (int i = 0; i < transitionLogs.Count; i++)
            {
                var currentLog = transitionLogs[i];
                var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length < 2) continue;

                string toStateRaw = parts[1].Trim();

                // Use ChartStateConfig to get the correct state ID for proper coloring
                int stateId = ChartStateConfig.GetId(toStateRaw);

                if (!timeIndexLookup.TryGetValue(currentLog.Date, out int startIdx))
                    continue;

                // Calculate end index
                int endIdx;
                if (i < transitionLogs.Count - 1)
                {
                    if (timeIndexLookup.TryGetValue(transitionLogs[i + 1].Date, out int nextIdx))
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
                catch { }

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
                    current.EndIndex = Math.Max(current.EndIndex, intervals[i].EndIndex);
                }
                else
                {
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
