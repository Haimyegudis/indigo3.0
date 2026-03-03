using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class ZipClassificationHelpersTests
    {
        // ── IsCustomTerminalLog ──

        [Theory]
        [InlineData("whel3_2024.log", true)]
        [InlineData("ecm_data.txt", true)]
        [InlineData("COM1_output.log", true)]
        [InlineData("0001_trace.log", true)]
        [InlineData("Io-MainBoard.log", true)]
        [InlineData("Stab-Report.txt", true)]
        [InlineData("PRE_check.log", true)]
        [InlineData("POST_verify.log", true)]
        [InlineData("random_file.log", false)]
        [InlineData("app.log", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsCustomTerminalLog_MatchesKnownPrefixes(string fileName, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsCustomTerminalLog(fileName));
        }

        [Fact]
        public void IsCustomTerminalLog_IsCaseInsensitive()
        {
            Assert.True(ZipClassificationHelpers.IsCustomTerminalLog("WHEL3_upper.log"));
            Assert.True(ZipClassificationHelpers.IsCustomTerminalLog("Ecm_mixed.log"));
            Assert.True(ZipClassificationHelpers.IsCustomTerminalLog("post_lower.log"));
        }

        // ── IsSystabFile ──

        [Theory]
        [InlineData("machine/diagnosticslogs/systab_saved.txt", true)]
        [InlineData("machine\\diagnosticslogs\\systab_default.txt", true)]
        [InlineData("diagnosticslogs/systab_minimum.txt", true)]
        [InlineData("machine/logs/systab_saved.txt", false)]
        [InlineData("diagnosticslogs/other_file.txt", false)]
        [InlineData("diagnosticslogs/systab_saved.log", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSystabFile_MatchesDiagnosticsLogsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsSystabFile(path));
        }

        // ── IsPluginCandidateExtension ──

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
        [InlineData("file.zip", false)]
        [InlineData("file", false)]
        public void IsPluginCandidateExtension_FiltersCorrectly(string fileName, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsPluginCandidateExtension(fileName));
        }

        // ── IsGlobalsXmlFile ──

        [Theory]
        [InlineData("machine/datamanagement/ecommon/globals/params.xml", true)]
        [InlineData("machine\\datamanagement\\ecommon\\globals\\config.xml", true)]
        [InlineData("datamanagement/ecommon/globals/test.xml", true)]
        [InlineData("machine/datamanagement/ecommon/globals/file.txt", false)]
        [InlineData("machine/other/globals/params.xml", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsGlobalsXmlFile_MatchesGlobalsPath(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsGlobalsXmlFile(path));
        }

        // ── IsTerminalLogsPath ──

        [Theory]
        [InlineData("machine/terminallogs/file.log", true)]
        [InlineData("machine\\terminallogs\\file.log", true)]
        [InlineData("terminallogs/file.log", true)]
        [InlineData("machine/logs/file.log", false)]
        public void IsTerminalLogsPath_MatchesCorrectPaths(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsTerminalLogsPath(path));
        }

        // ── IsLrsPath ──

        [Theory]
        [InlineData("machine/lrs/file.log", true)]
        [InlineData("machine\\lrs\\file.log", true)]
        [InlineData("lrs/file.log", true)]
        [InlineData("machine/other/file.log", false)]
        public void IsLrsPath_MatchesCorrectPaths(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsLrsPath(path));
        }

        // ── IsConfigurationPath ──

        [Theory]
        [InlineData("machine/configuration/file.cfg", true)]
        [InlineData("machine\\configuration\\file.cfg", true)]
        [InlineData("configuration/file.cfg", true)]
        [InlineData("machine/settings/file.cfg", false)]
        public void IsConfigurationPath_MatchesCorrectPaths(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsConfigurationPath(path));
        }

        // ── IsEmStatisticsFile ──

        [Theory]
        [InlineData("logs/Indigo.Infra.EM_Statistics.csv", true)]
        [InlineData("EM_Statistics.csv", true)]
        [InlineData("em_statistics.CSV", true)]
        [InlineData("EM_Statistics.txt", false)]
        [InlineData("other_file.csv", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsEmStatisticsFile_MatchesCorrectFiles(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsEmStatisticsFile(name));
        }

        // ── ShouldSkipEntry ──

        [Theory]
        [InlineData("machine/backup/old.log", true)]
        [InlineData("machine\\backup\\old.log", true)]
        [InlineData("machine/old/file.log", true)]
        [InlineData("machine/temp/file.log", true)]
        [InlineData("machine/archive/file.log", true)]
        [InlineData("machine/logs/file.log", false)]
        public void ShouldSkipEntry_MatchesExcludedFolders(string path, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.ShouldSkipEntry(path));
        }
    }
}
