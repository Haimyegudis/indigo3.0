using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace IndiLogs.Tests
{
    public class SearchReportServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public SearchReportServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTests_Report_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private string GetTempPath() => Path.Combine(_tempDir, "report.html");

        [Fact]
        public void GenerateHtmlReport_EmptyResults_CreatesFile()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams { QueryText = "error" };

            SearchReportService.GenerateHtmlReport(path, searchParams, new List<GrepResult>());

            Assert.True(File.Exists(path));
            var html = File.ReadAllText(path);
            Assert.Contains("IndiLogs Search Report", html);
        }

        [Fact]
        public void GenerateHtmlReport_ContainsQueryText()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams { QueryText = "MySearchQuery" };

            SearchReportService.GenerateHtmlReport(path, searchParams, new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.Contains("MySearchQuery", html);
        }

        [Fact]
        public void GenerateHtmlReport_WithResults_IncludesDetails()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams
            {
                QueryText = "error",
                LocationNames = new List<string> { "Server1" }
            };
            var results = new List<GrepResult>
            {
                new GrepResult
                {
                    SessionName = "session1",
                    LocationName = "Server1",
                    MatchedField = "Message",
                    ReferencedLogEntry = new LogEntry { Level = "Error", Message = "Disk error", ThreadName = "Worker", Date = new DateTime(2025, 1, 1, 10, 0, 0) }
                }
            };

            SearchReportService.GenerateHtmlReport(path, searchParams, results);

            var html = File.ReadAllText(path);
            Assert.Contains("Disk error", html);
            Assert.Contains("Server1", html);
            Assert.Contains("session1", html);
            Assert.Contains("Total matches", html);
        }

        [Fact]
        public void GenerateHtmlReport_HtmlEncodesSpecialChars()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams { QueryText = "<script>alert('xss')</script>" };

            SearchReportService.GenerateHtmlReport(path, searchParams, new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void GenerateHtmlReport_MultipleLocations_ShowsAll()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams
            {
                LocationNames = new List<string> { "Server1", "Server2" }
            };

            SearchReportService.GenerateHtmlReport(path, searchParams, new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.Contains("Server1", html);
            Assert.Contains("Server2", html);
        }

        [Fact]
        public void GenerateHtmlReport_WithErrorLevel_HasErrorStyling()
        {
            var path = GetTempPath();
            var results = new List<GrepResult>
            {
                new GrepResult
                {
                    SessionName = "s1",
                    ReferencedLogEntry = new LogEntry { Level = "ERROR", Message = "fail" }
                }
            };

            SearchReportService.GenerateHtmlReport(path, new SearchReportParams(), results);

            var html = File.ReadAllText(path);
            Assert.Contains("lvl-error", html);
        }

        [Fact]
        public void GenerateHtmlReport_WithWarnLevel_HasWarnStyling()
        {
            var path = GetTempPath();
            var results = new List<GrepResult>
            {
                new GrepResult
                {
                    SessionName = "s1",
                    ReferencedLogEntry = new LogEntry { Level = "WARNING", Message = "warn msg" }
                }
            };

            SearchReportService.GenerateHtmlReport(path, new SearchReportParams(), results);

            var html = File.ReadAllText(path);
            Assert.Contains("lvl-warn", html);
        }

        [Fact]
        public void GenerateHtmlReport_ContainsCss()
        {
            var path = GetTempPath();
            SearchReportService.GenerateHtmlReport(path, new SearchReportParams(), new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.Contains("<style>", html);
            Assert.Contains("font-family", html);
        }

        [Fact]
        public void GenerateHtmlReport_ContainsTimestamp()
        {
            var path = GetTempPath();
            SearchReportService.GenerateHtmlReport(path, new SearchReportParams(), new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.Contains("Generated:", html);
        }

        [Fact]
        public void GenerateHtmlReport_ResultsGroupedByLocation()
        {
            var path = GetTempPath();
            var results = new List<GrepResult>
            {
                new GrepResult { LocationName = "Site A", SessionName = "s1", ReferencedLogEntry = new LogEntry() },
                new GrepResult { LocationName = "Site A", SessionName = "s2", ReferencedLogEntry = new LogEntry() },
                new GrepResult { LocationName = "Site B", SessionName = "s3", ReferencedLogEntry = new LogEntry() },
            };

            SearchReportService.GenerateHtmlReport(path, new SearchReportParams(), results);

            var html = File.ReadAllText(path);
            Assert.Contains("Matches by Location", html);
            Assert.Contains("Site A", html);
            Assert.Contains("Site B", html);
        }

        [Fact]
        public void GenerateHtmlReport_DurationParam_Shown()
        {
            var path = GetTempPath();
            var searchParams = new SearchReportParams { SearchDuration = "00:02:15" };

            SearchReportService.GenerateHtmlReport(path, searchParams, new List<GrepResult>());

            var html = File.ReadAllText(path);
            Assert.Contains("00:02:15", html);
        }

        [Fact]
        public void GenerateStatisticsReport_CreatesFile()
        {
            var path = GetTempPath();
            var stats = new LogStatisticsResult();

            SearchReportService.GenerateStatisticsReport(path, new SearchReportParams(), stats);

            Assert.True(File.Exists(path));
            var html = File.ReadAllText(path);
            Assert.Contains("Statistics Report", html);
        }
    }
}
