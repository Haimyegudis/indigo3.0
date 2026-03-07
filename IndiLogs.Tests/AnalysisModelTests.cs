using IndiLogs_3._0.Converters;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace IndiLogs.Tests
{
    public class AnalysisResultTests
    {
        [Fact]
        public void HasCriticalEvent_ReturnsFalse_WhenNull()
        {
            var result = new AnalysisResult { CriticalEventMessage = null };
            Assert.False(result.HasCriticalEvent);
        }

        [Fact]
        public void HasCriticalEvent_ReturnsFalse_WhenEmpty()
        {
            var result = new AnalysisResult { CriticalEventMessage = "" };
            Assert.False(result.HasCriticalEvent);
        }

        [Fact]
        public void HasCriticalEvent_ReturnsTrue_WhenSet()
        {
            var result = new AnalysisResult { CriticalEventMessage = "PLC_FAILURE_STATE_CHANGE" };
            Assert.True(result.HasCriticalEvent);
        }

        [Fact]
        public void StatusColor_Green_WhenSuccess()
        {
            var result = new AnalysisResult { Status = AnalysisStatus.Success };
            Assert.Equal(System.Windows.Media.Brushes.Green, result.StatusColor);
        }

        [Fact]
        public void StatusColor_Orange_WhenWarning()
        {
            var result = new AnalysisResult { Status = AnalysisStatus.Warning };
            Assert.Equal(System.Windows.Media.Brushes.Orange, result.StatusColor);
        }

        [Fact]
        public void StatusColor_Red_WhenFailure()
        {
            var result = new AnalysisResult { Status = AnalysisStatus.Failure };
            Assert.Equal(System.Windows.Media.Brushes.Red, result.StatusColor);
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var result = new AnalysisResult();
            Assert.Equal("", result.ProcessName);
            Assert.Equal("", result.Summary);
            Assert.Empty(result.Steps);
            Assert.Empty(result.ErrorsFound);
            Assert.Empty(result.ErrorItems);
            Assert.Null(result.CriticalEventMessage);
        }
    }

    public class AnalysisStepTests
    {
        [Fact]
        public void DurationMs_ReturnsCorrectValue()
        {
            var step = new AnalysisStep
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 5)
            };
            Assert.Equal(5000, step.DurationMs);
        }

        [Fact]
        public void DurationMs_ReturnsZero_WhenSameTime()
        {
            var time = new DateTime(2025, 6, 15, 12, 0, 0);
            var step = new AnalysisStep { StartTime = time, EndTime = time };
            Assert.Equal(0, step.DurationMs);
        }

        [Fact]
        public void DurationMs_ReturnsNegative_WhenEndBeforeStart()
        {
            var step = new AnalysisStep
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 5),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 0)
            };
            Assert.True(step.DurationMs < 0);
        }

        [Fact]
        public void DurationMs_SubMillisecondPrecision()
        {
            var step = new AnalysisStep
            {
                StartTime = new DateTime(2025, 1, 1, 0, 0, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 0, 0, 0, 150)
            };
            Assert.Equal(150, step.DurationMs);
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var step = new AnalysisStep();
            Assert.Equal("", step.StepName);
            Assert.Equal("", step.Status);
            Assert.Null(step.StartLog);
            Assert.Null(step.EndLog);
        }
    }

    public class FailureErrorItemTests
    {
        [Fact]
        public void IsNavigable_ReturnsTrue_WhenLogEntrySet()
        {
            var item = new FailureErrorItem { LogEntry = new LogEntry() };
            Assert.True(item.IsNavigable);
        }

        [Fact]
        public void IsNavigable_ReturnsFalse_WhenLogEntryNull()
        {
            var item = new FailureErrorItem { LogEntry = null };
            Assert.False(item.IsNavigable);
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var item = new FailureErrorItem();
            Assert.Equal("", item.DisplayText);
            Assert.Null(item.LogEntry);
            Assert.False(item.IsNavigable);
        }
    }

    public class ExtraFieldConverterTests
    {
        private readonly ExtraFieldConverter _converter = new();

        [Fact]
        public void Convert_ReturnsValue_WhenKeyExists()
        {
            var dict = new Dictionary<string, string> { { "Level", "Error" } };
            var result = _converter.Convert(dict, typeof(string), "Level", CultureInfo.InvariantCulture);
            Assert.Equal("Error", result);
        }

        [Fact]
        public void Convert_ReturnsEmpty_WhenKeyMissing()
        {
            var dict = new Dictionary<string, string> { { "Level", "Error" } };
            var result = _converter.Convert(dict, typeof(string), "Missing", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Convert_ReturnsEmpty_WhenValueIsNull()
        {
            var result = _converter.Convert(null, typeof(string), "Key", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Convert_ReturnsEmpty_WhenParameterIsNull()
        {
            var dict = new Dictionary<string, string> { { "Level", "Error" } };
            var result = _converter.Convert(dict, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Convert_ReturnsEmpty_WhenValueIsNotDictionary()
        {
            var result = _converter.Convert("not a dict", typeof(string), "Key", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Convert_ReturnsEmpty_WhenParameterIsNotString()
        {
            var dict = new Dictionary<string, string> { { "Level", "Error" } };
            var result = _converter.Convert(dict, typeof(string), 42, CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(
                () => _converter.ConvertBack("val", typeof(string), "Key", CultureInfo.InvariantCulture));
        }
    }
}
