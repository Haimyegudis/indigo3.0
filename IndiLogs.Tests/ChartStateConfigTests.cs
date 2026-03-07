using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models.Charts;
using SkiaSharp;
using Xunit;

namespace IndiLogs.Tests
{
    public class ChartStateConfigTests
    {
        // ── GetColor ────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(18)]
        public void GetColor_KnownStateId_ReturnsSemiTransparentColor(int stateId)
        {
            var color = ChartStateConfig.GetColor(stateId);

            Assert.Equal(60, color.Alpha);
            var expected = ChartStateConfig.StateColors[stateId];
            Assert.Equal(expected.Red, color.Red);
            Assert.Equal(expected.Green, color.Green);
            Assert.Equal(expected.Blue, color.Blue);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(19)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void GetColor_UnknownStateId_ReturnsTransparent(int stateId)
        {
            var color = ChartStateConfig.GetColor(stateId);

            Assert.Equal(SKColors.Transparent, color);
        }

        // ── GetSolidColor ───────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        [InlineData(10)]
        [InlineData(18)]
        public void GetSolidColor_KnownStateId_ReturnsFullOpacityColor(int stateId)
        {
            var color = ChartStateConfig.GetSolidColor(stateId);

            var expected = ChartStateConfig.StateColors[stateId];
            Assert.Equal(expected, color);
            Assert.Equal(255, color.Alpha);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(19)]
        [InlineData(999)]
        public void GetSolidColor_UnknownStateId_ReturnsGray(int stateId)
        {
            var color = ChartStateConfig.GetSolidColor(stateId);

            Assert.Equal(SKColors.Gray, color);
        }

        // ── GetName ─────────────────────────────────────────────────

        [Theory]
        [InlineData(0, "UNDEFINED")]
        [InlineData(1, "INIT")]
        [InlineData(3, "OFF")]
        [InlineData(6, "STANDBY")]
        [InlineData(8, "READY")]
        [InlineData(10, "PRINT")]
        [InlineData(12, "PAUSE")]
        [InlineData(13, "RECOVERY")]
        [InlineData(18, "DYNAMIC_READY")]
        public void GetName_KnownStateId_ReturnsExpectedName(int stateId, string expectedName)
        {
            var name = ChartStateConfig.GetName(stateId);

            Assert.Equal(expectedName, name);
        }

        [Fact]
        public void GetName_AllDefinedStates_ReturnNonEmptyStrings()
        {
            for (int i = 0; i <= 18; i++)
            {
                var name = ChartStateConfig.GetName(i);
                Assert.False(string.IsNullOrEmpty(name));
            }
        }

        [Theory]
        [InlineData(-1, "-1")]
        [InlineData(19, "19")]
        [InlineData(100, "100")]
        public void GetName_UnknownStateId_ReturnsIdAsString(int stateId, string expected)
        {
            var name = ChartStateConfig.GetName(stateId);

            Assert.Equal(expected, name);
        }

        // ── GetId — numeric input ───────────────────────────────────

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("10", 10)]
        [InlineData("18", 18)]
        [InlineData("99", 99)]
        [InlineData("-5", -5)]
        public void GetId_NumericString_ReturnsParsedInt(string raw, int expected)
        {
            Assert.Equal(expected, ChartStateConfig.GetId(raw));
        }

        // ── GetId — exact name match ────────────────────────────────

        [Theory]
        [InlineData("PRINT", 10)]
        [InlineData("OFF", 3)]
        [InlineData("STANDBY", 6)]
        [InlineData("INIT", 1)]
        [InlineData("DYNAMIC_READY", 18)]
        public void GetId_ExactUpperCaseName_ReturnsCorrectId(string raw, int expected)
        {
            Assert.Equal(expected, ChartStateConfig.GetId(raw));
        }

        [Theory]
        [InlineData("print", 10)]
        [InlineData("Print", 10)]
        [InlineData("off", 3)]
        [InlineData("standby", 6)]
        [InlineData("dynamic_ready", 18)]
        public void GetId_CaseInsensitiveName_ReturnsCorrectId(string raw, int expected)
        {
            Assert.Equal(expected, ChartStateConfig.GetId(raw));
        }

        [Theory]
        [InlineData("  PRINT  ", 10)]
        [InlineData("  off ", 3)]
        [InlineData("\tSTANDBY\t", 6)]
        public void GetId_NameWithWhitespace_ReturnsCorrectId(string raw, int expected)
        {
            Assert.Equal(expected, ChartStateConfig.GetId(raw));
        }

        // ── GetId — partial match ───────────────────────────────────

        [Theory]
        [InlineData("State: PRINT active", 10)]
        [InlineData("going to STANDBY mode", 6)]
        [InlineData("RECOVERY in progress", 13)]
        public void GetId_PartialMatch_ReturnsFirstMatchingStateId(string raw, int expected)
        {
            Assert.Equal(expected, ChartStateConfig.GetId(raw));
        }

        // ── GetId — null / empty / whitespace ───────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void GetId_NullOrWhitespace_ReturnsZero(string? raw)
        {
            Assert.Equal(0, ChartStateConfig.GetId(raw!));
        }

        // ── GetId — unrecognized string ─────────────────────────────

        [Theory]
        [InlineData("UNKNOWN_STATE")]
        [InlineData("xyz")]
        [InlineData("!!!")]
        public void GetId_UnrecognizedString_ReturnsZero(string raw)
        {
            Assert.Equal(0, ChartStateConfig.GetId(raw));
        }

        // ── Static dictionaries consistency ─────────────────────────

        [Fact]
        public void StateNames_And_StateColors_CoverSameKeys()
        {
            Assert.Equal(
                ChartStateConfig.StateNames.Keys.ToList().OrderBy(k => k),
                ChartStateConfig.StateColors.Keys.ToList().OrderBy(k => k));
        }

        [Fact]
        public void StateNameToId_IsReverseMappingOfStateNames()
        {
            foreach (var kvp in ChartStateConfig.StateNames)
            {
                Assert.True(ChartStateConfig.StateNameToId.ContainsKey(kvp.Value));
                Assert.Equal(kvp.Key, ChartStateConfig.StateNameToId[kvp.Value]);
            }
        }

        [Fact]
        public void StateNames_Contains19Entries()
        {
            Assert.Equal(19, ChartStateConfig.StateNames.Count);
        }
    }
}
