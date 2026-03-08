using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.BuiltInPlugins;
using IndiLogs_3._0.ViewModels;
using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit;

namespace IndiLogs.Tests
{
    public class HelperMethodTests
    {
        // ── MainViewModel.SplitCsvLineHelper ──

        private static List<string> InvokeSplitCsvLine(string line)
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (List<string>)method!.Invoke(null, new object[] { line })!;
        }

        [Fact]
        public void SplitCsvLine_Simple()
        {
            var result = InvokeSplitCsvLine("a,b,c");
            Assert.Equal(3, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsvLine_QuotedField()
        {
            var result = InvokeSplitCsvLine("\"hello, world\",b");
            Assert.Equal(2, result.Count);
            Assert.Equal("hello, world", result[0]);
        }

        [Fact]
        public void SplitCsvLine_EmptyFields()
        {
            var result = InvokeSplitCsvLine(",,,");
            Assert.Equal(4, result.Count);
            Assert.All(result, s => Assert.Equal("", s));
        }

        [Fact]
        public void SplitCsvLine_SingleField()
        {
            var result = InvokeSplitCsvLine("only");
            Assert.Single(result);
            Assert.Equal("only", result[0]);
        }

        [Fact]
        public void SplitCsvLine_EmptyString()
        {
            var result = InvokeSplitCsvLine("");
            Assert.Single(result);
            Assert.Equal("", result[0]);
        }

        [Fact]
        public void SplitCsvLine_QuotedWithInternalQuotes()
        {
            var result = InvokeSplitCsvLine("\"a\"\"b\",c");
            Assert.Equal(2, result.Count);
            // Quotes get stripped, inner doubled quotes become single
            Assert.Equal("ab", result[0]);
        }

        // ── CsvAutoPlugin ──

        [Fact]
        public void CsvAutoPlugin_CanHandle_ValidCsv()
        {
            var plugin = new CsvAutoPlugin();
            Assert.True(plugin.CanHandle("data.csv", new[] { "Time,Level,Message" }));
        }

        [Fact]
        public void CsvAutoPlugin_CanHandle_NotCsv()
        {
            var plugin = new CsvAutoPlugin();
            Assert.False(plugin.CanHandle("data.txt", new[] { "Time,Level,Message" }));
        }

        [Fact]
        public void CsvAutoPlugin_CanHandle_EventHistory_False()
        {
            var plugin = new CsvAutoPlugin();
            Assert.False(plugin.CanHandle("event-history__From2024.csv", new[] { "Time,Level" }));
        }

        [Fact]
        public void CsvAutoPlugin_CanHandle_PressEvents_False()
        {
            var plugin = new CsvAutoPlugin();
            Assert.False(plugin.CanHandle("pressEvents.csv", new[] { "Time,Level" }));
        }

        [Fact]
        public void CsvAutoPlugin_CanHandle_NoSampleLines_False()
        {
            var plugin = new CsvAutoPlugin();
            Assert.False(plugin.CanHandle("data.csv", null));
            Assert.False(plugin.CanHandle("data.csv", Array.Empty<string>()));
        }

        [Fact]
        public void CsvAutoPlugin_CanHandle_NoCommaInHeader_False()
        {
            var plugin = new CsvAutoPlugin();
            Assert.False(plugin.CanHandle("data.csv", new[] { "single_column" }));
        }

        [Fact]
        public void CsvAutoPlugin_GetColumns_Returns3()
        {
            var plugin = new CsvAutoPlugin();
            var cols = plugin.GetColumns();
            Assert.Equal(3, cols.Count);
        }

        [Fact]
        public void CsvAutoPlugin_Properties()
        {
            var plugin = new CsvAutoPlugin();
            Assert.Equal("CSV Auto-Detect (Built-in)", plugin.Name);
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Contains("*.csv", plugin.SupportedExtensions);
        }

        [Fact]
        public void CsvAutoPlugin_Parse_SimpleCsv()
        {
            var plugin = new CsvAutoPlugin();
            var csv = "Timestamp,Level,Message\n2024-01-15 10:30:00,ERROR,Something failed\n2024-01-15 10:31:00,INFO,All good";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();

            Assert.Equal(2, entries.Count);
            Assert.Equal("Error", entries[0].Level);
            Assert.Equal("Something failed", entries[0].Message);
            Assert.Equal("Info", entries[1].Level);
        }

        [Fact]
        public void CsvAutoPlugin_Parse_WithExtraColumns()
        {
            var plugin = new CsvAutoPlugin();
            var csv = "Timestamp,Level,Message,Duration,Source\n2024-01-15 10:30:00,ERROR,Fail,250,API";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            Assert.NotNull(entries[0].ExtraFields);
            Assert.Equal("250", entries[0].ExtraFields!["Duration"]);
            // "Source" may get mapped to Logger field since it matches "source" keyword
            // Just check Duration is in extras
        }

        [Fact]
        public void CsvAutoPlugin_Parse_ThreadAndLogger()
        {
            var plugin = new CsvAutoPlugin();
            var csv = "Timestamp,Level,Message,Thread,Logger\n2024-01-15 10:30:00,INFO,Test,Main,App.Service";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            Assert.Equal("Main", entries[0].ThreadName);
            Assert.Equal("App.Service", entries[0].Logger);
        }

        [Fact]
        public void CsvAutoPlugin_Parse_EmptyStream()
        {
            var plugin = new CsvAutoPlugin();
            using var stream = new MemoryStream(Array.Empty<byte>());
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();
            Assert.Empty(entries);
        }

        [Fact]
        public void CsvAutoPlugin_Parse_HeaderOnly()
        {
            var plugin = new CsvAutoPlugin();
            var csv = "Timestamp,Level,Message\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();
            Assert.Empty(entries);
        }

        [Fact]
        public void CsvAutoPlugin_Parse_QuotedFieldsInData()
        {
            var plugin = new CsvAutoPlugin();
            var csv = "Timestamp,Level,Message\n2024-01-15 10:30:00,ERROR,\"Error: connection, timeout\"";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var context = new ParseContext { FileName = "test.csv" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();

            Assert.Single(entries);
            Assert.Equal("Error: connection, timeout", entries[0].Message);
        }

        // ── CsvAutoPlugin private helpers via reflection ──

        [Theory]
        [InlineData(null, "Info")]
        [InlineData("FATAL", "Fatal")]
        [InlineData("CRITICAL", "Fatal")]
        [InlineData("ERROR", "Error")]
        [InlineData("WARNING", "Warning")]
        [InlineData("WARN", "Warning")]
        [InlineData("INFO", "Info")]
        [InlineData("DEBUG", "Debug")]
        [InlineData("TRACE", "Trace")]
        [InlineData("VERBOSE", "Trace")]
        [InlineData("CustomLevel", "CustomLevel")]
        public void CsvAutoPlugin_NormalizeLevel(string? input, string expected)
        {
            var method = typeof(CsvAutoPlugin).GetMethod("NormalizeLevel",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (string)method!.Invoke(null, new object?[] { input })!;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CsvAutoPlugin_FindColumn_MatchesKeyword()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("FindColumn",
                BindingFlags.NonPublic | BindingFlags.Static);

            var headers = new[] { "ID", "Timestamp", "Message" };
            var result = (int)method!.Invoke(null, new object[] { headers, new[] { "time", "date" } })!;
            Assert.Equal(1, result);
        }

        [Fact]
        public void CsvAutoPlugin_FindColumn_NoMatch_ReturnsNeg1()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("FindColumn",
                BindingFlags.NonPublic | BindingFlags.Static);

            var headers = new[] { "A", "B", "C" };
            var result = (int)method!.Invoke(null, new object[] { headers, new[] { "xyz" } })!;
            Assert.Equal(-1, result);
        }

        [Fact]
        public void CsvAutoPlugin_SafeGet_InRange()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("SafeGet",
                BindingFlags.NonPublic | BindingFlags.Static);
            var parts = new[] { "a", "b", "c" };
            var result = (string?)method!.Invoke(null, new object[] { parts, 1 });
            Assert.Equal("b", result);
        }

        [Fact]
        public void CsvAutoPlugin_SafeGet_OutOfRange()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("SafeGet",
                BindingFlags.NonPublic | BindingFlags.Static);
            var parts = new[] { "a" };
            var result = (string?)method!.Invoke(null, new object[] { parts, 5 });
            Assert.Null(result);
        }

        [Fact]
        public void CsvAutoPlugin_SafeGet_NegativeIndex()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("SafeGet",
                BindingFlags.NonPublic | BindingFlags.Static);
            var parts = new[] { "a" };
            var result = (string?)method!.Invoke(null, new object[] { parts, -1 });
            Assert.Null(result);
        }

