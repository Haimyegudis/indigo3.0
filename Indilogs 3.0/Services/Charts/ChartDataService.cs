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
    public class ChartDataService : IDisposable
    {
        private FileStream _fileStream;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private unsafe byte* _ptr;
        private long _fileLength;
        private List<long> _lineOffsets;

        public List<string> ColumnNames { get; private set; }
        public List<string> RawColumnNames { get; private set; }
        public int TotalRows => _lineOffsets.Count;
        public int DataStartRow { get; private set; } = 3;
        public CsvFormat DetectedFormat { get; private set; } = CsvFormat.Unknown;
        public string LoadedFilePath { get; private set; }
        public bool IsLoaded => _mmf != null;

        public ChartDataService()
        {
            _lineOffsets = new List<long>();
            ColumnNames = new List<string>();
            RawColumnNames = new List<string>();
        }

        public unsafe void Load(string filePath)
        {
            // Dispose previous file if any
            Dispose();

            var info = new FileInfo(filePath);
            _fileLength = info.Length;
            LoadedFilePath = filePath;

            // Open file with read-only access and allow sharing with other processes
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _mmf = MemoryMappedFile.CreateFromFile(_fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            _accessor = _mmf.CreateViewAccessor(0, _fileLength, MemoryMappedFileAccess.Read);

            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _ptr = ptr;

            ParseStructure();
        }

        private unsafe void ParseStructure()
        {
            _lineOffsets.Clear();
            long currentOffset = 0;
            while (currentOffset < _fileLength)
            {
                _lineOffsets.Add(currentOffset);
                while (currentOffset < _fileLength && _ptr[currentOffset] != (byte)'\n')
                {
                    currentOffset++;
                }
                currentOffset++;
            }

            // Detect format
            DetectedFormat = DetectFormat();

            switch (DetectedFormat)
            {
                case CsvFormat.YTScope:
                    ParseYTScopeFormat();
                    break;
                case CsvFormat.PlcIos:
                    ParsePlcIosFormat();
                    break;
                case CsvFormat.Legacy:
                default:
                    ParseHierarchicalHeader();
                    break;
            }
        }

        private CsvFormat DetectFormat()
        {
            if (_lineOffsets.Count < 2) return CsvFormat.Legacy;

            string firstLine = ReadLineAsString(0);
            string secondLine = _lineOffsets.Count > 1 ? ReadLineAsString(1) : "";

            // Check for YT Scope format
            if (firstLine.StartsWith("Name,YT Scope Project") || firstLine.StartsWith("Name,YT Scope"))
            {
                return CsvFormat.YTScope;
            }

            // Check for PLC-IOS format
            if (firstLine.Contains("PolicyName") || firstLine.Contains("Data.OPCUAInterface") ||
                firstLine.Contains("Unix_Time") || firstLine.Contains("Machine_State") ||
                (secondLine.Contains("T") && secondLine.Contains(":") && secondLine.Contains("-")))
            {
                return CsvFormat.PlcIos;
            }

            return CsvFormat.Legacy;
        }

        private void ParsePlcIosFormat()
        {
            DataStartRow = 1;
            var line = ReadLineAsString(0).Split(',');
            ColumnNames.Clear();
            RawColumnNames.Clear();

            for (int i = 0; i < line.Length; i++)
            {
                string raw = line[i].Trim().Trim('"');
                RawColumnNames.Add(raw);

                // Simplify long OPC-UA style names
                string[] parts = raw.Split('.');
                if (parts.Length > 4)
                {
                    ColumnNames.Add(string.Join(".", parts.Skip(parts.Length - 4)));
                }
                else
                {
                    ColumnNames.Add(raw);
                }
            }
        }

        private void ParseYTScopeFormat()
        {
            DataStartRow = 9;

            // Find the line that starts with "Name," and contains actual column names
            int nameLineIndex = -1;
            for (int i = 0; i < Math.Min(10, _lineOffsets.Count); i++)
            {
                string line = ReadLineAsString(i);
                if (line.StartsWith("Name,") && (line.Contains("Station.") || line.Contains("gStation") || line.Contains("arrInk")))
                {
                    nameLineIndex = i;
                    break;
                }
            }

            if (nameLineIndex == -1)
            {
                // Fallback: look for the line with the most commas
                int maxCommas = 0;
                for (int i = 0; i < Math.Min(10, _lineOffsets.Count); i++)
                {
                    string line = ReadLineAsString(i);
                    int commaCount = line.Count(c => c == ',');
                    if (commaCount > maxCommas)
                    {
                        maxCommas = commaCount;
                        nameLineIndex = i;
                    }
                }
            }

            // Find the SampleTime line to determine where data starts
            for (int i = 0; i < Math.Min(15, _lineOffsets.Count); i++)
            {
                string line = ReadLineAsString(i);
                if (line.StartsWith("SampleTime"))
                {
                    DataStartRow = i + 1;
                    break;
                }
            }

            ColumnNames.Clear();
            RawColumnNames.Clear();

            if (nameLineIndex >= 0)
            {
                var cols = ReadLineAsString(nameLineIndex).Split(',');
                for (int i = 0; i < cols.Length; i++)
                {
                    string raw = cols[i].Trim().Trim('"');
                    RawColumnNames.Add(raw);
                    ColumnNames.Add(SimplifyYTScopeName(raw));
                }
            }
        }

        private string SimplifyYTScopeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            string result = raw;

            if (result.StartsWith("Station.pArr"))
            {
                result = result.Substring("Station.pArr".Length);
            }
            else if (result.StartsWith("gStationAxes_"))
            {
                result = result.Replace("gStationAxes_", "Stn");
            }
            else if (result.StartsWith("arrInk["))
            {
                result = "Ink" + result.Substring("arrInk".Length);
            }

            result = result.Replace("^.", ".");

            return result;
        }

        private unsafe void ParseHierarchicalHeader()
        {
            if (_lineOffsets.Count < 3) return;
            DataStartRow = 3;

            var line1 = ReadLineAsString(0).Split(',');
            var line2 = ReadLineAsString(1).Split(',');
            var line3 = ReadLineAsString(2).Split(',');

            int cols = line1.Length;
            ColumnNames.Clear();
            RawColumnNames.Clear();

            for (int i = 0; i < cols; i++)
            {
                string p1 = (i < line1.Length) ? line1[i].Trim() : "";
                string p2 = (i < line2.Length) ? line2[i].Trim() : "";
                string p3 = (i < line3.Length) ? line3[i].Trim() : "";

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(p1)) parts.Add(p1);
                if (!string.IsNullOrWhiteSpace(p2)) parts.Add(p2);
                if (!string.IsNullOrWhiteSpace(p3)) parts.Add(p3);

                string fullName = string.Join("_", parts);
                if (string.IsNullOrWhiteSpace(fullName)) fullName = $"Column_{i}";

                ColumnNames.Add(fullName);
                RawColumnNames.Add(fullName);
            }
        }

        private unsafe string ReadLineAsString(int index)
        {
            if (index >= _lineOffsets.Count) return "";
            long start = _lineOffsets[index];
            long end = (index + 1 < _lineOffsets.Count) ? _lineOffsets[index + 1] - 1 : _fileLength;
            if (end > start && _ptr[end - 1] == '\r') end--;

            int len = (int)(end - start);
            if (len <= 0) return "";

            byte[] buffer = new byte[len];
            Marshal.Copy((IntPtr)(_ptr + start), buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }

        public unsafe string GetStringAt(int rowIndex, int colIndex)
        {
            if (rowIndex >= _lineOffsets.Count) return "";

            long start = _lineOffsets[rowIndex];
            long end = (rowIndex + 1 < _lineOffsets.Count) ? _lineOffsets[rowIndex + 1] - 1 : _fileLength;
            if (end > start && _ptr[end - 1] == '\r') end--;

            int len = (int)(end - start);
            byte[] buffer = new byte[len];
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
            byte[] buffer = new byte[len];
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
            string pattern = namePattern.ToLower();

            // Exact match first
            for (int i = 0; i < ColumnNames.Count; i++)
            {
                if (ColumnNames[i].Equals(namePattern, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            // Partial match
            for (int i = 0; i < ColumnNames.Count; i++)
            {
                if (ColumnNames[i].ToLower().Contains(pattern))
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
