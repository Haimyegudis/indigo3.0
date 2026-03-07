using IndiLogs_3._0.Models;
using Xunit;

namespace IndiLogs.Tests
{
    public class IndigoStripeEntryTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var entry = new IndigoStripeEntry();

            Assert.Equal("", entry.StripeType);
            Assert.Equal("", entry.InkName);
            Assert.Equal("", entry.HvTarget);
            Assert.Equal("", entry.SpmStatus);
            Assert.Equal("", entry.SpmScanDirection);
            Assert.Equal("", entry.SpmMeasureMode);
            Assert.Equal("", entry.IlsScanMode);
            Assert.Equal("", entry.DataTransferControl);
        }

        [Fact]
        public void StationStatus_Active()
        {
            var entry = new IndigoStripeEntry { IsStationActive = true };
            Assert.Equal("Active", entry.StationStatus);
        }

        [Fact]
        public void StationStatus_Inactive()
        {
            var entry = new IndigoStripeEntry { IsStationActive = false };
            Assert.Equal("Inactive", entry.StationStatus);
        }

        [Fact]
        public void DisplayInk_ReturnsIdAsString()
        {
            var entry = new IndigoStripeEntry { InkId = 42 };
            Assert.Equal("42", entry.DisplayInk);
        }

        [Theory]
        [InlineData(1500, 1.5)]
        [InlineData(0, 0)]
        [InlineData(123456, 123.46)]
        public void StartPosMm_ConvertsFromMicrometers(int um, double expectedMm)
        {
            var entry = new IndigoStripeEntry { StartPosUm = um };
            Assert.Equal(expectedMm, entry.StartPosMm);
        }

        [Theory]
        [InlineData(2500, 2.5)]
        [InlineData(0, 0)]
        public void EndPosMm_ConvertsFromMicrometers(int um, double expectedMm)
        {
            var entry = new IndigoStripeEntry { EndPosUm = um };
            Assert.Equal(expectedMm, entry.EndPosMm);
        }

        [Fact]
        public void StartPosInBlanketLoopMm_ConvertsFromMicrometers()
        {
            var entry = new IndigoStripeEntry { StartPosInBlanketLoopUm = 5000 };
            Assert.Equal(5.0, entry.StartPosInBlanketLoopMm);
        }

        [Fact]
        public void BlanketLoopRepeatLenMm_ConvertsFromMicrometers()
        {
            var entry = new IndigoStripeEntry { BlanketLoopRepeatLenUm = 3000 };
            Assert.Equal(3.0, entry.BlanketLoopRepeatLenMm);
        }

        [Fact]
        public void IlsScanLenMm_ConvertsFromMicrometers()
        {
            var entry = new IndigoStripeEntry { IlsScanLenUm = 7500 };
            Assert.Equal(7.5, entry.IlsScanLenMm);
        }

        [Fact]
        public void IsNullStripe_TrueForNullGap()
        {
            var entry = new IndigoStripeEntry { StripeType = "Null-Gap" };
            Assert.True(entry.IsNullStripe);
        }

        [Fact]
        public void IsNullStripe_FalseForPrintImage()
        {
            var entry = new IndigoStripeEntry { StripeType = "Print-Image" };
            Assert.False(entry.IsNullStripe);
        }

        [Theory]
        [InlineData("HV_TO_VJOS", true)]
        [InlineData("HV_TO_PRINT", false)]
        [InlineData("HV_TO_NULL", false)]
        public void IsVjosTarget_MatchesExpected(string hvTarget, bool expected)
        {
            var entry = new IndigoStripeEntry { HvTarget = hvTarget };
            Assert.Equal(expected, entry.IsVjosTarget);
        }

        [Theory]
        [InlineData("HV_TO_PRINT", true)]
        [InlineData("HV_TO_VJOS", false)]
        public void IsPrintTarget_MatchesExpected(string hvTarget, bool expected)
        {
            var entry = new IndigoStripeEntry { HvTarget = hvTarget };
            Assert.Equal(expected, entry.IsPrintTarget);
        }

        [Theory]
        [InlineData("HV_TO_NULL", true)]
        [InlineData("HV_TO_PRINT", false)]
        public void IsNullTarget_MatchesExpected(string hvTarget, bool expected)
        {
            var entry = new IndigoStripeEntry { HvTarget = hvTarget };
            Assert.Equal(expected, entry.IsNullTarget);
        }

        [Fact]
        public void IsHvMismatch_NullStripeWithNonNullTarget()
        {
            var entry = new IndigoStripeEntry { StripeType = "Null-Gap", HvTarget = "HV_TO_PRINT" };
            Assert.True(entry.IsHvMismatch);
        }

        [Fact]
        public void IsHvMismatch_NullStripeWithNullTarget_NoMismatch()
        {
            var entry = new IndigoStripeEntry { StripeType = "Null-Gap", HvTarget = "HV_TO_NULL" };
            Assert.False(entry.IsHvMismatch);
        }

        [Fact]
        public void IsHvMismatch_PrintStripe_NeverMismatch()
        {
            var entry = new IndigoStripeEntry { StripeType = "Print-Image", HvTarget = "HV_TO_NULL" };
            Assert.False(entry.IsHvMismatch);
        }
    }
}
