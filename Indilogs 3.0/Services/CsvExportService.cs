using IndiLogs_3._0.Models;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class CsvExportService
    {
        private readonly string[] _axisParams = new[] { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr" };
        private readonly string[] _chStepParams = new[] { "StepMessage", "SubStepNo", "CHObjType", "PrevStepNo", "DiffTime", "State" };

        // Progress reporting
        public interface IProgress
        {
            void Report(int percentage, string status, string details = "");
            bool IsCancelled { get; }
        }

        public async Task<string> ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset = null)
        {
            if (preset != null)
            {
                return await ExportLogsWithPresetAsync(logs, defaultFileName, preset);
            }

            return await ExportLogsOriginalAsync(logs, defaultFileName);
        }

        private async Task<string> ExportLogsOriginalAsync(IEnumerable<LogEntry> logs, string defaultFileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_CombinedData.csv"
            };

            if (saveFileDialog.ShowDialog() != true) return null;

            string filePath = saveFileDialog.FileName;

            // Show progress window (NON-MODAL)
            var progressWindow = new ExportProgressWindow();
            progressWindow.Show(); // NON-MODAL - allows user to continue working

            var progressReporter = new ProgressReporter(progressWindow);

            // Run export in background - don't block
            _ = Task.Run(async () =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, null, progressReporter);
                    progressWindow.Complete(true, $"Saved to:\n{Path.GetFileName(filePath)}");
                }
                catch (OperationCanceledException)
                {
                    progressWindow.Complete(false, "Export cancelled by user");
                }
                catch (Exception ex)
                {
                    progressWindow.Complete(false, $"Error: {ex.Message}");
                }
            });

            // Return file path immediately - export continues in background
            return filePath;
        }

        private async Task<string> ExportLogsWithPresetAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_Filtered.csv"
            };

            if (saveFileDialog.ShowDialog() != true) return null;

            string filePath = saveFileDialog.FileName;

            // Show progress window (NON-MODAL)
            var progressWindow = new ExportProgressWindow();
            progressWindow.Show(); // NON-MODAL - allows user to continue working

            var progressReporter = new ProgressReporter(progressWindow);

            // Run export in background - don't block
            _ = Task.Run(async () =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, preset, progressReporter);
                    progressWindow.Complete(true, $"Saved to:\n{Path.GetFileName(filePath)}");
                }
                catch (OperationCanceledException)
                {
                    progressWindow.Complete(false, "Export cancelled by user");
                }
                catch (Exception ex)
                {
                    progressWindow.Complete(false, $"Error: {ex.Message}");
                }
            });

            // Return file path immediately - export continues in background
            return filePath;
        }

        // ===================================================================
        // ULTRA-OPTIMIZED PARSING HELPERS - NO REGEX!
        // ===================================================================

        // Fast PlcMngr state parsing
        private static bool TryParsePlcMngrState(string msg, out string stateName)
        {
            stateName = null;
            // CHStep: PlcMngr, STATE_NAME, ...
            // IMPORTANT: PlcMngr must be the CHName, not the Parent!

            if (string.IsNullOrEmpty(msg) || msg.Length < 20) return false;

            // Must start with "CHStep:"
            if (!msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                // Find first comma after "CHStep:"
                int chStepEnd = 7; // "CHStep:".Length
                int firstComma = msg.IndexOf(',', chStepEnd);
                if (firstComma < 0) return false;

                // Extract CHName (between "CHStep:" and first comma)
                string chName = msg.Substring(chStepEnd, firstComma - chStepEnd).Trim();

                // CRITICAL: Only proceed if CHName is "PlcMngr"
                if (!chName.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase)) return false;

                // Find second comma (after state name)
                int secondComma = msg.IndexOf(',', firstComma + 1);
                if (secondComma < 0)
                {
                    // Try to find " State " instead
                    int statePos = msg.IndexOf(" State ", firstComma, StringComparison.OrdinalIgnoreCase);
                    if (statePos > 0)
                        secondComma = statePos;
                    else
                        return false;
                }

                // Extract state name between first and second comma
                stateName = msg.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();

                return !string.IsNullOrEmpty(stateName);
            }
            catch
            {
                return false;
            }
        }

        // Fast CHStep parsing - replaces complex Regex
        private static bool TryParseCHStep(string msg, out string chName, out string stepMessage, out string stateId,
            out string chParentName, out string subsysID, out string prevStepNo, out string diffTime, out string subStepNo, out string chObjType)
        {
            chName = stepMessage = stateId = chParentName = subsysID = prevStepNo = diffTime = subStepNo = chObjType = null;

            // CHStep: CHName, StepMessage, State X <Parent, SubsysID, PrevStepNo, DiffTime, SubStepNo, CHObjType>
            if (msg.Length < 30) return false;

            try
            {
                // Find first comma after "CHStep:"
                int stepIndex = msg.IndexOf("CHStep:", StringComparison.OrdinalIgnoreCase);
                if (stepIndex < 0) return false;

                int startPos = stepIndex + 7; // "CHStep:".Length
                while (startPos < msg.Length && char.IsWhiteSpace(msg[startPos])) startPos++;

                // Extract CHName
                int comma1 = msg.IndexOf(',', startPos);
                if (comma1 < 0) return false;
                chName = msg.Substring(startPos, comma1 - startPos).Trim();

                // Extract StepMessage
                int comma2Start = comma1 + 1;
                while (comma2Start < msg.Length && char.IsWhiteSpace(msg[comma2Start])) comma2Start++;
                int comma2 = msg.IndexOf(',', comma2Start);
                if (comma2 < 0) return false;
                stepMessage = msg.Substring(comma2Start, comma2 - comma2Start).Trim();

                // Extract State
                int stateStart = msg.IndexOf("State ", comma2, StringComparison.OrdinalIgnoreCase);
                if (stateStart < 0) return false;
                stateStart += 6; // "State ".Length
                while (stateStart < msg.Length && char.IsWhiteSpace(msg[stateStart])) stateStart++;

                int stateEnd = stateStart;
                while (stateEnd < msg.Length && char.IsDigit(msg[stateEnd])) stateEnd++;
                if (stateEnd == stateStart) return false;
                stateId = msg.Substring(stateStart, stateEnd - stateStart);

                // Find < >
                int openBracket = msg.IndexOf('<', stateEnd);
                if (openBracket < 0) return false;

                int closeBracket = msg.IndexOf('>', openBracket);
                if (closeBracket < 0) return false;

                // Parse content inside < >
                string bracketContent = msg.Substring(openBracket + 1, closeBracket - openBracket - 1);
                string[] parts = bracketContent.Split(',');
                if (parts.Length < 6) return false;

                chParentName = parts[0].Trim();
                subsysID = parts[1].Trim();
                prevStepNo = parts[2].Trim();
                diffTime = parts[3].Trim();
                subStepNo = parts[4].Trim();
                chObjType = parts[5].Trim();

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Fast LogStats parsing - replaces multiple Regex
        private static bool TryParseLogStats(string msg, out string total, out string isReady, out string semTotal,
            out string semMult, out string lost, out string bufFull, out string maxNum, out string maxCat)
        {
            total = isReady = semTotal = semMult = lost = bufFull = maxNum = maxCat = null;

            // LogStat: Logs(Total=X IsReady=Y) nSemMissed(total=Z Mult=W) Lost=L bufFull=B Max(num=N cat=C)
            if (!msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                // Parse Logs(Total=X IsReady=Y)
                int logsStart = msg.IndexOf("Logs(Total=");
                if (logsStart >= 0)
                {
                    int totalStart = logsStart + 11; // "Logs(Total=".Length
                    int totalEnd = msg.IndexOf(' ', totalStart);
                    if (totalEnd > totalStart)
                        total = msg.Substring(totalStart, totalEnd - totalStart);

                    int isReadyStart = msg.IndexOf("IsReady=", totalEnd);
                    if (isReadyStart > 0)
                    {
                        isReadyStart += 8; // "IsReady=".Length
                        int isReadyEnd = msg.IndexOf(')', isReadyStart);
                        if (isReadyEnd > isReadyStart)
                            isReady = msg.Substring(isReadyStart, isReadyEnd - isReadyStart);
                    }
                }

                // Parse nSemMissed(total=Z Mult=W)
                int semStart = msg.IndexOf("nSemMissed(total=");
                if (semStart >= 0)
                {
                    int semTotalStart = semStart + 17; // "nSemMissed(total=".Length
                    int semTotalEnd = msg.IndexOf(' ', semTotalStart);
                    if (semTotalEnd > semTotalStart)
                        semTotal = msg.Substring(semTotalStart, semTotalEnd - semTotalStart);

                    int multStart = msg.IndexOf("Mult=", semTotalEnd);
                    if (multStart > 0)
                    {
                        multStart += 5; // "Mult=".Length
                        int multEnd = msg.IndexOf(')', multStart);
                        if (multEnd > multStart)
                            semMult = msg.Substring(multStart, multEnd - multStart);
                    }
                }

                // Parse Lost=L
                int lostStart = msg.IndexOf("Lost=");
                if (lostStart >= 0)
                {
                    lostStart += 5; // "Lost=".Length
                    int lostEnd = lostStart;
                    while (lostEnd < msg.Length && char.IsDigit(msg[lostEnd])) lostEnd++;
                    if (lostEnd > lostStart)
                        lost = msg.Substring(lostStart, lostEnd - lostStart);
                }

                // Parse bufFull=B
                int bufFullStart = msg.IndexOf("bufFull=");
                if (bufFullStart >= 0)
                {
                    bufFullStart += 8; // "bufFull=".Length
                    int bufFullEnd = bufFullStart;
                    while (bufFullEnd < msg.Length && char.IsDigit(msg[bufFullEnd])) bufFullEnd++;
                    if (bufFullEnd > bufFullStart)
                        bufFull = msg.Substring(bufFullStart, bufFullEnd - bufFullStart);
                }

                // Parse Max(num=N cat=C)
                int maxStart = msg.IndexOf("Max(num=");
                if (maxStart >= 0)
                {
                    int maxNumStart = maxStart + 8; // "Max(num=".Length
                    int maxNumEnd = msg.IndexOf(' ', maxNumStart);
                    if (maxNumEnd > maxNumStart)
                        maxNum = msg.Substring(maxNumStart, maxNumEnd - maxNumStart);

                    int catStart = msg.IndexOf("cat=", maxNumEnd);
                    if (catStart > 0)
                    {
                        catStart += 4; // "cat=".Length
                        int catEnd = msg.IndexOf(')', catStart);
                        if (catEnd > catStart)
                            maxCat = msg.Substring(catStart, catEnd - catStart);
                    }
                }

                return !string.IsNullOrEmpty(total);
            }
            catch
            {
                return false;
            }
        }
        // ===================================================================
        // ULTRA-OPTIMIZED EXPORT METHOD
        // - NO Regex (replaced with IndexOf)
        // - Forward-fill inline (no huge dictionary)
        // - Streaming write
        // - Progress reporting every 1%
        // ===================================================================

        private void ExportWithForwardFill(IEnumerable<LogEntry> logs, string filePath, ExportPreset preset, IProgress progress = null)
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
                    // Still check for Events and selected threads
                    if (!(preset != null && preset.IncludeEvents && string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase)) &&
                        !(selectedThreads != null && !string.IsNullOrEmpty(log.ThreadName) && selectedThreads.Contains(log.ThreadName)))
                    {
                        continue; // Skip this log entry entirely
                    }
                }

                // A: AxisMon - OPTIMIZED
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
                                    ParseAndAddValue(parts[i], subsys, motor, time, dataMatrix, _axisParams);
                                }
                            }
                        }
                    }
                    catch { }
                }
                // B: IO_Mon - OPTIMIZED
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

                                    string componentKey = $"{rawSub}|{componentName}";

                                    if (selectedIO == null || selectedIO.Contains(componentKey))
                                    {
                                        AddToSchema(schema, subsys, componentName, new[] { paramName });

                                        string key = $"{subsys}|{componentName}|{paramName}";
                                        threadNameMap[key] = threadName;

                                        if (!dataMatrix.ContainsKey(time))
                                            dataMatrix[time] = new Dictionary<string, string>();

                                        dataMatrix[time][key] = cleanValue;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                // C: Machine State - OPTIMIZED (no Regex)
                // Only capture CHStep where PlcMngr is the CHName (not Parent!)
                else if ((preset == null || preset.IncludeMachineState) &&
                         msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParsePlcMngrState(msg, out string stateName))
                    {
                        if (!string.IsNullOrEmpty(stateName))
                        {
                            machineStates[time] = stateName;
                        }
                    }
                }
                // D: CHStep - OPTIMIZED (no Regex)
                else if (msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCHStep(msg, out string chName, out string stepMessage, out string stateId,
                        out string chParentName, out string subsysID, out string prevStepNo, out string diffTime,
                        out string subStepNo, out string chObjType))
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
                                chObjTypeText = chObjType;

                            string subsys = $"CHStep: {chParentName}§{chName}§{subsysID}";
                            string component = "Data";

                            AddToSchema(schema, subsys, component, _chStepParams);

                            if (!dataMatrix.ContainsKey(time))
                                dataMatrix[time] = new Dictionary<string, string>();

                            dataMatrix[time][$"{subsys}|{component}|StepMessage"] = stepMessage;
                            dataMatrix[time][$"{subsys}|{component}|SubStepNo"] = subStepNo;
                            dataMatrix[time][$"{subsys}|{component}|CHObjType"] = chObjTypeText;
                            dataMatrix[time][$"{subsys}|{component}|PrevStepNo"] = prevStepNo;
                            dataMatrix[time][$"{subsys}|{component}|DiffTime"] = diffTime;
                            dataMatrix[time][$"{subsys}|{component}|State"] = stateId;

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
                    if (!threadMessages.ContainsKey(time))
                        threadMessages[time] = new Dictionary<string, string>();

                    threadMessages[time][log.ThreadName] = msg;
                }

                // F: Events
                if ((preset == null || preset.IncludeEvents) &&
                    string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                {
                    if (!threadMessages.ContainsKey(time))
                        threadMessages[time] = new Dictionary<string, string>();

                    threadMessages[time]["Events"] = msg;
                }

                // G: LogStats - OPTIMIZED (no Regex)
                if ((preset == null || preset.IncludeLogStats))
                {
                    string cleanThreadName = threadName?.Trim() ?? "";
                    if (string.Equals(cleanThreadName, "LogStats", StringComparison.OrdinalIgnoreCase) &&
                        msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseLogStats(msg, out string total, out string isReady, out string semTotal,
                            out string semMult, out string lost, out string bufFull, out string maxNum, out string maxCat))
                        {
                            string subsys = "LogStats";
                            string component = "Metrics";

                            var logStatsParams = new[] { "Total", "IsReady", "nSemMissed_total", "nSemMissed_Mult", "Lost", "bufFull", "Max_num", "Max_cat" };
                            AddToSchema(schema, subsys, component, logStatsParams);

                            if (!dataMatrix.ContainsKey(time))
                                dataMatrix[time] = new Dictionary<string, string>();

                            if (!string.IsNullOrEmpty(total))
                                dataMatrix[time][$"{subsys}|{component}|Total"] = total;
                            if (!string.IsNullOrEmpty(isReady))
                                dataMatrix[time][$"{subsys}|{component}|IsReady"] = isReady;
                            if (!string.IsNullOrEmpty(semTotal))
                                dataMatrix[time][$"{subsys}|{component}|nSemMissed_total"] = semTotal;
                            if (!string.IsNullOrEmpty(semMult))
                                dataMatrix[time][$"{subsys}|{component}|nSemMissed_Mult"] = semMult;
                            if (!string.IsNullOrEmpty(lost))
                                dataMatrix[time][$"{subsys}|{component}|Lost"] = lost;
                            if (!string.IsNullOrEmpty(bufFull))
                                dataMatrix[time][$"{subsys}|{component}|bufFull"] = bufFull;
                            if (!string.IsNullOrEmpty(maxNum))
                                dataMatrix[time][$"{subsys}|{component}|Max_num"] = maxNum;
                            if (!string.IsNullOrEmpty(maxCat))
                                dataMatrix[time][$"{subsys}|{component}|Max_cat"] = maxCat;

                            foreach (var param in logStatsParams)
                            {
                                string key = $"{subsys}|{component}|{param}";
                                threadNameMap[key] = threadName;
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
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    MessageBox.Show("No parsable data found.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning)));
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
                            string thread = threadNameMap.ContainsKey(fullKey) ? threadNameMap[fullKey] : "";

                            // Use - as separator between components (Parent-Child-Subsystem)
                            // Keep _ within each component name
                            string hierarchicalHeader = $"{subsysClean.Replace("§", "-")}-{compName}-{param}";

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
                    if (machineStates.ContainsKey(time))
                        lastState = machineStates[time];
                    filledStates[time] = lastState;
                }

                progress?.Report(75, "Writing CSV rows...", $"0 / {totalTimes:N0} rows");

                // Initialize lastValues for forward-fill
                var lastValues = new Dictionary<string, string>();

                int writtenRows = 0;
                lastReportedPercent = 75;

                // Write data rows with inline forward-fill
                foreach (var time in allTimes)
                {
                    if (progress?.IsCancelled == true)
                        throw new OperationCanceledException();

                    writtenRows++;
                    int currentPercent = 75 + (writtenRows * 25 / totalTimes);
                    if (currentPercent > lastReportedPercent)
                    {
                        lastReportedPercent = currentPercent;
                        progress?.Report(currentPercent, "Writing CSV rows...",
                            $"{writtenRows:N0} / {totalTimes:N0} rows");
                    }

                    var rowSb = new StringBuilder();
                    rowSb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff"));

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
                    if (dataMatrix.ContainsKey(time))
                    {
                        foreach (var kvp in dataMatrix[time])
                        {
                            lastValues[kvp.Key] = kvp.Value;
                        }
                    }

                    // Write data columns using forward-filled values
                    foreach (var colKey in orderedKeys)
                    {
                        rowSb.Append(",");
                        if (lastValues.ContainsKey(colKey))
                        {
                            string val = lastValues[colKey];

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
                    if (preset != null && preset.IncludeEvents)
                    {
                        rowSb.Append(",");
                        if (threadMessages.ContainsKey(time) && threadMessages[time].ContainsKey("Events"))
                        {
                            string val = threadMessages[time]["Events"];
                            val = "\"" + val.Replace("\"", "\"\"") + "\"";
                            rowSb.Append(val);
                        }
                    }

                    if (selectedThreads != null)
                    {
                        foreach (var thread in selectedThreads.OrderBy(t => t))
                        {
                            rowSb.Append(",");
                            if (threadMessages.ContainsKey(time) && threadMessages[time].ContainsKey(thread))
                            {
                                string val = threadMessages[time][thread];
                                val = "\"" + val.Replace("\"", "\"\"") + "\"";
                                rowSb.Append(val);
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
            if (!schema.ContainsKey(subsys))
                schema[subsys] = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (!schema[subsys].ContainsKey(component))
                schema[subsys][component] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paramsToAdd)
            {
                schema[subsys][component].Add(p);
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
                    if (!data.ContainsKey(time))
                        data[time] = new Dictionary<string, string>();

                    string uniqueKey = $"{subsys}|{motor}|{key}";
                    data[time][uniqueKey] = val;
                }
            }
        }

        private class ProgressReporter : IProgress
        {
            private readonly ExportProgressWindow _window;

            public ProgressReporter(ExportProgressWindow window)
            {
                _window = window;
            }

            public void Report(int percentage, string status, string details = "")
            {
                _window.UpdateProgress(percentage, status, details);
            }

            public bool IsCancelled => _window.IsCancelled;
        }
    }
}