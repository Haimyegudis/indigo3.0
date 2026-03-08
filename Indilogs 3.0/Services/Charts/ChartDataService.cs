using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Services.Charts
{
    public enum CsvFormat
    {
        Unknown,
        PlcIos,      // Single header line with Data.OPCUAInterface... or Time,PolicyName columns
        YTScope,     // 5-line header (metadata, Name, SymbolComment, Data-Type, SampleTime)
        Legacy       // 3-line hierarchical header format
    }

    /// <summary>
    /// High-performance CSV file engine using memory-mapped files for large file support
    /// </summary>
    public partial class ChartDataService : IDisposable
    {
        private FileStream? _fileStream;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private unsafe byte* _ptr;
        private long _fileLength;
        private List<long> _lineOffsets;

        // Thread-local reusable buffer to avoid per-call byte[] allocations in GetStringAt/GetValueAt
        [ThreadStatic]
        private static byte[]? t_rowBuffer;

        public List<string> ColumnNames { get; private set; }
        public List<string> RawColumnNames { get; private set; }
        public int TotalRows => _lineOffsets.Count;
        public int DataStartRow { get; private set; } = 3;
        public CsvFormat DetectedFormat { get; private set; } = CsvFormat.Unknown;
        public string? LoadedFilePath { get; private set; }
        public bool IsLoaded => _mmf != null;

        public ChartDataService()
        {
            _lineOffsets = new List<long>();
            ColumnNames = new List<string>();
            RawColumnNames = new List<string>();
        }

        private static byte[] RentRowBuffer(int minSize)
        {
            var buf = t_rowBuffer;
            if (buf == null || buf.Length < minSize)
            {
                buf = new byte[Math.Max(minSize, 4096)];
                t_rowBuffer = buf;
            }
            return buf;
        }

        public unsafe string GetStringAt(int rowIndex, int colIndex)
        {
            if (rowIndex >= _lineOffsets.Count) return "";

            long start = _lineOffsets[rowIndex];
            long end = (rowIndex + 1 < _lineOffsets.Count) ? _lineOffsets[rowIndex + 1] - 1 : _fileLength;
            if (end > start && _ptr[end - 1] == '\r') end--;

            int len = (int)(end - start);
            byte[] buffer = RentRowBuffer(len);
            Marshal.Copy((IntPtr)(_ptr + start), buffer, 0, len);

            // Handle CSV with quoted fields containing commas
            int current = 0;
            int lastComma = -1;
            bool inQuotes = false;

            for (int i = 0; i <= len; i++)
            {
                if (i < len && buffer[i] == (byte)'"')
                {
                    inQuotes = !inQuotes;
                }

                if (i == len || (buffer[i] == (byte)',' && !inQuotes))
                {
                    if (current == colIndex)
                    {
                        int sliceStart = lastComma + 1;
                        int sliceLen = i - sliceStart;
                        return Encoding.UTF8.GetString(buffer, sliceStart, sliceLen).Trim().Trim('"');
                    }
                    current++;
                    lastComma = i;
                }
            }
            return "";
        }

        public unsafe double GetValueAt(int rowIndex, int colIndex)
        {
            if (rowIndex >= _lineOffsets.Count) return double.NaN;

            long start = _lineOffsets[rowIndex];
            long end = (rowIndex + 1 < _lineOffsets.Count) ? _lineOffsets[rowIndex + 1] - 1 : _fileLength;
            if (end > start && _ptr[end - 1] == '\r') end--;

            int len = (int)(end - start);
            byte[] buffer = RentRowBuffer(len);
            Marshal.Copy((IntPtr)(_ptr + start), buffer, 0, len);

            int current = 0;
            int lastComma = -1;

            for (int i = 0; i <= len; i++)
            {
                if (i == len || buffer[i] == (byte)',')
                {
                    if (current == colIndex)
                    {
                        int sliceStart = lastComma + 1;
                        int sliceLen = i - sliceStart;
                        string valueStr = Encoding.UTF8.GetString(buffer, sliceStart, sliceLen).Trim();

                        if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            return val;
                        }
                        return double.NaN;
                    }
                    current++;
                    lastComma = i;
                }
            }
            return double.NaN;
        }

        /// <summary>
        /// Load all data for a specific column as a double array
        /// </summary>
        public double[] GetColumnData(int colIndex)
        {
            int dataRows = TotalRows - DataStartRow;
            if (dataRows <= 0) return new double[0];

            double[] data = new double[dataRows];
            for (int i = 0; i < dataRows; i++)
            {
                data[i] = GetValueAt(DataStartRow + i, colIndex);
            }
            return data;
        }

        /// <summary>
        /// Get time/timestamp column data as strings
        /// </summary>
        public string[] GetTimeColumnData(int colIndex = 0)
        {
            int dataRows = TotalRows - DataStartRow;
            if (dataRows <= 0) return new string[0];

            string[] data = new string[dataRows];
            for (int i = 0; i < dataRows; i++)
            {
                data[i] = GetStringAt(DataStartRow + i, colIndex);
            }
            return data;
        }

        /// <summary>
        /// Detect and extract state intervals from a state column
        /// </summary>
        public List<StateInterval> ExtractStates(int stateColIndex)
        {
            var states = new List<StateInterval>();
            int dataRows = TotalRows - DataStartRow;
            if (dataRows <= 0) return states;

            int currentState = -1;
            int startIndex = 0;

            for (int i = 0; i < dataRows; i++)
            {
                string rawValue = GetStringAt(DataStartRow + i, stateColIndex);
                int stateId = ChartStateConfig.GetId(rawValue);

                if (stateId != currentState)
                {
                    if (currentState != -1 && i > 0)
                    {
                        states.Add(new StateInterval { StartIndex = startIndex, EndIndex = i - 1, StateId = currentState });
                    }
                    currentState = stateId;
                    startIndex = i;
                }
            }

            // Add final state
            if (currentState != -1)
            {
                states.Add(new StateInterval { StartIndex = startIndex, EndIndex = dataRows - 1, StateId = currentState });
            }

            return states;
        }

        /// <summary>
        /// Find column index by name (case-insensitive partial match)
        /// </summary>
        public int FindColumnIndex(string namePattern)
        {
            // Exact match first
            for (int i = 0; i < ColumnNames.Count; i++)
            {
                if (ColumnNames[i].Equals(namePattern, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            // Partial match
            for (int i = 0; i < ColumnNames.Count; i++)
            {
                if (ColumnNames[i].IndexOf(namePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Extract event markers from an Events_Message column.
        /// Returns events with their row index and message text.
        /// </summary>
        public List<EventMarker> ExtractEvents(int eventsColIndex, int timeColIndex = 0)
        {
            var events = new List<EventMarker>();
            int dataRows = TotalRows - DataStartRow;
            if (dataRows <= 0) return events;

            for (int i = 0; i < dataRows; i++)
            {
                string msg = GetStringAt(DataStartRow + i, eventsColIndex);
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    string timeStr = GetStringAt(DataStartRow + i, timeColIndex);
                    events.Add(new EventMarker
                    {
                        Index = i,
                        Message = msg,
                        Time = timeStr
                    });
                }
            }

            return events;
        }

        /// <summary>
        /// Find column index for Events_Message
        /// </summary>
        public int FindEventsColumnIndex()
        {
            for (int i = 0; i < ColumnNames.Count; i++)
            {
                string name = ColumnNames[i];
                if (name.Equals("Events_Message", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("Events_Message", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            // Also check raw column names
            for (int i = 0; i < RawColumnNames.Count; i++)
            {
                string name = RawColumnNames[i];
                if (name.Equals("Events_Message", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("Events_Message", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            return -1;
        }

        public void Dispose()
        {
            if (_accessor != null)
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            _accessor?.Dispose();
            _mmf?.Dispose();
            _fileStream?.Dispose();

            _accessor = null;
            _mmf = null;
            _fileStream = null;
            _lineOffsets.Clear();
            ColumnNames.Clear();
            RawColumnNames.Clear();
            LoadedFilePath = null;
        }
    }
}
