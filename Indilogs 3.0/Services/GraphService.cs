using IndiLogs_3._0.Models;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public class GraphService
    {
        // Regex קפדני לזיהוי פרמטרים (תומך ברווחים, מינוס, ומספרים מדעיים)
        private readonly Regex _paramRegex = new Regex(@"([a-zA-Z0-9_]+)\s*=\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)", RegexOptions.Compiled);

        private readonly HashSet<string> _axisParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr", "Vel", "Pos", "Acc", "Current"
        };

        private readonly Dictionary<string, OxyColor> _stateColors = new Dictionary<string, OxyColor>(StringComparer.OrdinalIgnoreCase)
        {
            { "INIT", OxyColor.Parse("#FFE135") }, { "POWER_DISABLE", OxyColor.Parse("#FF6B6B") }, { "OFF", OxyColor.Parse("#808080") },
            { "SERVICE", OxyColor.Parse("#8B4513") }, { "MECH_INIT", OxyColor.Parse("#FFA500") }, { "STANDBY", OxyColor.Parse("#FFFF00") },
            { "GET_READY", OxyColor.Parse("#FFA500") }, { "READY", OxyColor.Parse("#90EE90") }, { "PRE_PRINT", OxyColor.Parse("#26C6DA") },
            { "PRINT", OxyColor.Parse("#228B22") }, { "POST_PRINT", OxyColor.Parse("#4169E1") }, { "PAUSE", OxyColor.Parse("#FFA726") },
            { "RECOVERY", OxyColor.Parse("#EC407A") }, { "SML_OFF", OxyColor.Parse("#C62828") }, { "DYNAMIC_READY", OxyColor.Parse("#32CD32") }
        };

        public async Task<(Dictionary<string, List<SimpleDataPoint>>, ObservableCollection<GraphNode>, List<MachineStateSegment>)> ParseLogsToGraphDataAsync(IEnumerable<LogEntry> logs)
        {
            return await Task.Run(() =>
            {
                var dataStore = new Dictionary<string, List<SimpleDataPoint>>();
                var rootNodes = new ObservableCollection<GraphNode>();
                var stateSegments = new List<MachineStateSegment>();

                void AddPathToTree(string[] pathParts, string fullKey)
                {
                    ObservableCollection<GraphNode> currentCollection = rootNodes;

                    for (int i = 0; i < pathParts.Length; i++)
                    {
                        string partName = pathParts[i];
                        bool isLeaf = (i == pathParts.Length - 1);

                        var node = currentCollection.FirstOrDefault(n => n.Name == partName);

                        if (node == null)
                        {
                            node = new GraphNode
                            {
                                Name = partName,
                                IsLeaf = isLeaf,
                                FullPath = isLeaf ? fullKey : null,
                                IsExpanded = i == 0
                            };

                            int insertIndex = 0;
                            while (insertIndex < currentCollection.Count && string.Compare(currentCollection[insertIndex].Name, partName) < 0)
                            {
                                insertIndex++;
                            }
                            currentCollection.Insert(insertIndex, node);
                        }

                        currentCollection = node.Children;
                    }
                }

                var sortedLogs = logs.Where(l => !string.IsNullOrEmpty(l.Message)).OrderBy(l => l.Date).ToList();
                if (sortedLogs.Count == 0) return (dataStore, rootNodes, stateSegments);

                // ✅ שמירת זמני התחלה וסיום של הלוג
                DateTime logStartTime = sortedLogs.First().Date;
                DateTime logEndTime = sortedLogs.Last().Date;

                LogEntry lastStateLog = null;
                string currentStateName = "UNDEFINED";

                // ✅ הוספת state התחלתי אם הלוג לא מתחיל ב-state transition
                var firstStateTransition = sortedLogs.FirstOrDefault(l =>
                    l.ThreadName == "Manager" &&
                    l.Message != null &&
                    l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                    l.Message.Contains("->"));

                if (firstStateTransition != null && firstStateTransition.Date > logStartTime)
                {
                    // יש פער בין תחילת הלוג לבין ה-state הראשון - נמלא אותו
                    AddStateSegment(stateSegments, "UNDEFINED", logStartTime, firstStateTransition.Date);

                    // עכשיו נתחיל מה-state הראשון
                    var parts = firstStateTransition.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        currentStateName = parts[1].Trim();
                        lastStateLog = firstStateTransition;
                    }
                }

                foreach (var log in sortedLogs)
                {
                    string msg = log.Message;
                    string thread = log.ThreadName ?? "Unknown";
                    double timeVal = log.Date.Ticks;

                    // Motor Axis Logic
                    if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = msg.Substring(8).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 3)
                        {
                            string component = parts[0].Trim();
                            string subComponent = parts[1].Trim();

                            string paramsPart = string.Join(",", parts.Skip(2));
                            var matches = _paramRegex.Matches(paramsPart);

                            foreach (Match m in matches)
                            {
                                string key = m.Groups[1].Value;
                                string valStr = m.Groups[2].Value;

                                if (_axisParams.Contains(key))
                                {
                                    if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                    {
                                        string fullKey = $"{thread}.{component}.{subComponent}.{key}";
                                        AddPoint(dataStore, fullKey, timeVal, val);
                                        AddPathToTree(new[] { "Motor Axis", thread, component, subComponent, key }, fullKey);
                                    }
                                }
                            }
                        }
                    }
                    // IO Monitor Logic
                    else if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = msg.Substring(7).Trim();
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            string componentName = parts[0].Trim();

                            for (int i = 1; i < parts.Length; i++)
                            {
                                string pair = parts[i].Trim();
                                int eqIdx = pair.LastIndexOf('=');

                                if (eqIdx > 0)
                                {
                                    string subComponentName = pair.Substring(0, eqIdx).Trim();
                                    string valStr = pair.Substring(eqIdx + 1).Trim();

                                    if (valStr.Contains(" ")) valStr = valStr.Split(' ')[0];

                                    if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                    {
                                        string fullKey = $"IO.{thread}.{componentName}.{subComponentName}";
                                        AddPoint(dataStore, fullKey, timeVal, val);
                                        AddPathToTree(new[] { "IO Monitor", thread, componentName, subComponentName }, fullKey);
                                    }
                                }
                            }
                        }
                    }
                    // Machine States Logic
                    else if (log.ThreadName == "Manager" && msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) && msg.Contains("->"))
                    {
                        // דלג על ה-state הראשון אם כבר טיפלנו בו
                        if (lastStateLog != null && log == firstStateTransition)
                            continue;

                        var parts = msg.Split(new[] { "->" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            if (lastStateLog != null)
                                AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, log.Date);

                            currentStateName = parts[1].Trim();
                            lastStateLog = log;
                        }
                    }
                }

                // ✅ סיום ה-state האחרון עד סוף הלוג
                if (lastStateLog != null)
                    AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, logEndTime);
                else if (stateSegments.Count == 0)
                {
                    // אין שום state transitions - נמלא את כל הלוג ב-UNDEFINED
                    AddStateSegment(stateSegments, "UNDEFINED", logStartTime, logEndTime);
                }

                System.Diagnostics.Debug.WriteLine($"🟡 GraphService: Found {stateSegments.Count} state segments");
                foreach (var seg in stateSegments)
                {
                    System.Diagnostics.Debug.WriteLine($"   - {seg.Name}: {seg.Start} to {seg.End}");
                }

                return (dataStore, rootNodes, stateSegments);
            });
        }

        private void AddStateSegment(List<MachineStateSegment> list, string name, DateTime start, DateTime end)
        {
            var color = _stateColors.ContainsKey(name) ? _stateColors[name] : OxyColors.LightGray;

            // ✅ הורדת הסף המינימלי ל-1ms (במקום 10ms)
            if ((end - start).TotalMilliseconds > 1)
            {
                list.Add(new MachineStateSegment
                {
                    Name = name,
                    Start = DateTimeAxis.ToDouble(start),
                    End = DateTimeAxis.ToDouble(end),
                    Color = color
                });
            }
        }

        private void AddPoint(Dictionary<string, List<SimpleDataPoint>> store, string key, double x, double y)
        {
            if (!store.TryGetValue(key, out var list))
            {
                // הקצאת זיכרון מראש לשיפור ביצועים
                list = new List<SimpleDataPoint>(1000);
                store[key] = list;
            }
            list.Add(new SimpleDataPoint(x, y));
        }
    }
}