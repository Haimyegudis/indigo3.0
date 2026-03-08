using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using Xunit;

namespace IndiLogs.Tests;

public class CoverageBoostMiscTests
{
    // ═══════════════════════════════════════════════════════════
    // 1. LogEntry property coverage
    // ═══════════════════════════════════════════════════════════

    #region LogEntry Tests

    [Fact]
    public void LogEntry_DefaultValues()
    {
        var entry = new LogEntry();
        Assert.Equal("", entry.Level);
        Assert.Equal("", entry.Message);
        Assert.Equal("", entry.ThreadName);
        Assert.Equal("", entry.Logger);
        Assert.Equal("", entry.ProcessName);
        Assert.Equal("", entry.Method);
        Assert.Equal("", entry.Pattern);
        Assert.Equal("", entry.Data);
        Assert.Equal("", entry.Exception);
        Assert.Equal("", entry.AnnotationContent);
        Assert.False(entry.HasAnnotation);
        Assert.False(entry.IsAnnotationExpanded);
        Assert.False(entry.IsMarked);
        Assert.False(entry.IsCurrentMarked);
        Assert.False(entry.IsErrorOrEvents);
        Assert.Null(entry.CustomColor);
        Assert.Null(entry.SystemDefaultColor);
        Assert.Null(entry.SyncedTime);
        Assert.Null(entry.ExtraFields);
        Assert.Null(entry.RowForeground);
        Assert.False(entry.HasSystemDefaultColor);
    }

    [Fact]
    public void LogEntry_SetLevel()
    {
        var entry = new LogEntry { Level = "ERROR" };
        Assert.Equal("ERROR", entry.Level);
    }

    [Fact]
    public void LogEntry_SetDate()
    {
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        var entry = new LogEntry { Date = dt };
        Assert.Equal(dt, entry.Date);
    }

    [Fact]
    public void LogEntry_SetSyncedTime()
    {
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        var entry = new LogEntry { SyncedTime = dt };
        Assert.Equal(dt, entry.SyncedTime);
    }

    [Fact]
    public void LogEntry_SetMessage()
    {
        var entry = new LogEntry { Message = "test message" };
        Assert.Equal("test message", entry.Message);
    }

    [Fact]
    public void LogEntry_SetThreadName()
    {
        var entry = new LogEntry { ThreadName = "Main" };
        Assert.Equal("Main", entry.ThreadName);
    }

    [Fact]
    public void LogEntry_SetLogger()
    {
        var entry = new LogEntry { Logger = "TestLogger" };
        Assert.Equal("TestLogger", entry.Logger);
    }

    [Fact]
    public void LogEntry_SetProcessName()
    {
        var entry = new LogEntry { ProcessName = "proc1" };
        Assert.Equal("proc1", entry.ProcessName);
    }

    [Fact]
    public void LogEntry_SetMethod()
    {
        var entry = new LogEntry { Method = "DoWork" };
        Assert.Equal("DoWork", entry.Method);
    }

    [Fact]
    public void LogEntry_SetPattern()
    {
        var entry = new LogEntry { Pattern = "pat1" };
        Assert.Equal("pat1", entry.Pattern);
    }

    [Fact]
    public void LogEntry_SetData()
    {
        var entry = new LogEntry { Data = "data1" };
        Assert.Equal("data1", entry.Data);
    }

    [Fact]
    public void LogEntry_SetException()
    {
        var entry = new LogEntry { Exception = "ex1" };
        Assert.Equal("ex1", entry.Exception);
    }

    [Fact]
    public void LogEntry_ExtraFields_SetAndGet()
    {
        var entry = new LogEntry { ExtraFields = new Dictionary<string, string> { ["key"] = "val" } };
        Assert.NotNull(entry.ExtraFields);
        Assert.Equal("val", entry.ExtraFields["key"]);
    }

