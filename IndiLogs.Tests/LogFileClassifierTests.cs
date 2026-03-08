using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogFileClassifierTests
    {
        // IsLogFile

        [Theory]
        [InlineData("session.zip", false, false)]
        [InlineData("session.ZIP", false, false)]
        [InlineData("path/to/archive.zip", true, false)]
        [InlineData("path/to/archive.zip", false, true)]
        public void IsLogFile_ZipFile_ReturnsTrue(string path, bool plc, bool app)
        {
            Assert.True(LogFileClassifier.IsLogFile(path, plc, app));
        }

        [Fact]
        public void IsLogFile_NonZipNonLog_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("readme.txt", true, true));
        }

        [Fact]
        public void IsLogFile_PlcFileWithPlcFlag_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("enginegroupa.file.log", true, false));
        }

        [Fact]
        public void IsLogFile_PlcFileWithoutPlcFlag_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("enginegroupa.file.log", false, false));
        }

        // IsLogEntry

        [Fact]
        public void IsLogEntry_ZipEntry_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("nested.zip", true, true));
        }

        [Fact]
        public void IsLogEntry_PlcEntry_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("enginegroupa.file", true, false));
        }

        [Fact]
        public void IsLogEntry_NonLogEntry_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("readme.txt", true, true));
        }

        // IsSearchableLogFile - PLC patterns

        [Theory]
        [InlineData("enginegroupa.file")]
        [InlineData("enginegroupa.file.log")]
        [InlineData("path/enginegroupa.file")]
        public void IsSearchableLogFile_EngineGroupA_PlcTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("enginegroupb.file")]
        [InlineData("enginegroupb.file.log")]
        public void IsSearchableLogFile_EngineGroupB_PlcTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("something.file.log")]
        [InlineData("test.file.log")]
        public void IsSearchableLogFile_DotFileLog_PlcTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("no-sn_file_data")]
        [InlineData("no-sn.file.log")]
        public void IsSearchableLogFile_NoSnWithFile_PlcTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Fact]
        public void IsSearchableLogFile_NoSnWithoutFile_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("no-sn_data", true, false));
        }

        [Fact]
        public void IsSearchableLogFile_PlcPattern_AppFlagOnly_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", false, true));
        }

        // IsSearchableLogFile - APP patterns

        [Theory]
        [InlineData("appdev.log")]
        [InlineData("some_appdev_data")]
        public void IsSearchableLogFile_Appdev_AppTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, false, true));
        }

        [Theory]
        [InlineData("press.host.app")]
        [InlineData("press.host.app.log")]
        public void IsSearchableLogFile_PressHostApp_AppTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, false, true));
        }

        [Fact]
        public void IsSearchableLogFile_AppPattern_PlcFlagOnly_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("appdev.log", true, false));
        }

        [Fact]
        public void IsSearchableLogFile_BothFlagsTrue_PlcMatch_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", true, true));
        }

        [Fact]
        public void IsSearchableLogFile_BothFlagsTrue_AppMatch_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("appdev.log", true, true));
        }

        [Fact]
        public void IsSearchableLogFile_BothFlagsFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", false, false));
        }

        [Fact]
        public void IsSearchableLogFile_ExtractsFileNameFromPath()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("c:/logs/session/appdev.log", false, true));
        }

        [Fact]
        public void IsSearchableLogFile_BackslashPath_ExtractsFileName()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("c:\\logs\\session\\appdev.log", false, true));
        }

        // IsNumericAppFileName

        [Theory]
        [InlineData("abc1.file", true)]
        [InlineData("test9.file.log", true)]
        [InlineData("x0.file", true)]
        public void IsNumericAppFileName_DigitBeforeDotFile_ReturnsTrue(string name, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(name));
        }

        [Theory]
        [InlineData("abc.file")]
        [InlineData("test.file.log")]
        public void IsNumericAppFileName_NoDigitBeforeDotFile_ReturnsFalse(string name)
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(name));
        }

        [Fact]
        public void IsNumericAppFileName_EngineGroup_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName("enginegroupa1.file"));
        }

        [Fact]
        public void IsNumericAppFileName_NoDotFile_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName("abc1.txt"));
        }

        [Fact]
        public void IsNumericAppFileName_DotFileAtStart_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(".file"));
        }

        [Fact]
        public void IsNumericAppFileName_EmptyString_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(""));
        }

        // IsSearchableLogFile with numeric APP filename

        [Fact]
        public void IsSearchableLogFile_NumericAppFile_AppTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("module1.file", false, true));
        }

        [Fact]
        public void IsSearchableLogFile_NumericAppFile_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("module1.file", false, false));
        }

        // DetermineLogType

        [Theory]
        [InlineData("appdev.log")]
        [InlineData("C:\\logs\\appdev")]
        [InlineData("/logs/some_appdev_data")]
        public void DetermineLogType_Appdev_ReturnsAPP(string path)
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType(path));
        }

        [Theory]
        [InlineData("press.host.app")]
        [InlineData("C:\\data\\press.host.app.log")]
        public void DetermineLogType_PressHostApp_ReturnsAPP(string path)
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType(path));
        }

        [Fact]
        public void DetermineLogType_NumericAppFile_ReturnsAPP()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("module1.file"));
        }

        [Theory]
        [InlineData("enginegroupa.file")]
        [InlineData("enginegroupb.file.log")]
        [InlineData("something.file.log")]
        [InlineData("unknown.txt")]
        public void DetermineLogType_NonAppPattern_ReturnsPLC(string path)
        {
            Assert.Equal("PLC", LogFileClassifier.DetermineLogType(path));
        }

        [Fact]
        public void DetermineLogType_EngineGroupWithDigit_ReturnsPLC()
        {
            Assert.Equal("PLC", LogFileClassifier.DetermineLogType("enginegroupa1.file"));
        }

        [Fact]
        public void DetermineLogType_CaseInsensitive()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("APPDEV.LOG"));
        }
    }
}
