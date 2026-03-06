using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;

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
                char firstChar = msg.Length > 0 ? msg[0] : ' ';
                bool maybeRelevant = firstChar == 'A' || firstChar == 'I' || firstChar == 'C' ||
                                    firstChar == 'L' || firstChar == 'a' || firstChar == 'i' ||
                                    firstChar == 'c' || firstChar == 'l';

                if (!maybeRelevant)
                {
                    if (!(preset != null && preset.IncludeEvents && string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase)) &&
                        !(selectedThreads != null && !string.IsNullOrEmpty(log.ThreadName) && selectedThreads.Contains(log.ThreadName)))
                    {
                        continue;
                    }
                }

                // A: AxisMon
                if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseAxisMon(msg, threadName, time, selectedAxis, schema, dataMatrix, threadNameMap);
                }
                // A2: AxM - Optimized AxisMon pattern
                else if (msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseAxM(msg, threadName, time, selectedAxis, schema, dataMatrix, threadNameMap);
                }
                // B: IO_Mon
                else if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseIOMon(msg, threadName, time, selectedIO, schema, dataMatrix, threadNameMap);
                }
                // B2: IO - Optimized IO_Mon pattern
                else if (msg.StartsWith("IO:", StringComparison.OrdinalIgnoreCase) &&
                         !msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseIO(msg, threadName, time, selectedIO, schema, dataMatrix, threadNameMap);
                }
                // C+D: CHStep messages
                else if (msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseCHStepExport(msg, threadName, time, preset, selectedCHSteps,
                        schema, dataMatrix, machineStates, threadNameMap);
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
                if (preset == null || preset.IncludeLogStats)
                {
                    ParseLogStats(msg, threadName, time, schema, dataMatrix, threadNameMap);
                }
            }

            if (progress?.IsCancelled == true)
                throw new OperationCanceledException();

            WriteForwardFilledCsv(filePath, preset, progress, schema, dataMatrix, machineStates,
                threadMessages, threadNameMap, selectedThreads);
        }

        private void ParseCHStepExport(string msg, string threadName, DateTime time,
            ExportPreset? preset, HashSet<string>? selectedCHSteps,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            SortedDictionary<DateTime, string> machineStates,
            Dictionary<string, string> threadNameMap)
        {
            // C: Machine State - extract from PlcMngr CHStep
            if ((preset == null || preset.IncludeMachineState) &&
                TryParsePlcMngrState(msg, out string? stateName))
            {
                if (!string.IsNullOrEmpty(stateName))
                    machineStates[time] = stateName;
            }

            // D: Regular CHStep export
            if (TryParseCHStep(msg, out string? chName, out string? stepMessage, out string? stateId,
                out string? chParentName, out string? subsysID, out string? prevStepNo, out string? diffTime,
                out string? subStepNo, out string? chObjType))
            {
                string componentKey = $"{chParentName}|{chName}";

                if (selectedCHSteps == null || selectedCHSteps.Contains(componentKey))
                {
                    string chObjTypeText;
                    if (chObjType == "0") chObjTypeText = "action";
                    else if (chObjType == "1") chObjTypeText = "component";
                    else chObjTypeText = chObjType ?? "";

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

        private void ParseLogStats(string msg, string threadName, DateTime time,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
        {
            string cleanThreadName = threadName?.Trim() ?? "";
            if (!string.Equals(cleanThreadName, "LogStats", StringComparison.OrdinalIgnoreCase) ||
                !msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase))
                return;

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
