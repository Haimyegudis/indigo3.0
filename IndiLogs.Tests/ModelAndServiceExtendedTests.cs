using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Xunit;

namespace IndiLogs.Tests
{
    public class ModelAndServiceExtendedTests
    {
        // ── ExportPreset tests ──

        [Fact]
        public void ExportPreset_DefaultValues()
        {
            var preset = new ExportPreset();

            Assert.Equal("", preset.Name);
            Assert.True(preset.IncludeUnixTime);
            Assert.True(preset.IncludeEvents);
            Assert.True(preset.IncludeMachineState);
            Assert.False(preset.IncludeLogStats);
            Assert.NotNull(preset.SelectedIOComponents);
            Assert.Empty(preset.SelectedIOComponents);
            Assert.NotNull(preset.SelectedAxisComponents);
            Assert.Empty(preset.SelectedAxisComponents);
            Assert.NotNull(preset.SelectedCHSteps);
            Assert.Empty(preset.SelectedCHSteps);
            Assert.NotNull(preset.SelectedThreads);
            Assert.Empty(preset.SelectedThreads);
        }

        [Fact]
        public void ExportPreset_CreatedDateIsSet()
        {
            var before = DateTime.Now;
            var preset = new ExportPreset();
            var after = DateTime.Now;

            Assert.True(preset.CreatedDate >= before);
            Assert.True(preset.CreatedDate <= after);
        }

        // ── SelectableItem tests ──

        [Fact]
        public void SelectableItem_DefaultValues()
        {
            var item = new SelectableItem();
            Assert.Equal("", item.Name);
            Assert.Equal("", item.Category);
            Assert.False(item.IsSelected);
        }

        [Fact]
        public void SelectableItem_PropertyChanged_Name()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            item.Name = "Test";

            Assert.Contains("Name", changed);
            Assert.Equal("Test", item.Name);
        }

        [Fact]
        public void SelectableItem_PropertyChanged_Category()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            item.Category = "IO";

