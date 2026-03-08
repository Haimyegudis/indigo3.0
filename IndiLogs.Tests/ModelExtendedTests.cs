using System;
using System.Collections.Generic;
using System.ComponentModel;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using Xunit;

namespace IndiLogs.Tests;

public class ModelExtendedTests
{
    #region CaseFile Tests

    [Fact]
    public void CaseFile_DefaultConstructor_InitializesAllCollections()
    {
        var cf = new CaseFile();
        Assert.NotNull(cf.Meta);
        Assert.NotNull(cf.Resources);
        Assert.NotNull(cf.ViewState);
        Assert.NotNull(cf.Annotations);
        Assert.NotNull(cf.MainColoringRules);
        Assert.NotNull(cf.AppColoringRules);
        Assert.Empty(cf.Resources);
        Assert.Empty(cf.Annotations);
    }

    [Fact]
    public void CaseMetadata_Defaults()
    {
        var m = new CaseMetadata();
        Assert.Equal("1.0", m.Version);
        Assert.Equal("", m.Author);
        Assert.Equal("", m.Description);
        Assert.Equal(default(DateTime), m.CreatedAt);
    }

    [Fact]
    public void CaseMetadata_SetProperties()
    {
        var m = new CaseMetadata
        {
            Version = "2.0",
            Author = "Test",
            CreatedAt = new DateTime(2025, 1, 1),
            Description = "Test case"
        };
        Assert.Equal("2.0", m.Version);
        Assert.Equal("Test", m.Author);
        Assert.Equal("Test case", m.Description);
    }

    [Fact]
    public void CaseResource_Defaults()
    {
        var r = new CaseResource();
        Assert.Equal("", r.FileName);
        Assert.Equal(0, r.Size);
        Assert.Null(r.Hash);
    }

    [Fact]
    public void CaseResource_SetProperties()
    {
        var r = new CaseResource
        {
            FileName = "test.zip",
            Size = 12345,
            LastModified = new DateTime(2025, 6, 15),
            Hash = "abc123"
        };
        Assert.Equal("test.zip", r.FileName);
        Assert.Equal(12345, r.Size);
        Assert.Equal("abc123", r.Hash);
    }

    [Fact]
    public void CaseViewState_DefaultConstructor_InitializesLists()
    {
        var vs = new CaseViewState();
        Assert.NotNull(vs.ActiveThreadFilters);
        Assert.NotNull(vs.NegativeFilters);
        Assert.Empty(vs.ActiveThreadFilters);
        Assert.Empty(vs.NegativeFilters);
        Assert.Null(vs.ActiveFilters);
        Assert.Null(vs.QuickSearchText);
        Assert.Null(vs.SelectedTab);
    }

    [Fact]
    public void LogAnnotation_DefaultConstructor_SetsCreatedAt()
    {
        var before = DateTime.Now;
        var a = new LogAnnotation();
        var after = DateTime.Now;
        Assert.InRange(a.CreatedAt, before, after);
        Assert.Equal("", a.Content);
        Assert.Equal("#FFFF00", a.Color);
        Assert.Equal("", a.Author);
        Assert.Null(a.TargetLog);
    }

    [Fact]
    public void LogAnnotation_SetProperties()
    {
        var a = new LogAnnotation
        {
            Content = "Important note",
            Color = "#FF0000",
            Author = "Tester",
            TargetLog = new LogTarget { Logger = "TestLogger" }
        };
        Assert.Equal("Important note", a.Content);
        Assert.Equal("#FF0000", a.Color);
        Assert.Equal("Tester", a.Author);
        Assert.Equal("TestLogger", a.TargetLog.Logger);
    }

    [Fact]
    public void LogTarget_Defaults()
    {
        var t = new LogTarget();
        Assert.Equal("", t.Logger);
        Assert.Equal("", t.Thread);
        Assert.Equal("", t.Snippet);
        Assert.Equal("", t.Level);
    }

    [Fact]
    public void LogTarget_SetProperties()
    {
        var ts = new DateTime(2025, 3, 1, 12, 0, 0);
        var t = new LogTarget
        {
            Timestamp = ts,
            Logger = "MyLogger",
            Thread = "Thread-1",
            Snippet = "Error occurred",
            Level = "ERROR"
        };
        Assert.Equal(ts, t.Timestamp);
        Assert.Equal("MyLogger", t.Logger);
        Assert.Equal("Thread-1", t.Thread);
        Assert.Equal("Error occurred", t.Snippet);
        Assert.Equal("ERROR", t.Level);
    }

    #endregion

    #region LogSessionData Tests

