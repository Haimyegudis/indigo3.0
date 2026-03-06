using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IndiLogs_3._0.Services.Charts
{
    public partial class ChartDataService
    {
        public unsafe void Load(string filePath)
        {
            // Dispose previous file if any
            Dispose();

            var info = new System.IO.FileInfo(filePath);
            _fileLength = info.Length;
            LoadedFilePath = filePath;

            // Open file with read-only access and allow sharing with other processes
            _fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            _mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(_fileStream, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read, System.IO.HandleInheritability.None, false);
            _accessor = _mmf.CreateViewAccessor(0, _fileLength, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);

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
    }
}
