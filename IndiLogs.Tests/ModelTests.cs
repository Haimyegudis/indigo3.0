using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class StateEntryTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var entry = new StateEntry();
            Assert.Equal("", entry.StateName);
            Assert.Equal("", entry.TransitionTitle);
            Assert.Equal("", entry.Status);
            Assert.Null(entry.EndTime);
            Assert.Null(entry.LogReference);
            Assert.Null(entry.StatusColor);
        }

        [Fact]
        public void Duration_WithEndTime_ReturnsFormatted()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 5, 500)
            };

            Assert.Equal("00:00:05.500", entry.Duration);
        }

        [Fact]
        public void Duration_WithoutEndTime_ReturnsCurrent()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0)
            };

            Assert.Equal("Current...", entry.Duration);
        }

        [Fact]
        public void Duration_ZeroDuration_ReturnsZero()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 0)
            };

            Assert.Equal("00:00:00.000", entry.Duration);
        }
    }

    public class EventEntryTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var entry = new EventEntry();
            Assert.Equal("", entry.Name);
            Assert.Equal("", entry.State);
            Assert.Equal("", entry.Severity);
            Assert.Equal("", entry.Description);
            Assert.Null(entry.Parameters);
        }

        [Fact]
        public void SetProperties_Works()
        {
            var entry = new EventEntry
            {
                Time = new DateTime(2025, 6, 15),
                Name = "PrintStart",
                State = "Running",
                Severity = "Info",
                Description = "Print job started",
                Parameters = "JobId=123"
            };

            Assert.Equal("PrintStart", entry.Name);
            Assert.Equal("Running", entry.State);
            Assert.Equal("Info", entry.Severity);
            Assert.Equal("Print job started", entry.Description);
            Assert.Equal("JobId=123", entry.Parameters);
        }
    }

    public class GapInfoTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var gap = new GapInfo();
            Assert.Equal(0, gap.Index);
            Assert.Equal("", gap.DurationText);
            Assert.Null(gap.LastMessageBeforeGap);
            Assert.Null(gap.LastLogBeforeGap);
        }

        [Fact]
        public void SetProperties_Works()
        {
            var gap = new GapInfo
            {
                Index = 5,
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 10, 5, 0),
                Duration = TimeSpan.FromMinutes(5),
                DurationText = "5 minutes",
                LastMessageBeforeGap = "Last log before gap"
            };

            Assert.Equal(5, gap.Index);
            Assert.Equal(TimeSpan.FromMinutes(5), gap.Duration);
            Assert.Equal("5 minutes", gap.DurationText);
        }
    }

    public class LoggerNodeTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var node = new LoggerNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.FullPath);
            Assert.Equal(0, node.Count);
            Assert.Empty(node.Children);
            Assert.False(node.IsExpanded);
            Assert.False(node.IsSelected);
            Assert.False(node.IsHidden);
            Assert.False(node.IsActive);
        }

        [Fact]
        public void DisplayText_FormatsCorrectly()
        {
            var node = new LoggerNode { Name = "indigo", Count = 42 };
            Assert.Equal("indigo (42)", node.DisplayText);
        }

        [Fact]
        public void IsExpanded_RaisesPropertyChanged()
        {
            var node = new LoggerNode();
            string? raised = null;
            node.PropertyChanged += (s, e) => raised = e.PropertyName;

            node.IsExpanded = true;

            Assert.Equal("IsExpanded", raised);
        }

        [Fact]
        public void IsSelected_RaisesPropertyChanged()
        {
            var node = new LoggerNode();
            string? raised = null;
            node.PropertyChanged += (s, e) => raised = e.PropertyName;

            node.IsSelected = true;

            Assert.Equal("IsSelected", raised);
        }

        [Fact]
        public void IsHidden_RaisesPropertyChanged()
        {
            var node = new LoggerNode();
            string? raised = null;
            node.PropertyChanged += (s, e) => raised = e.PropertyName;

            node.IsHidden = true;

            Assert.Equal("IsHidden", raised);
        }

        [Fact]
        public void IsActive_RaisesPropertyChanged()
        {
            var node = new LoggerNode();
            string? raised = null;
            node.PropertyChanged += (s, e) => raised = e.PropertyName;

            node.IsActive = true;

            Assert.Equal("IsActive", raised);
        }

        [Fact]
        public void Children_CanAddNodes()
        {
            var parent = new LoggerNode { Name = "com" };
            var child = new LoggerNode { Name = "indigo", FullPath = "com.indigo" };
            parent.Children.Add(child);

            Assert.Single(parent.Children);
            Assert.Equal("indigo", parent.Children[0].Name);
        }
    }

    public class ColumnSettingsTests
    {
        [Fact]
        public void Defaults_AreEmpty()
        {
            var settings = new ColumnSettings();
            Assert.Empty(settings.ColumnWidths);
            Assert.Empty(settings.ColumnOrders);
            Assert.Empty(settings.ColumnVisibility);
        }

        [Fact]
        public void CanStoreSettings()
        {
            var settings = new ColumnSettings();
            settings.ColumnWidths["Message"] = 300.0;
            settings.ColumnOrders["Message"] = 5;
            settings.ColumnVisibility["Message"] = true;

            Assert.Equal(300.0, settings.ColumnWidths["Message"]);
            Assert.Equal(5, settings.ColumnOrders["Message"]);
            Assert.True(settings.ColumnVisibility["Message"]);
        }
    }

    public class GridSettingsTests
    {
        [Fact]
        public void Defaults_AreInitialized()
        {
            var gs = new GridSettings();
            Assert.NotNull(gs.PlcColumns);
            Assert.NotNull(gs.AppColumns);
            Assert.NotNull(gs.PlcColumnsS45);
            Assert.NotNull(gs.AppColumnsS45);
        }
    }

    public class SearchProfileTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var profile = new SearchProfile();
            Assert.Equal("", profile.Name);
            Assert.Empty(profile.Locations);
            Assert.NotNull(profile.Criteria);
            Assert.Empty(profile.Schedules);
        }

        [Fact]
        public void CanPopulateProfile()
        {
            var profile = new SearchProfile
            {
                Name = "Test Profile",
                Locations = { new SearchLocation { Name = "Server1", BasePath = @"\\server\share" } },
                Schedules = { new ScheduledSearch { Id = System.Guid.NewGuid() } }
            };

            Assert.Equal("Test Profile", profile.Name);
            Assert.Single(profile.Locations);
            Assert.Single(profile.Schedules);
        }
    }

    public class SearchLocationTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var loc = new SearchLocation();
            Assert.Equal("", loc.Name);
            Assert.Equal("", loc.Address);
            Assert.Equal("", loc.BasePath);
            Assert.True(loc.IsActive);
            Assert.Equal(ConnectionStatus.Unknown, loc.ConnectionStatus);
            Assert.Null(loc.LastAccessed);
            Assert.NotEqual(System.Guid.Empty, loc.Id);
        }

        [Fact]
        public void PropertyChanged_Fires()
        {
            var loc = new SearchLocation();
            var raised = new List<string>();
            loc.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            loc.Name = "Server1";
            loc.Address = "192.168.1.1";
            loc.BasePath = @"\\server\logs";
            loc.IsActive = false;
            loc.ConnectionStatus = ConnectionStatus.Connected;
            loc.LastAccessed = DateTime.Now;

            Assert.Contains("Name", raised);
            Assert.Contains("Address", raised);
            Assert.Contains("BasePath", raised);
            Assert.Contains("IsActive", raised);
            Assert.Contains("ConnectionStatus", raised);
            Assert.Contains("LastAccessed", raised);
        }
    }

    public class DefaultConfigurationTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var config = new DefaultConfiguration();
            Assert.Null(config.PlcFilteredDefaultFilter);
            Assert.Null(config.MainDefaultColoringRules);
            Assert.Null(config.AppDefaultColoringRules);
            Assert.False(config.HasCustomPlcFilter);
            Assert.False(config.HasCustomMainColoring);
            Assert.False(config.HasCustomAppColoring);
        }
    }

    public class CprFilterStateTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var f = new IndiLogs_3._0.Models.Cpr.CprFilterState();
            Assert.Equal("Y", f.Axis);
            Assert.True(f.AutoYAxis);
            Assert.False(f.SharedYAxis);
            Assert.Equal(1, f.SmoothingWindow);
            Assert.Equal(3, f.BowDegree);
            Assert.Equal(-200, f.YAxisFrom);
            Assert.Equal(200, f.YAxisTo);
        }
    }
}