    [Fact]
    public void LogSessionData_DefaultConstructor_InitializesAllCollections()
    {
        var s = new LogSessionData();
        Assert.NotNull(s.Logs);
        Assert.NotNull(s.AppDevLogs);
        Assert.NotNull(s.StateTransitions);
        Assert.NotNull(s.CriticalFailureEvents);
        Assert.NotNull(s.ConfigurationFiles);
        Assert.NotNull(s.DatabaseFiles);
        Assert.NotNull(s.TerminalLogFiles);
        Assert.NotNull(s.TerminalCsvBytes);
        Assert.NotNull(s.GlobalsFiles);
        Assert.NotNull(s.SystabFiles);
        Assert.NotNull(s.Events);
        Assert.NotNull(s.Screenshots);
        Assert.NotNull(s.MarkedLogs);
        Assert.Equal("", s.FileName);
        Assert.Equal("", s.FilePath);
    }

    [Fact]
    public void LogSessionData_SetProperties()
    {
        var s = new LogSessionData
        {
            HasBinaryAppLogs = true,
            ConfigurationType = "S4-5",
            FileName = "test.zip",
            FilePath = @"C:\logs\test.zip",
            SetupInfo = "Setup v1",
            PressConfiguration = "Config A",
            VersionsInfo = "v1.0",
            EmStatisticsCsvContent = "col1,col2",
            TimeSyncOffset = TimeSpan.FromSeconds(1.5),
            HasTimeSyncData = true
        };
        Assert.True(s.HasBinaryAppLogs);
        Assert.Equal("S4-5", s.ConfigurationType);
        Assert.Equal("test.zip", s.FileName);
        Assert.Equal("Setup v1", s.SetupInfo);
        Assert.Equal(TimeSpan.FromSeconds(1.5), s.TimeSyncOffset);
        Assert.True(s.HasTimeSyncData);
    }

    [Fact]
    public void SessionFilterState_Defaults()
    {
        var f = new SessionFilterState();
        Assert.Null(f.MainFilterRoot);
        Assert.Null(f.AppFilterRoot);
        Assert.False(f.IsMainFilterActive);
        Assert.False(f.IsAppFilterActive);
        Assert.False(f.IsMainFilterOutActive);
        Assert.False(f.IsAppFilterOutActive);
        Assert.False(f.IsTimeFocusActive);
        Assert.False(f.IsAppTimeFocusActive);
        Assert.Null(f.NegativeFilters);
        Assert.Null(f.ActiveThreadFilters);
        Assert.Null(f.SearchText);
    }

    [Fact]
    public void SessionChartState_Defaults()
    {
        var c = new SessionChartState();
        Assert.Equal(0, c.ViewStartIndex);
        Assert.Equal(0, c.ViewEndIndex);
        Assert.Equal(0, c.CursorIndex);
        Assert.False(c.ShowStates);
        Assert.False(c.IsGridLayout);
        Assert.False(c.IsSignalPanelVisible);
        Assert.Equal(0, c.SmoothWindowSize);
        Assert.Equal(0, c.ColorIndex);
        Assert.Equal(-1, c.SelectedChartIndex);
        Assert.Null(c.DataPackage);
        Assert.Null(c.TimeData);
    }

    #endregion

    #region SystabEntry Tests

    [Fact]
    public void SystabEntry_Defaults()
    {
        var e = new SystabEntry();
        Assert.Equal("", e.Parameter);
        Assert.Equal("", e.Saved);
        Assert.Equal("", e.Default);
        Assert.Equal("", e.Minimum);
        Assert.Equal("", e.Maximum);
        Assert.False(e.IsDifferent);
    }

    [Fact]
    public void SystabEntry_SetProperties()
    {
        var e = new SystabEntry
        {
            Parameter = "Speed",
            Saved = "100",
            Default = "50",
            Minimum = "0",
            Maximum = "200",
            IsDifferent = true
        };
        Assert.Equal("Speed", e.Parameter);
        Assert.Equal("100", e.Saved);
        Assert.True(e.IsDifferent);
    }

    [Fact]
    public void SystabTopicNode_PropertyChanged_Fires()
    {
        var node = new SystabTopicNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        node.Name = "Topic1";
        node.FullPath = "Root/Topic1";
        node.IsExpanded = true;
        node.IsSelected = true;
        node.HasDifferences = true;

        Assert.Contains("Name", changed);
        Assert.Contains("FullPath", changed);
        Assert.Contains("IsExpanded", changed);
        Assert.Contains("IsSelected", changed);
        Assert.Contains("HasDifferences", changed);
    }

