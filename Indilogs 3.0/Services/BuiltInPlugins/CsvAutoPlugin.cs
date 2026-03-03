/*
 * CsvAutoPlugin — IndiLogs 3.0 Built-in Parser
 * =============================================
 * Handles generic CSV files with automatic header detection.
 * Auto-maps well-known column names to LogEntryDto fields;
 * all remaining columns go to ExtraFields for display.
 *
 * Excluded: event-history and pressEvents CSV files which are
 * handled by the built-in EventsCsv parser.
 *
 * Example input:
 *   Timestamp,Level,Component,Message,Duration
 *   2026-01-15 10:32:01,ERROR,Database,Query failed,250
 *   2026-01-15 10:32:02,INFO,API,Request handled,12
 */

using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    internal class CsvAutoPlugin : ILogFilePlugin
    {
        public string Name    => "CSV Auto-Detect (Built-in)";
        public string Version => "1.0.0";

        public string[] SupportedExtensions => new[] { "*.csv" };

        // ── Detection ─────────────────────────────────────────────────────────
        public bool CanHandle(string fileName, string[]? sampleLines)
        {
            string lower = fileName.ToLowerInvariant();
            if (!lower.EndsWith(".csv")) return false;

            // Leave the built-in EventsCsv parser's files alone
            string nameOnly = Path.GetFileNameWithoutExtension(lower);
            if (nameOnly.StartsWith("event-history__from") ||
                nameOnly.StartsWith("pressevents"))
                return false;

            // Must have a header line with at least one comma
            if (sampleLines == null || sampleLines.Length == 0) return false;
            string header = sampleLines[0];
            return !string.IsNullOrWhiteSpace(header) && header.Contains(",");
        }

        // ── Column layout (built dynamically after first parse; static default here) ──
        public IReadOnlyList<PluginColumnDef> GetColumns()
        {
            // Return a minimal default; the real columns are built at parse time
            // because they depend on which CSV headers are present.
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Timestamp", Field = "Date",    Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",     Field = "Level",   Width = 75  },
                new PluginColumnDef { Header = "Message",   Field = "Message", Width = -1  },
            };
        }

        // ── Parse ─────────────────────────────────────────────────────────────
        public IEnumerable<LogEntryDto> Parse(
            Stream stream, ParseContext context,
            IProgress<double>? progress, CancellationToken ct)
        {
            stream.Position = 0;
            long totalBytes = stream.CanSeek ? stream.Length : 0;
            int  lineNum    = 0;

            using (var reader = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true))
            {
                // ── Parse header ────────────────────────────────────────────
                string? headerLine = reader.ReadLine();
                if (headerLine == null) yield break;

                string[] headers = SplitCsvLine(headerLine);

                // Map header indices to known fields
                int tsIdx     = FindColumn(headers, "time", "date", "timestamp", "ts", "datetime");
                int levelIdx  = FindColumn(headers, "level", "severity", "priority", "loglevel", "log_level");
                int msgIdx    = FindColumn(headers, "message", "msg", "description", "text", "log", "body", "content");
                int threadIdx = FindColumn(headers, "thread", "threadname", "thread_name");
                int loggerIdx = FindColumn(headers, "logger", "category", "source", "component", "module");

                // Remaining indices → ExtraFields
                var extraIndices = new List<(int idx, string name)>();
                var knownIdx = new HashSet<int>(new[] { tsIdx, levelIdx, msgIdx, threadIdx, loggerIdx }
                    .Where(i => i >= 0));
                for (int i = 0; i < headers.Length; i++)
                    if (!knownIdx.Contains(i) && !string.IsNullOrWhiteSpace(headers[i]))
                        extraIndices.Add((i, headers[i].Trim()));

                // ── Parse rows ──────────────────────────────────────────────
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lineNum++;

                    if (lineNum % 500 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = SplitCsvLine(line);

                    // Build ExtraFields for this row
                    Dictionary<string, string> extra = null;
                    if (extraIndices.Count > 0)
                    {
                        extra = new Dictionary<string, string>(extraIndices.Count);
                        foreach (var (idx, name) in extraIndices)
                        {
                            string v = SafeGet(parts, idx);
                            if (!string.IsNullOrEmpty(v))
                                extra[name] = v;
                        }
                        if (extra.Count == 0) extra = null;
                    }

                    yield return new LogEntryDto
                    {
                        Date        = ParseDate(SafeGet(parts, tsIdx)),
                        Level       = NormalizeLevel(SafeGet(parts, levelIdx)),
                        Message     = SafeGet(parts, msgIdx) ?? "",
                        ThreadName  = SafeGet(parts, threadIdx),
                        Logger      = SafeGet(parts, loggerIdx),
                        ExtraFields = extra
                    };
                }
            }

            progress?.Report(100);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Finds the first header index whose name contains any of the given keywords.</summary>
        private static int FindColumn(string[] headers, params string[] keywords)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
                foreach (var kw in keywords)
                    if (h == kw || h.Contains(kw))
                        return i;
            }
            return -1;
        }

        private static string? SafeGet(string[] parts, int idx)
        {
            if (idx < 0 || idx >= parts.Length) return null;
            string v = parts[idx].Trim().Trim('"');
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static readonly string[] _dateFmts =
        {
            // High-precision formats first (7→1 fractional digits)
            "yyyy-MM-dd HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ss,fffffff",
            "yyyy-MM-dd HH:mm:ss.ffffff",  "yyyy-MM-dd HH:mm:ss,ffffff",
            "yyyy-MM-dd HH:mm:ss.fffff",   "yyyy-MM-dd HH:mm:ss,fffff",
            "yyyy-MM-dd HH:mm:ss.ffff",    "yyyy-MM-dd HH:mm:ss,ffff",
            "yyyy-MM-dd HH:mm:ss.fff",     "yyyy-MM-dd HH:mm:ss,fff",
            "yyyy-MM-dd HH:mm:ss.ff",      "yyyy-MM-dd HH:mm:ss.f",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffffffZ", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffffff",  "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss",
            "MM/dd/yyyy HH:mm:ss",          "dd/MM/yyyy HH:mm:ss"
        };

        private static DateTime ParseDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.Now;
            string trimmed = raw.Trim();
            if (DateTime.TryParseExact(trimmed, _dateFmts,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            // Normalize comma→period for variable-length fractional seconds
            string normalized = trimmed.Replace(',', '.');
            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt))
                return dt;
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt))
                return dt;
            return DateTime.Now;
        }

        private static string NormalizeLevel(string? raw)
        {
            if (raw == null) return "Info";
            string u = raw.ToUpperInvariant().Trim();
            if (u == "FATAL" || u == "CRITICAL") return "Fatal";
            if (u == "ERROR")  return "Error";
            if (u.StartsWith("WARN")) return "Warning";
            if (u.StartsWith("INFO")) return "Info";
            if (u == "DEBUG")  return "Debug";
            if (u == "TRACE" || u == "VERBOSE") return "Trace";
            return string.IsNullOrEmpty(raw) ? "Info" : raw;
        }

        /// <summary>Minimal RFC-4180 CSV line splitter (handles quoted fields).</summary>
        private static string[] SplitCsvLine(string line)
        {
            var result  = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
