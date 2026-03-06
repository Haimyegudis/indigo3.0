using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        private enum FileType { MainLog, AppDevLog, AppBinaryLog, EventsCsv, EventsXml, Plugin }

        /// <summary>
        /// Checks whether a file name matches known events file patterns (CSV or XML).
        /// Returns true and sets <paramref name="type"/> accordingly.
        /// </summary>
        private static bool IsEventsFile(string fileName, out FileType type)
        {
            string name = Path.GetFileName(fileName);
            bool isEventHistory = name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase);
            bool isPressEvents = name.StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase);

            if (!isEventHistory && !isPressEvents) { type = default; return false; }

            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            { type = FileType.EventsCsv; return true; }

            if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            { type = FileType.EventsXml; return true; }

            type = default;
            return false;
        }

        private class ZipEntryData
        {
            public string Name = "";
            public string EntryFullName = ""; // For deferred CopyToMemory (pipeline extraction)
            public FileType Type;
            public MemoryStream Stream = null!;
            // Set when Type == Plugin:
            public ILogFilePlugin Plugin = null!;
            public ParseContext Context = null!;
        }

        /// <summary>Fast inline check: "YYYY-MM-DD HH:MM:SS,ddd" — replaces _dateStartPattern regex.</summary>
        private static bool IsDateStart(string line)
        {
            if (line.Length < 23) return false;
            return line[4] == '-' && line[7] == '-' && line[10] == ' '
                && line[13] == ':' && line[16] == ':' && line[19] == ','
                && (uint)(line[0] - '0') <= 9 && (uint)(line[5] - '0') <= 9
                && (uint)(line[8] - '0') <= 9 && (uint)(line[11] - '0') <= 9;
        }

        /// <summary>Fast manual timestamp parse: "YYYY-MM-DD HH:MM:SS,ddddddd" — avoids DateTime.TryParse overhead.</summary>
        private static DateTime ParseTimestampFast(string ts)
        {
            // ts is at least 23 chars (checked by IsDateStart)
            int year = (ts[0] - '0') * 1000 + (ts[1] - '0') * 100 + (ts[2] - '0') * 10 + (ts[3] - '0');
            int month = (ts[5] - '0') * 10 + (ts[6] - '0');
            int day = (ts[8] - '0') * 10 + (ts[9] - '0');
            int hour = (ts[11] - '0') * 10 + (ts[12] - '0');
            int minute = (ts[14] - '0') * 10 + (ts[15] - '0');
            int second = (ts[17] - '0') * 10 + (ts[18] - '0');

            // Fractional: parse 3-7 digits after comma at position 20
            long ticks = 0;
            int digits = 0;
            for (int i = 20; i < ts.Length && (uint)(ts[i] - '0') <= 9; i++)
            {
                ticks = ticks * 10 + (ts[i] - '0');
                digits++;
            }
            // Normalize to 100ns ticks: 3 digits=ms→*10000, 7 digits=ticks directly
            switch (digits)
            {
                case 3: ticks *= 10000; break;
                case 4: ticks *= 1000; break;
                case 5: ticks *= 100; break;
                case 6: ticks *= 10; break;
                case 7: break;
                default: ticks = 0; break;
            }

            try
            {
                return new DateTime(year, month, day, hour, minute, second).AddTicks(ticks);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"DateTime construction failed: {ex.Message}");
                return DateTime.MinValue;
            }
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private (string sw, string plc) ParseReadmeVersions(string content)
        {
            try
            {
                var sw = Regex.Match(content, @"Version[:=]\s*(.+)", RegexOptions.IgnoreCase, AppConstants.RegexTimeout);
                var plc = Regex.Match(content, @"PressPlcVersion[:=]\s*(.+)", RegexOptions.IgnoreCase, AppConstants.RegexTimeout);
                return (sw.Success ? sw.Groups[1].Value.Trim() : "Unknown", plc.Success ? plc.Groups[1].Value.Trim() : "Unknown");
            }
            catch (Exception ex) { AppLogger.Error("ParseReadmeVersions failed", ex); return ("Unknown", "Unknown"); }
        }

        private string? ExtractPlcVersionFromSetupInfo(string jsonContent)
        {
            try
            {
                var match = Regex.Match(jsonContent, @"\""Name\""\s*:\s*\""press-content-mcs-plc\""[\s\S]*?\""Version\""\s*:\s*\""(?<ver>[^\""]+)\""", RegexOptions.IgnoreCase, AppConstants.RegexTimeout);
                if (match.Success) return match.Groups["ver"].Value.Trim();
            }
            catch (Exception ex) { AppLogger.Error("ExtractPlcVersionFromSetupInfo failed", ex); }
            return null;
        }

        /// <summary>
        /// Cache-friendly sort: extracts Date.Ticks into a contiguous long[] array so
        /// comparisons access sequential memory instead of chasing object pointers.
        /// 4-8x faster than List.Sort for millions of entries due to CPU cache locality.
        /// </summary>
        private static List<LogEntry> SortLogEntriesCacheFriendly(List<LogEntry> list)
        {
            int count = list.Count;
            if (count <= 1) return list;

            var ticks = new long[count];
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                ticks[i] = list[i].Date.Ticks;
                indices[i] = i;
            }

            Array.Sort(ticks, indices);
            // indices[i] = original position of item that belongs at sorted position i.
            // To apply this forward permutation in-place we need the inverse:
            // inverse[originalPos] = sortedPos
            var inverse = new int[count];
            for (int i = 0; i < count; i++)
                inverse[indices[i]] = i;

            // In-place cycle-chase using the inverse permutation
            for (int i = 0; i < count; i++)
            {
                while (inverse[i] != i)
                {
                    int j = inverse[i];
                    var temp = list[i];
                    list[i] = list[j];
                    list[j] = temp;
                    inverse[i] = inverse[j];
                    inverse[j] = j;
                }
            }

            return list;
        }

        private double CalculatePercent(long processed, long total) => total == 0 ? 0 : Math.Min(99, ((double)processed / total) * 100);
    }
}
