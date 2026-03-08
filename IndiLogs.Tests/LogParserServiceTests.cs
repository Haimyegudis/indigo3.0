using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogParserServiceTests
    {
        private static LogEntry MakeEntry(string message, string level = "")
            => new LogEntry { Message = message, Level = level };

        // ════════════════════════════════════════════
        //  Null / empty guard
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_NullEntry_DoesNotThrow()
        {
            LogParserService.ParseLogEntry(null!);
        }

        [Fact]
        public void ParseLogEntry_NullMessage_LeavesFieldsEmpty()
        {
            var entry = new LogEntry { Message = null! };
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
            Assert.Empty(entry.Data);
            Assert.Empty(entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_EmptyMessage_LeavesFieldsEmpty()
        {
            var entry = MakeEntry("");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
            Assert.Empty(entry.Data);
            Assert.Empty(entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_WhitespaceOnlyMessage_LeavesFieldsEmpty()
        {
            // Whitespace is not empty per string.IsNullOrEmpty, so the method proceeds
            // but no regex should match pure whitespace.
            var entry = MakeEntry("   ");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
            Assert.Empty(entry.Data);
            Assert.Empty(entry.Exception);
        }

        // ════════════════════════════════════════════
        //  Pattern extraction
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_PatternColon_ExtractsValue()
        {
            var entry = MakeEntry("Starting operation Pattern: MyPattern more text");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("MyPattern", entry.Pattern);
        }

        [Fact]
        public void ParseLogEntry_PatternEquals_ExtractsValue()
        {
            var entry = MakeEntry("pattern=SomeValue, continue");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("SomeValue", entry.Pattern);
        }

        [Theory]
        [InlineData("PATTERN: UPPER")]
        [InlineData("Pattern: Mixed")]
        [InlineData("pattern: lower")]
        public void ParseLogEntry_PatternColon_CaseInsensitive(string message)
        {
            var entry = MakeEntry(message);
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Pattern));
        }

        [Fact]
        public void ParseLogEntry_NoPatternKeyword_LeavesPatternEmpty()
        {
            var entry = MakeEntry("Just a regular log message without the p-word");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
        }

        [Fact]
        public void ParseLogEntry_PatternWithTrailingWhitespace_IsTrimmed()
        {
            var entry = MakeEntry("Pattern:   TrimMe   ;next");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("TrimMe", entry.Pattern);
        }

        // ════════════════════════════════════════════
        //  Data extraction
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_DataColon_ExtractsValue()
        {
            var entry = MakeEntry("Data: SimpleValue rest");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("SimpleValue", entry.Data);
        }

        [Fact]
        public void ParseLogEntry_DataEquals_ExtractsValue()
        {
            var entry = MakeEntry("data=SomeDataValue here");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("SomeDataValue", entry.Data);
        }

        [Fact]
        public void ParseLogEntry_DataJsonBraces_ExtractsBlock()
        {
            var entry = MakeEntry("Data: {key=value} more stuff");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("{key=value}", entry.Data);
        }

        [Fact]
        public void ParseLogEntry_StandaloneBraces_DoesNotMatchData()
        {
            // Standalone braces without "Data:" prefix do not match
            var entry = MakeEntry("Received {payload=123} from server");
            LogParserService.ParseLogEntry(entry);
            Assert.True(string.IsNullOrEmpty(entry.Data));
        }

        [Fact]
        public void ParseLogEntry_NoDataKeyword_LeavesDataEmpty()
        {
            var entry = MakeEntry("Nothing relevant here at all");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Data);
        }

        [Theory]
        [InlineData("DATA: upper")]
        [InlineData("Data: mixed")]
        [InlineData("data: lower")]
        public void ParseLogEntry_DataColon_CaseInsensitive(string message)
        {
            var entry = MakeEntry(message);
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Data));
        }

        // ════════════════════════════════════════════
        //  Exception extraction — regex paths
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_ExceptionColon_ExtractsMessage()
        {
            var entry = MakeEntry("Exception: Something went wrong at line 42");
            LogParserService.ParseLogEntry(entry);
            Assert.Contains("Something went wrong", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorColon_ExtractsMessage()
        {
            var entry = MakeEntry("Error: disk full at /dev/sda");
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_ExceptionClassName_ExtractsFromClass()
        {
            var entry = MakeEntry("Caught NullReferenceException: Object reference not set");
            LogParserService.ParseLogEntry(entry);
            Assert.Contains("Object reference not set", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ExceptionClassOnly_FallsBackToClassRegex()
        {
            // If the ExceptionRegex fails but ExceptionClassRegex matches
            // (e.g. "SomeException: ..." where the first regex doesn't capture due to "at ")
            var entry = MakeEntry("Unhandled ArgumentException: value at SomeMethod");
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_ExceptionClassLongMessage_ExtractsException()
        {
            string longTail = new string('z', 600);
            var entry = MakeEntry($"Something happened FooException: {longTail}");
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_NoExceptionKeyword_LeavesExceptionEmpty()
        {
            var entry = MakeEntry("All systems operational, nothing to report");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Exception);
        }

        // ════════════════════════════════════════════
        //  Error-level fallback (first 200 chars)
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_ErrorLevelWithFailed_SetsException()
        {
            var entry = MakeEntry("Connection to server failed unexpectedly", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Contains("failed", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelWithErrorKeyword_SetsException()
        {
            var entry = MakeEntry("Something caused an error during startup", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelWithExceptionKeyword_SetsException()
        {
            // Contains "exception" in message but no "Exception:" pattern that regex would catch
            var entry = MakeEntry("Caught an unhandled exception in the main loop", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelLongMessage_TruncatesTo200()
        {
            string longMessage = "Operation failed " + new string('a', 300);
            var entry = MakeEntry(longMessage, "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            Assert.True(entry.Exception.Length <= 203); // 200 + "..."
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelShortMessage_NoTruncation()
        {
            var entry = MakeEntry("Simple failed message", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("Simple failed message", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelNoKeywords_LeavesExceptionEmpty()
        {
            var entry = MakeEntry("This is a perfectly normal log line", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Exception);
        }

        [Theory]
        [InlineData("error")]
        [InlineData("ERROR")]
        [InlineData("Error")]
        public void ParseLogEntry_ErrorLevelCaseInsensitive_Triggers(string level)
        {
            var entry = MakeEntry("Something failed here", level);
            LogParserService.ParseLogEntry(entry);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_WarnLevel_DoesNotTriggerFallback()
        {
            var entry = MakeEntry("Something failed here", "WARN");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_InfoLevel_DoesNotTriggerFallback()
        {
            var entry = MakeEntry("Something failed here", "INFO");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Exception);
        }

        // ════════════════════════════════════════════
        //  Combined field extraction
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_AllThreeFields_Extracted()
        {
            var entry = MakeEntry("Pattern: MyPat Data: {x=1} Exception: Something bad");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("MyPat", entry.Pattern);
            Assert.Equal("{x=1}", entry.Data);
            Assert.False(string.IsNullOrEmpty(entry.Exception));
        }

        [Fact]
        public void ParseLogEntry_PatternAndData_NoException()
        {
            var entry = MakeEntry("Pattern: ABC Data: {y=2} all good");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("ABC", entry.Pattern);
            Assert.Equal("{y=2}", entry.Data);
            Assert.Empty(entry.Exception);
        }

        // ════════════════════════════════════════════
        //  Exception already found prevents fallback
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_ErrorLevelWithExplicitException_DoesNotUseFallback()
        {
            // Exception regex should match, so the error-level fallback should NOT overwrite
            var entry = MakeEntry("Exception: Specific error detail at SomeMethod", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Contains("Specific error detail", entry.Exception);
            // Should not be the 200-char truncation; should be the regex result
            Assert.DoesNotContain("...", entry.Exception);
        }

        // ════════════════════════════════════════════
        //  ParseLogEntriesAsync
        // ════════════════════════════════════════════

        [Fact]
        public async System.Threading.Tasks.Task ParseLogEntriesAsync_NullCollection_DoesNotThrow()
        {
            await LogParserService.ParseLogEntriesAsync(null!);
        }

        [Fact]
        public async System.Threading.Tasks.Task ParseLogEntriesAsync_EmptyList_DoesNotThrow()
        {
            await LogParserService.ParseLogEntriesAsync(new System.Collections.Generic.List<LogEntry>());
        }

        [Fact]
        public async System.Threading.Tasks.Task ParseLogEntriesAsync_MultipleLogs_AllParsed()
        {
            var logs = new System.Collections.Generic.List<LogEntry>
            {
                MakeEntry("Pattern: A Data: {v=1}"),
                MakeEntry("Pattern: B Exception: Boom"),
                MakeEntry("Just text", "Error"),
            };

            await LogParserService.ParseLogEntriesAsync(logs);

            Assert.Equal("A", logs[0].Pattern);
            Assert.Equal("{v=1}", logs[0].Data);
            Assert.Equal("B", logs[1].Pattern);
            Assert.False(string.IsNullOrEmpty(logs[1].Exception));
            Assert.Empty(logs[2].Pattern);
        }

        // ════════════════════════════════════════════
        //  Boundary / regression
        // ════════════════════════════════════════════

        [Fact]
        public void ParseLogEntry_MessageExactly200Chars_ErrorLevel_NoEllipsis()
        {
            string message = "failed" + new string('x', 194); // 6 + 194 = 200
            Assert.Equal(200, message.Length);
            var entry = MakeEntry(message, "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal(message, entry.Exception);
            Assert.DoesNotContain("...", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_MessageExactly201Chars_ErrorLevel_HasEllipsis()
        {
            string message = "failed" + new string('x', 195); // 6 + 195 = 201
            Assert.Equal(201, message.Length);
            var entry = MakeEntry(message, "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.EndsWith("...", entry.Exception);
            Assert.Equal(203, entry.Exception.Length); // 200 + "..."
        }

        [Fact]
        public void ParseLogEntry_PreExistingFieldValues_OverwrittenWhenMatched()
        {
            var entry = new LogEntry
            {
                Message = "Pattern: NewVal Data: {new=1}",
                Level = "INFO",
                Pattern = "OldVal",
                Data = "OldData",
            };
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("NewVal", entry.Pattern);
            Assert.Equal("{new=1}", entry.Data);
        }

        [Fact]
        public void ParseLogEntry_PreExistingFieldValues_KeptWhenNoMatch()
        {
            var entry = new LogEntry
            {
                Message = "Nothing special here",
                Level = "INFO",
                Pattern = "OldVal",
                Data = "OldData",
                Exception = "OldExc",
            };
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("OldVal", entry.Pattern);
            Assert.Equal("OldData", entry.Data);
            Assert.Equal("OldExc", entry.Exception);
        }
    }
}
