using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class CaseAndGrepViewModelTests
    {
        // ===== Helper factories =====

        private static LogEntry MakeLog(
            string message = "", string level = "", string logger = "",
            string threadName = "", string method = "", string pattern = "",
            string data = "", string exception = "",
            DateTime? date = null) =>
            new()
            {
                Message = message, Level = level, Logger = logger,
                ThreadName = threadName, Method = method, Pattern = pattern,
                Data = data, Exception = exception,
                Date = date ?? DateTime.Now
            };

        private static T CreateUninitialized<T>() =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetPrivateField<T>(T obj, string fieldName, object? value)
        {
            var type = typeof(T);
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            field?.SetValue(obj, value);
        }

        private static object? GetPrivateField<T>(T obj, string fieldName)
        {
            var type = typeof(T);
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            return field?.GetValue(obj);
        }

        private static TResult? InvokePrivate<T, TResult>(T obj, string methodName, params object?[] args)
        {
            var type = typeof(T);
            MethodInfo? method = null;
            while (type != null && method == null)
            {
                method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            return (TResult?)method?.Invoke(obj, args);
        }

        private static void InvokePrivateVoid<T>(T obj, string methodName, params object?[] args)
        {
            var type = typeof(T);
            MethodInfo? method = null;
            while (type != null && method == null)
            {
                method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                type = type.BaseType;
            }
            method?.Invoke(obj, args);
        }

        // =====================================================================
        // CaseManagementViewModel Tests
        // =====================================================================

        #region CaseManagement: GetAnnotation

        [Fact]
        public void GetAnnotation_NullLog_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_logAnnotations", new Dictionary<LogEntry, LogAnnotation>());
            Assert.Null(vm.GetAnnotation(null));
        }

        [Fact]
        public void GetAnnotation_NoAnnotation_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_logAnnotations", new Dictionary<LogEntry, LogAnnotation>());
            var log = MakeLog("test");
            Assert.Null(vm.GetAnnotation(log));
        }

        [Fact]
        public void GetAnnotation_WithAnnotation_ReturnsAnnotation()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var annotations = new Dictionary<LogEntry, LogAnnotation>();
            var log = MakeLog("test");
            var annotation = new LogAnnotation { Content = "my note" };
            annotations[log] = annotation;
            SetPrivateField(vm, "_logAnnotations", annotations);

            var result = vm.GetAnnotation(log);
            Assert.NotNull(result);
            Assert.Equal("my note", result!.Content);
        }

        #endregion

        #region CaseManagement: FindLogByTarget

        [Fact]
        public void FindLogByTarget_NullTarget_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var logs = new List<LogEntry> { MakeLog("test") };
            Assert.Null(vm.FindLogByTarget(null, logs));
        }

        [Fact]
        public void FindLogByTarget_NullLogs_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var target = new LogTarget { Timestamp = DateTime.Now, Logger = "L", Thread = "T" };
            Assert.Null(vm.FindLogByTarget(target, null));
        }

        [Fact]
        public void FindLogByTarget_ExactMatch()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var dt = new DateTime(2024, 1, 15, 10, 30, 0);
            var log = MakeLog("hello world", logger: "MyLogger", threadName: "Thread1", date: dt);
            var target = new LogTarget { Timestamp = dt, Logger = "MyLogger", Thread = "Thread1" };

            var result = vm.FindLogByTarget(target, new[] { log });
            Assert.Same(log, result);
        }

        [Fact]
        public void FindLogByTarget_NoMatch_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var dt = new DateTime(2024, 1, 15, 10, 30, 0);
            var log = MakeLog("hello", logger: "A", threadName: "B", date: dt);
            var target = new LogTarget { Timestamp = dt.AddMinutes(5), Logger = "X", Thread = "Y" };

            Assert.Null(vm.FindLogByTarget(target, new[] { log }));
        }

        [Fact]
        public void FindLogByTarget_FallbackByTimeTolerance()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var dt = new DateTime(2024, 1, 15, 10, 30, 0);
            var log = MakeLog("hello world is a very very long message that has more than 50 chars for snippet matching",
                logger: "MyLogger", threadName: "Thread1", date: dt.AddMilliseconds(50));
            var target = new LogTarget
            {
                Timestamp = dt,
                Logger = "MyLogger",
                Thread = "Thread1",
                Snippet = "hello world is a very very long message that has mo"
            };

            var result = vm.FindLogByTarget(target, new[] { log });
            Assert.Same(log, result);
        }

        [Fact]
        public void FindLogByTarget_EmptyLogList_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var target = new LogTarget { Timestamp = DateTime.Now, Logger = "L", Thread = "T" };
            Assert.Null(vm.FindLogByTarget(target, Array.Empty<LogEntry>()));
        }

        #endregion

        #region CaseManagement: CreateLogTarget (private)

        [Fact]
        public void CreateLogTarget_ShortMessage()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = MakeLog("short", level: "ERROR", logger: "L1", threadName: "T1",
                date: new DateTime(2024, 3, 1));

            var target = InvokePrivate<CaseManagementViewModel, LogTarget>(vm, "CreateLogTarget", log);
            Assert.NotNull(target);
            Assert.Equal(new DateTime(2024, 3, 1), target!.Timestamp);
            Assert.Equal("L1", target.Logger);
            Assert.Equal("T1", target.Thread);
            Assert.Equal("ERROR", target.Level);
            Assert.Equal("short", target.Snippet);
        }

        [Fact]
        public void CreateLogTarget_LongMessage_TruncatesSnippet()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            string longMsg = new string('A', 200);
            var log = MakeLog(longMsg, level: "INFO", logger: "L2", threadName: "T2");

            var target = InvokePrivate<CaseManagementViewModel, LogTarget>(vm, "CreateLogTarget", log);
            Assert.NotNull(target);
            Assert.Equal(100, target!.Snippet.Length);
        }

        [Fact]
        public void CreateLogTarget_NullMessage_UsesEmpty()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = new LogEntry { Message = null!, Level = "WARN", Logger = "L3", ThreadName = "T3" };

            var target = InvokePrivate<CaseManagementViewModel, LogTarget>(vm, "CreateLogTarget", log);
            Assert.NotNull(target);
            Assert.Equal("", target!.Snippet);
        }

        #endregion

        #region CaseManagement: ClearAnnotations, ClearMarkedLogs

        [Fact]
        public void ClearAnnotations_EmptiesDictionary()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var annotations = new Dictionary<LogEntry, LogAnnotation>();
            annotations[MakeLog("a")] = new LogAnnotation { Content = "note" };
            SetPrivateField(vm, "_logAnnotations", annotations);

            vm.ClearAnnotations();
            Assert.Empty(vm.LogAnnotations);
        }

        [Fact]
        public void ClearMarkedLogs_ClearsBothCollections()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var marked = new ObservableCollection<LogEntry> { MakeLog("a"), MakeLog("b") };
            var markedApp = new ObservableCollection<LogEntry> { MakeLog("c") };
            vm.MarkedLogs = marked;
            vm.MarkedAppLogs = markedApp;

            vm.ClearMarkedLogs();
            Assert.Empty(vm.MarkedLogs);
            Assert.Empty(vm.MarkedAppLogs);
        }

        #endregion

        #region CaseManagement: Property Setters

        [Fact]
        public void SelectedConfig_SetGet()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var config = new SavedConfiguration { Name = "Test" };
            vm.SelectedConfig = config;
            Assert.Same(config, vm.SelectedConfig);
        }

        [Fact]
        public void ShowAllAnnotations_DefaultFalse()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_showAllAnnotations", false);
            Assert.False(vm.ShowAllAnnotations);
        }

        [Fact]
        public void IsLoadingCase_ReflectsPrivateField()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_isLoadingCase", true);
            Assert.True(vm.IsLoadingCase);
        }

        [Fact]
        public void MainColoringRules_SetGet()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var rules = new List<ColoringCondition>();
            vm.MainColoringRules = rules;
            Assert.Same(rules, vm.MainColoringRules);
        }

        [Fact]
        public void AppColoringRules_SetGet()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var rules = new List<ColoringCondition>();
            vm.AppColoringRules = rules;
            Assert.Same(rules, vm.AppColoringRules);
        }

        #endregion

        #region CaseManagement: UpdateAllAnnotationsVisibility

        [Fact]
        public void UpdateAllAnnotationsVisibility_CollapsesAnnotations()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_showAllAnnotations", false);

            var sessionVM = CreateUninitialized<LogSessionViewModel>();
            var log1 = MakeLog("a");
            log1.HasAnnotation = true;
            log1.IsAnnotationExpanded = true;
            var log2 = MakeLog("b");
            log2.HasAnnotation = false;
            log2.IsAnnotationExpanded = false;
            var logs = new List<LogEntry> { log1, log2 };
            SetPrivateField(sessionVM, "_allLogsCache", (IList<LogEntry>)logs);
            SetPrivateField(sessionVM, "_allAppLogsCache", (IList<LogEntry>)new List<LogEntry>());
            SetPrivateField(vm, "_sessionVM", sessionVM);

            vm.UpdateAllAnnotationsVisibility();

            Assert.False(log1.IsAnnotationExpanded);
        }

        #endregion

        #region CaseManagement: ToggleAnnotation (private)

        [Fact]
        public void ToggleAnnotation_WithAnnotation_Toggles()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = MakeLog("test");
            log.HasAnnotation = true;
            log.IsAnnotationExpanded = false;

            InvokePrivateVoid(vm, "ToggleAnnotation", (object?)log);
            Assert.True(log.IsAnnotationExpanded);

            InvokePrivateVoid(vm, "ToggleAnnotation", (object?)log);
            Assert.False(log.IsAnnotationExpanded);
        }

        [Fact]
        public void ToggleAnnotation_WithoutAnnotation_DoesNothing()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = MakeLog("test");
            log.HasAnnotation = false;
            log.IsAnnotationExpanded = false;

            InvokePrivateVoid(vm, "ToggleAnnotation", (object?)log);
            Assert.False(log.IsAnnotationExpanded);
        }

        [Fact]
        public void ToggleAnnotation_NullParameter_NoException()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            InvokePrivateVoid(vm, "ToggleAnnotation", (object?)null);
        }

        #endregion

        #region CaseManagement: CloseAnnotation (private)

        [Fact]
        public void CloseAnnotation_CollapsesLog()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = MakeLog("test");
            log.IsAnnotationExpanded = true;

            InvokePrivateVoid(vm, "CloseAnnotation", (object?)log);
            Assert.False(log.IsAnnotationExpanded);
        }

        [Fact]
        public void CloseAnnotation_NullParam_NoException()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            InvokePrivateVoid(vm, "CloseAnnotation", (object?)null);
        }

        #endregion

        #region CaseManagement: ClearCurrentMarked (private)

        [Fact]
        public void ClearCurrentMarked_ClearsAndNulls()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = MakeLog("x");
            log.IsCurrentMarked = true;
            SetPrivateField(vm, "_currentMarkedLog", log);

            InvokePrivateVoid(vm, "ClearCurrentMarked");

            Assert.False(log.IsCurrentMarked);
            Assert.Null(GetPrivateField(vm, "_currentMarkedLog"));
        }

        [Fact]
        public void ClearCurrentMarked_NullCurrentLog_NoException()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetPrivateField(vm, "_currentMarkedLog", (LogEntry?)null);
            InvokePrivateVoid(vm, "ClearCurrentMarked");
        }

        #endregion

        // =====================================================================
        // GlobalGrepViewModel Tests
        // =====================================================================

        #region GlobalGrep: Properties

        [Fact]
        public void GlobalGrep_ResultCount_ReturnsCount()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var results = new ObservableRangeCollection<GrepResult>();
            results.Add(new GrepResult { PreviewText = "test" });
            results.Add(new GrepResult { PreviewText = "test2" });
            SetPrivateField(vm, "_results", results);

            Assert.Equal(2, vm.ResultCount);
        }

        [Fact]
        public void GlobalGrep_ResultCount_NullResults_ReturnsZero()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_results", (ObservableRangeCollection<GrepResult>?)null!);

            Assert.Equal(0, vm.ResultCount);
        }

        [Fact]
        public void GlobalGrep_SearchQuery_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SearchQuery = "test pattern";
            Assert.Equal("test pattern", vm.SearchQuery);
        }

        [Fact]
        public void GlobalGrep_UseRegex_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.UseRegex = true;
            Assert.True(vm.UseRegex);
        }

        [Fact]
        public void GlobalGrep_SearchPLC_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SearchPLC = true;
            Assert.True(vm.SearchPLC);
        }

        [Fact]
        public void GlobalGrep_SearchAPP_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SearchAPP = false;
            Assert.False(vm.SearchAPP);
        }

        [Fact]
        public void GlobalGrep_IsSearching_TogglesIsNotSearching()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.IsSearching = true;
            Assert.True(vm.IsSearching);
            Assert.False(vm.IsNotSearching);

            vm.IsSearching = false;
            Assert.False(vm.IsSearching);
            Assert.True(vm.IsNotSearching);
        }

        [Fact]
        public void GlobalGrep_StatusMessage_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.StatusMessage = "hello";
            Assert.Equal("hello", vm.StatusMessage);
        }

        [Fact]
        public void GlobalGrep_SearchDuration_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SearchDuration = "(500ms)";
            Assert.Equal("(500ms)", vm.SearchDuration);
        }

        [Fact]
        public void GlobalGrep_ProgressCurrent_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.ProgressCurrent = 5;
            Assert.Equal(5, vm.ProgressCurrent);
        }

        [Fact]
        public void GlobalGrep_ProgressTotal_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.ProgressTotal = 10;
            Assert.Equal(10, vm.ProgressTotal);
        }

        [Fact]
        public void GlobalGrep_SelectedResult_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var result = new GrepResult { PreviewText = "found it" };
            vm.SelectedResult = result;
            Assert.Same(result, vm.SelectedResult);
        }

        [Fact]
        public void GlobalGrep_SelectedLocation_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var loc = new SearchLocation { Name = "Sim1" };
            vm.SelectedLocation = loc;
            Assert.Same(loc, vm.SelectedLocation);
        }

        [Fact]
        public void GlobalGrep_ExternalPath_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.ExternalPath = @"C:\temp";
            Assert.Equal(@"C:\temp", vm.ExternalPath);
        }

        [Fact]
        public void GlobalGrep_FileTimeFrom_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var dt = new DateTime(2024, 6, 1);
            vm.FileTimeFrom = dt;
            Assert.Equal(dt, vm.FileTimeFrom);
        }

        [Fact]
        public void GlobalGrep_FileTimeTo_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var dt = new DateTime(2024, 12, 31);
            vm.FileTimeTo = dt;
            Assert.Equal(dt, vm.FileTimeTo);
        }

        [Fact]
        public void GlobalGrep_ResultTimeFrom_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.ResultTimeFrom = null;
            Assert.Null(vm.ResultTimeFrom);
        }

        [Fact]
        public void GlobalGrep_HasConditions_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.HasConditions = true;
            Assert.True(vm.HasConditions);
        }

        [Fact]
        public void GlobalGrep_SelectedGroupOperator_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SelectedGroupOperator = LogicalGroupOperator.Or;
            Assert.Equal(LogicalGroupOperator.Or, vm.SelectedGroupOperator);
        }

        [Fact]
        public void GlobalGrep_SelectedProfile_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            // SelectedProfile setter calls UpdateProfilePreview, which needs _configService
            // We just test the field directly
            SetPrivateField(vm, "_selectedProfile", "TestProfile");
            Assert.Equal("TestProfile", vm.SelectedProfile);
        }

        [Fact]
        public void GlobalGrep_ProfilePreview_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.ProfilePreview = "preview text";
            Assert.Equal("preview text", vm.ProfilePreview);
        }

        [Fact]
        public void GlobalGrep_SearchMode_Default()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            Assert.Equal(GlobalGrepViewModel.SearchModeType.LoadedSessions, vm.SearchMode);
        }

        [Fact]
        public void GlobalGrep_SelectedSchedule_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var schedule = new ScheduledSearch { Name = "Daily" };
            vm.SelectedSchedule = schedule;
            Assert.Same(schedule, vm.SelectedSchedule);
        }

        [Fact]
        public void GlobalGrep_SelectedQuickSearchField_SetGet()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            vm.SelectedQuickSearchField = SearchField.Message;
            Assert.Equal(SearchField.Message, vm.SelectedQuickSearchField);
        }

        #endregion

        #region GlobalGrep: BuildCriteria, BuildQuickSearchCriteria (private)

        [Fact]
        public void BuildCriteria_BasicStructure()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", false);
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.Or);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)null);

            var groups = new ObservableCollection<ConditionGroupVM>();
            var group = new ConditionGroupVM { Operator = ConditionOperator.And };
            var cond = new ConditionVM { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error" };
            group.Conditions.Add(cond);
            groups.Add(group);

            // Need to set ConditionGroups via reflection since it's a property with no setter
            var prop = typeof(GlobalGrepViewModel).GetProperty("ConditionGroups");
            // ConditionGroups is { get; } so set the backing field
            // Actually it's initialized in constructor. Let's use reflection on field
            var field = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, groups);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildCriteria");
            Assert.NotNull(criteria);
            Assert.True(criteria!.SearchPLC);
            Assert.False(criteria.SearchAPP);
            Assert.Equal(LogicalGroupOperator.Or, criteria.GroupOperator);
            Assert.Single(criteria.Groups);
            Assert.Single(criteria.Groups[0].Conditions);
            Assert.Equal("error", criteria.Groups[0].Conditions[0].Value);
        }

        [Fact]
        public void BuildCriteria_WithTimeFilters()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", true);
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 12, 31);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)from);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)to);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)from);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)to);

            var groups = new ObservableCollection<ConditionGroupVM>();
            groups.Add(new ConditionGroupVM());
            var field = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, groups);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildCriteria");
            Assert.NotNull(criteria);
            Assert.NotNull(criteria!.FileTimeFilter);
            Assert.Equal(from, criteria.FileTimeFilter!.From);
            Assert.NotNull(criteria.ResultTimeFilter);
        }

        [Fact]
        public void BuildQuickSearchCriteria_Contains()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", true);
            SetPrivateField(vm, "_searchQuery", "test");
            SetPrivateField(vm, "_selectedQuickSearchField", SearchField.Message);
            SetPrivateField(vm, "_useRegex", false);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)null);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildQuickSearchCriteria");
            Assert.NotNull(criteria);
            Assert.Single(criteria!.Groups);
            Assert.Equal(SearchOperator.Contains, criteria.Groups[0].Conditions[0].Operator);
            Assert.Equal("test", criteria.Groups[0].Conditions[0].Value);
        }

        [Fact]
        public void BuildQuickSearchCriteria_Regex()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", true);
            SetPrivateField(vm, "_searchQuery", "err.*");
            SetPrivateField(vm, "_selectedQuickSearchField", SearchField.Any);
            SetPrivateField(vm, "_useRegex", true);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)null);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildQuickSearchCriteria");
            Assert.NotNull(criteria);
            Assert.Equal(SearchOperator.Regex, criteria!.Groups[0].Conditions[0].Operator);
        }

        #endregion

        #region GlobalGrep: ClearResults (private)

        [Fact]
        public void ClearResults_EmptiesResults()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var results = new ObservableRangeCollection<GrepResult>();
            results.Add(new GrepResult { PreviewText = "test" });
            SetPrivateField(vm, "_results", results);
            SetPrivateField(vm, "_statusMessage", "old");
            SetPrivateField(vm, "_searchDuration", "100ms");
            SetPrivateField(vm, "_selectedResult", new GrepResult());

            InvokePrivateVoid(vm, "ClearResults");

            Assert.Empty(vm.Results);
            Assert.Equal("Results cleared.", vm.StatusMessage);
            Assert.Equal("", vm.SearchDuration);
            Assert.Null(vm.SelectedResult);
        }

        #endregion

        #region GlobalGrep: FindFirstOccurrence (private)

        [Fact]
        public void FindFirstOccurrence_SelectsEarliestTimestamp()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var results = new ObservableRangeCollection<GrepResult>();
            results.Add(new GrepResult { Timestamp = new DateTime(2024, 6, 15), SessionName = "S2" });
            results.Add(new GrepResult { Timestamp = new DateTime(2024, 1, 1), SessionName = "S1" });
            results.Add(new GrepResult { Timestamp = null, SessionName = "S3" });
            SetPrivateField(vm, "_results", results);
            SetPrivateField(vm, "_statusMessage", "");

            InvokePrivateVoid(vm, "FindFirstOccurrence");

            Assert.NotNull(vm.SelectedResult);
            Assert.Equal("S1", vm.SelectedResult!.SessionName);
        }

        [Fact]
        public void FindFirstOccurrence_NoTimestamps_NoSelection()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var results = new ObservableRangeCollection<GrepResult>();
            results.Add(new GrepResult { Timestamp = null });
            SetPrivateField(vm, "_results", results);
            SetPrivateField(vm, "_selectedResult", (GrepResult?)null);

            InvokePrivateVoid(vm, "FindFirstOccurrence");
            Assert.Null(vm.SelectedResult);
        }

        #endregion

        #region GlobalGrep: GetUniqueFiles

        [Fact]
        public void GetUniqueFiles_ReturnsDistinctFiles()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var results = new ObservableRangeCollection<GrepResult>();
            results.Add(new GrepResult { FilePath = "a.zip", SessionName = "A" });
            results.Add(new GrepResult { FilePath = "a.zip", SessionName = "A" });
            results.Add(new GrepResult { FilePath = "b.zip", SessionName = "B" });
            results.Add(new GrepResult { FilePath = "", SessionName = "" }); // should be excluded
            SetPrivateField(vm, "_results", results);

            var files = vm.GetUniqueFiles();
            Assert.Equal(2, files.Count);
            Assert.Equal("A", files[0].SessionName);
            Assert.Equal("B", files[1].SessionName);
        }

        [Fact]
        public void GetUniqueFiles_EmptyResults_EmptyList()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_results", new ObservableRangeCollection<GrepResult>());
            Assert.Empty(vm.GetUniqueFiles());
        }

        #endregion

        #region GlobalGrep: BuildCriteriaSummary (private)

        [Fact]
        public void BuildCriteriaSummary_WithConditions()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);

            var groups = new ObservableCollection<ConditionGroupVM>();
            var g1 = new ConditionGroupVM { Operator = ConditionOperator.Or };
            g1.Conditions.Add(new ConditionVM { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error" });
            g1.Conditions.Add(new ConditionVM { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "FATAL", Negate = true });
            groups.Add(g1);

            var field = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, groups);

            var summary = InvokePrivate<GlobalGrepViewModel, string>(vm, "BuildCriteriaSummary");
            Assert.NotNull(summary);
            Assert.Contains("error", summary!);
            Assert.Contains("NOT", summary);
        }

        [Fact]
        public void BuildCriteriaSummary_EmptyConditions_ReturnsEmpty()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);

            var groups = new ObservableCollection<ConditionGroupVM>();
            groups.Add(new ConditionGroupVM());

            var field = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, groups);

            var summary = InvokePrivate<GlobalGrepViewModel, string>(vm, "BuildCriteriaSummary");
            Assert.Equal("", summary);
        }

        #endregion

        #region GlobalGrep: FormatTimeRange (private static)

        [Fact]
        public void FormatTimeRange_BothNull_ReturnsNull()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (string?)method?.Invoke(null, new object?[] { null, null });
            Assert.Null(result);
        }

        [Fact]
        public void FormatTimeRange_FromOnly()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange",
                BindingFlags.Static | BindingFlags.NonPublic);
            var from = new DateTime(2024, 3, 15);
            var result = (string?)method?.Invoke(null, new object?[] { (DateTime?)from, null });
            Assert.NotNull(result);
            Assert.Contains("2024-03-15", result!);
            Assert.Contains("...", result);
        }

        [Fact]
        public void FormatTimeRange_BothSet()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange",
                BindingFlags.Static | BindingFlags.NonPublic);
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 12, 31);
            var result = (string?)method?.Invoke(null, new object?[] { (DateTime?)from, (DateTime?)to });
            Assert.Contains("2024-01-01", result!);
            Assert.Contains("2024-12-31", result);
            Assert.Contains("to", result);
        }

        #endregion

        #region GlobalGrep: Esc (private static)

        [Fact]
        public void Esc_Null_ReturnsEmpty()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (string?)method?.Invoke(null, new object?[] { null });
            Assert.Equal("", result);
        }

        [Fact]
        public void Esc_WithQuotes_Escapes()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (string?)method?.Invoke(null, new object?[] { "say \"hello\"" });
            Assert.Equal("say \"\"hello\"\"", result);
        }

        [Fact]
        public void Esc_NoSpecialChars_Unchanged()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (string?)method?.Invoke(null, new object?[] { "plain text" });
            Assert.Equal("plain text", result);
        }

        #endregion

        #region GlobalGrep: CancelSearch (private)

        [Fact]
        public void CancelSearch_SetsStatusMessage()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_cancellationTokenSource", new System.Threading.CancellationTokenSource());
            SetPrivateField(vm, "_statusMessage", "");

            InvokePrivateVoid(vm, "CancelSearch");
            Assert.Equal("Cancelling search...", vm.StatusMessage);
        }

        #endregion

        #region GlobalGrep: BuildFullCriteria (private)

        [Fact]
        public void BuildFullCriteria_WithQuickSearch_AddsGroup()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", true);
            SetPrivateField(vm, "_searchQuery", "quick text");
            SetPrivateField(vm, "_selectedQuickSearchField", SearchField.Message);
            SetPrivateField(vm, "_useRegex", false);
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)null);

            var groups = new ObservableCollection<ConditionGroupVM>();
            groups.Add(new ConditionGroupVM());
            var field = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, groups);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildFullCriteria");
            Assert.NotNull(criteria);
            // Should have the quick search group inserted at index 0
            Assert.True(criteria!.Groups.Count >= 1);
            Assert.Equal("quick text", criteria.Groups[0].Conditions[0].Value);
        }

        [Fact]
        public void BuildFullCriteria_NoQuickSearch_NoExtraGroup()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetPrivateField(vm, "_searchPLC", true);
            SetPrivateField(vm, "_searchAPP", true);
            SetPrivateField(vm, "_searchQuery", "");
            SetPrivateField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);
            SetPrivateField(vm, "_fileTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_fileTimeTo", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeFrom", (DateTime?)null);
            SetPrivateField(vm, "_resultTimeTo", (DateTime?)null);

            var groups = new ObservableCollection<ConditionGroupVM>();
            groups.Add(new ConditionGroupVM());
            var condField = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            condField?.SetValue(vm, groups);

            var criteria = InvokePrivate<GlobalGrepViewModel, SearchCriteria>(vm, "BuildFullCriteria");
            Assert.NotNull(criteria);
            Assert.Empty(criteria!.Groups);
        }

        #endregion

        // =====================================================================
        // ComparisonPaneViewModel Tests
        // =====================================================================

        #region ComparisonPane: BinarySearchNearest

        [Fact]
        public void BinarySearchNearest_EmptyList_ReturnsMinusOne()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), new List<LogEntry>());
            int result = vm.BinarySearchNearest(DateTime.Now);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearest_ExactMatch()
        {
            var t1 = new DateTime(2024, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2024, 1, 1, 11, 0, 0);
            var t3 = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: t1), MakeLog("b", date: t2), MakeLog("c", date: t3)
            };
            var vm = new ComparisonPaneViewModel(logs, new List<LogEntry>());

            Assert.Equal(1, vm.BinarySearchNearest(t2));
        }

        [Fact]
        public void BinarySearchNearest_BeforeFirst_ReturnsZero()
        {
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: new DateTime(2024, 6, 1)),
                MakeLog("b", date: new DateTime(2024, 7, 1))
            };
            var vm = new ComparisonPaneViewModel(logs, new List<LogEntry>());

            Assert.Equal(0, vm.BinarySearchNearest(new DateTime(2024, 1, 1)));
        }

        [Fact]
        public void BinarySearchNearest_AfterLast_ReturnsLast()
        {
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: new DateTime(2024, 1, 1)),
                MakeLog("b", date: new DateTime(2024, 6, 1))
            };
            var vm = new ComparisonPaneViewModel(logs, new List<LogEntry>());

            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2025, 1, 1)));
        }

        [Fact]
        public void BinarySearchNearest_ClosestNeighbor()
        {
            var t1 = new DateTime(2024, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("a", date: t1), MakeLog("b", date: t2)
            };
            var vm = new ComparisonPaneViewModel(logs, new List<LogEntry>());

            // 10:30 is closer to 10:00 than 12:00
            Assert.Equal(0, vm.BinarySearchNearest(new DateTime(2024, 1, 1, 10, 30, 0)));
            // 11:30 is closer to 12:00 than 10:00
            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2024, 1, 1, 11, 30, 0)));
        }

        #endregion

        #region ComparisonPane: GetLogAtIndex

        [Fact]
        public void GetLogAtIndex_ValidIndex()
        {
            var log = MakeLog("test", date: new DateTime(2024, 1, 1));
            var vm = new ComparisonPaneViewModel(new List<LogEntry> { log }, new List<LogEntry>());

            Assert.Same(log, vm.GetLogAtIndex(0));
        }

        [Fact]
        public void GetLogAtIndex_OutOfRange_ReturnsNull()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.Null(vm.GetLogAtIndex(0));
            Assert.Null(vm.GetLogAtIndex(-1));
        }

        #endregion

        #region ComparisonPane: ApplySourceFilter

        [Fact]
        public void ApplySourceFilter_AllPLC()
        {
            var plcLog = MakeLog("plc", date: new DateTime(2024, 1, 1));
            var appLog = MakeLog("app", date: new DateTime(2024, 1, 2));
            var vm = new ComparisonPaneViewModel(
                new List<LogEntry> { plcLog },
                new List<LogEntry> { appLog });

            // Default is AllPLC
            Assert.Contains(plcLog, vm.FilteredLogs);
        }

        [Fact]
        public void ApplySourceFilter_AllAPP()
        {
            var plcLog = MakeLog("plc", date: new DateTime(2024, 1, 1));
            var appLog = MakeLog("app", date: new DateTime(2024, 1, 2));
            var vm = new ComparisonPaneViewModel(
                new List<LogEntry> { plcLog },
                new List<LogEntry> { appLog });

            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllAPP;
            Assert.Contains(appLog, vm.FilteredLogs);
            Assert.DoesNotContain(plcLog, vm.FilteredLogs);
        }

        #endregion

        // =====================================================================
        // ScheduleEditorViewModel Tests
        // =====================================================================

        #region ScheduleEditor: LoadFromSchedule

        [Fact]
        public void ScheduleEditor_LoadFromSchedule_DailyType()
        {
            var schedule = new ScheduledSearch
            {
                Name = "Test Daily",
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 30, 0),
                ScanMode = ScanMode.SearchOnly,
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };

            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());

            Assert.Equal("Test Daily", vm.ScheduleName);
            Assert.True(vm.IsEnabled);
            Assert.Equal(1, vm.ScheduleTypeIndex); // Daily = 1
            Assert.Equal("14", vm.RunHour);
            Assert.Equal("30", vm.RunMinute);
            Assert.True(vm.SearchPLC);
            Assert.False(vm.SearchAPP);
            Assert.True(vm.IsSimpleMode); // single condition -> simple mode
            Assert.Equal("error", vm.SimpleSearchText);
        }

        [Fact]
        public void ScheduleEditor_LoadFromSchedule_WeeklyType()
        {
            var schedule = new ScheduledSearch
            {
                Name = "Weekly",
                ScheduleType = ScheduleType.Weekly,
                RunTime = new TimeSpan(8, 0, 0),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true }
            };

            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.Equal(2, vm.ScheduleTypeIndex);
            Assert.True(vm.DayMon);
            Assert.True(vm.DayFri);
            Assert.False(vm.DaySun);
            Assert.True(vm.ScanModeStats);
        }

        [Fact]
        public void ScheduleEditor_LoadFromSchedule_IntervalType()
        {
            var schedule = new ScheduledSearch
            {
                Name = "Interval",
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true }
            };

            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.Equal(3, vm.ScheduleTypeIndex);
            Assert.Equal("30", vm.IntervalValue);
            Assert.Equal(0, vm.IntervalUnitIndex); // Minutes = 0
        }

        [Fact]
        public void ScheduleEditor_LoadFromSchedule_AdvancedConditions()
        {
            var schedule = new ScheduledSearch
            {
                Name = "Advanced",
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = true,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Operator = ConditionOperator.Or,
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "error" },
                                new SearchCondition { Field = SearchField.Level, Value = "FATAL", Negate = true }
                            }
                        }
                    }
                }
            };

            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.False(vm.IsSimpleMode); // >1 condition -> advanced
            Assert.Equal(2, vm.Conditions.Count);
            Assert.True(vm.Conditions[1].Negate);
        }

        #endregion

        #region ScheduleEditor: Properties

        [Fact]
        public void ScheduleEditor_NeedsSearch_True_ForSearchMode()
        {
            var schedule = new ScheduledSearch
            {
                ScanMode = ScanMode.SearchOnly,
                Criteria = new SearchCriteria()
            };
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.NeedsSearch);
        }

        [Fact]
        public void ScheduleEditor_NeedsSearch_False_ForStatsOnly()
        {
            var schedule = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria()
            };
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.False(vm.NeedsSearch);
        }

        [Fact]
        public void ScheduleEditor_IsOnce_WhenIndex0()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { ScheduleType = ScheduleType.Once, Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.IsOnce);
            Assert.False(vm.IsWeekly);
            Assert.False(vm.IsInterval);
        }

        [Fact]
        public void ScheduleEditor_IsWeekly_WhenIndex2()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { ScheduleType = ScheduleType.Weekly, Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.IsWeekly);
        }

        [Fact]
        public void ScheduleEditor_IsInterval_WhenIndex3()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { ScheduleType = ScheduleType.Interval, Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.IsInterval);
        }

        [Fact]
        public void ScheduleEditor_TimeLabelText_DailyShowsRunTime()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { ScheduleType = ScheduleType.Daily, Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            Assert.Equal("Run Time (HH:mm)", vm.TimeLabelText);
        }

        [Fact]
        public void ScheduleEditor_TimeLabelText_IntervalShowsStartTime()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { ScheduleType = ScheduleType.Interval, Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            Assert.Equal("Start Time (HH:mm)", vm.TimeLabelText);
        }

        [Fact]
        public void ScheduleEditor_IsAdvancedMode_InverseOfSimple()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.IsSimpleMode = true;
            Assert.False(vm.IsAdvancedMode);
            vm.IsSimpleMode = false;
            Assert.True(vm.IsAdvancedMode);
        }

        #endregion

        #region ScheduleEditor: Condition/Location Management

        [Fact]
        public void ScheduleEditor_AddCondition()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            int before = vm.Conditions.Count;
            vm.AddConditionCommand.Execute(null);
            Assert.Equal(before + 1, vm.Conditions.Count);
        }

        [Fact]
        public void ScheduleEditor_RemoveCondition()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.AddConditionCommand.Execute(null);
            var cond = vm.Conditions.Last();
            vm.RemoveConditionCommand.Execute(cond);
            Assert.DoesNotContain(cond, vm.Conditions);
        }

        [Fact]
        public void ScheduleEditor_SelectAllLocations()
        {
            var locations = new List<SearchLocation>
            {
                new SearchLocation { Name = "A" },
                new SearchLocation { Name = "B" }
            };
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                locations, new SearchCriteria());

            vm.SelectNoLocationsCommand.Execute(null);
            Assert.All(vm.LocationItems, l => Assert.False(l.IsChecked));

            vm.SelectAllLocationsCommand.Execute(null);
            Assert.All(vm.LocationItems, l => Assert.True(l.IsChecked));
        }

        #endregion

        #region ScheduleEditor: Email

        [Fact]
        public void ScheduleEditor_AddRecipient_ValidEmail()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.NewRecipient = "user@example.com";
            InvokePrivateVoid(vm, "AddRecipient");
            Assert.Contains("user@example.com", vm.Recipients);
            Assert.Equal("", vm.NewRecipient);
        }

        [Fact]
        public void ScheduleEditor_AddRecipient_InvalidEmail_DoesNothing()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.NewRecipient = "notanemail";
            InvokePrivateVoid(vm, "AddRecipient");
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void ScheduleEditor_RemoveRecipient()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.Recipients.Add("user@example.com");
            InvokePrivateVoid(vm, "RemoveRecipient", (object?)"user@example.com");
            Assert.Empty(vm.Recipients);
        }

        [Fact]
        public void ScheduleEditor_RemoveRecipient_NullDoesNothing()
        {
            var vm = new ScheduleEditorViewModel(
                new ScheduledSearch { Criteria = new SearchCriteria() },
                new List<SearchLocation>(), new SearchCriteria());
            vm.Recipients.Add("user@example.com");
            InvokePrivateVoid(vm, "RemoveRecipient", (object?)(string?)null);
            Assert.Single(vm.Recipients);
        }

        [Fact]
        public void ScheduleEditor_LoadEmailConfig()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria(),
                EmailConfig = new EmailNotificationConfig
                {
                    IsEnabled = true,
                    Recipients = new List<string> { "a@b.com" },
                    Timing = EmailTiming.AtSpecificTime,
                    SendTime = new TimeSpan(9, 15, 0),
                    CustomSubject = "Alert!"
                }
            };
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.EmailEnabled);
            Assert.Single(vm.Recipients);
            Assert.True(vm.TimingDeferred);
            Assert.Equal("09", vm.EmailHour);
            Assert.Equal("15", vm.EmailMinute);
            Assert.Equal("Alert!", vm.CustomSubject);
        }

        [Fact]
        public void ScheduleEditor_TimeFilterProperties()
        {
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 12, 31);
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    FileTimeFilter = new TimeRangeFilter { From = from, To = to },
                    ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours }
                }
            };
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            Assert.True(vm.FileFilterCustom);
            Assert.Equal(from, vm.FileFromDate);
            Assert.Equal(to, vm.FileToDate);
            Assert.True(vm.ResultFilter24h);
        }

        #endregion

        // =====================================================================
        // TabOrchestrationViewModel Tests
        // =====================================================================

        #region TabOrchestration: Properties

        [Fact]
        public void TabOrchestration_TimeSyncOffsetSeconds_SetGet()
        {
            var vm = CreateUninitialized<TabOrchestrationViewModel>();
            vm.TimeSyncOffsetSeconds = 42;
            Assert.Equal(42, vm.TimeSyncOffsetSeconds);
        }

        [Fact]
        public void TabOrchestration_PendingSyncLog_SetGet()
        {
            var vm = CreateUninitialized<TabOrchestrationViewModel>();
            var log = MakeLog("test");
            vm.PendingSyncLog = log;
            Assert.Same(log, vm.PendingSyncLog);
        }

        [Fact]
        public void TabOrchestration_PendingSyncTabIndex_SetGet()
        {
            var vm = CreateUninitialized<TabOrchestrationViewModel>();
            vm.PendingSyncTabIndex = 3;
            Assert.Equal(3, vm.PendingSyncTabIndex);
        }

        #endregion

        // =====================================================================
        // CprAnalysisViewModel Tests
        // =====================================================================

        #region CprAnalysis: ParseIntList (private static)

        [Theory]
        [InlineData("", new int[0])]
        [InlineData("   ", new int[0])]
        [InlineData("1 2 3", new int[] { 1, 2, 3 })]
        [InlineData("1,2,3", new int[] { 1, 2, 3 })]
        [InlineData("1;2;3", new int[] { 1, 2, 3 })]
        [InlineData("1, 2, 3", new int[] { 1, 2, 3 })]
        [InlineData("0 -1 5", new int[] { 5 })] // 0 and negative filtered out
        public void CprAnalysis_ParseIntList(string input, int[] expected)
        {
            var method = typeof(CprAnalysisViewModel).GetMethod("ParseIntList",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (int[]?)method?.Invoke(null, new object[] { input });
            Assert.Equal(expected, result);
        }

        #endregion

        #region CprAnalysis: Properties

        [Fact]
        public void CprAnalysis_IsBlanketCyclesVisible()
        {
            var vm = CreateUninitialized<CprAnalysisViewModel>();
            SetPrivateField(vm, "_selectedGraphType", CprGraphType.BlanketCycles);
            Assert.True(vm.IsBlanketCyclesVisible);

            SetPrivateField(vm, "_selectedGraphType", CprGraphType.Colors);
            Assert.False(vm.IsBlanketCyclesVisible);
        }

        [Fact]
        public void CprAnalysis_IsHistoStationsVisible()
        {
            var vm = CreateUninitialized<CprAnalysisViewModel>();
            SetPrivateField(vm, "_selectedGraphType", CprGraphType.Histogram);
            Assert.True(vm.IsHistoStationsVisible);

            SetPrivateField(vm, "_selectedGraphType", CprGraphType.Colors);
            Assert.False(vm.IsHistoStationsVisible);
        }

        [Fact]
        public void CprAnalysis_IsManualYVisible()
        {
            var vm = CreateUninitialized<CprAnalysisViewModel>();
            SetPrivateField(vm, "_autoYAxis", false);
            Assert.True(vm.IsManualYVisible);

            SetPrivateField(vm, "_autoYAxis", true);
            Assert.False(vm.IsManualYVisible);
        }

        [Fact]
        public void CprAnalysis_StationArraySize()
        {
            // Use constructor to get properly initialized arrays
            var vm = new CprAnalysisViewModel(new TestDialogService());
            Assert.Equal(6, vm.StationTestSelections.Length);
            Assert.Equal(6, vm.StationRefSelections.Length);
        }

        [Fact]
        public void CprAnalysis_BuildFilterState()
        {
            var vm = CreateUninitialized<CprAnalysisViewModel>();
            SetPrivateField(vm, "_selectedMachine", 42);
            SetPrivateField(vm, "_selectedCalibTime", "2024-01-01");
            SetPrivateField(vm, "_selectedRevolution", "Rev1");
            SetPrivateField(vm, "_selectedIteration", 3);
            SetPrivateField(vm, "_selectedCycleFrom", 1);
            SetPrivateField(vm, "_selectedCycleTo", 10);
            SetPrivateField(vm, "_selectedColumnFrom", 1);
            SetPrivateField(vm, "_selectedColumnTo", 5);
            SetPrivateField(vm, "_isYAxis", true);
            SetPrivateField(vm, "_removeDC", true);
            SetPrivateField(vm, "_autoYAxis", false);
            SetPrivateField(vm, "_sharedYAxis", true);
            SetPrivateField(vm, "_selectedSmoothing", 3);
            SetPrivateField(vm, "_selectedBowDegree", 4);
            SetPrivateField(vm, "_yAxisFrom", "-100");
            SetPrivateField(vm, "_yAxisTo", "100");

            var state = InvokePrivate<CprAnalysisViewModel, CprFilterState>(vm, "BuildFilterState");
            Assert.NotNull(state);
            Assert.Equal(42, state!.MachineSN);
            Assert.Equal("2024-01-01", state.CalibrationTime);
            Assert.Equal("Rev1", state.Revolution);
            Assert.Equal(3, state.Iteration);
            Assert.Equal("Y", state.Axis);
            Assert.True(state.RemoveDC);
            Assert.False(state.AutoYAxis);
            Assert.True(state.SharedYAxis);
            Assert.Equal(3, state.SmoothingWindow);
            Assert.Equal(4, state.BowDegree);
            Assert.Equal(-100.0, state.YAxisFrom);
            Assert.Equal(100.0, state.YAxisTo);
        }

        [Fact]
        public void CprAnalysis_BuildStationPairs()
        {
            var vm = new CprAnalysisViewModel(new TestDialogService());
            vm.StationTestSelections[0] = 1;
            vm.StationRefSelections[0] = 0;
            vm.StationTestSelections[1] = 2;
            vm.StationRefSelections[1] = 3;

            var pairs = InvokePrivate<CprAnalysisViewModel, CprStationPair[]>(vm, "BuildStationPairs");
            Assert.NotNull(pairs);
            Assert.Equal(6, pairs!.Length);
            Assert.Equal(1, pairs[0].TestStation);
            Assert.Equal(0, pairs[0].RefStation);
            Assert.Equal(2, pairs[1].TestStation);
            Assert.Equal(3, pairs[1].RefStation);
        }

        [Fact]
        public void CprAnalysis_SetAllRefStationsBatch()
        {
            var vm = new CprAnalysisViewModel(new TestDialogService());
            SetPrivateField(vm, "_isLoadingFilters", true); // prevent AutoApply from calling _dataService

            vm.SetAllRefStationsBatch(5);

            for (int i = 0; i < 6; i++)
                Assert.Equal(5, vm.StationRefSelections[i]);
        }

        #endregion

        // =====================================================================
        // ConfigExplorerViewModel Tests
        // =====================================================================

        #region ConfigExplorer: Properties

        [Fact]
        public void ConfigExplorer_IsDbFileSelected_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.IsDbFileSelected = true;
            Assert.True(vm.IsDbFileSelected);
        }

        [Fact]
        public void ConfigExplorer_IsCsvFileSelected_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.IsCsvFileSelected = true;
            Assert.True(vm.IsCsvFileSelected);
        }

        [Fact]
        public void ConfigExplorer_IsExplorerMenuOpen_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.IsExplorerMenuOpen = true;
            Assert.True(vm.IsExplorerMenuOpen);
        }

        [Fact]
        public void ConfigExplorer_IsConfigMenuOpen_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.IsConfigMenuOpen = true;
            Assert.True(vm.IsConfigMenuOpen);
        }

        [Fact]
        public void ConfigExplorer_IsLoggersMenuOpen_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.IsLoggersMenuOpen = true;
            Assert.True(vm.IsLoggersMenuOpen);
        }

        [Fact]
        public void ConfigExplorer_ConfigFileContent_SyncsFiltered()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.ConfigFileContent = "some text";
            Assert.Equal("some text", vm.FilteredConfigContent);
        }

        [Fact]
        public void ConfigExplorer_FilteredConfigContent_SetGet()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            vm.FilteredConfigContent = "filtered";
            Assert.Equal("filtered", vm.FilteredConfigContent);
        }

        [Fact]
        public void ConfigExplorer_ClearConfigurationFiles()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var files = new ObservableCollection<string> { "file1.json", "file2.db" };
            vm.ConfigurationFiles = files;
            SetPrivateField(vm, "_dbTreeNodes", new ObservableCollection<DbTreeNode>());
            SetPrivateField(vm, "_allDbTreeNodes", new ObservableCollection<DbTreeNode>());

            vm.ClearConfigurationFiles();

            Assert.Empty(vm.ConfigurationFiles);
            Assert.False(vm.IsDbFileSelected);
            Assert.False(vm.IsCsvFileSelected);
        }

        #endregion

        #region ConfigExplorer: FilterTreeNode (private)

        [Fact]
        public void FilterTreeNode_MatchesSelf()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var node = new DbTreeNode { Name = "TestTable", Type = "Table" };

            var result = InvokePrivate<ConfigExplorerViewModel, bool>(vm, "FilterTreeNode", node, "Test");
            Assert.True(result);
            Assert.True(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_NoMatch()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var node = new DbTreeNode { Name = "MyTable", Type = "Table" };

            var result = InvokePrivate<ConfigExplorerViewModel, bool>(vm, "FilterTreeNode", node, "ZZZZZ");
            Assert.False(result);
            Assert.False(node.IsVisible);
        }

        [Fact]
        public void FilterTreeNode_ChildMatches()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var parent = new DbTreeNode { Name = "Parent" };
            var child = new DbTreeNode { Name = "MatchMe" };
            parent.Children.Add(child);

            var result = InvokePrivate<ConfigExplorerViewModel, bool>(vm, "FilterTreeNode", parent, "Match");
            Assert.True(result);
            Assert.True(parent.IsVisible);
            Assert.True(child.IsVisible);
        }

        #endregion

        #region ConfigExplorer: FilterConfigContent (private)

        [Fact]
        public void FilterConfigContent_EmptySearch_ShowsAll()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            SetPrivateField(vm, "_configSearchText", "");
            vm.ConfigFileContent = "line1\nline2\nline3";

            InvokePrivateVoid(vm, "FilterConfigContent");
            Assert.Equal("line1\nline2\nline3", vm.FilteredConfigContent);
        }

        [Fact]
        public void FilterConfigContent_WithSearch_FiltersLines()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            SetPrivateField(vm, "_configSearchText", "error");
            SetPrivateField(vm, "_configFileContent", "line1 ok\nerror here\nline3 ok\nerror again");

            InvokePrivateVoid(vm, "FilterConfigContent");
            Assert.Contains("error here", vm.FilteredConfigContent!);
            Assert.Contains("error again", vm.FilteredConfigContent!);
            Assert.DoesNotContain("line1", vm.FilteredConfigContent!);
        }

        #endregion

        #region ConfigExplorer: ParseCsvToDataView (private)

        [Fact]
        public void ParseCsvToDataView_ValidCsv()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            string csv = "Name,Value\nA,1\nB,2";

            var view = InvokePrivate<ConfigExplorerViewModel, System.Data.DataView>(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(view);
            Assert.Equal(2, view!.Table!.Rows.Count);
            Assert.Equal(2, view.Table.Columns.Count);
            Assert.Equal("Name", view.Table.Columns[0].ColumnName);
        }

        [Fact]
        public void ParseCsvToDataView_EmptyCsv_ReturnsNull()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var view = InvokePrivate<ConfigExplorerViewModel, System.Data.DataView>(vm, "ParseCsvToDataView", "");
            Assert.Null(view);
        }

        [Fact]
        public void ParseCsvToDataView_DuplicateColumnNames()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            string csv = "Col,Col,Col\n1,2,3";
            var view = InvokePrivate<ConfigExplorerViewModel, System.Data.DataView>(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(view);
            // Should have unique column names
            var names = new HashSet<string>();
            foreach (System.Data.DataColumn col in view!.Table!.Columns)
                Assert.True(names.Add(col.ColumnName));
        }

        [Fact]
        public void ParseCsvToDataView_HeaderOnly()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            string csv = "A,B,C";
            var view = InvokePrivate<ConfigExplorerViewModel, System.Data.DataView>(vm, "ParseCsvToDataView", csv);
            Assert.NotNull(view);
            Assert.Equal(0, view!.Table!.Rows.Count);
            Assert.Equal(3, view.Table.Columns.Count);
        }

        #endregion

        // =====================================================================
        // ConditionVM / ConditionGroupVM Tests
        // =====================================================================

        #region ConditionVM

        [Fact]
        public void ConditionVM_Defaults()
        {
            var vm = new ConditionVM();
            Assert.Equal(SearchField.Any, vm.Field);
            Assert.Equal(SearchOperator.Contains, vm.Operator);
            Assert.Null(vm.Value);
            Assert.False(vm.Negate);
        }

        [Fact]
        public void ConditionVM_PropertyChangedFires()
        {
            var vm = new ConditionVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            vm.Field = SearchField.Level;
            vm.Operator = SearchOperator.Equals;
            vm.Value = "ERROR";
            vm.Negate = true;

            Assert.Contains("Field", changed);
            Assert.Contains("Operator", changed);
            Assert.Contains("Value", changed);
            Assert.Contains("Negate", changed);
        }

        #endregion

        #region ConditionGroupVM

        [Fact]
        public void ConditionGroupVM_Defaults()
        {
            var vm = new ConditionGroupVM();
            Assert.Equal(ConditionOperator.And, vm.Operator);
            Assert.Empty(vm.Conditions);
        }

        [Fact]
        public void ConditionGroupVM_PropertyChangedFires()
        {
            var vm = new ConditionGroupVM();
            string? changed = null;
            vm.PropertyChanged += (s, e) => changed = e.PropertyName;

            vm.Operator = ConditionOperator.Or;
            Assert.Equal("Operator", changed);
        }

        #endregion

        #region ConditionRowViewModel

        [Fact]
        public void ConditionRowVM_Defaults()
        {
            var vm = new ConditionRowViewModel();
            Assert.Equal(SearchField.Any, vm.Field);
            Assert.Equal(SearchOperator.Contains, vm.Operator);
            Assert.Equal("", vm.Value);
            Assert.False(vm.Negate);
        }

        [Fact]
        public void ConditionRowVM_SetGet()
        {
            var vm = new ConditionRowViewModel
            {
                Field = SearchField.Exception,
                Operator = SearchOperator.Regex,
                Value = "NullRef.*",
                Negate = true
            };

            Assert.Equal(SearchField.Exception, vm.Field);
            Assert.Equal(SearchOperator.Regex, vm.Operator);
            Assert.Equal("NullRef.*", vm.Value);
            Assert.True(vm.Negate);
        }

        #endregion

        #region LocationCheckItem

        [Fact]
        public void LocationCheckItem_SetGet()
        {
            var item = new LocationCheckItem
            {
                Id = Guid.NewGuid(),
                DisplayText = "Sim1 (192.168.1.1)",
                IsChecked = true
            };

            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.Equal("Sim1 (192.168.1.1)", item.DisplayText);
            Assert.True(item.IsChecked);
        }

        #endregion

        // =====================================================================
        // ViewModelBase Tests
        // =====================================================================

        #region ViewModelBase

        private class TestVM : ViewModelBase
        {
            private string _name = "";
            public string Name
            {
                get => _name;
                set => SetField(ref _name, value);
            }
        }

        [Fact]
        public void ViewModelBase_SetField_ChangesValue()
        {
            var vm = new TestVM();
            vm.Name = "hello";
            Assert.Equal("hello", vm.Name);
        }

        [Fact]
        public void ViewModelBase_SetField_SameValue_ReturnsFalse()
        {
            var vm = new TestVM();
            vm.Name = "hello";
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.Name = "hello"; // same value
            Assert.Empty(changed);
        }

        [Fact]
        public void ViewModelBase_SetField_DifferentValue_RaisesPropertyChanged()
        {
            var vm = new TestVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.Name = "world";
            Assert.Contains("Name", changed);
        }

        [Fact]
        public void ViewModelBase_NotifyPropertyChanged()
        {
            var vm = new TestVM();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.NotifyPropertyChanged("SomeOtherProp");
            Assert.Contains("SomeOtherProp", changed);
        }

        [Fact]
        public void ViewModelBase_Dispose_TwiceIsSafe()
        {
            var vm = new TestVM();
            vm.Dispose();
            vm.Dispose(); // should not throw
        }

        #endregion

        // =====================================================================
        // LogEntry Property Tests (relevant to CaseManagement)
        // =====================================================================

        #region LogEntry

        [Fact]
        public void LogEntry_IsMarked_RaisesPropertyChanged()
        {
            var log = MakeLog("test");
            var changed = new List<string>();
            log.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            log.IsMarked = true;
            Assert.Contains("IsMarked", changed);
            Assert.Contains("RowBackground", changed);
        }

        [Fact]
        public void LogEntry_IsCurrentMarked_RaisesPropertyChanged()
        {
            var log = MakeLog("test");
            var changed = new List<string>();
            log.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            log.IsCurrentMarked = true;
            Assert.Contains("IsCurrentMarked", changed);
        }

        [Fact]
        public void LogEntry_HasAnnotation_RaisesPropertyChanged()
        {
            var log = MakeLog("test");
            var changed = new List<string>();
            log.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            log.HasAnnotation = true;
            Assert.Contains("HasAnnotation", changed);
            Assert.Contains("RowBackground", changed);
        }

        [Fact]
        public void LogEntry_AnnotationIcon_NoAnnotation_Empty()
        {
            var log = MakeLog("test");
            log.HasAnnotation = false;
            Assert.Equal("", log.AnnotationIcon);
        }

        [Fact]
        public void LogEntry_AnnotationIcon_Collapsed()
        {
            var log = MakeLog("test");
            log.HasAnnotation = true;
            log.IsAnnotationExpanded = false;
            Assert.NotEmpty(log.AnnotationIcon);
        }

        [Fact]
        public void LogEntry_AnnotationIcon_Expanded()
        {
            var log = MakeLog("test");
            log.HasAnnotation = true;
            log.IsAnnotationExpanded = true;
            Assert.NotEmpty(log.AnnotationIcon);
        }

        #endregion
    }
}
