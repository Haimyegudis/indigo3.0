using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IndiLogs_3._0.Services
{
    public partial class CsvExportService
    {
        private void ExportWithForwardFill(IEnumerable<LogEntry> logs, string filePath, ExportPreset? preset, IProgress? progress = null)
        {
            progress?.Report(0, "Initializing...", "Preparing data structures");

            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>();

            // Filters
            var selectedIO = preset != null ? new HashSet<string>(preset.SelectedIOComponents, StringComparer.OrdinalIgnoreCase) : null;
            var selectedAxis = preset != null ? new HashSet<string>(preset.SelectedAxisComponents, StringComparer.OrdinalIgnoreCase) : null;
            var selectedCHSteps = preset != null ? new HashSet<string>(preset.SelectedCHSteps, StringComparer.OrdinalIgnoreCase) : null;
            var selectedThreads = preset != null ? new HashSet<string>(preset.SelectedThreads, StringComparer.OrdinalIgnoreCase) : null;

            // Convert to list for counting
            var logList = logs.ToList();
            int totalLogs = logList.Count;
            int processedLogs = 0;
            int lastReportedPercent = 0;

            progress?.Report(5, "Processing logs...", $"0 / {totalLogs:N0} logs");

            // PHASE 1: Parse logs (0-60%) - OPTIMIZED WITH IndexOf
            foreach (var log in logList)
            {
                if (progress?.IsCancelled == true)
                    throw new OperationCanceledException();

                processedLogs++;

                // Report progress every 1%
                int currentPercent = 5 + (processedLogs * 55 / totalLogs);
                if (currentPercent > lastReportedPercent)
                {
                    lastReportedPercent = currentPercent;
                    progress?.Report(currentPercent, "Processing logs...",
                        $"{processedLogs:N0} / {totalLogs:N0} logs ({(double)processedLogs / totalLogs * 100:F1}%)");
                }

                if (string.IsNullOrEmpty(log.Message)) continue;

                string msg = log.Message;
                DateTime time = log.Date;
                string threadName = log.ThreadName ?? "Unknown";

                // Early filtering - skip lines that are definitely not relevant
                // A=AxisMon/AxM, I=IO_Mon/IO, C=CHStep, L=LogStat
                char firstChar = msg.Length > 0 ? msg[0] : ' ';
                bool maybeRelevant = firstChar == 'A' || firstChar == 'I' || firstChar == 'C' ||
                                    firstChar == 'L' || firstChar == 'a' || firstChar == 'i' ||
                                    firstChar == 'c' || firstChar == 'l';

                if (!maybeRelevant)
                {
                    // Still check for Events and selected threads
                    if (!(preset != null && preset.IncludeEvents && string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase)) &&
                        !(selectedThreads != null && !string.IsNullOrEmpty(log.ThreadName) && selectedThreads.Contains(log.ThreadName)))
                    {
                        continue; // Skip this log entry entirely
                    }
                }

                // A: AxisMon - Current pattern
                // AxisMon: SubsysID,MotorID,SetP=val,ActP=val,...,LagErr=val,Trg=trigger
                if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 3)
                        {
                            string rawSub = parts[0].Trim();
                            string motor = parts[1].Trim();
                            string componentKey = $"{rawSub}|{motor}";

                            if (selectedAxis == null || selectedAxis.Contains(componentKey))
                            {
                                string subsys = $"AxisMon: {rawSub}";
                                AddToSchema(schema, subsys, motor, _axisParams);

                                foreach (var param in _axisParams)
                                {
                                    string key = $"{subsys}|{motor}|{param}";
                                    threadNameMap[key] = threadName;
                                }

                                for (int i = 2; i < parts.Length; i++)
                                {
                                    string rawPart = parts[i].Trim();
                                    // Handle Trg=trigger -> store as Trigger
                                    if (rawPart.StartsWith("Trg=", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string trigVal = rawPart.Substring(4).Trim();
                                        if (!dataMatrix.TryGetValue(time, out var trigRow))
                                        {
                                            trigRow = new Dictionary<string, string>();
                                            dataMatrix[time] = trigRow;
                                        }
                                        trigRow[$"{subsys}|{motor}|Trigger"] = trigVal;
                                    }
                                    else
                                    {
                                        ParseAndAddValue(rawPart, subsys, motor, time, dataMatrix, _axisParams);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
                }
                // A2: AxM - Optimized AxisMon pattern (20.01.2026)
                // AxM: SubsysID,MotorID,SetP=val,ActP=val,...,LagE=val,trigger
                else if (msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 3)
                        {
                            string rawSub = parts[0].Trim();
                            string motor = parts[1].Trim();
                            string componentKey = $"{rawSub}|{motor}";

                            if (selectedAxis == null || selectedAxis.Contains(componentKey))
                            {
                                // Store under same schema as AxisMon for unified view
                                string subsys = $"AxisMon: {rawSub}";
                                AddToSchema(schema, subsys, motor, _axisParams);

                                foreach (var param in _axisParams)
                                {
                                    string key = $"{subsys}|{motor}|{param}";
                                    threadNameMap[key] = threadName;
                                }

                                if (!dataMatrix.TryGetValue(time, out var axmRow))
                                {
                                    axmRow = new Dictionary<string, string>();
                                    dataMatrix[time] = axmRow;
                                }

                                for (int i = 2; i < parts.Length; i++)
                                {
                                    string rawPart = parts[i].Trim();

                                    // Handle LagE=val -> store as LagErr
                                    if (rawPart.StartsWith("LagE=", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string lagVal = rawPart.Substring(5).Trim();
                                        axmRow[$"{subsys}|{motor}|LagErr"] = lagVal;
                                    }
                                    else if (rawPart.Contains("="))
                                    {
                                        ParseAndAddValue(rawPart, subsys, motor, time, dataMatrix, _axisParams);
                                    }
                                    else
                                    {
                                        // Last part without = is the trigger value
                                        axmRow[$"{subsys}|{motor}|Trigger"] = rawPart;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
                }
                // B: IO_Mon - Current pattern
                // IO_Mon: SubsytemID, SimbolName=value
                // IO_Mon: SubsytemID, SimbolName= New Status eIoStatus  (status change log)
                else if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            string rawSub = parts[0].Trim();
                            string subsys = $"IO_Mon: {rawSub}";

                            for (int i = 1; i < parts.Length; i++)
                            {
                                string rawPair = parts[i].Trim();
                                int eqIndex = rawPair.IndexOf('=');

                                if (eqIndex > 0)
                                {
                                    string fullSymbolName = rawPair.Substring(0, eqIndex).Trim();
                                    string valueStr = rawPair.Substring(eqIndex + 1).Trim();

                                    // Check if this is a "New Status" log line
                                    // Pattern: IO_Mon: SubsysID, SimbolName= New Status eIoStatus
                                    if (valueStr.StartsWith("New Status", StringComparison.OrdinalIgnoreCase) ||
                                        valueStr.StartsWith(" New Status", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Extract status from "New Status <statusStr>"
                                        string statusPart = valueStr.Replace("New Status", "").Trim();
                                        string ioStatus = DecodeIoStatus(statusPart);

                                        string componentKey = $"{rawSub}|{fullSymbolName}";
                                        if (selectedIO == null || selectedIO.Contains(componentKey))
                                        {
                                            AddToSchema(schema, subsys, fullSymbolName, new[] { "eIoStatus" });

                                            string statusKey = $"{subsys}|{fullSymbolName}|eIoStatus";
                                            threadNameMap[statusKey] = threadName;

                                            if (!dataMatrix.TryGetValue(time, out var ioStatusRow))
                                                dataMatrix[time] = ioStatusRow = new Dictionary<string, string>();

                                            ioStatusRow[statusKey] = ioStatus;
                                        }
                                        continue;
                                    }

                                    string cleanValue = valueStr.Split(' ')[0];

                                    string componentName;
                                    string paramName;

                                    if (fullSymbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                    {
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                        paramName = "MotTemp";
                                    }
                                    else if (fullSymbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                    {
                                        componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                        paramName = "DrvTemp";
                                    }
                                    else
                                    {
                                        componentName = fullSymbolName;
                                        paramName = "Value";
                                    }

                                    string componentKey2 = $"{rawSub}|{componentName}";

                                    if (selectedIO == null || selectedIO.Contains(componentKey2))
                                    {
                                        AddToSchema(schema, subsys, componentName, new[] { paramName });

                                        string key = $"{subsys}|{componentName}|{paramName}";
                                        threadNameMap[key] = threadName;

                                        if (!dataMatrix.TryGetValue(time, out var ioMonRow))
                                            dataMatrix[time] = ioMonRow = new Dictionary<string, string>();

                                        ioMonRow[key] = cleanValue;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
                }
                // B2: IO - Optimized IO_Mon pattern (20.01.2026)
                // IO: SubsytemID,SimbolName=value           (if eIoStatus = Operational)
                // IO: SubsytemID,SimbolName=value,eIoStatus (if eIoStatus != Operational)
                // Also handles MotTemp and DrvTemp suffixes
                else if (msg.StartsWith("IO:", StringComparison.OrdinalIgnoreCase) &&
                         !msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int colonIndex = msg.IndexOf(':');
                        string content = msg.Substring(colonIndex + 1).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            string rawSub = parts[0].Trim();
                            // Store under same schema as IO_Mon for unified view
                            string subsys = $"IO_Mon: {rawSub}";

                            // parts[1] contains SimbolName=value
                            string rawPair = parts[1].Trim();
                            int eqIndex = rawPair.IndexOf('=');

                            if (eqIndex > 0)
                            {
                                string fullSymbolName = rawPair.Substring(0, eqIndex).Trim();
                                string valueStr = rawPair.Substring(eqIndex + 1).Trim();
                                string cleanValue = valueStr.Split(' ')[0];

                                // Check for eIoStatus in parts[2]
                                string? ioStatus = null;
                                if (parts.Length >= 3)
                                {
                                    string statusPart = parts[2].Trim();
                                    // If it doesn't contain '=', it's the eIoStatus
                                    if (!statusPart.Contains("="))
                                    {
                                        ioStatus = DecodeIoStatus(statusPart);
                                    }
                                }

                                string componentName;
                                string paramName;

                                if (fullSymbolName.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                {
                                    componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                    paramName = "MotTemp";
                                }
                                else if (fullSymbolName.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                {
                                    componentName = fullSymbolName.Substring(0, fullSymbolName.Length - 8).Trim();
                                    paramName = "DrvTemp";
                                }
                                else
                                {
                                    componentName = fullSymbolName;
                                    paramName = "Value";
                                }

                                string componentKey = $"{rawSub}|{componentName}";

                                if (selectedIO == null || selectedIO.Contains(componentKey))
                                {
                                    // Add eIoStatus to schema if present
                                    if (ioStatus != null)
                                    {
                                        AddToSchema(schema, subsys, componentName, new[] { paramName, "eIoStatus" });
                                    }
                                    else
                                    {
                                        AddToSchema(schema, subsys, componentName, new[] { paramName });
                                    }

                                    string key = $"{subsys}|{componentName}|{paramName}";
                                    threadNameMap[key] = threadName;

                                    if (!dataMatrix.TryGetValue(time, out var ioRow))
                                        dataMatrix[time] = ioRow = new Dictionary<string, string>();

                                    ioRow[key] = cleanValue;

                                    if (ioStatus != null)
                                    {
                                        string statusKey = $"{subsys}|{componentName}|eIoStatus";
                                        threadNameMap[statusKey] = threadName;
                                        ioRow[statusKey] = ioStatus;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
                }
                // C+D: CHStep messages - handles both Machine State (PlcMngr) and regular CHStep
                else if (msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    // C: Machine State - extract from PlcMngr CHStep
                    if ((preset == null || preset.IncludeMachineState) &&
                        TryParsePlcMngrState(msg, out string? stateName))
                    {
                        if (!string.IsNullOrEmpty(stateName))
                        {
                            machineStates[time] = stateName;
                        }
                    }

                    // D: Regular CHStep export (always try, even for PlcMngr if selected)
                    if (TryParseCHStep(msg, out string? chName, out string? stepMessage, out string? stateId,
                        out string? chParentName, out string? subsysID, out string? prevStepNo, out string? diffTime,
                        out string? subStepNo, out string? chObjType))
                    {
                        string componentKey = $"{chParentName}|{chName}";

                        if (selectedCHSteps == null || selectedCHSteps.Contains(componentKey))
                        {
                            // Convert CHObjType: 0 => action, 1 => component
                            string chObjTypeText;
                            if (chObjType == "0")
                                chObjTypeText = "action";
                            else if (chObjType == "1")
                                chObjTypeText = "component";
                            else
                                chObjTypeText = chObjType ?? "";

                            string subsys = $"CHStep: {chParentName}§{chName}§{subsysID}";
                            string component = "Data";

                            AddToSchema(schema, subsys, component, _chStepParams);

                            if (!dataMatrix.TryGetValue(time, out var chStepRow))
                                dataMatrix[time] = chStepRow = new Dictionary<string, string>();

                            chStepRow[$"{subsys}|{component}|StepMessage"] = stepMessage ?? "";
                            chStepRow[$"{subsys}|{component}|SubStepNo"] = subStepNo ?? "";
                            chStepRow[$"{subsys}|{component}|CHObjType"] = chObjTypeText;
                            chStepRow[$"{subsys}|{component}|PrevStepNo"] = prevStepNo ?? "";
                            chStepRow[$"{subsys}|{component}|DiffTime"] = diffTime ?? "";
                            chStepRow[$"{subsys}|{component}|State"] = stateId ?? "";
                            chStepRow[$"{subsys}|{component}|Parent"] = chParentName ?? "";
                            chStepRow[$"{subsys}|{component}|SubsysID"] = subsysID ?? "";

                            foreach (var param in _chStepParams)
                            {
                                string key = $"{subsys}|{component}|{param}";
                                threadNameMap[key] = threadName;
                            }
                        }
                    }
                }

                // E: Thread Messages
                if (selectedThreads != null && !string.IsNullOrEmpty(log.ThreadName) && selectedThreads.Contains(log.ThreadName))
                {
                    if (!threadMessages.TryGetValue(time, out var threadRow))
                        threadMessages[time] = threadRow = new Dictionary<string, string>();

                    threadRow[log.ThreadName] = msg;
                }

                // F: Events
                if ((preset == null || preset.IncludeEvents) &&
                    string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                {
                    if (!threadMessages.TryGetValue(time, out var eventsRow))
                        threadMessages[time] = eventsRow = new Dictionary<string, string>();

                    eventsRow["Events"] = msg;
                }

                // G: LogStats - OPTIMIZED (no Regex)
                if ((preset == null || preset.IncludeLogStats))
                {
                    string cleanThreadName = threadName?.Trim() ?? "";
                    if (string.Equals(cleanThreadName, "LogStats", StringComparison.OrdinalIgnoreCase) &&
                        msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseLogStats(msg, out string? total, out string? isReady, out string? semTotal,
                            out string? semMult, out string? lost, out string? bufFull, out string? maxNum, out string? maxCat))
                        {
                            string subsys = "LogStats";
                            string component = "Metrics";

                            var logStatsParams = new[] { "Total", "IsReady", "nSemMissed_total", "nSemMissed_Mult", "Lost", "bufFull", "Max_num", "Max_cat" };
                            AddToSchema(schema, subsys, component, logStatsParams);

                            if (!dataMatrix.TryGetValue(time, out var logStatsRow))
                                dataMatrix[time] = logStatsRow = new Dictionary<string, string>();

                            if (!string.IsNullOrEmpty(total))
                                logStatsRow[$"{subsys}|{component}|Total"] = total;
                            if (!string.IsNullOrEmpty(isReady))
                                logStatsRow[$"{subsys}|{component}|IsReady"] = isReady;
                            if (!string.IsNullOrEmpty(semTotal))
                                logStatsRow[$"{subsys}|{component}|nSemMissed_total"] = semTotal;
                            if (!string.IsNullOrEmpty(semMult))
                                logStatsRow[$"{subsys}|{component}|nSemMissed_Mult"] = semMult;
                            if (!string.IsNullOrEmpty(lost))
                                logStatsRow[$"{subsys}|{component}|Lost"] = lost;
                            if (!string.IsNullOrEmpty(bufFull))
                                logStatsRow[$"{subsys}|{component}|bufFull"] = bufFull;
                            if (!string.IsNullOrEmpty(maxNum))
                                logStatsRow[$"{subsys}|{component}|Max_num"] = maxNum;
                            if (!string.IsNullOrEmpty(maxCat))
                                logStatsRow[$"{subsys}|{component}|Max_cat"] = maxCat;

                            foreach (var param in logStatsParams)
                            {
                                string key = $"{subsys}|{component}|{param}";
                                threadNameMap[key] = threadName!;
                            }
                        }
                    }
                }
            }

            if (progress?.IsCancelled == true)
                throw new OperationCanceledException();

            // PHASE 2: Build CSV structure (60-70%)
            progress?.Report(60, "Building CSV structure...", "Creating column headers");

            if (schema.Count == 0 && machineStates.Count == 0 && threadMessages.Count == 0)
            {
                progress?.Report(100, "No data found", "No parsable data in logs");
                _dispatcher?.Post(() =>
                    _dialogService?.ShowWarning("No parsable data found.", "Export"));
                return;
            }

            var orderedKeys = new List<string>();

            // Build header and write to file with streaming
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8, 65536))
            {
                var headerSb = new StringBuilder();
                headerSb.Append("Time");

                if (preset != null && preset.IncludeUnixTime)
                    headerSb.Append(",Unix_Time");

                if (preset == null || preset.IncludeMachineState)
                    headerSb.Append(",Machine_State");

                foreach (var subEntry in schema)
                {
                    string subsysClean = subEntry.Key
                        .Replace("AxisMon: ", "")
                        .Replace("IO_Mon: ", "")
                        .Replace("CHStep: ", "");

                    foreach (var compEntry in subEntry.Value)
                    {
                        string compName = compEntry.Key;

                        foreach (var param in compEntry.Value)
                        {
                            string fullKey = $"{subEntry.Key}|{compName}|{param}";
                            string thread = threadNameMap.TryGetValue(fullKey, out var tName) ? tName : "";

                            // Use - as separator between components
                            // Keep § for CHStep columns so import can detect them
                            string hierarchicalHeader = $"{subsysClean}-{compName}-{param}";

                            if (!string.IsNullOrEmpty(thread))
                                hierarchicalHeader += $" [{thread}]";

                            headerSb.Append($",{hierarchicalHeader}");
                            orderedKeys.Add(fullKey);
                        }
                    }
                }

                if (preset != null && preset.IncludeEvents)
                    headerSb.Append(",Events_Message");

                if (selectedThreads != null)
                {
                    foreach (var thread in selectedThreads.OrderBy(t => t))
                    {
                        headerSb.Append($",{thread}_Message");
                    }
                }

                writer.WriteLine(headerSb.ToString());

                if (progress?.IsCancelled == true)
                    throw new OperationCanceledException();

                // PHASE 3 & 4 COMBINED: Forward-fill + Write (70-100%)
                progress?.Report(70, "Preparing time series...", "Forward-filling Machine States");

                var allTimes = new SortedSet<DateTime>();
                foreach (var t in dataMatrix.Keys) allTimes.Add(t);
                foreach (var t in machineStates.Keys) allTimes.Add(t);
                foreach (var t in threadMessages.Keys) allTimes.Add(t);

                int totalTimes = allTimes.Count;

                // Forward-fill Machine States
                var filledStates = new Dictionary<DateTime, string>();
                string lastState = "";
                foreach (var time in allTimes)
                {
                    if (machineStates.TryGetValue(time, out var state))
                        lastState = state;
                    filledStates[time] = lastState;
                }

                progress?.Report(75, "Writing CSV rows...", $"0 / {totalTimes:N0} rows");

                // Initialize lastValues for forward-fill
                var lastValues = new Dictionary<string, string>();

                int writtenRows = 0;
                lastReportedPercent = 75;

                // Pre-allocate StringBuilder for better performance
                var rowSb = new StringBuilder(orderedKeys.Count * 12 + 100);

                // Write data rows with inline forward-fill
                foreach (var time in allTimes)
                {
                    if (progress?.IsCancelled == true)
                        throw new OperationCanceledException();

                    writtenRows++;
                    if ((writtenRows & 0xFF) == 0) // Report progress every 256 rows instead of every row
                    {
                        int currentPercent = 75 + (writtenRows * 25 / totalTimes);
                        if (currentPercent > lastReportedPercent)
                        {
                            lastReportedPercent = currentPercent;
                            progress?.Report(currentPercent, "Writing CSV rows...",
                                $"{writtenRows:N0} / {totalTimes:N0} rows");
                        }
                    }

                    rowSb.Clear();
                    rowSb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));

                    if (preset != null && preset.IncludeUnixTime)
                    {
                        long unixTime = ((DateTimeOffset)time).ToUnixTimeMilliseconds();
                        rowSb.Append($",{unixTime}");
                    }

                    if (preset == null || preset.IncludeMachineState)
                    {
                        rowSb.Append($",{filledStates[time]}");
                    }

                    // Forward-fill inline: update lastValues if we have new data for this time
                    Dictionary<string, string>? timeData;
                    if (dataMatrix.TryGetValue(time, out timeData))
                    {
                        foreach (var kvp in timeData)
                        {
                            lastValues[kvp.Key] = kvp.Value;
                        }
                    }

                    // Write data columns using forward-filled values
                    string? val;
                    foreach (var colKey in orderedKeys)
                    {
                        rowSb.Append(",");
                        if (lastValues.TryGetValue(colKey, out val))
                        {

                            bool isWarning = false;
                            if (colKey.Contains("LogStats") && colKey.Contains("|Metrics|"))
                            {
                                if ((colKey.EndsWith("|nSemMissed_Mult") || colKey.EndsWith("|Lost") || colKey.EndsWith("|bufFull"))
                                    && val != "0")
                                {
                                    isWarning = true;
                                }
                            }

                            if (isWarning)
                            {
                                val = $"[!] {val}";
                            }

                            if (val.Contains(",") || val.Contains("\""))
                            {
                                val = "\"" + val.Replace("\"", "\"\"") + "\"";
                            }
                            rowSb.Append(val);
                        }
                    }

                    // Thread messages
                    Dictionary<string, string>? threadMsgs;
                    if (preset != null && preset.IncludeEvents)
                    {
                        rowSb.Append(",");
                        if (threadMessages.TryGetValue(time, out threadMsgs))
                        {
                            string? evtVal;
                            if (threadMsgs.TryGetValue("Events", out evtVal))
                            {
                                evtVal = "\"" + evtVal.Replace("\"", "\"\"") + "\"";
                                rowSb.Append(evtVal);
                            }
                        }
                    }

                    if (selectedThreads != null)
                    {
                        // Note: selectedThreads already pre-sorted in header section
                        foreach (var thread in selectedThreads.OrderBy(t => t))
                        {
                            rowSb.Append(",");
                            if (threadMessages.TryGetValue(time, out threadMsgs))
                            {
                                string? tVal;
                                if (threadMsgs.TryGetValue(thread, out tVal))
                                {
                                    tVal = "\"" + tVal.Replace("\"", "\"\"") + "\"";
                                    rowSb.Append(tVal);
                                }
                            }
                        }
                    }

                    writer.WriteLine(rowSb.ToString());
                }

                // Report success
                int exportedRows = allTimes.Count;
                progress?.Report(100, "Export Complete!", $"Saved {exportedRows:N0} rows to:\n{Path.GetFileName(filePath)}");
            }
        }

        private void AddToSchema(SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
                                 string subsys, string component, IEnumerable<string> paramsToAdd)
        {
            if (!schema.TryGetValue(subsys, out var compDict))
                schema[subsys] = compDict = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (!compDict.TryGetValue(component, out var paramSet))
                compDict[component] = paramSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paramsToAdd)
            {
                paramSet.Add(p);
            }
        }

        private void ParseAndAddValue(string rawPart, string subsys, string motor, DateTime time,
                                      SortedDictionary<DateTime, Dictionary<string, string>> data,
                                      string[] validParams)
        {
            int eqIndex = rawPart.IndexOf('=');
            if (eqIndex > 0)
            {
                string key = rawPart.Substring(0, eqIndex).Trim();
                string val = rawPart.Substring(eqIndex + 1).Trim();

                if (validParams.Contains(key))
                {
                    if (!data.TryGetValue(time, out var dataRow))
                        data[time] = dataRow = new Dictionary<string, string>();

                    string uniqueKey = $"{subsys}|{motor}|{key}";
                    dataRow[uniqueKey] = val;
                }
            }
        }
    }
}
