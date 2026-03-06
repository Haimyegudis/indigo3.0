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

    }
}
