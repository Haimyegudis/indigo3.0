/*
 * JsonLogPlugin — IndiLogs 3.0 Built-in Parser
 * =============================================
 * Handles JSON log files (NDJSON / JSON Lines).
 * Supports common structured logging frameworks:
 *   • Winston  — {level, message, timestamp, ...}
 *   • Bunyan   — {v, msg, time, level(int), ...}
 *   • Serilog  — {@timestamp, @l, @mt/@m, ...}
 *   • NLog/log4net JSON — {time, level, logger, message}
 *   • Generic  — any JSON object with detectable time + message keys
 *
 * Extra JSON fields are stored in ExtraFields for display in the DataGrid.
 */

using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    internal class JsonLogPlugin : ILogFilePlugin
    {
        public string Name    => "JSON Log (Built-in)";
        public string Version => "1.0.0";

        public string[] SupportedExtensions => new[] { "*.json", "*.jsonl" };

        // ── Detection ─────────────────────────────────────────────────────────
        public bool CanHandle(string fileName, string[]? sampleLines)
        {
            string lower = fileName.ToLowerInvariant();
            bool extOk = lower.EndsWith(".json") || lower.EndsWith(".jsonl");

            // Also accept .log files whose first non-empty line looks like JSON
            if (!extOk && lower.EndsWith(".log"))
            {
                if (sampleLines == null || sampleLines.Length == 0) return false;
                string first = sampleLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.TrimStart();
                extOk = first != null && first.StartsWith("{");
            }

            if (!extOk) return false;

            // Quick validity check: at least one sample line must be parseable JSON
            if (sampleLines != null)
            {
                foreach (var s in sampleLines)
                {
                    string t = s?.Trim();
                    if (string.IsNullOrEmpty(t) || !t.StartsWith("{")) continue;
                    try { JObject.Load(new JsonTextReader(new System.IO.StringReader(t)) { MaxDepth = AppConstants.JsonMaxDepth }); return true; }
                    catch (Exception ex) { AppLogger.Error("JSON validation failed", ex); }
                }
            }
            // If no sample lines, accept by extension alone
            return sampleLines == null || sampleLines.Length == 0;
        }

        // ── Column layout ─────────────────────────────────────────────────────
        public IReadOnlyList<PluginColumnDef> GetColumns()
        {
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Timestamp", Field = "Date",    Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",     Field = "Level",   Width = 75  },
                new PluginColumnDef { Header = "Logger",    Field = "Logger",  Width = 130 },
                new PluginColumnDef { Header = "Message",   Field = "Message", Width = -1  },
            };
        }

        // ── Parse ─────────────────────────────────────────────────────────────
        public IEnumerable<LogEntryDto> Parse(
            Stream stream, ParseContext context,
            IProgress<double>? progress, CancellationToken ct)
        {
            stream.Position = 0;
            long totalBytes  = stream.CanSeek ? stream.Length : 0;
            int  lineNum     = 0;
            JsonFormat format = JsonFormat.Unknown;

            using (var reader = new StreamReader(stream, Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lineNum++;

                    if (lineNum % 500 == 0 && totalBytes > 0)
                        progress?.Report((double)stream.Position / totalBytes * 100.0);

                    line = line.Trim();
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("{")) continue;

                    JObject obj;
                    try { obj = JObject.Load(new JsonTextReader(new System.IO.StringReader(line)) { MaxDepth = AppConstants.JsonMaxDepth }); }
                    catch (Exception ex) { AppLogger.Error("JSON parse failed", ex); continue; }

                    // Detect format on first valid object
                    if (format == JsonFormat.Unknown)
                        format = DetectFormat(obj);

                    LogEntryDto? entry = ParseEntry(obj, format);
                    if (entry != null) yield return entry;
                }
            }

            progress?.Report(100);
        }

        // ── Format detection ──────────────────────────────────────────────────

        private enum JsonFormat
        {
            Unknown,
            Winston,   // {level, message, timestamp}
            Bunyan,    // {v, msg, time, level(int)}
            Serilog,   // {@timestamp, @l, @mt/@m}
            Generic    // any object with detectable time + message
        }

        private static JsonFormat DetectFormat(JObject obj)
        {
            var keys = new HashSet<string>(obj.Properties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            if (keys.Contains("v") && keys.Contains("msg") && keys.Contains("time") &&
                obj["level"]?.Type == JTokenType.Integer)
                return JsonFormat.Bunyan;

            if (keys.Contains("@timestamp") || keys.Contains("@l"))
                return JsonFormat.Serilog;

            if (keys.Contains("level") && keys.Contains("message") &&
               (keys.Contains("timestamp") || keys.Contains("time")))
                return JsonFormat.Winston;

            return JsonFormat.Generic;
        }

        // ── Entry parsing by format ───────────────────────────────────────────

        private static LogEntryDto? ParseEntry(JObject obj, JsonFormat format)
        {
            try
            {
                switch (format)
                {
                    case JsonFormat.Winston:  return ParseWinston(obj);
                    case JsonFormat.Bunyan:   return ParseBunyan(obj);
                    case JsonFormat.Serilog:  return ParseSerilog(obj);
                    default:                  return ParseGeneric(obj);
                }
            }
            catch (Exception ex) { AppLogger.Error("ParseEntry failed", ex); return null; }
        }

        private static LogEntryDto ParseWinston(JObject o)
        {
            var extra = BuildExtra(o, "level", "message", "timestamp", "time", "service", "logger", "name");
            string logger = Str(o, "service") ?? Str(o, "logger") ?? Str(o, "name");
            return new LogEntryDto
            {
                Date        = ParseTime(Str(o, "timestamp") ?? Str(o, "time")),
                Level       = NormalizeLevel(Str(o, "level")),
                Message     = Str(o, "message") ?? "",
                Logger      = logger,
                ExtraFields = extra.Count > 0 ? extra : null
            };
        }

        private static LogEntryDto ParseBunyan(JObject o)
        {
            int intLevel = o["level"]?.Value<int>() ?? 30;
            string level = BunyanLevel(intLevel);
            var extra = BuildExtra(o, "v", "name", "hostname", "pid", "level", "msg", "time");
            return new LogEntryDto
            {
                Date        = ParseTime(Str(o, "time")),
                Level       = level,
                Message     = Str(o, "msg") ?? "",
                Logger      = Str(o, "name"),
                ThreadName  = Str(o, "hostname"),
                ExtraFields = extra.Count > 0 ? extra : null
            };
        }

        private static LogEntryDto ParseSerilog(JObject o)
        {
            string msg = Str(o, "@m") ?? Str(o, "@mt") ?? Str(o, "message") ?? "";
            var extra = BuildExtra(o, "@timestamp", "@l", "@m", "@mt", "@r", "@i", "@x", "message");
            return new LogEntryDto
            {
                Date        = ParseTime(Str(o, "@timestamp") ?? Str(o, "timestamp")),
                Level       = NormalizeLevel(Str(o, "@l") ?? Str(o, "level")),
                Message     = msg,
                ExtraFields = extra.Count > 0 ? extra : null
            };
        }

        private static LogEntryDto ParseGeneric(JObject o)
        {
            // Find time key
            string? timeVal = null;
            foreach (var k in new[] { "timestamp", "time", "ts", "date", "@timestamp", "datetime", "created" })
            {
                timeVal = Str(o, k);
                if (timeVal != null) break;
            }

            // Find message key
            string? msgVal = null;
            string? msgKey = null;
            foreach (var k in new[] { "message", "msg", "text", "body", "log", "content", "description" })
            {
                msgVal = Str(o, k);
                if (msgVal != null) { msgKey = k; break; }
            }

            // Find level key
            string? levelVal = null;
            foreach (var k in new[] { "level", "severity", "loglevel", "log_level", "priority" })
            {
                levelVal = Str(o, k);
                if (levelVal != null) break;
            }

            // Build extra — exclude known fields
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "timestamp","time","ts","date","@timestamp","datetime","created",
                  "message","msg","text","body","log","content","description",
                  "level","severity","loglevel","log_level","priority" };
            var extra = new Dictionary<string, string>();
            foreach (var prop in o.Properties())
                if (!known.Contains(prop.Name))
                    extra[prop.Name] = prop.Value?.ToString();

            return new LogEntryDto
            {
                Date        = ParseTime(timeVal),
                Level       = NormalizeLevel(levelVal) ?? "Info",
                Message     = msgVal ?? o.ToString(Formatting.None),
                ExtraFields = extra.Count > 0 ? extra : null
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? Str(JObject o, string key)
        {
            var tok = o[key];
            if (tok == null || tok.Type == JTokenType.Null) return null;
            return tok.ToString();
        }

        private static Dictionary<string, string> BuildExtra(JObject o, params string[] skip)
        {
            var skipSet = new HashSet<string>(skip, StringComparer.OrdinalIgnoreCase);
            var dict    = new Dictionary<string, string>();
            foreach (var prop in o.Properties())
                if (!skipSet.Contains(prop.Name) && prop.Value?.Type != JTokenType.Null)
                    dict[prop.Name] = prop.Value.ToString();
            return dict;
        }

        private static DateTime ParseTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.Now;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime();
            return DateTime.Now;
        }

        private static string BunyanLevel(int n)
        {
            if (n >= 60) return "Fatal";
            if (n >= 50) return "Error";
            if (n >= 40) return "Warning";
            if (n >= 30) return "Info";
            if (n >= 20) return "Debug";
            return "Trace";
        }

        private static string? NormalizeLevel(string? raw)
        {
            if (raw == null) return null;
            string u = raw.ToUpperInvariant();
            if (u == "FATAL" || u == "CRITICAL") return "Fatal";
            if (u == "ERROR")   return "Error";
            if (u.StartsWith("WARN")) return "Warning";
            if (u.StartsWith("INFO")) return "Info";
            if (u == "DEBUG")   return "Debug";
            if (u == "TRACE" || u == "VERBOSE") return "Trace";
            return raw;
        }
    }
}
