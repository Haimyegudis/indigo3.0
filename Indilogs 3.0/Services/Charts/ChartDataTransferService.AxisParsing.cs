using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Services.Charts
{
    public partial class ChartDataTransferService
    {
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
