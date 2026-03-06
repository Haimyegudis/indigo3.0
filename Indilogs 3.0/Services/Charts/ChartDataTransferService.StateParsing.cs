using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Services.Charts
{
    public partial class ChartDataTransferService
    {
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



        /// <summary>
        /// Parses machine state from pre-filtered log lists (no full-scan needed).
        /// S6 transitions: PlcMngr: STATE1 -> STATE2 (already filtered during classification).
        /// S4-5 fallback: ==== STATE_XXX - Enter (already filtered during classification).
        /// </summary>
        private StateData? ParseMachineState(
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
                    var match = AppConstants.S4StateRegex.Match(log.Message);
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
}
