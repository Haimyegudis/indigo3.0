using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Charts
{
    internal static class PatternChartService
    {
        public static ChartDataPackage BuildFromPattern(
            IList<LogEntry> logs,
            string regexPattern,
            string chartName,
            PatternSearchField searchField,
            bool includeMachineState,
            string sessionName)
        {
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled,
                AppConstants.RegexTimeout);

            // Collect unique timestamps and time index
            var timestamps = new List<DateTime>(logs.Count / 2);
            var timeIndex = new Dictionary<DateTime, int>(logs.Count / 2);
            DateTime lastTs = DateTime.MinValue;

            for (int i = 0; i < logs.Count; i++)
            {
                var dt = logs[i].Date;
                if (dt != lastTs)
                {
                    timeIndex[dt] = timestamps.Count;
                    timestamps.Add(dt);
                    lastTs = dt;
                }
            }

            int dataLength = timestamps.Count;

            // Extract signal values
            var sparsePoints = new List<KeyValuePair<int, double>>();
            bool? isBoolean = null;

            // Also collect state transition logs if requested
            var s6StateLogs = includeMachineState ? new List<LogEntry>() : null;
            var s4StateLogs = includeMachineState ? new List<LogEntry>() : null;

            for (int i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                string fieldValue = searchField switch
                {
                    PatternSearchField.Logger => log.Logger ?? "",
                    PatternSearchField.ThreadName => log.ThreadName ?? "",
                    _ => log.Message ?? ""
                };

                if (string.IsNullOrEmpty(fieldValue)) goto checkState;

                try
                {
                    var match = regex.Match(fieldValue);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        string captured = match.Groups[1].Value;
                        if (!timeIndex.TryGetValue(log.Date, out int idx)) goto checkState;

                        if (double.TryParse(captured, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                        {
                            sparsePoints.Add(new KeyValuePair<int, double>(idx, val));
                            if (isBoolean == null) isBoolean = false;
                        }
                        else if (bool.TryParse(captured, out bool boolVal))
                        {
                            sparsePoints.Add(new KeyValuePair<int, double>(idx, boolVal ? 1.0 : 0.0));
                            if (isBoolean == null) isBoolean = true;
                        }
                    }
                }
                catch (RegexMatchTimeoutException) { }

                checkState:
                if (!includeMachineState) continue;
                string msg = log.Message ?? "";
                if (msg.Length > 0)
                {
                    if (msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) && msg.Contains("->"))
                    {
                        if (string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase))
                            s6StateLogs!.Add(log);
                    }
                    if (msg.Contains("==== STATE"))
                        s4StateLogs!.Add(log);
                }
            }

            // Build signal
            var signal = new SignalData
            {
                Name = chartName,
                Category = "Pattern",
                SignalType = isBoolean == true ? SignalType.Digital : SignalType.Analog,
                DataLength = dataLength,
                SparsePoints = sparsePoints
            };

            var package = new ChartDataPackage
            {
                SessionName = sessionName,
                CreatedAt = DateTime.Now,
                TimeStamps = timestamps,
                Signals = new List<SignalData> { signal },
                States = new List<StateData>(),
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>()
            };

            // Parse machine state
            if (includeMachineState)
            {
                var machineState = ParseMachineState(s6StateLogs!, s4StateLogs!, dataLength, timeIndex);
                if (machineState != null)
                    package.States.Add(machineState);
            }

            AppLogger.Info($"[PatternChart] Built '{chartName}': {sparsePoints.Count} data points, " +
                           $"{dataLength} timestamps, type={signal.SignalType}");

            return package;
        }

        private static StateData? ParseMachineState(
            List<LogEntry> s6Logs, List<LogEntry> s4Logs,
            int dataLength, Dictionary<DateTime, int> timeIndex)
        {
            var stateData = new StateData
            {
                Name = "MachineState",
                Category = "PlcMngr",
                Intervals = new List<StateInterval>()
            };

            if (s6Logs.Count > 0)
            {
                for (int i = 0; i < s6Logs.Count; i++)
                {
                    var parts = s6Logs[i].Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    string toState = parts[1].Trim();
                    int stateId = ChartStateConfig.GetId(toState);

                    if (!timeIndex.TryGetValue(s6Logs[i].Date, out int startIdx)) continue;

                    int endIdx = i < s6Logs.Count - 1 && timeIndex.TryGetValue(s6Logs[i + 1].Date, out int nextIdx)
                        ? nextIdx - 1
                        : dataLength - 1;

                    stateData.Intervals.Add(new StateInterval
                    {
                        StartIndex = startIdx,
                        EndIndex = Math.Max(startIdx, endIdx),
                        StateId = stateId,
                        StateName = toState
                    });
                }
            }

            if (stateData.Intervals.Count == 0 && s4Logs.Count > 0)
            {
                var enterLogs = new List<(LogEntry Log, string StateName)>();
                foreach (var log in s4Logs)
                {
                    var match = AppConstants.S4StateRegex().Match(log.Message);
                    if (match.Success && match.Groups[2].Value.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                        enterLogs.Add((log, match.Groups[1].Value.ToUpperInvariant()));
                }

                for (int i = 0; i < enterLogs.Count; i++)
                {
                    var (log, stateName) = enterLogs[i];
                    int stateId = ChartStateConfig.GetId(stateName);
                    if (!timeIndex.TryGetValue(log.Date, out int startIdx)) continue;

                    int endIdx = i < enterLogs.Count - 1 && timeIndex.TryGetValue(enterLogs[i + 1].Log.Date, out int nextIdx)
                        ? nextIdx - 1
                        : dataLength - 1;

                    stateData.Intervals.Add(new StateInterval
                    {
                        StartIndex = startIdx,
                        EndIndex = Math.Max(startIdx, endIdx),
                        StateId = stateId,
                        StateName = stateName
                    });
                }
            }

            return stateData.Intervals.Count > 0 ? stateData : null;
        }
    }
}
