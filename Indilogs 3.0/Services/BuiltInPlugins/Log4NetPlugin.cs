/*
 * Log4NetPlugin — IndiLogs 3.0 Built-in Parser
 * =============================================
 * Handles log4net and log4j log files in two formats:
 *
 * 1. XML Appender format (log4net XmlLayout):
 *    <log4net:event logger="MyApp.Service" timestamp="2026-01-15T10:32:01.123+00:00"
 *                   level="ERROR" thread="Worker-1" domain="MyApp" username="user">
 *      <log4net:message>Something failed</log4net:message>
 *      <log4net:exception>System.Exception: ...</log4net:exception>
 *    </log4net:event>
 *
 * 2. PatternLayout text format (common default pattern):
 *    2026-01-15 10:32:01,123 [Worker-1] ERROR MyApp.Service - Something failed
 */

using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    internal class Log4NetPlugin : ILogFilePlugin
    {
        public string Name    => "log4net / log4j (Built-in)";
        public string Version => "1.0.0";

        public string[] SupportedExtensions => new[] { "*.log", "*.xml" };

        // ── Detection ─────────────────────────────────────────────────────────
        public bool CanHandle(string fileName, string[] sampleLines)
        {
            string lower = fileName.ToLowerInvariant();
            if (!lower.EndsWith(".log") && !lower.EndsWith(".xml")) return false;

            if (sampleLines == null || sampleLines.Length == 0) return false;

            foreach (var line in sampleLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // XML appender format
                if (line.Contains("<log4net:event") || line.Contains("<log4j:event"))
                    return true;

                // PatternLayout: "yyyy-MM-dd HH:mm:ss,fff [Thread] LEVEL Logger - message"
                if (_rxPattern.IsMatch(line))
                    return true;
            }
            return false;
        }

        // ── Column layout ─────────────────────────────────────────────────────
        public IReadOnlyList<PluginColumnDef> GetColumns()
        {
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Timestamp", Field = "Date",       Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",     Field = "Level",      Width = 75  },
                new PluginColumnDef { Header = "Thread",    Field = "ThreadName", Width = 100 },
                new PluginColumnDef { Header = "Logger",    Field = "Logger",     Width = 200 },
                new PluginColumnDef { Header = "Message",   Field = "Message",    Width = -1  },
            };
        }

        // ── PatternLayout regex ────────────────────────────────────────────────
        // Matches: 2026-01-15 10:32:01,123 [Thread] LEVEL Logger - message
        private static readonly Regex _rxPattern = new Regex(
            @"^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[.,]\d+)\s+\[([^\]]+)\]\s+(FATAL|ERROR|WARN(?:ING)?|INFO(?:RMATION)?|DEBUG|TRACE)\s+(\S+)\s+-\s+(.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        private static readonly string[] _dateFmts =
        {
            // High-precision formats first (7→3 fractional digits)
            "yyyy-MM-dd HH:mm:ss,fffffff", "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-dd HH:mm:ss,ffffff",  "yyyy-MM-dd HH:mm:ss.ffffff",
            "yyyy-MM-dd HH:mm:ss,fffff",   "yyyy-MM-dd HH:mm:ss.fffff",
            "yyyy-MM-dd HH:mm:ss,ffff",    "yyyy-MM-dd HH:mm:ss.ffff",
            "yyyy-MM-dd HH:mm:ss,fff",     "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffffffzzz", "yyyy-MM-ddTHH:mm:ss.fffzzz", "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss"
        };

        // ── Parse ─────────────────────────────────────────────────────────────
        public IEnumerable<LogEntryDto> Parse(
            Stream stream, ParseContext context,
            IProgress<double> progress, CancellationToken ct)
        {
            // Peek to determine format
            stream.Position = 0;
            bool isXml = IsXmlFormat(stream);
            stream.Position = 0;

            if (isXml)
                return ParseXml(stream, progress, ct);
            else
                return ParsePatternLayout(stream, progress, ct);
        }

        private static bool IsXmlFormat(Stream stream)
        {
            using (var sr = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true))
            {
                for (int i = 0; i < 10; i++)
                {
                    string line = sr.ReadLine();
                    if (line == null) break;
                    if (line.Contains("<log4net:event") || line.Contains("<log4j:event"))
                        return true;
                }
            }
            return false;
        }

        // ── XML format parsing ────────────────────────────────────────────────
        private static IEnumerable<LogEntryDto> ParseXml(
            Stream stream, IProgress<double> progress, CancellationToken ct)
        {
            long totalBytes = stream.CanSeek ? stream.Length : 0;

            // Wrap in a root element so XmlReader can parse a series of events
            // Some log4net files are NOT well-formed XML at the top level
            var wrapped = new ConcatStream(
                new MemoryStream(Encoding.UTF8.GetBytes("<root xmlns:log4net=\"log4net\" xmlns:log4j=\"log4j\">")),
                stream,
                new MemoryStream(Encoding.UTF8.GetBytes("</root>")));

            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace     = true,
                IgnoreComments       = true,
                DtdProcessing        = DtdProcessing.Ignore,
                ValidationType       = ValidationType.None,
                ConformanceLevel     = ConformanceLevel.Fragment
            };

            int count = 0;
            using (var reader = XmlReader.Create(wrapped, settings))
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element) continue;

                    string localName = reader.LocalName;  // "event" for both log4net: and log4j:
                    if (!localName.Equals("event", StringComparison.OrdinalIgnoreCase)) continue;

                    string timestamp = reader.GetAttribute("timestamp");
                    string level     = reader.GetAttribute("level");
                    string logger    = reader.GetAttribute("logger");
                    string thread    = reader.GetAttribute("thread");
                    string message   = null;
                    string exception = null;

                    using (var sub = reader.ReadSubtree())
                    {
                        while (sub.Read())
                        {
                            if (sub.NodeType != XmlNodeType.Element) continue;
                            switch (sub.LocalName.ToLowerInvariant())
                            {
                                case "message":   message   = sub.ReadElementContentAsString(); break;
                                case "exception": exception = sub.ReadElementContentAsString(); break;
                            }
                        }
                    }

                    count++;
                    if (count % 500 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    yield return new LogEntryDto
                    {
                        Date       = ParseTimestamp(timestamp),
                        Level      = NormalizeLevel(level),
                        ThreadName = thread,
                        Logger     = logger,
                        Message    = message   ?? "",
                        Exception  = exception
                    };
                }
            }

            progress?.Report(100);
        }

        // ── PatternLayout format parsing ──────────────────────────────────────
        private static IEnumerable<LogEntryDto> ParsePatternLayout(
            Stream stream, IProgress<double> progress, CancellationToken ct)
        {
            long totalBytes = stream.CanSeek ? stream.Length : 0;
            int  lineNum    = 0;
            LogEntryDto current = null;

            using (var reader = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, 65536, leaveOpen: true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lineNum++;

                    if (lineNum % 1000 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var m = _rxPattern.Match(line);
                    if (m.Success)
                    {
                        if (current != null) yield return current;
                        current = new LogEntryDto
                        {
                            Date       = ParseTimestamp(m.Groups[1].Value),
                            ThreadName = m.Groups[2].Value.Trim(),
                            Level      = NormalizeLevel(m.Groups[3].Value),
                            Logger     = m.Groups[4].Value.Trim(),
                            Message    = m.Groups[5].Value.Trim()
                        };
                    }
                    else if (current != null)
                    {
                        // Continuation / exception line
                        current.Message += "\n" + line;
                    }
                }
            }

            if (current != null) yield return current;
            progress?.Report(100);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static DateTime ParseTimestamp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.Now;
            string trimmed = raw.Trim();
            if (DateTime.TryParseExact(trimmed, _dateFmts,
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime();
            // Normalize comma→period for variable-length fractional seconds
            string normalized = trimmed.Replace(',', '.');
            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime();
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime();
            return DateTime.Now;
        }

        private static string NormalizeLevel(string raw)
        {
            if (raw == null) return "Info";
            string u = raw.ToUpperInvariant();
            if (u == "FATAL") return "Fatal";
            if (u == "ERROR") return "Error";
            if (u.StartsWith("WARN")) return "Warning";
            if (u.StartsWith("INFO")) return "Info";
            if (u == "DEBUG") return "Debug";
            if (u == "TRACE") return "Trace";
            return raw;
        }

        // ── Helper: concatenate streams ───────────────────────────────────────
        private class ConcatStream : Stream
        {
            private readonly Stream[] _streams;
            private int _idx;

            public ConcatStream(params Stream[] streams) { _streams = streams; _idx = 0; }

            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (_idx < _streams.Length)
                {
                    int n = _streams[_idx].Read(buffer, offset, count);
                    if (n > 0) return n;
                    _idx++;
                }
                return 0;
            }

            public override void Flush()  { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
