using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogFileClassifierExtendedTests
    {
        // ===== IsLogFile =====

        [Theory]
        [InlineData("logs.zip")]
        [InlineData("LOGS.ZIP")]
        [InlineData("data.Zip")]
        [InlineData("path/to/nested.zip")]
        [InlineData("c:\\folder\\archive.zip")]
        public void IsLogFile_ZipExtension_AlwaysTrue_RegardlessOfFlags(string path)
        {
            Assert.True(LogFileClassifier.IsLogFile(path, false, false));
            Assert.True(LogFileClassifier.IsLogFile(path, true, false));
            Assert.True(LogFileClassifier.IsLogFile(path, false, true));
            Assert.True(LogFileClassifier.IsLogFile(path, true, true));
        }

        [Fact]
        public void IsLogFile_NonZipNonLog_BothFlagsTrue_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("readme.txt", true, true));
        }

        [Fact]
        public void IsLogFile_PlcPattern_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("enginegroupa.file", true, false));
        }

        [Fact]
        public void IsLogFile_PlcPattern_PlcFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("enginegroupa.file", false, false));
        }

        [Fact]
        public void IsLogFile_AppPattern_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("appdev.log", false, true));
        }

        [Fact]
        public void IsLogFile_AppPattern_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("appdev.log", false, false));
        }

        [Fact]
        public void IsLogFile_CaseInsensitive_EngineGroup()
        {
            Assert.True(LogFileClassifier.IsLogFile("ENGINEGROUPA.FILE", true, false));
        }

        [Fact]
        public void IsLogFile_BothFlags_PlcFile_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("enginegroupb.file", true, true));
        }

        [Fact]
        public void IsLogFile_BothFlags_AppFile_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("press.host.app", true, true));
        }

        [Fact]
        public void IsLogFile_NumericAppFile_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("module3.file", false, true));
        }

        [Fact]
        public void IsLogFile_NonZipNonLog_BothFlagsFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogFile("enginegroupa.file", false, false));
        }

        // ===== IsLogEntry =====

        [Theory]
        [InlineData("nested.zip")]
        [InlineData("INNER.ZIP")]
        [InlineData("path/archive.Zip")]
        public void IsLogEntry_ZipEntry_AlwaysFalse(string entry)
        {
            Assert.False(LogFileClassifier.IsLogEntry(entry, true, true));
        }

        [Fact]
        public void IsLogEntry_PlcEntry_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("enginegroupa.file", true, false));
        }

        [Fact]
        public void IsLogEntry_PlcEntry_PlcFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("enginegroupa.file", false, false));
        }

        [Fact]
        public void IsLogEntry_AppEntry_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("appdev.log", false, true));
        }

        [Fact]
        public void IsLogEntry_AppEntry_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("appdev.log", false, false));
        }

        [Fact]
        public void IsLogEntry_NonLogNonZip_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("config.xml", true, true));
        }

        [Fact]
        public void IsLogEntry_EngineGroupBFileLog_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("enginegroupb.file.log", true, false));
        }

        [Fact]
        public void IsLogEntry_PressHostApp_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("press.host.app.log", false, true));
        }

        [Fact]
        public void IsLogEntry_NumericAppFile_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("widget5.file", false, true));
        }

        [Fact]
        public void IsLogEntry_CaseInsensitive()
        {
            Assert.True(LogFileClassifier.IsLogEntry("APPDEV.LOG", false, true));
        }

        [Fact]
        public void IsLogEntry_ZipEntry_BothFlagsFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("data.zip", false, false));
        }

        // ===== IsSearchableLogFile - PLC patterns =====

        [Theory]
        [InlineData("enginegroupa.file")]
        [InlineData("enginegroupa.file.log")]
        [InlineData("enginegroupa.file.something")]
        public void IsSearchableLogFile_EngineGroupA_PlcTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("enginegroupb.file")]
        [InlineData("enginegroupb.file.log")]
        [InlineData("enginegroupb.file.old")]
        public void IsSearchableLogFile_EngineGroupB_PlcTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("random.file.log")]
        [InlineData("xyz.file.log")]
        [InlineData("test123.file.log")]
        public void IsSearchableLogFile_EndsWithDotFileLog_PlcTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Theory]
        [InlineData("no-sn_file_data")]
        [InlineData("no-sn.file.log")]
        [InlineData("no-sn_something_file")]
        public void IsSearchableLogFile_NoSnContainsFile_PlcTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, true, false));
        }

        [Fact]
        public void IsSearchableLogFile_NoSnWithoutFile_PlcTrue_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("no-sn_data_only", true, false));
        }

        [Fact]
        public void IsSearchableLogFile_PlcPattern_PlcFalse_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", false, false));
        }

        [Fact]
        public void IsSearchableLogFile_PlcPattern_PlcFalseAppTrue_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", false, true));
        }

        // ===== IsSearchableLogFile - APP patterns =====

        [Theory]
        [InlineData("appdev")]
        [InlineData("appdev.log")]
        [InlineData("some_appdev_data")]
        [InlineData("myappdev")]
        public void IsSearchableLogFile_ContainsAppdev_AppTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, false, true));
        }

        [Theory]
        [InlineData("press.host.app")]
        [InlineData("press.host.app.log")]
        [InlineData("prefix_press.host.app_suffix")]
        public void IsSearchableLogFile_ContainsPressHostApp_AppTrue_ReturnsTrue(string path)
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile(path, false, true));
        }

        [Fact]
        public void IsSearchableLogFile_AppPattern_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("appdev.log", false, false));
        }

        [Fact]
        public void IsSearchableLogFile_NumericAppFile_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("module1.file", false, true));
        }

        [Fact]
        public void IsSearchableLogFile_NumericAppFile_AppFalse_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("module1.file", false, false));
        }

        // ===== IsSearchableLogFile - path extraction =====

        [Fact]
        public void IsSearchableLogFile_ForwardSlashPath_ExtractsFileName()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("/logs/dir/enginegroupa.file", true, false));
        }

        [Fact]
        public void IsSearchableLogFile_BackslashPath_ExtractsFileName()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("c:\\logs\\dir\\appdev.log", false, true));
        }

        [Fact]
        public void IsSearchableLogFile_MixedSlashPath_UsesLastSeparator()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("c:\\logs/dir\\enginegroupb.file", true, false));
        }

        [Fact]
        public void IsSearchableLogFile_DirectoryContainsPLC_FileIsNotPLC_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("c:/enginegroupa.file/readme.txt", true, false));
        }

        // ===== IsSearchableLogFile - both flags =====

        [Fact]
        public void IsSearchableLogFile_BothTrue_PlcMatch_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", true, true));
        }

        [Fact]
        public void IsSearchableLogFile_BothTrue_AppMatch_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsSearchableLogFile("appdev.log", true, true));
        }

        [Fact]
        public void IsSearchableLogFile_BothTrue_NoMatch_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("readme.txt", true, true));
        }

        [Fact]
        public void IsSearchableLogFile_BothFalse_AlwaysFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa.file", false, false));
            Assert.False(LogFileClassifier.IsSearchableLogFile("appdev.log", false, false));
        }

        // ===== IsSearchableLogFile - overlap: file is both PLC and APP =====

        [Fact]
        public void IsSearchableLogFile_FileMatchesBothPLCAndAPP_EitherFlagReturnsTrue()
        {
            string hybrid = "appdev.file.log";
            Assert.True(LogFileClassifier.IsSearchableLogFile(hybrid, true, false));
            Assert.True(LogFileClassifier.IsSearchableLogFile(hybrid, false, true));
            Assert.True(LogFileClassifier.IsSearchableLogFile(hybrid, true, true));
        }

        [Fact]
        public void IsSearchableLogFile_NonMatchingFile_NeitherFlag_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsSearchableLogFile("random.txt", false, false));
        }

        [Fact]
        public void IsSearchableLogFile_EngineGroupWithDigit_NeitherPLCNorAPP()
        {
            // "enginegroupa1.file" does NOT contain "enginegroupa.file" literally.
            // It does NOT end with ".file.log".
            // IsNumericAppFileName returns false because it contains "enginegroup".
            // So neither PLC nor APP match.
            Assert.False(LogFileClassifier.IsSearchableLogFile("enginegroupa1.file", true, true));
        }

        // ===== IsNumericAppFileName =====

        [Theory]
        [InlineData("abc1.file", true)]
        [InlineData("test9.file", true)]
        [InlineData("x0.file", true)]
        [InlineData("mod3.file.log", true)]
        [InlineData("item55.file", true)]
        public void IsNumericAppFileName_DigitBeforeDotFile_ReturnsTrue(string name, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(name));
        }

        [Theory]
        [InlineData("abc.file")]
        [InlineData("test.file")]
        [InlineData("xyz.file.log")]
        public void IsNumericAppFileName_NoDigitBeforeDotFile_ReturnsFalse(string name)
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(name));
        }

        [Theory]
        [InlineData("enginegroupa1.file")]
        [InlineData("enginegroupb9.file")]
        [InlineData("enginegroup0.file")]
        public void IsNumericAppFileName_EngineGroup_AlwaysFalse(string name)
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(name));
        }

        [Theory]
        [InlineData("abc1.txt")]
        [InlineData("test9.log")]
        [InlineData("data1.csv")]
        public void IsNumericAppFileName_NoDotFile_ReturnsFalse(string name)
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName(name));
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

        [Fact]
        public void IsNumericAppFileName_NoDotFileSubstring_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName("nofilehere1"));
        }

        [Fact]
        public void IsNumericAppFileName_SingleDigitBeforeDotFile_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsNumericAppFileName("7.file"));
        }

        [Fact]
        public void IsNumericAppFileName_MultipleDigits_BeforeDotFile_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsNumericAppFileName("abc123.file"));
        }

        [Fact]
        public void IsNumericAppFileName_LetterAfterDigit_BeforeDotFile_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsNumericAppFileName("abc1a.file"));
        }

        [Fact]
        public void IsNumericAppFileName_OnlyDigits_BeforeDotFile_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsNumericAppFileName("999.file"));
        }

        [Fact]
        public void IsNumericAppFileName_DotFileInMiddle_DigitBefore_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsNumericAppFileName("mod1.file.extra.stuff"));
        }

        // ===== DetermineLogType =====

        [Theory]
        [InlineData("appdev")]
        [InlineData("appdev.log")]
        [InlineData("some_appdev_stuff")]
        [InlineData("APPDEV.LOG")]
        public void DetermineLogType_ContainsAppdev_ReturnsAPP(string path)
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType(path));
        }

        [Theory]
        [InlineData("press.host.app")]
        [InlineData("press.host.app.log")]
        [InlineData("PRESS.HOST.APP")]
        public void DetermineLogType_ContainsPressHostApp_ReturnsAPP(string path)
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType(path));
        }

        [Theory]
        [InlineData("module1.file")]
        [InlineData("item9.file")]
        [InlineData("x0.file.log")]
        public void DetermineLogType_NumericAppFile_ReturnsAPP(string path)
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType(path));
        }

        [Theory]
        [InlineData("enginegroupa.file")]
        [InlineData("enginegroupb.file.log")]
        [InlineData("random.file.log")]
        [InlineData("no-sn_file_data")]
        [InlineData("unknown.txt")]
        [InlineData("readme.md")]
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
        public void DetermineLogType_PathWithDirectory_UsesFileName()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("c:\\logs\\appdev.log"));
        }

        [Fact]
        public void DetermineLogType_DirectoryContainsAppdev_FileDoesNot_ReturnsPLC()
        {
            Assert.Equal("PLC", LogFileClassifier.DetermineLogType("c:\\appdev\\enginegroupa.file"));
        }

        [Fact]
        public void DetermineLogType_ForwardSlashPath_ReturnsCorrectType()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("/data/logs/press.host.app"));
        }

        [Fact]
        public void DetermineLogType_PlainNonMatchingFile_ReturnsPLC()
        {
            Assert.Equal("PLC", LogFileClassifier.DetermineLogType("somefile"));
        }

        [Fact]
        public void DetermineLogType_AppdevTakesPriority_OverNumericCheck()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("appdev1.file"));
        }

        [Fact]
        public void DetermineLogType_PressHostAppTakesPriority_OverNumericCheck()
        {
            Assert.Equal("APP", LogFileClassifier.DetermineLogType("press.host.app.file"));
        }

        [Fact]
        public void DetermineLogType_EmptyString_ReturnsPLC()
        {
            Assert.Equal("PLC", LogFileClassifier.DetermineLogType(""));
        }

        // ===== IsLogFile / IsLogEntry interaction edge cases =====

        [Fact]
        public void IsLogFile_ZipInSubdirectory_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("deep/nested/path/file.zip", false, false));
        }

        [Fact]
        public void IsLogEntry_ZipInSubdirectory_ReturnsFalse()
        {
            Assert.False(LogFileClassifier.IsLogEntry("deep/nested/path/file.zip", true, true));
        }

        [Fact]
        public void IsLogFile_FileLogExtension_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("custom.file.log", true, false));
        }

        [Fact]
        public void IsLogEntry_FileLogExtension_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("custom.file.log", true, false));
        }

        [Fact]
        public void IsLogFile_NoSnFile_BothFlags_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("no-sn_file_log", true, true));
        }

        [Fact]
        public void IsLogEntry_NoSnFile_PlcTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("no-sn_file_log", true, false));
        }

        [Fact]
        public void IsLogFile_UpperCaseAppdev_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogFile("APPDEV", false, true));
        }

        [Fact]
        public void IsLogEntry_UpperCasePressHostApp_AppTrue_ReturnsTrue()
        {
            Assert.True(LogFileClassifier.IsLogEntry("PRESS.HOST.APP", false, true));
        }
    }
}