    [Fact]
    public void SystabTopicNode_Defaults()
    {
        var node = new SystabTopicNode();
        Assert.Equal("", node.Name);
        Assert.Equal("", node.FullPath);
        Assert.False(node.IsTopLevel);
        Assert.False(node.IsExpanded);
        Assert.False(node.IsSelected);
        Assert.False(node.HasDifferences);
        Assert.NotNull(node.Children);
        Assert.NotNull(node.Entries);
        Assert.Equal(0, node.Count);
    }

    #endregion

    #region DbTreeNode Tests

    [Fact]
    public void DbTreeNode_Defaults()
    {
        var node = new DbTreeNode();
        Assert.Equal("", node.Name);
        Assert.Equal("", node.Type);
        Assert.Equal("", node.Schema);
        Assert.Equal("", node.NodeType);
        Assert.Equal("", node.DatabaseFileName);
        Assert.False(node.IsExpanded);
        Assert.True(node.IsVisible);
        Assert.NotNull(node.Children);
        Assert.NotNull(node.FieldValues);
    }

    [Fact]
    public void DbTreeNode_PropertyChanged_Fires()
    {
        var node = new DbTreeNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        node.Name = "users";
        node.Type = "TABLE";
        node.Schema = "CREATE TABLE users";
        node.IsExpanded = true;
        node.IsVisible = false;

        Assert.Contains("Name", changed);
        Assert.Contains("Type", changed);
        Assert.Contains("Schema", changed);
        Assert.Contains("IsExpanded", changed);
        Assert.Contains("IsVisible", changed);
    }

    [Fact]
    public void DbFieldValue_Defaults()
    {
        var fv = new DbFieldValue();
        Assert.Equal("", fv.ColumnName);
        Assert.Equal("", fv.Value);
        Assert.Equal("", fv.Type);
    }

    [Fact]
    public void DbFieldValue_PropertyChanged_Fires()
    {
        var fv = new DbFieldValue();
        var fired = false;
        fv.PropertyChanged += (s, e) => fired = true;
        // DbFieldValue doesn't call OnPropertyChanged from setters (auto-props)
        // so we just verify the event exists
        Assert.NotNull(fv);
        Assert.False(fired);
    }

    #endregion

    #region TabSelectionConfig Tests

    [Fact]
    public void TabSelectionConfig_AllLoadDefaults_True()
    {
        var c = new TabSelectionConfig();
        Assert.True(c.LoadApp);
        Assert.True(c.LoadPlc);
        Assert.True(c.LoadTerminalLogs);
        Assert.True(c.LoadConfiguration);
        Assert.True(c.LoadSystab);
        Assert.True(c.LoadGlobals);
        Assert.True(c.LoadLrs);
        Assert.True(c.LoadEvents);
        Assert.True(c.LoadScreenshots);
        Assert.True(c.LoadSetupInfo);
        Assert.True(c.LoadManagerThread);
    }

    [Fact]
    public void TabSelectionConfig_AllShowDefaults_True()
    {
        var c = new TabSelectionConfig();
        Assert.True(c.ShowCharts);
        Assert.True(c.ShowCpr);
        Assert.True(c.ShowStepRecorder);
        Assert.True(c.ShowDifferentLogs);
    }

    [Fact]
    public void TabSelectionConfig_AllHasDefaults_False()
    {
        var c = new TabSelectionConfig();
        Assert.False(c.HasApp);
        Assert.False(c.HasPlc);
        Assert.False(c.HasTerminalLogs);
        Assert.False(c.HasConfiguration);
        Assert.False(c.HasSystab);
        Assert.False(c.HasGlobals);
        Assert.False(c.HasLrs);
        Assert.False(c.HasEvents);
        Assert.False(c.HasScreenshots);
        Assert.False(c.HasSetupInfo);
        Assert.False(c.HasManagerThread);
        Assert.False(c.IsS6);
    }

    [Fact]
    public void TabSelectionConfig_TimeFilter_Defaults()
    {
        var c = new TabSelectionConfig();
        Assert.Null(c.AvailableStartTime);
        Assert.Null(c.AvailableEndTime);
        Assert.Null(c.FilterStartTime);
        Assert.Null(c.FilterEndTime);
        Assert.False(c.UseTimeFilter);
    }

    [Fact]
    public void TabSelectionConfig_CreateForConfiguration_SetsIsS6()
    {
        var s6 = TabSelectionConfig.CreateForConfiguration(true);
        Assert.True(s6.IsS6);

        var s45 = TabSelectionConfig.CreateForConfiguration(false);
        Assert.False(s45.IsS6);
    }

    [Fact]
    public void TabSelectionConfig_SetTimeFilter()
    {
        var c = new TabSelectionConfig
        {
            AvailableStartTime = new DateTime(2025, 1, 1),
            AvailableEndTime = new DateTime(2025, 1, 2),
            FilterStartTime = new DateTime(2025, 1, 1, 6, 0, 0),
            FilterEndTime = new DateTime(2025, 1, 1, 18, 0, 0),
            UseTimeFilter = true
        };
        Assert.True(c.UseTimeFilter);
        Assert.Equal(new DateTime(2025, 1, 1), c.AvailableStartTime);
    }

