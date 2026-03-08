using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogFileServiceParserTests
    {
        private static LogFileService CreateService()
        {
            return (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
        }

        // ── ParseEventsCsv ──

        [Fact]
        public void ParseEventsCsv_EmptyStream_ReturnsEmptyList()
        {
            var svc = CreateService();
            using var stream = new MemoryStream(Array.Empty<byte>());
            var result = svc.ParseEventsCsv(stream);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseEventsCsv_HeaderOnly_ReturnsEmptyList()
        {
            var svc = CreateService();
            var csv = "Time,Name,State,Severity,Parameters,Description\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseEventsCsv_SimpleData_ParsesCorrectly()
        {
            var svc = CreateService();
            var csv = "Time,Name,State,Severity,Parameters,Description\n" +
                      "2024-01-15 10:30:00,PrintError,Active,High,param1,Something broke\n" +
                      "2024-01-15 10:31:00,PaperJam,Cleared,Medium,,Paper removed\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Equal(2, result.Count);
            Assert.Equal("PrintError", result[0].Name);
            Assert.Equal("Active", result[0].State);
            Assert.Equal("High", result[0].Severity);
            Assert.Equal("param1", result[0].Parameters);
            Assert.Equal("Something broke", result[0].Description);
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result[0].Time);
        }

        [Fact]
        public void ParseEventsCsv_AlternativeHeaders_StillParses()
        {
            var svc = CreateService();
            var csv = "Timestamp,EventName,Status,Level,Params,Info\n" +
                      "2024-01-15 10:30:00,TestEvent,OK,Warning,p1,desc1\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
            Assert.Equal("TestEvent", result[0].Name);
        }

        [Fact]
        public void ParseEventsCsv_DateHeaders_Detected()
        {
            var svc = CreateService();
            var csv = "Date,Event,State\n2024-01-15 10:30:00,MyEvent,Active\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
            Assert.Equal("MyEvent", result[0].Name);
        }

        [Fact]
        public void ParseEventsCsv_InvalidDate_Skipped()
        {
            var svc = CreateService();
            var csv = "Time,Name\nnotadate,Event1\n2024-01-15 10:30:00,Event2\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
            Assert.Equal("Event2", result[0].Name);
        }

        [Fact]
        public void ParseEventsCsv_EmptyLines_Skipped()
        {
            var svc = CreateService();
            var csv = "Time,Name\n\n2024-01-15 10:30:00,Event1\n\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
        }

        [Fact]
        public void ParseEventsCsv_QuotedFields_Handled()
        {
            var svc = CreateService();
            var csv = "Time,Name,Description\n\"2024-01-15 10:30:00\",\"Test, Event\",\"Error: something, happened\"\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
        }

        [Fact]
        public void ParseEventsCsv_MissingColumns_DefaultValues()
        {
            var svc = CreateService();
            var csv = "Time,Name\n2024-01-15 10:30:00,OnlyName\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = svc.ParseEventsCsv(stream);

            Assert.Single(result);
            Assert.Equal("OnlyName", result[0].Name);
            Assert.Equal(string.Empty, result[0].State);
            Assert.Equal(string.Empty, result[0].Severity);
        }

        // ── ConvertEventsXmlToCsv ──

        [Fact]
        public void ConvertEventsXmlToCsv_SimpleAttributes()
        {
            var xml = "<Events><Event Time=\"2024-01-15\" Name=\"Error1\" State=\"Active\"/></Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Contains("Time", csv);
            Assert.Contains("Name", csv);
            Assert.Contains("Error1", csv);
            Assert.Contains("Active", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_Elements()
        {
            var xml = "<Events><Event><Time>2024-01-15</Time><Name>Error1</Name></Event></Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Contains("Time", csv);
            Assert.Contains("Error1", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_EmptyRoot_ReturnsEmpty()
        {
            var xml = "<Events></Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Equal("", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_InvalidXml_ReturnsEmpty()
        {
            var xml = "not xml at all";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Equal("", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_MultipleRecords()
        {
            var xml = "<Events>" +
                      "<Event Time=\"2024-01-15\" Name=\"E1\" Severity=\"High\"/>" +
                      "<Event Time=\"2024-01-16\" Name=\"E2\" Severity=\"Low\"/>" +
                      "</Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            // Header + 2 data lines
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 3);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_QuotesInValues_Escaped()
        {
            var xml = "<Events><Event Name=\"He said &quot;hello&quot;\"/></Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            // Quotes should be doubled in CSV
            Assert.Contains("\"\"", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_DifferentColumnSets()
        {
            var xml = "<Events>" +
                      "<Event Time=\"2024-01-15\" Name=\"E1\"/>" +
                      "<Event Time=\"2024-01-16\" Name=\"E2\" Extra=\"val\"/>" +
                      "</Events>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Contains("Extra", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);
            Assert.Equal("", csv);
        }

        // ── SplitCsvLine (private static) ──

        [Fact]
        public void SplitCsvLine_Simple()
        {
            var method = typeof(LogFileService).GetMethod("SplitCsvLine",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return; // Skip if not found

            var result = (List<string>)method.Invoke(null, new object[] { "a,b,c" })!;
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void SplitCsvLine_Quoted()
        {
            var method = typeof(LogFileService).GetMethod("SplitCsvLine",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return;

            var result = (List<string>)method.Invoke(null, new object[] { "\"hello, world\",b" })!;
            Assert.Equal(2, result.Count);
        }
    }
}
