/*
 * GenericTextLogPlugin — IndiLogs 3.0 Built-in Parser
 * =====================================================
 * Handles everyday text log files (.log, .txt, .err, .error, .out).
 * Tries a cascade of common timestamp+level patterns per line.
 * Lines that do not match any pattern are appended as continuations.
 *
 * Supported formats (examples):
 *   2026-01-15 10:32:01.123 [ERROR] Something failed
 *   2026-01-15 10:32:01 WARN: Retrying connection
 *   [2026-01-15 10:32:01] [INFO] Server started
 *   ERROR 2026-01-15 10:32:01.456 Disk full
 *   Jan 15 10:32:01 hostname myapp[1234]: message (syslog)
 */

using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    internal class GenericTextLogPlugin : ILogFilePlugin
    {
        public string Name    => "Generic Text Log (Built-in)";
        public string Version => "1.0.0";

        public string[] SupportedExtensions => new[]
        {
            "*.log", "*.txt", "*.err", "*.error", "*.out"
        };

        // ── Detection ────────────────────────────────────────────────────────
        public bool CanHandle(string fileName, string[]? sampleLines)
        {
            string lower = fileName.ToLowerInvariant();
            bool extOk = lower.EndsWith(".log")   || lower.EndsWith(".txt") ||
                         lower.EndsWith(".err")   || lower.EndsWith(".error") ||
                         lower.EndsWith(".out");
            if (!extOk) return false;

            // Don't steal files that look like JSON or log4net XML
            if (sampleLines != null && sampleLines.Length > 0)
            {
                string first = sampleLines[0].TrimStart();
                if (first.StartsWith("{") || first.StartsWith("["))  return false; // JSON
                if (first.StartsWith("<log4net:")  || first.StartsWith("<log4j:")) return false;
            }

            return true;
        }

        // ── Column layout ─────────────────────────────────────────────────────
        public IReadOnlyList<PluginColumnDef> GetColumns()
        {
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Timestamp", Field = "Date",    Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",     Field = "Level",   Width = 75  },
                new PluginColumnDef { Header = "Message",   Field = "Message", Width = -1  },
            };
        }

        // ── Regex patterns (compiled for performance) ─────────────────────────
        private const RegexOptions _rxOpts =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private const string _levelGroup =
            @"(FATAL|CRITICAL|ERROR|WARN(?:ING)?|INFO(?:RMATION)?|DEBUG|TRACE|VERBOSE)";

        // 1: ISO timestamp with ms + optional bracketed level
        private static readonly Regex _rx1 = new Regex(
            @"^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[.,]\d+)\s+\[?" + _levelGroup + @"\]?\s+(.+)$",
            _rxOpts, TimeSpan.FromSeconds(2));

        // 2: ISO timestamp without ms + level + colon optional
        private static readonly Regex _rx2 = new Regex(
            @"^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2})\s+" + _levelGroup + @":?\s+(.+)$",
            _rxOpts, TimeSpan.FromSeconds(2));

        // 3: [ISO timestamp] [level] or [ISO timestamp] level
        private static readonly Regex _rx3 = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[^\]]*)\]\s*\[?" + _levelGroup + @"\]?\s+(.+)$",
            _rxOpts, TimeSpan.FromSeconds(2));

        // 4: level first, then ISO timestamp
        private static readonly Regex _rx4 = new Regex(
            @"^" + _levelGroup + @"\s+(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[.,]?\d*)\s+(.+)$",
            _rxOpts, TimeSpan.FromSeconds(2));

        // 5: syslog — "Jan 15 10:32:01 hostname process: message"
        private static readonly Regex _rx5 = new Regex(
            @"^(\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+\S+\s+\S+:\s+(.+)$",
            _rxOpts, TimeSpan.FromSeconds(2));

        // ── Parse ─────────────────────────────────────────────────────────────
        public IEnumerable<LogEntryDto> Parse(
            Stream stream, ParseContext context,
            IProgress<double>? progress, CancellationToken ct)
        {
            stream.Position = 0;
            long totalBytes = stream.CanSeek ? stream.Length : 0;
            int  lineNum    = 0;

            // Running entry we might accumulate continuation lines into
            LogEntryDto? current = null;

            using (var reader = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lineNum++;

                    if (lineNum % 1000 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    LogEntryDto? parsed = TryParseAsNewEntry(line);
                    if (parsed != null)
                    {
                        if (current != null) yield return current;
                        current = parsed;
                    }
                    else if (current != null)
                    {
                        // Continuation line — append to current entry's message
                        current.Message += "\n" + line.TrimEnd();
                    }
                    else
                    {
                        // No context yet — emit as standalone Info line
                        yield return new LogEntryDto
                        {
                            Date    = DateTime.Now,
                            Level   = "Info",
                            Message = line.TrimEnd()
                        };
                    }
                }
            }

            if (current != null) yield return current;
            progress?.Report(100);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static LogEntryDto? TryParseAsNewEntry(string line)
        {
            Match m;

            // Pattern 1: ISO + ms + level
            m = _rx1.Match(line);
            if (m.Success)
                return Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);

            // Pattern 2: ISO without ms + level
            m = _rx2.Match(line);
            if (m.Success)
                return Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);

            // Pattern 3: [timestamp] level
            m = _rx3.Match(line);
            if (m.Success)
                return Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);

            // Pattern 4: level first
            m = _rx4.Match(line);
            if (m.Success)
                return Make(m.Groups[2].Value, m.Groups[1].Value, m.Groups[3].Value);

            // Pattern 5: syslog (no level — default Info)
            m = _rx5.Match(line);
            if (m.Success)
                return Make(m.Groups[1].Value, "Info", m.Groups[2].Value);

            return null;
        }

        private static LogEntryDto Make(string rawDate, string level, string message)
        {
            TryParseDate(rawDate, out DateTime dt);
            return new LogEntryDto
            {
                Date    = dt,
                Level   = NormalizeLevel(level),
                Message = message?.Trim() ?? ""
            };
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
            "MMM d HH:mm:ss", "MMM  d HH:mm:ss"
        };

        private static bool TryParseDate(string raw, out DateTime result)
        {
            if (string.IsNullOrWhiteSpace(raw)) { result = DateTime.Now; return false; }
            string trimmed = raw.Trim();
            if (DateTime.TryParseExact(trimmed, _dateFmts,
                       CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;
            // Normalize comma→period for variable-length fractional seconds
            string normalized = trimmed.Replace(',', '.');
            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out result))
                return true;
            return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out result);
        }

        private static string NormalizeLevel(string? raw)
        {
            if (raw == null) return "Info";
            string u = raw.ToUpperInvariant();
            if (u.StartsWith("FATAL") || u == "CRITICAL") return "Fatal";
            if (u == "ERROR")   return "Error";
            if (u.StartsWith("WARN")) return "Warning";
            if (u.StartsWith("INFO")) return "Info";
            if (u == "DEBUG")   return "Debug";
            if (u == "TRACE" || u == "VERBOSE") return "Trace";
            return raw;
        }
    }
}
