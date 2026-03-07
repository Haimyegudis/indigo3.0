using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class GrepResultTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var result = new GrepResult();
            Assert.NotNull(result.FilePath);
            Assert.Equal(0, result.LineNumber);
            Assert.Null(result.ReferencedLogEntry);
            Assert.False(result.IsSelected);
        }

        [Fact]
        public void TimestampDisplay_FromReferencedLogEntry()
        {
            var result = new GrepResult
            {
                ReferencedLogEntry = new LogEntry
                {
                    Date = new DateTime(2025, 6, 15, 14, 30, 0)
                }
            };

            Assert.Contains("2025-06-15", result.TimestampDisplay);
            Assert.Contains("14:30:00", result.TimestampDisplay);
        }

        [Fact]
        public void TimestampDisplay_NoLogEntry_UsesTimestamp()
        {
            var result = new GrepResult
            {
                Timestamp = new DateTime(2025, 3, 1, 12, 0, 0)
            };

            var display = result.TimestampDisplay;
            Assert.NotNull(display);
        }

        [Fact]
        public void IsSelected_RaisesPropertyChanged()
        {
            var result = new GrepResult();
            string? raised = null;
            result.PropertyChanged += (s, e) => raised = e.PropertyName;

            result.IsSelected = true;

            Assert.Equal("IsSelected", raised);
        }

        [Fact]
        public void PreviewText_CanBeSet()
        {
            var result = new GrepResult { PreviewText = "Error in module XYZ" };
            Assert.Equal("Error in module XYZ", result.PreviewText);
        }

        [Fact]
        public void LogType_CanBeSetToPLCOrAPP()
        {
            var plc = new GrepResult { LogType = "PLC" };
            var app = new GrepResult { LogType = "APP" };

            Assert.Equal("PLC", plc.LogType);
            Assert.Equal("APP", app.LogType);
        }

        [Fact]
        public void MatchedField_CanBeSet()
        {
            var result = new GrepResult { MatchedField = "Message" };
            Assert.Equal("Message", result.MatchedField);
        }

        [Fact]
        public void LocationInfo_CanBeSet()
        {
            var result = new GrepResult
            {
                LocationName = "Server1",
                LocationAddress = "192.168.1.1"
            };

            Assert.Equal("Server1", result.LocationName);
            Assert.Equal("192.168.1.1", result.LocationAddress);
        }
    }
}
