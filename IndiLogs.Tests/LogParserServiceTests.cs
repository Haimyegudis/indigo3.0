using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogParserServiceTests
    {
        private LogEntry MakeEntry(string message, string level = "")
        {
            return new LogEntry { Message = message, Level = level };
        }

        // ── Pattern extraction ──

        [Fact]
        public void ParseLogEntry_ExtractsPattern_ColonSyntax()
        {
            var entry = MakeEntry("Starting operation Pattern: MyPattern more text");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("MyPattern", entry.Pattern);
        }

        [Fact]
        public void ParseLogEntry_ExtractsPattern_EqualsSyntax()
        {
            var entry = MakeEntry("pattern=SomeValue, continue");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("SomeValue", entry.Pattern);
        }

        [Fact]
        public void ParseLogEntry_NoPattern_LeavesEmpty()
        {
            var entry = MakeEntry("Just a regular log message");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
        }

        // ── Data extraction ──

        [Fact]
        public void ParseLogEntry_ExtractsData_JsonBraces()
        {
            var entry = MakeEntry("Data: {key=value} more stuff");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("{key=value}", entry.Data);
        }

        [Fact]
        public void ParseLogEntry_ExtractsData_EqualsSyntax()
        {
            var entry = MakeEntry("data=SomeDataValue here");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("SomeDataValue", entry.Data);
        }

        // ── Exception extraction ──

        [Fact]
        public void ParseLogEntry_ExtractsException_ColonSyntax()
        {
            var entry = MakeEntry("Exception: Something went wrong at line 42");
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            Assert.Contains("Something went wrong", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ExtractsException_ClassNameSyntax()
        {
            // ExceptionRegex matches "Exception:" within "NullReferenceException:" and captures what follows
            var entry = MakeEntry("Caught NullReferenceException: Object reference not set");
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            Assert.Contains("Object reference not set", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelWithFailedKeyword_SetsException()
        {
            var entry = MakeEntry("Connection to server failed unexpectedly", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            Assert.Contains("failed", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorLevelNoFailKeyword_NoException()
        {
            var entry = MakeEntry("This is a normal info log", "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Exception);
        }

        // ── Edge cases ──

        [Fact]
        public void ParseLogEntry_NullEntry_NoException()
        {
            LogParserService.ParseLogEntry(null);
            // Should not throw
        }

        [Fact]
        public void ParseLogEntry_NullMessage_NoException()
        {
            var entry = new LogEntry { Message = null };
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
            Assert.Empty(entry.Data);
            Assert.Empty(entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_EmptyMessage_NoException()
        {
            var entry = MakeEntry("");
            LogParserService.ParseLogEntry(entry);
            Assert.Empty(entry.Pattern);
        }

        [Fact]
        public void ParseLogEntry_LongExceptionClass_ExtractsFromRegex()
        {
            // ExceptionRegex captures the part after "Exception:" — no 500-char limit on this path
            string longMessage = "NullReferenceException: " + new string('x', 600);
            var entry = MakeEntry(longMessage);
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            // The captured group is the long string of x's
            Assert.Contains("xxx", entry.Exception);
        }

        [Fact]
        public void ParseLogEntry_ErrorLevel_LongMessage_TruncatesTo200()
        {
            string longMessage = "Connection error " + new string('a', 300);
            var entry = MakeEntry(longMessage, "Error");
            LogParserService.ParseLogEntry(entry);
            Assert.NotNull(entry.Exception);
            Assert.True(entry.Exception.Length <= 204); // 200 + "..."
        }

        [Fact]
        public void ParseLogEntry_MultipleFields_AllExtracted()
        {
            var entry = MakeEntry("Pattern: MyPat Data: {x=1} Exception: Something bad");
            LogParserService.ParseLogEntry(entry);
            Assert.Equal("MyPat", entry.Pattern);
            Assert.Equal("{x=1}", entry.Data);
            Assert.NotNull(entry.Exception);
        }
    }
}
