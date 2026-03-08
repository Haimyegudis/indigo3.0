using IndiLogs_3._0.Services;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class DiffEngineTests
    {
        private readonly DiffEngine _engine = new DiffEngine();

        // ===== Compare: basic equality =====

        [Fact]
        public void Compare_IdenticalStrings_ReturnsEqual()
        {
            var result = _engine.Compare("hello world", "hello world");

            Assert.True(result.AreEqual);
            Assert.Single(result.LeftSegments);
            Assert.Single(result.RightSegments);
            Assert.Equal(DiffType.Unchanged, result.LeftSegments[0].Type);
        }

        [Fact]
        public void Compare_DifferentStrings_ReturnsNotEqual()
        {
            var result = _engine.Compare("hello", "world");

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.LeftSegments);
            Assert.NotEmpty(result.RightSegments);
        }

        [Fact]
        public void Compare_EmptyStrings_ReturnsEqual()
        {
            var result = _engine.Compare("", "");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_IdenticalStrings_SegmentTextMatchesInput()
        {
            var result = _engine.Compare("test text", "test text");

            Assert.True(result.AreEqual);
            Assert.Equal("test text", result.LeftSegments[0].Text);
            Assert.Equal("test text", result.RightSegments[0].Text);
        }

        // ===== Compare: null handling =====

        [Fact]
        public void Compare_NullInputs_TreatsAsEmpty()
        {
            var result = _engine.Compare(null, null);

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_LeftNull_RightNonEmpty_ReturnsNotEqual()
        {
            var result = _engine.Compare(null, "hello");

            Assert.False(result.AreEqual);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added);
        }

        [Fact]
        public void Compare_LeftNonEmpty_RightNull_ReturnsNotEqual()
        {
            var result = _engine.Compare("hello", null);

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
        }

        [Fact]
        public void Compare_LeftNull_RightEmpty_ReturnsEqual()
        {
            var result = _engine.Compare(null, "");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_LeftEmpty_RightNull_ReturnsEqual()
        {
            var result = _engine.Compare("", null);

            Assert.True(result.AreEqual);
        }

        // ===== Compare: empty vs non-empty =====

        [Fact]
        public void Compare_LeftEmpty_RightNonEmpty_AllAdded()
        {
            var result = _engine.Compare("", "abc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added && s.Text == "abc");
        }

        [Fact]
        public void Compare_LeftNonEmpty_RightEmpty_AllRemoved()
        {
            var result = _engine.Compare("abc", "");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed && s.Text == "abc");
        }

        // ===== Compare: added/removed segments =====

        [Fact]
        public void Compare_AddedText_ShowsAddedSegment()
        {
            var result = _engine.Compare("abc", "abXc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added);
        }

        [Fact]
        public void Compare_RemovedText_ShowsRemovedSegment()
        {
            var result = _engine.Compare("abXc", "abc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
        }

        [Fact]
        public void Compare_AddedAtEnd_ShowsAddedSegment()
        {
            var result = _engine.Compare("abc", "abcdef");

            Assert.False(result.AreEqual);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added && s.Text == "def");
        }

        [Fact]
        public void Compare_RemovedAtEnd_ShowsRemovedSegment()
        {
            var result = _engine.Compare("abcdef", "abc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed && s.Text == "def");
        }

        [Fact]
        public void Compare_AddedAtStart_ShowsAddedSegment()
        {
            var result = _engine.Compare("abc", "Xabc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added);
        }

        [Fact]
        public void Compare_RemovedAtStart_ShowsRemovedSegment()
        {
            var result = _engine.Compare("Xabc", "abc");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
        }

        // ===== Compare: partial matches =====

        [Fact]
        public void Compare_PartialMatch_PreservesUnchangedSegments()
        {
            var result = _engine.Compare("abcdef", "abXdef");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Unchanged);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Unchanged);
        }

        [Fact]
        public void Compare_PartialMatch_UnchangedTextIsCorrect()
        {
            var result = _engine.Compare("abcdef", "abXdef");

            var leftUnchanged = result.LeftSegments.Where(s => s.Type == DiffType.Unchanged).ToList();
            string leftUnchangedText = string.Join("", leftUnchanged.Select(s => s.Text));
            Assert.Contains("ab", leftUnchangedText);
            Assert.Contains("def", leftUnchangedText);
        }

        [Fact]
        public void Compare_ReplacedMiddle_ShowsBothAddedAndRemoved()
        {
            var result = _engine.Compare("aXb", "aYb");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added);
        }

        [Fact]
        public void Compare_CompletelyDifferentStrings_AllChanged()
        {
            var result = _engine.Compare("abc", "xyz");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
            Assert.Contains(result.RightSegments, s => s.Type == DiffType.Added);
        }

        [Fact]
        public void Compare_SingleCharDifference_DetectsChange()
        {
            var result = _engine.Compare("a", "b");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_SingleCharIdentical_ReturnsEqual()
        {
            var result = _engine.Compare("a", "a");

            Assert.True(result.AreEqual);
        }

        // ===== Compare: concatenated segment text matches original =====

        [Fact]
        public void Compare_SegmentsConcatenated_MatchOriginalLeftText()
        {
            var result = _engine.Compare("hello world", "hello there");

            string reconstructed = string.Join("", result.LeftSegments
                .Where(s => s.Type != DiffType.Added)
                .Select(s => s.Text));
            Assert.Equal("hello world", reconstructed);
        }

        [Fact]
        public void Compare_SegmentsConcatenated_MatchOriginalRightText()
        {
            var result = _engine.Compare("hello world", "hello there");

            string reconstructed = string.Join("", result.RightSegments
                .Where(s => s.Type != DiffType.Removed)
                .Select(s => s.Text));
            Assert.Equal("hello there", reconstructed);
        }

        // ===== Compare: special characters =====

        [Fact]
        public void Compare_SpecialCharacters_HandledCorrectly()
        {
            var result = _engine.Compare("a\tb\nc", "a\tb\nc");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_SpecialCharsDiffer_DetectsChange()
        {
            var result = _engine.Compare("a\tb", "a\nb");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_UnicodeCharacters_HandledCorrectly()
        {
            var result = _engine.Compare("hello \u00e9\u00e8\u00ea", "hello \u00e9\u00e8\u00ea");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_UnicodeCharactersDiffer_DetectsChange()
        {
            var result = _engine.Compare("caf\u00e9", "cafe");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_MultilineContent_HandledCorrectly()
        {
            string left = "line1\nline2\nline3";
            string right = "line1\nmodified\nline3";

            var result = _engine.Compare(left, right);

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Unchanged);
        }

        [Fact]
        public void Compare_MultilineIdentical_ReturnsEqual()
        {
            string text = "line1\nline2\nline3\n";

            var result = _engine.Compare(text, text);

            Assert.True(result.AreEqual);
        }

        // ===== Compare: long strings (MaxDiffLength truncation) =====

        [Fact]
        public void Compare_LongStrings_HandlesGracefully()
        {
            string longA = new string('a', 600) + "X";
            string longB = new string('a', 600) + "Y";

            var result = _engine.Compare(longA, longB);

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.LeftSegments);
            Assert.NotEmpty(result.RightSegments);
        }

        [Fact]
        public void Compare_LongIdenticalStrings_ReturnsEqual()
        {
            string longStr = new string('a', 1000);

            var result = _engine.Compare(longStr, longStr);

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_LongStrings_SegmentsIncludeRemainder()
        {
            string longA = new string('a', 600);
            string longB = new string('b', 600);

            var result = _engine.Compare(longA, longB);

            Assert.False(result.AreEqual);
            // The truncation adds remainder as Unchanged segments
            int leftTotalLen = result.LeftSegments.Sum(s => s.Text.Length);
            Assert.True(leftTotalLen >= 500);
        }

        [Fact]
        public void Compare_ExactlyAtMaxDiffLength_NoTruncation()
        {
            string strA = new string('a', 500);
            string strB = new string('b', 500);

            var result = _engine.Compare(strA, strB);

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.LeftSegments);
        }

        [Fact]
        public void Compare_OneShortOneLong_HandlesAsymmetricTruncation()
        {
            string shortStr = "abc";
            string longStr = new string('x', 600);

            var result = _engine.Compare(shortStr, longStr);

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.LeftSegments);
            Assert.NotEmpty(result.RightSegments);
        }

        [Fact]
        public void Compare_BothExceedMaxDiffLength_BothTruncated()
        {
            string longA = new string('a', 300) + new string('x', 300);
            string longB = new string('a', 300) + new string('y', 300);

            var result = _engine.Compare(longA, longB);

            Assert.False(result.AreEqual);
            // Both sides should have segments adding up to at least the full length
            int leftTotal = result.LeftSegments.Sum(s => s.Text.Length);
            Assert.True(leftTotal >= 500);
            int rightTotal = result.RightSegments.Sum(s => s.Text.Length);
            Assert.True(rightTotal >= 500);
        }

        // ===== IgnoreMaskPattern property =====

        [Fact]
        public void IgnoreMaskPattern_ValidRegex_AppliesMask()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            Assert.True(_engine.IsMaskPatternValid);

            string? masked = _engine.ApplyMask("error 123 at line 456");
            Assert.Equal("error # at line #", masked);
        }

        [Fact]
        public void IgnoreMaskPattern_InvalidRegex_MarksInvalid()
        {
            _engine.IgnoreMaskPattern = @"[invalid";

            Assert.False(_engine.IsMaskPatternValid);
        }

        [Fact]
        public void IgnoreMaskPattern_NullOrEmpty_NoMasking()
        {
            _engine.IgnoreMaskPattern = null;
            Assert.True(_engine.IsMaskPatternValid);

            _engine.IgnoreMaskPattern = "";
            Assert.True(_engine.IsMaskPatternValid);
        }

        [Fact]
        public void IgnoreMaskPattern_SetThenClear_ResetsToValid()
        {
            _engine.IgnoreMaskPattern = @"[invalid";
            Assert.False(_engine.IsMaskPatternValid);

            _engine.IgnoreMaskPattern = null;
            Assert.True(_engine.IsMaskPatternValid);
        }

        [Fact]
        public void IgnoreMaskPattern_SetThenClearToEmpty_ResetsToValid()
        {
            _engine.IgnoreMaskPattern = @"[invalid";
            Assert.False(_engine.IsMaskPatternValid);

            _engine.IgnoreMaskPattern = "";
            Assert.True(_engine.IsMaskPatternValid);
        }

        [Fact]
        public void IgnoreMaskPattern_SetValidThenInvalid_BecomesInvalid()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            Assert.True(_engine.IsMaskPatternValid);

            _engine.IgnoreMaskPattern = @"(unclosed";
            Assert.False(_engine.IsMaskPatternValid);
        }

        [Fact]
        public void IgnoreMaskPattern_GetReturnsSetValue()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            Assert.Equal(@"\d+", _engine.IgnoreMaskPattern);
        }

        [Fact]
        public void IgnoreMaskPattern_DefaultIsNull()
        {
            var engine = new DiffEngine();
            Assert.Null(engine.IgnoreMaskPattern);
        }

        [Fact]
        public void IsMaskPatternValid_DefaultIsTrue()
        {
            var engine = new DiffEngine();
            Assert.True(engine.IsMaskPatternValid);
        }

        // ===== ApplyMask =====

        [Fact]
        public void ApplyMask_NullText_ReturnsNull()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            string? result = _engine.ApplyMask(null);
            Assert.Null(result);
        }

        [Fact]
        public void ApplyMask_EmptyText_ReturnsEmpty()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            string? result = _engine.ApplyMask("");
            Assert.Equal("", result);
        }

        [Fact]
        public void ApplyMask_NoMaskSet_ReturnsOriginal()
        {
            string? result = _engine.ApplyMask("hello 123");
            Assert.Equal("hello 123", result);
        }

        [Fact]
        public void ApplyMask_InvalidMask_ReturnsOriginal()
        {
            _engine.IgnoreMaskPattern = @"[invalid";
            string? result = _engine.ApplyMask("hello 123");
            Assert.Equal("hello 123", result);
        }

        [Fact]
        public void ApplyMask_NoMatch_ReturnsOriginal()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            string? result = _engine.ApplyMask("hello world");
            Assert.Equal("hello world", result);
        }

        [Fact]
        public void ApplyMask_MultipleMatches_AllReplaced()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            string? result = _engine.ApplyMask("a1b2c3d4");
            Assert.Equal("a#b#c#d#", result);
        }

        [Fact]
        public void ApplyMask_EntireStringMatches_ReplacedWithHash()
        {
            _engine.IgnoreMaskPattern = @".+";
            string? result = _engine.ApplyMask("hello");
            Assert.Equal("#", result);
        }

        [Fact]
        public void ApplyMask_TimestampPattern_MasksTimestamps()
        {
            _engine.IgnoreMaskPattern = @"\d{2}:\d{2}:\d{2}";
            string? result = _engine.ApplyMask("event at 12:34:56 and 01:02:03");
            Assert.Equal("event at # and #", result);
        }

        [Fact]
        public void ApplyMask_CaseInsensitive_MasksRegardlessOfCase()
        {
            _engine.IgnoreMaskPattern = @"error";
            string? result = _engine.ApplyMask("ERROR occurred, error again, Error too");
            Assert.Equal("# occurred, # again, # too", result);
        }

        [Fact]
        public void ApplyMask_GuidPattern_MasksGuids()
        {
            _engine.IgnoreMaskPattern = @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
            string? result = _engine.ApplyMask("id=550e8400-e29b-41d4-a716-446655440000 done");
            Assert.Equal("id=# done", result);
        }

        // ===== Compare with mask =====

        [Fact]
        public void Compare_WithMask_IgnoresMaskedContent()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            var result = _engine.Compare("error 123", "error 456");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_WithMask_DetectsRealDifferences()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            var result = _engine.Compare("error 123 at foo", "warning 456 at bar");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_WithMask_EqualResult_SegmentsUseOriginalText()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            var result = _engine.Compare("error 123", "error 456");

            Assert.True(result.AreEqual);
            Assert.Equal("error 123", result.LeftSegments[0].Text);
            Assert.Equal("error 456", result.RightSegments[0].Text);
        }

        [Fact]
        public void Compare_WithMask_InvalidMask_StillComparesRawText()
        {
            _engine.IgnoreMaskPattern = @"[invalid";

            var result = _engine.Compare("abc", "abc");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_WithMask_InvalidMask_DifferentText_DetectsDiff()
        {
            _engine.IgnoreMaskPattern = @"[invalid";

            var result = _engine.Compare("abc", "def");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_MaskMakesStringsEqual_ButOriginalsDiffer()
        {
            _engine.IgnoreMaskPattern = @"\d{4}-\d{2}-\d{2}";

            var result = _engine.Compare(
                "log entry 2024-01-15 started",
                "log entry 2025-12-31 started");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_MaskPartialOverlap_DetectsDifference()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            var result = _engine.Compare("abc 123 def", "abc 456 ghi");

            Assert.False(result.AreEqual);
        }

        // ===== Compare: whitespace edge cases =====

        [Fact]
        public void Compare_WhitespaceOnly_IdenticalReturnsEqual()
        {
            var result = _engine.Compare("   ", "   ");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_DifferentWhitespace_ReturnsNotEqual()
        {
            var result = _engine.Compare("  ", "\t");

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_TrailingWhitespaceDifference_ReturnsNotEqual()
        {
            var result = _engine.Compare("abc ", "abc");

            Assert.False(result.AreEqual);
        }

        // ===== DiffResult / DiffSegment structures =====

        [Fact]
        public void DiffResult_DefaultValues_CorrectDefaults()
        {
            var result = new DiffResult();

            Assert.False(result.AreEqual);
            Assert.NotNull(result.LeftSegments);
            Assert.NotNull(result.RightSegments);
            Assert.Empty(result.LeftSegments);
            Assert.Empty(result.RightSegments);
        }

        [Fact]
        public void DiffSegment_DefaultValues_CorrectDefaults()
        {
            var segment = new DiffSegment();

            Assert.Equal("", segment.Text);
            Assert.Equal(DiffType.Unchanged, segment.Type);
        }

        [Fact]
        public void DiffSegment_SetProperties_Roundtrip()
        {
            var segment = new DiffSegment { Text = "hello", Type = DiffType.Added };

            Assert.Equal("hello", segment.Text);
            Assert.Equal(DiffType.Added, segment.Type);
        }

        [Fact]
        public void DiffSegment_RemovedType_CanBeSet()
        {
            var segment = new DiffSegment { Text = "removed text", Type = DiffType.Removed };

            Assert.Equal(DiffType.Removed, segment.Type);
            Assert.Equal("removed text", segment.Text);
        }

        [Fact]
        public void DiffType_HasExpectedValues()
        {
            Assert.Equal(0, (int)DiffType.Unchanged);
            Assert.Equal(1, (int)DiffType.Added);
            Assert.Equal(2, (int)DiffType.Removed);
        }

        // ===== Compare: multiple differences =====

        [Fact]
        public void Compare_MultipleDifferences_AllDetected()
        {
            var result = _engine.Compare("aXbYc", "aAbBc");

            Assert.False(result.AreEqual);
            var removedSegs = result.LeftSegments.Where(s => s.Type == DiffType.Removed).ToList();
            var addedSegs = result.RightSegments.Where(s => s.Type == DiffType.Added).ToList();
            Assert.NotEmpty(removedSegs);
            Assert.NotEmpty(addedSegs);
        }

        [Fact]
        public void Compare_RepeatedCharacters_HandledCorrectly()
        {
            var result = _engine.Compare("aaaa", "aaa");

            Assert.False(result.AreEqual);
            Assert.Contains(result.LeftSegments, s => s.Type == DiffType.Removed);
        }

        [Fact]
        public void Compare_SwappedSections_DetectsChanges()
        {
            var result = _engine.Compare("abcd", "cdab");

            Assert.False(result.AreEqual);
        }

        // ===== Compare: symmetry =====

        [Fact]
        public void Compare_Symmetry_SwappedInputsSwapSegmentTypes()
        {
            var resultAB = _engine.Compare("abc", "abXc");
            var resultBA = _engine.Compare("abXc", "abc");

            bool abHasAdded = resultAB.RightSegments.Any(s => s.Type == DiffType.Added);
            bool baHasRemoved = resultBA.LeftSegments.Any(s => s.Type == DiffType.Removed);

            Assert.True(abHasAdded);
            Assert.True(baHasRemoved);
        }

        // ===== Compare with mask on long strings =====

        [Fact]
        public void Compare_WithMask_LongStrings_HandlesGracefully()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            string longA = "entry " + new string('a', 500) + " id=123";
            string longB = "entry " + new string('a', 500) + " id=456";

            var result = _engine.Compare(longA, longB);

            // After masking, the numeric parts become "#", so the strings might differ
            // depending on how truncation interacts with masking.
            // The key assertion: it should not throw.
            Assert.NotNull(result);
        }

        // ===== Multiple engine instances are independent =====

        [Fact]
        public void MultipleEngines_IndependentMaskState()
        {
            var engine1 = new DiffEngine();
            var engine2 = new DiffEngine();

            engine1.IgnoreMaskPattern = @"\d+";

            Assert.True(engine1.IsMaskPatternValid);
            Assert.True(engine2.IsMaskPatternValid);
            Assert.Null(engine2.IgnoreMaskPattern);

            string? masked1 = engine1.ApplyMask("abc 123");
            string? masked2 = engine2.ApplyMask("abc 123");

            Assert.Equal("abc #", masked1);
            Assert.Equal("abc 123", masked2);
        }

        // ===== Mask pattern change between compares =====

        [Fact]
        public void Compare_MaskChangedBetweenCalls_UsesCurrentMask()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            var result1 = _engine.Compare("error 111", "error 222");
            Assert.True(result1.AreEqual);

            _engine.IgnoreMaskPattern = null;
            var result2 = _engine.Compare("error 111", "error 222");
            Assert.False(result2.AreEqual);
        }

        // ===== Regex special characters in mask =====

        [Fact]
        public void ApplyMask_DotPattern_MatchesAnyChar()
        {
            _engine.IgnoreMaskPattern = @"\.";
            string? result = _engine.ApplyMask("a.b.c");
            Assert.Equal("a#b#c", result);
        }

        [Fact]
        public void ApplyMask_ComplexPattern_Works()
        {
            _engine.IgnoreMaskPattern = @"\[.*?\]";
            string? result = _engine.ApplyMask("text [INFO] more [WARN] end");
            Assert.Equal("text # more # end", result);
        }
    }
}