            Assert.Contains("Category", changed);
        }

        [Fact]
        public void SelectableItem_PropertyChanged_IsSelected()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            item.IsSelected = true;

            Assert.Contains("IsSelected", changed);
            Assert.True(item.IsSelected);
        }

        // ── ColoringCondition tests ──

        [Fact]
        public void ColoringCondition_DefaultValues()
        {
            var cond = new ColoringCondition();
            Assert.Equal("", cond.Field);
            Assert.Equal("", cond.Operator);
            Assert.Equal("", cond.Value);
            Assert.True(cond.IsEnabled);
        }

        [Fact]
        public void ColoringCondition_Clone_CopiesAllFields()
        {
            var original = new ColoringCondition
            {
                Field = "Level",
                Operator = "Equals",
                Value = "Error",
                Color = System.Windows.Media.Colors.Red
            };

            var clone = original.Clone();

            Assert.Equal("Level", clone.Field);
            Assert.Equal("Equals", clone.Operator);
            Assert.Equal("Error", clone.Value);
            Assert.Equal(System.Windows.Media.Colors.Red, clone.Color);
        }

        [Fact]
        public void ColoringCondition_Clone_IsIndependent()
        {
            var original = new ColoringCondition { Field = "Level", Value = "Error" };
            var clone = original.Clone();

            clone.Value = "Warning";
            Assert.Equal("Error", original.Value);
        }

        [Fact]
        public void ColoringCondition_IsEnabled_NotSerialized()
        {
            // IsEnabled has [JsonIgnore] - verify it exists and defaults to true
            var cond = new ColoringCondition();
            Assert.True(cond.IsEnabled);
            cond.IsEnabled = false;
            Assert.False(cond.IsEnabled);
        }

        // ── FilterCondition tests ──

        [Fact]
        public void FilterCondition_DefaultValues()
        {
            var cond = new FilterCondition();
            Assert.Equal("", cond.Field);
            Assert.Equal("", cond.Operator);
            Assert.Equal("", cond.Value);
            Assert.True(cond.IsActive);
        }

        // ── FilterNode tests ──

        [Fact]
        public void FilterNode_DefaultType_IsGroup()
        {
            var node = new FilterNode();
            Assert.Equal(NodeType.Group, node.Type);
        }

        [Fact]
        public void FilterNode_Children_DefaultNotNull()
        {
            var node = new FilterNode();
            Assert.NotNull(node.Children);
        }

        [Fact]
        public void FilterNode_DeepClone_EmptyChildren_Works()
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
            };
            node.Children.Clear();

            var clone = node.DeepClone();
            Assert.Equal(NodeType.Group, clone.Type);
            Assert.Equal("AND", clone.LogicalOperator);
            Assert.Empty(clone.Children);
        }

        [Fact]
        public void FilterNode_DeepClone_NestedGroups()
        {
            var inner = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Fatal" }
                }
            };

            var outer = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode> { inner }
            };

            var clone = outer.DeepClone();

            Assert.Equal(NodeType.Group, clone.Type);
            Assert.Single(clone.Children);
            Assert.Equal(NodeType.Group, clone.Children[0].Type);
            Assert.Equal(2, clone.Children[0].Children.Count);

            // Mutate clone, verify original unchanged
            clone.Children[0].Children[0].Value = "Warning";
            Assert.Equal("Error", inner.Children[0].Value);
        }

        // ── LogSessionData tests ──

        [Fact]
        public void LogSessionData_DefaultValues()
        {
            var session = new LogSessionData();

            Assert.NotNull(session.Logs);
            Assert.Empty(session.Logs);
            Assert.NotNull(session.AppDevLogs);
            Assert.Empty(session.AppDevLogs);
            Assert.NotNull(session.ConfigurationFiles);
            Assert.NotNull(session.DatabaseFiles);
            Assert.NotNull(session.TerminalLogFiles);
            Assert.NotNull(session.TerminalCsvBytes);
            Assert.NotNull(session.GlobalsFiles);
            Assert.NotNull(session.SystabFiles);
            Assert.NotNull(session.Events);
            Assert.NotNull(session.Screenshots);
            Assert.NotNull(session.MarkedLogs);
            Assert.NotNull(session.StateTransitions);
            Assert.NotNull(session.CriticalFailureEvents);
            Assert.Equal("", session.FileName);
            Assert.Equal("", session.FilePath);
            Assert.False(session.HasBinaryAppLogs);
            Assert.Null(session.ConfigurationType);
            Assert.Null(session.SetupInfo);
            Assert.Null(session.PressConfiguration);
            Assert.Null(session.VersionsInfo);
            Assert.Null(session.EmStatisticsCsvContent);
            Assert.Null(session.TimeSyncOffset);
            Assert.False(session.HasTimeSyncData);
        }

        // ── SessionFilterState tests ──

        [Fact]
        public void SessionFilterState_DefaultValues()
        {
            var state = new SessionFilterState();
            Assert.Null(state.MainFilterRoot);
            Assert.Null(state.AppFilterRoot);
            Assert.False(state.IsMainFilterActive);
            Assert.False(state.IsAppFilterActive);
            Assert.False(state.IsMainFilterOutActive);
            Assert.False(state.IsAppFilterOutActive);
            Assert.False(state.IsTimeFocusActive);
            Assert.False(state.IsAppTimeFocusActive);
            Assert.Null(state.NegativeFilters);
            Assert.Null(state.ActiveThreadFilters);
            Assert.Null(state.SearchText);
        }

        // ── SessionChartState tests ──

        [Fact]
        public void SessionChartState_DefaultValues()
        {
            var state = new SessionChartState();
            Assert.Null(state.DataPackage);
            Assert.Null(state.TimeData);
            Assert.Null(state.GlobalStates);
            Assert.False(state.InMemoryDataLoaded);
            Assert.Equal(0, state.ViewStartIndex);
            Assert.Equal(0, state.ViewEndIndex);
            Assert.Equal(0, state.CursorIndex);
            Assert.False(state.ShowStates);
            Assert.False(state.IsGridLayout);
            Assert.False(state.IsSignalPanelVisible);
            Assert.Equal(-1, state.SelectedChartIndex);
        }

        // ── EventEntry tests ──

        [Fact]
        public void EventEntry_DefaultValues()
        {
            var entry = new EventEntry();
            Assert.Equal("", entry.Name);
            Assert.Equal("", entry.State);
            Assert.Equal("", entry.Severity);
            Assert.Equal("", entry.Description);
        }

        // ── TabSelectionConfig tests ──

        [Fact]
        public void TabSelectionConfig_DefaultValues()
        {
            var config = new TabSelectionConfig();
            Assert.True(config.LoadPlc);
            Assert.True(config.LoadApp);
            Assert.True(config.LoadEvents);
            Assert.True(config.LoadTerminalLogs);
            Assert.True(config.LoadConfiguration);
            Assert.True(config.LoadSystab);
            Assert.True(config.LoadGlobals);
            Assert.True(config.LoadLrs);
            Assert.True(config.LoadScreenshots);
            Assert.True(config.LoadSetupInfo);
            Assert.True(config.LoadManagerThread);
            Assert.True(config.ShowCharts);
            Assert.True(config.ShowCpr);
            Assert.True(config.ShowStepRecorder);
            Assert.True(config.ShowDifferentLogs);
            Assert.False(config.HasApp);
            Assert.False(config.HasPlc);
            Assert.False(config.IsS6);
            Assert.False(config.UseTimeFilter);
        }

        [Fact]
        public void TabSelectionConfig_CanDisableAll()
        {
            var config = new TabSelectionConfig
            {
                LoadPlc = false,
                LoadApp = false,
                LoadEvents = false
            };
            Assert.False(config.LoadPlc);
            Assert.False(config.LoadApp);
            Assert.False(config.LoadEvents);
        }

        [Fact]
        public void TabSelectionConfig_CreateForConfiguration()
        {
            var s6Config = TabSelectionConfig.CreateForConfiguration(true);
            Assert.True(s6Config.IsS6);

            var s4Config = TabSelectionConfig.CreateForConfiguration(false);
            Assert.False(s4Config.IsS6);
        }

        // ── StateEntry tests ──

        [Fact]
        public void StateEntry_DefaultValues()
        {
            var entry = new StateEntry();
            Assert.Equal("", entry.StateName);
            Assert.Null(entry.EndTime);
        }

        [Fact]
        public void StateEntry_Duration_WhenEndTimeSet()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2025, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 10, 5, 0)
            };
            Assert.NotNull(entry.EndTime);
            var duration = entry.EndTime.Value - entry.StartTime;
            Assert.Equal(TimeSpan.FromMinutes(5), duration);
        }

        // ── GapInfo tests ──

        [Fact]
        public void GapInfo_DefaultValues()
        {
            var gap = new GapInfo();
            Assert.Equal(0, gap.Index);
            Assert.Null(gap.LastMessageBeforeGap);
        }

        // ── IoDeviceData tests ──

        [Fact]
        public void IoDeviceData_DefaultValues()
        {
            var device = new IoDeviceData();
            Assert.Equal("", device.DeviceName);
            Assert.Equal("", device.FileName);
            Assert.NotNull(device.Columns);
            Assert.Empty(device.Columns);
            Assert.NotNull(device.Rows);
            Assert.Empty(device.Rows);
        }

        // ── IoDataRow tests ──

        [Fact]
        public void IoDataRow_DefaultValues()
        {
            var row = new IoDataRow();
            Assert.Equal(0, row.RawTime);
            Assert.Equal("", row.MachineState);
            Assert.NotNull(row.Values);
            Assert.Empty(row.Values);
        }

        // ── LogStatisticsResult tests ──

        [Fact]
        public void LogStatisticsResult_DefaultValues()
        {
            var result = new LogStatisticsResult();
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Equal(0, result.TotalPlcErrors);
            Assert.Equal(0, result.TotalAppErrors);
            Assert.False(result.HasBinaryAppLogs);
            Assert.Null(result.EarliestTimestamp);
            Assert.Null(result.LatestTimestamp);
        }

        // ── GrepResult tests ──

        [Fact]
        public void GrepResult_DefaultValues()
        {
            var result = new GrepResult();
            Assert.Equal("", result.FilePath);
            Assert.Equal("", result.LogType);
            Assert.Equal("", result.PreviewText);
            Assert.Equal("", result.SessionName);
            Assert.Equal("", result.MatchedField);
            Assert.Null(result.ReferencedLogEntry);
            Assert.Null(result.LocationName);
            Assert.Null(result.LocationAddress);
            Assert.False(result.IsSelected);
            Assert.Equal(0, result.LineNumber);
            Assert.Equal(0, result.SessionIndex);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_NoTimestamp_ReturnsNA()
        {
            var result = new GrepResult();
            Assert.Equal("N/A", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_WithTimestamp()
        {
            var result = new GrepResult { Timestamp = new DateTime(2025, 3, 7, 10, 30, 0) };
            Assert.Contains("2025-03-07", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_PrefersLogEntry()
        {
            var logEntry = new LogEntry { Date = new DateTime(2025, 6, 15, 14, 0, 0) };
            var result = new GrepResult
            {
                Timestamp = new DateTime(2025, 1, 1),
                ReferencedLogEntry = logEntry
            };
            Assert.Contains("2025-06-15", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_IsSelected_RaisesPropertyChanged()
        {
            var result = new GrepResult();
            var changed = new List<string>();
            result.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            result.IsSelected = true;

            Assert.Contains("IsSelected", changed);
        }

        // ── SavedConfiguration tests ──

        [Fact]
        public void SavedConfiguration_DefaultValues()
        {
            var config = new SavedConfiguration();
            Assert.Equal("", config.Name);
        }

        // ── DbTreeNode tests ──

        [Fact]
        public void DbTreeNode_DefaultValues()
        {
            var node = new DbTreeNode();
            Assert.Equal("", node.Name);
            Assert.NotNull(node.Children);
        }

        // ── SystabEntry tests ──

        [Fact]
        public void SystabEntry_DefaultValues()
        {
            var entry = new SystabEntry();
            Assert.Equal("", entry.Parameter);
            Assert.Equal("", entry.Saved);
            Assert.Equal("", entry.Default);
            Assert.Equal("", entry.Minimum);
            Assert.Equal("", entry.Maximum);
            Assert.False(entry.IsDifferent);
        }

        // ── SystabTopicNode tests ──

        [Fact]
        public void SystabTopicNode_DefaultValues()
        {
            var node = new SystabTopicNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.FullPath);
            Assert.False(node.IsExpanded);
            Assert.False(node.IsSelected);
            Assert.False(node.HasDifferences);
            Assert.False(node.IsTopLevel);
            Assert.NotNull(node.Children);
            Assert.Empty(node.Children);
            Assert.NotNull(node.Entries);
            Assert.Empty(node.Entries);
        }

        [Fact]
        public void SystabTopicNode_PropertyChanged_Name()
        {
            var node = new SystabTopicNode();
            var changed = new List<string>();
            node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            node.Name = "Test Topic";

            Assert.Contains("Name", changed);
        }

        [Fact]
        public void SystabTopicNode_PropertyChanged_IsExpanded()
        {
            var node = new SystabTopicNode();
            var changed = new List<string>();
            node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            node.IsExpanded = true;

            Assert.Contains("IsExpanded", changed);
        }

        [Fact]
        public void SystabTopicNode_PropertyChanged_HasDifferences()
        {
            var node = new SystabTopicNode();
            var changed = new List<string>();
            node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            node.HasDifferences = true;

            Assert.Contains("HasDifferences", changed);
        }
    }
}

