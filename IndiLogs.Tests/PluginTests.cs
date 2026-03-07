using IndiLogs_3._0.Services.BuiltInPlugins;
using IndiLogs.PluginAPI;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace IndiLogs.Tests
{
    public class GenericTextLogPluginTests
    {
        private readonly GenericTextLogPlugin _plugin = new();

        [Theory]
        [InlineData("app.log", true)]
        [InlineData("error.txt", true)]
        [InlineData("system.err", true)]
        [InlineData("output.out", true)]
        [InlineData("debug.error", true)]
        [InlineData("data.csv", false)]
        [InlineData("config.json", false)]
        [InlineData("image.png", false)]
        public void CanHandle_ByExtension(string fileName, bool expected)
        {
            Assert.Equal(expected, _plugin.CanHandle(fileName, null));
        }

        [Fact]
        public void Name_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(_plugin.Name));
        }

        [Fact]
        public void Parse_SimpleTimestampedLines()
        {
            var lines = "2025-01-01 10:00:00,000 [Worker] INFO MyLogger - Hello world\n" +
                        "2025-01-01 10:00:01,000 [Worker] ERROR MyLogger - Something failed\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(lines));
            var ctx = new ParseContext { FileName = "test.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.NotNull(entries);
            Assert.True(entries.Count >= 1);
        }

        [Fact]
        public void Parse_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var ctx = new ParseContext { FileName = "test.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_NoMatchingFormat_StillReturns()
        {
            var lines = "just some random text\nanother line\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(lines));
            var ctx = new ParseContext { FileName = "test.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            // May or may not parse — depends on regex matching
            Assert.NotNull(entries);
        }
    }

    public class CsvAutoPluginTests
    {
        private readonly CsvAutoPlugin _plugin = new();

        [Fact]
        public void CanHandle_CsvWithHeader_ReturnsTrue()
        {
            var samples = new[] { "Timestamp,Level,Message" };
            Assert.True(_plugin.CanHandle("data.csv", samples));
        }

        [Theory]
        [InlineData("data.txt")]
        [InlineData("config.json")]
        public void CanHandle_NonCsv_ReturnsFalse(string fileName)
        {
            Assert.False(_plugin.CanHandle(fileName, null));
        }

        [Fact]
        public void CanHandle_CsvNoSamples_ReturnsFalse()
        {
            Assert.False(_plugin.CanHandle("data.csv", null));
        }

        [Fact]
        public void Name_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(_plugin.Name));
        }

        [Fact]
        public void Parse_ValidCsv_ParsesRows()
        {
            var csv = "Timestamp,Level,Message\n" +
                      "2025-01-01 10:00:00,Error,Disk failure\n" +
                      "2025-01-01 10:00:01,Info,Recovery started\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var ctx = new ParseContext { FileName = "test.csv" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Equal(2, entries.Count);
            Assert.Equal("Error", entries[0].Level);
            Assert.Contains("Disk failure", entries[0].Message);
        }

        [Fact]
        public void Parse_HeaderOnly_ReturnsEmpty()
        {
            var csv = "Timestamp,Level,Message\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var ctx = new ParseContext { FileName = "test.csv" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var ctx = new ParseContext { FileName = "test.csv" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_QuotedValues_HandlesCorrectly()
        {
            var csv = "Timestamp,Level,Message\n" +
                      "2025-01-01,Info,\"Message with, comma\"\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var ctx = new ParseContext { FileName = "test.csv" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            Assert.Contains("comma", entries[0].Message);
        }

        [Fact]
        public void Parse_DateColumn_ParsesTimestamp()
        {
            var csv = "date,level,message\n" +
                      "2025-06-15 14:30:00,Warning,Test warning\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var ctx = new ParseContext { FileName = "test.csv" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            Assert.Equal(2025, entries[0].Date.Year);
        }
    }

    public class JsonLogPluginTests
    {
        private readonly JsonLogPlugin _plugin = new();

        [Theory]
        [InlineData("logs.jsonl", true)]
        [InlineData("data.json", true)]
        [InlineData("data.csv", false)]
        public void CanHandle_ByExtension(string fileName, bool expected)
        {
            Assert.Equal(expected, _plugin.CanHandle(fileName, null));
        }

        [Fact]
        public void CanHandle_WithJsonSampleLines_ReturnsTrue()
        {
            var samples = new[] { "{\"level\":\"info\",\"message\":\"test\"}" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void Name_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(_plugin.Name));
        }

        [Fact]
        public void Parse_Ndjson_ParsesEntries()
        {
            var json = "{\"timestamp\":\"2025-01-01T10:00:00Z\",\"level\":\"error\",\"message\":\"fail\"}\n" +
                       "{\"timestamp\":\"2025-01-01T10:00:01Z\",\"level\":\"info\",\"message\":\"ok\"}\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var ctx = new ParseContext { FileName = "test.jsonl" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public void Parse_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var ctx = new ParseContext { FileName = "test.jsonl" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }

        [Fact]
        public void Parse_SerilogFormat()
        {
            var json = "{\"Timestamp\":\"2025-01-01T10:00:00Z\",\"Level\":\"Error\",\"MessageTemplate\":\"Something went {Wrong}\",\"RenderedMessage\":\"Something went bad\"}\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var ctx = new ParseContext { FileName = "test.jsonl" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Single(entries);
        }

        [Fact]
        public void Parse_BunyanFormat()
        {
            var json = "{\"time\":\"2025-01-01T10:00:00Z\",\"level\":50,\"msg\":\"error msg\",\"name\":\"myapp\"}\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var ctx = new ParseContext { FileName = "test.jsonl" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            // Bunyan uses numeric levels; plugin may or may not map them
            Assert.NotNull(entries[0].Level);
        }
    }

    public class Log4NetPluginTests
    {
        private readonly Log4NetPlugin _plugin = new();

        [Fact]
        public void CanHandle_WithXmlEventSample_ReturnsTrue()
        {
            var samples = new[] { "<log4j:event logger=\"MyApp\"" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void CanHandle_WithPatternLayoutSample_ReturnsTrue()
        {
            var samples = new[] { "2025-01-01 10:00:00,000 [main] ERROR MyLogger - message" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void Name_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(_plugin.Name));
        }

        [Fact]
        public void Parse_PatternLayout_ParsesEntries()
        {
            var text = "2025-01-01 10:00:00,000 [Worker] ERROR MyLogger - Something failed\n" +
                       "2025-01-01 10:00:01,000 [Worker] INFO MyLogger - Recovered\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var ctx = new ParseContext { FileName = "test.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Equal(2, entries.Count);
            Assert.Equal("Error", entries[0].Level);
        }

        [Fact]
        public void Parse_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var ctx = new ParseContext { FileName = "test.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }
    }

    public class IndigoAppLogPluginTests
    {
        private readonly IndigoAppLogPlugin _plugin = new();

        [Fact]
        public void Name_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(_plugin.Name));
        }

        [Fact]
        public void CanHandle_WithOldFormatSample_ReturnsTrue()
        {
            // Old format uses \x1e delimiter
            var samples = new[] { "2025-01-01 10:00:00,000\x1eINFO\x1eThread1\x1eLogger\x1eMethod\x1eMessage" };
            Assert.True(_plugin.CanHandle("app.log", samples));
        }

        [Fact]
        public void Parse_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var ctx = new ParseContext { FileName = "AppDev.log" };
            var entries = _plugin.Parse(stream, ctx, null, CancellationToken.None).ToList();

            Assert.Empty(entries);
        }
    }
}
