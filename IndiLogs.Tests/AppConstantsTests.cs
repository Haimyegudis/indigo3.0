using System;
using System.Text.RegularExpressions;
using IndiLogs_3._0;
using Xunit;

namespace IndiLogs.Tests;

public class AppConstantsTests
{
    #region RegexTimeout

    [Fact]
    public void RegexTimeout_Is2Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), AppConstants.RegexTimeout);
    }

    #endregion

    #region JsonMaxDepth

    [Fact]
    public void JsonMaxDepth_Is64()
    {
        Assert.Equal(64, AppConstants.JsonMaxDepth);
    }

    [Fact]
    public void SafeJsonSettings_HasMaxDepth64()
    {
        Assert.Equal(64, AppConstants.SafeJsonSettings.MaxDepth);
    }

    [Fact]
    public void SafeJsonDocumentOptions_HasMaxDepth64()
    {
        Assert.Equal(64, AppConstants.SafeJsonDocumentOptions.MaxDepth);
    }

    #endregion

    #region String Constants

    [Fact]
    public void ZipExtension_IsDotZip()
    {
        Assert.Equal(".zip", AppConstants.ZipExtension);
    }

    [Fact]
    public void CsvExtension_IsDotCsv()
    {
        Assert.Equal(".csv", AppConstants.CsvExtension);
    }

    [Fact]
    public void SystabPrefix_IsCorrect()
    {
        Assert.Equal("systab_", AppConstants.SystabPrefix);
    }

    [Fact]
    public void UiUpdateBatchSize_Is500()
    {
        Assert.Equal(500, AppConstants.UiUpdateBatchSize);
    }

    #endregion

    #region BuiltInFields

    [Theory]
    [InlineData("Date")]
    [InlineData("Level")]
    [InlineData("Message")]
    [InlineData("ThreadName")]
    [InlineData("Logger")]
    [InlineData("ProcessName")]
    [InlineData("Source")]
    [InlineData("LineNumber")]
    public void BuiltInFields_ContainsExpectedField(string field)
    {
        Assert.True(AppConstants.BuiltInFields.Contains(field));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("LEVEL")]
    [InlineData("message")]
    public void BuiltInFields_CaseInsensitive(string field)
    {
        Assert.True(AppConstants.BuiltInFields.Contains(field));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("CustomField")]
    [InlineData("")]
    public void BuiltInFields_DoesNotContainUnknown(string field)
    {
        Assert.False(AppConstants.BuiltInFields.Contains(field));
    }

    #endregion

    #region ErrorLevels

    [Theory]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("error")]
    [InlineData("FATAL")]
    public void ErrorLevels_ContainsExpected(string level)
    {
        Assert.True(AppConstants.ErrorLevels.Contains(level));
    }

    [Theory]
    [InlineData("Info")]
    [InlineData("Warning")]
    [InlineData("Debug")]
    [InlineData("")]
    public void ErrorLevels_DoesNotContainNonError(string level)
    {
        Assert.False(AppConstants.ErrorLevels.Contains(level));
    }

    #endregion

    #region Tab Indices

    [Fact]
    public void TabIndices_AreSequential()
    {
        Assert.Equal(0, AppConstants.TAB_PLC);
        Assert.Equal(1, AppConstants.TAB_APP);
        Assert.Equal(2, AppConstants.TAB_EVENTS);
        Assert.Equal(3, AppConstants.TAB_SCREENSHOTS);
        Assert.Equal(4, AppConstants.TAB_CONFIG);
        Assert.Equal(5, AppConstants.TAB_TERMINALS);
        Assert.Equal(6, AppConstants.TAB_SETUP_INFO);
        Assert.Equal(7, AppConstants.TAB_GLOBALS);
        Assert.Equal(8, AppConstants.TAB_SYSTAB);
        Assert.Equal(9, AppConstants.TAB_CHARTS);
        Assert.Equal(10, AppConstants.TAB_CPR);
        Assert.Equal(11, AppConstants.TAB_STEP_RECORDER);
        Assert.Equal(12, AppConstants.TAB_DIFFERENT_LOGS);
    }

    #endregion

    #region S4StateRegex

    [Theory]
    [InlineData("STATE_STANDBY - Enter ======", "STANDBY", "Enter")]
    [InlineData("STATE_SERVICE - Exit ======", "SERVICE", "Exit")]
    [InlineData("STATE_PRINT - Enter ===", "PRINT", "Enter")]
    [InlineData("state_idle - enter", "idle", "enter")]
    public void S4StateRegex_MatchesValidPatterns(string input, string expectedState, string expectedAction)
    {
        var match = AppConstants.S4StateRegex().Match(input);
        Assert.True(match.Success);
        Assert.Equal(expectedState, match.Groups[1].Value);
        Assert.Equal(expectedAction, match.Groups[2].Value);
    }

    [Theory]
    [InlineData("No state here")]
    [InlineData("STANDBY - Enter")]
    [InlineData("STATE_ - Enter")]
    public void S4StateRegex_DoesNotMatchInvalid(string input)
    {
        var match = AppConstants.S4StateRegex().Match(input);
        if (input == "STATE_ - Enter")
            Assert.False(match.Success); // \w+ requires at least one word char
        else
            Assert.False(match.Success);
    }

    #endregion

    #region FileTimestampRegex

    [Theory]
    [InlineData("log_2024-01-15T14-30-00.zip", "2024", "01", "15", "14", "30", "00")]
    [InlineData("backup_2023-12-31_23-59-59.txt", "2023", "12", "31", "23", "59", "59")]
    public void FileTimestampRegex_MatchesValidTimestamps(string input, string y, string m, string d, string h, string min, string s)
    {
        var match = AppConstants.FileTimestampRegex().Match(input);
        Assert.True(match.Success);
        Assert.Equal(y, match.Groups[1].Value);
        Assert.Equal(m, match.Groups[2].Value);
        Assert.Equal(d, match.Groups[3].Value);
        Assert.Equal(h, match.Groups[4].Value);
        Assert.Equal(min, match.Groups[5].Value);
        Assert.Equal(s, match.Groups[6].Value);
    }

    [Theory]
    [InlineData("no-timestamp-here")]
    [InlineData("2024-01-15")]
    public void FileTimestampRegex_DoesNotMatchInvalid(string input)
    {
        Assert.False(AppConstants.FileTimestampRegex().IsMatch(input));
    }

    #endregion

    #region EscapeSqlBracketId

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has]bracket", "has]]bracket")]
    [InlineData("two]]brackets", "two]]]]brackets")]
    [InlineData("", "")]
    public void EscapeSqlBracketId_EscapesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, AppConstants.EscapeSqlBracketId(input));
    }

    [Fact]
    public void EscapeSqlBracketId_NullReturnsNull()
    {
        Assert.Null(AppConstants.EscapeSqlBracketId(null!));
    }

    #endregion

    #region EscapeSqlIdentifier

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has\"quote", "has\"\"quote")]
    [InlineData("", "")]
    public void EscapeSqlIdentifier_EscapesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, AppConstants.EscapeSqlIdentifier(input));
    }

    [Fact]
    public void EscapeSqlIdentifier_NullReturnsNull()
    {
        Assert.Null(AppConstants.EscapeSqlIdentifier(null!));
    }

    #endregion
}