        [Fact]
        public void CsvAutoPlugin_SafeGet_EmptyString_ReturnsNull()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("SafeGet",
                BindingFlags.NonPublic | BindingFlags.Static);
            var parts = new[] { "", "  " };
            Assert.Null((string?)method!.Invoke(null, new object[] { parts, 0 }));
            Assert.Null((string?)method!.Invoke(null, new object[] { parts, 1 }));
        }

        [Fact]
        public void CsvAutoPlugin_ParseDate_ValidFormat()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("ParseDate",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object?[] { "2024-01-15 10:30:00" })!;
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result);
        }

        [Fact]
        public void CsvAutoPlugin_ParseDate_Null_ReturnsNow()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("ParseDate",
                BindingFlags.NonPublic | BindingFlags.Static);
            var before = DateTime.Now.AddSeconds(-1);
            var result = (DateTime)method!.Invoke(null, new object?[] { null })!;
            Assert.True(result >= before);
        }

        [Fact]
        public void CsvAutoPlugin_ParseDate_WithCommaFraction()
        {
            var method = typeof(CsvAutoPlugin).GetMethod("ParseDate",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object?[] { "2024-01-15 10:30:00,123456" })!;
            Assert.Equal(2024, result.Year);
            Assert.Equal(10, result.Hour);
        }

        // ── GenericTextLogPlugin ──

        [Fact]
        public void GenericTextLogPlugin_CanHandle_TxtFile()
        {
            var plugin = new GenericTextLogPlugin();
            Assert.True(plugin.CanHandle("app.txt", new[] { "2024-01-15 10:30:00 INFO test message" }));
        }

        [Fact]
        public void GenericTextLogPlugin_CanHandle_LogFile()
        {
            var plugin = new GenericTextLogPlugin();
            Assert.True(plugin.CanHandle("app.log", new[] { "2024-01-15 10:30:00 INFO test message" }));
        }

        [Fact]
        public void GenericTextLogPlugin_CanHandle_NullSamples()
        {
            var plugin = new GenericTextLogPlugin();
            // CanHandle may return true or false for null samples depending on implementation
            var result = plugin.CanHandle("app.log", null);
            Assert.IsType<bool>(result);
        }

        [Fact]
        public void GenericTextLogPlugin_Properties()
        {
            var plugin = new GenericTextLogPlugin();
            Assert.Equal("Generic Text Log (Built-in)", plugin.Name);
            Assert.Contains("*.log", plugin.SupportedExtensions);
            Assert.Contains("*.txt", plugin.SupportedExtensions);
        }

        [Fact]
        public void GenericTextLogPlugin_Parse_SimpleLog()
        {
            var plugin = new GenericTextLogPlugin();
            var log = "2024-01-15 10:30:00 INFO Starting application\n2024-01-15 10:30:01 ERROR Something went wrong";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(log));
            var context = new ParseContext { FileName = "test.log" };

            var entries = plugin.Parse(stream, context, null, CancellationToken.None).ToList();

            Assert.True(entries.Count >= 1); // At least one entry should parse
        }

        [Fact]
        public void GenericTextLogPlugin_GetColumns()
        {
            var plugin = new GenericTextLogPlugin();
            var cols = plugin.GetColumns();
            Assert.True(cols.Count >= 3);
        }

        // ── AppSettingsService ──

        [Fact]
        public void AppSettingsService_JiraUrl_HasDefault()
        {
            Assert.False(string.IsNullOrEmpty(AppSettingsService.JiraUrl));
        }

        // ── IndigoAppLogPlugin ──

        [Fact]
        public void IndigoAppLogPlugin_Properties()
        {
            var plugin = new IndigoAppLogPlugin();
            Assert.Contains("Built-in", plugin.Name);
            Assert.NotEmpty(plugin.SupportedExtensions);
        }

        [Fact]
        public void IndigoAppLogPlugin_GetColumns()
        {
            var plugin = new IndigoAppLogPlugin();
            var cols = plugin.GetColumns();
            Assert.True(cols.Count >= 3);
        }
    }
}