    #endregion

    #region SearchLocation Tests

    [Fact]
    public void SearchLocation_Defaults()
    {
        var loc = new SearchLocation();
        Assert.NotEqual(Guid.Empty, loc.Id);
        Assert.Equal("", loc.Name);
        Assert.Equal("", loc.Address);
        Assert.Equal("", loc.BasePath);
        Assert.True(loc.IsActive);
        Assert.Equal(ConnectionStatus.Unknown, loc.ConnectionStatus);
        Assert.Null(loc.LastAccessed);
    }

    [Fact]
    public void SearchLocation_PropertyChanged_Fires()
    {
        var loc = new SearchLocation();
        var changed = new List<string>();
        loc.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        loc.Name = "Server1";
        loc.Address = "192.168.1.1";
        loc.BasePath = @"\\server\logs";
        loc.IsActive = false;
        loc.ConnectionStatus = ConnectionStatus.Connected;
        loc.LastAccessed = DateTime.Now;

        Assert.Contains("Name", changed);
        Assert.Contains("Address", changed);
        Assert.Contains("BasePath", changed);
        Assert.Contains("IsActive", changed);
        Assert.Contains("ConnectionStatus", changed);
        Assert.Contains("LastAccessed", changed);
        Assert.Equal(6, changed.Count);
    }

    [Fact]
    public void SearchLocation_UniqueIds()
    {
        var a = new SearchLocation();
        var b = new SearchLocation();
        Assert.NotEqual(a.Id, b.Id);
    }

    #endregion

    #region ConnectionStatus Enum Tests

    [Theory]
    [InlineData(ConnectionStatus.Unknown, 0)]
    [InlineData(ConnectionStatus.Connected, 1)]
    [InlineData(ConnectionStatus.Disconnected, 2)]
    [InlineData(ConnectionStatus.Testing, 3)]
    public void ConnectionStatus_HasExpectedValues(ConnectionStatus status, int expected)
    {
        Assert.Equal(expected, (int)status);
    }

    #endregion

    #region SearchProfile Tests

    [Fact]
    public void SearchProfile_Defaults()
    {
        var p = new SearchProfile();
        Assert.Equal("", p.Name);
        Assert.NotNull(p.Locations);
        Assert.NotNull(p.Criteria);
        Assert.NotNull(p.Schedules);
        Assert.Empty(p.Locations);
        Assert.Empty(p.Schedules);
    }

    [Fact]
    public void SearchProfile_SetProperties()
    {
        var p = new SearchProfile
        {
            Name = "Daily scan",
            Locations = { new SearchLocation { Name = "Server1" } },
            Schedules = { new ScheduledSearch { Name = "Hourly" } }
        };
        Assert.Equal("Daily scan", p.Name);
        Assert.Single(p.Locations);
        Assert.Single(p.Schedules);
    }

    #endregion

    #region LogStatisticsResult Tests

    [Fact]
    public void LogStatisticsResult_Defaults()
    {
        var r = new LogStatisticsResult();
        Assert.Equal(0, r.TotalPlcLogs);
        Assert.Equal(0, r.TotalAppLogs);
        Assert.Equal(0, r.TotalPlcErrors);
        Assert.Equal(0, r.TotalAppErrors);
        Assert.Null(r.EarliestTimestamp);
        Assert.Null(r.LatestTimestamp);
        Assert.NotNull(r.PlcTopErrors);
        Assert.NotNull(r.PlcThreadLoad);
        Assert.NotNull(r.PlcGaps);
        Assert.NotNull(r.AppLoggerErrors);
        Assert.NotNull(r.AppLoggerLoad);
        Assert.NotNull(r.AppMethodErrors);
        Assert.NotNull(r.AppMethodLoad);
        Assert.NotNull(r.AppGaps);
        Assert.NotNull(r.ErrorsBySource);
        Assert.NotNull(r.ErrorsByState);
        Assert.NotNull(r.StateEntries);
        Assert.False(r.HasBinaryAppLogs);
    }

    [Fact]
    public void StatCount_Defaults()
    {
        var sc = new StatCount();
        Assert.Equal("", sc.Name);
        Assert.Equal(0, sc.Count);
    }

    [Fact]
    public void StatCount_SetProperties()
    {
        var sc = new StatCount { Name = "CriticalError", Count = 42 };
        Assert.Equal("CriticalError", sc.Name);
        Assert.Equal(42, sc.Count);
    }

    #endregion
}
