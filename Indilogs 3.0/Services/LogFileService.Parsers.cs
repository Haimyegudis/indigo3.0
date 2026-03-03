using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
                    string header = reader.ReadLine();
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
                        string processName = logReader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
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
                        string processName = logReader.Current["ProcessName"]?.ToString();

                        var entry = new LogEntry
                        {
                            Level = pool.Intern(logReader.Current.Level?.ToString() ?? "Info"),
                            Date = logReader.Current.Time,
                            Message = logReader.Current.Message ?? "",
                            ThreadName = pool.Intern(logReader.Current.ThreadName ?? ""),
                            Logger = pool.Intern(logReader.Current.LoggerName ?? ""),
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
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

        public (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream, StringPool pool = null)
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
                        string processName = reader.Current["ProcessName"]?.ToString();

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
                            ProcessName = string.IsNullOrEmpty(processName) ? null : pool.Intern(processName)
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
                    System.Windows.MessageBox.Show(
                        $"Error parsing log stream: {ex.GetType().Name}: {ex.Message}",
                        "Parse Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
            }
            return (allLogs, transitions, failures);
        }

        private List<LogEntry> ParseAppDevLogStream(Stream stream, StringPool pool = null)
        {
            pool = pool ?? new StringPool();
            // Pre-allocate based on ~1KB per entry
            int estimatedEntries = stream.CanSeek ? (int)Math.Min(stream.Length / 1024, 500000) : 10000;
            var list = new List<LogEntry>(estimatedEntries);
            try
            {
                if (stream.CanSeek && stream.Position != 0) stream.Position = 0;
                // 64KB reader buffer — much better throughput for large files (default is 1KB)
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 65536))
                {
                    string line;
                    var buffer = new StringBuilder(4096);

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 7 && line == "!!![V2]") continue;

                        if (IsDateStart(line))
                        {
                            // Flush previous buffered entry (new-format multi-line)
                            if (buffer.Length > 0)
                            {
                                var logEntry = ProcessAppDevBufferFast(buffer.ToString(), pool);
                                if (logEntry != null) list.Add(logEntry);
                                buffer.Clear();
                            }

                            // Fast-path: old-format single-line entry (has \x1e separators)
                            // Skips StringBuilder + Split overhead (~6 fewer allocations per entry)
                            if (line.IndexOf('\x1e') > 0)
                            {
                                var entry = ParseOldFormatDirect(line, pool);
                                if (entry != null)
                                {
                                    list.Add(entry);
                                    continue; // parsed — don't buffer
                                }
                                // Incomplete (multi-line message) — fall through to buffer
                            }
                        }
                        buffer.AppendLine(line);
                    }

                    if (buffer.Length > 0)
                    {
                        var logEntry = ProcessAppDevBufferFast(buffer.ToString(), pool);
                        if (logEntry != null) list.Add(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Parsing app dev log failed", ex);
            }
            return list;
        }

        /// <summary>
        /// Parses old-format AppDev log entry directly from a line using IndexOf chain.
        /// Avoids Split (saves array + 12 substrings) and StringBuilder buffer.
        /// Returns null if the line doesn't have enough \x1e separators (multi-line entry).
        /// </summary>
        private LogEntry ParseOldFormatDirect(string line, StringPool pool)
        {
            // Fields: [0]Timestamp [1]Thread [2]RootIFlowId [3]IFlowId [4]IFlowName
            //         [5]Pattern [6]Context [7]LevelLogger [8]Location [9]Message [10]Exception [11]Data
            int e0 = line.IndexOf('\x1e');
            if (e0 < 20) return null;

            int e1 = line.IndexOf('\x1e', e0 + 1);  if (e1 < 0) return null;
            int e2 = line.IndexOf('\x1e', e1 + 1);  if (e2 < 0) return null;
            int e3 = line.IndexOf('\x1e', e2 + 1);  if (e3 < 0) return null;
            int e4 = line.IndexOf('\x1e', e3 + 1);  if (e4 < 0) return null;
            int e5 = line.IndexOf('\x1e', e4 + 1);  if (e5 < 0) return null;
            int e6 = line.IndexOf('\x1e', e5 + 1);  if (e6 < 0) return null;
            int e7 = line.IndexOf('\x1e', e6 + 1);  if (e7 < 0) return null;
            int e8 = line.IndexOf('\x1e', e7 + 1);  if (e8 < 0) return null;
            int e9 = line.IndexOf('\x1e', e8 + 1);  if (e9 < 0) return null;

            // Timestamp — ParseTimestampFast reads from position 0, stops at first non-digit after comma
            DateTime date = ParseTimestampFast(line);
            if (date == DateTime.MinValue) return null;

            // Level + Logger from field [7]: "LEVEL Logger"
            string level = "INFO";
            string logger = "";
            int s7 = e6 + 1;
            int lvlSpace = line.IndexOf(' ', s7);
            if (lvlSpace > 0 && lvlSpace < e7)
            {
                level = line.Substring(s7, lvlSpace - s7).ToUpper();
                logger = line.Substring(lvlSpace + 1, e7 - lvlSpace - 1).Trim();
            }

            // Message [9]
            string message = line.Substring(e8 + 1, e9 - e8 - 1).Trim();

            // Exception [10] — optional
            string exception = null;
            int e10 = line.IndexOf('\x1e', e9 + 1);
            if (e10 > e9 + 1)
            {
                string exc = line.Substring(e9 + 1, e10 - e9 - 1).Trim();
                if (exc.Length > 0) exception = exc;
            }

            // Data [11] — optional
            string data = null;
            if (e10 > 0)
            {
                int e11 = line.IndexOf('\x1e', e10 + 1);
                int dataEnd = e11 > 0 ? e11 : line.Length;
                if (dataEnd > e10 + 1)
                {
                    string d = line.Substring(e10 + 1, dataEnd - e10 - 1).Trim();
                    if (d.Length > 0) data = d;
                }
            }

            // Pattern [5]
            string pattern = null;
            if (e5 > e4 + 1)
            {
                string p = line.Substring(e4 + 1, e5 - e4 - 1).Trim();
                if (p.Length > 0) pattern = p;
            }

            return new LogEntry
            {
                Date = date,
                ThreadName = pool.Intern(line.Substring(e0 + 1, e1 - e0 - 1)),
                Level = pool.Intern(level),
                Logger = pool.Intern(logger),
                Message = message,
                ProcessName = pool.Intern("APP"),
                Method = pool.Intern(line.Substring(e7 + 1, e8 - e7 - 1).Trim()),
                Pattern = pattern,
                Data = data,
                Exception = exception
            };
        }

        /// <summary>
        /// Fast manual parser replacing regex for AppDev log entries.
        /// Old format: fields separated by \x1e (record separator).
        /// New format: pipe-separated multi-line — falls back to regex only if needed.
        /// </summary>
        private LogEntry ProcessAppDevBufferFast(string rawText, StringPool pool)
        {
            // ── Old format: \x1e separated ──
            // Fields: Timestamp\x1eThread\x1eRootIFlowId\x1eIFlowId\x1eIFlowName\x1ePattern\x1eContext\x1e"Level Logger"\x1eLocation\x1eMessage\x1eException\x1eData
            int firstSep = rawText.IndexOf('\x1e');
            if (firstSep > 0)
            {
                var parts = rawText.Split('\x1e');
                if (parts.Length < 10) return null;

                DateTime date = ParseTimestampFast(parts[0].Trim());
                if (date == DateTime.MinValue) return null;

                // [7] = "Level Logger" — split on first space
                string levelLogger = parts[7];
                string level = "INFO";
                string logger = "";
                int spaceIdx = levelLogger.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    level = levelLogger.Substring(0, spaceIdx).ToUpper();
                    logger = levelLogger.Substring(spaceIdx + 1).Trim();
                }

                string message = parts[9].Trim();
                string exception = parts.Length > 10 ? parts[10].Trim() : "";
                string data = parts.Length > 11 ? parts[11].Trim() : "";
                string pattern = parts[5].Trim();
                string location = parts[8].Trim();

                return new LogEntry
                {
                    Date = date,
                    ThreadName = pool.Intern(parts[1]),
                    Level = pool.Intern(level),
                    Logger = pool.Intern(logger),
                    Message = message,
                    ProcessName = pool.Intern("APP"),
                    Method = pool.Intern(location),
                    Pattern = string.IsNullOrEmpty(pattern) ? null : pattern,
                    Data = string.IsNullOrEmpty(data) ? null : data,
                    Exception = string.IsNullOrEmpty(exception) ? null : exception
                };
            }

            // ── New format: pipe-separated multi-line ──
            // First line: Timestamp |Thread| |Root| |Flow| |Name| |Pattern| |Context| LEVEL Logger
            // Second line: |Method|
            // Remaining lines until ||: Message
            int firstNl = rawText.IndexOf('\n');
            if (firstNl < 0) return null;

            string firstLine = firstNl > 0 && rawText[firstNl - 1] == '\r'
                ? rawText.Substring(0, firstNl - 1) : rawText.Substring(0, firstNl);

            int firstPipe = firstLine.IndexOf('|');
            if (firstPipe < 0) return null;

            DateTime pipeDate = ParseTimestampFast(firstLine.Substring(0, firstPipe).Trim());
            if (pipeDate == DateTime.MinValue) return null;

            // Extract |Field| groups
            var pipeFields = new List<string>(8);
            int pos = firstPipe;
            while (pos < firstLine.Length && firstLine[pos] == '|')
            {
                int endPipe = firstLine.IndexOf('|', pos + 1);
                if (endPipe < 0) break;
                pipeFields.Add(firstLine.Substring(pos + 1, endPipe - pos - 1));
                pos = endPipe + 1;
                while (pos < firstLine.Length && firstLine[pos] == ' ') pos++;
            }

            if (pipeFields.Count < 6) return null;

            string pThread = pipeFields[0];
            string pPattern = pipeFields.Count > 4 ? pipeFields[4] : "";

            // Remainder is "LEVEL  Logger"
            string pLevelLogger = pos < firstLine.Length ? firstLine.Substring(pos).Trim() : "";
            string pLevel = "INFO";
            string pLogger = "";
            int pSpIdx = pLevelLogger.IndexOf(' ');
            if (pSpIdx > 0)
            {
                pLevel = pLevelLogger.Substring(0, pSpIdx).Trim().ToUpper();
                pLogger = pLevelLogger.Substring(pSpIdx).Trim();
            }

            // Second line: |Method|
            string pLocation = "";
            int secondStart = firstNl + 1;
            if (secondStart < rawText.Length)
            {
                int secondNl = rawText.IndexOf('\n', secondStart);
                if (secondNl > secondStart)
                {
                    string secondLine = rawText[secondNl - 1] == '\r'
                        ? rawText.Substring(secondStart, secondNl - secondStart - 1)
                        : rawText.Substring(secondStart, secondNl - secondStart);
                    if (secondLine.Length > 2 && secondLine[0] == '|' && secondLine[secondLine.Length - 1] == '|')
                        pLocation = secondLine.Substring(1, secondLine.Length - 2);

                    // Message: everything after second line until trailing ||
                    int msgStart = secondNl + 1;
                    if (msgStart < rawText.Length)
                    {
                        int terminator = rawText.LastIndexOf("||");
                        string pMessage = terminator > msgStart
                            ? rawText.Substring(msgStart, terminator - msgStart).Trim()
                            : rawText.Substring(msgStart).Trim();

                        return new LogEntry
                        {
                            Date = pipeDate,
                            ThreadName = pool.Intern(pThread),
                            Level = pool.Intern(pLevel),
                            Logger = pool.Intern(pLogger),
                            Message = pMessage,
                            ProcessName = pool.Intern("APP"),
                            Method = pool.Intern(pLocation),
                            Pattern = string.IsNullOrEmpty(pPattern) ? null : pPattern
                        };
                    }
                }
            }

            return null;
        }
    }
}
