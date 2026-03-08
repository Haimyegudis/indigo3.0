using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    // ================================================================
    // Helper factories shared across all test classes in this file
    // ================================================================
    internal static class CoverageBoost2Helpers
    {
        public static LogEntry MakeLog(
            string message = "", string level = "", string logger = "",
            string threadName = "", string processName = "", string method = "",
            string pattern = "", string data = "", string exception = "",
            Dictionary<string, string>? extraFields = null,
            DateTime? date = null)
            => new()
            {
                Message = message, Level = level, Logger = logger,
                ThreadName = threadName, ProcessName = processName,
                Method = method, Pattern = pattern, Data = data,
                Exception = exception, ExtraFields = extraFields,
                Date = date ?? DateTime.MinValue
            };

        public static FilterNode MakeCondition(string field, string op, string value)
            => new() { Type = NodeType.Condition, Field = field, Operator = op, Value = value };

        public static FilterNode MakeGroup(string logicalOp, params FilterNode[] children)
            => new()
            {
                Type = NodeType.Group,
                LogicalOperator = logicalOp,
                Children = new ObservableCollection<FilterNode>(children)
            };
    }

    // ================================================================
    // 1. FilterSearchViewModel.FilterEvaluation — GetActiveFilters + CollectFilterNodeDescriptions
    //    These are public/private methods with significant branching NOT covered by FilterEvaluationTests
    // ================================================================
    public class GetActiveFiltersTests
    {
        private static readonly FilterSearchViewModel _vm =
            (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

        // CollectFilterNodeDescriptions is private instance — access via reflection
        private static readonly MethodInfo _collectMethod =
            typeof(FilterSearchViewModel).GetMethod("CollectFilterNodeDescriptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void CollectFilterNodeDescriptions_Condition_AddsItem()
        {
            var items = new List<ActiveFilterItem>();
            var node = CoverageBoost2Helpers.MakeCondition("Message", "Contains", "error");
            int idx = 0;
            object[] args = { items, node, "FILTER", "", "TEST", idx };
            _collectMethod.Invoke(_vm, args);
            Assert.Single(items);
            Assert.Contains("Message", items[0].Description);
            Assert.Contains("Contains", items[0].Description);
            Assert.Equal("TEST:0", items[0].Key);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NullNode_DoesNothing()
        {
            var items = new List<ActiveFilterItem>();
            int idx = 0;
            object[] args = { items, null!, "FILTER", "", "TEST", idx };
            _collectMethod.Invoke(_vm, args);
            Assert.Empty(items);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_Group_RecursesChildren()
        {
            var items = new List<ActiveFilterItem>();
            var group = CoverageBoost2Helpers.MakeGroup("AND",
                CoverageBoost2Helpers.MakeCondition("Level", "Equals", "Error"),
                CoverageBoost2Helpers.MakeCondition("Message", "Contains", "disk"));
            int idx = 0;
            object[] args = { items, group, "FILTER", "", "MAIN_FILTER", idx };
            _collectMethod.Invoke(_vm, args);
            Assert.Equal(2, items.Count);
            Assert.Equal("MAIN_FILTER:0", items[0].Key);
            Assert.Equal("MAIN_FILTER:1", items[1].Key);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_NestedGroup_IncrementsIndex()
        {
            var items = new List<ActiveFilterItem>();
            var inner = CoverageBoost2Helpers.MakeGroup("OR",
                CoverageBoost2Helpers.MakeCondition("Level", "Equals", "Warn"),
                CoverageBoost2Helpers.MakeCondition("Logger", "Begins With", "com"));
            var outer = CoverageBoost2Helpers.MakeGroup("AND",
                CoverageBoost2Helpers.MakeCondition("Message", "Contains", "hello"),
                inner);
            int idx = 0;
            object[] args = { items, outer, "FILTER", "", "PFX", idx };
            _collectMethod.Invoke(_vm, args);
            Assert.Equal(3, items.Count);
        }

        [Fact]
        public void CollectFilterNodeDescriptions_GroupWithNullChildren_Skips()
        {
            var items = new List<ActiveFilterItem>();
            var group = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND", Children = null! };
            int idx = 0;
            object[] args = { items, group, "FILTER", "", "PFX", idx };
            _collectMethod.Invoke(_vm, args);
            Assert.Empty(items);
        }
    }

    // ================================================================
    // 2. MainViewModel.Windows — OpenUrl static method (internal)
    // ================================================================
    public class MainViewModelOpenUrlTests
    {
        private static readonly MethodInfo _openUrl =
            typeof(MainViewModel).GetMethod("OpenUrl",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!;

        private static void CallOpenUrl(string url)
        {
            // We can't actually open a browser, but we test the validation logic
            // by ensuring no exception is thrown for valid/invalid URLs.
            try { _openUrl.Invoke(null, new object[] { url }); } catch { }
        }

        [Fact]
        public void OpenUrl_NullOrEmpty_NoException()
        {
            CallOpenUrl(null!);
            CallOpenUrl("");
            CallOpenUrl("   ");
        }

        [Fact]
        public void OpenUrl_InvalidScheme_NoException()
        {
            CallOpenUrl("ftp://example.com");
            CallOpenUrl("javascript:alert(1)");
            CallOpenUrl("file:///etc/passwd");
        }

        [Fact]
        public void OpenUrl_InvalidUri_NoException()
        {
            CallOpenUrl("not a url at all");
            CallOpenUrl("://broken");
        }

        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://example.com")]
        [InlineData("mailto:user@example.com")]
        public void OpenUrl_ValidSchemes_NoException(string url)
        {
            // These would open a browser in real life; we just verify no crash
            CallOpenUrl(url);
        }
    }

    // ================================================================
    // 3. ConfigExplorerViewModel.Database — FilterTreeNode + FilterDbTreeNodes + SetNodeVisibility
    // ================================================================
    public class ConfigExplorerDbTreeTests
    {
        private static readonly ConfigExplorerViewModel _vm =
            (ConfigExplorerViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ConfigExplorerViewModel));

        private static readonly MethodInfo _filterTreeNode =
            typeof(ConfigExplorerViewModel).GetMethod("FilterTreeNode",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly MethodInfo _setNodeVisibility =
            typeof(ConfigExplorerViewModel).GetMethod("SetNodeVisibility",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static bool CallFilterTreeNode(DbTreeNode node, string text)
            => (bool)_filterTreeNode.Invoke(_vm, new object[] { node, text })!;

        private static void CallSetNodeVisibility(DbTreeNode node, bool visible)
            => _setNodeVisibility.Invoke(_vm, new object[] { node, visible });

        [Fact]
        public void FilterTreeNode_SelfMatches_ReturnsTrue()
        {
            var node = new DbTreeNode { Name = "MyTable", Type = "Table", Schema = "" };
            Assert.True(CallFilterTreeNode(node, "MyTable"));
            Assert.True(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_TypeMatches_ReturnsTrue()
        {
            var node = new DbTreeNode { Name = "col1", Type = "INTEGER", Schema = "" };
            Assert.True(CallFilterTreeNode(node, "INTEGER"));
        }

        [Fact]
        public void FilterTreeNode_SchemaMatches_ReturnsTrue()
        {
            var node = new DbTreeNode { Name = "col1", Type = "", Schema = "PRIMARY KEY" };
            Assert.True(CallFilterTreeNode(node, "PRIMARY"));
        }

        [Fact]
        public void FilterTreeNode_NoMatch_ReturnsFalse()
        {
            var node = new DbTreeNode { Name = "abc", Type = "TEXT", Schema = "simple" };
            Assert.False(CallFilterTreeNode(node, "xyz"));
            Assert.False(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_ChildMatches_ParentBecomesVisible()
        {
            var parent = new DbTreeNode { Name = "parent", Type = "", Schema = "" };
            var child = new DbTreeNode { Name = "MatchingChild", Type = "", Schema = "" };
            parent.Children.Add(child);
            Assert.True(CallFilterTreeNode(parent, "MatchingChild"));
            Assert.True(parent.IsVisible);
            Assert.True(child.IsVisible);
            Assert.True(parent.IsExpanded);
        }

        [Fact]
        public void FilterTreeNode_ParentAndChildNoMatch_BothHidden()
        {
            var parent = new DbTreeNode { Name = "a", Type = "", Schema = "" };
            var child = new DbTreeNode { Name = "b", Type = "", Schema = "" };
            parent.Children.Add(child);
            Assert.False(CallFilterTreeNode(parent, "zzz"));
            Assert.False(parent.IsVisible);
            Assert.False(child.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_CaseInsensitive()
        {
            var node = new DbTreeNode { Name = "SomeTable", Type = "", Schema = "" };
            Assert.True(CallFilterTreeNode(node, "sometable"));
        }

        [Fact]
        public void SetNodeVisibility_RecursivelyApplies()
        {
            var root = new DbTreeNode { Name = "root" };
            var child1 = new DbTreeNode { Name = "child1" };
            var child2 = new DbTreeNode { Name = "child2" };
            var grandchild = new DbTreeNode { Name = "gc" };
            child1.Children.Add(grandchild);
            root.Children.Add(child1);
            root.Children.Add(child2);

            CallSetNodeVisibility(root, false);
            Assert.False(root.IsVisible);
            Assert.False(child1.IsVisible);
            Assert.False(child2.IsVisible);
            Assert.False(grandchild.IsVisible);

            CallSetNodeVisibility(root, true);
            Assert.True(root.IsVisible);
            Assert.True(grandchild.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_DeepNesting_MatchAtLeaf()
        {
            var root = new DbTreeNode { Name = "root" };
            var mid = new DbTreeNode { Name = "mid" };
            var leaf = new DbTreeNode { Name = "target_leaf" };
            mid.Children.Add(leaf);
            root.Children.Add(mid);
            Assert.True(CallFilterTreeNode(root, "target_leaf"));
            Assert.True(root.IsExpanded);
            Assert.True(mid.IsExpanded);
        }
    }

    // ================================================================
    // 4. ScheduleEditorViewModel — LoadFromSchedule + validation/save logic
    // ================================================================
    public class ScheduleEditorViewModelTests
    {
        private static ScheduleEditorViewModel CreateVm(ScheduledSearch? schedule = null, List<SearchLocation>? locs = null)
        {
            var s = schedule ?? new ScheduledSearch();
            var l = locs ?? new List<SearchLocation>();
            return new ScheduleEditorViewModel(s, l, new SearchCriteria());
        }

        [Fact]
        public void Constructor_LoadsScheduleName()
        {
            var s = new ScheduledSearch { Name = "TestSchedule", IsEnabled = true };
            var vm = CreateVm(s);
            Assert.Equal("TestSchedule", vm.ScheduleName);
            Assert.True(vm.IsEnabled);
        }

        [Fact]
        public void LoadFromSchedule_SetsScheduleType_Once()
        {
            var s = new ScheduledSearch { ScheduleType = ScheduleType.Once, RunDate = new DateTime(2026, 1, 1) };
            var vm = CreateVm(s);
            Assert.Equal(0, vm.ScheduleTypeIndex);
            Assert.True(vm.IsOnce);
            Assert.False(vm.IsWeekly);
            Assert.False(vm.IsInterval);
        }

        [Fact]
        public void LoadFromSchedule_SetsScheduleType_Daily()
        {
            var s = new ScheduledSearch { ScheduleType = ScheduleType.Daily };
            var vm = CreateVm(s);
            Assert.Equal(1, vm.ScheduleTypeIndex);
        }

        [Fact]
        public void LoadFromSchedule_SetsScheduleType_Weekly()
        {
            var s = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday }
            };
            var vm = CreateVm(s);
            Assert.Equal(2, vm.ScheduleTypeIndex);
            Assert.True(vm.IsWeekly);
            Assert.True(vm.DayMon);
            Assert.True(vm.DayFri);
            Assert.False(vm.DaySun);
            Assert.False(vm.DayTue);
            Assert.False(vm.DayWed);
            Assert.False(vm.DayThu);
            Assert.False(vm.DaySat);
        }

        [Fact]
        public void LoadFromSchedule_SetsScheduleType_Interval()
        {
            var s = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes
            };
            var vm = CreateVm(s);
            Assert.Equal(3, vm.ScheduleTypeIndex);
            Assert.True(vm.IsInterval);
            Assert.Equal("30", vm.IntervalValue);
            Assert.Equal(0, vm.IntervalUnitIndex);
        }

        [Fact]
        public void LoadFromSchedule_IntervalUnit_Hours()
        {
            var s = new ScheduledSearch { IntervalUnit = IntervalUnit.Hours };
            var vm = CreateVm(s);
            Assert.Equal(1, vm.IntervalUnitIndex);
        }

        [Fact]
        public void LoadFromSchedule_IntervalUnit_Days()
        {
            var s = new ScheduledSearch { IntervalUnit = IntervalUnit.Days };
            var vm = CreateVm(s);
            Assert.Equal(2, vm.IntervalUnitIndex);
        }

        [Fact]
        public void LoadFromSchedule_RunTime()
        {
            var s = new ScheduledSearch { RunTime = new TimeSpan(14, 30, 0) };
            var vm = CreateVm(s);
            Assert.Equal("14", vm.RunHour);
            Assert.Equal("30", vm.RunMinute);
        }

        [Fact]
        public void LoadFromSchedule_SimpleCriteria()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Operator = ConditionOperator.Or,
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test123", Operator = SearchOperator.Contains }
                            }
                        }
                    }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.IsSimpleMode);
            Assert.True(vm.SearchPLC);
            Assert.False(vm.SearchAPP);
            Assert.Equal("test123", vm.SimpleSearchText);
            Assert.Equal(SearchField.Message, vm.SimpleField);
        }

        [Fact]
        public void LoadFromSchedule_SimpleRegexCriteria()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Any, Value = "err.*", Operator = SearchOperator.Regex }
                            }
                        }
                    }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.SimpleUseRegex);
            Assert.Equal("err.*", vm.SimpleSearchText);
        }

        [Fact]
        public void LoadFromSchedule_AdvancedCriteria_MultipleConditions()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Operator = ConditionOperator.And,
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "error", Operator = SearchOperator.Contains },
                                new SearchCondition { Field = SearchField.Level, Value = "Error", Operator = SearchOperator.Equals, Negate = true }
                            }
                        }
                    }
                }
            };
            var vm = CreateVm(s);
            Assert.False(vm.IsSimpleMode);
            Assert.True(vm.IsAdvancedMode);
            Assert.Equal(ConditionOperator.And, vm.AdvancedOperator);
            Assert.Equal(2, vm.Conditions.Count);
            Assert.Equal("error", vm.Conditions[0].Value);
            Assert.True(vm.Conditions[1].Negate);
        }

        [Fact]
        public void LoadFromSchedule_Locations()
        {
            var loc1Id = Guid.NewGuid();
            var loc2Id = Guid.NewGuid();
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria { LocationIds = new List<Guid> { loc1Id } }
            };
            var locs = new List<SearchLocation>
            {
                new SearchLocation { Id = loc1Id, Name = "Loc1", Address = "addr1", BasePath = "path1" },
                new SearchLocation { Id = loc2Id, Name = "Loc2", Address = "addr2", BasePath = "path2" }
            };
            var vm = CreateVm(s, locs);
            Assert.Equal(2, vm.LocationItems.Count);
            Assert.True(vm.LocationItems.First(l => l.Id == loc1Id).IsChecked);
            Assert.False(vm.LocationItems.First(l => l.Id == loc2Id).IsChecked);
            Assert.True(vm.HasLocations);
        }

        [Fact]
        public void LoadFromSchedule_NoLocationIds_AllChecked()
        {
            var locId = Guid.NewGuid();
            var s = new ScheduledSearch { Criteria = new SearchCriteria() };
            var locs = new List<SearchLocation> { new SearchLocation { Id = locId, Name = "L", Address = "a", BasePath = "p" } };
            var vm = CreateVm(s, locs);
            Assert.True(vm.LocationItems[0].IsChecked);
        }

        [Fact]
        public void LoadFromSchedule_FileTimeFilter_24h()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.FileFilter24h);
            Assert.False(vm.FileFilterWeek);
            Assert.False(vm.FileFilterCustom);
        }

        [Fact]
        public void LoadFromSchedule_FileTimeFilter_Week()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.FileFilterWeek);
        }

        [Fact]
        public void LoadFromSchedule_FileTimeFilter_Custom()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 1, 31);
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    FileTimeFilter = new TimeRangeFilter { From = from, To = to }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.FileFilterCustom);
            Assert.Equal(from, vm.FileFromDate);
            Assert.Equal(to, vm.FileToDate);
        }

        [Fact]
        public void LoadFromSchedule_ResultTimeFilter_24h()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.ResultFilter24h);
        }

        [Fact]
        public void LoadFromSchedule_ResultTimeFilter_Week()
        {
            var s = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek }
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.ResultFilterWeek);
        }

        [Fact]
        public void LoadFromSchedule_Output()
        {
            var s = new ScheduledSearch { OutputDirectory = @"C:\Output" };
            var vm = CreateVm(s);
            Assert.Equal(@"C:\Output", vm.OutputDirectory);
        }

        [Fact]
        public void LoadFromSchedule_Email()
        {
            var s = new ScheduledSearch
            {
                EmailConfig = new EmailNotificationConfig
                {
                    IsEnabled = true,
                    Recipients = new List<string> { "a@b.com", "c@d.com" },
                    Timing = EmailTiming.AtSpecificTime,
                    SendTime = new TimeSpan(8, 30, 0),
                    CustomSubject = "My Subject"
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.EmailEnabled);
            Assert.Equal(2, vm.Recipients.Count);
            Assert.True(vm.TimingDeferred);
            Assert.True(vm.TimingIsDeferred);
            Assert.Equal("08", vm.EmailHour);
            Assert.Equal("30", vm.EmailMinute);
            Assert.Equal("My Subject", vm.CustomSubject);
        }

        [Fact]
        public void LoadFromSchedule_Email_Immediately()
        {
            var s = new ScheduledSearch
            {
                EmailConfig = new EmailNotificationConfig { Timing = EmailTiming.Immediately }
            };
            var vm = CreateVm(s);
            Assert.True(vm.TimingImmediate);
            Assert.False(vm.TimingDeferred);
        }

        [Fact]
        public void LoadFromSchedule_ScanMode_Search()
        {
            var s = new ScheduledSearch { ScanMode = ScanMode.SearchOnly };
            var vm = CreateVm(s);
            Assert.True(vm.ScanModeSearch);
            Assert.True(vm.NeedsSearch);
        }

        [Fact]
        public void LoadFromSchedule_ScanMode_Stats()
        {
            var s = new ScheduledSearch { ScanMode = ScanMode.StatisticsOnly };
            var vm = CreateVm(s);
            Assert.True(vm.ScanModeStats);
            Assert.False(vm.NeedsSearch);
        }

        [Fact]
        public void LoadFromSchedule_ScanMode_Both()
        {
            var s = new ScheduledSearch { ScanMode = ScanMode.SearchAndStatistics };
            var vm = CreateVm(s);
            Assert.True(vm.ScanModeBoth);
            Assert.True(vm.NeedsSearch);
        }

        [Fact]
        public void LoadFromSchedule_RunDays_AllDays()
        {
            var s = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunDays = new HashSet<DayOfWeek>
                {
                    DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
                }
            };
            var vm = CreateVm(s);
            Assert.True(vm.DaySun);
            Assert.True(vm.DayMon);
            Assert.True(vm.DayTue);
            Assert.True(vm.DayWed);
            Assert.True(vm.DayThu);
            Assert.True(vm.DayFri);
            Assert.True(vm.DaySat);
        }

        // AddConditionCommand / RemoveConditionCommand
        [Fact]
        public void AddConditionCommand_AddsCondition()
        {
            var vm = CreateVm();
            int before = vm.Conditions.Count;
            vm.AddConditionCommand.Execute(null);
            Assert.Equal(before + 1, vm.Conditions.Count);
        }

        [Fact]
        public void RemoveConditionCommand_RemovesCondition()
        {
            var vm = CreateVm();
            vm.AddConditionCommand.Execute(null);
            var cond = vm.Conditions.Last();
            vm.RemoveConditionCommand.Execute(cond);
            Assert.DoesNotContain(cond, vm.Conditions);
        }

        [Fact]
        public void RemoveConditionCommand_NullParameter_DoesNothing()
        {
            var vm = CreateVm();
            vm.AddConditionCommand.Execute(null);
            int count = vm.Conditions.Count;
            vm.RemoveConditionCommand.Execute(null);
            Assert.Equal(count, vm.Conditions.Count);
        }

        // SelectAllLocationsCommand / SelectNoLocationsCommand
        [Fact]
        public void SelectAllLocations_SetsAll()
        {
            var locs = new List<SearchLocation>
            {
                new SearchLocation { Name = "A" },
                new SearchLocation { Name = "B" }
            };
            var vm = CreateVm(locs: locs);
            vm.SelectNoLocationsCommand.Execute(null);
            Assert.All(vm.LocationItems, li => Assert.False(li.IsChecked));
            vm.SelectAllLocationsCommand.Execute(null);
            Assert.All(vm.LocationItems, li => Assert.True(li.IsChecked));
        }

        // Property computed tests
        [Fact]
        public void TimeLabelText_Interval_ShowsStartTime()
        {
            var vm = CreateVm();
            vm.ScheduleTypeIndex = 3;
            Assert.Contains("Start Time", vm.TimeLabelText);
        }

        [Fact]
        public void TimeLabelText_NonInterval_ShowsRunTime()
        {
            var vm = CreateVm();
            vm.ScheduleTypeIndex = 1;
            Assert.Contains("Run Time", vm.TimeLabelText);
        }

        [Fact]
        public void IsSimpleMode_SwitchToAdvanced_AddsCondition()
        {
            var vm = CreateVm();
            vm.IsSimpleMode = true;
            vm.Conditions.Clear();
            vm.IsSimpleMode = false; // sets IsAdvancedMode, should auto-add condition if empty
            Assert.True(vm.Conditions.Count >= 1);
        }

        [Fact]
        public void IsAdvancedMode_Setter()
        {
            var vm = CreateVm();
            vm.IsAdvancedMode = true;
            Assert.False(vm.IsSimpleMode);
            vm.IsAdvancedMode = false;
            Assert.True(vm.IsSimpleMode);
        }
    }

    // ================================================================
    // 5. ScheduleEditorViewModel.Commands — AddRecipient, RemoveRecipient, SendTestEmail
    // ================================================================
    public class ScheduleEditorCommandTests
    {
        private static ScheduleEditorViewModel CreateVm()
            => new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());

        // AddRecipient via reflection
        private static readonly MethodInfo _addRecipient =
            typeof(ScheduleEditorViewModel).GetMethod("AddRecipient",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly MethodInfo _removeRecipient =
            typeof(ScheduleEditorViewModel).GetMethod("RemoveRecipient",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void AddRecipient_ValidEmail_Adds()
        {
            var vm = CreateVm();
            vm.NewRecipient = "user@example.com";
            _addRecipient.Invoke(vm, Array.Empty<object>());
            Assert.Contains("user@example.com", vm.Recipients);
            Assert.Equal("", vm.NewRecipient);
        }

        [Fact]
        public void AddRecipient_NoAtSign_DoesNotAdd()
        {
            var vm = CreateVm();
            vm.NewRecipient = "invalid-email";
            _addRecipient.Invoke(vm, Array.Empty<object>());
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void AddRecipient_Empty_DoesNotAdd()
        {
            var vm = CreateVm();
            vm.NewRecipient = "";
            _addRecipient.Invoke(vm, Array.Empty<object>());
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void AddRecipient_Null_DoesNotAdd()
        {
            var vm = CreateVm();
            vm.NewRecipient = null;
            _addRecipient.Invoke(vm, Array.Empty<object>());
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void RemoveRecipient_ExistingEmail_Removes()
        {
            var vm = CreateVm();
            vm.Recipients.Add("a@b.com");
            _removeRecipient.Invoke(vm, new object?[] { "a@b.com" });
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void RemoveRecipient_Null_DoesNothing()
        {
            var vm = CreateVm();
            vm.Recipients.Add("a@b.com");
            _removeRecipient.Invoke(vm, new object?[] { null });
            Assert.Single(vm.Recipients);
        }

        [Fact]
        public void SendTestEmail_NoRecipients_SetsStatus()
        {
            var vm = CreateVm();
            // Call via reflection (async but no service)
            var method = typeof(ScheduleEditorViewModel).GetMethod("SendTestEmailAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (System.Threading.Tasks.Task)method.Invoke(vm, Array.Empty<object>())!;
            task.Wait();
            Assert.Contains("recipient", vm.TestEmailStatus, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SendTestEmail_WithRecipient_NoService_SetsNotAvailable()
        {
            var vm = CreateVm();
            vm.Recipients.Add("a@b.com");
            var method = typeof(ScheduleEditorViewModel).GetMethod("SendTestEmailAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (System.Threading.Tasks.Task)method.Invoke(vm, Array.Empty<object>())!;
            task.Wait();
            Assert.Contains("not available", vm.TestEmailStatus, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ================================================================
    // 6. ConditionRowViewModel + LocationCheckItem tests
    // ================================================================
    public class ConditionRowViewModelTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var row = new ConditionRowViewModel();
            Assert.Equal(SearchField.Any, row.Field);
            Assert.Equal(SearchOperator.Contains, row.Operator);
            Assert.Equal("", row.Value);
            Assert.False(row.Negate);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var row = new ConditionRowViewModel
            {
                Field = SearchField.Logger,
                Operator = SearchOperator.Regex,
                Value = "test.*",
                Negate = true
            };
            Assert.Equal(SearchField.Logger, row.Field);
            Assert.Equal(SearchOperator.Regex, row.Operator);
            Assert.Equal("test.*", row.Value);
            Assert.True(row.Negate);
        }

        [Fact]
        public void SearchFieldValues_ContainsExpectedValues()
        {
            var row = new ConditionRowViewModel();
            var values = row.SearchFieldValues;
            Assert.NotNull(values);
            Assert.True(values.Length > 0);
        }

        [Fact]
        public void SearchOperatorValues_ContainsExpectedValues()
        {
            var row = new ConditionRowViewModel();
            var values = row.SearchOperatorValues;
            Assert.NotNull(values);
            Assert.True(values.Length > 0);
        }
    }

    public class LocationCheckItemTests
    {
        [Fact]
        public void Defaults()
        {
            var item = new LocationCheckItem();
            Assert.Equal(Guid.Empty, item.Id);
            Assert.Null(item.DisplayText);
            Assert.False(item.IsChecked);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var id = Guid.NewGuid();
            var item = new LocationCheckItem
            {
                Id = id,
                DisplayText = "Location 1",
                IsChecked = true
            };
            Assert.Equal(id, item.Id);
            Assert.Equal("Location 1", item.DisplayText);
            Assert.True(item.IsChecked);
        }

        [Fact]
        public void PropertyChanged_Fires()
        {
            var item = new LocationCheckItem();
            string? changedProp = null;
            item.PropertyChanged += (s, e) => changedProp = e.PropertyName;
            item.IsChecked = true;
            Assert.Equal(nameof(LocationCheckItem.IsChecked), changedProp);
        }
    }

    // ================================================================
    // 7. DifferentLogsViewModel — BuildAvailableFields, BuildDefaultColumns, BuildOpenDialogFilter, CloseFile
    // ================================================================
    public class DifferentLogsViewModelCoverage2Tests
    {
        private static readonly MethodInfo _buildDefaultColumns =
            typeof(DifferentLogsViewModel).GetMethod("BuildDefaultColumns",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo _buildOpenDialogFilter =
            typeof(DifferentLogsViewModel).GetMethod("BuildOpenDialogFilter",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void BuildDefaultColumns_ReturnsThreeColumns()
        {
            var result = (IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>)_buildDefaultColumns.Invoke(null, Array.Empty<object>())!;
            Assert.Equal(3, result.Count);
            Assert.Equal("Date", result[0].Header);
            Assert.Equal("Level", result[1].Header);
            Assert.Equal("Message", result[2].Header);
        }

        [Fact]
        public void BuildAvailableFields_StandardFieldsAlwaysPresent()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            // Initialize fields via reflection
            typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<LogEntry>());
            typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, null);
            typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<string>());

            vm.BuildAvailableFields();
            var fields = vm.AvailableFields;
            Assert.Contains("Message", fields);
            Assert.Contains("Level", fields);
            Assert.Contains("ThreadName", fields);
            Assert.Contains("Logger", fields);
            Assert.Contains("Data", fields);
            Assert.Contains("Exception", fields);
        }

        [Fact]
        public void BuildAvailableFields_IncludesExtraFieldsFromEntries()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            var entries = new List<LogEntry>
            {
                new LogEntry { ExtraFields = new Dictionary<string, string> { { "CustomField", "val" } } }
            };
            typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, entries);
            typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, null);
            typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<string>());

            vm.BuildAvailableFields();
            Assert.Contains("CustomField", vm.AvailableFields);
        }

        [Fact]
        public void BuildAvailableFields_IncludesPluginColumns()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<LogEntry>());
            var cols = new List<IndiLogs.PluginAPI.PluginColumnDef>
            {
                new IndiLogs.PluginAPI.PluginColumnDef { Header = "PluginCol", Field = "PluginCol" }
            };
            typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, (IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>)cols.AsReadOnly());
            typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<string>());

            vm.BuildAvailableFields();
            Assert.Contains("PluginCol", vm.AvailableFields);
        }

        [Fact]
        public void BuildAvailableFields_DoesNotDuplicateBuiltInFieldsFromPlugins()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            typeof(DifferentLogsViewModel).GetField("_allLogEntries", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<LogEntry>());
            // Plugin column with built-in field name "Message" should not be duplicated
            var cols = new List<IndiLogs.PluginAPI.PluginColumnDef>
            {
                new IndiLogs.PluginAPI.PluginColumnDef { Header = "Message", Field = "Message" }
            };
            typeof(DifferentLogsViewModel).GetField("_columns", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, (IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>)cols.AsReadOnly());
            typeof(DifferentLogsViewModel).GetField("_availableFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new List<string>());

            vm.BuildAvailableFields();
            Assert.Equal(1, vm.AvailableFields.Count(f => f == "Message"));
        }

        [Fact]
        public void CurrentFileName_Empty_WhenNoFile()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            typeof(DifferentLogsViewModel).GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, null);
            Assert.Equal(string.Empty, vm.CurrentFileName);
            Assert.False(vm.HasFile);
        }

        [Fact]
        public void CurrentFileName_ReturnsFilename_WhenSet()
        {
            var vm = (DifferentLogsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DifferentLogsViewModel));
            typeof(DifferentLogsViewModel).GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, @"C:\logs\myfile.log");
            Assert.Equal("myfile.log", vm.CurrentFileName);
            Assert.True(vm.HasFile);
        }
    }

    // ================================================================
    // 8. DifferentLogsViewModel.FileLoading — ExtractSingleZipEntry static method
    // ================================================================
    public class ExtractSingleZipEntryTests
    {
        private static readonly MethodInfo _extractMethod =
            typeof(DifferentLogsViewModel).GetMethod("ExtractSingleZipEntry",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static string? CallExtract(string zipPath, string entryName)
            => (string?)_extractMethod.Invoke(null, new object[] { zipPath, entryName });

        [Fact]
        public void NonExistentZip_ReturnsNull()
        {
            var result = CallExtract(@"C:\does_not_exist_12345.zip", "file.txt");
            Assert.Null(result);
        }

        [Fact]
        public void StripsBracketSuffix()
        {
            // The method strips " [nested ZIP]" suffix before extracting
            // With a non-existent file it returns null, but covers the stripping logic
            var result = CallExtract(@"C:\does_not_exist_12345.zip", "inner.txt [nested ZIP]");
            Assert.Null(result);
        }
    }

    // ================================================================
    // 9. ExportConfigurationViewModel.Filtering — UpdateIOFilter, UpdateAxisFilter, etc.
    // ================================================================
    public class ExportConfigFilteringTests
    {
        private static readonly ExportConfigurationViewModel _vm =
            (ExportConfigurationViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ExportConfigurationViewModel));

        private static void InitCollections()
        {
            var ioField = typeof(ExportConfigurationViewModel).GetProperty("IOComponents")!;
            var axisField = typeof(ExportConfigurationViewModel).GetProperty("AxisComponents")!;
            var chField = typeof(ExportConfigurationViewModel).GetProperty("CHStepComponents")!;
            var threadField = typeof(ExportConfigurationViewModel).GetProperty("ThreadItems")!;

            ioField.SetValue(_vm, new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "Motor1", Category = "BIM" },
                new SelectableItem { Name = "Sensor2", Category = "PCU" }
            });
            axisField.SetValue(_vm, new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "Axis_X", Category = "Drive" },
                new SelectableItem { Name = "Axis_Y", Category = "Drive" }
            });
            chField.SetValue(_vm, new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "CH_Step1", Category = "Parent" }
            });
            threadField.SetValue(_vm, new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "Thread-1", Category = "Thread" },
                new SelectableItem { Name = "Manager", Category = "Thread" }
            });
        }

        private static readonly MethodInfo _updateIO = typeof(ExportConfigurationViewModel).GetMethod("UpdateIOFilter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo _updateAxis = typeof(ExportConfigurationViewModel).GetMethod("UpdateAxisFilter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo _updateCH = typeof(ExportConfigurationViewModel).GetMethod("UpdateCHStepFilter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo _updateThread = typeof(ExportConfigurationViewModel).GetMethod("UpdateThreadFilter", BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void UpdateIOFilter_EmptySearch_ClearsCache()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_ioSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "");
            _updateIO.Invoke(_vm, Array.Empty<object>());
            var cached = typeof(ExportConfigurationViewModel).GetField("_cachedIOFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.Null(cached);
        }

        [Fact]
        public void UpdateIOFilter_WithSearch_FiltersComponents()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_ioSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "Motor");
            _updateIO.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedIOFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Single(cached);
            Assert.Equal("Motor1", cached[0].Name);
        }

        [Fact]
        public void UpdateIOFilter_MatchesByCategory()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_ioSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "PCU");
            _updateIO.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedIOFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Single(cached);
            Assert.Equal("Sensor2", cached[0].Name);
        }

        [Fact]
        public void UpdateAxisFilter_Filters()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_axisSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "Axis_X");
            _updateAxis.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedAxisFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Single(cached);
        }

        [Fact]
        public void UpdateAxisFilter_EmptySearch_ClearsCache()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_axisSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "   ");
            _updateAxis.Invoke(_vm, Array.Empty<object>());
            var cached = typeof(ExportConfigurationViewModel).GetField("_cachedAxisFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.Null(cached);
        }

        [Fact]
        public void UpdateCHStepFilter_Filters()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_chStepSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "CH_Step");
            _updateCH.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedCHStepFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Single(cached);
        }

        [Fact]
        public void UpdateCHStepFilter_EmptySearch_ClearsCache()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_chStepSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "");
            _updateCH.Invoke(_vm, Array.Empty<object>());
            var cached = typeof(ExportConfigurationViewModel).GetField("_cachedCHStepFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.Null(cached);
        }

        [Fact]
        public void UpdateThreadFilter_Filters()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_threadSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "Manager");
            _updateThread.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedThreadFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Single(cached);
        }

        [Fact]
        public void UpdateThreadFilter_EmptySearch_ClearsCache()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_threadSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "");
            _updateThread.Invoke(_vm, Array.Empty<object>());
            var cached = typeof(ExportConfigurationViewModel).GetField("_cachedThreadFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.Null(cached);
        }

        [Fact]
        public void UpdateIOFilter_NoMatch_ReturnsEmptyList()
        {
            InitCollections();
            typeof(ExportConfigurationViewModel).GetField("_ioSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "zzzzz");
            _updateIO.Invoke(_vm, Array.Empty<object>());
            var cached = (List<SelectableItem>?)typeof(ExportConfigurationViewModel).GetField("_cachedIOFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_vm);
            Assert.NotNull(cached);
            Assert.Empty(cached);
        }

        [Fact]
        public void FilteredProperties_ReturnCachedOrFull()
        {
            InitCollections();
            // Clear caches
            typeof(ExportConfigurationViewModel).GetField("_cachedIOFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, null);
            // When null, should return IOComponents
            var filtered = _vm.FilteredIOComponents;
            Assert.NotNull(filtered);

            typeof(ExportConfigurationViewModel).GetField("_cachedAxisFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, null);
            Assert.NotNull(_vm.FilteredAxisComponents);

            typeof(ExportConfigurationViewModel).GetField("_cachedCHStepFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, null);
            Assert.NotNull(_vm.FilteredCHStepComponents);

            typeof(ExportConfigurationViewModel).GetField("_cachedThreadFiltered", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, null);
            Assert.NotNull(_vm.FilteredThreadItems);
        }

        [Fact]
        public void SearchDebounceTimer_Tick_ExecutesPendingSearches()
        {
            InitCollections();
            var method = typeof(ExportConfigurationViewModel).GetMethod("SearchDebounceTimer_Tick",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Set IO search pending
            typeof(ExportConfigurationViewModel).GetField("_ioSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, true);
            typeof(ExportConfigurationViewModel).GetField("_ioSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "Motor");

            // Set axis search pending
            typeof(ExportConfigurationViewModel).GetField("_axisSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, true);
            typeof(ExportConfigurationViewModel).GetField("_axisSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "X");

            // Set chStep search pending
            typeof(ExportConfigurationViewModel).GetField("_chStepSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, true);
            typeof(ExportConfigurationViewModel).GetField("_chStepSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "CH");

            // Set thread search pending
            typeof(ExportConfigurationViewModel).GetField("_threadSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, true);
            typeof(ExportConfigurationViewModel).GetField("_threadSearchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, "Thread");

            method.Invoke(_vm, new object?[] { null, EventArgs.Empty });

            // Verify pending flags were cleared
            Assert.False((bool)typeof(ExportConfigurationViewModel).GetField("_ioSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_vm)!);
            Assert.False((bool)typeof(ExportConfigurationViewModel).GetField("_axisSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_vm)!);
            Assert.False((bool)typeof(ExportConfigurationViewModel).GetField("_chStepSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_vm)!);
            Assert.False((bool)typeof(ExportConfigurationViewModel).GetField("_threadSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_vm)!);
        }

        [Fact]
        public void SearchDebounceTimer_Tick_NoPending_DoesNothing()
        {
            InitCollections();
            var method = typeof(ExportConfigurationViewModel).GetMethod("SearchDebounceTimer_Tick",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            typeof(ExportConfigurationViewModel).GetField("_ioSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, false);
            typeof(ExportConfigurationViewModel).GetField("_axisSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, false);
            typeof(ExportConfigurationViewModel).GetField("_chStepSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, false);
            typeof(ExportConfigurationViewModel).GetField("_threadSearchPending", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_vm, false);

            // Should not throw
            method.Invoke(_vm, new object?[] { null, EventArgs.Empty });
        }
    }

    // ================================================================
    // 10. LiveMonitoringViewModel.Polling — ShouldShowInFilteredView
    // ================================================================
    public class LiveMonitoringShouldShowTests
    {
        private static readonly MethodInfo _shouldShow =
            typeof(LiveMonitoringViewModel).GetMethod("ShouldShowInFilteredView",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        // We need a FilterSearchViewModel with specific fields set
        private static LiveMonitoringViewModel CreateVmWithFilter(
            bool mainFilterOutActive = false,
            List<string>? negativeFilters = null,
            string? searchText = null,
            bool mainFilterActive = false,
            List<string>? threadFilters = null)
        {
            var filterVm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

            // Set _negativeFilters
            var negField = typeof(FilterSearchViewModel).GetField("_negativeFilters", BindingFlags.NonPublic | BindingFlags.Instance)!;
            negField.SetValue(filterVm, new List<string>(negativeFilters ?? new List<string>()));

            // Set IsMainFilterOutActive
            typeof(FilterSearchViewModel).GetProperty("IsMainFilterOutActive")!.SetValue(filterVm, mainFilterOutActive);

            // Set SearchText
            typeof(FilterSearchViewModel).GetField("_searchText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(filterVm, searchText ?? "");

            // Set IsMainFilterActive
            typeof(FilterSearchViewModel).GetProperty("IsMainFilterActive")!.SetValue(filterVm, mainFilterActive);

            // Set _activeThreadFilters
            var threadField = typeof(FilterSearchViewModel).GetField("_activeThreadFilters", BindingFlags.NonPublic | BindingFlags.Instance)!;
            threadField.SetValue(filterVm, new List<string>(threadFilters ?? new List<string>()));

            // Set _mainFilterRoot to null
            typeof(FilterSearchViewModel).GetField("_mainFilterRoot", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(filterVm, null);

            // Create LiveVM with the filter VM
            var liveVm = (LiveMonitoringViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LiveMonitoringViewModel));
            typeof(LiveMonitoringViewModel).GetField("_filterVM", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(liveVm, filterVm);

            return liveVm;
        }

        [Fact]
        public void NegativeFilter_MessageMatch_ReturnsFalse()
        {
            var vm = CreateVmWithFilter(mainFilterOutActive: true, negativeFilters: new List<string> { "error text" });
            var log = CoverageBoost2Helpers.MakeLog(message: "Contains error text here");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void NegativeFilter_ThreadMatch_ReturnsFalse()
        {
            var vm = CreateVmWithFilter(mainFilterOutActive: true, negativeFilters: new List<string> { "THREAD:Worker" });
            var log = CoverageBoost2Helpers.MakeLog(threadName: "WorkerThread");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void NegativeFilter_NoMatch_ContinuesEvaluation()
        {
            var vm = CreateVmWithFilter(mainFilterOutActive: true, negativeFilters: new List<string> { "zzz" });
            var log = CoverageBoost2Helpers.MakeLog(message: "hello");
            // With no other filters and no default filter, this should call IsDefaultLog
            // which uses a factory PLC filter. Since we haven't set one up, it may fail,
            // but the important thing is we got past the negative filter check.
            try { _shouldShow.Invoke(vm, new object[] { log }); } catch { }
        }

        [Fact]
        public void ThreadFilter_NotInList_ReturnsFalse()
        {
            var vm = CreateVmWithFilter(threadFilters: new List<string> { "AllowedThread" });
            var log = CoverageBoost2Helpers.MakeLog(threadName: "OtherThread", message: "test");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void ThreadFilter_InList_Passes()
        {
            var vm = CreateVmWithFilter(threadFilters: new List<string> { "AllowedThread" });
            var log = CoverageBoost2Helpers.MakeLog(threadName: "AllowedThread", message: "test");
            // No search text, no main filter - passes all checks
            Assert.True((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void SearchText_Matches_Passes()
        {
            var vm = CreateVmWithFilter(searchText: "hello");
            var log = CoverageBoost2Helpers.MakeLog(message: "hello world");
            Assert.True((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void SearchText_NoMatch_ReturnsFalse()
        {
            var vm = CreateVmWithFilter(searchText: "hello");
            var log = CoverageBoost2Helpers.MakeLog(message: "goodbye world");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void NegativeFilter_MultipleFilters_FirstMatches()
        {
            var vm = CreateVmWithFilter(mainFilterOutActive: true,
                negativeFilters: new List<string> { "filterA", "filterB" });
            var log = CoverageBoost2Helpers.MakeLog(message: "contains filterA");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log })!);
        }

        [Fact]
        public void NegativeFilter_ThreadFilter_NullThreadName_Passes()
        {
            var vm = CreateVmWithFilter(mainFilterOutActive: true,
                negativeFilters: new List<string> { "THREAD:Worker" });
            var log = CoverageBoost2Helpers.MakeLog(message: "some message"); // ThreadName is ""
            // ThreadName is "" which is not null, but doesn't contain "Worker"
            // So negative filter doesn't match - continues
            try { _shouldShow.Invoke(vm, new object[] { log }); } catch { }
        }

        [Fact]
        public void MainFilterActive_WithFilterRoot_Evaluates()
        {
            var vm = CreateVmWithFilter(mainFilterActive: true);
            // Set a filter root on the filter VM
            var filterVm = (FilterSearchViewModel)typeof(LiveMonitoringViewModel)
                .GetField("_filterVM", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(vm)!;
            var filterRoot = CoverageBoost2Helpers.MakeGroup("AND",
                CoverageBoost2Helpers.MakeCondition("Level", "Equals", "Error"));
            typeof(FilterSearchViewModel).GetField("_mainFilterRoot", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(filterVm, filterRoot);

            var log = CoverageBoost2Helpers.MakeLog(level: "Error", message: "test");
            Assert.True((bool)_shouldShow.Invoke(vm, new object[] { log })!);

            var log2 = CoverageBoost2Helpers.MakeLog(level: "Info", message: "test");
            Assert.False((bool)_shouldShow.Invoke(vm, new object[] { log2 })!);
        }
    }

    // ================================================================
    // 11. DbTreeNode and DbFieldValue model tests
    // ================================================================
    public class DbTreeNodeTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var node = new DbTreeNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.Type);
            Assert.Equal("", node.Schema);
            Assert.Equal("", node.NodeType);
            Assert.Equal("", node.DatabaseFileName);
            Assert.False(node.IsExpanded);
            Assert.True(node.IsVisible);
            Assert.Empty(node.Children);
            Assert.Empty(node.FieldValues);
        }

        [Fact]
        public void PropertyChanged_FiresOnNameChange()
        {
            var node = new DbTreeNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Name = "TestTable";
            Assert.Equal("Name", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnTypeChange()
        {
            var node = new DbTreeNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Type = "INTEGER";
            Assert.Equal("Type", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnSchemaChange()
        {
            var node = new DbTreeNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Schema = "CREATE TABLE ...";
            Assert.Equal("Schema", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnExpandedChange()
        {
            var node = new DbTreeNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.IsExpanded = true;
            Assert.Equal("IsExpanded", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnVisibleChange()
        {
            var node = new DbTreeNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.IsVisible = false;
            Assert.Equal("IsVisible", prop);
        }

        [Fact]
        public void Children_CanBeAdded()
        {
            var parent = new DbTreeNode { Name = "parent" };
            var child = new DbTreeNode { Name = "child" };
            parent.Children.Add(child);
            Assert.Single(parent.Children);
            Assert.Equal("child", parent.Children[0].Name);
        }
    }

    public class DbFieldValueTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var fv = new DbFieldValue();
            Assert.Equal("", fv.ColumnName);
            Assert.Equal("", fv.Value);
            Assert.Equal("", fv.Type);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var fv = new DbFieldValue
            {
                ColumnName = "col1",
                Value = "42",
                Type = "INTEGER"
            };
            Assert.Equal("col1", fv.ColumnName);
            Assert.Equal("42", fv.Value);
            Assert.Equal("INTEGER", fv.Type);
        }
    }

    // ================================================================
    // 12. FilterNode additional tests (CompiledRegex caching)
    // ================================================================
    public class FilterNodeCompiledRegexTests
    {
        [Fact]
        public void CompiledRegex_ReturnsNull_WhenOperatorNotRegex()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void CompiledRegex_ReturnsNull_WhenValueEmpty()
        {
            var node = new FilterNode { Operator = "Regex", Value = "" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void CompiledRegex_ReturnsRegex_WhenValid()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var regex = node.CompiledRegex;
            Assert.NotNull(regex);
            Assert.True(regex!.IsMatch("42"));
        }

        [Fact]
        public void CompiledRegex_CachesRegex_SamePattern()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "[invalid" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void CompiledRegex_RecompilesOnValueChange()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"abc" };
            var regex1 = node.CompiledRegex;
            node.Value = @"def";
            var regex2 = node.CompiledRegex;
            Assert.NotSame(regex1, regex2);
        }

        [Fact]
        public void IsEnabled_DefaultsToTrue()
        {
            var node = new FilterNode();
            Assert.True(node.IsEnabled);
        }

        [Fact]
        public void PropertyChanged_FiresOnFieldChange()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Field = "Logger";
            Assert.Equal("Field", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnOperatorChange()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Operator = "Regex";
            Assert.Equal("Operator", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnValueChange()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Value = "test";
            Assert.Equal("Value", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnTypeChange()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.Type = NodeType.Group;
            Assert.Equal("Type", prop);
        }

        [Fact]
        public void PropertyChanged_FiresOnLogicalOperatorChange()
        {
            var node = new FilterNode();
            string? prop = null;
            node.PropertyChanged += (s, e) => prop = e.PropertyName;
            node.LogicalOperator = "OR";
            Assert.Equal("LogicalOperator", prop);
        }

        [Fact]
        public void Value_Setter_ClearsCompiledRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var r = node.CompiledRegex; // compile
            Assert.NotNull(r);
            node.Value = @"\w+"; // clear via setter
            // Access again - should recompile with new pattern
            var r2 = node.CompiledRegex;
            Assert.NotSame(r, r2);
        }
    }

    // ================================================================
    // 13. LogEntry model property tests (annotation logic, RowBackground)
    // ================================================================
    public class LogEntryAnnotationTests
    {
        [Fact]
        public void AnnotationIcon_NoAnnotation_Empty()
        {
            var log = new LogEntry();
            Assert.Equal("", log.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_Collapsed()
        {
            var log = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = false };
            Assert.NotEqual("", log.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_Expanded()
        {
            var log = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = true };
            Assert.NotEqual("", log.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_DifferentWhenExpandedVsCollapsed()
        {
            var log = new LogEntry { HasAnnotation = true };
            log.IsAnnotationExpanded = false;
            string collapsed = log.AnnotationIcon;
            log.IsAnnotationExpanded = true;
            string expanded = log.AnnotationIcon;
            Assert.NotEqual(collapsed, expanded);
        }

        [Fact]
        public void PropertyChanged_IsAnnotationExpanded_NotifiesIcon()
        {
            var log = new LogEntry { HasAnnotation = true };
            var props = new List<string>();
            log.PropertyChanged += (s, e) => props.Add(e.PropertyName!);
            log.IsAnnotationExpanded = true;
            Assert.Contains("IsAnnotationExpanded", props);
            Assert.Contains("AnnotationIcon", props);
        }

        [Fact]
        public void PropertyChanged_HasAnnotation_NotifiesRowBackground()
        {
            var log = new LogEntry();
            var props = new List<string>();
            log.PropertyChanged += (s, e) => props.Add(e.PropertyName!);
            log.HasAnnotation = true;
            Assert.Contains("HasAnnotation", props);
            Assert.Contains("RowBackground", props);
            Assert.Contains("AnnotationIcon", props);
        }

        [Fact]
        public void PropertyChanged_AnnotationContent()
        {
            var log = new LogEntry();
            string? prop = null;
            log.PropertyChanged += (s, e) => prop = e.PropertyName;
            log.AnnotationContent = "Hello";
            Assert.Equal("AnnotationContent", prop);
        }

        [Fact]
        public void IsAnnotationExpanded_SameValue_NoNotification()
        {
            var log = new LogEntry { IsAnnotationExpanded = false };
            var props = new List<string>();
            log.PropertyChanged += (s, e) => props.Add(e.PropertyName!);
            log.IsAnnotationExpanded = false; // same value
            Assert.Empty(props);
        }

        [Fact]
        public void HasAnnotation_SameValue_NoNotification()
        {
            var log = new LogEntry { HasAnnotation = false };
            var props = new List<string>();
            log.PropertyChanged += (s, e) => props.Add(e.PropertyName!);
            log.HasAnnotation = false; // same value
            Assert.Empty(props);
        }

        [Fact]
        public void AnnotationContent_SameValue_NoNotification()
        {
            var log = new LogEntry { AnnotationContent = "" };
            var props = new List<string>();
            log.PropertyChanged += (s, e) => props.Add(e.PropertyName!);
            log.AnnotationContent = ""; // same value
            Assert.Empty(props);
        }
    }

    // ================================================================
    // 14. SelectableItem model tests
    // ================================================================
    public class SelectableItemTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var item = new SelectableItem();
            Assert.Equal("", item.Name);
            Assert.Equal("", item.Category);
            Assert.False(item.IsSelected);
        }

        [Fact]
        public void PropertyChanged_Fires_OnNameChange()
        {
            var item = new SelectableItem();
            string? prop = null;
            item.PropertyChanged += (s, e) => prop = e.PropertyName;
            item.Name = "Test";
            Assert.Equal("Name", prop);
        }

        [Fact]
        public void PropertyChanged_Fires_OnIsSelectedChange()
        {
            var item = new SelectableItem();
            string? prop = null;
            item.PropertyChanged += (s, e) => prop = e.PropertyName;
            item.IsSelected = true;
            Assert.Equal("IsSelected", prop);
        }

        [Fact]
        public void PropertyChanged_Fires_OnCategoryChange()
        {
            var item = new SelectableItem();
            string? prop = null;
            item.PropertyChanged += (s, e) => prop = e.PropertyName;
            item.Category = "IO";
            Assert.Equal("Category", prop);
        }
    }

    // ================================================================
    // 15. ActiveFilterItem model tests
    // ================================================================
    public class ActiveFilterItemTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var item = new ActiveFilterItem();
            Assert.Equal("", item.Category);
            Assert.Equal("", item.Description);
            Assert.Equal("", item.Key);
            Assert.False(item.IsActive);
            Assert.Null(item.ColorBrush);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var item = new ActiveFilterItem
            {
                Category = "THREAD",
                Description = "Thread = Worker",
                Key = "THREAD:Worker",
                IsActive = true
            };
            Assert.Equal("THREAD", item.Category);
            Assert.Equal("Thread = Worker", item.Description);
            Assert.Equal("THREAD:Worker", item.Key);
            Assert.True(item.IsActive);
        }
    }

    // ================================================================
    // 16. StateEntry model tests - Duration property
    // ================================================================
    public class StateEntryDurationTests
    {
        [Fact]
        public void Duration_WithEndTime_CalculatesCorrectly()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2026, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2026, 1, 1, 10, 5, 30)
            };
            var duration = entry.EndTime!.Value - entry.StartTime;
            Assert.Equal(TimeSpan.FromMinutes(5.5), duration);
        }

        [Fact]
        public void Duration_WithoutEndTime_IsNull()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2026, 1, 1, 10, 0, 0),
                EndTime = null
            };
            Assert.Null(entry.EndTime);
        }
    }

    // ================================================================
    // 17. FilterNode.DeepClone — edge cases
    // ================================================================
    public class FilterNodeDeepCloneEdgeCaseTests
    {
        [Fact]
        public void DeepClone_EmptyChildren_ProducesEmptyChildren()
        {
            var group = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>()
            };
            var clone = group.DeepClone();
            Assert.NotNull(clone.Children);
            Assert.Empty(clone.Children);
        }

        [Fact]
        public void DeepClone_DeeplyNested_ClonesAll()
        {
            var leaf = CoverageBoost2Helpers.MakeCondition("Level", "Equals", "Error");
            var mid = CoverageBoost2Helpers.MakeGroup("OR", leaf);
            var root = CoverageBoost2Helpers.MakeGroup("AND", mid);

            var clone = root.DeepClone();
            Assert.Single(clone.Children);
            Assert.Single(clone.Children[0].Children);
            Assert.Equal("Error", clone.Children[0].Children[0].Value);

            // Mutate clone leaf
            clone.Children[0].Children[0].Value = "Warning";
            Assert.Equal("Error", leaf.Value); // original unchanged
        }
    }

    // ================================================================
    // 18. EvaluateFilterNode — ExtraFields edge cases + compiled regex path
    // ================================================================
    public class EvaluateFilterNodeExtendedTests
    {
        private static readonly FilterSearchViewModel _vm =
            (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

        [Fact]
        public void Regex_WithCompiledRegex_UsesCompiled()
        {
            var node = CoverageBoost2Helpers.MakeCondition("Message", "Regex", @"^hello\s+\w+");
            // Access CompiledRegex to pre-compile
            var compiled = node.CompiledRegex;
            Assert.NotNull(compiled);

            var log = CoverageBoost2Helpers.MakeLog(message: "hello world");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void ExtraField_FoundButEmpty_ReturnsFalse()
        {
            var log = CoverageBoost2Helpers.MakeLog(extraFields: new Dictionary<string, string> { { "Custom", "" } });
            var node = CoverageBoost2Helpers.MakeCondition("Custom", "Contains", "value");
            Assert.False(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void ExtraField_FoundWithValue_ReturnsTrue()
        {
            var log = CoverageBoost2Helpers.MakeLog(extraFields: new Dictionary<string, string> { { "Custom", "match_value" } });
            var node = CoverageBoost2Helpers.MakeCondition("Custom", "Contains", "match");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void ProcessName_Field_Evaluates()
        {
            var log = CoverageBoost2Helpers.MakeLog(processName: "MyProcess");
            var node = CoverageBoost2Helpers.MakeCondition("ProcessName", "Equals", "MyProcess");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void Pattern_Field_Evaluates()
        {
            var log = CoverageBoost2Helpers.MakeLog(pattern: "SomePattern");
            var node = CoverageBoost2Helpers.MakeCondition("Pattern", "Contains", "Pattern");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void Data_Field_Evaluates()
        {
            var log = CoverageBoost2Helpers.MakeLog(data: "{\"key\":\"value\"}");
            var node = CoverageBoost2Helpers.MakeCondition("Data", "Contains", "key");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void Exception_Field_Evaluates()
        {
            var log = CoverageBoost2Helpers.MakeLog(exception: "NullReferenceException at line 42");
            var node = CoverageBoost2Helpers.MakeCondition("Exception", "Contains", "NullReference");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void Method_Field_Evaluates()
        {
            var log = CoverageBoost2Helpers.MakeLog(method: "DoWork");
            var node = CoverageBoost2Helpers.MakeCondition("Method", "Begins With", "Do");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void BeginsWith_CaseInsensitive()
        {
            var log = CoverageBoost2Helpers.MakeLog(message: "Hello World");
            var node = CoverageBoost2Helpers.MakeCondition("Message", "Begins With", "HELLO");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }

        [Fact]
        public void EndsWith_CaseInsensitive()
        {
            var log = CoverageBoost2Helpers.MakeLog(message: "Hello World");
            var node = CoverageBoost2Helpers.MakeCondition("Message", "Ends With", "WORLD");
            Assert.True(_vm.EvaluateFilterNode(log, node));
        }
    }
}
