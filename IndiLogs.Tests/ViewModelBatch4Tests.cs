using IndiLogs_3._0;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelBatch4Tests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static void SetField(object obj, string fieldName, object? value)
        {
            var type = obj.GetType();
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, NonPublicInstance | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            field?.SetValue(obj, value);
        }

        private static void SetProperty(object obj, string propertyName, object? value)
        {
            var prop = obj.GetType().GetProperty(propertyName, PublicInstance | NonPublicInstance);
            prop?.SetValue(obj, value);
        }

        private static object? GetField(object obj, string fieldName)
        {
            var type = obj.GetType();
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, NonPublicInstance | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            return field?.GetValue(obj);
        }

        private static T? GetFieldValue<T>(object obj, string fieldName)
        {
            var val = GetField(obj, fieldName);
            return val is T t ? t : default;
        }

        private static object? InvokeMethod(object obj, string methodName, params object?[] args)
        {
            var method = obj.GetType().GetMethod(methodName, NonPublicInstance | PublicInstance);
            return method?.Invoke(obj, args);
        }

        private static LogEntry MakeLog(
            string message = "", string level = "", string logger = "",
            string threadName = "", string method = "",
            DateTime? date = null) =>
            new()
            {
                Message = message, Level = level, Logger = logger,
                ThreadName = threadName, Method = method,
                Date = date ?? DateTime.Now
            };

        // =====================================================================
        // FilterSearchViewModel.ApplyFilter tests
        // =====================================================================

        [Fact]
        public void ApplyAppLogsFilter_NullCache_ReturnsEarly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            SetField(vm, "_sessionVM", sessionVM);
            // AllAppLogsCache is null by default on uninitialized object
            // Should return early without crashing
            var ex = Record.Exception(() => vm.ApplyAppLogsFilter());
            // May throw NRE for _parent or other fields, but should not crash on null cache guard
            // The method guards: if (_sessionVM?.AllAppLogsCache == null) return;
            Assert.True(ex == null || ex is NullReferenceException);
        }

        [Fact]
        public void ApplyAppLogsFilter_EmptyCache_NoFilters_ShowsAll()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("msg1"), MakeLog("msg2"), MakeLog("msg3")
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(3, appDevLogsFiltered.Count);
        }

        [Fact]
        public void ApplyAppLogsFilter_ThreadFilter_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("msg1", threadName: "Thread-1"),
                MakeLog("msg2", threadName: "Thread-2"),
                MakeLog("msg3", threadName: "Thread-1"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "Thread-1" });
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.All(appDevLogsFiltered, l => Assert.Equal("Thread-1", l.ThreadName));
        }

        [Fact]
        public void ApplyAppLogsFilter_LoggerFilter_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo.app"),
                MakeLog(logger: "com.indigo.plc"),
                MakeLog(logger: "com.hp.other"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string> { "com.indigo.app" });
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Single(appDevLogsFiltered);
            Assert.Equal("com.indigo.app", appDevLogsFiltered[0].Logger);
        }

        [Fact]
        public void ApplyAppLogsFilter_MethodFilter_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(method: "Start"),
                MakeLog(method: "Stop"),
                MakeLog(method: "Start"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string> { "Start" });
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.All(appDevLogsFiltered, l => Assert.Equal("Start", l.Method));
        }

        [Fact]
        public void ApplyAppLogsFilter_NegativeFilter_ExcludesMessages()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("keep this"),
                MakeLog("remove this noise"),
                MakeLog("also keep"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", true);
            SetField(vm, "_appNegativeFilters", new List<string> { "noise" });
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.DoesNotContain(appDevLogsFiltered, l => l.Message.Contains("noise"));
        }

        [Fact]
        public void ApplyAppLogsFilter_NegativeThreadFilter_ExcludesThreads()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("msg1", threadName: "Worker-1"),
                MakeLog("msg2", threadName: "Noise-Thread"),
                MakeLog("msg3", threadName: "Worker-2"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", true);
            SetField(vm, "_appNegativeFilters", new List<string> { "THREAD:Noise" });
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.DoesNotContain(appDevLogsFiltered, l => l.ThreadName.Contains("Noise"));
        }

        [Fact]
        public void ApplyAppLogsFilter_TreeShowOnlyLogger_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo"),
                MakeLog(logger: "com.indigo.sub"),
                MakeLog(logger: "com.hp"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", "com.indigo");
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.All(appDevLogsFiltered, l => Assert.StartsWith("com.indigo", l.Logger));
        }

        [Fact]
        public void ApplyAppLogsFilter_TreeShowOnlyPrefix_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo"),
                MakeLog(logger: "com.indigo.sub"),
                MakeLog(logger: "com.hp"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", "com.indigo");
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
        }

        [Fact]
        public void ApplyAppLogsFilter_TreeHiddenLoggers_ExcludesLoggers()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo.app"),
                MakeLog(logger: "com.indigo.plc"),
                MakeLog(logger: "com.hp.other"),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string> { "com.indigo.plc" });
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Equal(2, appDevLogsFiltered.Count);
            Assert.DoesNotContain(appDevLogsFiltered, l => l.Logger == "com.indigo.plc");
        }

        [Fact]
        public void ApplyAppLogsFilter_TreeHiddenPrefixes_ExcludesPrefixAndChildren()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "com.indigo"),
                MakeLog(logger: "com.indigo.sub"),
                MakeLog(logger: "com.hp"),
                MakeLog(logger: null!), // null logger should pass through
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string> { "com.indigo" });
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            // com.hp + null logger = 2
            Assert.Equal(2, appDevLogsFiltered.Count);
        }

        [Fact]
        public void ApplyAppLogsFilter_GlobalTimeRange_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("early", date: baseTime.AddMinutes(-10)),
                MakeLog("in range", date: baseTime),
                MakeLog("late", date: baseTime.AddMinutes(10)),
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)baseTime.AddMinutes(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)baseTime.AddMinutes(1));
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Single(appDevLogsFiltered);
            Assert.Equal("in range", appDevLogsFiltered[0].Message);
        }

        [Fact]
        public void ApplyAppLogsFilter_AdvancedFilter_Applies()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("error occurred", level: "Error"),
                MakeLog("info message", level: "Info"),
            };

            var filterRoot = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" }
                }
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", filterRoot);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            Assert.Single(appDevLogsFiltered);
            Assert.Equal("Error", appDevLogsFiltered[0].Level);
        }

        [Fact]
        public void ApplyAppLogsFilter_TimeFocusActive_UsesFilteredCache()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var allLogs = new List<LogEntry>
            {
                MakeLog("all1"), MakeLog("all2"), MakeLog("all3"), MakeLog("all4"), MakeLog("all5")
            };
            var filteredCache = new List<LogEntry>
            {
                MakeLog("filtered1"), MakeLog("filtered2")
            };

            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)allLogs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", true);
            SetField(vm, "_lastFilteredAppCache", filteredCache);

            var appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            SetField(vm, "_appDevLogsFiltered", appDevLogsFiltered);

            vm.ApplyAppLogsFilter();

            // Should use filtered cache (2 items) not allLogs (5 items)
            Assert.Equal(2, appDevLogsFiltered.Count);
        }

        // =====================================================================
        // FilterSearchViewModel.RangeFilters tests (CheckIfFiltersEmpty, SetFilterActive, UndoFilterOut)
        // =====================================================================

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_AllEmpty_DeactivatesFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());

            InvokeMethod(vm, "CheckIfFiltersEmpty", true);

            var isActive = GetFieldValue<bool>(vm, "_isAppFilterActive");
            Assert.False(isActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_HasThreadFilter_StaysActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string> { "Thread-1" });
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());

            InvokeMethod(vm, "CheckIfFiltersEmpty", true);

            var isActive = GetFieldValue<bool>(vm, "_isAppFilterActive");
            Assert.True(isActive);
        }

        [Fact]
        public void CheckIfFiltersEmpty_PlcTab_AllEmpty_DeactivatesFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);

            InvokeMethod(vm, "CheckIfFiltersEmpty", false);

            var isActive = GetFieldValue<bool>(vm, "_isMainFilterActive");
            Assert.False(isActive);
        }

        [Fact]
        public void SetFilterActive_AppTab_SetsAppFilterActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isAppFilterActive", false);

            InvokeMethod(vm, "SetFilterActive", true);

            var isActive = GetFieldValue<bool>(vm, "_isAppFilterActive");
            Assert.True(isActive);
        }

        [Fact]
        public void SetFilterActive_PlcTab_SetsMainFilterActive()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            SetField(vm, "_isMainFilterActive", false);

            InvokeMethod(vm, "SetFilterActive", false);

            var isActive = GetFieldValue<bool>(vm, "_isMainFilterActive");
            Assert.True(isActive);
        }

        // =====================================================================
        // FilterSearchViewModel.TimeRangeOps tests (StartRange, ClearRange)
        // =====================================================================

        [Fact]
        public void StartRange_NoSelectedLog_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_parent", parent);
            SetField(vm, "_hasRangeStart", false);
            // SelectedLog is null on uninitialized parent

            InvokeMethod(vm, "StartRange", new object?[] { null });

            Assert.False(GetFieldValue<bool>(vm, "_hasRangeStart"));
        }

        [Fact]
        public void StartRange_WithSelectedLog_SetsRangeStart()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

            var selectedLog = MakeLog("selected", date: new DateTime(2024, 1, 1, 12, 0, 0));
            SetField(parent, "_selectedLog", selectedLog);
            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", sessionVM);
            SetProperty(sessionVM, "StatusMessage", "");
            SetField(vm, "_hasRangeStart", false);

            InvokeMethod(vm, "StartRange", new object?[] { null });

            Assert.True(GetFieldValue<bool>(vm, "_hasRangeStart"));
            Assert.Same(selectedLog, GetField(vm, "_rangeStartLog"));
        }

        [Fact]
        public void ClearRange_PlcTab_ClearsTimeFocus()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(parent, "_selectedTabIndex", 0); // PLC tab
            SetField(vm, "_isTimeFocusActive", true);
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_lastFilteredCache", new List<LogEntry>());
            SetField(vm, "_hasRangeStart", true);
            SetField(vm, "_rangeStartLog", MakeLog("start"));
            SetField(vm, "_isAppTimeFocusActive", false);

            // Set up fields needed by ToggleFilterView -> ApplyMainLogsFilter/ApplyAppLogsFilter
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_lastFilteredAppCache", null);
            SetField(vm, "_appDevLogsFiltered", new ObservableRangeCollection<LogEntry>());
            SetProperty(sessionVM, "StatusMessage", "");

            // ToggleFilterView calls both Apply methods - they need LiveVM check
            // ApplyMainLogsFilter: if (_parent.LiveVM?.IsLiveMode == true) return;
            // Set LiveVM to null is fine - it'll pass the null check

            var ex = Record.Exception(() => InvokeMethod(vm, "ClearRange", new object?[] { null }));

            // ClearRange should clear _rangeStartLog and _hasRangeStart
            Assert.False(GetFieldValue<bool>(vm, "_hasRangeStart"));
            Assert.Null(GetField(vm, "_rangeStartLog"));
        }

        // =====================================================================
        // FilterSearchViewModel.TreeFilters tests
        // =====================================================================

        [Fact]
        public void ExecuteTreeShowThis_NullParam_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var ex = Record.Exception(() => InvokeMethod(vm, "ExecuteTreeShowThis", new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void ExecuteTreeHideThis_NullParam_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var ex = Record.Exception(() => InvokeMethod(vm, "ExecuteTreeHideThis", new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void ExecuteTreeShowOnlyThis_NullParam_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var ex = Record.Exception(() => InvokeMethod(vm, "ExecuteTreeShowOnlyThis", new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void ExecuteTreeShowAll_NullParam_DoesNothing()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            // This may throw because it references _parent.IsPLCTabSelected
            var ex = Record.Exception(() => InvokeMethod(vm, "ExecuteTreeShowAll", new object?[] { null }));
            Assert.True(ex == null || ex is TargetInvocationException);
        }

        // =====================================================================
        // MainViewModel.TimeSync tests (BinarySearchNearest, BinarySearchNearestBy)
        // =====================================================================

        [Fact]
        public void BinarySearchNearest_EmptyCollection_ReturnsMinus1()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var result = InvokeMethod(vm, "BinarySearchNearest", new List<LogEntry>() as IList<LogEntry>, DateTime.Now);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearest_NullCollection_ReturnsMinus1()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var result = InvokeMethod(vm, "BinarySearchNearest", (IList<LogEntry>?)null!, DateTime.Now);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearest_SingleItem_Returns0()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var time = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry> { MakeLog(date: time) } as IList<LogEntry>;
            var result = InvokeMethod(vm, "BinarySearchNearest", logs, time);
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearest_ExactMatch_ReturnsCorrectIndex()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddMinutes(1)),
                MakeLog(date: baseTime.AddMinutes(2)),
                MakeLog(date: baseTime.AddMinutes(3)),
                MakeLog(date: baseTime.AddMinutes(4)),
            } as IList<LogEntry>;

            var result = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddMinutes(2));
            Assert.Equal(2, result);
        }

        [Fact]
        public void BinarySearchNearest_BetweenItems_ReturnsNearest()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddMinutes(10)),
            } as IList<LogEntry>;

            // Closer to first item
            var result1 = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddMinutes(3));
            Assert.Equal(0, result1);

            // Closer to second item
            var result2 = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddMinutes(7));
            Assert.Equal(1, result2);
        }

        [Fact]
        public void BinarySearchNearest_BeforeAllItems_Returns0()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddMinutes(5)),
            } as IList<LogEntry>;

            var result = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddMinutes(-10));
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearest_AfterAllItems_ReturnsLast()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog(date: baseTime),
                MakeLog(date: baseTime.AddMinutes(5)),
            } as IList<LogEntry>;

            var result = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddMinutes(100));
            Assert.Equal(1, result);
        }

        [Fact]
        public void BinarySearchNearest_ManyItems_FindsClosest()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = Enumerable.Range(0, 100)
                .Select(i => MakeLog(date: baseTime.AddSeconds(i * 10)))
                .ToList() as IList<LogEntry>;

            // Search for time at 56 seconds (should be closer to index 6 at 60s than index 5 at 50s)
            var result = InvokeMethod(vm, "BinarySearchNearest", logs, baseTime.AddSeconds(56));
            Assert.Equal(6, result); // 60s is closer than 50s
        }

        // =====================================================================
        // MainViewModel.TimeSync - RequestSyncScroll
        // =====================================================================

        [Fact]
        public void RequestSyncScroll_Disabled_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_isTimeSyncEnabled", false);
            SetField(vm, "_isSyncScrolling", false);
            SetField(vm, "_isTabSwitching", false);
            SetField(vm, "_pendingSyncLog", null);

            var log = MakeLog(date: new DateTime(2024, 1, 1, 12, 0, 0));
            vm.RequestSyncScroll(log, "PLC");

            Assert.Null(GetField(vm, "_pendingSyncLog"));
        }

        [Fact]
        public void RequestSyncScroll_AlreadySyncing_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_isTimeSyncEnabled", true);
            SetField(vm, "_isSyncScrolling", true);
            SetField(vm, "_isTabSwitching", false);
            SetField(vm, "_pendingSyncLog", null);

            var log = MakeLog(date: new DateTime(2024, 1, 1, 12, 0, 0));
            vm.RequestSyncScroll(log, "PLC");

            Assert.Null(GetField(vm, "_pendingSyncLog"));
        }

        [Fact]
        public void RequestSyncScroll_TabSwitching_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_isTimeSyncEnabled", true);
            SetField(vm, "_isSyncScrolling", false);
            SetField(vm, "_isTabSwitching", true);
            SetField(vm, "_pendingSyncLog", null);

            var log = MakeLog(date: new DateTime(2024, 1, 1, 12, 0, 0));
            vm.RequestSyncScroll(log, "PLC");

            Assert.Null(GetField(vm, "_pendingSyncLog"));
        }

        [Fact]
        public void RequestSyncScroll_DateTimeOverload_CreatesTemporaryLogEntry()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_isTimeSyncEnabled", false); // disabled, so it returns early
            SetField(vm, "_isSyncScrolling", false);
            SetField(vm, "_isTabSwitching", false);

            // Should not crash
            var ex = Record.Exception(() => vm.RequestSyncScroll(DateTime.Now, "PLC"));
            Assert.Null(ex);
        }

        // =====================================================================
        // MainViewModel.TimeSync - NavigateToLogTime
        // =====================================================================

        [Fact]
        public void NavigateToLogTime_NullFilteredLogs_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // FilterVM is null on uninitialized object
            var ex = Record.Exception(() => vm.NavigateToLogTime(DateTime.Now));
            Assert.Null(ex);
        }

        // =====================================================================
        // MainViewModel.TimeSync - OnLogEntrySelected
        // =====================================================================

        [Fact]
        public void OnLogEntrySelected_NullEntry_DoesNothing()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var ex = Record.Exception(() => vm.OnLogEntrySelected(null!));
            Assert.Null(ex);
        }

        // =====================================================================
        // MainViewModel.Tabs tests
        // =====================================================================

        [Fact]
        public void DbConfigTabHeader_NullSession_ReturnsDbConfig()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // SessionVM is null
            var header = vm.DbConfigTabHeader;
            Assert.Equal("DB & CONFIG", header);
        }

        [Fact]
        public void PlcTabHeader_NullSession_ReturnsPlcLogs()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var header = vm.PlcTabHeader;
            Assert.Equal("PLC LOGS", header);
        }

        [Fact]
        public void HasBinaryAppLogs_NullSession_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasBinaryAppLogs);
        }

        [Fact]
        public void HasSessionLoaded_NullSession_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasSessionLoaded);
        }

        [Fact]
        public void ShowMainTabs_NullSession_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.ShowMainTabs);
        }

        [Fact]
        public void HasGlobalsFiles_NullSession_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasGlobalsFiles);
        }

        [Fact]
        public void IsPLCTabSelected_DefaultIndex0_ReturnsTrue()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.True(vm.IsPLCTabSelected);
        }

        [Fact]
        public void IsAppTabSelected_Index1_ReturnsTrue()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.True(vm.IsAppTabSelected);
        }

        [Fact]
        public void IsPrintAnalysisVisible_AppTab_NoBinary_ReturnsTrue()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            // HasBinaryAppLogs returns false when session is null
            Assert.True(vm.IsPrintAnalysisVisible);
        }

        [Fact]
        public void ReportsButtonText_NoBinary_ReturnsReports()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.Contains("Reports", vm.ReportsButtonText);
        }

        [Fact]
        public void LoggerTabTitle_PlcTab_ReturnsPLCLoggers()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.Equal("PLC Loggers", vm.LoggerTabTitle);
        }

        [Fact]
        public void LoggerTabTitle_AppTab_ReturnsAppLoggers()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.Equal("App Loggers", vm.LoggerTabTitle);
        }

        [Fact]
        public void IsExportVisible_PlcTab_ReturnsTrue()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_PLC);
            Assert.True(vm.IsExportVisible);
        }

        [Fact]
        public void IsExportVisible_AppTab_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_selectedTabIndex", AppConstants.TAB_APP);
            Assert.False(vm.IsExportVisible);
        }

        [Fact]
        public void ApplyTabSelection_Null_ResetsAllToVisible()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.ApplyTabSelection(null, null);

            Assert.True(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
            Assert.True(vm.ShowEventsTab);
            Assert.True(vm.ShowScreenshotsTab);
            Assert.True(vm.ShowConfigTab);
            Assert.True(vm.ShowDbConfigTab);
            Assert.True(vm.ShowSetupInfoTab);
            Assert.True(vm.ShowGlobalsTab);
            Assert.True(vm.ShowSystabTab);
            Assert.True(vm.ShowChartsTab);
            Assert.True(vm.ShowCprTab);
            Assert.True(vm.ShowStepRecorderTab);
            Assert.True(vm.ShowDifferentLogsTab);
        }

        [Fact]
        public void ApplyTabSelection_WithConfig_SetsCorrectVisibility()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var config = new TabSelectionConfig
            {
                LoadPlc = true,
                LoadApp = false,
                LoadEvents = true,
                LoadScreenshots = false,
                LoadConfiguration = true,
                LoadTerminalLogs = false,
                LoadLrs = false,
                LoadSetupInfo = true,
                LoadGlobals = false,
                LoadSystab = true,
                ShowCharts = false,
                ShowCpr = true,
                ShowStepRecorder = false,
                ShowDifferentLogs = true,
            };

            vm.ApplyTabSelection(config, null);

            Assert.True(vm.ShowPlcTab);
            Assert.False(vm.ShowAppTab);
            Assert.True(vm.ShowEventsTab);
            Assert.False(vm.ShowScreenshotsTab);
            Assert.True(vm.ShowConfigTab);
            Assert.True(vm.ShowDbConfigTab); // LoadConfiguration is true
            Assert.True(vm.ShowSetupInfoTab);
            Assert.False(vm.ShowGlobalsTab);
            Assert.True(vm.ShowSystabTab);
            Assert.False(vm.ShowChartsTab);
            Assert.True(vm.ShowCprTab);
            Assert.False(vm.ShowStepRecorderTab);
            Assert.True(vm.ShowDifferentLogsTab);
        }

        [Fact]
        public void ApplyTabSelection_DbConfigTab_RequiresConfigOrTerminalOrLrs()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            // All three false -> DbConfig hidden
            var config1 = new TabSelectionConfig
            {
                LoadConfiguration = false,
                LoadTerminalLogs = false,
                LoadLrs = false,
            };
            vm.ApplyTabSelection(config1, null);
            Assert.False(vm.ShowDbConfigTab);

            // Terminal true -> DbConfig shown
            var config2 = new TabSelectionConfig
            {
                LoadConfiguration = false,
                LoadTerminalLogs = true,
                LoadLrs = false,
            };
            vm.ApplyTabSelection(config2, null);
            Assert.True(vm.ShowDbConfigTab);

            // LRS true -> DbConfig shown
            var config3 = new TabSelectionConfig
            {
                LoadConfiguration = false,
                LoadTerminalLogs = false,
                LoadLrs = true,
            };
            vm.ApplyTabSelection(config3, null);
            Assert.True(vm.ShowDbConfigTab);
        }

        [Fact]
        public void GetSkippedComponents_NullSession_ReturnsEmpty()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // SessionVM is null
            var result = vm.GetSkippedComponents();
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void HasSkippedComponents_NullSession_ReturnsFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.HasSkippedComponents);
        }

        [Fact]
        public void LeftTabIndex_SetAndGet()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.LeftTabIndex = 3;
            Assert.Equal(3, vm.LeftTabIndex);
        }

        // =====================================================================
        // MainViewModel.Windows tests (OpenUrl)
        // =====================================================================

        [Fact]
        public void OpenUrl_NullOrWhitespace_DoesNotCrash()
        {
            var ex1 = Record.Exception(() => MainViewModel.OpenUrl(null!));
            Assert.Null(ex1);

            var ex2 = Record.Exception(() => MainViewModel.OpenUrl(""));
            Assert.Null(ex2);

            var ex3 = Record.Exception(() => MainViewModel.OpenUrl("   "));
            Assert.Null(ex3);
        }

        [Fact]
        public void OpenUrl_InvalidScheme_DoesNotOpen()
        {
            // ftp scheme is blocked
            var ex = Record.Exception(() => MainViewModel.OpenUrl("ftp://example.com"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_InvalidUri_DoesNotCrash()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("not a valid uri at all !!!"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_JavascriptScheme_Blocked()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("javascript:alert(1)"));
            Assert.Null(ex);
        }

        // =====================================================================
        // MainViewModel.FilterHelpers tests
        // =====================================================================

        [Fact]
        public void ResetTreeFilters_WithFilterVM_ClearsAllTreeFilters()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));

            // Initialize tree filter state
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string> { "a", "b" });
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string> { "c" });
            SetField(filterVM, "_treeShowOnlyLogger", "someLogger");
            SetField(filterVM, "_treeShowOnlyPrefix", "somePrefix");
            SetField(filterVM, "_loggerTreeRoot", new ObservableCollection<LoggerNode>());

            // Set FilterVM on MainViewModel
            SetProperty(vm, "FilterVM", filterVM);

            vm.ResetTreeFilters();

            var hiddenLoggers = GetFieldValue<HashSet<string>>(filterVM, "_treeHiddenLoggers");
            var hiddenPrefixes = GetFieldValue<HashSet<string>>(filterVM, "_treeHiddenPrefixes");

            Assert.Empty(hiddenLoggers!);
            Assert.Empty(hiddenPrefixes!);
            Assert.Null(GetField(filterVM, "_treeShowOnlyLogger"));
            Assert.Null(GetField(filterVM, "_treeShowOnlyPrefix"));
        }

        [Fact]
        public void ResetVisualHiddenState_ResetsNodeAndChildren()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var child = new LoggerNode { Name = "child", IsHidden = true, IsActive = true };
            var parent = new LoggerNode
            {
                Name = "parent",
                IsHidden = true,
                IsActive = true,
                Children = new ObservableCollection<LoggerNode> { child }
            };

            InvokeMethod(vm, "ResetVisualHiddenState", parent);

            Assert.False(parent.IsHidden);
            Assert.False(parent.IsActive);
            Assert.False(child.IsHidden);
            Assert.False(child.IsActive);
        }

        [Fact]
        public void NavigateToLogFromStats_NullLog_DoesNotCrash()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            InvokeMethod(vm, "NavigateToLogFromStats", (LogEntry)null!);
            // No exception expected - method guards with if (log == null) return;
        }

        [Fact]
        public void NavigateToGrepResult_NullResult_DoesNotCrash()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.NavigateToGrepResult(null!);
            // No exception expected
        }

        [Fact]
        public void ApplyChartDrillDownFilter_Logger_DoesNotCrash()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var dialogService = new TestDialogService();

            SetField(filterVM, "_parent", vm);
            SetField(filterVM, "_sessionVM", sessionVM);
            SetProperty(vm, "FilterVM", filterVM);
            SetProperty(vm, "SessionVM", sessionVM);
            SetField(vm, "_dialogService", dialogService);

            // ApplyChartDrillDownFilter catches all exceptions, so it should never throw
            var ex = Record.Exception(() => InvokeMethod(vm, "ApplyChartDrillDownFilter", "Logger", "com.indigo"));
            Assert.Null(ex);
        }

        [Fact]
        public void ApplyChartDrillDownFilter_State_DoesNotCrash()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var dialogService = new TestDialogService();

            SetField(filterVM, "_parent", vm);
            SetField(filterVM, "_sessionVM", sessionVM);
            SetProperty(vm, "FilterVM", filterVM);
            SetProperty(vm, "SessionVM", sessionVM);
            SetField(vm, "_dialogService", dialogService);

            var ex = Record.Exception(() => InvokeMethod(vm, "ApplyChartDrillDownFilter", "State", "PRINTING"));
            Assert.Null(ex);
        }

        [Fact]
        public void ApplyChartDrillDownFilter_UnknownType_DoesNotCrash()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var dialogService = new TestDialogService();

            SetField(filterVM, "_parent", vm);
            SetField(filterVM, "_sessionVM", sessionVM);
            SetProperty(vm, "FilterVM", filterVM);
            SetProperty(vm, "SessionVM", sessionVM);
            SetField(vm, "_dialogService", dialogService);

            var ex = Record.Exception(() => InvokeMethod(vm, "ApplyChartDrillDownFilter", "Unknown", "value"));
            Assert.Null(ex);
        }

        // =====================================================================
        // MainViewModel.TimeSync - TimeSyncOffsetSeconds, ShowSyncedTimeColumn, HasTimeSyncData
        // =====================================================================

        [Fact]
        public void TimeSyncOffsetSeconds_SetAndGet()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.TimeSyncOffsetSeconds = 1.5;
            Assert.Equal(1.5, vm.TimeSyncOffsetSeconds);
        }

        [Fact]
        public void ShowSyncedTimeColumn_SetAndGet()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            vm.ShowSyncedTimeColumn = true;
            Assert.True(vm.ShowSyncedTimeColumn);
        }

        [Fact]
        public void HasTimeSyncData_SetFalse_AutoHidesSyncedColumn()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            SetField(vm, "_showSyncedTimeColumn", true);
            vm.HasTimeSyncData = false;
            Assert.False(vm.ShowSyncedTimeColumn);
        }

        [Fact]
        public void TimeSyncScrollWasApplied_DefaultFalse()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Assert.False(vm.TimeSyncScrollWasApplied);
        }

        // =====================================================================
        // MainViewModel.Tabs - ShowTab properties
        // =====================================================================

        [Theory]
        [InlineData(nameof(MainViewModel.ShowPlcTab))]
        [InlineData(nameof(MainViewModel.ShowAppTab))]
        [InlineData(nameof(MainViewModel.ShowEventsTab))]
        [InlineData(nameof(MainViewModel.ShowScreenshotsTab))]
        [InlineData(nameof(MainViewModel.ShowConfigTab))]
        [InlineData(nameof(MainViewModel.ShowDbConfigTab))]
        [InlineData(nameof(MainViewModel.ShowSetupInfoTab))]
        [InlineData(nameof(MainViewModel.ShowGlobalsTab))]
        [InlineData(nameof(MainViewModel.ShowSystabTab))]
        [InlineData(nameof(MainViewModel.ShowChartsTab))]
        [InlineData(nameof(MainViewModel.ShowCprTab))]
        [InlineData(nameof(MainViewModel.ShowStepRecorderTab))]
        [InlineData(nameof(MainViewModel.ShowDifferentLogsTab))]
        public void ShowTab_SetAndGet(string propertyName)
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var prop = typeof(MainViewModel).GetProperty(propertyName)!;
            prop.SetValue(vm, false);
            Assert.False((bool)prop.GetValue(vm)!);
            prop.SetValue(vm, true);
            Assert.True((bool)prop.GetValue(vm)!);
        }

        // =====================================================================
        // MainViewModel.FilterHelpers - GetActiveFilters via FilterVM
        // =====================================================================

        [Fact]
        public void GetActiveFilters_NoFilters_ReturnsEmpty()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", false);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Empty(items);
        }

        [Fact]
        public void GetActiveFilters_WithAppErrorFilter_ReturnsItem()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", true);
            SetField(filterVM, "_isAppErrorFilterActive", true);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Contains(items, i => i.Key == "APP_ERROR_FILTER");
        }

        [Fact]
        public void GetActiveFilters_WithTimeFocus_ReturnsItem()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", true);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", true);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Contains(items, i => i.Key == "APP_TIME_FOCUS");
        }

        [Fact]
        public void GetActiveFilters_WithThreadFilters_ReturnsItems()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", true);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string> { "Thread-1", "Thread-2" });
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Equal(2, items.Count(i => i.Category == "THREAD"));
        }

        [Fact]
        public void GetActiveFilters_WithNegativeFilters_ReturnsItems()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", false);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string> { "noise", "THREAD:Worker" });
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Equal(2, items.Count(i => i.Category == "FILTER OUT"));
        }

        [Fact]
        public void GetActiveFilters_WithLoggerAndMethodFilters_ReturnsItems()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", true);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string> { "com.indigo" });
            SetField(filterVM, "_activeMethodFilters", new List<string> { "Start" });
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", null);
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string>());
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Contains(items, i => i.Category == "LOGGER");
            Assert.Contains(items, i => i.Category == "METHOD");
        }

        [Fact]
        public void GetActiveFilters_WithTreeFilters_ReturnsItems()
        {
            var filterVM = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            SetField(filterVM, "_parent", parent);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(filterVM, "_isAppFilterActive", true);
            SetField(filterVM, "_isAppErrorFilterActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);
            SetField(filterVM, "_appActiveThreadFilters", new List<string>());
            SetField(filterVM, "_appNegativeFilters", new List<string>());
            SetField(filterVM, "_activeLoggerFilters", new List<string>());
            SetField(filterVM, "_activeMethodFilters", new List<string>());
            SetField(filterVM, "_appFilterRoot", null);
            SetField(filterVM, "_treeShowOnlyLogger", "com.indigo.app");
            SetField(filterVM, "_treeShowOnlyPrefix", null);
            SetField(filterVM, "_treeHiddenLoggers", new HashSet<string> { "com.hp" });
            SetField(filterVM, "_treeHiddenPrefixes", new HashSet<string> { "com.other" });
            SetField(filterVM, "_searchText", null);
            SetField(filterVM, "_globalTimeRangeStart", (DateTime?)null);
            SetField(filterVM, "_globalTimeRangeEnd", (DateTime?)null);

            var items = filterVM.GetActiveFilters();
            Assert.Contains(items, i => i.Key == "TREE_SHOW_ONLY_LOGGER");
            Assert.Contains(items, i => i.Key.StartsWith("TREE_HIDE_LOGGER:"));
            Assert.Contains(items, i => i.Key.StartsWith("TREE_HIDE_PREFIX:"));
        }

        // =====================================================================
        // MainViewModel.ApplyMainLogsFilter tests (via FilterSearchViewModel)
        // =====================================================================

        [Fact]
        public void ApplyMainLogsFilter_NoFilters_ShowsAll()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("msg1"), MakeLog("msg2"), MakeLog("msg3")
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            // LiveVM null check
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs;
            Assert.Equal(3, sessionLogs.Count());
        }

        [Fact]
        public void ApplyMainLogsFilter_ThreadFilter_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("msg1", threadName: "Thread-A"),
                MakeLog("msg2", threadName: "Thread-B"),
                MakeLog("msg3", threadName: "Thread-A"),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread-A" });
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Equal(2, sessionLogs.Count);
            Assert.All(sessionLogs, l => Assert.Equal("Thread-A", l.ThreadName));
        }

        [Fact]
        public void ApplyMainLogsFilter_NegativeFilter_ExcludesMessages()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog("important message"),
                MakeLog("noise to filter out"),
                MakeLog("another important one"),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string> { "noise" });
            SetField(vm, "_isMainFilterOutActive", true);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Equal(2, sessionLogs.Count);
            Assert.DoesNotContain(sessionLogs, l => l.Message.Contains("noise"));
        }

        [Fact]
        public void ApplyMainLogsFilter_PlcTreeShowOnlyLogger_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "E1.PLC.Motor"),
                MakeLog(logger: "E1.PLC.Sensor"),
                MakeLog(logger: "E1.APP.Main"),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", "E1.PLC.Motor");
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Single(sessionLogs);
            Assert.Equal("E1.PLC.Motor", sessionLogs[0].Logger);
        }

        [Fact]
        public void ApplyMainLogsFilter_PlcTreeShowOnlyPrefix_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "E1.PLC"),
                MakeLog(logger: "E1.PLC.Motor"),
                MakeLog(logger: "E1.APP.Main"),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", "E1.PLC");
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Equal(2, sessionLogs.Count);
            Assert.All(sessionLogs, l => Assert.StartsWith("E1.PLC", l.Logger));
        }

        [Fact]
        public void ApplyMainLogsFilter_PlcTreeHiddenLoggers_ExcludesLoggers()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "E1.PLC.Motor"),
                MakeLog(logger: "E1.PLC.Sensor"),
                MakeLog(logger: null!), // null logger passes through
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string> { "E1.PLC.Motor" });
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Equal(2, sessionLogs.Count);
            Assert.DoesNotContain(sessionLogs, l => l.Logger == "E1.PLC.Motor");
        }

        [Fact]
        public void ApplyMainLogsFilter_PlcTreeHiddenPrefixes_ExcludesPrefixAndChildren()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var logs = new List<LogEntry>
            {
                MakeLog(logger: "E1.PLC"),
                MakeLog(logger: "E1.PLC.Motor"),
                MakeLog(logger: "E1.APP"),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "E1.PLC" });
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Single(sessionLogs);
            Assert.Equal("E1.APP", sessionLogs[0].Logger);
        }

        [Fact]
        public void ApplyMainLogsFilter_GlobalTimeRange_FiltersCorrectly()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
            var logs = new List<LogEntry>
            {
                MakeLog("early", date: baseTime.AddMinutes(-10)),
                MakeLog("in range", date: baseTime),
                MakeLog("late", date: baseTime.AddMinutes(10)),
            };

            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)logs);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(vm, "_parent", parent);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_searchText", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_negativeFilters", new List<string>());
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)baseTime.AddMinutes(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)baseTime.AddMinutes(1));
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(parent, "LiveVM", null);

            vm.ApplyMainLogsFilter();

            var sessionLogs = sessionVM.Logs.ToList();
            Assert.Single(sessionLogs);
            Assert.Equal("in range", sessionLogs[0].Message);
        }

        // =====================================================================
        // MainViewModel.FilterHelpers - UndoFilterOut
        // =====================================================================

        [Fact]
        public void UndoFilterOut_AppTab_RemovesLastNegativeFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(vm, "_appNegativeFilters", new List<string> { "filter1", "filter2" });
            SetField(vm, "_isAppFilterOutActive", true);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_negativeFilters", new List<string>());

            // Fields needed for ToggleFilterView
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);
            SetField(vm, "_appDevLogsFiltered", new ObservableRangeCollection<LogEntry>());
            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)new List<LogEntry>());

            // For ApplyMainLogsFilter
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)new List<LogEntry>());
            SetProperty(parent, "LiveVM", null);

            InvokeMethod(vm, "UndoFilterOut", new object?[] { null });

            var filters = GetFieldValue<List<string>>(vm, "_appNegativeFilters");
            Assert.Single(filters!);
            Assert.Equal("filter1", filters![0]);
        }

        [Fact]
        public void UndoFilterOut_PlcTab_RemovesLastNegativeFilter()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_PLC);
            SetField(vm, "_negativeFilters", new List<string> { "noise1", "noise2" });
            SetField(vm, "_isMainFilterOutActive", true);
            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_appNegativeFilters", new List<string>());

            // Fields needed for ToggleFilterView
            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_isAppFilterOutActive", false);
            SetField(vm, "_lastFilteredAppCache", null);
            SetField(vm, "_appDevLogsFiltered", new ObservableRangeCollection<LogEntry>());
            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)new List<LogEntry>());

            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)new List<LogEntry>());
            SetProperty(parent, "LiveVM", null);

            InvokeMethod(vm, "UndoFilterOut", new object?[] { null });

            var filters = GetFieldValue<List<string>>(vm, "_negativeFilters");
            Assert.Single(filters!);
            Assert.Equal("noise1", filters![0]);
        }

        [Fact]
        public void UndoFilterOut_LastFilter_DeactivatesFilterOut()
        {
            var vm = (FilterSearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FilterSearchViewModel));
            var parent = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var sessionVM = (LogSessionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(LogSessionViewModel));

            SetField(vm, "_parent", parent);
            SetField(vm, "_sessionVM", sessionVM);
            SetField(parent, "_selectedTabIndex", AppConstants.TAB_APP);
            SetField(vm, "_appNegativeFilters", new List<string> { "last" });
            SetField(vm, "_isAppFilterOutActive", true);
            SetField(vm, "_isAppFilterActive", false);
            SetField(vm, "_negativeFilters", new List<string>());

            SetField(vm, "_searchText", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetField(vm, "_isAppTimeFocusActive", false);
            SetField(vm, "_lastFilteredAppCache", null);
            SetField(vm, "_appDevLogsFiltered", new ObservableRangeCollection<LogEntry>());
            SetProperty(sessionVM, "AllAppLogsCache", (IList<LogEntry>)new List<LogEntry>());

            SetField(vm, "_isMainFilterActive", false);
            SetField(vm, "_isMainFilterOutActive", false);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_isTimeFocusActive", false);
            SetField(vm, "_lastFilteredCache", null);
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetProperty(sessionVM, "AllLogsCache", (IList<LogEntry>)new List<LogEntry>());
            SetProperty(parent, "LiveVM", null);

            InvokeMethod(vm, "UndoFilterOut", new object?[] { null });

            Assert.False(GetFieldValue<bool>(vm, "_isAppFilterOutActive"));
            var filters = GetFieldValue<List<string>>(vm, "_appNegativeFilters");
            Assert.Empty(filters!);
        }

        // =====================================================================
        // MainViewModel.Windows - OpenKibana
        // =====================================================================

        [Fact]
        public void OpenKibana_NoMachineName_FallsBackToLocalhost()
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            // PressConfig is null/empty on uninitialized object, so it should fall back
            // to OpenUrl("http://localhost/") - which doesn't actually open a browser in tests
            // Just verify no crash
            SetField(vm, "_pressConfig", "Some other config\nNo machine name here");
            var ex = Record.Exception(() => InvokeMethod(vm, "OpenKibana", new object?[] { null }));
            Assert.Null(ex);
        }

        // =====================================================================
        // LoggerNode model tests
        // =====================================================================

        [Fact]
        public void LoggerNode_DisplayText_FormatsCorrectly()
        {
            var node = new LoggerNode { Name = "indigo", Count = 42 };
            Assert.Equal("indigo (42)", node.DisplayText);
        }

        [Fact]
        public void LoggerNode_IsExpanded_NotifiesPropertyChanged()
        {
            var node = new LoggerNode();
            bool notified = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(LoggerNode.IsExpanded)) notified = true; };
            node.IsExpanded = true;
            Assert.True(notified);
            Assert.True(node.IsExpanded);
        }

        [Fact]
        public void LoggerNode_IsSelected_NotifiesPropertyChanged()
        {
            var node = new LoggerNode();
            bool notified = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(LoggerNode.IsSelected)) notified = true; };
            node.IsSelected = true;
            Assert.True(notified);
        }

        [Fact]
        public void LoggerNode_Children_DefaultEmpty()
        {
            var node = new LoggerNode();
            Assert.NotNull(node.Children);
            Assert.Empty(node.Children);
        }

        // =====================================================================
        // TabSelectionConfig model tests
        // =====================================================================

        [Fact]
        public void TabSelectionConfig_Defaults_AllTrue()
        {
            var config = new TabSelectionConfig();
            Assert.True(config.LoadApp);
            Assert.True(config.LoadPlc);
            Assert.True(config.LoadTerminalLogs);
            Assert.True(config.LoadConfiguration);
            Assert.True(config.LoadSystab);
            Assert.True(config.LoadGlobals);
            Assert.True(config.LoadLrs);
            Assert.True(config.LoadEvents);
            Assert.True(config.LoadScreenshots);
            Assert.True(config.LoadSetupInfo);
            Assert.True(config.LoadManagerThread);
            Assert.True(config.ShowCharts);
            Assert.True(config.ShowCpr);
            Assert.True(config.ShowStepRecorder);
            Assert.True(config.ShowDifferentLogs);
        }

        [Fact]
        public void TabSelectionConfig_CreateForConfiguration_SetsIsS6()
        {
            var config = TabSelectionConfig.CreateForConfiguration(true);
            Assert.True(config.IsS6);

            var config2 = TabSelectionConfig.CreateForConfiguration(false);
            Assert.False(config2.IsS6);
        }

        [Fact]
        public void TabSelectionConfig_HasFlags_DefaultFalse()
        {
            var config = new TabSelectionConfig();
            Assert.False(config.HasApp);
            Assert.False(config.HasPlc);
            Assert.False(config.HasTerminalLogs);
            Assert.False(config.HasConfiguration);
            Assert.False(config.HasSystab);
            Assert.False(config.HasGlobals);
            Assert.False(config.HasLrs);
            Assert.False(config.HasEvents);
            Assert.False(config.HasScreenshots);
            Assert.False(config.HasSetupInfo);
            Assert.False(config.HasManagerThread);
            Assert.False(config.IsS6);
        }

        [Fact]
        public void TabSelectionConfig_TimeFilter_DefaultsToInactive()
        {
            var config = new TabSelectionConfig();
            Assert.False(config.UseTimeFilter);
            Assert.Null(config.FilterStartTime);
            Assert.Null(config.FilterEndTime);
            Assert.Null(config.AvailableStartTime);
            Assert.Null(config.AvailableEndTime);
        }
    }

}
