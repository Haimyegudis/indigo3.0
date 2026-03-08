using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public partial class IoTerminalDataService
    {
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
                            if (lastValues.TryGetValue(header, out string? v))
                                sb.Append(v);
                        }
                        writer.WriteLine(sb.ToString());

                        written++;
                        if (written % 500 == 0)
                            progress?.Report((double)written / total * 100.0);
                    }
                }

                progress?.Report(100.0);
            }, ct).ConfigureAwait(false);
        }
    }
}
