/*
 * IoTerminalDataService — IndiLogs 3.0
 * =====================================
 * Parses Io-*.csv files from the TerminalLogs folder in binary-APP log ZIPs.
 * Provides component lists for ExportConfigurationViewModel and the merged
 * CSV export for selected IO signals.
 *
 * File naming: Io-BIM[0]_e[1]-Mon_P000_...csv → device "BIM[0]"
 *              Io-ECM_e[1]-...csv              → device "ECM"
 *
 * CSV columns: Ticks, rawTime (Unix ns), Timestamp (HH:MM:SS.mmm),
 *              MachineState, TotalImpressions, [device-specific IO columns]
 */

using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    // ── Data models ─────────────────────────────────────────────────────────

    public class IoDeviceData
    {
        public string DeviceName  { get; set; }   // e.g. "BIM[0]", "ECM", "PDC"
        public string FileName    { get; set; }   // original filename key
        public string[] Columns   { get; set; }   // non-standard IO columns only
        public List<IoDataRow> Rows { get; set; } = new List<IoDataRow>();
    }

    public class IoDataRow
    {
        public long     RawTime      { get; set; }   // rawTime (Unix ns) — used for sub-ms ordering
        public DateTime Timestamp    { get; set; }   // parsed from Timestamp column
        public string   MachineState { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    // ── Service ─────────────────────────────────────────────────────────────

    public class IoTerminalDataService
    {
        // Standard columns that are not IO signals
        private static readonly HashSet<string> _stdCols = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
            { "Ticks", "rawTime", "Timestamp", "MachineState", "TotalImpressions" };

        // ── Parsing ────────────────────────────────────────────────────────

        /// <summary>
        /// Parses all Io-*.csv entries from the TerminalCsvBytes dictionary (raw byte arrays).
        /// Falls back to TerminalLogFiles for backward compatibility.
        /// Parsing is parallelized for multi-file scenarios.
        /// </summary>
        public List<IoDeviceData> ParseIoFiles(Dictionary<string, string> terminalLogFiles,
                                                Dictionary<string, byte[]> terminalCsvBytes = null)
        {
            var devices = new System.Collections.Concurrent.ConcurrentBag<IoDeviceData>();

            // Collect IO CSV entries from both sources
            var ioEntries = new List<(string fileName, Func<string> getContent)>();

            // Prefer byte[] source (faster — avoids double-conversion during ZIP load)
            if (terminalCsvBytes != null)
            {
                foreach (var kvp in terminalCsvBytes)
                {
                    string fn = kvp.Key;
                    if (!fn.StartsWith("Io-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!fn.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] bytes = kvp.Value;
                    ioEntries.Add((fn, () => Encoding.UTF8.GetString(bytes)));
                }
            }

            // Fallback: string dictionary (legacy/backward-compat)
            if (terminalLogFiles != null)
            {
                foreach (var kvp in terminalLogFiles)
                {
                    string fn = kvp.Key;
                    if (!fn.StartsWith("Io-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!fn.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                    // Skip if already found in byte[] source
                    if (ioEntries.Exists(e => string.Equals(e.fileName, fn, StringComparison.OrdinalIgnoreCase))) continue;
                    string content = kvp.Value;
                    ioEntries.Add((fn, () => content));
                }
            }

            // Parse in parallel for multi-file scenarios
            Parallel.ForEach(ioEntries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entry =>
            {
                try
                {
                    var device = ParseOneCsvFile(entry.fileName, entry.getContent());
                    if (device != null) devices.Add(device);
                }
                catch
                {
                    // Skip malformed files
                }
            });

            return devices.OrderBy(d => d.DeviceName).ToList();
        }

        private static IoDeviceData ParseOneCsvFile(string fileName, string csvContent)
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return null;

            using (var reader = new StringReader(csvContent))
            {
                string headerLine = reader.ReadLine();
                if (string.IsNullOrEmpty(headerLine)) return null;

                string[] headers = SplitCsv(headerLine);

                // Identify column indices
                int rawTimeIdx     = IndexOf(headers, "rawTime");
                int timestampIdx   = IndexOf(headers, "Timestamp");
                int machineStateIdx = IndexOf(headers, "MachineState");

                // Determine IO-specific columns (exclude standard ones)
                var ioCols = new List<string>();
                var ioColIdx = new List<int>();
                for (int i = 0; i < headers.Length; i++)
                {
                    string h = headers[i].Trim();
                    if (!_stdCols.Contains(h) && !string.IsNullOrEmpty(h))
                    {
                        ioCols.Add(h);
                        ioColIdx.Add(i);
                    }
                }

                var device = new IoDeviceData
                {
                    DeviceName = ExtractDeviceName(fileName),
                    FileName   = fileName,
                    Columns    = ioCols.ToArray(),
                };

                // Parse rows — use known column count for pre-sized split
                int colCount = headers.Length;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = SplitCsv(line, colCount);

                    var row = new IoDataRow
                    {
                        RawTime      = ParseLong(SafeGet(parts, rawTimeIdx)),
                        Timestamp    = ParseTimestamp(SafeGet(parts, timestampIdx)),
                        MachineState = SafeGet(parts, machineStateIdx) ?? "",
                        Values       = new Dictionary<string, string>(ioColIdx.Count)
                    };

                    for (int i = 0; i < ioColIdx.Count; i++)
                    {
                        string v = SafeGet(parts, ioColIdx[i]);
                        // Always store value (even null → "") so chart builder TryGetValue succeeds.
                        // Empty strings will fail double.TryParse → NaN → forward-fill handles gaps.
                        row.Values[ioCols[i]] = v ?? "";
                    }

                    device.Rows.Add(row);
                }

                return device;
            }
        }

        // ── Component list for ExportConfigurationViewModel ────────────────

        /// <summary>
        /// Builds SelectableItem-compatible list from all parsed devices.
        /// Category = device name, Name = column name.
        /// Key used in export: "Category|Name" (same format as normal IO).
        /// </summary>
        public List<SelectableItem> GetAllComponents(List<IoDeviceData> devices)
        {
            var result = new List<SelectableItem>();
            foreach (var device in devices)
            {
                foreach (var col in device.Columns)
                {
                    result.Add(new SelectableItem
                    {
                        Category   = device.DeviceName,
                        Name       = col,
                        IsSelected = false,
                    });
                }
            }
            return result;
        }

        // ── Merged CSV Export ──────────────────────────────────────────────

        /// <summary>
        /// Exports a merged CSV from the selected IO components.
        /// Rows are sorted by RawTime ascending; values are forward-filled.
        /// Timestamp uses microsecond precision derived from rawTime.
        /// </summary>
        public async Task ExportMergedCsvAsync(
            List<IoDeviceData> devices,
            List<string> selectedKeys,          // "DeviceName|ColumnName"
            string outputPath,
            IProgress<double> progress,
            CancellationToken ct)
        {
            await Task.Run(() =>
            {
                // Build lookup: "DeviceName|ColumnName" → (deviceIdx, colName)
                var colMap = new List<(int devIdx, string col, string header)>();
                for (int d = 0; d < devices.Count; d++)
                {
                    foreach (var col in devices[d].Columns)
                    {
                        string key = $"{devices[d].DeviceName}|{col}";
                        if (selectedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                            colMap.Add((d, col, key));
                    }
                }

                // Build sorted union timeline: (RawTime, machineState, devIdx, row)
                var timeline = new List<(long rawTime, int devIdx, IoDataRow row)>();
                for (int d = 0; d < devices.Count; d++)
                    foreach (var row in devices[d].Rows)
                        timeline.Add((row.RawTime, d, row));

                timeline.Sort((a, b) => a.rawTime.CompareTo(b.rawTime));

                // Forward-fill state per device/column
                var lastValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string lastMachineState = "";

                using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    // Header
                    var sb = new StringBuilder();
                    sb.Append("Timestamp,MachineState");
                    foreach (var (_, _, header) in colMap)
                    {
                        sb.Append(',');
                        // Quote if contains comma
                        if (header.Contains(',')) { sb.Append('"'); sb.Append(header); sb.Append('"'); }
                        else sb.Append(header);
                    }
                    writer.WriteLine(sb.ToString());

                    int total = timeline.Count;
                    int written = 0;

                    foreach (var (rawTime, devIdx, row) in timeline)
                    {
                        ct.ThrowIfCancellationRequested();

                        // Update last known values from this row
                        if (!string.IsNullOrEmpty(row.MachineState))
                            lastMachineState = row.MachineState;

                        foreach (var kv in row.Values)
                        {
                            string fullKey = $"{devices[devIdx].DeviceName}|{kv.Key}";
                            lastValues[fullKey] = kv.Value;
                        }

                        // Build row: Timestamp (microsecond precision), MachineState, [values]
                        sb.Clear();
                        sb.Append(FormatTimestampUs(rawTime));
                        sb.Append(',');
                        sb.Append(lastMachineState);

                        foreach (var (_, _, header) in colMap)
                        {
                            sb.Append(',');
                            if (lastValues.TryGetValue(header, out string v))
                                sb.Append(v);
                        }
                        writer.WriteLine(sb.ToString());

                        written++;
                        if (written % 500 == 0)
                            progress?.Report((double)written / total * 100.0);
                    }
                }

                progress?.Report(100.0);
            }, ct);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts device name from Io-*.csv filename.
        /// "Io-BIM[0]_e[1]-Mon_..." → "BIM[0]"
        /// "Io-ECM_e[1]-..."        → "ECM"
        /// </summary>
        public static string ExtractDeviceName(string fileName)
        {
            // Remove "Io-" prefix
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (name.StartsWith("Io-", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(3);

            // Take everything before first "_"
            int us = name.IndexOf('_');
            return us > 0 ? name.Substring(0, us) : name;
        }

        private static int IndexOf(string[] headers, string name)
        {
            for (int i = 0; i < headers.Length; i++)
                if (string.Equals(headers[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string SafeGet(string[] parts, int idx)
        {
            if (idx < 0 || idx >= parts.Length) return null;
            string v = parts[idx].Trim().Trim('"');
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static long ParseLong(string raw)
        {
            if (raw == null) return 0;
            return long.TryParse(raw, out long v) ? v : 0;
        }

        private static readonly string[] _tsFmts =
        {
            "HH:mm:ss.fffffff", "HH:mm:ss.ffffff", "HH:mm:ss.fffff",
            "HH:mm:ss.ffff",    "HH:mm:ss.fff",    "HH:mm:ss.ff", "HH:mm:ss.f",
            "HH:mm:ss", "H:mm:ss.fff", "H:mm:ss"
        };

        private static DateTime ParseTimestamp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.MinValue;
            if (DateTime.TryParseExact(raw.Trim(), _tsFmts,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            if (DateTime.TryParse(raw.Trim(), out dt))
                return dt;
            return DateTime.MinValue;
        }

        /// <summary>
        /// Converts rawTime (Unix nanoseconds) to a local HH:mm:ss.ffffff string.
        /// Falls back to the row's Timestamp field if rawTime is 0.
        /// </summary>
        private static string FormatTimestampUs(long rawTimeNs)
        {
            if (rawTimeNs == 0) return "";
            try
            {
                // rawTime is Unix epoch nanoseconds
                long us    = rawTimeNs / 1000L;                   // microseconds since epoch
                long secUs = us / 1_000_000L;                     // whole seconds since epoch
                int subUs  = (int)(us % 1_000_000L);              // sub-second microseconds

                var dt = DateTimeOffset.FromUnixTimeSeconds(secUs).LocalDateTime;
                return $"{dt:HH:mm:ss}.{subUs:D6}";
            }
            catch
            {
                return rawTimeNs.ToString();
            }
        }

        /// <summary>Minimal RFC-4180 CSV line splitter (optimized: pre-sized list, avoids ToArray).</summary>
        private static string[] SplitCsv(string line, int expectedColumns = 0)
        {
            var result  = expectedColumns > 0 ? new List<string>(expectedColumns) : new List<string>(16);
            int start   = 0;
            bool inQ    = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { inQ = !inQ; }
                else if (c == ',' && !inQ)
                {
                    result.Add(line.Substring(start, i - start).Trim('"'));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start).Trim('"'));
            return result.ToArray();
        }
    }
}
