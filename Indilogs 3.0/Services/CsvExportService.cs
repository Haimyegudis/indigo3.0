using IndiLogs_3._0.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class CsvExportService
    {
        private readonly string[] _axisParams = new[] { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr" };
        private readonly string[] _chStepParams = new[] { "CHObjType", "CHParentName", "DiffTime", "PrevStepNo", "State", "StepMessage", "SubStepNo", "SubsysID" };

        public async Task ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset = null)
        {
            if (preset != null)
            {
                await ExportLogsWithPresetAsync(logs, defaultFileName, preset);
                return;
            }

            await ExportLogsOriginalAsync(logs, defaultFileName);
        }

        private async Task ExportLogsOriginalAsync(IEnumerable<LogEntry> logs, string defaultFileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_CombinedData.csv"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            string filePath = saveFileDialog.FileName;

            await Task.Run(() =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, null);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Failed: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private async Task ExportLogsWithPresetAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_Filtered.csv"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            string filePath = saveFileDialog.FileName;

            await Task.Run(() =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, preset);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Failed: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private void ExportWithForwardFill(IEnumerable<LogEntry> logs, string filePath, ExportPreset preset)
        {
            var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
            var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var machineStates = new SortedDictionary<DateTime, string>();
            var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();
            var threadNameMap = new Dictionary<string, string>(); // key -> thread name

            // Filters (if preset exists)
            var selectedIO = preset != null ? new HashSet<string>(preset.SelectedIOComponents, StringComparer.OrdinalIgnoreCase) : null;
            var selectedAxis = preset != null ? new HashSet<string>(preset.SelectedAxisComponents, StringComparer.OrdinalIgnoreCase) : null;
            var selectedCHSteps = preset != null ? new HashSet<string>(preset.SelectedCHSteps, StringComparer.OrdinalIgnoreCase) : null;
            var selectedThreads = preset != null ? new HashSet<string>(preset.SelectedThreads, StringComparer.OrdinalIgnoreCase) : null;

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.Message)) continue;

                string msg = log.Message.Trim();
                DateTime time = log.Date;
                string threadName = log.ThreadName ?? "Unknown";

                // A: AxisMon
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

                            // Check if selected (if filtering)
                            if (selectedAxis == null || selectedAxis.Contains(componentKey))
                            {
                                string subsys = $"AxisMon: {rawSub}";

                                AddToSchema(schema, subsys, motor, _axisParams);

                                // Store thread name
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
                // B: IO_Mon
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

                                    // Check if selected (if filtering)
                                    if (selectedIO == null || selectedIO.Contains(componentKey))
                                    {
                                        AddToSchema(schema, subsys, componentName, new[] { paramName });

                                        // Store thread name
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
                // C: Machine State (forward-fill) - from PlcMngr CHStep
                else if ((preset == null || preset.IncludeMachineState) &&
                         msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase) &&
                         msg.Contains("PlcMngr,"))
                {
                    try
                    {
                        var match = Regex.Match(msg, @"CHStep:\s*PlcMngr,\s*([^,]+),");
                        if (match.Success)
                        {
                            string stateName = match.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(stateName))
                            {
                                machineStates[time] = stateName;
                            }
                        }
                    }
                    catch { }
                }
                // D: CHStep - ALL CHSteps
                else if (msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var match = Regex.Match(msg, @"CHStep:\s*([^,]+),\s*([^,]*),\s*State\s+(\d+)\s*<([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,>]+)");

                        if (match.Success)
                        {
                            string chName = match.Groups[1].Value.Trim();
                            string stepMessage = match.Groups[2].Value.Trim();
                            string stateId = match.Groups[3].Value.Trim();
                            string chParentName = match.Groups[4].Value.Trim();
                            string subsysID = match.Groups[5].Value.Trim();
                            string prevStepNo = match.Groups[6].Value.Trim();
                            string diffTime = match.Groups[7].Value.Trim();
                            string subStepNo = match.Groups[8].Value.Trim();
                            string chObjTypeRaw = match.Groups[9].Value.Trim();

                            string componentKey = $"{chParentName}|{chName}";

                            // Check if selected (if filtering)
                            if (selectedCHSteps == null || selectedCHSteps.Contains(componentKey))
                            {
                                // Normalize CHObjType: 1 => action, 0 => component
                                string chObjType;
                                if (chObjTypeRaw == "1")
                                    chObjType = "action";
                                else if (chObjTypeRaw == "0")
                                    chObjType = "component";
                                else
                                    chObjType = chObjTypeRaw;

                                // Subsystem = CHParentName_CHName (flattened)
                                string subsys = $"CHStep: {chParentName}_{chName}";

                                // Component = "Data" (single entry point)
                                string component = "Data";

                                AddToSchema(schema, subsys, component, _chStepParams);

                                if (!dataMatrix.ContainsKey(time))
                                    dataMatrix[time] = new Dictionary<string, string>();

                                dataMatrix[time][$"{subsys}|{component}|CHObjType"] = chObjType;
                                dataMatrix[time][$"{subsys}|{component}|CHParentName"] = chParentName;
                                dataMatrix[time][$"{subsys}|{component}|DiffTime"] = diffTime;
                                dataMatrix[time][$"{subsys}|{component}|PrevStepNo"] = prevStepNo;
                                dataMatrix[time][$"{subsys}|{component}|State"] = stateId;
                                dataMatrix[time][$"{subsys}|{component}|StepMessage"] = stepMessage;
                                dataMatrix[time][$"{subsys}|{component}|SubStepNo"] = subStepNo;
                                dataMatrix[time][$"{subsys}|{component}|SubsysID"] = subsysID;

                                // Store thread name
                                foreach (var param in _chStepParams)
                                {
                                    string key = $"{subsys}|{component}|{param}";
                                    threadNameMap[key] = threadName;
                                }
                            }
                        }
                    }
                    catch { }
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

                // G: LogStats parsing
                string cleanThreadName = threadName?.Trim() ?? "";
                if (string.Equals(cleanThreadName, "LogStats", StringComparison.OrdinalIgnoreCase) &&
                    msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Pattern: LogStat: Logs(Total=X IsReady=Y) nSemMissed(total=Z Mult=W) Lost=L bufFull=B Max(num=N cat=C)
                        var logsMatch = Regex.Match(msg, @"Logs\(Total=(\d+)\s+IsReady=(\d+)\)");
                        var semMatch = Regex.Match(msg, @"nSemMissed\(total=(\d+)\s+Mult=(\d+)\)");
                        var lostMatch = Regex.Match(msg, @"Lost=(\d+)");
                        var bufFullMatch = Regex.Match(msg, @"bufFull=(\d+)");
                        var maxMatch = Regex.Match(msg, @"Max\(num=(\d+)\s+cat=([^)]+)\)");

                        if (logsMatch.Success)
                        {
                            string subsys = "LogStats";
                            string component = "Metrics";

                            var logStatsParams = new[] { "Total", "IsReady", "nSemMissed_total", "nSemMissed_Mult", "Lost", "bufFull", "Max_num", "Max_cat" };
                            AddToSchema(schema, subsys, component, logStatsParams);

                            if (!dataMatrix.ContainsKey(time))
                                dataMatrix[time] = new Dictionary<string, string>();

                            dataMatrix[time][$"{subsys}|{component}|Total"] = logsMatch.Groups[1].Value;
                            dataMatrix[time][$"{subsys}|{component}|IsReady"] = logsMatch.Groups[2].Value;

                            if (semMatch.Success)
                            {
                                dataMatrix[time][$"{subsys}|{component}|nSemMissed_total"] = semMatch.Groups[1].Value;
                                dataMatrix[time][$"{subsys}|{component}|nSemMissed_Mult"] = semMatch.Groups[2].Value;
                            }

                            if (lostMatch.Success)
                                dataMatrix[time][$"{subsys}|{component}|Lost"] = lostMatch.Groups[1].Value;

                            if (bufFullMatch.Success)
                                dataMatrix[time][$"{subsys}|{component}|bufFull"] = bufFullMatch.Groups[1].Value;

                            if (maxMatch.Success)
                            {
                                dataMatrix[time][$"{subsys}|{component}|Max_num"] = maxMatch.Groups[1].Value;
                                dataMatrix[time][$"{subsys}|{component}|Max_cat"] = maxMatch.Groups[2].Value;
                            }

                            // Store thread name
                            foreach (var param in logStatsParams)
                            {
                                string key = $"{subsys}|{component}|{param}";
                                threadNameMap[key] = threadName;
                            }
                        }
                    }
                    catch { }
                }
            }

            // === Build CSV ===
            if (schema.Count == 0 && machineStates.Count == 0 && threadMessages.Count == 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show("No parsable data found.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            // Debug: Check if LogStats was parsed
            bool hasLogStats = schema.ContainsKey("LogStats");

            var sb = new StringBuilder();
            var orderedKeys = new List<string>();

            // Header: Time, Unix_Time (if preset), Machine_State, Hierarchical columns with ThreadName
            sb.Append("Time");

            if (preset != null && preset.IncludeUnixTime)
                sb.Append(",Unix_Time");

            if (preset == null || preset.IncludeMachineState)
                sb.Append(",Machine_State");

            foreach (var subEntry in schema)
            {
                string subsysClean = subEntry.Key
                    .Replace("AxisMon: ", "")
                    .Replace("IO_Mon: ", "")
                    .Replace("CHStep: ", "");

                foreach (var compEntry in subEntry.Value)
                {
                    string compName = compEntry.Key;
                    bool isCHStep = subEntry.Key.StartsWith("CHStep:");

                    foreach (var param in compEntry.Value)
                    {
                        string fullKey = $"{subEntry.Key}|{compName}|{param}";
                        string thread = threadNameMap.ContainsKey(fullKey) ? threadNameMap[fullKey] : "";

                        // Hierarchical header with thread
                        string hierarchicalHeader;
                        if (isCHStep)
                        {
                            // CHStep format: CHParent_CHName_Data_Param [Thread]
                            hierarchicalHeader = $"{subsysClean}_{compName}_{param}";
                        }
                        else
                        {
                            // IO/AXIS format: Subsystem_Component_Param [Thread]
                            hierarchicalHeader = $"{subsysClean}_{compName}_{param}";
                        }

                        if (!string.IsNullOrEmpty(thread))
                            hierarchicalHeader += $" [{thread}]";

                        sb.Append($",{hierarchicalHeader}");

                        orderedKeys.Add(fullKey);
                    }
                }
            }

            // Thread columns
            if (preset != null && preset.IncludeEvents)
                sb.Append(",Events_Message");

            if (selectedThreads != null)
            {
                foreach (var thread in selectedThreads.OrderBy(t => t))
                {
                    sb.Append($",{thread}_Message");
                }
            }

            sb.AppendLine();

            // === FORWARD-FILL ALL DATA ===
            var allTimes = new SortedSet<DateTime>();
            foreach (var t in dataMatrix.Keys) allTimes.Add(t);
            foreach (var t in machineStates.Keys) allTimes.Add(t);
            foreach (var t in threadMessages.Keys) allTimes.Add(t);

            // Forward-fill Machine States
            var filledStates = new Dictionary<DateTime, string>();
            string lastState = "";
            foreach (var time in allTimes)
            {
                if (machineStates.ContainsKey(time))
                    lastState = machineStates[time];
                filledStates[time] = lastState;
            }

            // Forward-fill ALL data columns
            var filledData = new Dictionary<DateTime, Dictionary<string, string>>();
            var lastValues = new Dictionary<string, string>();

            foreach (var time in allTimes)
            {
                filledData[time] = new Dictionary<string, string>();

                foreach (var colKey in orderedKeys)
                {
                    if (dataMatrix.ContainsKey(time) && dataMatrix[time].ContainsKey(colKey))
                    {
                        // New value - update last value
                        lastValues[colKey] = dataMatrix[time][colKey];
                    }

                    // Use last known value (forward-fill)
                    if (lastValues.ContainsKey(colKey))
                    {
                        filledData[time][colKey] = lastValues[colKey];
                    }
                }
            }

            // Data Rows
            foreach (var time in allTimes)
            {
                sb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff"));

                if (preset != null && preset.IncludeUnixTime)
                {
                    long unixTime = ((DateTimeOffset)time).ToUnixTimeMilliseconds();
                    sb.Append($",{unixTime}");
                }

                if (preset == null || preset.IncludeMachineState)
                {
                    sb.Append($",{filledStates[time]}");
                }

                foreach (var colKey in orderedKeys)
                {
                    sb.Append(",");
                    if (filledData[time].ContainsKey(colKey))
                    {
                        string val = filledData[time][colKey];

                        // Check if this is a LogStats warning column
                        bool isWarning = false;
                        if (colKey.Contains("LogStats") && colKey.Contains("|Metrics|"))
                        {
                            if ((colKey.EndsWith("|nSemMissed_Mult") || colKey.EndsWith("|Lost") || colKey.EndsWith("|bufFull"))
                                && val != "0")
                            {
                                isWarning = true;
                            }
                        }

                        // Add warning prefix if needed
                        if (isWarning)
                        {
                            val = $"[!] {val}";
                        }

                        // Escape if contains comma or quote
                        if (val.Contains(",") || val.Contains("\""))
                        {
                            val = "\"" + val.Replace("\"", "\"\"") + "\"";
                        }
                        sb.Append(val);
                    }
                }

                // Thread messages
                if (preset != null && preset.IncludeEvents)
                {
                    sb.Append(",");
                    if (threadMessages.ContainsKey(time) && threadMessages[time].ContainsKey("Events"))
                    {
                        string val = threadMessages[time]["Events"];
                        val = "\"" + val.Replace("\"", "\"\"") + "\"";
                        sb.Append(val);
                    }
                }

                if (selectedThreads != null)
                {
                    foreach (var thread in selectedThreads.OrderBy(t => t))
                    {
                        sb.Append(",");
                        if (threadMessages.ContainsKey(time) && threadMessages[time].ContainsKey(thread))
                        {
                            string val = threadMessages[time][thread];
                            val = "\"" + val.Replace("\"", "\"\"") + "\"";
                            sb.Append(val);
                        }
                    }
                }

                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Export Complete!\nSaved to: {filePath}\nRows: {allTimes.Count}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            });
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
    }
}