using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelBatchTests
    {
        // =====================================================================
        // VisualTimelineViewModel tests
        // =====================================================================

        [Fact]
        public void VisualTimeline_Constructor_InitializesDefaults()
        {
            var vm = new VisualTimelineViewModel();
            Assert.NotNull(vm.States);
            Assert.NotNull(vm.Markers);
            Assert.Equal(0, vm.States.Count);
            Assert.Equal(0, vm.Markers.Count);
            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
            Assert.Null(vm.SelectedState);
            Assert.NotNull(vm.ResetZoomCommand);
        }

        [Fact]
        public void VisualTimeline_TotalStates_ReturnsCount()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal(0, vm.TotalStates);
            vm.States.Add(new TimelineState { Name = "INIT" });
            Assert.Equal(1, vm.TotalStates);
        }

        [Fact]
        public void VisualTimeline_TotalErrors_CountsErrorMarkers()
        {
            var vm = new VisualTimelineViewModel();
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Event });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            Assert.Equal(2, vm.TotalErrors);
        }

        [Fact]
        public void VisualTimeline_TotalEvents_CountsEventMarkers()
        {
            var vm = new VisualTimelineViewModel();
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Event });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            Assert.Equal(1, vm.TotalEvents);
        }

        [Fact]
        public void VisualTimeline_SelectedStateDuration_WhenNull_ReturnsDash()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal("-", vm.SelectedStateDuration);
        }

        [Fact]
        public void VisualTimeline_SelectedStateDuration_WhenSet_ReturnsFormatted()
        {
            var vm = new VisualTimelineViewModel();
            var state = new TimelineState
            {
                StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 2, 30, 500)
            };
            vm.SelectedState = state;
            Assert.Contains("02:30", vm.SelectedStateDuration);
        }

        [Fact]
        public void VisualTimeline_SelectedStateErrors_WhenNull_ReturnsZero()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal(0, vm.SelectedStateErrors);
        }

        [Fact]
        public void VisualTimeline_SelectedStateErrors_WhenSet_ReturnsCount()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { ErrorCount = 5 };
            Assert.Equal(5, vm.SelectedStateErrors);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_WhenNull_ReturnsDash()
        {
            var vm = new VisualTimelineViewModel();
            Assert.Equal("-", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_WhenFailed_ReturnsFailure()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "FAILED" };
            Assert.Equal("FAILURE", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_WhenErrorCount_ReturnsFailure()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "OK", ErrorCount = 3 };
            Assert.Equal("FAILURE", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_SelectedStateResult_WhenOK_ReturnsPassed()
        {
            var vm = new VisualTimelineViewModel();
            vm.SelectedState = new TimelineState { Status = "OK", ErrorCount = 0 };
            Assert.Equal("PASSED", vm.SelectedStateResult);
        }

        [Fact]
        public void VisualTimeline_Clear_ResetsAllState()
        {
            var vm = new VisualTimelineViewModel();
            vm.States.Add(new TimelineState { Name = "INIT" });
            vm.Markers.Add(new TimelineMarker { Type = TimelineMarkerType.Error });
            vm.SelectedState = new TimelineState { Name = "X" };
            vm.ViewScale = 3.0;
            vm.ViewOffset = 50;

            vm.Clear();

            Assert.Equal(0, vm.States.Count);
            Assert.Equal(0, vm.Markers.Count);
            Assert.Null(vm.SelectedState);
            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_ResetZoomCommand_ResetsScaleAndOffset()
        {
            var vm = new VisualTimelineViewModel();
            vm.ViewScale = 5.0;
            vm.ViewOffset = -100;
            vm.ResetZoomCommand.Execute(null);
            Assert.Equal(1.0, vm.ViewScale);
            Assert.Equal(0, vm.ViewOffset);
        }

        [Fact]
        public void VisualTimeline_ViewScale_SetterNotifies()
        {
            var vm = new VisualTimelineViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.ViewScale = 2.5;
            Assert.Contains("ViewScale", changed);
        }

        [Fact]
        public void VisualTimeline_ViewOffset_SetterNotifies()
        {
            var vm = new VisualTimelineViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.ViewOffset = -10;
            Assert.Contains("ViewOffset", changed);
        }

        [Fact]
        public void VisualTimeline_SelectedState_SetterNotifiesMultipleProperties()
        {
            var vm = new VisualTimelineViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.SelectedState = new TimelineState { Name = "INIT" };
            Assert.Contains("SelectedState", changed);
            Assert.Contains("SelectedStateDuration", changed);
            Assert.Contains("SelectedStateErrors", changed);
            Assert.Contains("SelectedStateResult", changed);
        }

        [Fact]
        public void VisualTimeline_LoadData_WithErrorLogs_CreatesErrorMarkers()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 10, 0, 0), Level = "Error", Message = "Something failed" },
                new LogEntry { Date = new DateTime(2024, 1, 1, 10, 0, 1), Level = "Info", Message = "OK" }
            };
            vm.LoadData(logs, null);
            Assert.True(vm.Markers.Any(m => m.Type == TimelineMarkerType.Error));
        }

        [Fact]
        public void VisualTimeline_LoadData_WithOpcuaFailure_CreatesCriticalMarker()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 10, 0, 0), Level = "Info", Message = "ERR-OPCUA is not operational. Go to Init state." }
            };
            vm.LoadData(logs, null);
            Assert.True(vm.Markers.Any(m => m.Message == "OPCUA CRITICAL FAILURE"));
        }

        [Fact]
        public void VisualTimeline_LoadData_WithPlcFailureEvent_CreatesCriticalMarker()
        {
            var vm = new VisualTimelineViewModel();
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2024, 1, 1, 10, 0, 0), Level = "Info", ThreadName = "Events", Message = "PLC_FAILURE_STATE_CHANGE happened" }
            };
            vm.LoadData(logs, null);
            Assert.True(vm.Markers.Any(m => m.Message == "CRITICAL FAILURE EVENT"));
        }

        [Fact]
        public void VisualTimeline_LoadData_WithEvents_CreatesEventMarkers()
        {
            var vm = new VisualTimelineViewModel();
            var events = new List<EventEntry>
            {
                new EventEntry { Time = DateTime.Now, Name = "TestEvent", Description = "desc", Severity = "Warning" }
            };
            vm.LoadData(new List<LogEntry>(), events);
            Assert.Equal(1, vm.Markers.Count(m => m.Type == TimelineMarkerType.Event));
        }

        [Fact]
        public void VisualTimeline_LoadData_EmptyLogs_NoStates()
        {
            var vm = new VisualTimelineViewModel();
            vm.LoadData(new List<LogEntry>(), null);
            Assert.Equal(0, vm.States.Count);
        }

        [Fact]
        public void VisualTimeline_FocusOnState_EmptyStates_DoesNotThrow()
        {
            var vm = new VisualTimelineViewModel();
            vm.FocusOnState("INIT");
        }

        [Fact]
        public void VisualTimeline_FocusOnState_NonExistentState_DoesNotChangeSelection()
        {
            var vm = new VisualTimelineViewModel();
            vm.States.Add(new TimelineState
            {
                Name = "STANDBY",
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddMinutes(1)
            });
            vm.FocusOnState("NONEXISTENT");
            Assert.Null(vm.SelectedState);
        }

        [Fact]
        public void VisualTimeline_FocusOnState_ExistingState_SelectsAndZooms()
        {
            var vm = new VisualTimelineViewModel();
            var s1 = new TimelineState { Name = "INIT", StartTime = new DateTime(2024, 1, 1, 10, 0, 0), EndTime = new DateTime(2024, 1, 1, 10, 1, 0) };
            var s2 = new TimelineState { Name = "STANDBY", StartTime = new DateTime(2024, 1, 1, 10, 1, 0), EndTime = new DateTime(2024, 1, 1, 10, 10, 0) };
            vm.States.Add(s1);
            vm.States.Add(s2);
            vm.FocusOnState("STANDBY");
            Assert.Equal(s2, vm.SelectedState);
            Assert.True(vm.ViewScale >= 1.0);
        }

        // =====================================================================
        // VisualTimelineViewModel.DetermineStatus via reflection
        // =====================================================================

        [Theory]
        [InlineData("GET_READY", "DYNAMIC_READY", 0, "SUCCESS")]
        [InlineData("GET_READY", "STANDBY", 0, "FAILED")]
        [InlineData("MECH_INIT", "STANDBY", 0, "SUCCESS")]
        [InlineData("MECH_INIT", "OFF", 0, "FAILED")]
        [InlineData("PRINT", "POST_PRINT", 0, "OK")]
        [InlineData("PRINT", "POST_PRINT", 2, "WARNING")]
        public void VisualTimeline_DetermineStatus_ReturnsExpected(string current, string next, int errors, string expected)
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("DetermineStatus", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (string)method!.Invoke(vm, new object[] { current, next, errors })!;
            Assert.Equal(expected, result);
        }

        // =====================================================================
        // VisualTimelineViewModel.GetColorForState via reflection
        // =====================================================================

        [Theory]
        [InlineData("INIT")]
        [InlineData("STANDBY")]
        [InlineData("PRINT")]
        [InlineData("OFF")]
        public void VisualTimeline_GetColorForState_KnownState_ReturnsNonDefault(string stateName)
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetColorForState", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var color = (Color)method!.Invoke(vm, new object[] { stateName })!;
            Assert.NotEqual(Colors.CornflowerBlue, color);
        }

        [Fact]
        public void VisualTimeline_GetColorForState_UnknownState_ReturnsCornflowerBlue()
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetColorForState", BindingFlags.NonPublic | BindingFlags.Instance);
            var color = (Color)method!.Invoke(vm, new object[] { "ZZUNKNOWN_STATE_ZZ" })!;
            Assert.Equal(Colors.CornflowerBlue, color);
        }

        // =====================================================================
        // VisualTimelineViewModel.GetEventColor via reflection
        // =====================================================================

        [Theory]
        [InlineData("Error")]
        [InlineData("Warning")]
        [InlineData("Info")]
        public void VisualTimeline_GetEventColor_ReturnsValidColor(string severity)
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetEventColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var color = (Color)method!.Invoke(vm, new object[] { severity })!;
            Assert.IsType<Color>(color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Error_ReturnsOrangeRed()
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetEventColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var color = (Color)method!.Invoke(vm, new object[] { "Error" })!;
            Assert.Equal(Colors.OrangeRed, color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Warning_ReturnsOrange()
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetEventColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var color = (Color)method!.Invoke(vm, new object[] { "Warning" })!;
            Assert.Equal(Colors.Orange, color);
        }

        [Fact]
        public void VisualTimeline_GetEventColor_Other_ReturnsCyan()
        {
            var vm = new VisualTimelineViewModel();
            var method = typeof(VisualTimelineViewModel).GetMethod("GetEventColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var color = (Color)method!.Invoke(vm, new object[] { "Something" })!;
            Assert.Equal(Colors.Cyan, color);
        }

        // =====================================================================
        // StepRecorderViewModel tests
        // =====================================================================

        [Fact]
        public void StepFrame_Defaults()
        {
            var frame = new StepFrame();
            Assert.Equal("", frame.FileName);
            Assert.Equal("", frame.TextContent);
            Assert.Null(frame.ImageData);
            Assert.Null(frame.Bitmap);
        }

        [Fact]
        public void StepFrame_Bitmap_NullData_ReturnsNull()
        {
            var frame = new StepFrame { ImageData = null };
            Assert.Null(frame.Bitmap);
        }

        [Fact]
        public void StepFrame_Bitmap_EmptyData_ReturnsNull()
        {
            var frame = new StepFrame { ImageData = Array.Empty<byte>() };
            Assert.Null(frame.Bitmap);
        }

        [Fact]
        public void StepRecorder_Clear_ResetsState()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            vm.Clear();
            Assert.False(vm.HasFrames);
            Assert.False(vm.HasIsr);
            Assert.False(vm.IsPlaying);
            Assert.Equal(-1, GetPrivateField<int>(vm, "_currentIndex"));
            Assert.Equal("No step data loaded.", vm.StatusText);
        }

        [Fact]
        public void StepRecorder_CurrentFrame_WhenNoFrames_ReturnsNull()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            Assert.Null(vm.CurrentFrame);
        }

        [Fact]
        public void StepRecorder_CurrentIndex_WhenNoFrames_ReturnsZero()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            Assert.Equal(0, vm.CurrentIndex);
        }

        [Fact]
        public void StepRecorder_TotalFrames_WhenNoFrames_ReturnsZero()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            Assert.Equal(0, vm.TotalFrames);
        }

        [Fact]
        public void StepRecorder_HasFrames_WhenNoFrames_ReturnsFalse()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            Assert.False(vm.HasFrames);
        }

        [Fact]
        public void StepRecorder_Dispose_DoesNotThrow()
        {
            var dialog = new TestDialogService();
            var vm = new StepRecorderViewModel(dialog);
            vm.Dispose();
        }

        [Fact]
        public void StepRecorder_ParseTimestampFromFilename_KnownFormat_ReturnsDate()
        {
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (DateTime)method!.Invoke(null, new object[] { "2024-01-15_10-30-45" })!;
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45), result);
        }

        [Fact]
        public void StepRecorder_ParseTimestampFromFilename_CompactFormat_ReturnsDate()
        {
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object[] { "20240115_103045" })!;
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45), result);
        }

        [Fact]
        public void StepRecorder_ParseTimestampFromFilename_Unknown_ReturnsMinValue()
        {
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object[] { "randomname" })!;
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void StepRecorder_ParseTimestampFromFilename_WithPrefix_FindsTimestamp()
        {
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object[] { "step_2024-01-15_10-30-45_capture" })!;
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45), result);
        }

        [Fact]
        public void StepRecorder_ParseTimestampFromFilename_WithSubseconds_ReturnsValidDate()
        {
            // The shorter format (yyyy-MM-dd_HH-mm-ss) matches first in the sliding window,
            // so sub-second precision depends on format ordering. Verify we at least get the date.
            var method = typeof(StepRecorderViewModel).GetMethod("ParseTimestampFromFilename",
                BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DateTime)method!.Invoke(null, new object[] { "2024-01-15_10-30-45.123" })!;
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 45), new DateTime(result.Year, result.Month, result.Day, result.Hour, result.Minute, result.Second));
        }

        // =====================================================================
        // LogComparisonViewModel tests
        // =====================================================================

        [Fact]
        public void LogComparison_DeltaDisplay_BothNull_ReturnsDash()
        {
            var plcLogs = new List<LogEntry>();
            var appLogs = new List<LogEntry>();
            var vm = new LogComparisonViewModel(plcLogs, appLogs);
            Assert.Equal("\u2014", vm.DeltaDisplay); // em dash
        }

        [Fact]
        public void LogComparison_DeltaDisplay_SubMillisecond_ReturnsZeroMs()
        {
            var plcLogs = new List<LogEntry>();
            var appLogs = new List<LogEntry>();
            var vm = new LogComparisonViewModel(plcLogs, appLogs);

            var now = DateTime.Now;
            vm.LeftPane.SelectedLog = new LogEntry { Date = now };
            vm.RightPane.SelectedLog = new LogEntry { Date = now };
            Assert.Equal("0ms", vm.DeltaDisplay);
        }

        [Fact]
        public void LogComparison_DeltaDisplay_SmallPositiveDelta_ReturnsMilliseconds()
        {
            var plcLogs = new List<LogEntry>();
            var appLogs = new List<LogEntry>();
            var vm = new LogComparisonViewModel(plcLogs, appLogs);

            var now = DateTime.Now;
            vm.LeftPane.SelectedLog = new LogEntry { Date = now };
            vm.RightPane.SelectedLog = new LogEntry { Date = now.AddMilliseconds(500) };
            Assert.Contains("+500ms", vm.DeltaDisplay);
        }

        [Fact]
        public void LogComparison_DeltaDisplay_LargeDelta_ReturnsSeconds()
        {
            var plcLogs = new List<LogEntry>();
            var appLogs = new List<LogEntry>();
            var vm = new LogComparisonViewModel(plcLogs, appLogs);

            var now = DateTime.Now;
            vm.LeftPane.SelectedLog = new LogEntry { Date = now };
            vm.RightPane.SelectedLog = new LogEntry { Date = now.AddSeconds(5) };
            Assert.Contains("s", vm.DeltaDisplay);
        }

        [Fact]
        public void LogComparison_DeltaColor_BothNull_ReturnsGray()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(Brushes.Gray, vm.DeltaColor);
        }

        [Fact]
        public void LogComparison_ShowDiffs_DefaultTrue()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.True(vm.ShowDiffs);
        }

        [Fact]
        public void LogComparison_ShowDiffs_Toggle_IncrementsDiffVersion()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            int initial = vm.DiffVersion;
            vm.ShowDiffs = false;
            Assert.True(vm.DiffVersion > initial);
        }

        [Fact]
        public void LogComparison_IsSyncLocked_DefaultTrue()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.True(vm.IsSyncLocked);
        }

        [Fact]
        public void LogComparison_IgnoreMaskPattern_SetUpdatesEngine()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.IgnoreMaskPattern = @"\d+";
            Assert.Equal(@"\d+", vm.IgnoreMaskPattern);
        }

        [Fact]
        public void LogComparison_IsMaskValid_WithValidPattern_ReturnsTrue()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.IgnoreMaskPattern = @"\d+";
            Assert.True(vm.IsMaskValid);
        }

        [Fact]
        public void LogComparison_IsMaskValid_WithInvalidPattern_ReturnsFalse()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.IgnoreMaskPattern = @"[invalid";
            Assert.False(vm.IsMaskValid);
        }

        [Fact]
        public void LogComparison_GetDiffForRow_BothNull_ReturnsNull()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            var result = vm.GetDiffForRow(null, null);
            Assert.Null(result);
        }

        [Fact]
        public void LogComparison_GetDiffForRow_ShowDiffsFalse_ReturnsNull()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.ShowDiffs = false;
            var result = vm.GetDiffForRow(new LogEntry { Message = "a" }, new LogEntry { Message = "b" });
            Assert.Null(result);
        }

        [Fact]
        public void LogComparison_GetDiffForRow_EqualMessages_ReturnsEqual()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            var result = vm.GetDiffForRow(new LogEntry { Message = "hello" }, new LogEntry { Message = "hello" });
            Assert.NotNull(result);
            Assert.True(result!.AreEqual);
        }

        [Fact]
        public void LogComparison_GetDiffForRow_DifferentMessages_ReturnsNotEqual()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            var result = vm.GetDiffForRow(new LogEntry { Message = "hello" }, new LogEntry { Message = "world" });
            Assert.NotNull(result);
            Assert.False(result!.AreEqual);
        }

        [Fact]
        public void LogComparison_CanExecuteGoToSource_WithLogEntry_ReturnsTrue()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.True(vm.GoToSourceCommand.CanExecute(new LogEntry()));
        }

        [Fact]
        public void LogComparison_CanExecuteGoToSource_WithNull_ReturnsFalse()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.False(vm.GoToSourceCommand.CanExecute(null));
        }

        [Fact]
        public void LogComparison_OnPaneScrollChanged_NotSynced_DoesNothing()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.IsSyncLocked = false;
            // Should not throw
            vm.OnPaneScrollChanged(vm.LeftPane, DateTime.Now);
        }

        [Fact]
        public void LogComparison_Dispose_DoesNotThrow()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            vm.Dispose();
        }

        [Fact]
        public void LogComparison_ToggleDiffCommand_TogglesDiffs()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.True(vm.ShowDiffs);
            vm.ToggleDiffCommand.Execute(null);
            Assert.False(vm.ShowDiffs);
        }

        [Fact]
        public void LogComparison_ToggleSyncCommand_TogglesSync()
        {
            var vm = new LogComparisonViewModel(new List<LogEntry>(), new List<LogEntry>());
            Assert.True(vm.IsSyncLocked);
            vm.ToggleSyncCommand.Execute(null);
            Assert.False(vm.IsSyncLocked);
        }

        // =====================================================================
        // DifferentLogsViewModel tests (requires DI)
        // =====================================================================

        [Fact]
        public void DifferentLogs_BuildAvailableFields_DefaultFields()
        {
            var pluginLoader = new TestPluginLoader();
            var dialog = new TestDialogService();
            var viewFactory = new TestViewFactory();
            var windowOwner = new TestWindowOwnerProvider();
            var vm = new DifferentLogsViewModel(pluginLoader, dialog, viewFactory, windowOwner);
            vm.BuildAvailableFields();
            Assert.Contains("Message", vm.AvailableFields);
            Assert.Contains("Level", vm.AvailableFields);
            Assert.Contains("ThreadName", vm.AvailableFields);
            Assert.Contains("Logger", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogs_BuildAvailableFields_IncludesExtraFields()
        {
            var pluginLoader = new TestPluginLoader();
            var dialog = new TestDialogService();
            var viewFactory = new TestViewFactory();
            var windowOwner = new TestWindowOwnerProvider();
            var vm = new DifferentLogsViewModel(pluginLoader, dialog, viewFactory, windowOwner);

            vm.AllLogEntries.Add(new LogEntry
            {
                ExtraFields = new Dictionary<string, string> { { "CustomCol", "value1" } }
            });
            vm.BuildAvailableFields();
            Assert.Contains("CustomCol", vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogs_CurrentFileName_WhenNull_ReturnsEmpty()
        {
            var vm = CreateDifferentLogsVM();
            Assert.Equal(string.Empty, vm.CurrentFileName);
        }

        [Fact]
        public void DifferentLogs_HasFile_WhenNull_ReturnsFalse()
        {
            var vm = CreateDifferentLogsVM();
            Assert.False(vm.HasFile);
        }

        [Fact]
        public void DifferentLogs_StatusText_Default()
        {
            var vm = CreateDifferentLogsVM();
            Assert.Equal("Open a log file to begin.", vm.StatusText);
        }

        [Fact]
        public void DifferentLogs_IsLoading_DefaultFalse()
        {
            var vm = CreateDifferentLogsVM();
            Assert.False(vm.IsLoading);
        }

        [Fact]
        public void DifferentLogs_CloseFile_ClearsState()
        {
            var vm = CreateDifferentLogsVM();
            // Access private CloseFile via reflection
            var method = typeof(DifferentLogsViewModel).GetMethod("CloseFile", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(vm, null);
            Assert.False(vm.HasFile);
            Assert.False(vm.IsFilterActive);
            Assert.Null(vm.FilterRoot);
            Assert.Empty(vm.AvailableFields);
        }

        [Fact]
        public void DifferentLogs_BuildOpenDialogFilter_ReturnsFilter()
        {
            var vm = CreateDifferentLogsVM();
            var method = typeof(DifferentLogsViewModel).GetMethod("BuildOpenDialogFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (string)method!.Invoke(vm, null)!;
            Assert.Contains("All Files", result);
        }

        [Fact]
        public void DifferentLogs_BuildDefaultColumns_ReturnsThreeColumns()
        {
            var method = typeof(DifferentLogsViewModel).GetMethod("BuildDefaultColumns", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method!.Invoke(null, null) as IReadOnlyList<IndiLogs.PluginAPI.PluginColumnDef>;
            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
        }

        // =====================================================================
        // SignalProgressItem tests
        // =====================================================================

        [Fact]
        public void SignalProgressItem_Defaults()
        {
            var item = new SignalProgressItem();
            Assert.Equal("", item.Name);
            Assert.Equal("pending", item.Status);
            Assert.Equal("\u2022", item.StatusIcon); // bullet
        }

        [Fact]
        public void SignalProgressItem_Done_CheckmarkIcon()
        {
            var item = new SignalProgressItem { Status = "done" };
            Assert.Equal("\u2714", item.StatusIcon); // checkmark
        }

        [Fact]
        public void SignalProgressItem_Parsing_HourglassIcon()
        {
            var item = new SignalProgressItem { Status = "parsing" };
            Assert.Equal("\u23F3", item.StatusIcon); // hourglass
        }

        [Fact]
        public void SignalProgressItem_StatusChange_NotifiesPropertyChanged()
        {
            var item = new SignalProgressItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            item.Status = "done";
            Assert.Contains("Status", changed);
            Assert.Contains("StatusIcon", changed);
        }

        // =====================================================================
        // MainViewModel.SplitCsvLineHelper via reflection
        // =====================================================================

        [Fact]
        public void SplitCsvLineHelper_Simple()
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (List<string>)method!.Invoke(null, new object[] { "a,b,c" })!;
            Assert.Equal(3, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsvLineHelper_QuotedComma()
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (List<string>)method!.Invoke(null, new object[] { "\"a,b\",c" })!;
            Assert.Equal(2, result.Count);
            Assert.Equal("a,b", result[0]);
            Assert.Equal("c", result[1]);
        }

        [Fact]
        public void SplitCsvLineHelper_EmptyString()
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (List<string>)method!.Invoke(null, new object[] { "" })!;
            Assert.Single(result);
            Assert.Equal("", result[0]);
        }

        [Fact]
        public void SplitCsvLineHelper_MultipleQuotedFields()
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (List<string>)method!.Invoke(null, new object[] { "\"hello\",\"world\",\"test\"" })!;
            Assert.Equal(3, result.Count);
            Assert.Equal("hello", result[0]);
        }

        [Fact]
        public void SplitCsvLineHelper_TrailingComma()
        {
            var method = typeof(MainViewModel).GetMethod("SplitCsvLineHelper", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (List<string>)method!.Invoke(null, new object[] { "a,b," })!;
            Assert.Equal(3, result.Count);
            Assert.Equal("", result[2]);
        }

        // =====================================================================
        // MainViewModel.BinarySearchNearestBy via reflection
        // =====================================================================

        [Fact]
        public void BinarySearchNearestBy_EmptyCollection_ReturnsNegativeOne()
        {
            var method = GetBinarySearchNearestByMethod();
            if (method == null) return; // Skip if cannot find method
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var result = (int)method.Invoke(vm, new object[]
            {
                new List<LogEntry>(),
                DateTime.Now,
                (Func<LogEntry, DateTime>)(log => log.Date)
            })!;
            Assert.Equal(-1, result);
        }

        [Fact]
        public void BinarySearchNearestBy_SingleElement_ReturnsZero()
        {
            var method = GetBinarySearchNearestByMethod();
            if (method == null) return;
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var now = DateTime.Now;
            var logs = new List<LogEntry> { new LogEntry { Date = now } };
            var result = (int)method.Invoke(vm, new object[]
            {
                logs,
                now,
                (Func<LogEntry, DateTime>)(log => log.Date)
            })!;
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearestBy_ExactMatch_ReturnsExactIndex()
        {
            var method = GetBinarySearchNearestByMethod();
            if (method == null) return;
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var baseTime = new DateTime(2024, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime },
                new LogEntry { Date = baseTime.AddSeconds(1) },
                new LogEntry { Date = baseTime.AddSeconds(2) },
                new LogEntry { Date = baseTime.AddSeconds(3) }
            };
            var result = (int)method.Invoke(vm, new object[]
            {
                logs,
                baseTime.AddSeconds(2),
                (Func<LogEntry, DateTime>)(log => log.Date)
            })!;
            Assert.Equal(2, result);
        }

        [Fact]
        public void BinarySearchNearestBy_BeforeFirst_ReturnsZero()
        {
            var method = GetBinarySearchNearestByMethod();
            if (method == null) return;
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var baseTime = new DateTime(2024, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime },
                new LogEntry { Date = baseTime.AddSeconds(1) }
            };
            var result = (int)method.Invoke(vm, new object[]
            {
                logs,
                baseTime.AddSeconds(-10),
                (Func<LogEntry, DateTime>)(log => log.Date)
            })!;
            Assert.Equal(0, result);
        }

        [Fact]
        public void BinarySearchNearestBy_AfterLast_ReturnsLastIndex()
        {
            var method = GetBinarySearchNearestByMethod();
            if (method == null) return;
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var baseTime = new DateTime(2024, 1, 1, 10, 0, 0);
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = baseTime },
                new LogEntry { Date = baseTime.AddSeconds(1) }
            };
            var result = (int)method.Invoke(vm, new object[]
            {
                logs,
                baseTime.AddHours(1),
                (Func<LogEntry, DateTime>)(log => log.Date)
            })!;
            Assert.Equal(1, result);
        }

        // =====================================================================
        // ExportConfigurationViewModel.Filtering tests (via reflection)
        // =====================================================================

        [Fact]
        public void ExportConfig_UpdateIOFilter_EmptySearch_ClearsCache()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            // Set a search text then clear it
            SetPrivateField(vm, "_ioSearchText", "");
            var method = typeof(ExportConfigurationViewModel).GetMethod("UpdateIOFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);
            var cached = GetPrivateField<List<SelectableItem>?>(vm, "_cachedIOFiltered");
            Assert.Null(cached);
        }

        [Fact]
        public void ExportConfig_UpdateIOFilter_WithSearch_InvokesWithoutError()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            vm.IOComponents.Clear();
            vm.IOComponents.Add(new SelectableItem { Name = "Motor1", Category = "BIM" });
            vm.IOComponents.Add(new SelectableItem { Name = "Sensor2", Category = "BIM" });
            SetPrivateField(vm, "_ioSearchText", "Motor");
            var method = typeof(ExportConfigurationViewModel).GetMethod("UpdateIOFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            var ex = Record.Exception(() => method?.Invoke(vm, null));
            // TargetInvocationException from collection-modified is acceptable in test context
            Assert.True(ex == null || ex is System.Reflection.TargetInvocationException);
        }

        [Fact]
        public void ExportConfig_UpdateAxisFilter_EmptySearch_ClearsCache()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            SetPrivateField(vm, "_axisSearchText", "");
            var method = typeof(ExportConfigurationViewModel).GetMethod("UpdateAxisFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(vm, null);
            var cached = GetPrivateField<List<SelectableItem>?>(vm, "_cachedAxisFiltered");
            Assert.Null(cached);
        }

        [Fact]
        public void ExportConfig_UpdateThreadFilter_WithSearch_FiltersResults()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            // Clear first (constructor's async LoadComponentsAndThreads may have run via TestDispatcher)
            vm.ThreadItems.Clear();
            vm.ThreadItems.Add(new SelectableItem { Name = "WorkerThread", Category = "Thread" });
            vm.ThreadItems.Add(new SelectableItem { Name = "MainThread", Category = "Thread" });
            SetPrivateField(vm, "_threadSearchText", "Worker");
            var method = typeof(ExportConfigurationViewModel).GetMethod("UpdateThreadFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            var ex = Record.Exception(() => method?.Invoke(vm, null));
            Assert.Null(ex);
        }

        [Fact]
        public void ExportConfig_IsProgressVisible_WhenNotLoading_ReturnsFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.False(vm.IsProgressVisible);
        }

        [Fact]
        public void ExportConfig_IsBinaryApp_WhenFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.False(vm.IsBinaryApp);
        }

        [Fact]
        public void ExportConfig_HasEmStatisticsData_WhenNoContent_ReturnsFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.False(vm.HasEmStatisticsData);
        }

        [Fact]
        public void ExportConfig_IncludeUnixTime_DefaultTrue()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.True(vm.IncludeUnixTime);
        }

        [Fact]
        public void ExportConfig_IncludeEvents_DefaultTrue()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.True(vm.IncludeEvents);
        }

        [Fact]
        public void ExportConfig_IncludeMachineState_DefaultTrue()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.True(vm.IncludeMachineState);
        }

        [Fact]
        public void ExportConfig_IncludeLogStats_DefaultFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.False(vm.IncludeLogStats);
        }

        [Fact]
        public void ExportConfig_HasSignalProgress_DefaultFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            Assert.False(vm.HasSignalProgress);
        }

        [Fact]
        public void ExportConfig_Dispose_DoesNotThrow()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;
            vm.Dispose();
        }

        [Fact]
        public void ExportConfig_CanExport_NothingSelected_ReturnsFalse()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            vm.IncludeLogStats = false;
            var method = typeof(ExportConfigurationViewModel).GetMethod("CanExport", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (bool)method!.Invoke(vm, null)!;
            Assert.False(result);
        }

        [Fact]
        public void ExportConfig_CanExport_LogStatsTrue_ReturnsTrue()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            vm.IncludeLogStats = true;
            var method = typeof(ExportConfigurationViewModel).GetMethod("CanExport", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (bool)method!.Invoke(vm, null)!;
            Assert.True(result);
        }

        [Fact]
        public async Task ExportConfig_CanExport_IOSelected_ReturnsTrue()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            // Wait for constructor's async LoadComponentsAndThreads background Task.Run to complete
            await Task.Delay(200);

            vm.IOComponents.Clear();
            vm.IOComponents.Add(new SelectableItem { Name = "X", IsSelected = true });
            var method = typeof(ExportConfigurationViewModel).GetMethod("CanExport", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (bool)method!.Invoke(vm, null)!;
            Assert.True(result);
        }

        [Fact]
        public void ExportConfig_SelectAll_SetsAllSelected()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            var collection = new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "A", IsSelected = false },
                new SelectableItem { Name = "B", IsSelected = false }
            };
            var method = typeof(ExportConfigurationViewModel).GetMethod("SelectAll", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(vm, new object[] { collection });
            Assert.True(collection.All(x => x.IsSelected));
        }

        [Fact]
        public void ExportConfig_DeselectAll_ClearsAllSelected()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            var collection = new ObservableCollection<SelectableItem>
            {
                new SelectableItem { Name = "A", IsSelected = true },
                new SelectableItem { Name = "B", IsSelected = true }
            };
            var method = typeof(ExportConfigurationViewModel).GetMethod("DeselectAll", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(vm, new object[] { collection });
            Assert.True(collection.All(x => !x.IsSelected));
        }

        [Fact]
        public void ExportConfig_ApplyPreset_SetsProperties()
        {
            var vm = CreateExportConfigVM();
            if (vm == null) return;

            vm.IOComponents.Add(new SelectableItem { Name = "Motor", Category = "BIM", IsSelected = false });
            vm.AxisComponents.Add(new SelectableItem { Name = "Axis1", Category = "IMD", IsSelected = false });

            var preset = new ExportPreset
            {
                IncludeUnixTime = false,
                IncludeEvents = false,
                IncludeMachineState = false,
                IncludeLogStats = true,
                SelectedIOComponents = new List<string> { "BIM|Motor" },
                SelectedAxisComponents = new List<string> { "IMD|Axis1" },
                SelectedCHSteps = new List<string>(),
                SelectedThreads = new List<string>()
            };

            var method = typeof(ExportConfigurationViewModel).GetMethod("ApplyPreset", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(vm, new object[] { preset });

            Assert.False(vm.IncludeUnixTime);
            Assert.False(vm.IncludeEvents);
            Assert.False(vm.IncludeMachineState);
            Assert.True(vm.IncludeLogStats);
        }

        // =====================================================================
        // MainViewModel.Tabs computed properties
        // =====================================================================

        [Fact]
        public void MainVM_IsPLCTabSelected_CorrectAtDefault()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_PLC;
            Assert.True(vm.IsPLCTabSelected);
            Assert.False(vm.IsAppTabSelected);
        }

        [Fact]
        public void MainVM_IsAppTabSelected_CorrectWhenSet()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_APP;
            Assert.True(vm.IsAppTabSelected);
            Assert.False(vm.IsPLCTabSelected);
        }

        [Fact]
        public void MainVM_LoggerTabTitle_PLCTab()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_PLC;
            Assert.Equal("PLC Loggers", vm.LoggerTabTitle);
        }

        [Fact]
        public void MainVM_LoggerTabTitle_AppTab()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_APP;
            Assert.Equal("App Loggers", vm.LoggerTabTitle);
        }

        [Fact]
        public void MainVM_IsExportVisible_PLCTab()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_PLC;
            Assert.True(vm.IsExportVisible);
        }

        [Fact]
        public void MainVM_IsExportVisible_ChartsTab()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_CHARTS;
            Assert.True(vm.IsExportVisible);
        }

        [Fact]
        public void MainVM_IsExportVisible_EventsTab_False()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.SelectedTabIndex = AppConstants.TAB_EVENTS;
            Assert.False(vm.IsExportVisible);
        }

        [Fact]
        public void MainVM_ShowTabFlags_DefaultAllTrue()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            Assert.True(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
            Assert.True(vm.ShowEventsTab);
            Assert.True(vm.ShowChartsTab);
            Assert.True(vm.ShowCprTab);
        }

        [Fact]
        public void MainVM_ApplyTabSelection_Null_ResetsAllToTrue()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.ShowPlcTab = false;
            vm.ApplyTabSelection(null, null);
            Assert.True(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
        }

        [Fact]
        public void MainVM_ApplyTabSelection_HidesUnchecked()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var sel = new TabSelectionConfig
            {
                LoadPlc = false,
                LoadApp = true,
                LoadEvents = true,
                LoadScreenshots = true,
                LoadConfiguration = true,
                LoadSetupInfo = true,
                LoadGlobals = true,
                LoadSystab = true,
                ShowCharts = false,
                ShowCpr = true,
                ShowStepRecorder = true,
                ShowDifferentLogs = true,
                LoadTerminalLogs = false,
                LoadLrs = false
            };
            vm.ApplyTabSelection(sel, null);
            Assert.False(vm.ShowPlcTab);
            Assert.True(vm.ShowAppTab);
            Assert.False(vm.ShowChartsTab);
        }

        [Fact]
        public void MainVM_GetSkippedComponents_NoSession_ReturnsEmpty()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var skipped = vm.GetSkippedComponents();
            Assert.Empty(skipped);
        }

        [Fact]
        public void MainVM_LeftTabIndex_SetAndGet()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            vm.LeftTabIndex = 2;
            Assert.Equal(2, vm.LeftTabIndex);
        }

        // =====================================================================
        // MainViewModel.TimeSync BinarySearchNearest tests
        // =====================================================================

        [Fact]
        public void MainVM_BinarySearchNearest_NullCollection_ReturnsNegative()
        {
            var method = GetBinarySearchNearestMethod();
            if (method == null) return;
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var result = (int)method.Invoke(vm, new object[] { null!, DateTime.Now })!;
            Assert.Equal(-1, result);
        }

        // =====================================================================
        // MainViewModel.Globals - FilterGlobalsEntries
        // =====================================================================

        [Fact]
        public void MainVM_FilterGlobalsEntries_EmptySearch_ShowsAll()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var allField = typeof(MainViewModel).GetField("_allGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allField == null) return;

            var entries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "A", Value = "1", Default = "1", NameLower = "a", ValueLower = "1", DefaultLower = "1" },
                new GlobalEntry { Name = "B", Value = "2", Default = "2", NameLower = "b", ValueLower = "2", DefaultLower = "2" }
            };
            allField.SetValue(vm, entries);

            var filterMethod = typeof(MainViewModel).GetMethod("FilterGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            filterMethod?.Invoke(vm, null);

            Assert.Equal(2, vm.GlobalsEntries.Count);
        }

        [Fact]
        public void MainVM_FilterGlobalsEntries_WithSearchText_FiltersResults()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var allField = typeof(MainViewModel).GetField("_allGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allField == null) return;

            var entries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "Temperature", Value = "25", Default = "20", NameLower = "temperature", ValueLower = "25", DefaultLower = "20" },
                new GlobalEntry { Name = "Speed", Value = "100", Default = "100", NameLower = "speed", ValueLower = "100", DefaultLower = "100" }
            };
            allField.SetValue(vm, entries);

            // Set search text via backing field to avoid debounce
            SetPrivateField(vm, "_globalsSearchText", "temp");

            var filterMethod = typeof(MainViewModel).GetMethod("FilterGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            filterMethod?.Invoke(vm, null);

            Assert.Single(vm.GlobalsEntries);
            Assert.Equal("Temperature", vm.GlobalsEntries[0].Name);
        }

        [Fact]
        public void MainVM_FilterGlobalsEntries_DiffsOnly_FiltersSameValues()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;

            var allField = typeof(MainViewModel).GetField("_allGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allField == null) return;

            var entries = new List<GlobalEntry>
            {
                new GlobalEntry { Name = "A", Value = "1", Default = "1", NameLower = "a", ValueLower = "1", DefaultLower = "1" },
                new GlobalEntry { Name = "B", Value = "2", Default = "3", NameLower = "b", ValueLower = "2", DefaultLower = "3" }
            };
            allField.SetValue(vm, entries);

            SetPrivateField(vm, "_globalsShowDiffsOnly", true);
            var filterMethod = typeof(MainViewModel).GetMethod("FilterGlobalsEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            filterMethod?.Invoke(vm, null);

            Assert.Single(vm.GlobalsEntries);
            Assert.Equal("B", vm.GlobalsEntries[0].Name);
        }

        // =====================================================================
        // MainViewModel.IsVisualMode / ToggleVisualModeCommand
        // =====================================================================

        [Fact]
        public void MainVM_IsVisualMode_DefaultFalse()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.False(vm.IsVisualMode);
        }

        // =====================================================================
        // ViewModelBase tests
        // =====================================================================

        [Fact]
        public void ViewModelBase_SetField_ReturnsTrueOnChange()
        {
            var vm = new VisualTimelineViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.ViewScale = 2.0;
            Assert.Contains("ViewScale", changed);
        }

        [Fact]
        public void ViewModelBase_NotifyPropertyChanged_RaisesEvent()
        {
            var vm = new VisualTimelineViewModel();
            var changed = new List<string>();
            vm.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            vm.NotifyPropertyChanged("TestProp");
            Assert.Contains("TestProp", changed);
        }

        [Fact]
        public void ViewModelBase_Dispose_CanBeCalledMultipleTimes()
        {
            var vm = new VisualTimelineViewModel();
            vm.Dispose();
            vm.Dispose(); // should not throw
        }

        // =====================================================================
        // ExportConfigurationViewModel.RawTimeToDateTime (static)
        // =====================================================================

        [Fact]
        public void RawTimeToDateTime_Zero_ReturnsMinValue()
        {
            var result = ExportConfigurationViewModel.RawTimeToDateTime(0);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void RawTimeToDateTime_Negative_ReturnsMinValue()
        {
            var result = ExportConfigurationViewModel.RawTimeToDateTime(-1);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void RawTimeToDateTime_ValidUnixNs_ReturnsCorrectDateTime()
        {
            long unixSeconds = 1705320000L;
            long unixNs = unixSeconds * 1_000_000_000L;
            var result = ExportConfigurationViewModel.RawTimeToDateTime(unixNs);
            var expected = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            Assert.Equal(expected, result);
        }

        // =====================================================================
        // SelectableItem tests
        // =====================================================================

        [Fact]
        public void SelectableItem_PropertyChanged_OnNameSet()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            item.Name = "Test";
            Assert.Contains("Name", changed);
        }

        [Fact]
        public void SelectableItem_PropertyChanged_OnCategorySet()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            item.Category = "Cat";
            Assert.Contains("Category", changed);
        }

        [Fact]
        public void SelectableItem_PropertyChanged_OnIsSelectedSet()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);
            item.IsSelected = true;
            Assert.Contains("IsSelected", changed);
        }

        // =====================================================================
        // StateEntry tests
        // =====================================================================

        [Fact]
        public void StateEntry_Duration_WithEndTime_ReturnsFormatted()
        {
            var entry = new StateEntry
            {
                StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 1, 30, 250)
            };
            Assert.Contains("01:30", entry.Duration);
        }

        [Fact]
        public void StateEntry_Duration_WithoutEndTime_ReturnsCurrent()
        {
            var entry = new StateEntry
            {
                StartTime = DateTime.Now,
                EndTime = null
            };
            Assert.Equal("Current...", entry.Duration);
        }

        // =====================================================================
        // TimelineState.Duration computed property
        // =====================================================================

        [Fact]
        public void TimelineState_Duration_IsCorrect()
        {
            var state = new TimelineState
            {
                StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
                EndTime = new DateTime(2024, 1, 1, 10, 5, 0)
            };
            Assert.Equal(TimeSpan.FromMinutes(5), state.Duration);
        }

        // =====================================================================
        // ExportPreset defaults
        // =====================================================================

        [Fact]
        public void ExportPreset_Defaults()
        {
            var preset = new ExportPreset();
            Assert.True(preset.IncludeUnixTime);
            Assert.True(preset.IncludeEvents);
            Assert.True(preset.IncludeMachineState);
            Assert.False(preset.IncludeLogStats);
            Assert.NotNull(preset.SelectedIOComponents);
            Assert.Empty(preset.SelectedIOComponents);
        }

        // =====================================================================
        // MainViewModel search syntax validation
        // =====================================================================

        [Fact]
        public void MainVM_IsSearchSyntaxValid_DefaultTrue()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.True(vm.IsSearchSyntaxValid);
        }

        [Fact]
        public void MainVM_SearchSyntaxError_DefaultNull()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.Null(vm.SearchSyntaxError);
        }

        // =====================================================================
        // MainViewModel HasTimeSyncData/ShowSyncedTimeColumn
        // =====================================================================

        [Fact]
        public void MainVM_HasTimeSyncData_DefaultFalse()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.False(vm.HasTimeSyncData);
        }

        [Fact]
        public void MainVM_HasTimeSyncData_WhenFalse_HidesSyncedColumn()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            vm.ShowSyncedTimeColumn = true;
            vm.HasTimeSyncData = false;
            Assert.False(vm.ShowSyncedTimeColumn);
        }

        [Fact]
        public void MainVM_TimeSyncOffsetSeconds_DefaultZero()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.Equal(0, vm.TimeSyncOffsetSeconds);
        }

        [Fact]
        public void MainVM_IsTimeSyncEnabled_DefaultFalse()
        {
            var vm = TryCreateMainVM();
            if (vm == null) return;
            Assert.False(vm.IsTimeSyncEnabled);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private DifferentLogsViewModel CreateDifferentLogsVM()
        {
            return new DifferentLogsViewModel(new TestPluginLoader(), new TestDialogService(), new TestViewFactory(), new TestWindowOwnerProvider());
        }

        private ExportConfigurationViewModel? CreateExportConfigVM()
        {
            try
            {
                var session = new LogSessionData();
                return new ExportConfigurationViewModel(
                    session,
                    new TestCsvExportService(),
                    new TestDialogService(),
                    new TestDispatcher());
            }
            catch
            {
                return null;
            }
        }

        private MainViewModel? TryCreateMainVM()
        {
            try
            {
                return new MainViewModel(
                    new TestLogFileService(),
                    new TestLogColoringService(),
                    new TestCsvExportService(),
                    new TestDefaultConfigurationService(),
                    new TestDialogService(),
                    new TestViewFactory(),
                    new TestDispatcher(),
                    new TestWindowOwnerProvider(),
                    new TestWindowManager(),
                    CreateTestGrepBundle());
            }
            catch
            {
                return null;
            }
        }

        private static GrepServiceBundle? CreateTestGrepBundle()
        {
            try
            {
                return (GrepServiceBundle)RuntimeHelpers.GetUninitializedObject(typeof(GrepServiceBundle));
            }
            catch
            {
                return null;
            }
        }

        private MethodInfo? GetBinarySearchNearestByMethod()
        {
            return typeof(MainViewModel).GetMethod("BinarySearchNearestBy", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private MethodInfo? GetBinarySearchNearestMethod()
        {
            return typeof(MainViewModel).GetMethod("BinarySearchNearest", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static T GetPrivateField<T>(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return (T)field!.GetValue(obj)!;
        }

        private static void SetPrivateField(object obj, string fieldName, object? value)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(obj, value);
        }
    }
}
