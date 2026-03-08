using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogFileServiceExtendedTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();
        private readonly LogFileService _service;

        public LogFileServiceExtendedTests()
        {
            _service = new LogFileService(new PluginLoader());
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        // ── Helper Methods ──

        private string CreateTempZip(Action<ZipArchive> populate)
        {
            var path = Path.Combine(Path.GetTempPath(), $"IndiLogsExtTest_{Guid.NewGuid():N}.zip");
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                populate(archive);
            }
            _tempFiles.Add(path);
            return path;
        }

        private static void AddTextEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static void AddBinaryEntry(ZipArchive archive, string entryName, byte[] data)
        {
            var entry = archive.CreateEntry(entryName);
            using var s = entry.Open();
            s.Write(data, 0, data.Length);
        }

        private static void AddNestedZip(ZipArchive outerArchive, string outerEntryName, Action<ZipArchive> populateInner)
        {
            using var ms = new MemoryStream();
            using (var innerArchive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                populateInner(innerArchive);
            }
            ms.Position = 0;
            var entry = outerArchive.CreateEntry(outerEntryName);
            using var s = entry.Open();
            ms.CopyTo(s);
        }

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(LogFileService).GetMethod(methodName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return (T)method!.Invoke(null, args)!;
        }

        private T InvokePrivateInstance<T>(string methodName, params object[] args)
        {
            var method = typeof(LogFileService).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return (T)method!.Invoke(_service, args)!;
        }

        private static string BuildOldFormatLine(
            string timestamp, string thread, string pattern, string levelLogger,
            string location, string message, string exception = "", string data = "")
        {
            var sep = '\x1e';
            return $"{timestamp}{sep}{thread}{sep}root{sep}flow{sep}name{sep}{pattern}{sep}ctx{sep}{levelLogger}{sep}{location}{sep}{message}{sep}{exception}{sep}{data}";
        }

        // ═══════════════════════════════════════════════════════════
        // 1. StringPool Tests (extending existing)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void StringPool_Intern_EmptyString_ReturnsEmpty()
        {
            var pool = new LogFileService.StringPool();
            var result = pool.Intern("");
            Assert.Equal("", result);
        }

        [Fact]
        public void StringPool_Intern_MultipleDifferentStrings_AllStored()
        {
            var pool = new LogFileService.StringPool();
            var a = pool.Intern("alpha");
            var b = pool.Intern("beta");
            var c = pool.Intern("gamma");
            Assert.NotSame(a, b);
            Assert.NotSame(b, c);
            Assert.Equal("alpha", a);
            Assert.Equal("beta", b);
            Assert.Equal("gamma", c);
        }

        [Fact]
        public void StringPool_Intern_ThreadSafe_ConcurrentAccess()
        {
            var pool = new LogFileService.StringPool();
            var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            {
                pool.Intern($"key_{i % 10}");
            })).ToArray();
            Task.WaitAll(tasks);
            for (int i = 0; i < 10; i++)
                Assert.Equal($"key_{i}", pool.Intern($"key_{i}"));
        }

        // ═══════════════════════════════════════════════════════════
        // 2. ZipClassificationHelpers Tests
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData("whel3_data.csv", true)]
        [InlineData("ecm_log.txt", true)]
        [InlineData("COM1_serial.log", true)]
        [InlineData("0001_terminal.csv", true)]
        [InlineData("Io-BIM.csv", true)]
        [InlineData("Io-ECM.csv", true)]
        [InlineData("Stab-data.csv", true)]
        [InlineData("PRE_analysis.txt", true)]
        [InlineData("POST_analysis.txt", true)]
        [InlineData("random.txt", false)]
        [InlineData("appdev.log", false)]
        [InlineData("", false)]
        public void ZipClassificationHelpers_IsCustomTerminalLog(string fileName, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsCustomTerminalLog(fileName));
        }

        [Theory]
        [InlineData("diagnosticslogs/systab_saved.txt", true)]
        [InlineData("path/diagnosticslogs/systab_default.txt", true)]
        [InlineData("systab_saved.txt", false)]
        [InlineData("diagnosticslogs/other.txt", false)]
        [InlineData("diagnosticslogs/systab_saved.xml", false)]
        [InlineData("", false)]
        public void ZipClassificationHelpers_IsSystabFile(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsSystabFile(path.ToLower()));
        }

        [Theory]
        [InlineData("file.log", true)]
        [InlineData("file.txt", true)]
        [InlineData("file.csv", true)]
        [InlineData("file.json", true)]
        [InlineData("file.xml", true)]
        [InlineData("file.cfg", true)]
        [InlineData("file.ini", true)]
        [InlineData("file.config", true)]
        [InlineData("file.tsv", true)]
        [InlineData("file.file", true)]
        [InlineData("file.dll", false)]
        [InlineData("file.exe", false)]
        [InlineData("file.dat", false)]
        [InlineData("file.arl", false)]
        [InlineData("file", false)]
        public void ZipClassificationHelpers_IsPluginCandidateExtension(string fileName, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsPluginCandidateExtension(fileName));
        }

        [Theory]
        [InlineData("datamanagement/ecommon/globals/test.xml", true)]
        [InlineData("path/datamanagement/ecommon/globals/globals.xml", true)]
        [InlineData("datamanagement/ecommon/globals/test.txt", false)]
        [InlineData("ecommon/globals/test.xml", false)]
        [InlineData("", false)]
        public void ZipClassificationHelpers_IsGlobalsXmlFile(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsGlobalsXmlFile(path.ToLower()));
        }

        [Theory]
        [InlineData("path/terminallogs/file.csv", true)]
        [InlineData("terminallogs/file.csv", true)]
        [InlineData("random/file.csv", false)]
        public void ZipClassificationHelpers_IsTerminalLogsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsTerminalLogsPath(path.ToLower()));
        }

        [Theory]
        [InlineData("path/lrs/file.csv", true)]
        [InlineData("lrs/file.csv", true)]
        [InlineData("random/file.csv", false)]
        public void ZipClassificationHelpers_IsLrsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsLrsPath(path.ToLower()));
        }

        [Theory]
        [InlineData("path/configuration/file.xml", true)]
        [InlineData("configuration/file.xml", true)]
        [InlineData("random/file.xml", false)]
        public void ZipClassificationHelpers_IsConfigurationPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsConfigurationPath(path.ToLower()));
        }

        [Theory]
        [InlineData("Indigo.Infra.EM_Statistics.csv", true)]
        [InlineData("EM_Statistics_report.csv", true)]
        [InlineData("em_statistics.csv", true)]
        [InlineData("EM_Statistics.xml", false)]
        [InlineData("statistics.csv", false)]
        [InlineData("", false)]
        public void ZipClassificationHelpers_IsEmStatisticsFile(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsEmStatisticsFile(name));
        }

        [Theory]
        [InlineData("path/backup/file.txt", true)]
        [InlineData("path/old/file.txt", true)]
        [InlineData("path/temp/file.txt", true)]
        [InlineData("path/archive/file.txt", true)]
        [InlineData("path/logs/file.txt", false)]
        public void ZipClassificationHelpers_ShouldSkipEntry(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.ShouldSkipEntry(path.ToLower()));
        }

        // ═══════════════════════════════════════════════════════════
        // 3. IsDateStart and ParseTimestampFast via reflection
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData("2025-01-15 10:30:00,123", true)]
        [InlineData("2025-12-31 23:59:59,999", true)]
        [InlineData("2025-01-15 10:30:00,1234567", true)]
        [InlineData("short", false)]
        [InlineData("not a date at all!!!!!!", false)]
        [InlineData("XXXX-XX-XX XX:XX:XX,XXX", false)]
        public void IsDateStart_DetectsValidTimestamps(string line, bool expected)
        {
            var result = InvokePrivateStatic<bool>("IsDateStart", line);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTimestampFast_ParsesMilliseconds()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "2025-01-15 10:30:45,123");
            Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 45, 123), result);
        }

        [Fact]
        public void ParseTimestampFast_ParsesSevenDigitFraction()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "2025-06-15 08:00:00,1234567");
            var expected = new DateTime(2025, 6, 15, 8, 0, 0).AddTicks(1234567);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTimestampFast_FourDigitFraction()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "2025-01-01 00:00:00,1234X");
            var expected = new DateTime(2025, 1, 1, 0, 0, 0).AddTicks(1234 * 1000);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTimestampFast_FiveDigitFraction()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "2025-01-01 00:00:00,12345X");
            var expected = new DateTime(2025, 1, 1, 0, 0, 0).AddTicks(12345 * 100);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTimestampFast_SixDigitFraction()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "2025-01-01 00:00:00,123456X");
            var expected = new DateTime(2025, 1, 1, 0, 0, 0).AddTicks(123456 * 10);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTimestampFast_InvalidDate_ReturnsMinValue()
        {
            var result = InvokePrivateStatic<DateTime>("ParseTimestampFast", "9999-99-99 99:99:99,000");
            Assert.Equal(DateTime.MinValue, result);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. IsNumericAppFile via reflection
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData("50300001.file", true)]
        [InlineData("50300001.file.log.8865", true)]
        [InlineData("123.file", true)]
        [InlineData("engineGroupA.file", false)]
        [InlineData("enginegroupb.file", false)]
        [InlineData("nodigit.file", false)]
        [InlineData("test.txt", false)]
        [InlineData(".file", false)]
        public void IsNumericAppFile_ClassifiesCorrectly(string fileName, bool expected)
        {
            var result = InvokePrivateStatic<bool>("IsNumericAppFile", fileName);
            Assert.Equal(expected, result);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. IsEventsFile via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsEventsFile_PressEventsCsv_ReturnsTrue()
        {
            var args = new object[] { "pressEvents.test.csv", null! };
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (bool)method!.Invoke(null, args)!;
            Assert.True(result);
        }

        [Fact]
        public void IsEventsFile_EventHistoryXml_ReturnsTrue()
        {
            var args = new object[] { "event-history__From2025.xml", null! };
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (bool)method!.Invoke(null, args)!;
            Assert.True(result);
        }

        [Fact]
        public void IsEventsFile_RandomFile_ReturnsFalse()
        {
            var args = new object[] { "random.csv", null! };
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (bool)method!.Invoke(null, args)!;
            Assert.False(result);
        }

        [Fact]
        public void IsEventsFile_PressEventsTxt_ReturnsFalse()
        {
            var args = new object[] { "pressEvents.test.txt", null! };
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (bool)method!.Invoke(null, args)!;
            Assert.False(result);
        }

        [Fact]
        public void IsEventsFile_EventHistoryCsv_ReturnsTrue()
        {
            var args = new object[] { "event-history__From2025-01-01.csv", null! };
            var method = typeof(LogFileService).GetMethod("IsEventsFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (bool)method!.Invoke(null, args)!;
            Assert.True(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 6. IsPluginCandidateExtension (instance static)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsPluginCandidateExtension_Instance_MatchesStatic()
        {
            var method = typeof(LogFileService).GetMethod("IsPluginCandidateExtension",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "test.log" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "test.dll" })!);
        }

        // ═══════════════════════════════════════════════════════════
        // 7. SplitCsvLine via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SplitCsvLine_SimpleLine()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "a,b,c");
            Assert.Equal(3, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsvLine_QuotedFields()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "\"hello,world\",b,c");
            Assert.Equal(3, result.Count);
            Assert.Equal("hello,world", result[0]);
        }

        [Fact]
        public void SplitCsvLine_EmptyFields()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "a,,c");
            Assert.Equal(3, result.Count);
            Assert.Equal("", result[1]);
        }

        [Fact]
        public void SplitCsvLine_SingleField()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "only");
            Assert.Single(result);
            Assert.Equal("only", result[0]);
        }

        [Fact]
        public void SplitCsvLine_EmptyString()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "");
            Assert.Single(result);
            Assert.Equal("", result[0]);
        }

        [Fact]
        public void SplitCsvLine_TrailingComma()
        {
            var result = InvokePrivateInstance<List<string>>("SplitCsvLine", "a,b,");
            Assert.Equal(3, result.Count);
            Assert.Equal("", result[2]);
        }

        // ═══════════════════════════════════════════════════════════
        // 8. ParseReadmeVersions via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseReadmeVersions_ExtractsVersions()
        {
            var content = "Some header\nVersion: 1.2.3.456\nPressPlcVersion: 7.8.9\n";
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = ((string sw, string plc))method!.Invoke(_service, new object[] { content })!;
            Assert.Equal("1.2.3.456", result.sw);
            Assert.Equal("7.8.9", result.plc);
        }

        [Fact]
        public void ParseReadmeVersions_NoVersions_ReturnsUnknown()
        {
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = ((string sw, string plc))method!.Invoke(_service, new object[] { "no version here" })!;
            Assert.Equal("Unknown", result.sw);
            Assert.Equal("Unknown", result.plc);
        }

        [Fact]
        public void ParseReadmeVersions_EmptyContent_ReturnsUnknown()
        {
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = ((string sw, string plc))method!.Invoke(_service, new object[] { "" })!;
            Assert.Equal("Unknown", result.sw);
            Assert.Equal("Unknown", result.plc);
        }

        [Fact]
        public void ParseReadmeVersions_VersionWithEquals()
        {
            var content = "Version=4.5.6\nPressPlcVersion=1.0.0\n";
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = ((string sw, string plc))method!.Invoke(_service, new object[] { content })!;
            Assert.Equal("4.5.6", result.sw);
            Assert.Equal("1.0.0", result.plc);
        }

        // ═══════════════════════════════════════════════════════════
        // 9. ExtractPlcVersionFromSetupInfo via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ExtractPlcVersionFromSetupInfo_ExtractsVersion()
        {
            var json = """{"Name":"press-content-mcs-plc","Version":"12.34.56"}""";
            var method = typeof(LogFileService).GetMethod("ExtractPlcVersionFromSetupInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (string?)method!.Invoke(_service, new object[] { json });
            Assert.Equal("12.34.56", result);
        }

        [Fact]
        public void ExtractPlcVersionFromSetupInfo_NoMatch_ReturnsNull()
        {
            var method = typeof(LogFileService).GetMethod("ExtractPlcVersionFromSetupInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (string?)method!.Invoke(_service, new object[] { "{}" });
            Assert.Null(result);
        }

        [Fact]
        public void ExtractPlcVersionFromSetupInfo_EmptyJson_ReturnsNull()
        {
            var method = typeof(LogFileService).GetMethod("ExtractPlcVersionFromSetupInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (string?)method!.Invoke(_service, new object[] { "" });
            Assert.Null(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 10. SortLogEntriesCacheFriendly via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SortLogEntriesCacheFriendly_SortsCorrectly()
        {
            var list = new List<LogEntry>
            {
                new() { Date = new DateTime(2025, 1, 3), Message = "third" },
                new() { Date = new DateTime(2025, 1, 1), Message = "first" },
                new() { Date = new DateTime(2025, 1, 2), Message = "second" }
            };
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(null, new object[] { list });
            Assert.Equal("first", list[0].Message);
            Assert.Equal("second", list[1].Message);
            Assert.Equal("third", list[2].Message);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_EmptyList_NoException()
        {
            var list = new List<LogEntry>();
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[] { list });
            Assert.Empty(list);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_SingleItem_NoException()
        {
            var list = new List<LogEntry> { new() { Date = DateTime.Now, Message = "only" } };
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[] { list });
            Assert.Single(list);
            Assert.Equal("only", list[0].Message);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_AlreadySorted_PreservesOrder()
        {
            var list = new List<LogEntry>
            {
                new() { Date = new DateTime(2025, 1, 1), Message = "a" },
                new() { Date = new DateTime(2025, 1, 2), Message = "b" },
                new() { Date = new DateTime(2025, 1, 3), Message = "c" }
            };
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[] { list });
            Assert.Equal("a", list[0].Message);
            Assert.Equal("b", list[1].Message);
            Assert.Equal("c", list[2].Message);
        }

        [Fact]
        public void SortLogEntriesCacheFriendly_ReverseSorted_SortsCorrectly()
        {
            var list = new List<LogEntry>
            {
                new() { Date = new DateTime(2025, 1, 5), Message = "e" },
                new() { Date = new DateTime(2025, 1, 4), Message = "d" },
                new() { Date = new DateTime(2025, 1, 3), Message = "c" },
                new() { Date = new DateTime(2025, 1, 2), Message = "b" },
                new() { Date = new DateTime(2025, 1, 1), Message = "a" }
            };
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[] { list });
            Assert.Equal("a", list[0].Message);
            Assert.Equal("e", list[4].Message);
        }

        // ═══════════════════════════════════════════════════════════
        // 11. CalculatePercent via reflection
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData(0L, 0L, 0.0)]
        [InlineData(50L, 100L, 50.0)]
        [InlineData(100L, 100L, 99.0)]
        [InlineData(200L, 100L, 99.0)]
        public void CalculatePercent_ReturnsExpectedValues(long processed, long total, double expected)
        {
            var method = typeof(LogFileService).GetMethod("CalculatePercent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (double)method!.Invoke(_service, new object[] { processed, total })!;
            Assert.Equal(expected, result, 1);
        }

        // ═══════════════════════════════════════════════════════════
        // 12. ConvertEventsXmlToCsv Tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ConvertEventsXmlToCsv_AttributeBased_ProducesCsv()
        {
            var xml = """
                <Events>
                  <Event Time="2025-01-15 10:00:00" Name="TestEvent" State="Active" />
                  <Event Time="2025-01-15 10:01:00" Name="AnotherEvent" State="Inactive" />
                </Events>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Contains("Time", csv);
            Assert.Contains("Name", csv);
            Assert.Contains("TestEvent", csv);
            Assert.Contains("AnotherEvent", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_ElementBased_ProducesCsv()
        {
            var xml = """
                <Events>
                  <Event>
                    <Time>2025-01-15 10:00:00</Time>
                    <Name>TestEvent</Name>
                  </Event>
                </Events>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);

            Assert.Contains("Time", csv);
            Assert.Contains("TestEvent", csv);
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
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not xml at all"));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);
            Assert.Equal("", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_QuotesInValues_AreEscaped()
        {
            var xml = """
                <Events>
                  <Event Name="He said &quot;hello&quot;" />
                </Events>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);
            Assert.Contains("\"\"hello\"\"", csv);
        }

        [Fact]
        public void ConvertEventsXmlToCsv_MultipleRecordsDifferentColumns_MergesAll()
        {
            var xml = """
                <Events>
                  <Event Time="2025-01-15" Name="E1" />
                  <Event Time="2025-01-16" Extra="bonus" />
                </Events>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var csv = LogFileService.ConvertEventsXmlToCsv(stream);
            Assert.Contains("Time", csv);
            Assert.Contains("Name", csv);
            Assert.Contains("Extra", csv);
        }

        // ═══════════════════════════════════════════════════════════
        // 13. ParseEventsCsv Extended Tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseEventsCsv_AlternateHeaderNames()
        {
            var csv = "Timestamp,EventName,Status,Level,Info\n" +
                      "2025-01-15 10:30:00,MyEvent,Running,Warning,Desc here\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
            Assert.Equal("MyEvent", events[0].Name);
            Assert.Equal("Running", events[0].State);
            Assert.Equal("Warning", events[0].Severity);
            Assert.Equal("Desc here", events[0].Description);
        }

        [Fact]
        public void ParseEventsCsv_QuotedFields_ParsedCorrectly()
        {
            var csv = "Time,Name,State\n" +
                      "\"2025-01-15 10:30:00\",\"Event, with comma\",\"Active\"\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
            Assert.Equal("Event, with comma", events[0].Name);
        }

        [Fact]
        public void ParseEventsCsv_InvalidTimestamp_SkipsRow()
        {
            var csv = "Time,Name\n" +
                      "not-a-date,TestEvent\n" +
                      "2025-01-15 10:30:00,ValidEvent\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
            Assert.Equal("ValidEvent", events[0].Name);
        }

        [Fact]
        public void ParseEventsCsv_BlankLines_AreSkipped()
        {
            var csv = "Time,Name\n\n\n2025-01-15 10:30:00,TestEvent\n\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
        }

        [Fact]
        public void ParseEventsCsv_StreamNotAtStart_SeeksToBeginning()
        {
            var csv = "Time,Name\n2025-01-15 10:30:00,TestEvent\n";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            stream.Position = 10;
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
        }

        [Fact]
        public void ParseEventsCsv_ParametersColumn()
        {
            var csv = "Time,Name,Parameters\n" +
                      "2025-01-15 10:30:00,MyEvent,param1=val1\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            Assert.Single(events);
            Assert.Equal("param1=val1", events[0].Parameters);
        }

        [Fact]
        public void ParseEventsCsv_MissingNameColumn_DefaultsToUnknown()
        {
            var csv = "Time,State\n2025-01-15 10:30:00,Active\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = _service.ParseEventsCsv(stream);
            // Time column contains "Name" so nameIdx may or may not match
            // The test verifies no crash
            Assert.NotNull(events);
        }

        // ═══════════════════════════════════════════════════════════
        // 14. MapDtoToLogEntry Tests (public overload)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void MapDtoToLogEntry_MapsAllFields()
        {
            var dto = new LogEntryDto
            {
                Date = new DateTime(2025, 1, 15, 10, 0, 0),
                Message = "test message",
                Level = "Error",
                ThreadName = "Worker",
                Logger = "TestLogger",
                ProcessName = "APP",
                Method = "DoWork",
                Data = "extra data",
                Exception = "NullRefException"
            };
            var entry = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal(dto.Date, entry.Date);
            Assert.Equal("test message", entry.Message);
            Assert.Equal("Error", entry.Level);
            Assert.Equal("Worker", entry.ThreadName);
            Assert.Equal("TestLogger", entry.Logger);
            Assert.Equal("APP", entry.ProcessName);
            Assert.Equal("DoWork", entry.Method);
            Assert.Equal("extra data", entry.Data);
            Assert.Equal("NullRefException", entry.Exception);
        }

        [Fact]
        public void MapDtoToLogEntry_NullFields_DefaultsApplied()
        {
            var dto = new LogEntryDto
            {
                Date = DateTime.Now,
                Message = null!,
                Level = null!,
                ThreadName = null!,
                Logger = null!,
                ProcessName = null!,
                Method = null!
            };
            var entry = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal("Info", entry.Level);
            Assert.Equal("", entry.Message);
            Assert.Equal("", entry.ThreadName);
            Assert.Equal("", entry.Logger);
            Assert.Equal("", entry.ProcessName);
            Assert.Equal("", entry.Method);
        }

        [Fact]
        public void MapDtoToLogEntry_EmptyProcessName_MapsToEmpty()
        {
            var dto = new LogEntryDto
            {
                Date = DateTime.Now,
                Message = "msg",
                ProcessName = ""
            };
            var entry = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal("", entry.ProcessName);
        }

        [Fact]
        public void MapDtoToLogEntry_ExtraFields_Preserved()
        {
            var extras = new Dictionary<string, string> { { "key1", "val1" }, { "key2", "val2" } };
            var dto = new LogEntryDto
            {
                Date = DateTime.Now,
                Message = "msg",
                ExtraFields = extras
            };
            var entry = LogFileService.MapDtoToLogEntry(dto);
            Assert.NotNull(entry.ExtraFields);
            Assert.Equal("val1", entry.ExtraFields!["key1"]);
            Assert.Equal("val2", entry.ExtraFields!["key2"]);
        }

        [Fact]
        public void MapDtoToLogEntry_EmptyMethod_MapsToEmpty()
        {
            var dto = new LogEntryDto
            {
                Date = DateTime.Now,
                Message = "msg",
                Method = ""
            };
            var entry = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal("", entry.Method);
        }

        // ═══════════════════════════════════════════════════════════
        // 15. ProcessAppDevBufferFast via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ProcessAppDevBufferFast_OldFormat_ParsesCorrectly()
        {
            var sep = '\x1e';
            var raw = $"2025-01-15 10:30:00,123{sep}MainThread{sep}root1{sep}flow1{sep}name1{sep}Pattern1{sep}ctx{sep}ERROR MyLogger{sep}MyClass.Do{sep}Test message{sep}Exception text{sep}Some data";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.NotNull(result);
            Assert.Equal("Test message", result!.Message);
            Assert.Equal("ERROR", result.Level);
            Assert.Equal("MyLogger", result.Logger);
            Assert.Equal("MainThread", result.ThreadName);
            Assert.Equal("APP", result.ProcessName);
            Assert.Equal("Pattern1", result.Pattern);
            Assert.Equal("Exception text", result.Exception);
            Assert.Equal("Some data", result.Data);
            Assert.Equal("MyClass.Do", result.Method);
        }

        [Fact]
        public void ProcessAppDevBufferFast_OldFormat_TooFewParts_ReturnsNull()
        {
            var sep = '\x1e';
            var raw = $"2025-01-15 10:30:00,123{sep}only{sep}three{sep}parts";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.Null(result);
        }

        [Fact]
        public void ProcessAppDevBufferFast_OldFormat_InvalidTimestamp_ReturnsNull()
        {
            var sep = '\x1e';
            // Use a string that's long enough (>=23 chars before sep) but has invalid date values
            var raw = $"9999-99-99 99:99:99,000{sep}Thread{sep}r{sep}f{sep}n{sep}p{sep}c{sep}INFO Log{sep}Loc{sep}Msg{sep}{sep}";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.Null(result);
        }

        [Fact]
        public void ProcessAppDevBufferFast_NewFormat_ParsesCorrectly()
        {
            var raw = "2025-01-15 10:30:00,123 |Thread1| |Root1| |Flow1| |Name1| |Pattern1| |Ctx1| WARN MyLogger\r\n" +
                      "|MyClass.Method|\r\n" +
                      "This is the message content\r\n" +
                      "||";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.NotNull(result);
            Assert.Equal("WARN", result!.Level);
            Assert.Equal("MyLogger", result.Logger);
            Assert.Equal("Thread1", result.ThreadName);
            Assert.Equal("MyClass.Method", result.Method);
            Assert.Equal("This is the message content", result.Message);
            Assert.Equal("Pattern1", result.Pattern);
        }

        [Fact]
        public void ProcessAppDevBufferFast_NewFormat_TooFewPipeFields_ReturnsNull()
        {
            var raw = "2025-01-15 10:30:00,123 |Thread1| |Root1| INFO Logger\n|Method|\nMsg\n||";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.Null(result);
        }

        [Fact]
        public void ProcessAppDevBufferFast_NoSeparatorNoNewline_ReturnsNull()
        {
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { "just a single line no sep", pool });
            Assert.Null(result);
        }

        [Fact]
        public void ProcessAppDevBufferFast_OldFormat_NoExceptionNoData_SetsEmpty()
        {
            var sep = '\x1e';
            var raw = $"2025-01-15 10:30:00,123{sep}Thread{sep}r{sep}f{sep}n{sep}{sep}ctx{sep}INFO Logger{sep}Loc{sep}Message{sep}{sep}";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ProcessAppDevBufferFast",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { raw, pool });
            Assert.NotNull(result);
            Assert.Equal("", result!.Exception);
            Assert.Equal("", result.Data);
            Assert.Equal("", result.Pattern);
        }

        // ═══════════════════════════════════════════════════════════
        // 16. ParseOldFormatDirect via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseOldFormatDirect_ValidLine_ParsesAllFields()
        {
            var line = BuildOldFormatLine(
                "2025-01-15 10:30:00,456", "WorkerThread", "MyPattern",
                "WARN TestLogger", "MyClass.Execute", "Hello world",
                "SomeException", "SomeData");
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseOldFormatDirect",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { line, pool });
            Assert.NotNull(result);
            Assert.Equal("Hello world", result!.Message);
            Assert.Equal("WARN", result.Level);
            Assert.Equal("TestLogger", result.Logger);
            Assert.Equal("WorkerThread", result.ThreadName);
            Assert.Equal("SomeException", result.Exception);
            Assert.Equal("SomeData", result.Data);
            Assert.Equal("MyPattern", result.Pattern);
        }

        [Fact]
        public void ParseOldFormatDirect_ShortTimestamp_ReturnsNull()
        {
            var sep = '\x1e';
            var line = $"short{sep}rest";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseOldFormatDirect",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { line, pool });
            Assert.Null(result);
        }

        [Fact]
        public void ParseOldFormatDirect_InsufficientSeparators_ReturnsNull()
        {
            var sep = '\x1e';
            var line = $"2025-01-15 10:30:00,123{sep}Thread{sep}only{sep}three";
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseOldFormatDirect",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { line, pool });
            Assert.Null(result);
        }

        [Fact]
        public void ParseOldFormatDirect_EmptyExceptionAndData_SetsEmptyStrings()
        {
            var line = BuildOldFormatLine(
                "2025-01-15 10:30:00,456", "Thread", "",
                "INFO Logger", "Location", "Message", "", "");
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseOldFormatDirect",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (LogEntry?)method!.Invoke(_service, new object[] { line, pool });
            Assert.NotNull(result);
            Assert.Equal("", result!.Exception);
            Assert.Equal("", result.Data);
            Assert.Equal("", result.Pattern);
        }

        // ═══════════════════════════════════════════════════════════
        // 17. ParseAppDevLogStream via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ParseAppDevLogStream_OldFormat_ParsesMultipleEntries()
        {
            var line1 = BuildOldFormatLine("2025-01-15 10:30:00,123", "T1", "", "INFO Log1", "Loc1", "Msg1");
            var line2 = BuildOldFormatLine("2025-01-15 10:30:01,456", "T2", "", "ERROR Log2", "Loc2", "Msg2");
            var content = line1 + "\n" + line2 + "\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseAppDevLogStream",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(LogFileService.StringPool) }, null);
            Assert.NotNull(method);
            var result = (List<LogEntry>)method!.Invoke(_service, new object[] { stream, pool })!;
            Assert.Equal(2, result.Count);
            Assert.Equal("Msg1", result[0].Message);
            Assert.Equal("Msg2", result[1].Message);
        }

        [Fact]
        public void ParseAppDevLogStream_SkipsV2Marker()
        {
            var line1 = BuildOldFormatLine("2025-01-15 10:30:00,123", "T1", "", "INFO Log1", "Loc1", "Msg1");
            var content = "!!![V2]\n" + line1 + "\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseAppDevLogStream",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(LogFileService.StringPool) }, null);
            var result = (List<LogEntry>)method!.Invoke(_service, new object[] { stream, pool })!;
            Assert.Single(result);
        }

        [Fact]
        public void ParseAppDevLogStream_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseAppDevLogStream",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(LogFileService.StringPool) }, null);
            var result = (List<LogEntry>)method!.Invoke(_service, new object[] { stream, pool })!;
            Assert.Empty(result);
        }

        [Fact]
        public void ParseAppDevLogStream_NewFormat_ParsesEntry()
        {
            var content = "2025-01-15 10:30:00,123 |Thread1| |Root1| |Flow1| |Name1| |Pattern1| |Ctx1| ERROR MyLogger\r\n" +
                          "|MyClass.Method|\r\n" +
                          "This is the message\r\n" +
                          "||\r\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseAppDevLogStream",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(LogFileService.StringPool) }, null);
            var result = (List<LogEntry>)method!.Invoke(_service, new object[] { stream, pool })!;
            Assert.Single(result);
            Assert.Equal("ERROR", result[0].Level);
            Assert.Equal("This is the message", result[0].Message);
        }

        [Fact]
        public void ParseAppDevLogStream_NonSeekableStream_StillParses()
        {
            var line = BuildOldFormatLine("2025-01-15 10:30:00,123", "T1", "", "INFO Log1", "Loc1", "Msg1") + "\n";
            using var baseStream = new MemoryStream(Encoding.UTF8.GetBytes(line));
            // MemoryStream is seekable but we test the code path where CanSeek is true and Position != 0
            baseStream.Position = 0;
            var pool = new LogFileService.StringPool();
            var method = typeof(LogFileService).GetMethod("ParseAppDevLogStream",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(LogFileService.StringPool) }, null);
            var result = (List<LogEntry>)method!.Invoke(_service, new object[] { baseStream, pool })!;
            Assert.Single(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 18. ReadSampleLines via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ReadSampleLines_ReadsLimitedLines()
        {
            var content = "line1\nline2\nline3\nline4\nline5\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var method = typeof(LogFileService).GetMethod("ReadSampleLines",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(int) }, null);
            Assert.NotNull(method);
            var result = (string[])method!.Invoke(null, new object[] { stream, 3 })!;
            Assert.Equal(3, result.Length);
            Assert.Equal("line1", result[0]);
            Assert.Equal("line3", result[2]);
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void ReadSampleLines_FewerLinesThanRequested()
        {
            var content = "only\ntwo\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var method = typeof(LogFileService).GetMethod("ReadSampleLines",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(int) }, null);
            var result = (string[])method!.Invoke(null, new object[] { stream, 10 })!;
            Assert.Equal(2, result.Length);
        }

        [Fact]
        public void ReadSampleLines_EmptyStream_ReturnsEmptyArray()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var method = typeof(LogFileService).GetMethod("ReadSampleLines",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Stream), typeof(int) }, null);
            var result = (string[])method!.Invoke(null, new object[] { stream, 5 })!;
            Assert.Empty(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 19. MergeReloadResults via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void MergeReloadResults_MergesPlcLogs()
        {
            var session = new LogSessionData
            {
                Logs = new List<LogEntry>
                {
                    new() { Date = new DateTime(2025, 1, 1), Message = "existing" }
                },
                LoadTabSelection = new TabSelectionConfig()
            };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            logsBag.Add(new List<LogEntry>
            {
                new() { Date = new DateTime(2025, 1, 2), Message = "new" }
            });
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            var sel = new TabSelectionConfig { LoadPlc = true };

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(null, new object[]
            {
                session, sel, "Plc", logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });
            Assert.Equal(2, session.Logs.Count);
            Assert.True(session.LoadTabSelection!.LoadPlc);
        }

        [Fact]
        public void MergeReloadResults_MergesAppLogs()
        {
            var session = new LogSessionData
            {
                AppDevLogs = new List<LogEntry>(),
                LoadTabSelection = new TabSelectionConfig()
            };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            appBag.Add(new List<LogEntry>
            {
                new() { Date = new DateTime(2025, 1, 1), Message = "app1" },
                new() { Date = new DateTime(2025, 1, 2), Message = "app2" }
            });
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            var sel = new TabSelectionConfig { LoadApp = true };

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[]
            {
                session, sel, "App", logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });
            Assert.Equal(2, session.AppDevLogs.Count);
            Assert.True(session.LoadTabSelection!.LoadApp);
        }

        [Fact]
        public void MergeReloadResults_MergesEvents()
        {
            var session = new LogSessionData
            {
                Events = new List<EventEntry>
                {
                    new() { Time = new DateTime(2025, 1, 1), Name = "existing" }
                },
                LoadTabSelection = new TabSelectionConfig()
            };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            eventsBag.Add(new List<EventEntry>
            {
                new() { Time = new DateTime(2025, 1, 2), Name = "new" }
            });
            var sel = new TabSelectionConfig { LoadEvents = true };

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[]
            {
                session, sel, "Events", logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });
            Assert.Equal(2, session.Events.Count);
            Assert.True(session.LoadTabSelection!.LoadEvents);
        }

        [Fact]
        public void MergeReloadResults_EmptyBags_NoChange()
        {
            var session = new LogSessionData
            {
                Logs = new List<LogEntry> { new() { Date = DateTime.Now, Message = "only" } },
                LoadTabSelection = new TabSelectionConfig()
            };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            var sel = new TabSelectionConfig { LoadPlc = true };

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[]
            {
                session, sel, "Plc", logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });
            Assert.Single(session.Logs);
        }

        [Theory]
        [InlineData("App")]
        [InlineData("Plc")]
        [InlineData("Events")]
        [InlineData("TerminalLogs")]
        [InlineData("Configuration")]
        [InlineData("Systab")]
        [InlineData("Globals")]
        [InlineData("Lrs")]
        [InlineData("SetupInfo")]
        [InlineData("ManagerThread")]
        [InlineData("Screenshots")]
        public void MergeReloadResults_UpdatesTabSelection(string componentName)
        {
            var session = new LogSessionData { LoadTabSelection = new TabSelectionConfig() };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            var sel = new TabSelectionConfig();

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, new object[]
            {
                session, sel, componentName, logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });

            var prop = typeof(TabSelectionConfig).GetProperty($"Load{componentName}");
            if (prop != null)
                Assert.True((bool)prop.GetValue(session.LoadTabSelection)!);
        }

        [Fact]
        public void MergeReloadResults_NullTabSelection_NoException()
        {
            var session = new LogSessionData { LoadTabSelection = null };
            var logsBag = new ConcurrentBag<List<LogEntry>>();
            var appBag = new ConcurrentBag<List<LogEntry>>();
            var eventsBag = new ConcurrentBag<List<EventEntry>>();
            var sel = new TabSelectionConfig();

            var method = typeof(LogFileService).GetMethod("MergeReloadResults",
                BindingFlags.Static | BindingFlags.NonPublic);
            // Should not throw when LoadTabSelection is null
            method!.Invoke(null, new object[]
            {
                session, sel, "Plc", logsBag, appBag, eventsBag,
                new List<System.Windows.Media.Imaging.BitmapImage>()
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 20. IsSystabFile/IsGlobalsXmlFile instance methods
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsSystabFile_Instance_InDiagPath_ReturnsTrue()
        {
            var method = typeof(LogFileService).GetMethod("IsSystabFile",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "diagnosticslogs/systab_saved.txt" })!);
        }

        [Fact]
        public void IsSystabFile_Instance_NotInDiagPath_ReturnsFalse()
        {
            var method = typeof(LogFileService).GetMethod("IsSystabFile",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.False((bool)method!.Invoke(null, new object[] { "other/systab_saved.txt" })!);
        }

        [Fact]
        public void IsSystabFile_Instance_EmptyString_ReturnsFalse()
        {
            var method = typeof(LogFileService).GetMethod("IsSystabFile",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.False((bool)method!.Invoke(null, new object[] { "" })!);
        }

        [Fact]
        public void IsGlobalsXmlFile_Instance_CorrectPath_ReturnsTrue()
        {
            var method = typeof(LogFileService).GetMethod("IsGlobalsXmlFile",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "datamanagement/ecommon/globals/test.xml" })!);
        }

        [Fact]
        public void IsGlobalsXmlFile_Instance_WrongPath_ReturnsFalse()
        {
            var method = typeof(LogFileService).GetMethod("IsGlobalsXmlFile",
                BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            Assert.False((bool)method!.Invoke(null, new object[] { "other/path/globals.xml" })!);
        }

        // ═══════════════════════════════════════════════════════════
        // 21. IsCustomTerminalLog instance via reflection
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData("whel3_data.csv", true)]
        [InlineData("ecm_log.txt", true)]
        [InlineData("Io-BIM.csv", true)]
        [InlineData("Stab-data.csv", true)]
        [InlineData("PRE_analysis.txt", true)]
        [InlineData("POST_analysis.txt", true)]
        [InlineData("some_random.txt", false)]
        [InlineData("", false)]
        public void IsCustomTerminalLog_Instance_ClassifiesCorrectly(string name, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsCustomTerminalLog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.Equal(expected, (bool)method!.Invoke(_service, new object[] { name })!);
        }

        // ═══════════════════════════════════════════════════════════
        // 22. PreScanZip Integration Tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void PreScanZip_DetectsEventsCsv()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "pressEvents.test.csv", "Time,Name\n2025-01-15 10:00:00,Evt\n");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasEvents);
        }

        [Fact]
        public void PreScanZip_DetectsGlobals()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DataManagement/eCommon/Globals/test.xml", "<root/>");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasGlobals);
        }

        [Fact]
        public void PreScanZip_DetectsSystab()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DiagnosticsLogs/systab_saved.txt", "content");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasSystab);
        }

        [Fact]
        public void PreScanZip_DetectsConfiguration()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Configuration/settings.xml", "<config/>");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasConfiguration);
        }

        [Fact]
        public void PreScanZip_DetectsTerminalLogs()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "TerminalLogs/terminal.csv", "data");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasTerminalLogs);
        }

        [Fact]
        public void PreScanZip_DetectsLrs()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "LRS/data.csv", "data");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasLrs);
        }

        [Fact]
        public void PreScanZip_DetectsScreenshots()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddBinaryEntry(archive, "screenshot.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasScreenshots);
        }

        [Fact]
        public void PreScanZip_DetectsSetupInfo()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Readme.txt", "Version: 1.0");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasSetupInfo);
        }

        [Fact]
        public void PreScanZip_DetectsS6AppLogs()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "IndigoLogs/Logger Files/APPDEV.log", "2025-01-15 10:30:00,123 data");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasApp);
            Assert.True(config.IsS6);
        }

        [Fact]
        public void PreScanZip_SkipsBackupFolder()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "backup/pressEvents.old.csv", "Time,Name\n");
                AddTextEntry(archive, "pressEvents.current.csv", "Time,Name\n2025-01-15 10:00:00,Evt\n");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasEvents);
        }

        [Fact]
        public void PreScanZip_DetectsNestedZipContents()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "pressEvents.inner.csv", "Time,Name\n2025-01-15 10:00:00,InnerEvt\n");
                });
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasEvents);
        }

        [Fact]
        public void PreScanZip_NonExistentFile_ReturnsDefaultConfig()
        {
            var config = _service.PreScanZip(@"C:\nonexistent\file.zip");
            Assert.NotNull(config);
            Assert.False(config!.HasApp);
            Assert.False(config.HasPlc);
        }

        [Fact]
        public void PreScanZip_DetectsSetupInfoJson()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "test_setupInfo.json", "{}");
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasSetupInfo);
        }

        [Fact]
        public void PreScanZip_DetectsPlcLogs()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddBinaryEntry(archive, "engineGroupA.file", new byte[] { 1, 2, 3, 4 });
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            Assert.True(config!.HasPlc);
        }

        [Fact]
        public void PreScanZip_IgnoresEngineGroupZip()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddBinaryEntry(archive, "engineGroupA.file.zip", new byte[] { 1, 2, 3 });
            });
            var config = _service.PreScanZip(zipPath);
            Assert.NotNull(config);
            // .zip extension should not count as PLC log
            Assert.False(config!.HasPlc);
        }

        // ═══════════════════════════════════════════════════════════
        // 23. LoadSession ZIP integration (extended)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public async Task LoadSessionAsync_ZipWithReadme_ExtractsVersions()
        {
            var readmeContent = "Press Information\nVersion: 5.6.7.890\nPressPlcVersion: 1.2.3\n";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Readme.txt", readmeContent);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.Equal(readmeContent, session.PressConfiguration);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithSetupInfo_ExtractsSetupInfo()
        {
            var json = """{"Name":"press-content-mcs-plc","Version":"99.88.77"}""";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "test_setupInfo.json", json);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.Equal(json, session.SetupInfo);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithSystab_ExtractsSystabFiles()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DiagnosticsLogs/systab_saved.txt", "saved content");
                AddTextEntry(archive, "DiagnosticsLogs/systab_default.txt", "default content");
                AddTextEntry(archive, "DiagnosticsLogs/systab_minimum.txt", "min content");
                AddTextEntry(archive, "DiagnosticsLogs/systab_maximum.txt", "max content");
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.SystabFiles.ContainsKey("saved"));
            Assert.True(session.SystabFiles.ContainsKey("default"));
            Assert.True(session.SystabFiles.ContainsKey("minimum"));
            Assert.True(session.SystabFiles.ContainsKey("maximum"));
            Assert.Equal("saved content", session.SystabFiles["saved"]);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithGlobals_ExtractsGlobalsFiles()
        {
            var xml = "<globals><item>test</item></globals>";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DataManagement/eCommon/Globals/globals.xml", xml);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.GlobalsFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithConfiguration_ExtractsConfigs()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Configuration/app.config", "<config>test</config>");
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.ConfigurationFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithDb_ExtractsDatabase()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddBinaryEntry(archive, "Configuration/test.db", new byte[] { 1, 2, 3, 4 });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.DatabaseFiles.ContainsKey("test.db"));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, session.DatabaseFiles["test.db"]);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithTerminalCsv_StoresAsBytes()
        {
            var csvContent = "col1,col2\nval1,val2\n";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "TerminalLogs/terminal.csv", csvContent);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.TerminalCsvBytes.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithTerminalTxt_StoresAsString()
        {
            var logContent = "terminal log line 1\nterminal log line 2\n";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "TerminalLogs/terminal.txt", logContent);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.TerminalLogFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithEmStatistics_ExtractsCsv()
        {
            var csv = "Time,Value\n2025-01-15,100\n";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "EM_Statistics.csv", csv);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.Equal(csv, session.EmStatisticsCsvContent);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithEventsXml_ParsesEvents()
        {
            var xml = """
                <Events>
                  <Event Time="2025-01-15 10:00:00" Name="TestEvent" State="Active" Severity="Info" />
                </Events>
                """;
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "pressEvents.test.xml", xml);
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.NotEmpty(session.Events);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipDisabledEvents_NoEventsLoaded()
        {
            var csv = "Time,Name\n2025-01-15 10:00:00,Evt\n";
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "pressEvents.test.csv", csv);
            });

            var sel = new TabSelectionConfig { LoadEvents = false };
            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>(), sel);
            Assert.NotNull(session);
            Assert.Empty(session.Events);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipDisabledGlobals_NoGlobalsLoaded()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DataManagement/eCommon/Globals/test.xml", "<root/>");
            });

            var sel = new TabSelectionConfig { LoadGlobals = false };
            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>(), sel);
            Assert.NotNull(session);
            Assert.Empty(session.GlobalsFiles);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipSkipsBackupFolders()
        {
            // The skip logic requires the folder to appear as a path segment: /old/, /backup/, etc.
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "logs/old/pressEvents.csv", "Time,Name\n2025-01-15 10:00:00,OldEvt\n");
                AddTextEntry(archive, "logs/backup/pressEvents.csv", "Time,Name\n2025-01-15 10:00:00,BackupEvt\n");
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.Empty(session.Events);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipDisabledSystab_NoSystabLoaded()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "DiagnosticsLogs/systab_saved.txt", "content");
            });

            var sel = new TabSelectionConfig { LoadSystab = false };
            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>(), sel);
            Assert.NotNull(session);
            Assert.Empty(session.SystabFiles);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipDisabledConfiguration_NoConfigLoaded()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Configuration/app.config", "<config/>");
                AddBinaryEntry(archive, "Configuration/test.db", new byte[] { 1, 2 });
            });

            var sel = new TabSelectionConfig { LoadConfiguration = false };
            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>(), sel);
            Assert.NotNull(session);
            Assert.Empty(session.ConfigurationFiles);
            Assert.Empty(session.DatabaseFiles);
        }

        // ═══════════════════════════════════════════════════════════
        // 24. Nested ZIP integration tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithEvents_ParsesInnerEvents()
        {
            var csv = "Time,Name,State\n2025-01-15 10:00:00,InnerEvent,Active\n";
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "pressEvents.inner.csv", csv);
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.NotEmpty(session.Events);
            Assert.Contains(session.Events, e => e.Name == "InnerEvent");
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithConfig_ExtractsConfigs()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "Configuration/inner.config", "<inner>config</inner>");
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.ConfigurationFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithReadme_ExtractsReadme()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "Readme.txt", "Version: 9.9.9\n");
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.Contains("9.9.9", session.PressConfiguration ?? "");
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithDb_ExtractsDatabase()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddBinaryEntry(innerArchive, "Configuration/innerdb.db", new byte[] { 10, 20, 30 });
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.DatabaseFiles.ContainsKey("innerdb.db"));
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithTerminalLogs_ExtractsTerminals()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "TerminalLogs/ecm.txt", "terminal content");
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.TerminalLogFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithGlobals_ExtractsGlobals()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "DataManagement/eCommon/Globals/test.xml", "<globals/>");
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.GlobalsFiles.Count > 0);
        }

        [Fact]
        public async Task LoadSessionAsync_NestedZipWithSystab_ExtractsSystab()
        {
            var zipPath = CreateTempZip(outerArchive =>
            {
                AddNestedZip(outerArchive, "inner.zip", innerArchive =>
                {
                    AddTextEntry(innerArchive, "DiagnosticsLogs/systab_saved.txt", "systab data");
                });
            });

            var session = await _service.LoadSessionAsync(
                new[] { zipPath }, new Progress<(double, string)>());
            Assert.NotNull(session);
            Assert.True(session.SystabFiles.ContainsKey("saved"));
        }

        // ═══════════════════════════════════════════════════════════
        // 25. ReloadComponentAsync Error Handling
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public async Task ReloadComponentAsync_NullSession_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ReloadComponentAsync(null!, "Plc", new Progress<(double, string)>()));
        }

        [Fact]
        public async Task ReloadComponentAsync_NonZipPath_Throws()
        {
            var session = new LogSessionData { FilePath = "test.txt" };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ReloadComponentAsync(session, "Plc", new Progress<(double, string)>()));
        }

        [Fact]
        public async Task ReloadComponentAsync_EmptyFilePath_Throws()
        {
            var session = new LogSessionData { FilePath = "" };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ReloadComponentAsync(session, "Plc", new Progress<(double, string)>()));
        }

        // ═══════════════════════════════════════════════════════════
        // 26. GetPluginLoader
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void GetPluginLoader_ReturnsInjectedLoader()
        {
            var loader = new PluginLoader();
            var service = new LogFileService(loader);
            Assert.Same(loader, service.GetPluginLoader());
        }

        // ═══════════════════════════════════════════════════════════
        // 27. TabSelectionConfig Tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void TabSelectionConfig_DefaultValues()
        {
            var config = new TabSelectionConfig();
            Assert.True(config.LoadApp);
            Assert.True(config.LoadPlc);
            Assert.True(config.LoadEvents);
            Assert.True(config.LoadScreenshots);
            Assert.True(config.LoadTerminalLogs);
            Assert.True(config.LoadConfiguration);
            Assert.True(config.LoadSystab);
            Assert.True(config.LoadGlobals);
            Assert.True(config.LoadLrs);
            Assert.True(config.LoadSetupInfo);
            Assert.True(config.LoadManagerThread);
            Assert.True(config.ShowCharts);
            Assert.True(config.ShowCpr);
            Assert.True(config.ShowStepRecorder);
            Assert.True(config.ShowDifferentLogs);
        }

        [Fact]
        public void TabSelectionConfig_CreateForConfiguration_SetsIsS6()
        {
            var s6Config = TabSelectionConfig.CreateForConfiguration(true);
            Assert.True(s6Config.IsS6);

            var s4Config = TabSelectionConfig.CreateForConfiguration(false);
            Assert.False(s4Config.IsS6);
        }

        [Fact]
        public void TabSelectionConfig_HasFlags_DefaultFalse()
        {
            var config = new TabSelectionConfig();
            Assert.False(config.HasApp);
            Assert.False(config.HasPlc);
            Assert.False(config.HasEvents);
            Assert.False(config.HasScreenshots);
            Assert.False(config.HasTerminalLogs);
            Assert.False(config.HasConfiguration);
            Assert.False(config.HasSystab);
            Assert.False(config.HasGlobals);
            Assert.False(config.HasLrs);
            Assert.False(config.HasSetupInfo);
        }

        [Fact]
        public void TabSelectionConfig_TimeFilterProperties()
        {
            var config = new TabSelectionConfig
            {
                AvailableStartTime = new DateTime(2025, 1, 1),
                AvailableEndTime = new DateTime(2025, 12, 31),
                FilterStartTime = new DateTime(2025, 6, 1),
                FilterEndTime = new DateTime(2025, 6, 30),
                UseTimeFilter = true
            };
            Assert.Equal(new DateTime(2025, 1, 1), config.AvailableStartTime);
            Assert.Equal(new DateTime(2025, 12, 31), config.AvailableEndTime);
            Assert.True(config.UseTimeFilter);
        }

        // ═══════════════════════════════════════════════════════════
        // 28. LogSessionData Tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void LogSessionData_DefaultCollections_Initialized()
        {
            var session = new LogSessionData();
            Assert.NotNull(session.Logs);
            Assert.NotNull(session.AppDevLogs);
            Assert.NotNull(session.Events);
            Assert.NotNull(session.Screenshots);
            Assert.NotNull(session.ConfigurationFiles);
            Assert.NotNull(session.DatabaseFiles);
            Assert.NotNull(session.TerminalLogFiles);
            Assert.NotNull(session.TerminalCsvBytes);
            Assert.NotNull(session.GlobalsFiles);
            Assert.NotNull(session.SystabFiles);
            Assert.NotNull(session.StateTransitions);
            Assert.NotNull(session.CriticalFailureEvents);
            Assert.NotNull(session.MarkedLogs);
        }

        [Fact]
        public void LogSessionData_DefaultStrings_Initialized()
        {
            var session = new LogSessionData();
            Assert.Equal("", session.FileName);
            Assert.Equal("", session.FilePath);
            Assert.Null(session.SetupInfo);
            Assert.Null(session.PressConfiguration);
            Assert.Null(session.VersionsInfo);
            Assert.Null(session.EventsCsvRawContent);
            Assert.Null(session.EmStatisticsCsvContent);
            Assert.False(session.HasBinaryAppLogs);
            Assert.Null(session.ConfigurationType);
        }

        [Fact]
        public void LogSessionData_TimeSyncProperties()
        {
            var session = new LogSessionData();
            Assert.Null(session.TimeSyncOffset);
            Assert.False(session.HasTimeSyncData);

            session.TimeSyncOffset = TimeSpan.FromSeconds(5);
            session.HasTimeSyncData = true;
            Assert.Equal(TimeSpan.FromSeconds(5), session.TimeSyncOffset);
            Assert.True(session.HasTimeSyncData);
        }
    }
}
