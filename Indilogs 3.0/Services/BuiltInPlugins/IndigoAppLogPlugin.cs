/*
 * IndigoAppLogPlugin — IndiLogs 3.0 Built-in Parser
 * ===================================================
 * Handles INDIGO APP text log files in two formats:
 *
 *   • Old format: fields delimited by \x1e (ASCII Record Separator, 0x1E).
 *     Each log entry is a single line:
 *     TIMESTAMP\x1eTHREAD\x1eROOTIFLOWID\x1eIFLOWID\x1eIFLOWNAME\x1ePATTERN\x1eCONTEXT\x1eLEVEL LOGGER\x1eLOCATION\x1eMESSAGE\x1eEXCEPTION\x1eDATA
 *
 *   • New format: pipe-delimited, multi-line entries ending with ||:
 *     2026-01-29 10:32:38,073 |Thread| |RootIFlowId| |IFlowId| |IFlowName| |Pattern| |Context| LEVEL Logger
 *     |Location|
 *     Message text...
 *     ||
 *
 * Timestamp format: yyyy-MM-dd HH:mm:ss,fff  (comma before milliseconds)
 *
 * Parsed entries are routed to the APP log tab (ProcessName = "APP").
 * INDIGO-specific fields (IFlowId, IFlowName, Pattern, Context, Location)
 * are stored in ExtraFields so they appear as columns in the Plugin Tester.
 */

