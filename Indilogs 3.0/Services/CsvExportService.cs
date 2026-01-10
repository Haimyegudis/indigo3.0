using IndiLogs_3._0.Models;
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

        public async Task ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset = null)
        {
            if (preset != null)
            {
                await ExportLogsWithPresetAsync(logs, defaultFileName, preset);
                return;
            }

            // קריאה ישנה - ללא פילטרים
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
                    // Schema: Subsystem -> Component -> List of Params
                    var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
                    var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();

                    foreach (var log in logs)
                    {
                        if (string.IsNullOrEmpty(log.Message)) continue;

                        string msg = log.Message.Trim();
                        DateTime time = log.Date;

                        // ---------------------------------------------------------
                        // A: AxisMon (מנועים)
                        // ---------------------------------------------------------
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
                                    string subsys = $"AxisMon: {rawSub}";
                                    string motor = parts[1].Trim();

                                    AddToSchema(schema, subsys, motor, _axisParams);

                                    for (int i = 2; i < parts.Length; i++)
                                    {
                                        ParseAndAddValue(parts[i], subsys, motor, time, dataMatrix, _axisParams);
                                    }
                                }
                            }
                            catch { }
                        }
                        // ---------------------------------------------------------
                        // B: IO_Mon (חיישנים)
                        // ---------------------------------------------------------
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

                                            AddToSchema(schema, subsys, componentName, new[] { paramName });

                                            if (!dataMatrix.ContainsKey(time))
                                                dataMatrix[time] = new Dictionary<string, string>();

                                            string key = $"{subsys}|{componentName}|{paramName}";
                                            dataMatrix[time][key] = cleanValue;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        // ---------------------------------------------------------
                        // C: Machine State (PlcMngr Transitions) - המתוקן
                        // ---------------------------------------------------------
                        else if (string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) &&
                                 msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string newState = null;
                                int arrowIndex = msg.IndexOf("->");

                                if (arrowIndex > 0)
                                {
                                    // אופציה 1: יש חץ - לוקחים את מה שאחריו
                                    newState = msg.Substring(arrowIndex + 2).Trim();
                                }
                                else
                                {
                                    // אופציה 2 (גיבוי): אין חץ - מפרקים למילים ולוקחים את המילה האחרונה
                                    // דוגמה: PlcMngr: Reset GO_TO_OFF -> State = GO_TO_OFF
                                    string content = msg.Substring(8).Trim(); // מוריד "PlcMngr:"
                                    var parts = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 1)
                                    {
                                        newState = parts.Last();
                                    }
                                }

                                if (!string.IsNullOrEmpty(newState))
                                {
                                    string subsys = "System";       // אבא
                                    string component = "MachineState"; // בן
                                    string param = "State";         // ערך

                                    AddToSchema(schema, subsys, component, new[] { param });

                                    if (!dataMatrix.ContainsKey(time))
                                        dataMatrix[time] = new Dictionary<string, string>();

                                    string key = $"{subsys}|{component}|{param}";
                                    dataMatrix[time][key] = newState;
                                }
                            }
                            catch { }
                        }
                    }

                    // === בניית ה-CSV ===

                    if (schema.Count == 0)
                    {
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("No parsable data found.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return;
                    }

                    var sb = new StringBuilder();

                    // Header 1: Subsystems
                    sb.Append("Time");
                    foreach (var subEntry in schema)
                    {
                        string subName = subEntry.Key;
                        int totalCols = subEntry.Value.Sum(comp => comp.Value.Count);

                        sb.Append($",{subName}");
                        for (int k = 1; k < totalCols; k++) sb.Append(",");
                    }
                    sb.AppendLine();

                    // Header 2: Components
                    sb.Append("");
                    foreach (var subEntry in schema)
                    {
                        foreach (var compEntry in subEntry.Value)
                        {
                            string compName = compEntry.Key;
                            int paramCount = compEntry.Value.Count;

                            sb.Append($",{compName}");
                            for (int k = 1; k < paramCount; k++) sb.Append(",");
                        }
                    }
                    sb.AppendLine();

                    // Header 3: Params
                    sb.Append("");
                    foreach (var subEntry in schema)
                    {
                        foreach (var compEntry in subEntry.Value)
                        {
                            foreach (var param in compEntry.Value)
                            {
                                sb.Append($",{param}");
                            }
                        }
                    }
                    sb.AppendLine();

                    // Data Rows
                    var orderedKeys = new List<string>();
                    foreach (var subEntry in schema)
                    {
                        foreach (var compEntry in subEntry.Value)
                        {
                            foreach (var param in compEntry.Value)
                            {
                                orderedKeys.Add($"{subEntry.Key}|{compEntry.Key}|{param}");
                            }
                        }
                    }

                    foreach (var row in dataMatrix)
                    {
                        sb.Append(row.Key.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        foreach (var colKey in orderedKeys)
                        {
                            sb.Append(",");
                            if (row.Value.TryGetValue(colKey, out string val))
                            {
                                sb.Append(val);
                            }
                        }
                        sb.AppendLine();
                    }

                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Complete!\nSaved to: {filePath}\nRows: {dataMatrix.Count}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
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

        // ===================================================================
        // Export עם פילטרים על פי ExportPreset
        // ===================================================================
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
                    var schema = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
                    var dataMatrix = new SortedDictionary<DateTime, Dictionary<string, string>>();
                    var threadMessages = new SortedDictionary<DateTime, Dictionary<string, string>>();

                    // המרת רשימות פרסט לסטים לחיפוש מהיר
                    var selectedIO = new HashSet<string>(preset.SelectedIOComponents, StringComparer.OrdinalIgnoreCase);
                    var selectedAxis = new HashSet<string>(preset.SelectedAxisComponents, StringComparer.OrdinalIgnoreCase);
                    var selectedThreads = new HashSet<string>(preset.SelectedThreads, StringComparer.OrdinalIgnoreCase);

                    foreach (var log in logs)
                    {
                        if (string.IsNullOrEmpty(log.Message)) continue;

                        string msg = log.Message.Trim();
                        DateTime time = log.Date;

                        // ---------------------------------------------------------
                        // A: AxisMon (מנועים) - רק אם נבחרו
                        // ---------------------------------------------------------
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

                                    // בדיקה אם נבחר
                                    if (selectedAxis.Contains(componentKey))
                                    {
                                        string subsys = $"AxisMon: {rawSub}";
                                        AddToSchema(schema, subsys, motor, _axisParams);

                                        for (int i = 2; i < parts.Length; i++)
                                        {
                                            ParseAndAddValue(parts[i], subsys, motor, time, dataMatrix, _axisParams);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // ---------------------------------------------------------
                        // B: IO_Mon (חיישנים) - רק אם נבחרו
                        // ---------------------------------------------------------
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

                                            // בדיקה אם נבחר
                                            if (selectedIO.Contains(componentKey))
                                            {
                                                AddToSchema(schema, subsys, componentName, new[] { paramName });

                                                if (!dataMatrix.ContainsKey(time))
                                                    dataMatrix[time] = new Dictionary<string, string>();

                                                string key = $"{subsys}|{componentName}|{paramName}";
                                                dataMatrix[time][key] = cleanValue;
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // ---------------------------------------------------------
                        // C: Machine State (PlcMngr Transitions)
                        // ---------------------------------------------------------
                        else if (string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) &&
                                 msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string newState = null;
                                int arrowIndex = msg.IndexOf("->");

                                if (arrowIndex > 0)
                                {
                                    newState = msg.Substring(arrowIndex + 2).Trim();
                                }
                                else
                                {
                                    string content = msg.Substring(8).Trim();
                                    var parts = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 1)
                                    {
                                        newState = parts.Last();
                                    }
                                }

                                if (!string.IsNullOrEmpty(newState))
                                {
                                    string subsys = "System";
                                    string component = "MachineState";
                                    string param = "State";

                                    AddToSchema(schema, subsys, component, new[] { param });

                                    if (!dataMatrix.ContainsKey(time))
                                        dataMatrix[time] = new Dictionary<string, string>();

                                    string key = $"{subsys}|{component}|{param}";
                                    dataMatrix[time][key] = newState;
                                }
                            }
                            catch { }
                        }

                        // ---------------------------------------------------------
                        // D: Thread Messages - רק אם נבחרו
                        // ---------------------------------------------------------
                        if (!string.IsNullOrEmpty(log.ThreadName) && selectedThreads.Contains(log.ThreadName))
                        {
                            if (!threadMessages.ContainsKey(time))
                                threadMessages[time] = new Dictionary<string, string>();

                            threadMessages[time][log.ThreadName] = msg;
                        }

                        // ---------------------------------------------------------
                        // E: Events - אם נבחר
                        // ---------------------------------------------------------
                        if (preset.IncludeEvents &&
                            string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!threadMessages.ContainsKey(time))
                                threadMessages[time] = new Dictionary<string, string>();

                            threadMessages[time]["Events"] = msg;
                        }
                    }

                    // === בניית ה-CSV ===

                    if (schema.Count == 0 && threadMessages.Count == 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("No data matches the selected filters.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning));
                        return;
                    }

                    var sb = new StringBuilder();

                    // בניית Headers
                    var orderedKeys = new List<string>();

                    // Header 1: Time + UnixTime (אם נבחר) + Subsystems + Threads
                    sb.Append("Time");
                    if (preset.IncludeUnixTime)
                        sb.Append(",Unix Time");

                    foreach (var subEntry in schema)
                    {
                        string subName = subEntry.Key;
                        int totalCols = subEntry.Value.Sum(comp => comp.Value.Count);

                        sb.Append($",{subName}");
                        for (int k = 1; k < totalCols; k++) sb.Append(",");
                    }

                    // הוספת Thread Headers
                    if (preset.IncludeEvents)
                        sb.Append(",Events");

                    foreach (var thread in selectedThreads.OrderBy(t => t))
                    {
                        sb.Append($",{thread}");
                    }

                    sb.AppendLine();

                    // Header 2: Components
                    sb.Append("");
                    if (preset.IncludeUnixTime)
                        sb.Append(",");

                    foreach (var subEntry in schema)
                    {
                        foreach (var compEntry in subEntry.Value)
                        {
                            string compName = compEntry.Key;
                            int paramCount = compEntry.Value.Count;

                            sb.Append($",{compName}");
                            for (int k = 1; k < paramCount; k++) sb.Append(",");
                        }
                    }

                    if (preset.IncludeEvents)
                        sb.Append(",Message");

                    foreach (var thread in selectedThreads.OrderBy(t => t))
                    {
                        sb.Append(",Message");
                    }

                    sb.AppendLine();

                    // Header 3: Params
                    sb.Append("");
                    if (preset.IncludeUnixTime)
                        sb.Append(",Timestamp");

                    foreach (var subEntry in schema)
                    {
                        foreach (var compEntry in subEntry.Value)
                        {
                            foreach (var param in compEntry.Value)
                            {
                                sb.Append($",{param}");
                                orderedKeys.Add($"{subEntry.Key}|{compEntry.Key}|{param}");
                            }
                        }
                    }

                    if (preset.IncludeEvents)
                        sb.Append(",");

                    foreach (var thread in selectedThreads.OrderBy(t => t))
                    {
                        sb.Append(",");
                    }

                    sb.AppendLine();

                    // Data Rows - איחוד זמנים מכל המקורות
                    var allTimes = new SortedSet<DateTime>();
                    foreach (var t in dataMatrix.Keys) allTimes.Add(t);
                    foreach (var t in threadMessages.Keys) allTimes.Add(t);

                    foreach (var time in allTimes)
                    {
                        sb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff"));

                        // Unix Time
                        if (preset.IncludeUnixTime)
                        {
                            long unixTime = ((DateTimeOffset)time).ToUnixTimeMilliseconds();
                            sb.Append($",{unixTime}");
                        }

                        // ערכי Data Matrix
                        foreach (var colKey in orderedKeys)
                        {
                            sb.Append(",");
                            if (dataMatrix.ContainsKey(time) && dataMatrix[time].TryGetValue(colKey, out string val))
                            {
                                sb.Append(val);
                            }
                        }

                        // Events
                        if (preset.IncludeEvents)
                        {
                            sb.Append(",");
                            if (threadMessages.ContainsKey(time) && threadMessages[time].TryGetValue("Events", out string evtMsg))
                            {
                                // ניקוי תווים מיוחדים
                                string cleaned = evtMsg.Replace("\"", "\"\"");
                                sb.Append($"\"{cleaned}\"");
                            }
                        }

                        // Thread Messages
                        foreach (var thread in selectedThreads.OrderBy(t => t))
                        {
                            sb.Append(",");
                            if (threadMessages.ContainsKey(time) && threadMessages[time].TryGetValue(thread, out string thrMsg))
                            {
                                string cleaned = thrMsg.Replace("\"", "\"\"");
                                sb.Append($"\"{cleaned}\"");
                            }
                        }

                        sb.AppendLine();
                    }

                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Complete!\nSaved to: {filePath}\nRows: {allTimes.Count}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }
    }
}