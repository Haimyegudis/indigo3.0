using IndiLogs_3._0.Services;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class DiffEngineTests
    {
        private readonly DiffEngine _engine = new DiffEngine();

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
        public void Compare_NullInputs_TreatsAsEmpty()
        {
            var result = _engine.Compare(null, null);

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_OneNull_ReturnsNotEqual()
        {
            var result = _engine.Compare("hello", null);

            Assert.False(result.AreEqual);
        }

        [Fact]
        public void Compare_AddedText_ShowsAddedSegment()
        {
            var result = _engine.Compare("abc", "abXc");

            Assert.False(result.AreEqual);
            // Right side should have an Added segment
            Assert.True(result.RightSegments.Any(s => s.Type == DiffType.Added));
        }

        [Fact]
        public void Compare_RemovedText_ShowsRemovedSegment()
        {
            var result = _engine.Compare("abXc", "abc");

            Assert.False(result.AreEqual);
            // Left side should have a Removed segment
            Assert.True(result.LeftSegments.Any(s => s.Type == DiffType.Removed));
        }

        [Fact]
        public void Compare_EmptyStrings_ReturnsEqual()
        {
            var result = _engine.Compare("", "");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_LongStrings_HandlesGracefully()
        {
            // Test with strings exceeding the 500-char MaxDiffLength
            string longA = new string('a', 600) + "X";
            string longB = new string('a', 600) + "Y";

            var result = _engine.Compare(longA, longB);

            Assert.False(result.AreEqual);
            // Should not throw and should produce segments
            Assert.NotEmpty(result.LeftSegments);
            Assert.NotEmpty(result.RightSegments);
        }

        [Fact]
        public void IgnoreMaskPattern_ValidRegex_AppliesMask()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            Assert.True(_engine.IsMaskPatternValid);

            string masked = _engine.ApplyMask("error 123 at line 456");
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
        public void ApplyMask_NullText_ReturnsNull()
        {
            _engine.IgnoreMaskPattern = @"\d+";
            string result = _engine.ApplyMask(null);
            Assert.Null(result);
        }

        [Fact]
        public void Compare_WithMask_IgnoresMaskedContent()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            // These differ only in numbers, which should be masked
            var result = _engine.Compare("error 123", "error 456");

            Assert.True(result.AreEqual);
        }

        [Fact]
        public void Compare_WithMask_DetectsRealDifferences()
        {
            _engine.IgnoreMaskPattern = @"\d+";

            // These differ in non-numeric content
            var result = _engine.Compare("error 123 at foo", "warning 456 at bar");

            Assert.False(result.AreEqual);
        }
    }
}
