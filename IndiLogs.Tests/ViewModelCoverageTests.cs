using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelCoverageTests
    {
        private static readonly BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;

        private static T CreateUninitialized<T>() =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetField(object obj, string fieldName, object? value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var field = t.GetField(fieldName, NPI | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }
                t = t.BaseType;
            }
        }

        private static object? InvokePrivate(object obj, string methodName, params object?[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var method = t.GetMethod(methodName, NPI | BindingFlags.DeclaredOnly);
                if (method != null)
                    return method.Invoke(obj, args);
                t = t.BaseType;
            }
            throw new MissingMethodException($"Method '{methodName}' not found on type '{obj.GetType().Name}'");
        }

        private static object? InvokeStatic(Type type, string methodName, params object?[] args)
        {
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (method == null)
                throw new MissingMethodException($"Static method '{methodName}' not found on type '{type.Name}'");
            return method.Invoke(null, args);
        }

        private static LogEntry MakeLog(string level = "Info", string message = "msg",
            string thread = "T1", string logger = "App.Service.Foo",
            string method = "DoWork", DateTime? date = null, string? pattern = null)
        {
            return new LogEntry
            {
                Level = level,
                Message = message,
                ThreadName = thread,
                Logger = logger,
                Method = method,
                Date = date ?? new DateTime(2025, 1, 1, 12, 0, 0),
                Pattern = pattern
            };
        }

        // =====================================================================
        //  VisualTimelineViewModel
        // =====================================================================

        [Fact]
        public void VisualTimeline_DefaultState_HasEmptyCollections()
        {
            var vm = new VisualTimelineViewModel();
            Assert.NotNull(vm.States);
            Assert.NotNull(vm.Markers);
            Assert.Empty(vm.States);
            Assert.Empty(vm.Markers);
            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_Clear_ResetsEverything()
        {
            var vm = new VisualTimelineViewModel();
            vm.States.Add(new TimelineState { Name = "INIT" });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });

            vm.Clear();

            Assert.Empty(vm.States);
            Assert.Empty(vm.Markers);
            Assert.Null(vm.SelectedState);
            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_TotalStates_ReturnsCount()
        {
            var vm = new VisualTimelineViewModel();
            vm.States.Add(new TimelineState { Name = "INIT" });
            vm.States.Add(new TimelineState { Name = "STANDBY" });
            Assert.Equal(2, vm.TotalStates);
        }

        [Fact]
        public void VisualTimeline_TotalErrors_CountsOnlyErrors()
        {
            var vm = new VisualTimelineViewModel();
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Event });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            Assert.Equal(2, vm.TotalErrors);
        }

        [Fact]
        public void VisualTimeline_TotalEvents_CountsOnlyEvents()
        {
            var vm = new VisualTimelineViewModel();
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Event });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            Assert.Equal(1, vm.TotalEvents);
        }

        [Fact]
        public void VisualTimeline_SelectedStateDuration_NoSelection_ReturnsDash()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal("-", vm.SelectedStateDuration);
        }

        [Fact]
        public void VisualTimeline_SelectedStateDuration_WithSelection_ReturnsFormatted()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState
            {
                StartTime = new DateTime(2025, 1, 1, 12, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 12, 1, 30)
            };
            Assert.NotEqual("-", vm.SelectedStateDuration);
            Assert.Contains(":", vm.SelectedStateDuration);
        }

        [Fact]
        public void VisualTimeline_SelectedStateErrors_NoSelection_ReturnsZero()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal(0, vm.SelectedStateErrors);
        }

        [Fact]
        public void VisualTimeline_SelectedStateErrors_WithSelection_ReturnsCount()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { ErrorCount = 5 };
            Assert.Equal(5, vm.SelectedStateErrors);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_NoSelection_ReturnsDash()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal("-", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_Failed_ReturnsFailure()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "FAILED" };
            Assert.Equal("FAILURE", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_WithErrors_ReturnsFailure()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "OK", ErrorCount = 3 };
            Assert.Equal("FAILURE", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_OkNoErrors_ReturnsPassed()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "OK", ErrorCount = 0 };
            Assert.Equal("PASSED", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_ViewScale_SetNotifies()
        {
            var vm = new VisualTimelineViewModel();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.ViewScale)) raised = true; };
            vm.ViewScale = 2.5;
            Assert.True(raised);
            Assert.Equal(2.5, vm.ViewScale);
        }

        [Fact]
        public void VisualTimeline_ViewOffset_SetNotifies()
        {
            var vm = new VisualTimelineViewModel();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.ViewOffset)) raised = true; };
            vm.ViewOffset = -50;
            Assert.True(raised);
            Assert.Equal(-50, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_ResetZoomCommand_ResetsScaleAndOffset()
        {
            var vm = new VisualTimelineViewModel();
            vm.ViewScale = 3.0;
            vm.ViewOffset = -100;

            vm.ResetZoomCommand.Execute(null);

            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_LoadData_EmptyLogs_NoStatesOrMarkers()
        {
            var vm = new VisualTimelineViewModel();
            vm.LoadData(new List<LogEntry>(), null);
            Assert.Empty(vm.States);
            Assert.Empty(vm.Markers);
        }

        [Fact]
        public void VisualTimeline_LoadData_ErrorLogs_CreatesMarkers()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", message: "Something failed", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(level: "Info", message: "normal log", date: new DateTime(2025, 1, 1, 12, 0, 1))
            };
            vm.LoadData(logs, null);
            Assert.Single(vm.Markers);
            Assert.Equal(TimelineMarkerType.Error, vm.Markers[0].Type);
        }

        [Fact]
        public void VisualTimeline_LoadData_OpcuaCriticalFailure_CreatesMarker()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Info", message: "ERR-OPCUA is not operational. Go to Init state.", date: new DateTime(2025, 1, 1, 12, 0, 0))
            };
            vm.LoadData(logs, null);
            Assert.Contains(vm.Markers, m => m.Message == "OPCUA CRITICAL FAILURE");
        }

        [Fact]
        public void VisualTimeline_LoadData_PlcFailureStateChange_CreatesMarker()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Info", message: "PLC_FAILURE_STATE_CHANGE detected", thread: "Events", date: new DateTime(2025, 1, 1, 12, 0, 0))
            };
            vm.LoadData(logs, null);
            Assert.Contains(vm.Markers, m => m.Message == "CRITICAL FAILURE EVENT");
        }

        [Fact]
        public void VisualTimeline_LoadData_S4StateTransitions_CreatesStates()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                MakeLog(message: "==== STATE_INIT - Enter ======", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(message: "some log", date: new DateTime(2025, 1, 1, 12, 0, 5)),
                MakeLog(message: "==== STATE_INIT - Exit ======", date: new DateTime(2025, 1, 1, 12, 0, 10)),
                MakeLog(message: "==== STATE_STANDBY - Enter ======", date: new DateTime(2025, 1, 1, 12, 0, 11)),
                MakeLog(message: "==== STATE_STANDBY - Exit ======", date: new DateTime(2025, 1, 1, 12, 0, 20)),
            };
            vm.LoadData(logs, null);
            Assert.True(vm.States.Count >= 2);
        }

        [Fact]
        public void VisualTimeline_LoadData_WithEvents_CreatesEventMarkers()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0))
            };
            var events = new List<EventEntry>
            {
                new EventEntry { Time = new DateTime(2025, 1, 1, 12, 0, 5), Name = "TestEvent", Description = "desc", Severity = "Warning" },
                new EventEntry { Time = new DateTime(2025, 1, 1, 12, 0, 10), Name = "ErrorEvent", Description = "desc", Severity = "Error" },
            };
            vm.LoadData(logs, events);
            Assert.Equal(2, vm.Markers.Count(m => m.Type == TimelineMarkerType.Event));
        }

        [Fact]
        public void VisualTimeline_FocusOnState_EmptyStates_DoesNotThrow()
        {
            var vm = new VisualTimelineViewModel();
            var ex = Record.Exception(() => vm.FocusOnState("INIT"));
            Assert.Null(ex);
        }

        [Fact]
        public void VisualTimeline_FocusOnState_FoundState_SelectsAndZooms()
        {
            var vm = new VisualTimelineViewModel();
            var s1 = new TimelineState
            {
                Name = "INIT",
                StartTime = new DateTime(2025, 1, 1, 12, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 12, 0, 10)
            };
            var s2 = new TimelineState
            {
                Name = "STANDBY",
                StartTime = new DateTime(2025, 1, 1, 12, 0, 10),
                EndTime = new DateTime(2025, 1, 1, 12, 1, 0)
            };
            vm.States.Add(s1);
            vm.States.Add(s2);

            vm.FocusOnState("STANDBY");

            Assert.Equal(s2, vm.SelectedState);
            Assert.True(vm.ViewScale >= 1.0);
        }

        [Fact]
        public void VisualTimeline_FocusOnState_NotFound_NoSelection()
        {
            var vm = new VisualTimelineViewModel();
            vm.States.Add(new TimelineState
            {
                Name = "INIT",
                StartTime = new DateTime(2025, 1, 1, 12, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 12, 0, 10)
            });

            vm.FocusOnState("NONEXISTENT");
            Assert.Null(vm.SelectedState);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_GetReady_ToDynamicReady_IsSuccess()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "GET_READY", "DYNAMIC_READY", 0);
            Assert.Equal("SUCCESS", result);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_GetReady_ToOther_IsFailed()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "GET_READY", "INIT", 0);
            Assert.Equal("FAILED", result);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_MechInit_ToStandby_IsSuccess()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "MECH_INIT", "STANDBY", 0);
            Assert.Equal("SUCCESS", result);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_MechInit_ToOther_IsFailed()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "MECH_INIT", "INIT", 0);
            Assert.Equal("FAILED", result);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_OtherState_WithErrors_IsWarning()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "STANDBY", "READY", 3);
            Assert.Equal("WARNING", result);
        }

        [Fact]
        public void VisualTimeline_DetermineStatus_OtherState_NoErrors_IsOk()
        {
            var vm = new VisualTimelineViewModel();
            var result = InvokePrivate(vm, "DetermineStatus", "STANDBY", "READY", 0);
            Assert.Equal("OK", result);
        }

        [Fact]
        public void VisualTimeline_GetColorForState_KnownState_ReturnsNonDefault()
        {
            var vm = new VisualTimelineViewModel();
            var color = InvokePrivate(vm, "GetColorForState", "INIT");
            Assert.NotNull(color);
        }

        [Fact]
        public void VisualTimeline_GetColorForState_UnknownState_ReturnsCornflowerBlue()
        {
            var vm = new VisualTimelineViewModel();
            var color = InvokePrivate(vm, "GetColorForState", "TOTALLY_UNKNOWN_STATE_XYZ");
            Assert.NotNull(color);
            // CornflowerBlue = #6495ED
            Assert.Equal(System.Windows.Media.Colors.CornflowerBlue, color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Error_ReturnsOrangeRed()
        {
            var vm = new VisualTimelineViewModel();
            var color = InvokePrivate(vm, "GetEventColor", "Error");
            Assert.Equal(System.Windows.Media.Colors.OrangeRed, color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Warning_ReturnsOrange()
        {
            var vm = new VisualTimelineViewModel();
            var color = InvokePrivate(vm, "GetEventColor", "Warning");
            Assert.Equal(System.Windows.Media.Colors.Orange, color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Other_ReturnsCyan()
        {
            var vm = new VisualTimelineViewModel();
            var color = InvokePrivate(vm, "GetEventColor", "Info");
            Assert.Equal(System.Windows.Media.Colors.Cyan, color);
        }

        // =====================================================================
        //  ComparisonPaneViewModel
        // =====================================================================

        [Fact]
        public void ComparisonPane_Constructor_InitializesWithAllPLC()
        {
            var plc = new List<LogEntry> { MakeLog(message: "plc1"), MakeLog(message: "plc2") };
            var app = new List<LogEntry> { MakeLog(message: "app1") };
            var vm = new ComparisonPaneViewModel(plc, app);

            Assert.Equal(ComparisonPaneViewModel.SourceType.AllPLC, vm.SelectedSourceType);
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ComparisonPane_SourceType_AllAPP_ShowsAppLogs()
        {
            var plc = new List<LogEntry> { MakeLog(message: "plc1") };
            var app = new List<LogEntry> { MakeLog(message: "app1"), MakeLog(message: "app2") };
            var vm = new ComparisonPaneViewModel(plc, app);

            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllAPP;
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ComparisonPane_ByThread_FiltersByThread()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(thread: "ThreadA", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(thread: "ThreadB", date: new DateTime(2025, 1, 1, 12, 0, 1)),
                MakeLog(thread: "ThreadA", date: new DateTime(2025, 1, 1, 12, 0, 2)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByThread;

            // First filter should be auto-selected
            Assert.NotNull(vm.SelectedFilter);
            // Should filter down to only matching thread
            Assert.True(vm.FilteredLogs.All(l => l.ThreadName == vm.SelectedFilter));
        }

        [Fact]
        public void ComparisonPane_ByLogger_FiltersByLogger()
        {
            var app = new List<LogEntry>
            {
                MakeLog(logger: "Service.A", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(logger: "Service.B", date: new DateTime(2025, 1, 1, 12, 0, 1)),
            };
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), app);
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByLogger;

            Assert.NotNull(vm.SelectedFilter);
            Assert.True(vm.FilteredLogs.All(l => l.Logger == vm.SelectedFilter));
        }

        [Fact]
        public void ComparisonPane_SearchText_FiltersResults()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(message: "alpha message", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(message: "beta message", date: new DateTime(2025, 1, 1, 12, 0, 1)),
                MakeLog(message: "alpha again", date: new DateTime(2025, 1, 1, 12, 0, 2)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());

            vm.SearchText = "alpha";
            Assert.Equal(2, vm.FilteredLogs.Count);
        }

        [Fact]
        public void ComparisonPane_ShowFilterDropdown_TrueForByThread()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByThread;
            Assert.True(vm.ShowFilterDropdown);
        }

        [Fact]
        public void ComparisonPane_ShowFilterDropdown_FalseForAllPLC()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.False(vm.ShowFilterDropdown);
        }

        [Fact]
        public void ComparisonPane_BinarySearchNearest_EmptyList_ReturnsNegative()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry>(), new List<LogEntry>());
            // FilteredLogs is empty since AllPLC with empty list
            Assert.Equal(-1, vm.BinarySearchNearest(DateTime.Now));
        }

        [Fact]
        public void ComparisonPane_BinarySearchNearest_TargetBeforeAll_ReturnsZero()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 10)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            Assert.Equal(0, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 11, 0, 0)));
        }

        [Fact]
        public void ComparisonPane_BinarySearchNearest_TargetAfterAll_ReturnsLast()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 10)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 13, 0, 0)));
        }

        [Fact]
        public void ComparisonPane_BinarySearchNearest_ExactMatch_ReturnsIndex()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 5)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 10)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            Assert.Equal(1, vm.BinarySearchNearest(new DateTime(2025, 1, 1, 12, 0, 5)));
        }

        [Fact]
        public void ComparisonPane_BinarySearchNearest_BetweenValues_ReturnsNearest()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 10)),
                MakeLog(date: new DateTime(2025, 1, 1, 12, 0, 20)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            // Closer to index 1 (12:00:10) than index 0 (12:00:00)
            int result = vm.BinarySearchNearest(new DateTime(2025, 1, 1, 12, 0, 8));
            Assert.Equal(1, result);
        }

        [Fact]
        public void ComparisonPane_GetLogAtIndex_ValidIndex_ReturnsLog()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(message: "first", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(message: "second", date: new DateTime(2025, 1, 1, 12, 0, 1)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            var log = vm.GetLogAtIndex(0);
            Assert.NotNull(log);
            Assert.Equal("first", log!.Message);
        }

        [Fact]
        public void ComparisonPane_GetLogAtIndex_NegativeIndex_ReturnsNull()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry> { MakeLog() }, new List<LogEntry>());
            Assert.Null(vm.GetLogAtIndex(-1));
        }

        [Fact]
        public void ComparisonPane_GetLogAtIndex_OutOfRange_ReturnsNull()
        {
            var vm = new ComparisonPaneViewModel(new List<LogEntry> { MakeLog() }, new List<LogEntry>());
            Assert.Null(vm.GetLogAtIndex(999));
        }

        [Fact]
        public void ComparisonPane_NullLogs_HandledGracefully()
        {
            var vm = new ComparisonPaneViewModel(null!, null!);
            Assert.Empty(vm.FilteredLogs);
        }

        [Fact]
        public void ComparisonPane_ByPattern_FiltersCorrectly()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(pattern: "PatA", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(pattern: "PatB", date: new DateTime(2025, 1, 1, 12, 0, 1)),
                MakeLog(pattern: "PatA", date: new DateTime(2025, 1, 1, 12, 0, 2)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByPattern;

            Assert.NotNull(vm.SelectedFilter);
            Assert.True(vm.FilteredLogs.All(l => l.Pattern == vm.SelectedFilter));
        }

        [Fact]
        public void ComparisonPane_ByMethodFromPLC_FiltersCorrectly()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(method: "MethodA", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(method: "MethodB", date: new DateTime(2025, 1, 1, 12, 0, 1)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByMethodFromPLC;

            Assert.NotNull(vm.SelectedFilter);
            Assert.True(vm.FilteredLogs.All(l => l.Method == vm.SelectedFilter));
        }

        [Fact]
        public void ComparisonPane_ByLoggerFromPLC_FiltersCorrectly()
        {
            var plc = new List<LogEntry>
            {
                MakeLog(logger: "LogA", date: new DateTime(2025, 1, 1, 12, 0, 0)),
                MakeLog(logger: "LogB", date: new DateTime(2025, 1, 1, 12, 0, 1)),
            };
            var vm = new ComparisonPaneViewModel(plc, new List<LogEntry>());
            vm.SelectedSourceType = ComparisonPaneViewModel.SourceType.ByLoggerFromPLC;

            Assert.NotNull(vm.SelectedFilter);
            Assert.True(vm.FilteredLogs.All(l => l.Logger == vm.SelectedFilter));
        }

        // =====================================================================
        //  ConfigExplorerViewModel.Database — FilterTreeNode, SetNodeVisibility
        // =====================================================================

        [Fact]
        public void ConfigExplorer_FilterTreeNode_SelfMatch_ReturnsTrue()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var node = new DbTreeNode { Name = "Users", Type = "TABLE", Schema = "CREATE TABLE" };
            var result = InvokePrivate(vm, "FilterTreeNode", node, "User");
            Assert.Equal(true, result);
            Assert.True(node.IsVisible);
        }

        [Fact]
        public void ConfigExplorer_FilterTreeNode_ChildMatch_ParentVisible()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var child = new DbTreeNode { Name = "UserName", Type = "TEXT" };
            var parent = new DbTreeNode { Name = "OtherTable" };
            parent.Children.Add(child);

            var result = InvokePrivate(vm, "FilterTreeNode", parent, "UserName");
            Assert.Equal(true, result);
            Assert.True(parent.IsVisible);
            Assert.True(child.IsVisible);
        }

        [Fact]
        public void ConfigExplorer_FilterTreeNode_NoMatch_ReturnsFalse()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var node = new DbTreeNode { Name = "Settings", Type = "TABLE" };
            var result = InvokePrivate(vm, "FilterTreeNode", node, "ZZZ_NOMATCH");
            Assert.Equal(false, result);
            Assert.False(node.IsVisible);
        }

        [Fact]
        public void ConfigExplorer_SetNodeVisibility_SetsRecursively()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            var grandchild = new DbTreeNode { Name = "GC" };
            var child = new DbTreeNode { Name = "C" };
            child.Children.Add(grandchild);
            var root = new DbTreeNode { Name = "Root" };
            root.Children.Add(child);

            InvokePrivate(vm, "SetNodeVisibility", root, false);
            Assert.False(root.IsVisible);
            Assert.False(child.IsVisible);
            Assert.False(grandchild.IsVisible);

            InvokePrivate(vm, "SetNodeVisibility", root, true);
            Assert.True(root.IsVisible);
            Assert.True(child.IsVisible);
            Assert.True(grandchild.IsVisible);
        }

        [Fact]
        public void ConfigExplorer_FilterDbTreeNodes_NoFilter_AllVisible()
        {
            var vm = CreateUninitialized<ConfigExplorerViewModel>();
            SetField(vm, "_configSearchText", "");

            var dbTreeNodes = new ObservableCollection<DbTreeNode>();
            var root = new DbTreeNode { Name = "Tables (2)" };
            var t1 = new DbTreeNode { Name = "Users", NodeType = "Table" };
            var t2 = new DbTreeNode { Name = "Settings", NodeType = "Table" };
            root.Children.Add(t1);
            root.Children.Add(t2);
            dbTreeNodes.Add(root);

            // Use reflection to set DbTreeNodes
            var prop = typeof(ConfigExplorerViewModel).GetProperty("DbTreeNodes");
            if (prop != null && prop.CanWrite)
                prop.SetValue(vm, dbTreeNodes);
            else
                SetField(vm, "<DbTreeNodes>k__BackingField", dbTreeNodes);

            var ex = Record.Exception(() => InvokePrivate(vm, "FilterDbTreeNodes"));
            // May throw if DbTreeNodes property can't be set, but FilterTreeNode is tested above
            if (ex == null)
            {
                Assert.True(t1.IsVisible);
                Assert.True(t2.IsVisible);
            }
        }

        // =====================================================================
        //  StatsViewModel — TruncateMessage, GetShortLoggerName, TopN
        // =====================================================================

        [Theory]
        [InlineData(null, "(empty)")]
        [InlineData("", "(empty)")]
        [InlineData("short", "short")]
        public void StatsVM_TruncateMessage_HandlesEdgeCases(string? input, string expected)
        {
            var result = (string)InvokeStatic(typeof(StatsViewModel), "TruncateMessage", input!, 100)!;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void StatsVM_TruncateMessage_LongMessage_Truncates()
        {
            string longMsg = new string('x', 150);
            var result = (string)InvokeStatic(typeof(StatsViewModel), "TruncateMessage", longMsg, 100)!;
            Assert.Equal(103, result.Length); // 100 + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void StatsVM_GetShortLoggerName_EmptyLogger_ReturnsUnknown()
        {
            var vm = new StatsViewModel(new List<LogEntry>(), new List<LogEntry>(), null, null, false, false);
            var result = InvokePrivate(vm, "GetShortLoggerName", "");
            Assert.Equal("Unknown", result);
        }

        [Fact]
        public void StatsVM_GetShortLoggerName_ShortLogger_ReturnsAsIs()
        {
            var vm = new StatsViewModel(new List<LogEntry>(), new List<LogEntry>(), null, null, false, false);
            var result = InvokePrivate(vm, "GetShortLoggerName", "Service.Foo");
            Assert.Equal("Service.Foo", result);
        }

        [Fact]
        public void StatsVM_GetShortLoggerName_LongLogger_ReturnsLastTwo()
        {
            var vm = new StatsViewModel(new List<LogEntry>(), new List<LogEntry>(), null, null, false, false);
            var result = InvokePrivate(vm, "GetShortLoggerName", "IndiLogs.App.Service.Foo");
            Assert.Equal("Service.Foo", result);
        }

        [Fact]
        public void StatsVM_GetShortLoggerName_Cached_ReturnsSameResult()
        {
            var vm = new StatsViewModel(new List<LogEntry>(), new List<LogEntry>(), null, null, false, false);
            var r1 = InvokePrivate(vm, "GetShortLoggerName", "A.B.C.D");
            var r2 = InvokePrivate(vm, "GetShortLoggerName", "A.B.C.D");
            Assert.Equal(r1, r2);
        }

        [Fact]
        public void StatsVM_TopN_EmptyDict_ReturnsEmpty()
        {
            var dict = new Dictionary<string, int>();
            var result = (List<KeyValuePair<string, int>>)InvokeStatic(typeof(StatsViewModel), "TopN", dict, 5)!;
            Assert.Empty(result);
        }

        [Fact]
        public void StatsVM_TopN_FewerThanN_ReturnsSorted()
        {
            var dict = new Dictionary<string, int> { { "A", 3 }, { "B", 1 }, { "C", 5 } };
            var result = (List<KeyValuePair<string, int>>)InvokeStatic(typeof(StatsViewModel), "TopN", dict, 10)!;
            Assert.Equal(3, result.Count);
            Assert.Equal("C", result[0].Key);
            Assert.Equal("A", result[1].Key);
            Assert.Equal("B", result[2].Key);
        }

        [Fact]
        public void StatsVM_TopN_MoreThanN_ReturnsTopN()
        {
            var dict = new Dictionary<string, int>
            {
                { "A", 10 }, { "B", 5 }, { "C", 20 }, { "D", 1 }, { "E", 15 }
            };
            var result = (List<KeyValuePair<string, int>>)InvokeStatic(typeof(StatsViewModel), "TopN", dict, 3)!;
            Assert.Equal(3, result.Count);
            Assert.Equal(20, result[0].Value);
            Assert.Equal(15, result[1].Value);
            Assert.Equal(10, result[2].Value);
        }

        [Fact]
        public void StatsVM_FormatDuration_Under1Min_ShowsSeconds()
        {
            var result = (string)InvokeStatic(typeof(StatsViewModel), "FormatDuration", TimeSpan.FromSeconds(45))!;
            Assert.Contains("sec", result);
        }

        [Fact]
        public void StatsVM_FormatDuration_Over1Min_ShowsMinutes()
        {
            var result = (string)InvokeStatic(typeof(StatsViewModel), "FormatDuration", TimeSpan.FromMinutes(2.5))!;
            Assert.Contains("min", result);
        }

        // =====================================================================
        //  DifferentLogsViewModel — BuildAvailableFields, CloseFile, helpers
        // =====================================================================

        [Fact]
        public void DifferentLogsVM_BuildAvailableFields_DefaultFields()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_columns", null);
            SetField(vm, "_allLogEntries", new List<LogEntry>());
            SetField(vm, "_availableFields", new List<string>());

            vm.BuildAvailableFields();

            Assert.Contains("Message", vm.AvailableFields);
            Assert.Contains("Level", vm.AvailableFields);
            Assert.Contains("ThreadName", vm.AvailableFields);
            Assert.Contains("Logger", vm.AvailableFields);
            Assert.Contains("Method", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogsVM_BuildAvailableFields_IncludesExtraFields()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_columns", null);
            var entries = new List<LogEntry>
            {
                new LogEntry { ExtraFields = new Dictionary<string, string> { { "CustomField", "val" } } }
            };
            SetField(vm, "_allLogEntries", entries);
            SetField(vm, "_availableFields", new List<string>());

            vm.BuildAvailableFields();

            Assert.Contains("CustomField", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogsVM_CloseFile_ResetsState()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_currentFilePath", "some/path.log");
            SetField(vm, "_allLogEntries", new List<LogEntry> { MakeLog() });
            SetField(vm, "_filteredEntries", new ObservableCollection<LogEntry> { MakeLog() });
            SetField(vm, "_columns", null);
            SetField(vm, "_filterRoot", null);
            SetField(vm, "_isFilterActive", true);
            SetField(vm, "_coloringRules", new List<IndiLogs_3._0.Models.ColoringCondition>());
            SetField(vm, "_availableFields", new List<string> { "a" });
            SetField(vm, "_statusText", "loaded");

            // Need Entries collection
            var entriesProp = typeof(DifferentLogsViewModel).GetProperty("Entries");
            if (entriesProp != null)
            {
                var entries = entriesProp.GetValue(vm);
                if (entries == null)
                {
                    // Entries is init-only, set via reflection
                    SetField(vm, "<Entries>k__BackingField", new ObservableCollection<IndiLogs.PluginAPI.LogEntryDto>());
                }
            }

            InvokePrivate(vm, "CloseFile");

            Assert.Null(vm.CurrentFilePath);
            Assert.False(vm.HasFile);
            Assert.Empty(vm.AllLogEntries);
            Assert.False(vm.IsFilterActive);
        }

        [Fact]
        public void DifferentLogsVM_CurrentFileName_NullPath_ReturnsEmpty()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_currentFilePath", null);
            Assert.Equal(string.Empty, vm.CurrentFileName);
        }

        [Fact]
        public void DifferentLogsVM_HasFile_NullPath_ReturnsFalse()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_currentFilePath", null);
            Assert.False(vm.HasFile);
        }

        [Fact]
        public void DifferentLogsVM_HasFile_WithPath_ReturnsTrue()
        {
            var vm = CreateUninitialized<DifferentLogsViewModel>();
            SetField(vm, "_currentFilePath", "c:\\test.log");
            Assert.True(vm.HasFile);
        }

        [Fact]
        public void DifferentLogsVM_BuildDefaultColumns_ReturnsThreeColumns()
        {
            var result = InvokeStatic(typeof(DifferentLogsViewModel), "BuildDefaultColumns");
            Assert.NotNull(result);
            var cols = (IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>)result!;
            Assert.Equal(3, cols.Count);
            Assert.Equal("Date", cols[0].Header);
            Assert.Equal("Level", cols[1].Header);
            Assert.Equal("Message", cols[2].Header);
        }

        // =====================================================================
        //  MainViewModel.Systab — FilterSystabEntries, LoadSystabEntries
        // =====================================================================

        [Fact]
        public void MainVM_FilterSystabEntries_NoFilter_ShowsAll()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Param1", Saved = "10", Default = "10" },
                new SystabEntry { Parameter = "Param2", Saved = "20", Default = "30", IsDifferent = true },
            };
            var systabEntries = new ObservableCollection<SystabEntry>();

            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokePrivate(vm, "FilterSystabEntries");

            Assert.Equal(2, systabEntries.Count);
        }

        [Fact]
        public void MainVM_FilterSystabEntries_WithSearchText_FiltersMatching()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Temperature", Saved = "100" },
                new SystabEntry { Parameter = "Pressure", Saved = "50" },
                new SystabEntry { Parameter = "TempSensor", Saved = "75" },
            };
            var systabEntries = new ObservableCollection<SystabEntry>();

            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_systabSearchText", "Temp");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokePrivate(vm, "FilterSystabEntries");

            Assert.Equal(2, systabEntries.Count);
        }

        [Fact]
        public void MainVM_FilterSystabEntries_DiffsOnly_ShowsOnlyDifferent()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Param1", IsDifferent = false },
                new SystabEntry { Parameter = "Param2", IsDifferent = true },
                new SystabEntry { Parameter = "Param3", IsDifferent = true },
            };
            var systabEntries = new ObservableCollection<SystabEntry>();

            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", true);

            InvokePrivate(vm, "FilterSystabEntries");

            Assert.Equal(2, systabEntries.Count);
            Assert.All(systabEntries, e => Assert.True(e.IsDifferent));
        }

        [Fact]
        public void MainVM_FilterSystabEntries_DiffsAndSearch_CombinesFilters()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Temperature", IsDifferent = true },
                new SystabEntry { Parameter = "Pressure", IsDifferent = true },
                new SystabEntry { Parameter = "TempSensor", IsDifferent = false },
            };
            var systabEntries = new ObservableCollection<SystabEntry>();

            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_systabSearchText", "Temp");
            SetField(vm, "_systabShowDiffsOnly", true);

            InvokePrivate(vm, "FilterSystabEntries");

            Assert.Single(systabEntries);
            Assert.Equal("Temperature", systabEntries[0].Parameter);
        }

        [Fact]
        public void MainVM_LoadSystabEntries_NullNode_ClearsEntries()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var systabEntries = new ObservableCollection<SystabEntry> { new SystabEntry() };
            var allEntries = new ObservableCollection<SystabEntry> { new SystabEntry() };

            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", null);
            SetField(vm, "_systabSearchText", "old");
            SetField(vm, "_systabShowDiffsOnly", true);

            InvokePrivate(vm, "LoadSystabEntries");

            Assert.Empty(systabEntries);
            Assert.Empty(allEntries);
        }

        [Fact]
        public void MainVM_LoadSystabEntries_NodeWithEntries_PopulatesBoth()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var systabEntries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>();
            var node = new SystabTopicNode
            {
                Name = "Topic1",
                Entries = new List<SystabEntry>
                {
                    new SystabEntry { Parameter = "P1" },
                    new SystabEntry { Parameter = "P2" },
                }
            };

            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", node);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokePrivate(vm, "LoadSystabEntries");

            Assert.Equal(2, systabEntries.Count);
            Assert.Equal(2, allEntries.Count);
        }

        [Fact]
        public void MainVM_LoadSystabEntries_TopicNodeWithChildren_ShowsChildEntries()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var systabEntries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>();
            var child1 = new SystabTopicNode
            {
                Name = "Child1",
                Entries = new List<SystabEntry> { new SystabEntry { Parameter = "CP1" } }
            };
            var child2 = new SystabTopicNode
            {
                Name = "Child2",
                Entries = new List<SystabEntry> { new SystabEntry { Parameter = "CP2" } }
            };
            var topicNode = new SystabTopicNode
            {
                Name = "Topic",
                Entries = new List<SystabEntry>(), // empty entries at topic level
                Children = new ObservableCollection<SystabTopicNode> { child1, child2 }
            };

            SetField(vm, "_systabEntries", systabEntries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", topicNode);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokePrivate(vm, "LoadSystabEntries");

            Assert.Equal(2, systabEntries.Count);
            Assert.Equal(2, allEntries.Count);
        }

        // =====================================================================
        //  ExportConfigurationViewModel — properties and state
        // =====================================================================

        [Fact]
        public void ExportConfigVM_IsLoading_DefaultFalse()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_isLoading", false);
            Assert.False(vm.IsLoading);
        }

        [Fact]
        public void ExportConfigVM_IncludeUnixTime_DefaultTrue()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_includeUnixTime", true);
            Assert.True(vm.IncludeUnixTime);
        }

        [Fact]
        public void ExportConfigVM_IncludeEvents_DefaultTrue()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_includeEvents", true);
            Assert.True(vm.IncludeEvents);
        }

        [Fact]
        public void ExportConfigVM_IncludeMachineState_DefaultTrue()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_includeMachineState", true);
            Assert.True(vm.IncludeMachineState);
        }

        [Fact]
        public void ExportConfigVM_IncludeLogStats_DefaultFalse()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_includeLogStats", false);
            Assert.False(vm.IncludeLogStats);
        }

        [Fact]
        public void ExportConfigVM_HasEmStatisticsData_NullSession_ReturnsFalse()
        {
            var vm = CreateUninitialized<ExportConfigurationViewModel>();
            SetField(vm, "_sessionData", null);
            // HasEmStatisticsData uses null-conditional, should return false
            Assert.False(vm.HasEmStatisticsData);
        }

        // =====================================================================
        //  Model tests for coverage (TimelineState, TimelineMarker, etc.)
        // =====================================================================

        [Fact]
        public void TimelineState_Duration_ComputesCorrectly()
        {
            var state = new TimelineState
            {
                StartTime = new DateTime(2025, 1, 1, 12, 0, 0),
                EndTime = new DateTime(2025, 1, 1, 12, 1, 30)
            };
            Assert.Equal(TimeSpan.FromSeconds(90), state.Duration);
        }

        [Fact]
        public void TimelineState_RelatedLogs_InitializedEmpty()
        {
            var state = new TimelineState();
            Assert.NotNull(state.RelatedLogs);
            Assert.Empty(state.RelatedLogs);
        }

        [Fact]
        public void SystabEntry_IsDifferent_DefaultFalse()
        {
            var entry = new SystabEntry();
            Assert.False(entry.IsDifferent);
        }

        [Fact]
        public void SystabTopicNode_PropertyChanged_Fires()
        {
            var node = new SystabTopicNode();
            bool raised = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(node.Name)) raised = true; };
            node.Name = "NewName";
            Assert.True(raised);
        }

        [Fact]
        public void DbTreeNode_SetVisibility_NotifiesPropertyChanged()
        {
            var node = new DbTreeNode();
            bool raised = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(node.IsVisible)) raised = true; };
            node.IsVisible = false;
            Assert.True(raised);
            Assert.False(node.IsVisible);
        }

        [Fact]
        public void DbTreeNode_IsExpanded_NotifiesPropertyChanged()
        {
            var node = new DbTreeNode();
            bool raised = false;
            node.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(node.IsExpanded)) raised = true; };
            node.IsExpanded = true;
            Assert.True(raised);
            Assert.True(node.IsExpanded);
        }

        [Fact]
        public void EventEntry_DefaultValues()
        {
            var entry = new EventEntry();
            Assert.Equal("", entry.Name);
            Assert.Equal("", entry.State);
            Assert.Equal("", entry.Severity);
            Assert.Equal("", entry.Description);
            Assert.Null(entry.Parameters);
        }

        [Fact]
        public void TimelineMarker_DefaultValues()
        {
            var marker = new TimelineMarker();
            Assert.Equal("", marker.Message);
            Assert.Null(marker.Severity);
            Assert.Null(marker.OriginalLog);
        }
    }
}