using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    internal class IndigoAppLogPlugin : ILogFilePlugin
    {
        public string Name    => "INDIGO APP Log (Built-in)";
        public string Version => "1.0.0";

        // Both formats produce .log (or sometimes .txt) files
        public string[] SupportedExtensions => new[] { "*.log", "*.txt" };

        // ── Detection helpers ──────────────────────────────────────────────────

        // Timestamp prefix: yyyy-MM-dd HH:mm:ss,fff[ffff] (3-7 fractional digits)
        private static readonly Regex _tsPrefix = new Regex(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}",
            RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // Old format: timestamp immediately followed by \x1e
        private static readonly Regex _oldFormatDetect = new Regex(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}\x1e",
            RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // New format: timestamp followed by space + pipe + content + pipe + space + pipe
        // e.g. "2026-01-29 10:32:38,073 |Thread-1| |abc| |def|..."
        private static readonly Regex _newFormatDetect = new Regex(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}\s+\|[^|]*\|\s+\|",
            RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        public bool CanHandle(string fileName, string[] sampleLines)
        {
            if (sampleLines == null || sampleLines.Length == 0) return false;

            foreach (var line in sampleLines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (_oldFormatDetect.IsMatch(line)) return true;
                if (_newFormatDetect.IsMatch(line)) return true;
            }
            return false;
        }

        // ── Column layout ──────────────────────────────────────────────────────

        public IReadOnlyList<PluginColumnDef> GetColumns()
        {
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Timestamp", Field = "Date",       Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",     Field = "Level",      Width = 75  },
                new PluginColumnDef { Header = "Thread",    Field = "ThreadName", Width = 120 },
                new PluginColumnDef { Header = "Logger",    Field = "Logger",     Width = 200 },
                new PluginColumnDef { Header = "Message",   Field = "Message",    Width = -1  },
            };
        }

        // ── Full regex parsers ─────────────────────────────────────────────────

        // Old format (\x1e delimited): one entry per accumulated buffer
        private static readonly Regex _oldFmt = new Regex(
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\x1e" +
            @"(?<Thread>[^\x1e]*)\x1e" +
            @"(?<RootIFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowName>[^\x1e]*)\x1e" +
            @"(?<Pattern>[^\x1e]*)\x1e" +
            @"(?<Context>[^\x1e]*)\x1e" +
            @"(?<Level>\w+)\s(?<Logger>[^\x1e]*)\x1e" +
            @"(?<Location>[^\x1e]*)\x1e" +
            @"(?<Message>.*?)\x1e" +
            @"(?<Exception>.*?)\x1e" +
            @"(?<Data>.*?)(\x1e|$)",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // New pipe format (multi-line, entry ends with ||)
        private static readonly Regex _newFmt = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\s*" +
            @"\|(?<Thread>[^|]*)\|\s*" +
            @"\|(?<RootIFlowId>[^|]*)\|\s*" +
            @"\|(?<IFlowId>[^|]*)\|\s*" +
            @"\|(?<IFlowName>[^|]*)\|\s*" +
            @"\|(?<Pattern>[^|]*)\|\s*" +
            @"\|(?<Context>[^|]*)\|\s*" +
            @"(?<Level>\w+)\s+(?<Logger>[^\r\n]*)[\r\n]+" +
            @"\|(?<Location>[^|]*)\|[\r\n]+" +
            @"(?<Message>.*?)\s*\|\|",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // ── Parse ──────────────────────────────────────────────────────────────

        public IEnumerable<LogEntryDto> Parse(
            Stream stream, ParseContext context,
            IProgress<double> progress, CancellationToken ct)
        {
            stream.Position = 0;
            long totalBytes = stream.CanSeek ? stream.Length : 0;

            bool? isOldFormat = null;
            var buffer = new StringBuilder();

            using (var reader = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true))
            {
                string line;
                int lineNum = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lineNum++;

                    if (lineNum % 500 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    // Auto-detect format on first non-empty line
                    if (isOldFormat == null && !string.IsNullOrWhiteSpace(line))
                        isOldFormat = line.Contains("\x1e");

                    bool startsEntry = _tsPrefix.IsMatch(line);

                    if (startsEntry)
                    {
                        // Flush previous buffered entry
                        if (buffer.Length > 0)
                        {
                            var entry = isOldFormat == true
                                ? TryParseOld(buffer.ToString())
                                : TryParseNew(buffer.ToString());
                            if (entry != null) yield return entry;
                            buffer.Clear();
                        }
                        buffer.AppendLine(line);
                    }
                    else if (buffer.Length > 0)
                    {
                        // Continuation of current entry
                        buffer.AppendLine(line);
                    }
                }

                // Flush last entry
                if (buffer.Length > 0)
                {
                    var entry = isOldFormat == true
                        ? TryParseOld(buffer.ToString())
                        : TryParseNew(buffer.ToString());
                    if (entry != null) yield return entry;
                }
            }

            progress?.Report(100);
        }

        // ── Format-specific parsers ────────────────────────────────────────────

        private static LogEntryDto TryParseOld(string text)
        {
            try
            {
                var m = _oldFmt.Match(text);
                if (!m.Success) return null;

                return BuildEntry(
                    timestamp:   m.Groups["Timestamp"].Value,
                    thread:      m.Groups["Thread"].Value,
                    level:       m.Groups["Level"].Value,
                    logger:      m.Groups["Logger"].Value,
                    location:    m.Groups["Location"].Value,
                    message:     m.Groups["Message"].Value,
                    exception:   m.Groups["Exception"].Value,
                    data:        m.Groups["Data"].Value,
                    rootIFlowId: m.Groups["RootIFlowId"].Value,
                    iFlowId:     m.Groups["IFlowId"].Value,
                    iFlowName:   m.Groups["IFlowName"].Value,
                    pattern:     m.Groups["Pattern"].Value,
                    ctx:         m.Groups["Context"].Value);
            }
            catch (Exception ex) { AppLogger.Error("TryParseOld failed", ex); return null; }
        }

        private static LogEntryDto TryParseNew(string text)
        {
            try
            {
                var m = _newFmt.Match(text);
                if (!m.Success) return null;

                return BuildEntry(
                    timestamp:   m.Groups["Timestamp"].Value,
                    thread:      m.Groups["Thread"].Value,
                    level:       m.Groups["Level"].Value,
                    logger:      m.Groups["Logger"].Value,
                    location:    m.Groups["Location"].Value,
                    message:     m.Groups["Message"].Value,
                    exception:   "",
                    data:        "",
                    rootIFlowId: m.Groups["RootIFlowId"].Value,
                    iFlowId:     m.Groups["IFlowId"].Value,
                    iFlowName:   m.Groups["IFlowName"].Value,
                    pattern:     m.Groups["Pattern"].Value,
                    ctx:         m.Groups["Context"].Value);
            }
            catch (Exception ex) { AppLogger.Error("TryParseNew failed", ex); return null; }
        }

        private static LogEntryDto BuildEntry(
            string timestamp, string thread, string level, string logger,
            string location, string message, string exception, string data,
            string rootIFlowId, string iFlowId, string iFlowName,
            string pattern, string ctx)
        {
            var extra = new Dictionary<string, string>();
            AddExtra(extra, "IFlowName",   iFlowName);
            AddExtra(extra, "IFlowId",     iFlowId);
            AddExtra(extra, "RootIFlowId", rootIFlowId);
            AddExtra(extra, "Pattern",     pattern);
            AddExtra(extra, "Context",     ctx);
            AddExtra(extra, "Location",    location);

            return new LogEntryDto
            {
                Date        = ParseTime(timestamp),
                Level       = NormalizeLevel(level),
                ThreadName  = thread?.Trim(),
                Logger      = logger?.Trim(),
                Message     = message?.Trim() ?? "",
                Method      = location?.Trim(),
                Exception   = string.IsNullOrWhiteSpace(exception) ? null : exception.Trim(),
                Data        = string.IsNullOrWhiteSpace(data)      ? null : data.Trim(),
                ProcessName = "APP",   // INDIGO APP text logs belong in the APP tab
                ExtraFields = extra.Count > 0 ? extra : null,
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddExtra(Dictionary<string, string> dict, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                dict[key] = value.Trim();
        }

        private static DateTime ParseTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.Now;
            // Replace comma with period so TryParse handles variable fractional digits (3-7)
            string normalized = raw.Trim().Replace(',', '.');
            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                return dt;
            // Fallback: original string
            if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt))
                return dt;
            return DateTime.Now;
        }

        private static string NormalizeLevel(string raw)
        {
            if (raw == null) return "Info";
            string u = raw.ToUpperInvariant().Trim();
            if (u == "FATAL" || u == "CRITICAL") return "Fatal";
            if (u == "ERROR")                    return "Error";
            if (u.StartsWith("WARN"))            return "Warning";
            if (u.StartsWith("INFO"))            return "Info";
            if (u == "DEBUG")                    return "Debug";
            if (u == "TRACE" || u == "VERBOSE")  return "Trace";
            return string.IsNullOrEmpty(u) ? "Info" : raw.Trim();
        }
    }
}
