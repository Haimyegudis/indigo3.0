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
        private List<SignalData> ParseIOSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup,
            IProgress<(string signal, string status)>? signalProgress = null)
        {
            // Two-level selection: subsystem → set of component names (no string concat per field)
            var selectionBySubsystem = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in selectedComponents)
            {
                int sep = k.IndexOf('|');
                if (sep > 0)
                {
                    string sub = k.Substring(0, sep);
                    string comp = k.Substring(sep + 1);
                    if (!selectionBySubsystem.TryGetValue(sub, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        selectionBySubsystem[sub] = set;
                    }
                    set.Add(comp);
                }
            }
            AppLogger.Info($"[ChartBuild] ParseIOSignals: {selectedComponents.Count} selected keys");

            // Two-level signal dictionary: subsystem → (cleanSymbol → SignalData)
            var signalsBySubsystem = new Dictionary<string, Dictionary<string, SignalData>>(StringComparer.OrdinalIgnoreCase);
            var stringPool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int ioMatchCount = 0;
            int logCount = logs.Count;
            for (int li = 0; li < logCount; li++)
            {
                string msg = logs[li].Message;
                if (string.IsNullOrEmpty(msg)) continue;

                int contentStart;
                if (msg.Length > 7 && msg[2] == '_') // IO_Mon:
                    contentStart = 7;
                else if (msg.Length > 3 && msg[2] == ':') // IO:
                    contentStart = 3;
                else
                    continue;

                ioMatchCount++;
                bool isIoShort = contentStart == 3;

                int c1 = msg.IndexOf(',', contentStart);
                if (c1 < 0) continue;

                string subsystem = InternString(stringPool, msg, contentStart, c1 - contentStart);

                // Early exit: skip if subsystem not selected
                if (!selectionBySubsystem.TryGetValue(subsystem, out var compSet)) continue;

                // Get/create signal sub-dictionary for this subsystem
                if (!signalsBySubsystem.TryGetValue(subsystem, out var subSignals))
                {
                    subSignals = new Dictionary<string, SignalData>(StringComparer.OrdinalIgnoreCase);
                    signalsBySubsystem[subsystem] = subSignals;
                }

                int fieldStart = c1 + 1;
                int msgLen = msg.Length;

                while (fieldStart < msgLen)
                {
                    int fieldEnd = msg.IndexOf(',', fieldStart);
                    if (fieldEnd < 0) fieldEnd = msgLen;

                    if (isIoShort && fieldStart != c1 + 1) break;

                    int eqPos = msg.IndexOf('=', fieldStart);
                    if (eqPos <= fieldStart || eqPos >= fieldEnd)
                    {
                        fieldStart = fieldEnd + 1;
                        continue;
                    }

                    int symStart = fieldStart;
                    int symEnd = eqPos;
                    while (symStart < symEnd && msg[symStart] == ' ') symStart++;
                    while (symEnd > symStart && msg[symEnd - 1] == ' ') symEnd--;
                    if (symStart >= symEnd) { fieldStart = fieldEnd + 1; continue; }

                    int valStart = eqPos + 1;
                    while (valStart < fieldEnd && msg[valStart] == ' ') valStart++;

                    // Skip "New Status" values
                    if (fieldEnd - valStart >= 10 && msg[valStart] == 'N' &&
                        string.Compare(msg, valStart, "New Status", 0, 10, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        fieldStart = fieldEnd + 1;
                        continue;
                    }

                    int valEnd = msg.IndexOf(' ', valStart);
                    if (valEnd < 0 || valEnd > fieldEnd) valEnd = fieldEnd;

                    if (!TryParseDoubleFast(msg, valStart, valEnd - valStart, out double value))
                    {
                        fieldStart = fieldEnd + 1;
                        continue;
                    }

                    // Build clean symbol name (strip subsystem prefix)
                    int cleanStart = symStart;
                    int cleanLen = symEnd - symStart;
                    int subLen = subsystem.Length;
                    if (cleanLen > subLen &&
                        string.Compare(msg, cleanStart, subsystem, 0, subLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        cleanStart += subLen;
                        while (cleanStart < symEnd && (msg[cleanStart] == '_' || msg[cleanStart] == ' '))
                            cleanStart++;
                        cleanLen = symEnd - cleanStart;
                    }

                    string cleanSymbol = InternString(stringPool, msg, cleanStart, cleanLen);

                    // Determine component name (strip _MotTemp / _DrvTemp suffix)
                    string componentName = cleanSymbol;
                    if (cleanLen > 8)
                    {
                        if (cleanSymbol.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase) ||
                            cleanSymbol.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                            componentName = cleanSymbol.Substring(0, cleanLen - 8);
                    }

                    // Selection check — no string concatenation
                    if (!compSet.Contains(componentName))
                    {
                        fieldStart = fieldEnd + 1;
                        continue;
                    }

                    // Signal lookup — no string concatenation
                    if (!subSignals.TryGetValue(cleanSymbol, out var signal))
                    {
                        signal = new SignalData
                        {
                            Name = cleanSymbol,
                            Category = "IO",
                            SignalType = SignalType.Analog,
                            DataLength = dataLength,
                            SparsePoints = new List<KeyValuePair<int, double>>()
                        };
                        subSignals[cleanSymbol] = signal;
                        signalProgress?.Report((cleanSymbol, "parsing"));
                    }

                    if (timeIndexLookup.TryGetValue(logs[li].Date, out int idx))
                    {
                        signal.SparsePoints!.Add(new KeyValuePair<int, double>(idx, value));
                    }

                    fieldStart = fieldEnd + 1;
                }
            }

            // Collect all signals from two-level dictionary
            var allSignals = new List<SignalData>();
            foreach (var subDict in signalsBySubsystem.Values)
                allSignals.AddRange(subDict.Values);

            AppLogger.Info($"[ChartBuild] ParseIOSignals: {logs.Count:N0} logs, {ioMatchCount} matches, {allSignals.Count} signals");

            return allSignals;
        }

        /// <summary>
        /// Interns a substring — reuses existing string instance if already seen.
        /// Avoids millions of duplicate allocations for repeated subsystem/symbol names.
        /// </summary>
        private static string InternString(Dictionary<string, string> pool, string source, int start, int length)
        {
            string key = source.Substring(start, length).Trim();
            if (pool.TryGetValue(key, out var existing))
                return existing;
            pool[key] = key;
            return key;
        }

        /// <summary>
        /// Fast double parser that works directly on a string range without Substring allocation.
        /// Handles integers, decimals, and negative numbers. Falls back to double.TryParse
        /// for scientific notation.
        /// </summary>
        private static bool TryParseDoubleFast(string s, int start, int length, out double result)
        {
            result = 0;
            if (length <= 0) return false;

            int end = start + length;
            int i = start;

            bool negative = false;
            if (s[i] == '-') { negative = true; i++; }
            else if (s[i] == '+') { i++; }

            if (i >= end) return false;

            long intPart = 0;
            bool hasDigits = false;
            while (i < end && s[i] >= '0' && s[i] <= '9')
            {
                intPart = intPart * 10 + (s[i] - '0');
                hasDigits = true;
                i++;
            }

            double fracPart = 0;
            if (i < end && s[i] == '.')
            {
                i++;
                double multiplier = 0.1;
                while (i < end && s[i] >= '0' && s[i] <= '9')
                {
                    fracPart += (s[i] - '0') * multiplier;
                    multiplier *= 0.1;
                    hasDigits = true;
                    i++;
                }
            }

            if (!hasDigits) return false;

            // Scientific notation fallback
            if (i < end && (s[i] == 'e' || s[i] == 'E'))
                return double.TryParse(s.Substring(start, length), out result);

            if (i != end) return false;

            result = intPart + fracPart;
            if (negative) result = -result;
            return true;
        }

        private List<SignalData> ParseAxisSignals(
            List<LogEntry> logs,
            List<string> selectedComponents,
            int dataLength,
            Dictionary<DateTime, int> timeIndexLookup,
            IProgress<(string signal, string status)>? signalProgress = null)
        {
            // Two-level selection: subsystem → set of motors (no string concat per log)
            var selectionBySubsystem = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in selectedComponents)
            {
                int sep = k.IndexOf('|');
                if (sep > 0)
                {
                    string sub = k.Substring(0, sep);
                    string motor = k.Substring(sep + 1);
                    if (!selectionBySubsystem.TryGetValue(sub, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        selectionBySubsystem[sub] = set;
                    }
                    set.Add(motor);
                }
            }

            // Two-level signal dictionary: (subsystem|motor) → (paramName → SignalData)
            var signalsByMotor = new Dictionary<string, Dictionary<string, SignalData>>(StringComparer.OrdinalIgnoreCase);
            var stringPool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int axisMatchCount = 0;
            int logCount = logs.Count;
            for (int li = 0; li < logCount; li++)
            {
                string msg = logs[li].Message;
                if (string.IsNullOrEmpty(msg)) continue;

                int contentStart;
                bool isAxM;
                if (msg.Length > 8 && msg[4] == 'M' && msg[7] == ':')
                {
                    contentStart = 8;
                    isAxM = false;
                }
                else if (msg.Length > 4 && msg[2] == 'M' && msg[3] == ':')
                {
                    contentStart = 4;
                    isAxM = true;
                }
                else
                    continue;

                axisMatchCount++;
                int msgLen = msg.Length;

                int c1 = msg.IndexOf(',', contentStart);
                if (c1 < 0) continue;
                string subsystem = InternString(stringPool, msg, contentStart, c1 - contentStart);

                int c2 = msg.IndexOf(',', c1 + 1);
                if (c2 < 0) continue;
                string motor = InternString(stringPool, msg, c1 + 1, c2 - c1 - 1);

                // Two-level selection check — no string concatenation
                if (!selectionBySubsystem.TryGetValue(subsystem, out var motorSet)) continue;
                if (!motorSet.Contains(motor)) continue;

                // Motor key only built once per matching log (not per field)
                string motorKey = subsystem + "|" + motor;
                if (!signalsByMotor.TryGetValue(motorKey, out var paramSignals))
                {
                    paramSignals = new Dictionary<string, SignalData>(StringComparer.OrdinalIgnoreCase);
                    signalsByMotor[motorKey] = paramSignals;
                }

                int fieldStart = c2 + 1;
                while (fieldStart < msgLen)
                {
                    int fieldEnd = msg.IndexOf(',', fieldStart);
                    if (fieldEnd < 0) fieldEnd = msgLen;

                    int eqPos = msg.IndexOf('=', fieldStart);

                    string paramName;
                    double value;

                    if (eqPos > fieldStart && eqPos < fieldEnd)
                    {
                        int pStart = fieldStart;
                        int pEnd = eqPos;
                        while (pStart < pEnd && msg[pStart] == ' ') pStart++;
                        while (pEnd > pStart && msg[pEnd - 1] == ' ') pEnd--;

                        int vStart = eqPos + 1;
                        while (vStart < fieldEnd && msg[vStart] == ' ') vStart++;
                        int vEnd = fieldEnd;
                        while (vEnd > vStart && msg[vEnd - 1] == ' ') vEnd--;

                        if (pStart >= pEnd || vStart >= vEnd ||
                            !TryParseDoubleFast(msg, vStart, vEnd - vStart, out value))
                        {
                            fieldStart = fieldEnd + 1;
                            continue;
                        }

                        int pLen = pEnd - pStart;
                        if (pLen == 4 && string.Compare(msg, pStart, "LagE", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                            paramName = "LagErr";
                        else if (pLen == 3 && string.Compare(msg, pStart, "Trg", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
                            paramName = "Trigger";
                        else
                            paramName = InternString(stringPool, msg, pStart, pLen);
                    }
                    else if (isAxM && fieldEnd == msgLen)
                    {
                        int vStart = fieldStart;
                        while (vStart < fieldEnd && msg[vStart] == ' ') vStart++;
                        if (!TryParseDoubleFast(msg, vStart, fieldEnd - vStart, out value))
                        {
                            fieldStart = fieldEnd + 1;
                            continue;
                        }
                        paramName = "Trigger";
                    }
                    else
                    {
                        fieldStart = fieldEnd + 1;
                        continue;
                    }

                    if (!paramSignals.TryGetValue(paramName, out var signal))
                    {
                        string displayName = subsystem + "_" + motor + "_" + paramName;
                        signal = new SignalData
                        {
                            Name = displayName,
                            Category = "Axis",
                            SignalType = SignalType.Analog,
                            DataLength = dataLength,
                            SparsePoints = new List<KeyValuePair<int, double>>()
                        };
                        paramSignals[paramName] = signal;
                        signalProgress?.Report((displayName, "parsing"));
                    }

                    if (timeIndexLookup.TryGetValue(logs[li].Date, out int idx))
                    {
                        signal.SparsePoints!.Add(new KeyValuePair<int, double>(idx, value));
                    }

                    fieldStart = fieldEnd + 1;
                }
            }

            var allSignals = new List<SignalData>();
            foreach (var paramDict in signalsByMotor.Values)
                allSignals.AddRange(paramDict.Values);

            AppLogger.Info($"[ChartBuild] ParseAxisSignals: {logs.Count:N0} logs, {axisMatchCount} matches, {allSignals.Count} signals");

            return allSignals;
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