public class AppPathsTests
{
    [Fact]
    public void Root_ContainsIndiLogs3()
    {
        Assert.Contains("IndiLogs3.0", AppPaths.Root);
    }

    [Fact]
    public void SearchLocations_IsInRoot()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.SearchLocations);
        Assert.EndsWith("search_locations.json", AppPaths.SearchLocations);
    }

    [Fact]
    public void DefaultConfig_IsInRoot()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.DefaultConfig);
        Assert.EndsWith("_defaults.json", AppPaths.DefaultConfig);
    }

    [Fact]
    public void Prefixes_AreCorrect()
    {
        Assert.Equal("config_", AppPaths.ConfigPrefix);
        Assert.Equal("searchprofile_", AppPaths.SearchProfilePrefix);
        Assert.Equal("dbcol_", AppPaths.DbColumnPrefix);
    }

    [Fact]
    public void AllPaths_AreUnderRoot()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.SearchSchedules);
        Assert.StartsWith(AppPaths.Root, AppPaths.GridColumnSettings);
        Assert.StartsWith(AppPaths.Root, AppPaths.DifferentLogsColumnSettings);
        Assert.StartsWith(AppPaths.Root, AppPaths.StripeColumnOrder);
        Assert.StartsWith(AppPaths.Root, AppPaths.SnakeScores);
        Assert.StartsWith(AppPaths.Root, AppPaths.UpdateLog);
        Assert.StartsWith(AppPaths.Root, AppPaths.UpdateCredentials);
    }
}
