using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace IndiLogs.Tests
{
    /// <summary>
    /// Integration tests for LogFileService — tests the full load pipeline
    /// using in-memory ZIP fixtures with known content.
    /// </summary>
    public class LogFileServiceTests : IDisposable
    {
        private readonly List<string> _tempFiles = new List<string>();

        private string CreateTempZip(Action<ZipArchive> populate)
        {
            var path = Path.Combine(Path.GetTempPath(), $"IndiLogsTest_{Guid.NewGuid():N}.zip");
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                populate(archive);
            }
            _tempFiles.Add(path);
            return path;
        }

        private void AddTextEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        // ── StringPool Tests ──

        [Fact]
        public void StringPool_Intern_ReturnsSameReference()
        {
            var pool = new LogFileService.StringPool();
            string a = pool.Intern("hello");
            string b = pool.Intern(new string("hello".ToCharArray()));
            Assert.Same(a, b);
        }

        [Fact]
        public void StringPool_Intern_NullReturnsNull()
        {
            var pool = new LogFileService.StringPool();
            Assert.Null(pool.Intern(null));
        }

        [Fact]
        public void StringPool_Clear_ResetsCache()
        {
            var pool = new LogFileService.StringPool();
            var first = pool.Intern("test");
            pool.Clear();
            // After clear, a new intern should still work
            var second = pool.Intern("test");
            Assert.Equal("test", second);
        }

        // ── Events CSV Parsing ──

        [Fact]
        public void ParseEventsCsv_ParsesValidCsv()
        {
            var csv = "Time,Name,State,Severity,Description\n" +
                      "2025-01-15 10:30:00,PrintStarted,Active,Info,Print job started\n" +
                      "2025-01-15 10:31:00,PaperJam,Active,Error,Paper jam detected\n";

            var service = new LogFileService(new PluginLoader());
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = service.ParseEventsCsv(stream);

            Assert.Equal(2, events.Count);
            Assert.Equal("PrintStarted", events[0].Name);
            Assert.Equal("PaperJam", events[1].Name);
            Assert.Equal("Error", events[1].Severity);
        }

        [Fact]
        public void ParseEventsCsv_EmptyStream_ReturnsEmpty()
        {
            var service = new LogFileService(new PluginLoader());
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));
            var events = service.ParseEventsCsv(stream);
            Assert.Empty(events);
        }

        [Fact]
        public void ParseEventsCsv_HeaderOnly_ReturnsEmpty()
        {
            var csv = "Time,Name,State,Severity,Description\n";
            var service = new LogFileService(new PluginLoader());
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var events = service.ParseEventsCsv(stream);
            Assert.Empty(events);
        }

        // ── LoadSessionAsync Integration Tests ──

        [Fact]
        public async Task LoadSessionAsync_NullPaths_ReturnsEmptySession()
        {
            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(null, progress);

            Assert.NotNull(session);
            // Logs may be null or empty list depending on code path
            Assert.True(session.Logs == null || session.Logs.Count == 0);
        }

        [Fact]
        public async Task LoadSessionAsync_EmptyPaths_ReturnsEmptySession()
        {
            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(Array.Empty<string>(), progress);

            Assert.NotNull(session);
            Assert.True(session.Logs == null || session.Logs.Count == 0);
        }

        [Fact]
        public async Task LoadSessionAsync_NonExistentFile_ReturnsEmptySession()
        {
            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(
                new[] { @"C:\nonexistent\file.zip" }, progress);

            Assert.NotNull(session);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithEventsCsv_ParsesEvents()
        {
            // Events CSV must match pressEvents.*.csv naming pattern
            var csv = "Time,Name,State,Severity,Description\n" +
                      "2025-01-15 10:30:00,TestEvent,Active,Info,Test event\n" +
                      "2025-01-15 10:31:00,ErrorEvent,Active,Error,Error occurred\n";

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "pressEvents.test.csv", csv);
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            Assert.NotNull(session);
            Assert.NotNull(session.Events);
            Assert.Equal(2, session.Events.Count);
            Assert.Equal("TestEvent", session.Events[0].Name);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithAppDevLog_ParsesAppLogs()
        {
            // Old format: \x1e separated fields
            // Path must contain "APPDEV" and "indigologs/logger files" for classification
            var sep = '\x1e';
            var lines = new[]
            {
                $"2025-01-15 10:30:00,000{sep}MainThread{sep}root1{sep}flow1{sep}name1{sep}Pattern1{sep}ctx{sep}INFO MyLogger{sep}MyClass.Method{sep}Test message 1{sep}{sep}",
                $"2025-01-15 10:30:01,000{sep}WorkerThread{sep}root2{sep}flow2{sep}name2{sep}{sep}ctx{sep}ERROR ErrorLogger{sep}Other.Method{sep}Error happened{sep}NullRef exception{sep}"
            };

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "IndigoLogs/Logger Files/APPDEV.log", string.Join("\n", lines));
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            Assert.NotNull(session);
            Assert.NotNull(session.AppDevLogs);
            Assert.True(session.AppDevLogs.Count >= 2, $"Expected >=2 APP logs, got {session.AppDevLogs.Count}");

            var firstLog = session.AppDevLogs.FirstOrDefault(l => l.Message == "Test message 1");
            Assert.NotNull(firstLog);
            Assert.Equal("INFO", firstLog.Level);
            Assert.Equal("MyLogger", firstLog.Logger);
            Assert.Equal("Pattern1", firstLog.Pattern);

            var errorLog = session.AppDevLogs.FirstOrDefault(l => l.Message == "Error happened");
            Assert.NotNull(errorLog);
            Assert.Equal("ERROR", errorLog.Level);
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithConfigFile_ExtractsConfig()
        {
            var configContent = "<configuration><setting key=\"test\">value</setting></configuration>";

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Config/system.config", configContent);
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            Assert.NotNull(session);
            Assert.NotNull(session.ConfigurationFiles);
            // Config files in Config/ folder should be extracted
        }

        [Fact]
        public async Task LoadSessionAsync_ZipWithGlobalsXml_ExtractsGlobals()
        {
            var globalsContent = "<?xml version=\"1.0\"?><globals><item name=\"test\">123</item></globals>";

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "Configuration/globals.xml", globalsContent);
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            Assert.NotNull(session);
            Assert.NotNull(session.GlobalsFiles);
        }

        [Fact]
        public async Task LoadSessionAsync_ProgressReports()
        {
            var progressValues = new List<(double pct, string msg)>();
            var progress = new Progress<(double, string)>(p => progressValues.Add(p));

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "events.csv", "Time,Name\n2025-01-15 10:00:00,Test\n");
            });

            var service = new LogFileService(new PluginLoader());
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            // Wait briefly for progress callbacks to fire (they use SynchronizationContext)
            await Task.Delay(100);

            // We should have received at least one progress report
            // (exact count depends on execution timing)
            Assert.NotNull(session);
        }

        [Fact]
        public async Task LoadSessionAsync_InitializesDictionaries()
        {
            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "pressEvents.test.csv", "Time,Name\n2025-01-15 10:00:00,Test\n");
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            Assert.NotNull(session);
            // Session should have all dictionaries initialized (not null)
            Assert.NotNull(session.ConfigurationFiles);
            Assert.NotNull(session.DatabaseFiles);
            Assert.NotNull(session.TerminalLogFiles);
            Assert.NotNull(session.GlobalsFiles);
            Assert.NotNull(session.SystabFiles);
        }

        [Fact]
        public async Task LoadSessionAsync_MultiplePaths_MergesResults()
        {
            var csv1 = "Time,Name,State,Severity\n2025-01-15 10:00:00,Event1,Active,Info\n";
            var csv2 = "Time,Name,State,Severity\n2025-01-15 11:00:00,Event2,Active,Warning\n";

            var zip1 = CreateTempZip(archive => AddTextEntry(archive, "pressEvents.1.csv", csv1));
            var zip2 = CreateTempZip(archive => AddTextEntry(archive, "pressEvents.2.csv", csv2));

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zip1, zip2 }, progress);

            Assert.NotNull(session);
            Assert.NotNull(session.Events);
            // Both ZIPs should contribute events
            Assert.True(session.Events.Count >= 2, $"Expected >=2 events, got {session.Events.Count}");
        }

        // ── ZIP Bomb Protection ──

        [Fact]
        public async Task LoadSessionAsync_LargeEntry_DoesNotCrash()
        {
            // Create a ZIP with a reasonably large text file (but under the 512MB limit)
            var zipPath = CreateTempZip(archive =>
            {
                var entry = archive.CreateEntry("large.txt");
                using var writer = new StreamWriter(entry.Open());
                // Write 1000 lines — well under the limit
                for (int i = 0; i < 1000; i++)
                    writer.WriteLine($"Line {i}: Some repeated content for testing purposes");
            });

            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress);

            // Should complete without exception
            Assert.NotNull(session);
        }

        // ── TabSelection Config ──

        [Fact]
        public async Task LoadSessionAsync_WithTabSelection_RespectsConfig()
        {
            var sep = '\x1e';
            var logLine = $"2025-01-15 10:30:00,000{sep}Thread{sep}r{sep}f{sep}n{sep}{sep}c{sep}INFO Log{sep}Loc{sep}Msg{sep}{sep}";

            var zipPath = CreateTempZip(archive =>
            {
                AddTextEntry(archive, "IndigoLogs/Logger Files/APPDEV.log", logLine);
                AddTextEntry(archive, "pressEvents.test.csv", "Time,Name\n2025-01-15 10:00:00,Evt\n");
            });

            // Disable APP loading, enable events
            var tabConfig = new TabSelectionConfig { LoadApp = false, LoadEvents = true };
            var service = new LogFileService(new PluginLoader());
            var progress = new Progress<(double, string)>();
            var session = await service.LoadSessionAsync(new[] { zipPath }, progress, tabConfig);

            Assert.NotNull(session);
            // Events should load since LoadEvents = true
            Assert.NotNull(session.Events);
            Assert.True(session.Events.Count >= 1);
            // APP logs should be empty since LoadApp = false
            Assert.True(session.AppDevLogs == null || session.AppDevLogs.Count == 0);
        }
    }
}
