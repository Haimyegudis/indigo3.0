using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    // ═══════════════════════════════════════════════════════════════
    // DefaultConfigurationService — extended coverage
    // ═══════════════════════════════════════════════════════════════
    public class DefaultConfigServiceExtendedTests
    {
        [Fact]
        public void GetFactoryPlcFilter_ChildrenAllConditions()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();
            foreach (var child in filter.Children)
                Assert.Equal(NodeType.Condition, child.Type);
        }

        [Fact]
        public void GetFactoryPlcFilter_ChildFieldsAreNonEmpty()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();
            foreach (var child in filter.Children)
            {
                Assert.False(string.IsNullOrEmpty(child.Field));
                Assert.False(string.IsNullOrEmpty(child.Operator));
                Assert.False(string.IsNullOrEmpty(child.Value));
            }
        }

        [Fact]
        public void GetFactoryBinaryAppPlcFilter_ChildrenAreConditions()
        {
            var filter = DefaultConfigurationService.GetFactoryBinaryAppPlcFilter();
            foreach (var child in filter.Children)
                Assert.Equal(NodeType.Condition, child.Type);
        }

        [Fact]
        public void GetFactoryBinaryAppPlcFilter_NotCached_NewInstanceEachCall()
        {
            var f1 = DefaultConfigurationService.GetFactoryBinaryAppPlcFilter();
            var f2 = DefaultConfigurationService.GetFactoryBinaryAppPlcFilter();
            Assert.NotSame(f1, f2);
        }

        [Fact]
        public void GetFactoryAppColoringRules_ReturnsNewListEachCall()
        {
            var r1 = DefaultConfigurationService.GetFactoryAppColoringRules();
            var r2 = DefaultConfigurationService.GetFactoryAppColoringRules();
            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void GetFactoryMainColoringRules_ReturnsNewListEachCall()
        {
            var r1 = DefaultConfigurationService.GetFactoryMainColoringRules();
            var r2 = DefaultConfigurationService.GetFactoryMainColoringRules();
            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void GetFactoryBinaryAppColoringRules_ReturnsNewListEachCall()
        {
            var r1 = DefaultConfigurationService.GetFactoryBinaryAppColoringRules();
            var r2 = DefaultConfigurationService.GetFactoryBinaryAppColoringRules();
            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void GetFactoryAppColoringRules_ColorsAreNonDefault()
        {
            var rules = DefaultConfigurationService.GetFactoryAppColoringRules();
            foreach (var rule in rules)
            {
                Assert.NotEqual(default, rule.Color);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SystabParserService — extended coverage
    // ═══════════════════════════════════════════════════════════════
    public class SystabParserExtendedTests
    {
        [Fact]
        public void ParseRegContent_DwordCaseInsensitive()
        {
            string content = "[Section]\r\n\"P\"=DWORD:0000000a";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Equal("10", result[("Section", "P")]);
        }

        [Fact]
        public void ParseRegContent_EmptyStringValue()
        {
            string content = "[Section]\r\n\"Empty\"=\"\"";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Equal("", result[("Section", "Empty")]);
        }

        [Fact]
        public void ParseRegContent_ValueWithEqualsSign()
        {
            string content = "[Section]\r\n\"Key\"=\"a=b\"";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Equal("a=b", result[("Section", "Key")]);
        }

        [Fact]
        public void ParseRegContent_LinuxLineEndings()
        {
            string content = "[Section]\n\"Key\"=\"Value\"";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Single(result);
            Assert.Equal("Value", result[("Section", "Key")]);
        }

        [Fact]
        public void ParseRegContent_InvalidDwordHex_LeavesAsIs()
        {
            string content = "[Section]\r\n\"P\"=dword:ZZZZ";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Equal("dword:ZZZZ", result[("Section", "P")]);
        }

        [Fact]
        public void ExtractTopicInfo_EmptyString_ReturnsNulls()
        {
            var (topic, station, index) = SystabParserService.ExtractTopicInfo("");
            Assert.Null(topic);
            Assert.Null(station);
            Assert.Null(index);
        }

        [Fact]
        public void ExtractTopicInfo_CaseInsensitive()
        {
            string path = @"HKEY_LOCAL_MACHINE\SOFTWARE\INDIGO\UNICORN\Production\Station1\TopicA\0";
            var (topic, station, index) = SystabParserService.ExtractTopicInfo(path);
            Assert.Equal("TopicA", topic);
            Assert.Equal("Station1", station);
            Assert.Equal("0", index);
        }

        [Fact]
        public void BuildSystabTree_MultipleSameTopicDifferentIndices_CreatesParentWithChildren()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Topic\0]" + "\r\n" +
                "\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Topic\1]" + "\r\n" +
                "\"B\"=\"2\"\r\n";

            var input = new Dictionary<string, string> { { "saved", saved }, { "default", saved } };
            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Single(tree);
            var parent = tree[0];
            Assert.True(parent.IsTopLevel);
            Assert.Equal(2, parent.Children.Count);
        }

        [Fact]
        public void BuildSystabTree_NumericIndex_DisplaysTopicPlusIndex()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Motor\0]" + "\r\n" +
                "\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Motor\1]" + "\r\n" +
                "\"B\"=\"2\"\r\n";

            var input = new Dictionary<string, string> { { "saved", saved }, { "default", saved } };
            var tree = SystabParserService.BuildSystabTree(input);

            var childNames = tree[0].Children.Select(c => c.Name).ToList();
            Assert.Contains("Motor 0", childNames);
            Assert.Contains("Motor 1", childNames);
        }

        [Fact]
        public void BuildSystabTree_NoDifferences_HasDifferencesFalse()
        {
            string content =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Motor\0]" + "\r\n" +
                "\"Speed\"=\"100\"\r\n";

            var input = new Dictionary<string, string> { { "saved", content }, { "default", content } };
            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Single(tree);
            Assert.False(tree[0].HasDifferences);
        }

        [Fact]
        public void BuildSystabTree_OnlySavedFile_DefaultIsEmpty()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Motor\0]" + "\r\n" +
                "\"Speed\"=\"100\"\r\n";

            var input = new Dictionary<string, string> { { "saved", saved } };
            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Single(tree);
            Assert.Equal("100", tree[0].Entries[0].Saved);
            Assert.Equal("", tree[0].Entries[0].Default);
            Assert.True(tree[0].Entries[0].IsDifferent);
        }

        [Fact]
        public void BuildSystabTree_ParentCountSumsChildren()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Topic\0]" + "\r\n" +
                "\"A\"=\"1\"\r\n\"B\"=\"2\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Sta\Topic\1]" + "\r\n" +
                "\"C\"=\"3\"\r\n";

            var input = new Dictionary<string, string> { { "saved", saved }, { "default", saved } };
            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Equal(3, tree[0].Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ObservableRangeCollection — extended coverage
    // ═══════════════════════════════════════════════════════════════
    public class ObservableRangeCollectionExtendedTests
    {
        [Fact]
        public void AddRange_EmptyCollection_NoChange()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(Array.Empty<int>());
            Assert.Empty(col);
        }

        [Fact]
        public void AddRange_MultipleBatches_AccumulatesItems()
        {
            var col = new ObservableRangeCollection<string>();
            col.AddRange(new[] { "a", "b" });
            col.AddRange(new[] { "c" });
            Assert.Equal(3, col.Count);
            Assert.Equal("c", col[2]);
        }

        [Fact]
        public void ReplaceAll_EmptyToEmpty_Works()
        {
            var col = new ObservableRangeCollection<int>();
            col.ReplaceAll(Array.Empty<int>());
            Assert.Empty(col);
        }

        [Fact]
        public void RemoveRange_ZeroCount_DoesNothing()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(new[] { 1, 2, 3 });
            col.RemoveRange(0, 0);
            Assert.Equal(3, col.Count);
        }

        [Fact]
        public void RemoveRange_AllItems_ClearsCollection()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(new[] { 1, 2, 3 });
            col.RemoveRange(0, 3);
            Assert.Empty(col);
        }

        [Fact]
        public void InsertRange_AtEnd_AppendsItems()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(new[] { 1, 2 });
            col.InsertRange(2, new[] { 3, 4 });
            Assert.Equal(4, col.Count);
            Assert.Equal(3, col[2]);
            Assert.Equal(4, col[3]);
        }

        [Fact]
        public void InsertRange_FiresResetNotification()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(new[] { 1, 2 });
            NotifyCollectionChangedAction? action = null;
            col.CollectionChanged += (s, e) => action = e.Action;
            col.InsertRange(0, new[] { 0 });
            Assert.Equal(NotifyCollectionChangedAction.Reset, action);
        }

        [Fact]
        public void ReplaceAll_RaisesPropertyChanged()
        {
            var col = new ObservableRangeCollection<int>();
            col.AddRange(new[] { 1 });
            var changed = new List<string>();
            ((INotifyPropertyChanged)col).PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            col.ReplaceAll(new[] { 2, 3 });
            Assert.Contains("Count", changed);
            Assert.Contains("Item[]", changed);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ChartModels — coverage for model classes
    // ═══════════════════════════════════════════════════════════════
    public class ChartModelsExtendedTests
    {
        [Fact]
        public void ReferenceLine_DefaultValues()
        {
            var line = new ReferenceLine();
            Assert.Equal("Line", line.Name);
            Assert.Equal(0, line.Value);
            Assert.Equal(ReferenceLineType.Horizontal, line.Type);
            Assert.Equal(2.0f, line.Thickness);
            Assert.True(line.IsDashed);
            Assert.Equal(AxisType.Left, line.YAxis);
        }

        [Fact]
        public void ReferenceLine_SetProperties_RaisesPropertyChanged()
        {
            var line = new ReferenceLine();
            var changed = new List<string>();
            line.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            line.Name = "Test";
            line.Value = 42;
            line.Type = ReferenceLineType.Vertical;
            line.Thickness = 5.0f;
            line.IsDashed = false;
            line.YAxis = AxisType.Right;

            Assert.Contains("Name", changed);
            Assert.Contains("Value", changed);
            Assert.Contains("Type", changed);
            Assert.Contains("Thickness", changed);
            Assert.Contains("IsDashed", changed);
            Assert.Contains("YAxis", changed);
        }

        [Fact]
        public void ReferenceLine_ColorString_ValidSkiaColor()
        {
            var line = new ReferenceLine();
            line.ColorString = "#FF00FF00";
            Assert.Contains("00ff00", line.ColorString);
        }

        [Fact]
        public void ReferenceLine_ColorString_InvalidColor_NoException()
        {
            var line = new ReferenceLine();
            var original = line.ColorString;
            line.ColorString = "not_a_color_at_all_xyz";
            // Should not throw; color may remain unchanged or fallback
        }

        [Fact]
        public void SignalSeries_DefaultValues()
        {
            var s = new SignalSeries();
            Assert.Equal("", s.Name);
            Assert.Empty(s.Data);
            Assert.True(s.IsVisible);
            Assert.Equal(AxisType.Left, s.YAxisType);
            Assert.Equal("L", s.AxisDisplay);
            Assert.Equal("-", s.CurrentValueDisplay);
            Assert.False(s.IsSmoothed);
            Assert.Null(s.SmoothedData);
        }

        [Fact]
        public void SignalSeries_SetYAxisType_ChangesDisplay()
        {
            var s = new SignalSeries();
            s.YAxisType = AxisType.Right;
            Assert.Equal("R", s.AxisDisplay);
        }

        [Fact]
        public void SignalSeries_CalculateSmoothing_CreatesSmoothedData()
        {
            var s = new SignalSeries { Data = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 } };
            s.CalculateSmoothing(3);
            Assert.NotNull(s.SmoothedData);
            Assert.Equal(s.Data.Length, s.SmoothedData!.Length);
        }

        [Fact]
        public void SignalSeries_CalculateSmoothing_EmptyData_NoException()
        {
            var s = new SignalSeries { Data = Array.Empty<double>() };
            s.CalculateSmoothing();
            Assert.Null(s.SmoothedData);
        }

        [Fact]
        public void SignalSeries_CalculateSmoothing_WithNaN_HandlesCorrectly()
        {
            var s = new SignalSeries { Data = new double[] { 1.0, double.NaN, 3.0, double.NaN, 5.0 } };
            s.CalculateSmoothing(3);
            Assert.NotNull(s.SmoothedData);
            Assert.True(double.IsNaN(s.SmoothedData![1]));
        }

        [Fact]
        public void SignalSeries_IsSmoothed_TriggersSmoothingIfNull()
        {
            var s = new SignalSeries { Data = new double[] { 1, 2, 3, 4, 5 } };
            s.IsSmoothed = true;
            Assert.NotNull(s.SmoothedData);
        }

        [Fact]
        public void SignalSeries_CurrentValueDisplay_RaisesPropertyChanged()
        {
            var s = new SignalSeries();
            var changed = new List<string>();
            s.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            s.CurrentValueDisplay = "42.5";
            Assert.Contains("CurrentValueDisplay", changed);
        }

        [Fact]
        public void SignalSeries_CurrentValueDisplay_SameValue_NoNotification()
        {
            var s = new SignalSeries();
            s.CurrentValueDisplay = "test";
            var changed = new List<string>();
            s.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            s.CurrentValueDisplay = "test";
            Assert.DoesNotContain("CurrentValueDisplay", changed);
        }

        [Fact]
        public void ChartViewModel_DefaultValues()
        {
            var vm = new ChartViewModel();
            Assert.Equal("", vm.Title);
            Assert.Equal(ChartViewType.Signal, vm.ViewType);
            Assert.True(vm.IsSignalView);
            Assert.False(vm.IsGanttView);
            Assert.False(vm.IsThreadView);
            Assert.False(vm.IsSelected);
            Assert.False(vm.IsDetached);
            Assert.True(vm.IsNotDetached);
            Assert.Equal(180, vm.ChartHeight);
            Assert.Empty(vm.Series);
            Assert.Empty(vm.ReferenceLines);
            Assert.Null(vm.States);
            Assert.Null(vm.EventMarkers);
            Assert.Null(vm.GanttStates);
            Assert.Null(vm.ThreadMessages);
            Assert.Null(vm.ThreadName);
            Assert.Null(vm.GanttDataLength);
            Assert.Null(vm.GanttGetXAxisLabel);
        }

        [Fact]
        public void ChartViewModel_SetViewType_ToGantt_UpdatesFlags()
        {
            var vm = new ChartViewModel();
            vm.ViewType = ChartViewType.Gantt;
            Assert.False(vm.IsSignalView);
            Assert.True(vm.IsGanttView);
            Assert.False(vm.IsThreadView);
        }

        [Fact]
        public void ChartViewModel_SetViewType_ToThread_UpdatesFlags()
        {
            var vm = new ChartViewModel();
            vm.ViewType = ChartViewType.Thread;
            Assert.False(vm.IsSignalView);
            Assert.False(vm.IsGanttView);
            Assert.True(vm.IsThreadView);
        }

        [Fact]
        public void ChartViewModel_IsDetached_TogglesIsNotDetached()
        {
            var vm = new ChartViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            vm.IsDetached = true;
            Assert.True(vm.IsDetached);
            Assert.False(vm.IsNotDetached);
            Assert.Contains("IsDetached", changed);
            Assert.Contains("IsNotDetached", changed);
        }

        [Fact]
        public void StateEventItem_DefaultValues()
        {
            var item = new StateEventItem();
            Assert.Equal("", item.Time);
            Assert.Equal("", item.StateName);
            Assert.Equal(0, item.LineIndex);
        }

        [Fact]
        public void WorkspaceModel_DefaultValues()
        {
            var ws = new WorkspaceModel();
            Assert.Equal("", ws.SourceCsvPath);
            Assert.NotNull(ws.Charts);
            Assert.Empty(ws.Charts);
        }

        [Fact]
        public void ChartSaveData_DefaultValues()
        {
            var csd = new ChartSaveData();
            Assert.Equal("", csd.Title);
            Assert.NotNull(csd.Series);
            Assert.NotNull(csd.ReferenceLines);
        }

        [Fact]
        public void SeriesSaveData_DefaultValues()
        {
            var ssd = new SeriesSaveData();
            Assert.Equal("", ssd.Name);
            Assert.Equal("", ssd.ColorHex);
            Assert.False(ssd.IsVisible);
            Assert.Equal(AxisType.Left, ssd.Axis);
        }

        [Fact]
        public void EventMarker_DefaultValues()
        {
            var em = new EventMarker();
            Assert.Equal(0, em.Index);
            Assert.Equal("", em.Name);
            Assert.Equal("", em.Message);
            Assert.Equal("", em.Time);
            Assert.Equal("", em.Severity);
            Assert.Equal("", em.Description);
        }

        [Fact]
        public void GapRegion_DefaultValues()
        {
            var gap = new GapRegion();
            Assert.Equal(0, gap.StartIndex);
            Assert.Equal(0, gap.EndIndex);
            Assert.Equal("", gap.Duration);
            Assert.Equal("", gap.StartTime);
            Assert.Equal("", gap.EndTime);
        }

        [Fact]
        public void SignalCategory_DefaultCategories_HasExpectedCount()
        {
            Assert.Equal(7, SignalCategory.DefaultCategories.Length);
        }

        [Fact]
        public void SignalCategory_FirstIsAllSignals()
        {
            var first = SignalCategory.DefaultCategories[0];
            Assert.Equal("All Signals", first.Name);
            Assert.Empty(first.Keywords);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ChartDataTransferService.Models — SignalData coverage
    // ═══════════════════════════════════════════════════════════════
    public class SignalDataTests
    {
        [Fact]
        public void SignalData_DefaultValues()
        {
            var sd = new SignalData();
            Assert.Equal("", sd.Name);
            Assert.Equal("", sd.Category);
            Assert.Equal(SignalType.Analog, sd.SignalType);
            Assert.Equal(0, sd.DataLength);
        }

        [Fact]
        public void SignalData_MaterializeData_FromSparsePoints()
        {
            var sd = new SignalData();
            sd.DataLength = 5;
            sd.SparsePoints = new List<KeyValuePair<int, double>>
            {
                new(0, 10.0),
                new(2, 20.0),
                new(4, 30.0)
            };

            var data = sd.Data;
            Assert.NotNull(data);
            Assert.Equal(5, data!.Length);
            Assert.Equal(10.0, data[0]);
            Assert.Equal(10.0, data[1]); // forward-fill
            Assert.Equal(20.0, data[2]);
            Assert.Equal(20.0, data[3]); // forward-fill
            Assert.Equal(30.0, data[4]);
        }

        [Fact]
        public void SignalData_MaterializeData_NullSparse_ReturnsNull()
        {
            var sd = new SignalData();
            sd.DataLength = 3;
            Assert.Null(sd.Data);
        }

        [Fact]
        public void SignalData_DataLength_FallsBackToDataArrayLength()
        {
            var sd = new SignalData();
            sd.Data = new double[] { 1, 2, 3 };
            Assert.Equal(3, sd.DataLength);
        }

        [Fact]
        public void SignalData_MaterializeData_EmptySparse_AllNaN()
        {
            var sd = new SignalData();
            sd.DataLength = 3;
            sd.SparsePoints = new List<KeyValuePair<int, double>>();

            var data = sd.Data;
            Assert.NotNull(data);
            Assert.True(double.IsNaN(data![0]));
            Assert.True(double.IsNaN(data[1]));
            Assert.True(double.IsNaN(data[2]));
        }

        [Fact]
        public void ChartDataPackage_DefaultValues()
        {
            var pkg = new ChartDataPackage();
            Assert.Equal("", pkg.SessionName);
            Assert.NotNull(pkg.TimeStamps);
            Assert.NotNull(pkg.Signals);
            Assert.NotNull(pkg.States);
            Assert.NotNull(pkg.ThreadMessages);
            Assert.NotNull(pkg.Events);
            Assert.False(pkg.SuppressGapDetection);
            Assert.Null(pkg.EmStatisticsCsvContent);
        }

        [Fact]
        public void EventMarkerData_DefaultValues()
        {
            var emd = new EventMarkerData();
            Assert.Equal("", emd.Name);
            Assert.Equal("", emd.State);
            Assert.Equal("", emd.Severity);
            Assert.Equal("", emd.Description);
            Assert.Equal("", emd.Parameters);
            Assert.Equal(0, emd.TimeIndex);
        }

        [Fact]
        public void StateData_DefaultValues()
        {
            var sd = new StateData();
            Assert.Equal("", sd.Name);
            Assert.Equal("", sd.Category);
            Assert.NotNull(sd.Intervals);
            Assert.Empty(sd.Intervals);
        }

        [Fact]
        public void ThreadMessageData_DefaultValues()
        {
            var tmd = new ThreadMessageData();
            Assert.Equal(0, tmd.TimeIndex);
            Assert.Equal("", tmd.ThreadName);
            Assert.Equal("", tmd.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SearchConfigService — file-based operations with temp dir
    // ═══════════════════════════════════════════════════════════════
    public class SearchConfigServiceTests : IDisposable
    {
        private readonly SearchConfigService _service;

        public SearchConfigServiceTests()
        {
            _service = new SearchConfigService();
        }

        public void Dispose()
        {
        }

        [Fact]
        public void SaveProfile_NullOrWhitespaceName_Throws()
        {
            var profile = new SearchProfile { Name = "" };
            Assert.Throws<ArgumentException>(() => _service.SaveProfile(profile));
        }

        [Fact]
        public void SaveProfile_WhitespaceName_Throws()
        {
            var profile = new SearchProfile { Name = "   " };
            Assert.Throws<ArgumentException>(() => _service.SaveProfile(profile));
        }

        [Fact]
        public void SaveAndLoadProfile_Roundtrips()
        {
            var name = $"TestProfile_{Guid.NewGuid():N}";
            try
            {
                var profile = new SearchProfile
                {
                    Name = name,
                    Criteria = new SearchCriteria { Name = "TestCriteria", SearchPLC = true, SearchAPP = false }
                };
                _service.SaveProfile(profile);

                var loaded = _service.LoadProfile(name);
                Assert.NotNull(loaded);
                Assert.Equal(name, loaded!.Name);
                Assert.Equal("TestCriteria", loaded.Criteria.Name);
                Assert.True(loaded.Criteria.SearchPLC);
                Assert.False(loaded.Criteria.SearchAPP);
            }
            finally
            {
                _service.DeleteProfile(name);
            }
        }

        [Fact]
        public void LoadProfile_NonExistent_ReturnsNull()
        {
            var result = _service.LoadProfile($"NonExistent_{Guid.NewGuid():N}");
            Assert.Null(result);
        }

        [Fact]
        public void DeleteProfile_NonExistent_ReturnsFalse()
        {
            var result = _service.DeleteProfile($"NonExistent_{Guid.NewGuid():N}");
            Assert.False(result);
        }

        [Fact]
        public void DeleteProfile_Existing_ReturnsTrue()
        {
            var name = $"ToDelete_{Guid.NewGuid():N}";
            _service.SaveProfile(new SearchProfile { Name = name });
            Assert.True(_service.DeleteProfile(name));
            Assert.Null(_service.LoadProfile(name));
        }

        [Fact]
        public void ListProfiles_ReturnsListOfStrings()
        {
            var profiles = _service.ListProfiles();
            Assert.NotNull(profiles);
            Assert.IsType<List<string>>(profiles);
        }

        [Fact]
        public void RenameProfile_ExistingProfile_Renames()
        {
            var oldName = $"OldName_{Guid.NewGuid():N}";
            var newName = $"NewName_{Guid.NewGuid():N}";
            try
            {
                _service.SaveProfile(new SearchProfile { Name = oldName });
                var result = _service.RenameProfile(oldName, newName);
                Assert.True(result);
                Assert.Null(_service.LoadProfile(oldName));
                Assert.NotNull(_service.LoadProfile(newName));
            }
            finally
            {
                _service.DeleteProfile(oldName);
                _service.DeleteProfile(newName);
            }
        }

        [Fact]
        public void RenameProfile_NonExistent_ReturnsFalse()
        {
            var result = _service.RenameProfile($"Ghost_{Guid.NewGuid():N}", "NewName");
            Assert.False(result);
        }

        [Fact]
        public void RenameProfile_TargetExists_ReturnsFalse()
        {
            var name1 = $"Rename1_{Guid.NewGuid():N}";
            var name2 = $"Rename2_{Guid.NewGuid():N}";
            try
            {
                _service.SaveProfile(new SearchProfile { Name = name1 });
                _service.SaveProfile(new SearchProfile { Name = name2 });
                var result = _service.RenameProfile(name1, name2);
                Assert.False(result);
            }
            finally
            {
                _service.DeleteProfile(name1);
                _service.DeleteProfile(name2);
            }
        }

        [Fact]
        public void ExportProfile_WritesToPath()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.json");
            try
            {
                var profile = new SearchProfile { Name = "ExportTest" };
                _service.ExportProfile(profile, tempFile);
                Assert.True(File.Exists(tempFile));
                var content = File.ReadAllText(tempFile);
                Assert.Contains("ExportTest", content);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void LoadProfileFromFile_NonExistent_ReturnsNull()
        {
            var result = _service.LoadProfileFromFile(@"C:\nonexistent_path_12345.json");
            Assert.Null(result);
        }

        [Fact]
        public void LoadProfileFromFile_ValidFile_ReturnsProfile()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"load_{Guid.NewGuid():N}.json");
            try
            {
                var profile = new SearchProfile { Name = "LoadTest" };
                _service.ExportProfile(profile, tempFile);
                var loaded = _service.LoadProfileFromFile(tempFile);
                Assert.NotNull(loaded);
                Assert.Equal("LoadTest", loaded!.Name);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SearchLocation model — property changed notifications
    // ═══════════════════════════════════════════════════════════════
    public class SearchLocationModelTests
    {
        [Fact]
        public void SearchLocation_DefaultValues()
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
        public void SearchLocation_PropertyChanged_RaisedForAllProperties()
        {
            var loc = new SearchLocation();
            var changed = new List<string>();
            loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            loc.Name = "Test";
            loc.Address = "1.2.3.4";
            loc.BasePath = @"C:\Logs";
            loc.IsActive = false;
            loc.ConnectionStatus = ConnectionStatus.Connected;
            loc.LastAccessed = DateTime.Now;

            Assert.Contains("Name", changed);
            Assert.Contains("Address", changed);
            Assert.Contains("BasePath", changed);
            Assert.Contains("IsActive", changed);
            Assert.Contains("ConnectionStatus", changed);
            Assert.Contains("LastAccessed", changed);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FilterNode — extended coverage
    // ═══════════════════════════════════════════════════════════════
    public class FilterNodeExtendedTests
    {
        [Fact]
        public void FilterNode_DefaultValues()
        {
            var node = new FilterNode();
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("", node.Value);
            Assert.True(node.IsEnabled);
            Assert.Empty(node.Children);
        }

        [Fact]
        public void FilterNode_DeepClone_ClonesAllFields()
        {
            var node = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Field = "Level",
                Operator = "Equals",
                Value = "error"
            };
            node.Children.Add(new FilterNode { Field = "Message", Value = "test" });

            var clone = node.DeepClone();
            Assert.Equal(node.Type, clone.Type);
            Assert.Equal(node.LogicalOperator, clone.LogicalOperator);
            Assert.Equal(node.Field, clone.Field);
            Assert.Equal(node.Operator, clone.Operator);
            Assert.Equal(node.Value, clone.Value);
            Assert.Single(clone.Children);
            Assert.NotSame(node.Children[0], clone.Children[0]);
            Assert.Equal("test", clone.Children[0].Value);
        }

        [Fact]
        public void FilterNode_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RegexOperator_ReturnsRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            Assert.NotNull(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "[invalid" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_SetValue_InvalidatesCompiledRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var regex1 = node.CompiledRegex;
            Assert.NotNull(regex1);

            node.Value = @"\w+";
            var regex2 = node.CompiledRegex;
            Assert.NotNull(regex2);
            Assert.NotSame(regex1, regex2);
        }

        [Fact]
        public void FilterNode_PropertyChanged_RaisedOnSet()
        {
            var node = new FilterNode();
            var changed = new List<string>();
            node.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            node.Type = NodeType.Group;
            node.LogicalOperator = "OR";
            node.Field = "Level";
            node.Operator = "Equals";
            node.Value = "error";

            Assert.Contains("Type", changed);
            Assert.Contains("LogicalOperator", changed);
            Assert.Contains("Field", changed);
            Assert.Contains("Operator", changed);
            Assert.Contains("Value", changed);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SystabEntry / SystabTopicNode model tests
    // ═══════════════════════════════════════════════════════════════
    public class SystabModelTests
    {
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

        [Fact]
        public void SystabTopicNode_DefaultValues()
        {
            var node = new SystabTopicNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.FullPath);
            Assert.False(node.IsTopLevel);
            Assert.False(node.IsExpanded);
            Assert.False(node.IsSelected);
            Assert.False(node.HasDifferences);
            Assert.Empty(node.Entries);
            Assert.Equal(0, node.Count);
            Assert.Empty(node.Children);
        }

        [Fact]
        public void SystabTopicNode_PropertyChanged_Raised()
        {
            var node = new SystabTopicNode();
            var changed = new List<string>();
            node.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            node.Name = "Test";
            node.FullPath = "/path";
            node.IsExpanded = true;
            node.IsSelected = true;
            node.HasDifferences = true;

            Assert.Contains("Name", changed);
            Assert.Contains("FullPath", changed);
            Assert.Contains("IsExpanded", changed);
            Assert.Contains("IsSelected", changed);
            Assert.Contains("HasDifferences", changed);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ConditionModels — ColoringCondition / FilterCondition
    // ═══════════════════════════════════════════════════════════════
    public class ConditionModelExtendedTests
    {
        [Fact]
        public void ColoringCondition_Clone_CopiesAllFields()
        {
            var original = new ColoringCondition
            {
                Field = "Logger",
                Operator = "Contains",
                Value = "TestValue",
                Color = System.Windows.Media.Color.FromRgb(255, 0, 0)
            };

            var clone = original.Clone();
            Assert.Equal(original.Field, clone.Field);
            Assert.Equal(original.Operator, clone.Operator);
            Assert.Equal(original.Value, clone.Value);
            Assert.Equal(original.Color, clone.Color);
        }

        [Fact]
        public void ColoringCondition_Clone_IsNotSameInstance()
        {
            var original = new ColoringCondition { Field = "test" };
            var clone = original.Clone();
            Assert.NotSame(original, clone);
        }

        [Fact]
        public void ColoringCondition_IsEnabled_DefaultTrue()
        {
            var cc = new ColoringCondition();
            Assert.True(cc.IsEnabled);
        }

        [Fact]
        public void FilterCondition_DefaultValues()
        {
            var fc = new FilterCondition();
            Assert.Equal("", fc.Field);
            Assert.Equal("", fc.Operator);
            Assert.Equal("", fc.Value);
            Assert.True(fc.IsActive);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SearchCriteria / TimeRangeFilter model tests
    // ═══════════════════════════════════════════════════════════════
    public class SearchCriteriaExtendedTests
    {
        [Fact]
        public void SearchCriteria_DefaultValues()
        {
            var sc = new SearchCriteria();
            Assert.Equal("", sc.Name);
            Assert.Empty(sc.Groups);
            Assert.Equal(LogicalGroupOperator.And, sc.GroupOperator);
            Assert.Null(sc.FileTimeFilter);
            Assert.Null(sc.ResultTimeFilter);
            Assert.Empty(sc.LocationIds);
            Assert.True(sc.SearchPLC);
            Assert.True(sc.SearchAPP);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.None };
            var resolved = filter.Resolve();
            Assert.Same(filter, resolved);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours_SetsFrom()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True(resolved.From!.Value > DateTime.Now.AddHours(-25));
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek_SetsFrom()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.Null(resolved.To);
            Assert.True(resolved.From!.Value > DateTime.Now.AddDays(-8));
        }

        [Fact]
        public void SearchCondition_CompiledRegex_ContainsOperator_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_RegexOperator_ReturnsRegex()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            Assert.NotNull(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "[bad" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchConditionGroup_DefaultValues()
        {
            var group = new SearchConditionGroup();
            Assert.Empty(group.Conditions);
            Assert.Equal(ConditionOperator.And, group.Operator);
        }

        [Fact]
        public void SearchProfile_DefaultValues()
        {
            var profile = new SearchProfile();
            Assert.Equal("", profile.Name);
            Assert.NotNull(profile.Locations);
            Assert.NotNull(profile.Criteria);
            Assert.NotNull(profile.Schedules);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PluginLoader — extended coverage
    // ═══════════════════════════════════════════════════════════════
    public class PluginLoaderExtendedTests
    {
        [Fact]
        public void Reload_ClearsAndReloads_SameCount()
        {
            var loader = new PluginLoader();
            int countBefore = loader.Plugins.Count;
            loader.Reload();
            Assert.Equal(countBefore, loader.Plugins.Count);
        }

        [Fact]
        public void GetDllPath_UnknownPlugin_ReturnsNull()
        {
            var loader = new PluginLoader();
            // Built-in plugins have no DLL path
            if (loader.Plugins.Count > 0)
                Assert.Null(loader.GetDllPath(loader.Plugins[0]));
        }
    }
}
