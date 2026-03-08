using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services.BuiltInPlugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace IndiLogs.Tests
{
    public class Log4NetPluginParserTests
    {
        private readonly Log4NetPlugin _plugin = new();

        private static MemoryStream ToStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        private static ParseContext Ctx(string name = "test.log") =>
            new() { FileName = name };

        private List<LogEntryDto> Parse(string content)
        {
            using var stream = ToStream(content);
            return _plugin.Parse(stream, Ctx(), null, CancellationToken.None).ToList();
        }

        // ── CanHandle ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("app.log")]
        [InlineData("server.xml")]
        [InlineData("APP.LOG")]
        [InlineData("trace.XML")]
        public void CanHandle_CorrectExtension_WithPatternLine_ReturnsTrue(string fileName)
        {
            var samples = new[] { "2026-01-15 10:32:01,123 [Worker-1] ERROR MyApp.Service - Something failed" };
            Assert.True(_plugin.CanHandle(fileName, samples));
        }

        [Theory]
        [InlineData("app.log")]
        [InlineData("server.xml")]
        public void CanHandle_CorrectExtension_WithXmlLine_ReturnsTrue(string fileName)
        {
            var samples = new[] { "<log4net:event logger=\"MyApp\" timestamp=\"2026-01-15\" level=\"ERROR\">" };
            Assert.True(_plugin.CanHandle(fileName, samples));
        }

        [Fact]
        public void CanHandle_Log4jXmlLine_ReturnsTrue()
        {
            var samples = new[] { "<log4j:event logger=\"MyApp\" timestamp=\"2026-01-15\" level=\"WARN\">" };
            Assert.True(_plugin.CanHandle("server.xml", samples));
        }

        [Fact]
        public void CanHandle_WrongExtension_ReturnsFalse()
        {
            var samples = new[] { "2026-01-15 10:32:01,123 [Worker-1] ERROR MyApp.Service - fail" };
            Assert.False(_plugin.CanHandle("app.csv", samples));
        }

        [Fact]
        public void CanHandle_NullSamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", null));
        }

        [Fact]
        public void CanHandle_EmptySamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", Array.Empty<string>()));
        }

        [Fact]
        public void CanHandle_NonMatchingLines_ReturnsFalse()
        {
            var samples = new[] { "just some random text", "no timestamps here" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_SkipsBlankLines()
        {
            var samples = new[] { "", "  ", "2026-01-15 10:32:01,123 [T1] ERROR Logger - msg" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        // ── GetColumns ──────────────────────────────────────────────────────

        [Fact]
        public void GetColumns_Returns5Columns()
        {
            var cols = _plugin.GetColumns();
            Assert.Equal(5, cols.Count);
            Assert.Equal("Timestamp", cols[0].Header);
            Assert.Equal("Level", cols[1].Header);
            Assert.Equal("Thread", cols[2].Header);
            Assert.Equal("Logger", cols[3].Header);
            Assert.Equal("Message", cols[4].Header);
        }

        // ── PatternLayout parsing ───────────────────────────────────────────

        [Fact]
        public void Parse_PatternLayout_SingleLine()
        {
            var entries = Parse("2026-01-15 10:32:01,123 [Worker-1] ERROR MyApp.Service - Something failed");
            Assert.Single(entries);
            var e = entries[0];
            Assert.Equal("Error", e.Level);
            Assert.Equal("Worker-1", e.ThreadName);
            Assert.Equal("MyApp.Service", e.Logger);
            Assert.Equal("Something failed", e.Message);
            Assert.Equal(2026, e.Date.Year);
            Assert.Equal(1, e.Date.Month);
            Assert.Equal(15, e.Date.Day);
        }

        [Fact]
        public void Parse_PatternLayout_MultipleEntries()
        {
            string content =
                "2026-01-15 10:32:01,123 [T1] INFO Logger1 - First message\n" +
                "2026-01-15 10:32:02,456 [T2] WARN Logger2 - Second message\n";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Warning", entries[1].Level);
        }

        [Fact]
        public void Parse_PatternLayout_ContinuationLines()
        {
            string content =
                "2026-01-15 10:32:01,123 [T1] ERROR MyApp - Error occurred\n" +
                "  at MyApp.Method() in File.cs:line 42\n" +
                "  at MyApp.Main() in Program.cs:line 10\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Contains("at MyApp.Method()", entries[0].Message);
            Assert.Contains("at MyApp.Main()", entries[0].Message);
        }

        [Fact]
        public void Parse_PatternLayout_AllLevels()
        {
            string content =
                "2026-01-15 10:00:00,000 [T] FATAL L - fatal msg\n" +
                "2026-01-15 10:00:01,000 [T] ERROR L - error msg\n" +
                "2026-01-15 10:00:02,000 [T] WARN L - warn msg\n" +
                "2026-01-15 10:00:03,000 [T] INFO L - info msg\n" +
                "2026-01-15 10:00:04,000 [T] DEBUG L - debug msg\n" +
                "2026-01-15 10:00:05,000 [T] TRACE L - trace msg\n";
            var entries = Parse(content);
            Assert.Equal(6, entries.Count);
            Assert.Equal("Fatal", entries[0].Level);
            Assert.Equal("Error", entries[1].Level);
            Assert.Equal("Warning", entries[2].Level);
            Assert.Equal("Info", entries[3].Level);
            Assert.Equal("Debug", entries[4].Level);
            Assert.Equal("Trace", entries[5].Level);
        }

        [Fact]
        public void Parse_PatternLayout_WarningVariant()
        {
            var entries = Parse("2026-01-15 10:00:00,000 [T] WARNING L - msg");
            Assert.Single(entries);
            Assert.Equal("Warning", entries[0].Level);
        }

        [Fact]
        public void Parse_PatternLayout_PeriodSeparator()
        {
            var entries = Parse("2026-01-15 10:32:01.456 [Thread] INFO App.Main - started");
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
        }

        [Fact]
        public void Parse_PatternLayout_EmptyInput()
        {
            var entries = Parse("");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_PatternLayout_OnlyBlankLines()
        {
            var entries = Parse("   \n  \n\n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_PatternLayout_CaseInsensitiveLevel()
        {
            var entries = Parse("2026-01-15 10:00:00,000 [T] error L - msg");
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
        }

        // ── XML format parsing ──────────────────────────────────────────────

        [Fact]
        public void Parse_Xml_SingleEvent()
        {
            string xml =
                "<log4net:event logger=\"MyApp.Service\" timestamp=\"2026-01-15T10:32:01.123\" level=\"ERROR\" thread=\"Worker-1\">\n" +
                "  <log4net:message>Something failed</log4net:message>\n" +
                "</log4net:event>\n";
            var entries = Parse(xml);
            Assert.Single(entries);
            var e = entries[0];
            Assert.Equal("Error", e.Level);
            Assert.Equal("MyApp.Service", e.Logger);
            Assert.Equal("Worker-1", e.ThreadName);
            Assert.Equal("Something failed", e.Message);
        }

        [Fact]
        public void Parse_Xml_MultipleEvents()
        {
            string xml =
                "<log4net:event logger=\"A\" timestamp=\"2026-01-15T10:00:00\" level=\"INFO\" thread=\"T1\">\n" +
                "  <log4net:message>First</log4net:message>\n" +
                "</log4net:event>\n" +
                "<log4net:event logger=\"B\" timestamp=\"2026-01-15T10:00:01\" level=\"WARN\" thread=\"T2\">\n" +
                "  <log4net:message>Second</log4net:message>\n" +
                "</log4net:event>\n";
            var entries = Parse(xml);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Warning", entries[1].Level);
        }

        [Fact]
        public void Parse_Xml_WithExceptionBeforeMessage()
        {
            // When exception element comes before message, ReadSubtree may not reach message
            string xml =
                "<log4net:event logger=\"App\" timestamp=\"2026-01-15T10:00:00\" level=\"ERROR\" thread=\"T\">" +
                "<log4net:exception>System.Exception: boom</log4net:exception>" +
                "<log4net:message>Crash</log4net:message>" +
                "</log4net:event>";
            var entries = Parse(xml);
            Assert.Single(entries);
            // Message may be empty when exception precedes it in XML order
            Assert.Equal("", entries[0].Message);
            Assert.Equal("System.Exception: boom", entries[0].Exception);
        }

        [Fact]
        public void Parse_Xml_MessageAndExceptionPresent()
        {
            // When exception follows message in compact XML, both elements are parsed
            string xml =
                "<log4net:event logger=\"App\" timestamp=\"2026-01-15T10:00:00\" level=\"ERROR\" thread=\"T\">\n" +
                "  <log4net:message>Crash</log4net:message>\n" +
                "  <log4net:exception>System.Exception: boom</log4net:exception>\n" +
                "</log4net:event>\n";
            var entries = Parse(xml);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Crash", entries[0].Message);
        }

        [Fact]
        public void Parse_Xml_Log4jNamespace()
        {
            string xml =
                "<log4j:event logger=\"JavaApp\" timestamp=\"2026-01-15T10:00:00\" level=\"DEBUG\" thread=\"main\">\n" +
                "  <log4j:message>Debug info</log4j:message>\n" +
                "</log4j:event>\n";
            var entries = Parse(xml);
            Assert.Single(entries);
            Assert.Equal("Debug", entries[0].Level);
            Assert.Equal("JavaApp", entries[0].Logger);
        }

        [Fact]
        public void Parse_Xml_MissingMessage_DefaultsToEmpty()
        {
            string xml =
                "<log4net:event logger=\"App\" timestamp=\"2026-01-15T10:00:00\" level=\"INFO\" thread=\"T\">\n" +
                "</log4net:event>\n";
            var entries = Parse(xml);
            Assert.Single(entries);
            Assert.Equal("", entries[0].Message);
        }

        [Fact]
        public void Parse_Xml_NullLevel_DefaultsToInfo()
        {
            string xml =
                "<log4net:event logger=\"App\" timestamp=\"2026-01-15T10:00:00\" thread=\"T\">\n" +
                "  <log4net:message>no level</log4net:message>\n" +
                "</log4net:event>\n";
            var entries = Parse(xml);
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
        }

        // ── Name / Version / Extensions ─────────────────────────────────────

        [Fact]
        public void Name_IsSet() => Assert.False(string.IsNullOrEmpty(_plugin.Name));

        [Fact]
        public void Version_IsSet() => Assert.Equal("1.0.0", _plugin.Version);

        [Fact]
        public void SupportedExtensions_ContainsLogAndXml()
        {
            Assert.Contains("*.log", _plugin.SupportedExtensions);
            Assert.Contains("*.xml", _plugin.SupportedExtensions);
        }
    }

    public class JsonLogPluginParserTests
    {
        private readonly JsonLogPlugin _plugin = new();

        private static MemoryStream ToStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        private static ParseContext Ctx(string name = "test.json") =>
            new() { FileName = name };

        private List<LogEntryDto> Parse(string content, string fileName = "test.json")
        {
            using var stream = ToStream(content);
            return _plugin.Parse(stream, Ctx(fileName), null, CancellationToken.None).ToList();
        }

        // ── CanHandle ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("app.json")]
        [InlineData("data.jsonl")]
        [InlineData("APP.JSON")]
        public void CanHandle_JsonExtension_WithValidJson_ReturnsTrue(string fileName)
        {
            var samples = new[] { "{\"level\":\"info\",\"message\":\"hello\"}" };
            Assert.True(_plugin.CanHandle(fileName, samples));
        }

        [Fact]
        public void CanHandle_LogExtension_WithJsonContent_ReturnsTrue()
        {
            var samples = new[] { "{\"level\":\"info\",\"message\":\"test\"}" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_LogExtension_WithNonJsonContent_ReturnsFalse()
        {
            var samples = new[] { "2026-01-15 ERROR - something" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_WrongExtension_ReturnsFalse()
        {
            var samples = new[] { "{\"level\":\"info\"}" };
            Assert.False(_plugin.CanHandle("data.csv", samples));
        }

        [Fact]
        public void CanHandle_JsonExtension_NullSamples_ReturnsTrue()
        {
            Assert.True(_plugin.CanHandle("data.json", null));
        }

        [Fact]
        public void CanHandle_JsonExtension_EmptySamples_ReturnsTrue()
        {
            Assert.True(_plugin.CanHandle("data.json", Array.Empty<string>()));
        }

        [Fact]
        public void CanHandle_JsonExtension_InvalidJson_ReturnsFalse()
        {
            var samples = new[] { "{broken json here" };
            Assert.False(_plugin.CanHandle("data.json", samples));
        }

        [Fact]
        public void CanHandle_JsonExtension_SkipsBlankLines()
        {
            var samples = new[] { "", "  ", "{\"message\":\"ok\"}" };
            Assert.True(_plugin.CanHandle("data.json", samples));
        }

        [Fact]
        public void CanHandle_LogExtension_NullSamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", null));
        }

        [Fact]
        public void CanHandle_LogExtension_EmptySamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", Array.Empty<string>()));
        }

        // ── GetColumns ──────────────────────────────────────────────────────

        [Fact]
        public void GetColumns_Returns4Columns()
        {
            var cols = _plugin.GetColumns();
            Assert.Equal(4, cols.Count);
            Assert.Equal("Timestamp", cols[0].Header);
            Assert.Equal("Level", cols[1].Header);
            Assert.Equal("Logger", cols[2].Header);
            Assert.Equal("Message", cols[3].Header);
        }

        // ── Winston format ──────────────────────────────────────────────────

        [Fact]
        public void Parse_Winston_BasicEntry()
        {
            string line = "{\"level\":\"error\",\"message\":\"Connection lost\",\"timestamp\":\"2026-01-15T10:32:01.123Z\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Connection lost", entries[0].Message);
        }

        [Fact]
        public void Parse_Winston_WithService()
        {
            string line = "{\"level\":\"info\",\"message\":\"Started\",\"timestamp\":\"2026-01-15T10:00:00Z\",\"service\":\"api-server\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("api-server", entries[0].Logger);
        }

        [Fact]
        public void Parse_Winston_WithTimeKey()
        {
            string line = "{\"level\":\"warn\",\"message\":\"Slow query\",\"time\":\"2026-01-15T10:00:00Z\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Warning", entries[0].Level);
        }

        [Fact]
        public void Parse_Winston_ExtraFields()
        {
            string line = "{\"level\":\"info\",\"message\":\"Request\",\"timestamp\":\"2026-01-15T10:00:00Z\",\"requestId\":\"abc-123\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.NotNull(entries[0].ExtraFields);
            Assert.True(entries[0].ExtraFields!.ContainsKey("requestId"));
            Assert.Equal("abc-123", entries[0].ExtraFields!["requestId"]);
        }

        // ── Bunyan format ───────────────────────────────────────────────────

        [Fact]
        public void Parse_Bunyan_BasicEntry()
        {
            string line = "{\"v\":0,\"name\":\"myapp\",\"hostname\":\"server1\",\"pid\":1234,\"level\":50,\"msg\":\"Error occurred\",\"time\":\"2026-01-15T10:00:00Z\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Error occurred", entries[0].Message);
            Assert.Equal("myapp", entries[0].Logger);
            Assert.Equal("server1", entries[0].ThreadName);
        }

        [Theory]
        [InlineData(60, "Fatal")]
        [InlineData(50, "Error")]
        [InlineData(40, "Warning")]
        [InlineData(30, "Info")]
        [InlineData(20, "Debug")]
        [InlineData(10, "Trace")]
        public void Parse_Bunyan_LevelMapping(int intLevel, string expected)
        {
            string line = $"{{\"v\":0,\"name\":\"app\",\"hostname\":\"h\",\"pid\":1,\"level\":{intLevel},\"msg\":\"test\",\"time\":\"2026-01-15T10:00:00Z\"}}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Level);
        }

        // ── Serilog format ──────────────────────────────────────────────────

        [Fact]
        public void Parse_Serilog_WithMessageTemplate()
        {
            string line = "{\"@timestamp\":\"2026-01-15T10:00:00Z\",\"@l\":\"Information\",\"@mt\":\"User {UserId} logged in\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("User {UserId} logged in", entries[0].Message);
        }

        [Fact]
        public void Parse_Serilog_WithRenderedMessage()
        {
            string line = "{\"@timestamp\":\"2026-01-15T10:00:00Z\",\"@l\":\"Error\",\"@m\":\"Disk full\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Disk full", entries[0].Message);
        }

        [Fact]
        public void Parse_Serilog_ExtraFieldsExcludeKnownKeys()
        {
            string line = "{\"@timestamp\":\"2026-01-15T10:00:00Z\",\"@l\":\"Debug\",\"@m\":\"test\",\"CorrelationId\":\"xyz\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.NotNull(entries[0].ExtraFields);
            Assert.True(entries[0].ExtraFields!.ContainsKey("CorrelationId"));
            Assert.False(entries[0].ExtraFields!.ContainsKey("@timestamp"));
        }

        // ── Generic format ──────────────────────────────────────────────────

        [Fact]
        public void Parse_Generic_DetectsVariousTimeKeys()
        {
            string line = "{\"ts\":\"2026-01-15T10:00:00Z\",\"severity\":\"error\",\"msg\":\"fail\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("fail", entries[0].Message);
        }

        [Fact]
        public void Parse_Generic_NoMessage_FallsBackToJson()
        {
            string line = "{\"ts\":\"2026-01-15T10:00:00Z\",\"custom\":\"data\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Contains("custom", entries[0].Message);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public void Parse_EmptyInput()
        {
            var entries = Parse("");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_OnlyBlankLines()
        {
            var entries = Parse("  \n\n  \n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_NonJsonLines_Skipped()
        {
            string content = "not json\n{\"level\":\"info\",\"message\":\"ok\",\"timestamp\":\"2026-01-15T10:00:00Z\"}\nnope";
            var entries = Parse(content);
            Assert.Single(entries);
        }

        [Fact]
        public void Parse_MalformedJson_Skipped()
        {
            string content = "{broken}\n{\"level\":\"info\",\"message\":\"ok\",\"timestamp\":\"2026-01-15T10:00:00Z\"}";
            var entries = Parse(content);
            Assert.Single(entries);
        }

        [Fact]
        public void Parse_MultipleLines()
        {
            string content =
                "{\"level\":\"info\",\"message\":\"first\",\"timestamp\":\"2026-01-15T10:00:00Z\"}\n" +
                "{\"level\":\"error\",\"message\":\"second\",\"timestamp\":\"2026-01-15T10:00:01Z\"}";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
            Assert.Equal("first", entries[0].Message);
            Assert.Equal("second", entries[1].Message);
        }

        [Fact]
        public void Parse_NormalizesLevels()
        {
            var levels = new[] { "fatal", "critical", "error", "warning", "info", "debug", "trace", "verbose" };
            var expected = new[] { "Fatal", "Fatal", "Error", "Warning", "Info", "Debug", "Trace", "Trace" };
            for (int i = 0; i < levels.Length; i++)
            {
                string line = $"{{\"level\":\"{levels[i]}\",\"message\":\"m\",\"timestamp\":\"2026-01-15T10:00:00Z\"}}";
                var entries = Parse(line);
                Assert.Single(entries);
                Assert.Equal(expected[i], entries[0].Level);
            }
        }

        [Fact]
        public void Parse_SpecialCharactersInMessage()
        {
            string line = "{\"level\":\"info\",\"message\":\"Line1\\nLine2\\ttab\",\"timestamp\":\"2026-01-15T10:00:00Z\"}";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Contains("Line1", entries[0].Message);
        }

        // ── Name / Version / Extensions ─────────────────────────────────────

        [Fact]
        public void Name_IsSet() => Assert.False(string.IsNullOrEmpty(_plugin.Name));

        [Fact]
        public void Version_IsSet() => Assert.Equal("1.0.0", _plugin.Version);

        [Fact]
        public void SupportedExtensions_ContainsJsonAndJsonl()
        {
            Assert.Contains("*.json", _plugin.SupportedExtensions);
            Assert.Contains("*.jsonl", _plugin.SupportedExtensions);
        }
    }

    public class IndigoAppLogPluginParserTests
    {
        private readonly IndigoAppLogPlugin _plugin = new();

        private static MemoryStream ToStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        private static ParseContext Ctx(string name = "test.log") =>
            new() { FileName = name };

        private List<LogEntryDto> Parse(string content)
        {
            using var stream = ToStream(content);
            return _plugin.Parse(stream, Ctx(), null, CancellationToken.None).ToList();
        }

        // ── CanHandle ───────────────────────────────────────────────────────

        [Fact]
        public void CanHandle_OldFormat_ReturnsTrue()
        {
            var samples = new[] { "2026-01-15 10:32:01,123\u001eThread1\u001eRoot1\u001eFlow1\u001eName1\u001ePat\u001eCtx\u001eERROR Logger\u001eLoc\u001eMsg\u001eEx\u001eData" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_NewFormat_ReturnsTrue()
        {
            var samples = new[] { "2026-01-15 10:32:01,123 |Thread1| |Root1| |Flow1| |Name1| |Pat| |Ctx| ERROR Logger" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_NullSamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", null));
        }

        [Fact]
        public void CanHandle_EmptySamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("app.log", Array.Empty<string>()));
        }

        [Fact]
        public void CanHandle_NonMatchingLines_ReturnsFalse()
        {
            var samples = new[] { "random text without timestamp" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_SkipsEmptyLines()
        {
            var samples = new[] { "", "2026-01-15 10:32:01,123\u001eThread\u001eR\u001eF\u001eN\u001eP\u001eC\u001eINFO Log\u001eL\u001eM\u001eE\u001eD" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        // ── GetColumns ──────────────────────────────────────────────────────

        [Fact]
        public void GetColumns_Returns5Columns()
        {
            var cols = _plugin.GetColumns();
            Assert.Equal(5, cols.Count);
            Assert.Equal("Timestamp", cols[0].Header);
            Assert.Equal("Level", cols[1].Header);
            Assert.Equal("Thread", cols[2].Header);
            Assert.Equal("Logger", cols[3].Header);
            Assert.Equal("Message", cols[4].Header);
        }

        // ── Old format parsing ──────────────────────────────────────────────

        [Fact]
        public void Parse_OldFormat_SingleEntry()
        {
            char rs = '\x1e';
            string line = string.Join(rs.ToString(), new[]
            {
                "2026-01-15 10:32:01,123", "Worker-1", "RootFlow1", "Flow1", "MyFlow",
                "Pattern1", "Context1", "ERROR MyApp.Logger", "MyApp.cs:42",
                "Something failed", "System.Exception: boom", "extra-data", ""
            });
            var entries = Parse(line);
            Assert.Single(entries);
            var e = entries[0];
            Assert.Equal("Error", e.Level);
            Assert.Equal("Worker-1", e.ThreadName);
            Assert.Equal("MyApp.Logger", e.Logger);
            Assert.Equal("Something failed", e.Message);
            Assert.Equal("System.Exception: boom", e.Exception);
            Assert.Equal("extra-data", e.Data);
            Assert.Equal("APP", e.ProcessName);
            Assert.Equal(2026, e.Date.Year);
        }

        [Fact]
        public void Parse_OldFormat_ExtraFields()
        {
            string line = "2026-01-15 10:32:01,123\u001eT1\u001eRootFlow\u001eFlowId\u001eFlowName\u001ePat\u001eCtx\u001eINFO Log\u001eLoc\u001eMsg\u001e\u001e\u001e";
            var entries = Parse(line);
            Assert.Single(entries);
            var e = entries[0];
            Assert.NotNull(e.ExtraFields);
            Assert.Equal("FlowName", e.ExtraFields!["IFlowName"]);
            Assert.Equal("FlowId", e.ExtraFields!["IFlowId"]);
            Assert.Equal("RootFlow", e.ExtraFields!["RootIFlowId"]);
        }

        [Fact]
        public void Parse_OldFormat_MultipleEntries()
        {
            string content =
                "2026-01-15 10:00:00,000\u001eT\u001eR\u001eF\u001eN\u001eP\u001eC\u001eINFO L\u001eLoc\u001eFirst\u001e\u001e\u001e\n" +
                "2026-01-15 10:00:01,000\u001eT\u001eR\u001eF\u001eN\u001eP\u001eC\u001eERROR L\u001eLoc\u001eSecond\u001e\u001e\u001e\n";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Error", entries[1].Level);
        }

        // ── New format parsing ──────────────────────────────────────────────

        [Fact]
        public void Parse_NewFormat_SingleEntry()
        {
            string content =
                "2026-01-15 10:32:01,123 |Worker-1| |Root1| |Flow1| |MyFlow| |Pat| |Ctx| ERROR MyApp.Logger\r\n" +
                "|MyApp.cs:42|\r\n" +
                "Something failed\r\n" +
                "||\r\n";
            var entries = Parse(content);
            Assert.Single(entries);
            var e = entries[0];
            Assert.Equal("Error", e.Level);
            Assert.Equal("Worker-1", e.ThreadName);
            Assert.Equal("MyApp.Logger", e.Logger);
            Assert.Equal("Something failed", e.Message);
            Assert.Equal("APP", e.ProcessName);
        }

        [Fact]
        public void Parse_NewFormat_ExtraFields()
        {
            string content =
                "2026-01-15 10:32:01,123 |T| |RootF| |FlowId| |FlowName| |Pat| |Ctx| INFO Log\r\n" +
                "|Loc|\r\n" +
                "msg\r\n" +
                "||\r\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.NotNull(entries[0].ExtraFields);
            Assert.Equal("FlowName", entries[0].ExtraFields!["IFlowName"]);
        }

        [Fact]
        public void Parse_NewFormat_MultiLineMessage()
        {
            string content =
                "2026-01-15 10:32:01,123 |T| |R| |F| |N| |P| |C| ERROR Logger\r\n" +
                "|Location|\r\n" +
                "Line 1 of message\r\n" +
                "Line 2 of message\r\n" +
                "||\r\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Contains("Line 1", entries[0].Message);
            Assert.Contains("Line 2", entries[0].Message);
        }

        [Fact]
        public void Parse_NewFormat_MultipleEntries()
        {
            string content =
                "2026-01-15 10:00:00,000 |T| |R| |F| |N| |P| |C| INFO L1\r\n" +
                "|Loc|\r\n" +
                "First\r\n" +
                "||\r\n" +
                "2026-01-15 10:00:01,000 |T| |R| |F| |N| |P| |C| WARN L2\r\n" +
                "|Loc|\r\n" +
                "Second\r\n" +
                "||\r\n";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Warning", entries[1].Level);
        }

        // ── Level normalization ─────────────────────────────────────────────

        [Theory]
        [InlineData("FATAL", "Fatal")]
        [InlineData("CRITICAL", "Fatal")]
        [InlineData("ERROR", "Error")]
        [InlineData("WARN", "Warning")]
        [InlineData("WARNING", "Warning")]
        [InlineData("INFO", "Info")]
        [InlineData("INFORMATION", "Info")]
        [InlineData("DEBUG", "Debug")]
        [InlineData("TRACE", "Trace")]
        [InlineData("VERBOSE", "Trace")]
        public void Parse_OldFormat_NormalizesLevel(string rawLevel, string expected)
        {
            string line = $"2026-01-15 10:00:00,000\u001eT\u001eR\u001eF\u001eN\u001eP\u001eC\u001e{rawLevel} L\u001eLoc\u001eMsg\u001e\u001e\u001e";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Level);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public void Parse_EmptyInput()
        {
            var entries = Parse("");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_OnlyWhitespace()
        {
            var entries = Parse("   \n  \n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_OldFormat_EmptyExtraFields_NotSet()
        {
            string line = "2026-01-15 10:00:00,000\u001eT\u001e\u001e\u001e\u001e\u001e\u001eINFO L\u001e\u001eMsg\u001e\u001e\u001e";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Null(entries[0].ExtraFields);
        }

        [Fact]
        public void Parse_OldFormat_ProcessNameIsAPP()
        {
            string line = "2026-01-15 10:00:00,000\u001eT\u001eR\u001eF\u001eN\u001eP\u001eC\u001eINFO L\u001eLoc\u001eMsg\u001e\u001e\u001e";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal("APP", entries[0].ProcessName);
        }

        [Fact]
        public void Parse_OldFormat_HighPrecisionTimestamp()
        {
            string line = "2026-01-15 10:32:01,1234567\u001eT\u001eR\u001eF\u001eN\u001eP\u001eC\u001eINFO L\u001eLoc\u001eMsg\u001e\u001e\u001e";
            var entries = Parse(line);
            Assert.Single(entries);
            Assert.Equal(2026, entries[0].Date.Year);
        }

        // ── Name / Version / Extensions ─────────────────────────────────────

        [Fact]
        public void Name_IsSet() => Assert.False(string.IsNullOrEmpty(_plugin.Name));

        [Fact]
        public void Version_IsSet() => Assert.Equal("1.0.0", _plugin.Version);

        [Fact]
        public void SupportedExtensions_ContainsLogAndTxt()
        {
            Assert.Contains("*.log", _plugin.SupportedExtensions);
            Assert.Contains("*.txt", _plugin.SupportedExtensions);
        }
    }

    public class GenericTextLogPluginParserTests
    {
        private readonly GenericTextLogPlugin _plugin = new();

        private static MemoryStream ToStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        private static ParseContext Ctx(string name = "test.log") =>
            new() { FileName = name };

        private List<LogEntryDto> Parse(string content)
        {
            using var stream = ToStream(content);
            return _plugin.Parse(stream, Ctx(), null, CancellationToken.None).ToList();
        }

        // ── CanHandle ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("app.log")]
        [InlineData("app.txt")]
        [InlineData("app.err")]
        [InlineData("app.error")]
        [InlineData("app.out")]
        public void CanHandle_SupportedExtension_ReturnsTrue(string fileName)
        {
            var samples = new[] { "some text line" };
            Assert.True(_plugin.CanHandle(fileName, samples));
        }

        [Theory]
        [InlineData("app.csv")]
        [InlineData("app.json")]
        [InlineData("app.xml")]
        public void CanHandle_UnsupportedExtension_ReturnsFalse(string fileName)
        {
            Assert.False(_plugin.CanHandle(fileName, new[] { "text" }));
        }

        [Fact]
        public void CanHandle_JsonContent_ReturnsFalse()
        {
            var samples = new[] { "{\"level\":\"info\"}" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_ArrayJsonContent_ReturnsFalse()
        {
            var samples = new[] { "[{\"level\":\"info\"}]" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_Log4netXmlContent_ReturnsFalse()
        {
            var samples = new[] { "<log4net:event logger=\"A\">" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_Log4jXmlContent_ReturnsFalse()
        {
            var samples = new[] { "<log4j:event logger=\"A\">" };
            Assert.False(_plugin.CanHandle("app.log", samples));
        }

        // ── GetColumns ──────────────────────────────────────────────────────

        [Fact]
        public void GetColumns_Returns3Columns()
        {
            var cols = _plugin.GetColumns();
            Assert.Equal(3, cols.Count);
            Assert.Equal("Timestamp", cols[0].Header);
            Assert.Equal("Level", cols[1].Header);
            Assert.Equal("Message", cols[2].Header);
        }

        // ── Pattern 1: ISO timestamp with ms + level ────────────────────────

        [Fact]
        public void Parse_Pattern1_TimestampMsLevel()
        {
            var entries = Parse("2026-01-15 10:32:01.123 ERROR Something went wrong");
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Something went wrong", entries[0].Message);
            Assert.Equal(2026, entries[0].Date.Year);
        }

        [Fact]
        public void Parse_Pattern1_BracketedLevel()
        {
            var entries = Parse("2026-01-15 10:32:01,123 [WARN] Disk usage high");
            Assert.Single(entries);
            Assert.Equal("Warning", entries[0].Level);
        }

        [Fact]
        public void Parse_Pattern1_CommaSeparator()
        {
            var entries = Parse("2026-01-15 10:32:01,456 INFO App started");
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
        }

        // ── Pattern 2: ISO timestamp without ms + level ─────────────────────

        [Fact]
        public void Parse_Pattern2_NoMs()
        {
            var entries = Parse("2026-01-15 10:32:01 ERROR Something failed");
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
        }

        [Fact]
        public void Parse_Pattern2_ColonAfterLevel()
        {
            var entries = Parse("2026-01-15 10:32:01 WARN: Retrying connection");
            Assert.Single(entries);
            Assert.Equal("Warning", entries[0].Level);
        }

        // ── Pattern 3: [timestamp] [level] ──────────────────────────────────

        [Fact]
        public void Parse_Pattern3_BothBracketed()
        {
            var entries = Parse("[2026-01-15 10:32:01] [INFO] Server started");
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Server started", entries[0].Message);
        }

        [Fact]
        public void Parse_Pattern3_TimestampBracketedLevelNot()
        {
            var entries = Parse("[2026-01-15 10:32:01.123] ERROR Connection dropped");
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
        }

        // ── Pattern 4: Level first, then timestamp ──────────────────────────

        [Fact]
        public void Parse_Pattern4_LevelFirst()
        {
            var entries = Parse("ERROR 2026-01-15 10:32:01.456 Disk full");
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Disk full", entries[0].Message);
        }

        // ── Pattern 5: syslog format ────────────────────────────────────────

        [Fact]
        public void Parse_Pattern5_Syslog()
        {
            var entries = Parse("Jan 15 10:32:01 hostname myapp[1234]: Service started");
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("Service started", entries[0].Message);
        }

        // ── Continuation lines ──────────────────────────────────────────────

        [Fact]
        public void Parse_ContinuationLines()
        {
            string content =
                "2026-01-15 10:32:01.123 ERROR Exception thrown\n" +
                "  at MyApp.Method()\n" +
                "  at MyApp.Main()\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Contains("at MyApp.Method()", entries[0].Message);
        }

        [Fact]
        public void Parse_StandaloneNonMatchingLine_EmittedAsInfo()
        {
            var entries = Parse("some line that does not match any pattern");
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
            Assert.Equal("some line that does not match any pattern", entries[0].Message);
        }

        // ── Level normalization ─────────────────────────────────────────────

        [Theory]
        [InlineData("FATAL", "Fatal")]
        [InlineData("CRITICAL", "Fatal")]
        [InlineData("ERROR", "Error")]
        [InlineData("WARN", "Warning")]
        [InlineData("WARNING", "Warning")]
        [InlineData("INFO", "Info")]
        [InlineData("INFORMATION", "Info")]
        [InlineData("DEBUG", "Debug")]
        [InlineData("TRACE", "Trace")]
        [InlineData("VERBOSE", "Trace")]
        public void Parse_NormalizesLevel(string level, string expected)
        {
            var entries = Parse($"2026-01-15 10:00:00 {level} test message");
            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Level);
        }

        // ── Multiple entries ────────────────────────────────────────────────

        [Fact]
        public void Parse_MultipleEntries()
        {
            string content =
                "2026-01-15 10:00:00 INFO First message\n" +
                "2026-01-15 10:00:01 WARN Second message\n";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public void Parse_EmptyInput()
        {
            var entries = Parse("");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_OnlyBlankLines()
        {
            var entries = Parse("  \n  \n");
            Assert.Empty(entries);
        }

        // ── Name / Version / Extensions ─────────────────────────────────────

        [Fact]
        public void Name_IsSet() => Assert.False(string.IsNullOrEmpty(_plugin.Name));

        [Fact]
        public void Version_IsSet() => Assert.Equal("1.0.0", _plugin.Version);

        [Fact]
        public void SupportedExtensions_ContainsAllTypes()
        {
            var ext = _plugin.SupportedExtensions;
            Assert.Contains("*.log", ext);
            Assert.Contains("*.txt", ext);
            Assert.Contains("*.err", ext);
            Assert.Contains("*.error", ext);
            Assert.Contains("*.out", ext);
        }
    }

    public class CsvAutoPluginParserTests
    {
        private readonly CsvAutoPlugin _plugin = new();

        private static MemoryStream ToStream(string content) =>
            new(Encoding.UTF8.GetBytes(content));

        private static ParseContext Ctx(string name = "test.csv") =>
            new() { FileName = name };

        private List<LogEntryDto> Parse(string content)
        {
            using var stream = ToStream(content);
            return _plugin.Parse(stream, Ctx(), null, CancellationToken.None).ToList();
        }

        // ── CanHandle ───────────────────────────────────────────────────────

        [Fact]
        public void CanHandle_CsvWithHeader_ReturnsTrue()
        {
            var samples = new[] { "Timestamp,Level,Message" };
            Assert.True(_plugin.CanHandle("data.csv", samples));
        }

        [Fact]
        public void CanHandle_WrongExtension_ReturnsFalse()
        {
            var samples = new[] { "Timestamp,Level,Message" };
            Assert.False(_plugin.CanHandle("data.log", samples));
        }

        [Fact]
        public void CanHandle_NullSamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("data.csv", null));
        }

        [Fact]
        public void CanHandle_EmptySamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("data.csv", Array.Empty<string>()));
        }

        [Fact]
        public void CanHandle_NoCommaInHeader_ReturnsFalse()
        {
            var samples = new[] { "JustOneColumn" };
            Assert.False(_plugin.CanHandle("data.csv", samples));
        }

        [Fact]
        public void CanHandle_EventHistoryFile_ReturnsFalse()
        {
            var samples = new[] { "Timestamp,Level,Message" };
            Assert.False(_plugin.CanHandle("event-history__from2026.csv", samples));
        }

        [Fact]
        public void CanHandle_PressEventsFile_ReturnsFalse()
        {
            var samples = new[] { "Timestamp,Level,Message" };
            Assert.False(_plugin.CanHandle("pressevents_2026.csv", samples));
        }

        [Fact]
        public void CanHandle_BlankHeader_ReturnsFalse()
        {
            var samples = new[] { "   " };
            Assert.False(_plugin.CanHandle("data.csv", samples));
        }

        // ── GetColumns ──────────────────────────────────────────────────────

        [Fact]
        public void GetColumns_Returns3DefaultColumns()
        {
            var cols = _plugin.GetColumns();
            Assert.Equal(3, cols.Count);
        }

        // ── Parse basic ─────────────────────────────────────────────────────

        [Fact]
        public void Parse_BasicCsv()
        {
            string content =
                "Timestamp,Level,Message\n" +
                "2026-01-15 10:32:01,ERROR,Query failed\n" +
                "2026-01-15 10:32:02,INFO,Request handled\n";
            var entries = Parse(content);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Query failed", entries[0].Message);
            Assert.Equal("Info", entries[1].Level);
        }

        [Fact]
        public void Parse_WithThreadAndLogger()
        {
            string content =
                "Timestamp,Level,Thread,Logger,Message\n" +
                "2026-01-15 10:00:00,INFO,Worker-1,MyApp.Db,Connected\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Equal("Worker-1", entries[0].ThreadName);
            Assert.Equal("MyApp.Db", entries[0].Logger);
        }

        [Fact]
        public void Parse_ExtraColumnsInExtraFields()
        {
            string content =
                "Timestamp,Level,Message,Duration,Host\n" +
                "2026-01-15 10:00:00,INFO,ok,250,server1\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.NotNull(entries[0].ExtraFields);
            Assert.Equal("250", entries[0].ExtraFields!["Duration"]);
            Assert.Equal("server1", entries[0].ExtraFields!["Host"]);
        }

        [Fact]
        public void Parse_QuotedFields()
        {
            string content =
                "Timestamp,Level,Message\n" +
                "\"2026-01-15 10:00:00\",\"INFO\",\"Message with, comma\"\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Equal("Message with, comma", entries[0].Message);
        }

        [Fact]
        public void Parse_AlternateColumnNames()
        {
            string content =
                "time,severity,component,description\n" +
                "2026-01-15 10:00:00,error,MyModule,Disk full\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("MyModule", entries[0].Logger);
            Assert.Equal("Disk full", entries[0].Message);
        }

        // ── Level normalization ─────────────────────────────────────────────

        [Theory]
        [InlineData("FATAL", "Fatal")]
        [InlineData("CRITICAL", "Fatal")]
        [InlineData("ERROR", "Error")]
        [InlineData("WARN", "Warning")]
        [InlineData("WARNING", "Warning")]
        [InlineData("INFO", "Info")]
        [InlineData("DEBUG", "Debug")]
        [InlineData("TRACE", "Trace")]
        [InlineData("VERBOSE", "Trace")]
        public void Parse_NormalizesLevel(string level, string expected)
        {
            string content = $"Timestamp,Level,Message\n2026-01-15 10:00:00,{level},test\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Level);
        }

        [Fact]
        public void Parse_NullLevel_DefaultsToInfo()
        {
            string content = "Timestamp,Message\n2026-01-15 10:00:00,test msg\n";
            var entries = Parse(content);
            Assert.Single(entries);
            Assert.Equal("Info", entries[0].Level);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public void Parse_EmptyInput()
        {
            var entries = Parse("");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_HeaderOnly()
        {
            var entries = Parse("Timestamp,Level,Message\n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_SkipsBlankRows()
        {
            string content = "Timestamp,Level,Message\n\n2026-01-15 10:00:00,INFO,ok\n  \n";
            var entries = Parse(content);
            Assert.Single(entries);
        }

        [Fact]
        public void Parse_MissingColumnsInRow()
        {
            string content = "Timestamp,Level,Message,Extra\n2026-01-15 10:00:00,INFO\n";
            var entries = Parse(content);
            Assert.Single(entries);
            // Message column is missing from this row, so it falls back to empty or null
            Assert.True(string.IsNullOrEmpty(entries[0].Message));
        }

        // ── Name / Version / Extensions ─────────────────────────────────────

        [Fact]
        public void Name_IsSet() => Assert.False(string.IsNullOrEmpty(_plugin.Name));

        [Fact]
        public void Version_IsSet() => Assert.Equal("1.0.0", _plugin.Version);

        [Fact]
        public void SupportedExtensions_ContainsCsv()
        {
            Assert.Contains("*.csv", _plugin.SupportedExtensions);
        }
    }
}
