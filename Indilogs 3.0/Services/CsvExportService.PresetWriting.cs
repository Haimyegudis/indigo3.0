using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    public partial class CsvExportService
    {
        private void WriteForwardFilledCsv(
            string filePath,
            ExportPreset? preset,
            IProgress? progress,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            SortedDictionary<DateTime, string> machineStates,
            SortedDictionary<DateTime, Dictionary<string, string>> threadMessages,
            Dictionary<string, string> threadNameMap,
            HashSet<string>? selectedThreads)
        {
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
                int lastReportedPercent = 75;

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
    }
}
