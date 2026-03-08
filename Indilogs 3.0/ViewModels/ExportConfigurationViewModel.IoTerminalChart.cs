using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel
    {
        /// <summary>
        /// Builds chart package from IoTerminal CSV data, clipped to the PLC log time range.
        /// MachineState is parsed from PlcMngr transitions in session.Logs (same as States window).
        /// </summary>
        internal static ChartDataPackage BuildIoTerminalChartPackage(
            List<IoDeviceData> devices,
            List<string> selectedKeys,
            LogSessionData sessionData,
            IProgress<(double pct, string msg)>? progress = null)
        {
            var empty = new ChartDataPackage
            {
                SessionName = "IO Terminal",
                CreatedAt = DateTime.Now,
                TimeStamps = new List<DateTime>(),
                Signals = new List<SignalData>(),
                States = new List<StateData>(),
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>()
            };

            if (devices == null || !devices.Any() || !selectedKeys.Any()) return empty;

            progress?.Report((5, $"Merging timeline from {devices.Count} devices..."));

            // ── 1. Build merged timeline, clipped to PLC log range ──────────
            //    IoTerminal CSV "Timestamp" is time-only (HH:mm:ss.fff) →
            //    row.Timestamp has date 0001-01-01.  Prefer RawTime (Unix ns)
            //    for proper full DateTime; fallback: combine log date + CSV time.
            DateTime logStart = DateTime.MinValue, logEnd = DateTime.MaxValue;
            if (sessionData?.Logs != null && sessionData.Logs.Count > 0)
            {
                // Logs are already sorted by Date during loading (LogFileService)
                logStart = sessionData.Logs[0].Date;
                logEnd = sessionData.Logs[sessionData.Logs.Count - 1].Date;
            }

            DateTime logDate = logStart > DateTime.MinValue ? logStart.Date : DateTime.Today;

            var allRowsRaw = devices
                .SelectMany(d => d.Rows.Select(r =>
                {
                    // Best: full DateTime from rawTime (Unix nanoseconds)
                    var fullDt = RawTimeToDateTime(r.RawTime);
                    // Fallback: combine PLC log date with CSV time-of-day
                    if (fullDt == DateTime.MinValue && r.Timestamp > DateTime.MinValue)
                        fullDt = logDate + r.Timestamp.TimeOfDay;
                    return (device: d, row: r, fullDt: fullDt);
                }))
                .OrderBy(x => x.fullDt)
                .ToList();

            // Clip to PLC log range (but keep everything if clipping empties the list)
            var allRows = allRowsRaw;
            if (logStart > DateTime.MinValue && logEnd < DateTime.MaxValue)
            {
                var clipped = allRowsRaw
                    .Where(x => x.fullDt >= logStart && x.fullDt <= logEnd)
                    .ToList();
                if (clipped.Any()) allRows = clipped;
            }

            if (!allRows.Any()) return empty;

            int dataLength = allRows.Count;
            var timestamps = allRows.Select(x => x.fullDt).ToList();

            progress?.Report((20, $"Preparing {selectedKeys.Count} signals ({allRows.Count:N0} data points)..."));

            // ── 2. Initialise signal arrays with NaN ────────────────────────
            var keyParts = new Dictionary<string, (string Dev, string Col)>(StringComparer.OrdinalIgnoreCase);
            var signalArr = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in selectedKeys)
            {
                int sep = key.IndexOf('|');
                if (sep < 0) continue;
                keyParts[key] = (key.Substring(0, sep), key.Substring(sep + 1));
                var arr = new double[dataLength];
                for (int j = 0; j < dataLength; j++) arr[j] = double.NaN;
                signalArr[key] = arr;
            }

            progress?.Report((30, $"Filling signal values..."));

            // ── 3. Fill values at each merged timeline index ────────────────
            //    Pre-group selected keys by device name to avoid O(N×M) inner loop.
            //    For each row we only iterate keys that match its device → O(N × avgKeysPerDevice).
            var keysByDevice = new Dictionary<string, List<(string Key, string Col)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in keyParts)
            {
                if (!keysByDevice.TryGetValue(kvp.Value.Dev, out var list))
                {
                    list = new List<(string, string)>();
                    keysByDevice[kvp.Value.Dev] = list;
                }
                list.Add((kvp.Key, kvp.Value.Col));
            }

            int totalRows = allRows.Count;
            int lastPctReport = 0;
            for (int i = 0; i < totalRows; i++)
            {
                var (device, row, _) = allRows[i];
                if (!keysByDevice.TryGetValue(device.DeviceName, out var devKeys)) continue;
                for (int k = 0; k < devKeys.Count; k++)
                {
                    var (key, col) = devKeys[k];
                    if (row.Values.TryGetValue(col, out string? strVal) &&
                        double.TryParse(strVal, NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out double dval))
                    {
                        signalArr[key][i] = dval;
                    }
                }

                // Report progress every 5%
                int pct = (int)((double)i / totalRows * 50); // 30-80% range
                if (pct > lastPctReport)
                {
                    lastPctReport = pct;
                    progress?.Report((30 + pct, $"Processing row {i:N0} / {totalRows:N0}..."));
                }
            }

            progress?.Report((80, "Forward-filling gaps..."));

            // ── 4. Forward-fill NaN gaps per signal ─────────────────────────
            foreach (var arr in signalArr.Values)
            {
                double last = double.NaN;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!double.IsNaN(arr[i])) last = arr[i];
                    else if (!double.IsNaN(last)) arr[i] = last;
                }
            }

            progress?.Report((85, $"Building {signalArr.Count} signal objects..."));

            // ── 5. Build SignalData objects ──────────────────────────────────
            var signals = new List<SignalData>();
            foreach (var kvp in signalArr)
            {
                var (cat, name) = keyParts[kvp.Key];

                // Single-pass digital detection (avoids double LINQ enumeration)
                bool isDigital = true;
                var vals = kvp.Value;
                for (int i = 0; i < vals.Length; i++)
                {
                    double v = vals[i];
                    if (!double.IsNaN(v) && v != 0.0 && v != 1.0)
                    {
                        isDigital = false;
                        break; // short-circuit on first non-binary value
                    }
                }

                signals.Add(new SignalData
                {
                    Name = $"{cat}-{name}",
                    Category = "IO",
                    SignalType = isDigital ? SignalType.Digital : SignalType.Analog,
                    Data = vals
                });
            }

            progress?.Report((90, "Building machine state data..."));

            // ── 6. Build MachineState from CSV MachineState column ─────────
            //    Each IoDataRow already has MachineState parsed from the CSV.
            var states = new List<StateData>();
            {
                var stateIntervals = new List<StateInterval>();
                string? currentState = null;
                int intervalStart = 0;

                for (int i = 0; i < allRows.Count; i++)
                {
                    string? rowState = allRows[i].row.MachineState;
                    if (string.IsNullOrEmpty(rowState)) rowState = currentState; // forward-fill

                    if (rowState != currentState)
                    {
                        // Close previous interval
                        if (currentState != null)
                        {
                            stateIntervals.Add(new StateInterval
                            {
                                StartIndex = intervalStart,
                                EndIndex = Math.Max(intervalStart, i - 1),
                                StateId = Models.Charts.ChartStateConfig.GetId(currentState),
                                StateName = currentState
                            });
                        }
                        currentState = rowState;
                        intervalStart = i;
                    }
                }

                // Close last interval
                if (currentState != null)
                {
                    stateIntervals.Add(new StateInterval
                    {
                        StartIndex = intervalStart,
                        EndIndex = dataLength - 1,
                        StateId = Models.Charts.ChartStateConfig.GetId(currentState),
                        StateName = currentState
                    });
                }

                if (stateIntervals.Count > 0)
                {
                    states.Add(new StateData
                    {
                        Name = "MachineState",
                        Category = "PlcMngr",
                        Intervals = stateIntervals
                    });
                }
            }

            progress?.Report((100, $"Done — {signals.Count} signals, {timestamps.Count:N0} points"));

            return new ChartDataPackage
            {
                SessionName = sessionData?.FileName ?? "IO Terminal",
                CreatedAt = DateTime.Now,
                TimeStamps = timestamps,
                Signals = signals,
                States = states,
                ThreadMessages = new List<ThreadMessageData>(),
                Events = new List<EventMarkerData>(),
                SuppressGapDetection = true
            };
        }

        /// <summary>Converts IoTerminal rawTime (Unix nanoseconds) to local DateTime.</summary>
        internal static DateTime RawTimeToDateTime(long rawTimeNs)
        {
            if (rawTimeNs <= 0) return DateTime.MinValue;
            try
            {
                long seconds = rawTimeNs / 1_000_000_000L;
                long remainNs = rawTimeNs % 1_000_000_000L;
                var dt = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                // Add sub-second ticks (1 tick = 100 ns)
                return dt.AddTicks(remainNs / 100L);
            }
            catch (Exception ex) { AppLogger.Error("RawTimeToDateTime failed", ex); return DateTime.MinValue; }
        }
    }
}