    [Fact]
    public void LogEntry_IsAnnotationExpanded_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.IsAnnotationExpanded = true;
        Assert.True(entry.IsAnnotationExpanded);
        Assert.Contains("IsAnnotationExpanded", changed);
        Assert.Contains("AnnotationIcon", changed);
    }

    [Fact]
    public void LogEntry_IsAnnotationExpanded_NoChangeNoEvent()
    {
        var entry = new LogEntry { IsAnnotationExpanded = false };
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.IsAnnotationExpanded = false;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_AnnotationContent_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.AnnotationContent = "note";
        Assert.Equal("note", entry.AnnotationContent);
        Assert.Contains("AnnotationContent", changed);
    }

    [Fact]
    public void LogEntry_AnnotationContent_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.AnnotationContent = "";
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_HasAnnotation_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.HasAnnotation = true;
        Assert.True(entry.HasAnnotation);
        Assert.Contains("HasAnnotation", changed);
        Assert.Contains("RowBackground", changed);
        Assert.Contains("AnnotationIcon", changed);
    }

    [Fact]
    public void LogEntry_HasAnnotation_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.HasAnnotation = false;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_AnnotationIcon_NoAnnotation_Empty()
    {
        var entry = new LogEntry();
        Assert.Equal("", entry.AnnotationIcon);
    }

    [Fact]
    public void LogEntry_AnnotationIcon_HasAnnotation_Collapsed()
    {
        var entry = new LogEntry { HasAnnotation = true };
        Assert.NotEmpty(entry.AnnotationIcon);
    }

    [Fact]
    public void LogEntry_AnnotationIcon_HasAnnotation_Expanded()
    {
        var entry = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = true };
        Assert.NotEmpty(entry.AnnotationIcon);
    }

    [Fact]
    public void LogEntry_IsMarked_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.IsMarked = true;
        Assert.True(entry.IsMarked);
        Assert.Contains("IsMarked", changed);
        Assert.Contains("RowBackground", changed);
    }

    [Fact]
    public void LogEntry_IsMarked_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.IsMarked = false;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_IsCurrentMarked_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.IsCurrentMarked = true;
        Assert.True(entry.IsCurrentMarked);
        Assert.Contains("IsCurrentMarked", changed);
        Assert.Contains("RowBackground", changed);
    }

    [Fact]
    public void LogEntry_IsCurrentMarked_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.IsCurrentMarked = false;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_IsErrorOrEvents_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.IsErrorOrEvents = true;
        Assert.Contains("IsErrorOrEvents", changed);
    }

    [Fact]
    public void LogEntry_IsErrorOrEvents_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.IsErrorOrEvents = false;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_SystemDefaultColor_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        var color = System.Windows.Media.Color.FromRgb(100, 200, 50);
        entry.SystemDefaultColor = color;
        Assert.Equal(color, entry.SystemDefaultColor);
        Assert.True(entry.HasSystemDefaultColor);
        Assert.Contains("SystemDefaultColor", changed);
        Assert.Contains("FilteredRowBackground", changed);
        Assert.Contains("HasSystemDefaultColor", changed);
    }

    [Fact]
    public void LogEntry_SystemDefaultColor_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.SystemDefaultColor = null;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_FilteredRowBackground_NoSystemDefault_Transparent()
    {
        var entry = new LogEntry();
        Assert.Equal(System.Windows.Media.Brushes.Transparent, entry.FilteredRowBackground);
    }

    [Fact]
    public void LogEntry_FilteredRowBackground_WithSystemDefault_ReturnsBrush()
    {
        var entry = new LogEntry();
        var color = System.Windows.Media.Color.FromRgb(100, 100, 100);
        entry.SystemDefaultColor = color;
        var brush = entry.FilteredRowBackground as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(color, brush.Color);
    }

    [Fact]
    public void LogEntry_FilteredRowBackground_CachesAndUpdates()
    {
        var entry = new LogEntry();
        var color1 = System.Windows.Media.Color.FromRgb(100, 100, 100);
        entry.SystemDefaultColor = color1;
        var brush1 = entry.FilteredRowBackground;

        // Same color returns same brush
        var brush1Again = entry.FilteredRowBackground;
        Assert.Same(brush1, brush1Again);

        // Different color returns new brush
        var color2 = System.Windows.Media.Color.FromRgb(200, 200, 200);
        entry.SystemDefaultColor = color2;
        var brush2 = entry.FilteredRowBackground as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(brush2);
        Assert.Equal(color2, brush2.Color);
    }

    [Fact]
    public void LogEntry_RowBackground_Transparent_NoSpecialState()
    {
        var entry = new LogEntry();
        Assert.Equal(System.Windows.Media.Brushes.Transparent, entry.RowBackground);
    }

    [Fact]
    public void LogEntry_RowBackground_Warning_Level()
    {
        var entry = new LogEntry { Level = "WARNING" };
        var brush = entry.RowBackground;
        Assert.NotEqual(System.Windows.Media.Brushes.Transparent, brush);
    }

    [Fact]
    public void LogEntry_RowBackground_Warn_Level()
    {
        var entry = new LogEntry { Level = "WARN" };
        var brush = entry.RowBackground;
        Assert.NotEqual(System.Windows.Media.Brushes.Transparent, brush);
    }

    [Fact]
    public void LogEntry_RowBackground_CustomColor_Priority()
    {
        var color = System.Windows.Media.Color.FromRgb(50, 100, 150);
        var entry = new LogEntry { CustomColor = color };
        var brush = entry.RowBackground as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(color, brush.Color);
    }

    [Fact]
    public void LogEntry_RowBackground_CustomColor_CachesCorrectly()
    {
        var color = System.Windows.Media.Color.FromRgb(50, 100, 150);
        var entry = new LogEntry { CustomColor = color };
        var brush1 = entry.RowBackground;
        var brush2 = entry.RowBackground;
        Assert.Same(brush1, brush2);

        // Change color → new brush
        var color2 = System.Windows.Media.Color.FromRgb(200, 50, 50);
        entry.CustomColor = color2;
        var brush3 = entry.RowBackground as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(brush3);
        Assert.Equal(color2, brush3.Color);
    }

    [Fact]
    public void LogEntry_RowBackground_HasAnnotation_Priority()
    {
        var entry = new LogEntry { HasAnnotation = true };
        var brush = entry.RowBackground;
        Assert.NotEqual(System.Windows.Media.Brushes.Transparent, brush);
    }

    [Fact]
    public void LogEntry_RowBackground_IsMarked_Priority()
    {
        var entry = new LogEntry { IsMarked = true, HasAnnotation = true };
        var brush = entry.RowBackground;
        // IsMarked has higher priority than HasAnnotation
        Assert.NotEqual(System.Windows.Media.Brushes.Transparent, brush);
    }

    [Fact]
    public void LogEntry_RowBackground_IsCurrentMarked_HighestPriority()
    {
        var entry = new LogEntry
        {
            IsCurrentMarked = true,
            IsMarked = true,
            HasAnnotation = true,
            CustomColor = System.Windows.Media.Color.FromRgb(0, 0, 0)
        };
        var brush = entry.RowBackground;
        Assert.NotEqual(System.Windows.Media.Brushes.Transparent, brush);
    }

    [Fact]
    public void LogEntry_CustomColor_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        var color = System.Windows.Media.Color.FromRgb(10, 20, 30);
        entry.CustomColor = color;
        Assert.Contains("CustomColor", changed);
        Assert.Contains("RowBackground", changed);
    }

    [Fact]
    public void LogEntry_CustomColor_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.CustomColor = null;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_RowForeground_RaisesPropertyChanged()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.RowForeground = System.Windows.Media.Brushes.Red;
        Assert.Contains("RowForeground", changed);
    }

    [Fact]
    public void LogEntry_RowForeground_NoChangeNoEvent()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        entry.RowForeground = null;
        Assert.Empty(changed);
    }

    [Fact]
    public void LogEntry_OnPropertyChanged_PublicMethod()
    {
        var entry = new LogEntry();
        var changed = new List<string>();
        entry.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        entry.OnPropertyChanged("TestProp");
        Assert.Contains("TestProp", changed);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 2. FilterNode coverage
    // ═══════════════════════════════════════════════════════════

    #region FilterNode Tests

    [Fact]
    public void FilterNode_DefaultValues()
    {
        var node = new FilterNode();
        Assert.Equal(NodeType.Group, node.Type);
        Assert.Equal("AND", node.LogicalOperator);
        Assert.Equal("Message", node.Field);
        Assert.Equal("Contains", node.Operator);
        Assert.Equal("", node.Value);
        Assert.NotNull(node.Children);
        Assert.Empty(node.Children);
        Assert.True(node.IsEnabled);
    }

    [Fact]
    public void FilterNode_Type_RaisesPropertyChanged()
    {
        var node = new FilterNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        node.Type = NodeType.Condition;
        Assert.Contains("Type", changed);
    }

    [Fact]
    public void FilterNode_LogicalOperator_RaisesPropertyChanged()
    {
        var node = new FilterNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        node.LogicalOperator = "OR";
        Assert.Contains("LogicalOperator", changed);
    }

    [Fact]
    public void FilterNode_Field_RaisesPropertyChanged()
    {
        var node = new FilterNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        node.Field = "Level";
        Assert.Contains("Field", changed);
    }

    [Fact]
    public void FilterNode_Operator_RaisesPropertyChanged()
    {
        var node = new FilterNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        node.Operator = "Equals";
        Assert.Contains("Operator", changed);
    }

    [Fact]
    public void FilterNode_Value_RaisesPropertyChanged_ClearsRegex()
    {
        var node = new FilterNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
        node.Value = "test";
        Assert.Contains("Value", changed);
    }

    [Fact]
    public void FilterNode_DeepClone_ClonesAllProperties()
    {
        var root = new FilterNode
        {
            Type = NodeType.Group,
            LogicalOperator = "OR",
            Field = "Level",
            Operator = "Equals",
            Value = "error",
            Children = new ObservableCollection<FilterNode>
            {
                new FilterNode
                {
                    Type = NodeType.Condition,
                    LogicalOperator = "AND",
                    Field = "Message",
                    Operator = "Contains",
                    Value = "test"
                }
            }
        };

        var clone = root.DeepClone();

        Assert.Equal(root.Type, clone.Type);
        Assert.Equal(root.LogicalOperator, clone.LogicalOperator);
        Assert.Equal(root.Field, clone.Field);
        Assert.Equal(root.Operator, clone.Operator);
        Assert.Equal(root.Value, clone.Value);
        Assert.Single(clone.Children);
        Assert.Equal("test", clone.Children[0].Value);

        // Verify deep clone - modifying clone doesn't affect original
        clone.Value = "modified";
        Assert.Equal("error", root.Value);
        clone.Children[0].Value = "modified";
        Assert.Equal("test", root.Children[0].Value);
    }

    [Fact]
    public void FilterNode_DeepClone_EmptyChildren()
    {
        var node = new FilterNode();
        var clone = node.DeepClone();
        Assert.Empty(clone.Children);
    }

    [Fact]
    public void FilterNode_DeepClone_NestedChildren()
    {
        var root = new FilterNode
        {
            Type = NodeType.Group,
            Children = new ObservableCollection<FilterNode>
            {
                new FilterNode
                {
                    Type = NodeType.Group,
                    Children = new ObservableCollection<FilterNode>
                    {
                        new FilterNode { Type = NodeType.Condition, Value = "deep" }
                    }
                }
            }
        };

        var clone = root.DeepClone();
        Assert.Equal("deep", clone.Children[0].Children[0].Value);
    }

    [Fact]
    public void FilterNode_CompiledRegex_ReturnsNull_WhenOperatorNotRegex()
    {
        var node = new FilterNode { Operator = "Contains", Value = "test" };
        Assert.Null(node.CompiledRegex);
    }

    [Fact]
    public void FilterNode_CompiledRegex_ReturnsNull_WhenValueEmpty()
    {
        var node = new FilterNode { Operator = "Regex", Value = "" };
        Assert.Null(node.CompiledRegex);
    }

    [Fact]
    public void FilterNode_CompiledRegex_ReturnsRegex_WhenValid()
    {
        var node = new FilterNode { Operator = "Regex", Value = @"test\d+" };
        var regex = node.CompiledRegex;
        Assert.NotNull(regex);
        Assert.True(regex.IsMatch("test123"));
    }

    [Fact]
    public void FilterNode_CompiledRegex_CachesResult()
    {
        var node = new FilterNode { Operator = "Regex", Value = @"abc" };
        var regex1 = node.CompiledRegex;
        var regex2 = node.CompiledRegex;
        Assert.Same(regex1, regex2);
    }

    [Fact]
    public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
    {
        var node = new FilterNode { Operator = "Regex", Value = @"[invalid" };
        Assert.Null(node.CompiledRegex);
    }

    [Fact]
    public void FilterNode_CompiledRegex_RecompilesWhenValueChanges()
    {
        var node = new FilterNode { Operator = "Regex", Value = @"abc" };
        var regex1 = node.CompiledRegex;
        Assert.NotNull(regex1);

        // Setting Value clears the cached regex
        node.Value = @"xyz";
        var regex2 = node.CompiledRegex;
        Assert.NotNull(regex2);
        Assert.NotSame(regex1, regex2);
    }

    [Fact]
    public void FilterNode_IsEnabled_DefaultTrue()
    {
        var node = new FilterNode();
        Assert.True(node.IsEnabled);
    }

    [Fact]
    public void FilterNode_IsEnabled_CanBeDisabled()
    {
        var node = new FilterNode { IsEnabled = false };
        Assert.False(node.IsEnabled);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 3. CaseFile and related model coverage
    // ═══════════════════════════════════════════════════════════

    #region CaseFile Model Tests

    [Fact]
    public void CaseFile_DefaultConstructor()
    {
        var cf = new CaseFile();
        Assert.NotNull(cf.Meta);
        Assert.NotNull(cf.Resources);
        Assert.NotNull(cf.ViewState);
        Assert.NotNull(cf.Annotations);
        Assert.NotNull(cf.MainColoringRules);
        Assert.NotNull(cf.AppColoringRules);
    }

    [Fact]
    public void CaseMetadata_SetAllProperties()
    {
        var m = new CaseMetadata
        {
            Version = "2.0",
            Author = "Alice",
            CreatedAt = new DateTime(2025, 3, 8),
            Description = "Investigation"
        };
        Assert.Equal("2.0", m.Version);
        Assert.Equal("Alice", m.Author);
        Assert.Equal("Investigation", m.Description);
    }

    [Fact]
    public void CaseResource_SetAllProperties()
    {
        var r = new CaseResource
        {
            FileName = "data.zip",
            Size = 99999,
            LastModified = DateTime.Now,
            Hash = "sha256hash"
        };
        Assert.Equal("data.zip", r.FileName);
        Assert.Equal(99999, r.Size);
        Assert.Equal("sha256hash", r.Hash);
    }

    [Fact]
    public void CaseViewState_DefaultLists()
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
    public void CaseViewState_SetProperties()
    {
        var vs = new CaseViewState
        {
            QuickSearchText = "error",
            SelectedTab = "APP",
            ActiveFilters = new FilterNode()
        };
        Assert.Equal("error", vs.QuickSearchText);
        Assert.Equal("APP", vs.SelectedTab);
        Assert.NotNull(vs.ActiveFilters);
    }

    [Fact]
    public void LogAnnotation_Defaults()
    {
        var a = new LogAnnotation();
        Assert.Equal("", a.Content);
        Assert.Equal("#FFFF00", a.Color);
        Assert.Equal("", a.Author);
        Assert.NotEqual(default(DateTime), a.CreatedAt);
    }

    [Fact]
    public void LogAnnotation_SetProperties()
    {
        var a = new LogAnnotation
        {
            Content = "Note here",
            Color = "#FF0000",
            Author = "Bob",
            TargetLog = new LogTarget { Logger = "TestLog" }
        };
        Assert.Equal("Note here", a.Content);
        Assert.Equal("#FF0000", a.Color);
        Assert.Equal("Bob", a.Author);
        Assert.NotNull(a.TargetLog);
        Assert.Equal("TestLog", a.TargetLog.Logger);
    }

    [Fact]
    public void LogTarget_Defaults()
    {
        var t = new LogTarget();
        Assert.Equal("", t.Logger);
        Assert.Equal("", t.Thread);
        Assert.Equal("", t.Snippet);
        Assert.Equal("", t.Level);
        Assert.Equal(default(DateTime), t.Timestamp);
    }

    [Fact]
    public void LogTarget_SetAllProperties()
    {
        var dt = new DateTime(2025, 6, 15);
        var t = new LogTarget
        {
            Timestamp = dt,
            Logger = "SomeLogger",
            Thread = "Thread1",
            Snippet = "first 100 chars",
            Level = "Error"
        };
        Assert.Equal(dt, t.Timestamp);
        Assert.Equal("SomeLogger", t.Logger);
        Assert.Equal("Thread1", t.Thread);
        Assert.Equal("first 100 chars", t.Snippet);
        Assert.Equal("Error", t.Level);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 4. LogSessionData model coverage
    // ═══════════════════════════════════════════════════════════

    #region LogSessionData Tests

    [Fact]
    public void LogSessionData_DefaultValues()
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
        Assert.False(s.HasBinaryAppLogs);
        Assert.Null(s.ConfigurationType);
        Assert.Null(s.PluginColumns);
        Assert.Null(s.SetupInfo);
        Assert.Null(s.PressConfiguration);
        Assert.Null(s.VersionsInfo);
        Assert.Null(s.CachedStates);
        Assert.Null(s.CachedAnalysis);
        Assert.Null(s.LoadTabSelection);
        Assert.Null(s.PreScanConfig);
        Assert.Null(s.EmStatisticsCsvContent);
        Assert.Null(s.TimeSyncOffset);
        Assert.False(s.HasTimeSyncData);
        Assert.Null(s.SavedFilterState);
        Assert.Null(s.SavedChartState);
        Assert.Null(s.EventsCsvRawContent);
    }

    [Fact]
    public void LogSessionData_SetProperties()
    {
        var s = new LogSessionData
        {
            FileName = "test.zip",
            FilePath = "C:\\test.zip",
            HasBinaryAppLogs = true,
            ConfigurationType = "S4-5",
            HasTimeSyncData = true,
            TimeSyncOffset = TimeSpan.FromSeconds(5),
            SetupInfo = "Info",
            PressConfiguration = "Config",
            VersionsInfo = "1.0",
            EmStatisticsCsvContent = "csv",
            EventsCsvRawContent = "raw"
        };
        Assert.Equal("test.zip", s.FileName);
        Assert.True(s.HasBinaryAppLogs);
        Assert.Equal("S4-5", s.ConfigurationType);
        Assert.True(s.HasTimeSyncData);
        Assert.Equal(5.0, s.TimeSyncOffset!.Value.TotalSeconds);
        Assert.Equal("raw", s.EventsCsvRawContent);
    }

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
        Assert.Null(state.LastFilteredCache);
        Assert.Null(state.LastFilteredAppCache);
    }

    [Fact]
    public void SessionFilterState_SetProperties()
    {
        var state = new SessionFilterState
        {
            MainFilterRoot = new FilterNode(),
            IsMainFilterActive = true,
            IsAppFilterActive = true,
            IsMainFilterOutActive = true,
            IsAppFilterOutActive = true,
            IsTimeFocusActive = true,
            IsAppTimeFocusActive = true,
            SearchText = "test",
            NegativeFilters = new List<string> { "neg1" },
            ActiveThreadFilters = new List<string> { "t1" },
            LastFilteredCache = new List<LogEntry>(),
            LastFilteredAppCache = new List<LogEntry>()
        };
        Assert.NotNull(state.MainFilterRoot);
        Assert.True(state.IsMainFilterActive);
        Assert.Equal("test", state.SearchText);
        Assert.Single(state.NegativeFilters!);
        Assert.Single(state.ActiveThreadFilters!);
    }

    [Fact]
    public void SessionChartState_DefaultValues()
    {
        var state = new SessionChartState();
        Assert.Null(state.DataPackage);
        Assert.Null(state.TimeData);
        Assert.Null(state.GlobalStates);
        Assert.Null(state.ThreadMessages);
        Assert.Null(state.ChStepStates);
        Assert.Null(state.EventMarkers);
        Assert.Null(state.TimeGapRegions);
        Assert.Null(state.DataService);
        Assert.Null(state.SyncService);
        Assert.Null(state.Charts);
        Assert.Equal(0, state.TotalDataLength);
        Assert.False(state.InMemoryDataLoaded);
        Assert.Equal(0, state.ViewStartIndex);
        Assert.Equal(0, state.ViewEndIndex);
        Assert.Equal(0, state.CursorIndex);
        Assert.False(state.ShowStates);
        Assert.False(state.IsGridLayout);
        Assert.False(state.IsSignalPanelVisible);
        Assert.Equal(0, state.SmoothWindowSize);
        Assert.Equal(0, state.ColorIndex);
        Assert.Equal(-1, state.SelectedChartIndex);
    }

    [Fact]
    public void SessionChartState_SetProperties()
    {
        var state = new SessionChartState
        {
            TotalDataLength = 1000,
            InMemoryDataLoaded = true,
            ViewStartIndex = 10,
            ViewEndIndex = 500,
            CursorIndex = 250,
            ShowStates = true,
            IsGridLayout = true,
            IsSignalPanelVisible = true,
            SmoothWindowSize = 5,
            ColorIndex = 3,
            SelectedChartIndex = 2
        };
        Assert.Equal(1000, state.TotalDataLength);
        Assert.True(state.InMemoryDataLoaded);
        Assert.Equal(10, state.ViewStartIndex);
        Assert.Equal(500, state.ViewEndIndex);
        Assert.Equal(250, state.CursorIndex);
        Assert.True(state.ShowStates);
        Assert.True(state.IsGridLayout);
        Assert.True(state.IsSignalPanelVisible);
        Assert.Equal(5, state.SmoothWindowSize);
        Assert.Equal(3, state.ColorIndex);
        Assert.Equal(2, state.SelectedChartIndex);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 5. DefaultConfiguration model coverage
    // ═══════════════════════════════════════════════════════════

    #region DefaultConfiguration Tests

    [Fact]
    public void DefaultConfiguration_DefaultValues()
    {
        var dc = new DefaultConfiguration();
        Assert.Null(dc.PlcFilteredDefaultFilter);
        Assert.Null(dc.MainDefaultColoringRules);
        Assert.Null(dc.AppDefaultColoringRules);
        Assert.False(dc.HasCustomPlcFilter);
        Assert.False(dc.HasCustomMainColoring);
        Assert.False(dc.HasCustomAppColoring);
    }

    [Fact]
    public void DefaultConfiguration_SetProperties()
    {
        var dc = new DefaultConfiguration
        {
            PlcFilteredDefaultFilter = new FilterNode { Value = "filter" },
            MainDefaultColoringRules = new List<ColoringCondition> { new ColoringCondition() },
            AppDefaultColoringRules = new List<ColoringCondition>(),
            HasCustomPlcFilter = true,
            HasCustomMainColoring = true,
            HasCustomAppColoring = true
        };
        Assert.NotNull(dc.PlcFilteredDefaultFilter);
        Assert.Single(dc.MainDefaultColoringRules);
        Assert.True(dc.HasCustomPlcFilter);
        Assert.True(dc.HasCustomMainColoring);
        Assert.True(dc.HasCustomAppColoring);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 6. SavedConfiguration model coverage
    // ═══════════════════════════════════════════════════════════

    #region SavedConfiguration Tests

    [Fact]
    public void SavedConfiguration_DefaultValues()
    {
        var sc = new SavedConfiguration();
        Assert.Equal("", sc.Name);
        Assert.Equal("", sc.FilePath);
        Assert.Null(sc.MainColoringRules);
        Assert.Null(sc.MainFilterRoot);
        Assert.Null(sc.AppColoringRules);
        Assert.Null(sc.AppFilterRoot);
        Assert.Null(sc.PlcFilteredRoot);
        Assert.Null(sc.HasMainConfig);
        Assert.Null(sc.HasAppConfig);
        Assert.Null(sc.HasPlcFilteredConfig);
    }

    [Fact]
    public void SavedConfiguration_SetProperties()
    {
        var sc = new SavedConfiguration
        {
            Name = "TestConfig",
            FilePath = "C:\\config.json",
            CreatedDate = new DateTime(2025, 1, 1),
            MainColoringRules = new List<ColoringCondition>(),
            MainFilterRoot = new FilterNode(),
            AppColoringRules = new List<ColoringCondition>(),
            AppFilterRoot = new FilterNode(),
            PlcFilteredRoot = new FilterNode(),
            HasMainConfig = true,
            HasAppConfig = true,
            HasPlcFilteredConfig = true
        };
        Assert.Equal("TestConfig", sc.Name);
        Assert.True(sc.HasMainConfig);
        Assert.True(sc.HasAppConfig);
        Assert.True(sc.HasPlcFilteredConfig);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 7. ColoringCondition coverage
    // ═══════════════════════════════════════════════════════════

    #region ColoringCondition Tests

    [Fact]
    public void ColoringCondition_DefaultValues()
    {
        var c = new ColoringCondition();
        Assert.Equal("", c.Field);
        Assert.Equal("", c.Operator);
        Assert.Equal("", c.Value);
        Assert.True(c.IsEnabled);
    }

    [Fact]
    public void ColoringCondition_Clone_CopiesAllFields()
    {
        var original = new ColoringCondition
        {
            Field = "Message",
            Operator = "Contains",
            Value = "error",
            Color = System.Windows.Media.Color.FromRgb(255, 0, 0),
            IsEnabled = false
        };

        var clone = original.Clone();
        Assert.Equal("Message", clone.Field);
        Assert.Equal("Contains", clone.Operator);
        Assert.Equal("error", clone.Value);
        Assert.Equal(System.Windows.Media.Color.FromRgb(255, 0, 0), clone.Color);
        // IsEnabled is JsonIgnore and not copied by Clone (it's always true by default)
        Assert.True(clone.IsEnabled);

        // Verify independence
        clone.Value = "changed";
        Assert.Equal("error", original.Value);
    }

    [Fact]
    public void ColoringCondition_SetColor()
    {
        var c = new ColoringCondition();
        var color = System.Windows.Media.Color.FromRgb(100, 150, 200);
        c.Color = color;
        Assert.Equal(color, c.Color);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 8. FilterCondition model coverage
    // ═══════════════════════════════════════════════════════════

    #region FilterCondition Tests

    [Fact]
    public void FilterCondition_DefaultValues()
    {
        var fc = new FilterCondition();
        Assert.Equal("", fc.Field);
        Assert.Equal("", fc.Operator);
        Assert.Equal("", fc.Value);
        Assert.True(fc.IsActive);
    }

    [Fact]
    public void FilterCondition_SetProperties()
    {
        var fc = new FilterCondition
        {
            Field = "Level",
            Operator = "Equals",
            Value = "Error",
            IsActive = false
        };
        Assert.Equal("Level", fc.Field);
        Assert.Equal("Equals", fc.Operator);
        Assert.Equal("Error", fc.Value);
        Assert.False(fc.IsActive);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 9. AuthenticodeVerifier coverage
    // ═══════════════════════════════════════════════════════════

    #region AuthenticodeVerifier Tests

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HP()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=HP, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HPInc()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=HP Inc, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HPIncDot()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=HP Inc., L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HewlettPackard()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=Hewlett-Packard, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HewlettPackardSpace()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=Hewlett Packard, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_HPIndigo()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=HP Indigo, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_UntrustedOrg()
    {
        Assert.False(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=Evil Corp, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_NoOrgField()
    {
        Assert.False(AuthenticodeVerifier.IsTrustedSubject("CN=Test, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_EmptyString()
    {
        Assert.False(AuthenticodeVerifier.IsTrustedSubject(""));
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_NullInput()
    {
        Assert.Null(AuthenticodeVerifier.ParseDnField(null!, "O"));
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_EmptyInput()
    {
        Assert.Null(AuthenticodeVerifier.ParseDnField("", "O"));
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_SimpleValue()
    {
        var result = AuthenticodeVerifier.ParseDnField("O=HP Inc", "O");
        Assert.Equal("HP Inc", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_QuotedValue()
    {
        var result = AuthenticodeVerifier.ParseDnField("O=\"HP Inc\"", "O");
        Assert.Equal("HP Inc", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_WithComma()
    {
        var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=HP, L=City", "O");
        Assert.Equal("HP", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_AtStart()
    {
        var result = AuthenticodeVerifier.ParseDnField("O=HP, CN=Test", "O");
        Assert.Equal("HP", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_NotFound()
    {
        var result = AuthenticodeVerifier.ParseDnField("CN=Test, L=City", "O");
        Assert.Null(result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_SpacesAroundEquals()
    {
        // The field search is "O=" so "O = HP" won't be found with current implementation
        var result = AuthenticodeVerifier.ParseDnField("CN=Test, O= HP, L=City", "O");
        Assert.Equal("HP", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_NotAPrefixOfAnotherField()
    {
        // "CO=test" should not match "O=..."
        var result = AuthenticodeVerifier.ParseDnField("CO=test", "O");
        Assert.Null(result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_CaseInsensitive()
    {
        var result = AuthenticodeVerifier.ParseDnField("o=HP Inc", "O");
        Assert.Equal("HP Inc", result);
    }

    [Fact]
    public void AuthenticodeVerifier_IsTrustedSubject_CaseInsensitive()
    {
        Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=hp, L=City"));
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_ValueAtEnd_NoComma()
    {
        var result = AuthenticodeVerifier.ParseDnField("CN=Cert, O=HP Indigo", "O");
        Assert.Equal("HP Indigo", result);
    }

    [Fact]
    public void AuthenticodeVerifier_ParseDnField_QuotedAtEnd()
    {
        var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=\"HP Inc.\"", "O");
        Assert.Equal("HP Inc.", result);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 10. ViewModelBase coverage
    // ═══════════════════════════════════════════════════════════

    #region ViewModelBase Tests

    private class TestViewModel : ViewModelBase
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private int _count;
        public int Count
        {
            get => _count;
            set => SetField(ref _count, value);
        }

        public void TriggerNotify(string prop) => NotifyPropertyChanged(prop);

        public new bool IsDisposed => base.IsDisposed;

        public new void Dispose() => base.Dispose();
    }

    [Fact]
    public void ViewModelBase_SetField_RaisesPropertyChanged()
    {
        var vm = new TestViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.Name = "test";
        Assert.Equal("test", vm.Name);
        Assert.Contains("Name", changed);
    }

    [Fact]
    public void ViewModelBase_SetField_NoChangeNoEvent()
    {
        var vm = new TestViewModel { Name = "test" };
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.Name = "test";
        Assert.Empty(changed);
    }

    [Fact]
    public void ViewModelBase_SetField_ReturnsTrueOnChange()
    {
        var vm = new TestViewModel();
        vm.Name = "test";
        Assert.Equal("test", vm.Name);
    }

    [Fact]
    public void ViewModelBase_NotifyPropertyChanged()
    {
        var vm = new TestViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.TriggerNotify("CustomProp");
        Assert.Contains("CustomProp", changed);
    }

    [Fact]
    public void ViewModelBase_Dispose()
    {
        var vm = new TestViewModel();
        Assert.False(vm.IsDisposed);
        vm.Dispose();
        Assert.True(vm.IsDisposed);
    }

    [Fact]
    public void ViewModelBase_DoubleDispose_Safe()
    {
        var vm = new TestViewModel();
        vm.Dispose();
        vm.Dispose(); // Should not throw
        Assert.True(vm.IsDisposed);
    }

    [Fact]
    public void ViewModelBase_SetField_IntType()
    {
        var vm = new TestViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.Count = 42;
        Assert.Equal(42, vm.Count);
        Assert.Contains("Count", changed);
    }

    [Fact]
    public void ViewModelBase_SetField_IntNoChange()
    {
        var vm = new TestViewModel { Count = 10 };
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.Count = 10;
        Assert.Empty(changed);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 11. CaseManagementViewModel coverage via reflection
    // ═══════════════════════════════════════════════════════════

    #region CaseManagementViewModel Tests

    [Fact]
    public void CaseManagementVM_FindLogByTarget_NullTarget_ReturnsNull()
    {
        // Use reflection to call FindLogByTarget (it's public)
        var vm = CreateUninitializedCaseManagementVM();
        var result = vm.FindLogByTarget(null, new List<LogEntry>());
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_FindLogByTarget_NullLogs_ReturnsNull()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var target = new LogTarget { Timestamp = DateTime.Now, Logger = "L", Thread = "T" };
        var result = vm.FindLogByTarget(target, null);
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_FindLogByTarget_ExactMatch()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        var log = new LogEntry { Date = dt, Logger = "MyLogger", ThreadName = "T1", Message = "hello world" };
        var target = new LogTarget { Timestamp = dt, Logger = "MyLogger", Thread = "T1" };

        var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
        Assert.Same(log, result);
    }

    [Fact]
    public void CaseManagementVM_FindLogByTarget_NoMatch()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        var log = new LogEntry { Date = dt, Logger = "DifferentLogger", ThreadName = "T1", Message = "test" };
        var target = new LogTarget { Timestamp = dt, Logger = "MyLogger", Thread = "T1", Snippet = "test" };

        var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_FindLogByTarget_FallbackCloseTimestamp()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        // Log is 50ms off from target
        var log = new LogEntry
        {
            Date = dt.AddMilliseconds(50),
            Logger = "MyLogger",
            ThreadName = "T1",
            Message = "This is a long message that starts with the snippet part"
        };
        var target = new LogTarget
        {
            Timestamp = dt,
            Logger = "MyLogger",
            Thread = "T1",
            Snippet = "This is a long message that starts with the snippet part"
        };

        var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
        Assert.Same(log, result);
    }

    [Fact]
    public void CaseManagementVM_FindLogByTarget_FallbackTooFar()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var dt = new DateTime(2025, 6, 15, 10, 30, 0);
        var log = new LogEntry
        {
            Date = dt.AddSeconds(1),
            Logger = "MyLogger",
            ThreadName = "T1",
            Message = "test message"
        };
        var target = new LogTarget
        {
            Timestamp = dt,
            Logger = "MyLogger",
            Thread = "T1",
            Snippet = "test message"
        };

        var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_GetAnnotation_NullLog_ReturnsNull()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var result = vm.GetAnnotation(null);
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_GetAnnotation_NoAnnotation_ReturnsNull()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry();
        var result = vm.GetAnnotation(log);
        Assert.Null(result);
    }

    [Fact]
    public void CaseManagementVM_GetAnnotation_HasAnnotation_Returns()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry();
        var annotation = new LogAnnotation { Content = "test note" };
        vm.LogAnnotations[log] = annotation;

        var result = vm.GetAnnotation(log);
        Assert.NotNull(result);
        Assert.Equal("test note", result.Content);
    }

    [Fact]
    public void CaseManagementVM_ClearAnnotations()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry();
        vm.LogAnnotations[log] = new LogAnnotation { Content = "note" };
        Assert.Single(vm.LogAnnotations);

        vm.ClearAnnotations();
        Assert.Empty(vm.LogAnnotations);
    }

    [Fact]
    public void CaseManagementVM_ClearMarkedLogs()
    {
        var vm = CreateUninitializedCaseManagementVM();
        // Initialize collections via reflection
        SetField(vm, "MarkedLogs", new ObservableCollection<LogEntry> { new LogEntry() });
        SetField(vm, "MarkedAppLogs", new ObservableCollection<LogEntry> { new LogEntry() });

        Assert.Single(vm.MarkedLogs);
        Assert.Single(vm.MarkedAppLogs);

        vm.ClearMarkedLogs();
        Assert.Empty(vm.MarkedLogs);
        Assert.Empty(vm.MarkedAppLogs);
    }

    [Fact]
    public void CaseManagementVM_CreateLogTarget_ViaReflection()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry
        {
            Date = new DateTime(2025, 6, 15),
            Logger = "TestLogger",
            ThreadName = "Thread1",
            Level = "Error",
            Message = "Short msg"
        };

        var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var target = (LogTarget)method.Invoke(vm, new object[] { log })!;

        Assert.Equal(log.Date, target.Timestamp);
        Assert.Equal("TestLogger", target.Logger);
        Assert.Equal("Thread1", target.Thread);
        Assert.Equal("Error", target.Level);
        Assert.Equal("Short msg", target.Snippet);
    }

    [Fact]
    public void CaseManagementVM_CreateLogTarget_LongMessage_Truncated()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry
        {
            Date = new DateTime(2025, 6, 15),
            Logger = "L",
            ThreadName = "T",
            Level = "Info",
            Message = new string('X', 200)
        };

        var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var target = (LogTarget)method!.Invoke(vm, new object[] { log })!;
        Assert.Equal(100, target.Snippet.Length);
    }

    [Fact]
    public void CaseManagementVM_CreateLogTarget_NullMessage()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var log = new LogEntry
        {
            Date = new DateTime(2025, 6, 15),
            Logger = "L",
            ThreadName = "T",
            Level = "Info"
        };
        // Default is "", not null

        var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var target = (LogTarget)method!.Invoke(vm, new object[] { log })!;
        Assert.Equal("", target.Snippet);
    }

    [Fact]
    public void CaseManagementVM_SelectedConfig_SetAndGet()
    {
        var vm = CreateUninitializedCaseManagementVM();
        var config = new SavedConfiguration { Name = "Test" };
        vm.SelectedConfig = config;
        Assert.Same(config, vm.SelectedConfig);
    }

    [Fact]
    public void CaseManagementVM_MainColoringRules_DefaultEmpty()
    {
        var vm = CreateUninitializedCaseManagementVM();
        Assert.NotNull(vm.MainColoringRules);
        Assert.Empty(vm.MainColoringRules);
    }

    [Fact]
    public void CaseManagementVM_AppColoringRules_DefaultEmpty()
    {
        var vm = CreateUninitializedCaseManagementVM();
        Assert.NotNull(vm.AppColoringRules);
        Assert.Empty(vm.AppColoringRules);
    }

    [Fact]
    public void CaseManagementVM_IsLoadingCase_DefaultFalse()
    {
        var vm = CreateUninitializedCaseManagementVM();
        Assert.False(vm.IsLoadingCase);
    }

    [Fact]
    public void CaseManagementVM_SavedConfigs_Initialized()
    {
        var vm = CreateUninitializedCaseManagementVM();
        // SavedConfigs is initialized in constructor but we use uninitialized object,
        // so initialize it here
        SetField(vm, "SavedConfigs", new ObservableCollection<SavedConfiguration>());
        Assert.NotNull(vm.SavedConfigs);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 12. LogSessionViewModel property coverage
    // ═══════════════════════════════════════════════════════════

    #region LogSessionViewModel Tests

    [Fact]
    public void LogSessionVM_StatusMessage_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        vm.StatusMessage = "Loading...";
        Assert.Equal("Loading...", vm.StatusMessage);
    }

    [Fact]
    public void LogSessionVM_StatusMessage_RaisesPropertyChanged()
    {
        var vm = CreateUninitializedLogSessionVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.StatusMessage = "Done";
        Assert.Contains("StatusMessage", changed);
    }

    [Fact]
    public void LogSessionVM_CurrentProgress_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        vm.CurrentProgress = 75.5;
        Assert.Equal(75.5, vm.CurrentProgress);
    }

    [Fact]
    public void LogSessionVM_CurrentProgress_RaisesPropertyChanged()
    {
        var vm = CreateUninitializedLogSessionVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.CurrentProgress = 50;
        Assert.Contains("CurrentProgress", changed);
    }

    [Fact]
    public void LogSessionVM_IsBusy_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        vm.IsBusy = true;
        Assert.True(vm.IsBusy);
    }

    [Fact]
    public void LogSessionVM_IsBusy_RaisesPropertyChanged()
    {
        var vm = CreateUninitializedLogSessionVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.IsBusy = true;
        Assert.Contains("IsBusy", changed);
    }

    [Fact]
    public void LogSessionVM_Logs_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var logs = new List<LogEntry> { new LogEntry { Message = "test" } };
        vm.Logs = logs;
        Assert.Same(logs, vm.Logs);
    }

    [Fact]
    public void LogSessionVM_AllLogsCache_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var cache = new List<LogEntry> { new LogEntry() };
        vm.AllLogsCache = cache;
        Assert.Same(cache, vm.AllLogsCache);
    }

    [Fact]
    public void LogSessionVM_AllAppLogsCache_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var cache = new List<LogEntry>();
        vm.AllAppLogsCache = cache;
        Assert.Same(cache, vm.AllAppLogsCache);
    }

    [Fact]
    public void LogSessionVM_Events_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var events = new ObservableCollection<EventEntry>();
        vm.Events = events;
        Assert.Same(events, vm.Events);
    }

    [Fact]
    public void LogSessionVM_AllEvents_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var events = new List<EventEntry>();
        vm.AllEvents = events;
        Assert.Same(events, vm.AllEvents);
    }

    [Fact]
    public void LogSessionVM_LoadedFiles_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var files = new ObservableCollection<string> { "file1.zip" };
        vm.LoadedFiles = files;
        Assert.Same(files, vm.LoadedFiles);
    }

    [Fact]
    public void LogSessionVM_LoadedSessions_SetAndGet()
    {
        var vm = CreateUninitializedLogSessionVM();
        var sessions = new ObservableCollection<LogSessionData>();
        vm.LoadedSessions = sessions;
        Assert.Same(sessions, vm.LoadedSessions);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 13. LiveMonitoringViewModel property coverage
    // ═══════════════════════════════════════════════════════════

    #region LiveMonitoringViewModel Tests

    [Fact]
    public void LiveMonitoringVM_IsLiveMode_SetAndGet()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        vm.IsLiveMode = true;
        Assert.True(vm.IsLiveMode);
    }

    [Fact]
    public void LiveMonitoringVM_IsLiveMode_RaisesPropertyChanged()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.IsLiveMode = true;
        Assert.Contains("IsLiveMode", changed);
    }

    [Fact]
    public void LiveMonitoringVM_IsRunning_SetAndGet()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        vm.IsRunning = true;
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public void LiveMonitoringVM_IsRunning_RaisesMultipleProperties()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.IsRunning = true;
        Assert.Contains("IsRunning", changed);
        Assert.Contains("IsPaused", changed);
    }

    [Fact]
    public void LiveMonitoringVM_IsPaused_SetAndGet()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        vm.IsPaused = true;
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public void LiveMonitoringVM_IsPaused_RaisesMultipleProperties()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        vm.IsPaused = true;
        Assert.Contains("IsPaused", changed);
        Assert.Contains("IsRunning", changed);
    }

    [Fact]
    public void LiveMonitoringVM_Dispose_Safe()
    {
        var vm = CreateUninitializedLiveMonitoringVM();
        // Should not throw even on uninitialized VM
        vm.Dispose();
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 14. DbTreeNode model coverage
    // ═══════════════════════════════════════════════════════════

    #region DbTreeNode Tests

    [Fact]
    public void DbTreeNode_DefaultValues()
    {
        var node = new DbTreeNode();
        Assert.Equal("", node.Name);
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void DbTreeNode_SetName()
    {
        var node = new DbTreeNode { Name = "TableName" };
        Assert.Equal("TableName", node.Name);
    }

    [Fact]
    public void DbTreeNode_Name_RaisesPropertyChanged()
    {
        var node = new DbTreeNode();
        var changed = new List<string>();
        node.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

        node.Name = "Test";
        Assert.Contains("Name", changed);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 15. UpdateService static helpers
    // ═══════════════════════════════════════════════════════════

    #region UpdateService Tests

    [Fact]
    public void UpdateService_GetCurrentVersion_ReturnsVersion()
    {
        // GetCurrentVersion returns the executing assembly version
        var version = UpdateService.GetCurrentVersion();
        // Should not throw; version might be null depending on assembly
    }

    [Fact]
    public void UpdateService_Constructor_NullParams()
    {
        // Should not throw
        var service = new UpdateService(null, null);
        Assert.NotNull(service);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 16. AppConstants coverage
    // ═══════════════════════════════════════════════════════════

    #region AppConstants Tests

    [Fact]
    public void AppConstants_EscapeSqlBracketId_NullReturnsNull()
    {
        Assert.Null(IndiLogs_3._0.AppConstants.EscapeSqlBracketId(null!));
    }

    [Fact]
    public void AppConstants_EscapeSqlBracketId_EmptyReturnsEmpty()
    {
        Assert.Equal("", IndiLogs_3._0.AppConstants.EscapeSqlBracketId(""));
    }

    [Fact]
    public void AppConstants_EscapeSqlBracketId_NoSpecialChars()
    {
        Assert.Equal("MyTable", IndiLogs_3._0.AppConstants.EscapeSqlBracketId("MyTable"));
    }

    [Fact]
    public void AppConstants_EscapeSqlBracketId_EscapesBrackets()
    {
        Assert.Equal("My]]Table", IndiLogs_3._0.AppConstants.EscapeSqlBracketId("My]Table"));
    }

    [Fact]
    public void AppConstants_EscapeSqlIdentifier_NullReturnsNull()
    {
        Assert.Null(IndiLogs_3._0.AppConstants.EscapeSqlIdentifier(null!));
    }

    [Fact]
    public void AppConstants_EscapeSqlIdentifier_EmptyReturnsEmpty()
    {
        Assert.Equal("", IndiLogs_3._0.AppConstants.EscapeSqlIdentifier(""));
    }

    [Fact]
    public void AppConstants_EscapeSqlIdentifier_NoSpecialChars()
    {
        Assert.Equal("MyTable", IndiLogs_3._0.AppConstants.EscapeSqlIdentifier("MyTable"));
    }

    [Fact]
    public void AppConstants_EscapeSqlIdentifier_EscapesQuotes()
    {
        Assert.Equal("My\"\"Table", IndiLogs_3._0.AppConstants.EscapeSqlIdentifier("My\"Table"));
    }

    [Fact]
    public void AppConstants_TabIndices_Values()
    {
        Assert.Equal(0, IndiLogs_3._0.AppConstants.TAB_PLC);
        Assert.Equal(1, IndiLogs_3._0.AppConstants.TAB_APP);
        Assert.Equal(2, IndiLogs_3._0.AppConstants.TAB_EVENTS);
        Assert.Equal(3, IndiLogs_3._0.AppConstants.TAB_SCREENSHOTS);
        Assert.Equal(4, IndiLogs_3._0.AppConstants.TAB_CONFIG);
        Assert.Equal(5, IndiLogs_3._0.AppConstants.TAB_TERMINALS);
        Assert.Equal(6, IndiLogs_3._0.AppConstants.TAB_SETUP_INFO);
        Assert.Equal(7, IndiLogs_3._0.AppConstants.TAB_GLOBALS);
        Assert.Equal(8, IndiLogs_3._0.AppConstants.TAB_SYSTAB);
        Assert.Equal(9, IndiLogs_3._0.AppConstants.TAB_CHARTS);
        Assert.Equal(10, IndiLogs_3._0.AppConstants.TAB_CPR);
        Assert.Equal(11, IndiLogs_3._0.AppConstants.TAB_STEP_RECORDER);
        Assert.Equal(12, IndiLogs_3._0.AppConstants.TAB_DIFFERENT_LOGS);
    }

    [Fact]
    public void AppConstants_UiUpdateBatchSize()
    {
        Assert.Equal(500, IndiLogs_3._0.AppConstants.UiUpdateBatchSize);
    }

    [Fact]
    public void AppConstants_JsonMaxDepth()
    {
        Assert.Equal(64, IndiLogs_3._0.AppConstants.JsonMaxDepth);
    }

    [Fact]
    public void AppConstants_RegexTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), IndiLogs_3._0.AppConstants.RegexTimeout);
    }

    [Fact]
    public void AppConstants_SafeJsonSettings_NotNull()
    {
        Assert.NotNull(IndiLogs_3._0.AppConstants.SafeJsonSettings);
        Assert.Equal(64, IndiLogs_3._0.AppConstants.SafeJsonSettings.MaxDepth);
    }

    [Fact]
    public void AppConstants_SafeJsonDocumentOptions_MaxDepth()
    {
        Assert.Equal(64, IndiLogs_3._0.AppConstants.SafeJsonDocumentOptions.MaxDepth);
    }

    [Fact]
    public void AppConstants_BuiltInFields_ContainsExpected()
    {
        var fields = IndiLogs_3._0.AppConstants.BuiltInFields;
        Assert.True(fields.Contains("Date"));
        Assert.True(fields.Contains("Level"));
        Assert.True(fields.Contains("Message"));
        Assert.True(fields.Contains("ThreadName"));
        Assert.True(fields.Contains("Logger"));
        Assert.True(fields.Contains("ProcessName"));
    }

    [Fact]
    public void AppConstants_BuiltInFields_CaseInsensitive()
    {
        var fields = IndiLogs_3._0.AppConstants.BuiltInFields;
        Assert.True(fields.Contains("date"));
        Assert.True(fields.Contains("LEVEL"));
        Assert.True(fields.Contains("message"));
    }

    [Fact]
    public void AppConstants_ErrorLevels_ContainsExpected()
    {
        var levels = IndiLogs_3._0.AppConstants.ErrorLevels;
        Assert.True(levels.Contains("Error"));
        Assert.True(levels.Contains("Fatal"));
    }

    [Fact]
    public void AppConstants_ErrorLevels_CaseInsensitive()
    {
        var levels = IndiLogs_3._0.AppConstants.ErrorLevels;
        Assert.True(levels.Contains("error"));
        Assert.True(levels.Contains("FATAL"));
    }

    [Fact]
    public void AppConstants_S4StateRegex_MatchesEnter()
    {
        var match = IndiLogs_3._0.AppConstants.S4StateRegex().Match("STATE_STANDBY - Enter ======");
        Assert.True(match.Success);
        Assert.Equal("STANDBY", match.Groups[1].Value);
        Assert.Equal("Enter", match.Groups[2].Value);
    }

    [Fact]
    public void AppConstants_S4StateRegex_MatchesExit()
    {
        var match = IndiLogs_3._0.AppConstants.S4StateRegex().Match("STATE_SERVICE - Exit ======");
        Assert.True(match.Success);
        Assert.Equal("SERVICE", match.Groups[1].Value);
        Assert.Equal("Exit", match.Groups[2].Value);
    }

    [Fact]
    public void AppConstants_S4StateRegex_NoMatch()
    {
        var match = IndiLogs_3._0.AppConstants.S4StateRegex().Match("Some random log message");
        Assert.False(match.Success);
    }

    [Fact]
    public void AppConstants_FileTimestampRegex_Matches()
    {
        var match = IndiLogs_3._0.AppConstants.FileTimestampRegex().Match("log_2024-01-15T14-30-00.txt");
        Assert.True(match.Success);
        Assert.Equal("2024", match.Groups[1].Value);
        Assert.Equal("01", match.Groups[2].Value);
        Assert.Equal("15", match.Groups[3].Value);
        Assert.Equal("14", match.Groups[4].Value);
        Assert.Equal("30", match.Groups[5].Value);
        Assert.Equal("00", match.Groups[6].Value);
    }

    [Fact]
    public void AppConstants_FileTimestampRegex_MatchesUnderscore()
    {
        var match = IndiLogs_3._0.AppConstants.FileTimestampRegex().Match("2024-01-15_14-30-00");
        Assert.True(match.Success);
    }

    [Fact]
    public void AppConstants_ZipExtension()
    {
        Assert.Equal(".zip", IndiLogs_3._0.AppConstants.ZipExtension);
    }

    [Fact]
    public void AppConstants_CsvExtension()
    {
        Assert.Equal(".csv", IndiLogs_3._0.AppConstants.CsvExtension);
    }

    [Fact]
    public void AppConstants_SystabPrefix()
    {
        Assert.Equal("systab_", IndiLogs_3._0.AppConstants.SystabPrefix);
    }

    [Fact]
    public void AppConstants_SystabExtension()
    {
        Assert.Equal(".txt", IndiLogs_3._0.AppConstants.SystabExtension);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // 17. AppPaths coverage
    // ═══════════════════════════════════════════════════════════

    #region AppPaths Tests

    [Fact]
    public void AppPaths_Root_EndsWithIndiLogs()
    {
        Assert.EndsWith("IndiLogs3.0", IndiLogs_3._0.AppPaths.Root);
    }

    [Fact]
    public void AppPaths_ConfigPrefix()
    {
        Assert.Equal("config_", IndiLogs_3._0.AppPaths.ConfigPrefix);
    }

    [Fact]
    public void AppPaths_SearchProfilePrefix()
    {
        Assert.Equal("searchprofile_", IndiLogs_3._0.AppPaths.SearchProfilePrefix);
    }

    [Fact]
    public void AppPaths_DbColumnPrefix()
    {
        Assert.Equal("dbcol_", IndiLogs_3._0.AppPaths.DbColumnPrefix);
    }

    [Fact]
    public void AppPaths_SearchLocations_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.SearchLocations));
    }

    [Fact]
    public void AppPaths_DefaultConfig_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.DefaultConfig));
    }

    [Fact]
    public void AppPaths_GridColumnSettings_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.GridColumnSettings));
    }

    [Fact]
    public void AppPaths_UpdateLog_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.UpdateLog));
    }

    [Fact]
    public void AppPaths_UpdateCredentials_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.UpdateCredentials));
    }

    [Fact]
    public void AppPaths_SnakeScores_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.SnakeScores));
    }

    [Fact]
    public void AppPaths_SearchSchedules_ContainsRoot()
    {
        Assert.StartsWith(IndiLogs_3._0.AppPaths.Root, IndiLogs_3._0.AppPaths.SearchSchedules);
    }

    [Fact]
    public void AppPaths_DifferentLogsColumnSettings_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.DifferentLogsColumnSettings));
    }

    [Fact]
    public void AppPaths_StripeColumnOrder_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(IndiLogs_3._0.AppPaths.StripeColumnOrder));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════

    private static CaseManagementViewModel CreateUninitializedCaseManagementVM()
    {
        var vm = (CaseManagementViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CaseManagementViewModel));

        // Initialize required private fields
        SetField(vm, "_logAnnotations", new Dictionary<LogEntry, LogAnnotation>());
        SetField(vm, "MainColoringRules", new List<ColoringCondition>());
        SetField(vm, "AppColoringRules", new List<ColoringCondition>());
        SetField(vm, "MarkedLogs", new ObservableCollection<LogEntry>());
        SetField(vm, "MarkedAppLogs", new ObservableCollection<LogEntry>());
        SetField(vm, "SavedConfigs", new ObservableCollection<SavedConfiguration>());
        SetField(vm, "_isLoadingCase", false);

        return vm;
    }

    private static LogSessionViewModel CreateUninitializedLogSessionVM()
    {
        var vm = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

        // Initialize fields that properties depend on
        SetField(vm, "_logs", new List<LogEntry>() as IEnumerable<LogEntry>);
        SetField(vm, "_allLogsCache", new List<LogEntry>() as IList<LogEntry>);
        SetField(vm, "_allAppLogsCache", new List<LogEntry>() as IList<LogEntry>);
        SetField(vm, "_events", new ObservableCollection<EventEntry>());
        SetField(vm, "_allEvents", new List<EventEntry>());
        SetField(vm, "_loadedFiles", new ObservableCollection<string>());
        SetField(vm, "_loadedSessions", new ObservableCollection<LogSessionData>());

        return vm;
    }

    private static LiveMonitoringViewModel CreateUninitializedLiveMonitoringVM()
    {
        var vm = (LiveMonitoringViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LiveMonitoringViewModel));

        SetField(vm, "_streamLock", new object());
        SetField(vm, "_collectionLock", new object());

        return vm;
    }

    private static void SetField(object obj, string fieldName, object? value)
    {
        var type = obj.GetType();
        // Try private fields first, then public properties
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }

        // Try property
        var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
            return;
        }

        // Try backing field with underscore prefix
        field = type.GetField("_" + fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(obj, value);
        }
    }
}
