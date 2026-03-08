using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class StepRecorderAndClassifierTests
    {
        // ── StepFrame ──

        [Fact]
        public void StepFrame_Defaults()
        {
            var f = new StepFrame();
            Assert.Equal("", f.FileName);
            Assert.Equal(default, f.Timestamp);
            Assert.Null(f.ImageData);
            Assert.Equal("", f.TextContent);
        }

        [Fact]
        public void StepFrame_Bitmap_NullImageData_ReturnsNull()
        {
            var f = new StepFrame { ImageData = null };
            Assert.Null(f.Bitmap);
        }

        [Fact]
        public void StepFrame_Bitmap_EmptyImageData_ReturnsNull()
        {
            var f = new StepFrame { ImageData = Array.Empty<byte>() };
            Assert.Null(f.Bitmap);
        }

        // ── ParseTimestampFromFilename ──

        private static DateTime InvokeParseTimestamp(string baseName)
        {
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (DateTime)method!.Invoke(null, new object[] { baseName })!;
        }

        [Theory]
        [InlineData("2024-01-15_10-30-45", 2024, 1, 15, 10, 30, 45)]
        [InlineData("20240115_103045", 2024, 1, 15, 10, 30, 45)]
        [InlineData("step_2024-01-15_10-30-45", 2024, 1, 15, 10, 30, 45)]
        public void ParseTimestamp_ValidFormats(string baseName, int y, int m, int d, int h, int min, int s)
        {
            var result = InvokeParseTimestamp(baseName);
            Assert.Equal(new DateTime(y, m, d, h, min, s), result);
        }

        [Fact]
        public void ParseTimestamp_WithSubseconds()
        {
            // The format "yyyy-MM-dd_HH-mm-ss.fff" has 3 fraction digits for milliseconds
            var result = InvokeParseTimestamp("2024-01-15_10-30-45.1230000");
            Assert.Equal(2024, result.Year);
            Assert.Equal(10, result.Hour);
            Assert.Equal(30, result.Minute);
            Assert.Equal(45, result.Second);
        }

        [Fact]
        public void ParseTimestamp_NoMatch_ReturnsMinValue()
        {
            var result = InvokeParseTimestamp("random_filename");
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void ParseTimestamp_EmptyString_ReturnsMinValue()
        {
            var result = InvokeParseTimestamp("");
            Assert.Equal(DateTime.MinValue, result);
        }

        // ── StepRecorderViewModel navigation ──

        [Fact]
        public void StepRecorderVM_Clear_ResetsState()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            // Initialize fields
            var framesField = typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance);
            framesField?.SetValue(vm, new List<StepFrame> { new StepFrame() });
            var indexField = typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            indexField?.SetValue(vm, 0);
            typeof(StepRecorderViewModel).GetField("_isPlaying", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, false);
            typeof(StepRecorderViewModel).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, "test");
            typeof(StepRecorderViewModel).GetField("_hasIsr", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, true);

            vm.Clear();

            Assert.False(vm.HasFrames);
            Assert.False(vm.HasIsr);
            Assert.Equal("No step data loaded.", vm.StatusText);
        }

        [Fact]
        public void StepRecorderVM_MovePrevious_DecreasesIndex()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            var frames = new List<StepFrame> { new StepFrame(), new StepFrame(), new StepFrame() };
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, frames);
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, 2);
            typeof(StepRecorderViewModel).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, "");

            var method = typeof(StepRecorderViewModel).GetMethod("MovePrevious", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            var idx = (int)(typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(vm) ?? -1);
            Assert.Equal(1, idx);
        }

        [Fact]
        public void StepRecorderVM_MovePrevious_AtZero_StaysAtZero()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            var frames = new List<StepFrame> { new StepFrame() };
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, frames);
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, 0);
            typeof(StepRecorderViewModel).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, "");

            var method = typeof(StepRecorderViewModel).GetMethod("MovePrevious", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            var idx = (int)(typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(vm) ?? -1);
            Assert.Equal(0, idx);
        }

        [Fact]
        public void StepRecorderVM_MoveNext_IncreasesIndex()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            var frames = new List<StepFrame> { new StepFrame(), new StepFrame(), new StepFrame() };
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, frames);
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, 0);
            typeof(StepRecorderViewModel).GetField("_isPlaying", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, false);
            typeof(StepRecorderViewModel).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, "");

            var method = typeof(StepRecorderViewModel).GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);

            var idx = (int)(typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(vm) ?? -1);
            Assert.Equal(1, idx);
        }

        [Fact]
        public void StepRecorderVM_CurrentFrame_ValidIndex()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            var frame = new StepFrame { FileName = "test.png" };
            var frames = new List<StepFrame> { frame };
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, frames);
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, 0);

            Assert.Same(frame, vm.CurrentFrame);
        }

        [Fact]
        public void StepRecorderVM_CurrentFrame_InvalidIndex_Null()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));

            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, new List<StepFrame>());
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, -1);

            Assert.Null(vm.CurrentFrame);
        }

        [Fact]
        public void StepRecorderVM_Properties()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, new List<StepFrame> { new StepFrame(), new StepFrame() });
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, 1);

            Assert.True(vm.HasFrames);
            Assert.Equal(2, vm.CurrentIndex); // 1-based
            Assert.Equal(2, vm.TotalFrames);
        }

        [Fact]
        public void StepRecorderVM_CurrentIndex_NoFrames_ReturnsZero()
        {
            var vm = (StepRecorderViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StepRecorderViewModel));
            typeof(StepRecorderViewModel).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, new List<StepFrame>());
            typeof(StepRecorderViewModel).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vm, -1);

            Assert.Equal(0, vm.CurrentIndex);
        }

        // ── ReadFramesFromZip with in-memory ZIP ──

        [Fact]
        public void ReadFramesFromZip_EmptyZip_ReturnsEmpty()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // Empty ZIP
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ReadFramesFromZip",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (List<StepFrame>)method!.Invoke(null, new object[] { tempPath })!;
                Assert.Empty(result);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Fact]
        public void ReadFramesFromZip_WithImageAndText_MatchesThem()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // Add a fake PNG (just bytes, won't render but tests parsing)
                    var imgEntry = archive.CreateEntry("IndigoLogs/ISR/Steps/2024-01-15_10-30-45.png");
                    using (var w = imgEntry.Open()) { w.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); }

                    // Add matching text
                    var txtEntry = archive.CreateEntry("IndigoLogs/ISR/Steps/2024-01-15_10-30-45.txt");
                    using (var w = new StreamWriter(txtEntry.Open())) { w.Write("Step description"); }
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ReadFramesFromZip",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (List<StepFrame>)method!.Invoke(null, new object[] { tempPath })!;

                Assert.Single(result);
                Assert.Equal("2024-01-15_10-30-45.png", result[0].FileName);
                Assert.Equal("Step description", result[0].TextContent);
                Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45), result[0].Timestamp);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Fact]
        public void ReadFramesFromZip_SkipsSubdirectories()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("IndigoLogs/ISR/Steps/subdir/image.png");
                    using (var w = entry.Open()) { w.Write(new byte[] { 0x89, 0x50 }); }
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ReadFramesFromZip",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (List<StepFrame>)method!.Invoke(null, new object[] { tempPath })!;

                Assert.Empty(result);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Fact]
        public void ReadFramesFromZip_SortsByTimestamp()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var e2 = archive.CreateEntry("IndigoLogs/ISR/Steps/2024-01-15_12-00-00.png");
                    using (var w = e2.Open()) { w.Write(new byte[] { 0x89, 0x50 }); }

                    var e1 = archive.CreateEntry("IndigoLogs/ISR/Steps/2024-01-15_10-00-00.png");
                    using (var w = e1.Open()) { w.Write(new byte[] { 0x89, 0x50 }); }
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ReadFramesFromZip",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (List<StepFrame>)method!.Invoke(null, new object[] { tempPath })!;

                Assert.Equal(2, result.Count);
                Assert.True(result[0].Timestamp < result[1].Timestamp);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        // ── ZipHasIsrFolder ──

        [Fact]
        public void ZipHasIsrFolder_WithIsrEntries_ReturnsTrue()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("IndigoLogs/ISR/Steps/file.png");
                    using (var w = entry.Open()) { w.Write(new byte[] { 1 }); }
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ZipHasIsrFolder",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (bool)method!.Invoke(null, new object[] { tempPath })!;
                Assert.True(result);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Fact]
        public void ZipHasIsrFolder_WithoutIsrEntries_ReturnsFalse()
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("IndigoLogs/other/file.log");
                    using (var w = entry.Open()) { w.Write(new byte[] { 1 }); }
                }

                var method = typeof(StepRecorderViewModel).GetMethod("ZipHasIsrFolder",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = (bool)method!.Invoke(null, new object[] { tempPath })!;
                Assert.False(result);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        // ── LogFileClassifier extended tests ──

        [Theory]
        [InlineData("EngineGroupA.file.log", true, false, true)]
        [InlineData("EngineGroupB.file.log", true, false, true)]
        [InlineData("AppDev.file.log", false, true, true)]
        [InlineData("press.host.app.log", false, true, true)]
        [InlineData("readme.txt", true, true, false)]
        [InlineData("random.csv", true, true, false)]
        public void LogFileClassifier_IsSearchableLogFile(string fileName, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsSearchableLogFile(fileName.ToLowerInvariant(), plc, app));
        }

        [Theory]
        [InlineData("12345.file.log", true)]
        [InlineData("abc.file.log", false)]
        [InlineData("enginegroupa.file.log", false)]
        [InlineData("no.file", false)]
        [InlineData("test123.file.log", true)]
        public void LogFileClassifier_IsNumericAppFileName(string fileName, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(fileName.ToLowerInvariant()));
        }

        [Theory]
        [InlineData("appdev.file.log", "APP")]
        [InlineData("press.host.app.log", "APP")]
        [InlineData("12345.file.log", "APP")]
        [InlineData("enginegroupa.file.log", "PLC")]
        [InlineData("unknown.file.log", "PLC")]
        public void LogFileClassifier_DetermineLogType(string path, string expected)
        {
            Assert.Equal(expected, LogFileClassifier.DetermineLogType(path));
        }

        [Fact]
        public void LogFileClassifier_IsLogFile_ZipAlwaysTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("test.zip", false, false));
            Assert.True(LogFileClassifier.IsLogFile("test.ZIP", false, false));
        }

        [Fact]
        public void LogFileClassifier_IsLogEntry_ZipAlwaysFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("inner.zip", true, true));
        }

        [Theory]
        [InlineData("path/to/EngineGroupA.file.log", true, false, true)]
        [InlineData("deep/path/appdev.file.log", false, true, true)]
        public void LogFileClassifier_IsSearchableLogFile_WithPath(string path, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsSearchableLogFile(path.ToLowerInvariant(), plc, app));
        }

        [Fact]
        public void LogFileClassifier_IsSearchableLogFile_NoSnFile()
        {
            // "no-sn" with "file" should be PLC
            Assert.True(LogFileClassifier.IsSearchableLogFile("no-sn.file.log", true, false));
            Assert.False(LogFileClassifier.IsSearchableLogFile("no-sn.file.log", false, true));
        }

        [Fact]
        public void LogFileClassifier_PlcAndAppBothFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file.log", false, false));
            Assert.False(LogFileClassifier.IsSearchableLogFile("appdev.file.log", false, false));
        }

        // ── UpdateService helpers ──

        [Fact]
        public void UpdateService_CanConstruct()
        {
            var svc = new UpdateService();
            Assert.NotNull(svc);
        }
    }
}
