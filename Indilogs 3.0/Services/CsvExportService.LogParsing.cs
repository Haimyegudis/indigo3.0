using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Services
{
    public partial class CsvExportService
    {
        internal void ParseAxisMon(string msg, string threadName, DateTime time,
            HashSet<string>? selectedAxis,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
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

        internal void ParseAxM(string msg, string threadName, DateTime time,
            HashSet<string>? selectedAxis,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
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

                        if (!dataMatrix.TryGetValue(time, out var axmRow))
                        {
                            axmRow = new Dictionary<string, string>();
                            dataMatrix[time] = axmRow;
                        }

                        for (int i = 2; i < parts.Length; i++)
                        {
                            string rawPart = parts[i].Trim();

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
                                axmRow[$"{subsys}|{motor}|Trigger"] = rawPart;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
        }

        internal void ParseIOMon(string msg, string threadName, DateTime time,
            HashSet<string>? selectedIO,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
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

                            if (valueStr.StartsWith("New Status", StringComparison.OrdinalIgnoreCase) ||
                                valueStr.StartsWith(" New Status", StringComparison.OrdinalIgnoreCase))
                            {
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
                            ExtractIOComponent(fullSymbolName, rawSub, subsys, cleanValue, threadName, time,
                                selectedIO, schema, dataMatrix, threadNameMap);
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Parse failed", ex); }
        }

        internal void ParseIO(string msg, string threadName, DateTime time,
            HashSet<string>? selectedIO,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
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

                    string rawPair = parts[1].Trim();
                    int eqIndex = rawPair.IndexOf('=');

                    if (eqIndex > 0)
                    {
                        string fullSymbolName = rawPair.Substring(0, eqIndex).Trim();
                        string valueStr = rawPair.Substring(eqIndex + 1).Trim();
                        string cleanValue = valueStr.Split(' ')[0];

                        string? ioStatus = null;
                        if (parts.Length >= 3)
                        {
                            string statusPart = parts[2].Trim();
                            if (!statusPart.Contains("="))
                            {
                                ioStatus = DecodeIoStatus(statusPart);
                            }
                        }

                        string componentName;
                        string paramName;
                        DecomposeIOSymbol(fullSymbolName, out componentName, out paramName);

                        string componentKey = $"{rawSub}|{componentName}";

                        if (selectedIO == null || selectedIO.Contains(componentKey))
                        {
                            if (ioStatus != null)
                                AddToSchema(schema, subsys, componentName, new[] { paramName, "eIoStatus" });
                            else
                                AddToSchema(schema, subsys, componentName, new[] { paramName });

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

        private void ExtractIOComponent(string fullSymbolName, string rawSub, string subsys,
            string cleanValue, string threadName, DateTime time,
            HashSet<string>? selectedIO,
            SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
            SortedDictionary<DateTime, Dictionary<string, string>> dataMatrix,
            Dictionary<string, string> threadNameMap)
        {
            string componentName;
            string paramName;
            DecomposeIOSymbol(fullSymbolName, out componentName, out paramName);

            string componentKey = $"{rawSub}|{componentName}";

            if (selectedIO == null || selectedIO.Contains(componentKey))
            {
                AddToSchema(schema, subsys, componentName, new[] { paramName });

                string key = $"{subsys}|{componentName}|{paramName}";
                threadNameMap[key] = threadName;

                if (!dataMatrix.TryGetValue(time, out var ioMonRow))
                    dataMatrix[time] = ioMonRow = new Dictionary<string, string>();

                ioMonRow[key] = cleanValue;
            }
        }

        internal static void DecomposeIOSymbol(string fullSymbolName, out string componentName, out string paramName)
        {
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
        }

        internal void AddToSchema(SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> schema,
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

        internal void ParseAndAddValue(string rawPart, string subsys, string motor, DateTime time,
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
