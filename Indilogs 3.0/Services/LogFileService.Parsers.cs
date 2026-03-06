using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        public List<EventEntry> ParseEventsCsv(Stream stream)
        {
            var list = new List<EventEntry>();
            try
            {
                if (stream.Position != 0) stream.Position = 0;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string? header = reader.ReadLine();
                    if (header == null) return list;

                    var headers = header.Split(',').Select(h => h.Trim().Trim('"')).ToArray();

                    int timeIdx = Array.FindIndex(headers, h => h.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  h.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  h.IndexOf("Timestamp", StringComparison.OrdinalIgnoreCase) >= 0);
                    int nameIdx = Array.FindIndex(headers, h => h.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("EventName", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("Event", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);
                    int stateIdx = Array.FindIndex(headers, h => h.Equals("State", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("EventState", StringComparison.OrdinalIgnoreCase) ||
                                                                  h.Equals("Status", StringComparison.OrdinalIgnoreCase));
                    int severityIdx = Array.FindIndex(headers, h => h.Equals("Severity", StringComparison.OrdinalIgnoreCase) ||
                                                                     h.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                                                                     h.Equals("Priority", StringComparison.OrdinalIgnoreCase));
                    int parametersIdx = Array.FindIndex(headers, h => h.IndexOf("Parameters", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                       h.IndexOf("Params", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                       h.IndexOf("Args", StringComparison.OrdinalIgnoreCase) >= 0);
                    int descriptionIdx = Array.FindIndex(headers, h => h.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Description", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Subsystem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                         h.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0);

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = SplitCsvLine(line);

                        if (parts.Count > timeIdx && DateTime.TryParse(parts[timeIdx].Trim('"'), out DateTime time))
                        {
                            list.Add(new EventEntry
                            {
                                Time = time,
                                Name = (nameIdx >= 0 && parts.Count > nameIdx) ? parts[nameIdx].Trim('"') : "Unknown",
                                State = (stateIdx >= 0 && parts.Count > stateIdx) ? parts[stateIdx].Trim('"') : string.Empty,
                                Severity = (severityIdx >= 0 && parts.Count > severityIdx) ? parts[severityIdx].Trim('"') : string.Empty,
                                Parameters = (parametersIdx >= 0 && parts.Count > parametersIdx) ? parts[parametersIdx].Trim('"') : string.Empty,
                                Description = (descriptionIdx >= 0 && parts.Count > descriptionIdx) ? parts[descriptionIdx].Trim('"') : string.Empty
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Parsing events CSV failed", ex); }
            return list;
        }

        /// <summary>
        /// Converts a pressEvents XML stream to CSV format so existing CSV display
        /// and parsing code can be reused. Handles both attribute-based and
        /// element-based XML structures.
        /// </summary>
        public static string ConvertEventsXmlToCsv(Stream stream)
        {
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                var doc = XDocument.Load(stream);
                var root = doc.Root;
                if (root == null) return "";

                var records = root.Elements().ToList();
                if (records.Count == 0) return "";

                var first = records[0];
                bool useAttributes = first.Attributes().Any();

                // Collect all column names preserving order of first occurrence
                var columnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var columns = new List<string>();

                foreach (var r in records)
                {
                    var names = useAttributes
                        ? r.Attributes().Select(a => a.Name.LocalName)
                        : r.Elements().Select(e => e.Name.LocalName);

                    foreach (var name in names)
                    {
                        if (columnSet.Add(name))
                            columns.Add(name);
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", columns.Select(c => $"\"{c}\"")));

                foreach (var record in records)
                {
                    var values = new List<string>();
                    foreach (var col in columns)
                    {
                        string val = useAttributes
                            ? (record.Attribute(col)?.Value ?? "")
                            : (record.Element(col)?.Value ?? "");
                        values.Add($"\"{val.Replace("\"", "\"\"")}\"");
                    }
                    sb.AppendLine(string.Join(",", values));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                AppLogger.Error("ConvertEventsXmlToCsv failed", ex);
                return "";
            }
        }

        public List<LogEntry> ParseLogStreamPartial(Stream stream)
        {
            var pool = new StringPool();
            var newLogs = new List<LogEntry>();

            try
            {
                var logReader = new IndigoLogsReader(stream);

                while (logReader.MoveToNext())
                {
                    if (logReader.Current != null)
                    {
                        string? processName = logReader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? "" : pool.Intern(processName)
                        };

                        newLogs.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ParseLogStreamPartial failed", ex);
            }

            return newLogs;
        }

        /// <summary>
        /// Parses a log stream, skipping the first <paramref name="skipCount"/> entries without
        /// creating LogEntry objects (fast iteration only). Returns only NEW entries after the skip.
        /// Also returns the total entry count for tracking.
        /// </summary>
        public (List<LogEntry> NewEntries, int TotalCount) ParseLogStreamSkipExisting(Stream stream, int skipCount)
        {
            var pool = new StringPool();
            var newEntries = new List<LogEntry>();
            int totalCount = 0;

            try
            {
                var logReader = new IndigoLogsReader(stream);

                while (logReader.MoveToNext())
                {
                    if (logReader.Current != null)
                    {
                        totalCount++;

                        // Fast skip: just advance the reader without creating LogEntry
                        if (totalCount <= skipCount)
                            continue;

                        // Only create LogEntry for NEW entries
                        string? processName = logReader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? "" : pool.Intern(processName)
                        };

                        newEntries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ParseLogStreamSkipping failed", ex);
            }

            return (newEntries, totalCount);
        }

        public (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream, StringPool? pool = null)
        {
            // If no Pool was passed (e.g. from legacy calls), create a local one
            pool = pool ?? new StringPool();

            // Pre-allocate based on estimated entries (~200 bytes per log entry in binary format)
            int estimatedEntries = stream.CanSeek ? (int)Math.Min(stream.Length / 200, 500000) : 10000;
            var allLogs = new List<LogEntry>(estimatedEntries);
            var transitions = new List<LogEntry>();
            var failures = new List<LogEntry>();

            try
            {
                if (stream.Position != 0) stream.Position = 0;
                var reader = new IndigoLogsReader(stream);

                while (reader.MoveToNext())
                {
                    if (reader.Current != null)
                    {
                        string? processName = reader.Current["ProcessName"]?.ToString();

                        string message = reader.Current.Message ?? "";
                        string threadName = pool.Intern(reader.Current.ThreadName ?? "");

                        var entry = new LogEntry
                        {
                            // Only intern repetitive fields (Level, ThreadName, Logger, ProcessName)
                            // Message is unique per log - interning wastes ConcurrentDictionary overhead
                            Level = pool.Intern(reader.Current.Level?.ToString() ?? "Info"),
                            Date = reader.Current.Time,
                            Message = message,
                            ThreadName = threadName,
                            Logger = pool.Intern(reader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? "" : pool.Intern(processName)
                        };

                        allLogs.Add(entry);

                        if (threadName == "Manager" &&
                            message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                            message.Contains("->"))
                        {
                            transitions.Add(entry);
                        }
                        else if (threadName == "Events" &&
                                 message.Contains("PLC_FAILURE_STATE_CHANGE"))
                        {
                            failures.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Use non-blocking BeginInvoke to avoid stalling parallel worker threads
                AppLogger.Error("Error parsing log stream", ex);
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    _dialogService?.ShowWarning(
                        $"Error parsing log stream: {ex.GetType().Name}: {ex.Message}",
                        "Parse Error")));
            }
            return (allLogs, transitions, failures);
        }

    }
}
