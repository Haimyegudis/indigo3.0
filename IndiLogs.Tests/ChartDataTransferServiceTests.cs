using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;
using Xunit;

namespace IndiLogs.Tests
{
    public class ChartDataTransferServiceTests
    {
        private static ChartDataTransferService Svc => ChartDataTransferService.Instance;

        #region Helpers

        private static LogEntry MakeLog(DateTime date, string message, string threadName = "")
            => new() { Date = date, Message = message, ThreadName = threadName };

        private static DateTime T(int seconds) => new DateTime(2025, 1, 1, 0, 0, 0).AddSeconds(seconds);

        private static ExportPreset EmptyPreset() => new()
        {
            SelectedIOComponents = new List<string>(),
            SelectedAxisComponents = new List<string>(),
            SelectedCHSteps = new List<string>(),
            SelectedThreads = new List<string>(),
            IncludeMachineState = false,
            IncludeEvents = false
        };

        // Reflection helper for private static TryParseDoubleFast
        private static readonly MethodInfo TryParseDoubleFastMethod =
            typeof(ChartDataTransferService).GetMethod("TryParseDoubleFast",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool CallTryParseDoubleFast(string s, int start, int length, out double result)
        {
            var args = new object[] { s, start, length, 0.0 };
            bool ok = (bool)TryParseDoubleFastMethod.Invoke(null, args)!;
            result = (double)args[3];
            return ok;
        }

        // Reflection helper for private static InternString
        private static readonly MethodInfo InternStringMethod =
            typeof(ChartDataTransferService).GetMethod("InternString",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static string CallInternString(Dictionary<string, string> pool, string source, int start, int length)
            => (string)InternStringMethod.Invoke(null, new object[] { pool, source, start, length })!;

        #endregion

        #region TryParseDoubleFast

        [Fact]
        public void TryParseDoubleFast_PositiveInteger()
        {
            Assert.True(CallTryParseDoubleFast("12345", 0, 5, out double val));
            Assert.Equal(12345.0, val);
        }

        [Fact]
        public void TryParseDoubleFast_NegativeInteger()
        {
            Assert.True(CallTryParseDoubleFast("-42", 0, 3, out double val));
            Assert.Equal(-42.0, val);
        }

        [Fact]
        public void TryParseDoubleFast_Decimal()
        {
            Assert.True(CallTryParseDoubleFast("3.14", 0, 4, out double val));
            Assert.Equal(3.14, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_NegativeDecimal()
        {
            Assert.True(CallTryParseDoubleFast("-0.5", 0, 4, out double val));
            Assert.Equal(-0.5, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_LeadingPlus()
        {
            Assert.True(CallTryParseDoubleFast("+7.25", 0, 5, out double val));
            Assert.Equal(7.25, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_ScientificNotation_Fallback()
        {
            Assert.True(CallTryParseDoubleFast("1.5e3", 0, 5, out double val));
            Assert.Equal(1500.0, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_ScientificNegativeExponent()
        {
            Assert.True(CallTryParseDoubleFast("2.5E-2", 0, 6, out double val));
            Assert.Equal(0.025, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_ZeroLength_ReturnsFalse()
        {
            Assert.False(CallTryParseDoubleFast("123", 0, 0, out _));
        }

        [Fact]
        public void TryParseDoubleFast_NonNumeric_ReturnsFalse()
        {
            Assert.False(CallTryParseDoubleFast("abc", 0, 3, out _));
        }

        [Fact]
        public void TryParseDoubleFast_OnlySign_ReturnsFalse()
        {
            Assert.False(CallTryParseDoubleFast("-", 0, 1, out _));
        }

        [Fact]
        public void TryParseDoubleFast_Substring()
        {
            Assert.True(CallTryParseDoubleFast("xxx99.5yyy", 3, 4, out double val));
            Assert.Equal(99.5, val, 6);
        }

        [Fact]
        public void TryParseDoubleFast_Zero()
        {
            Assert.True(CallTryParseDoubleFast("0", 0, 1, out double val));
            Assert.Equal(0.0, val);
        }

        [Fact]
        public void TryParseDoubleFast_TrailingText_ReturnsFalse()
        {
            Assert.False(CallTryParseDoubleFast("12abc", 0, 5, out _));
        }

        [Fact]
        public void TryParseDoubleFast_DecimalOnly()
        {
            Assert.True(CallTryParseDoubleFast(".75", 0, 3, out double val));
            Assert.Equal(0.75, val, 6);
        }

        #endregion

        #region InternString

        [Fact]
        public void InternString_ReturnsSubstring()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string result = CallInternString(pool, "HelloWorld", 5, 5);
            Assert.Equal("World", result);
        }

        [Fact]
        public void InternString_ReusesExistingInstance()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string first = CallInternString(pool, "FooBar", 0, 3);
            string second = CallInternString(pool, "xxFooyy", 2, 3);
            Assert.Same(first, second);
        }

        [Fact]
        public void InternString_TrimsWhitespace()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string result = CallInternString(pool, "  abc  ", 0, 7);
            Assert.Equal("abc", result);
        }

        [Fact]
        public void InternString_CaseInsensitiveReuse()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string first = CallInternString(pool, "ABC", 0, 3);
            string second = CallInternString(pool, "abc", 0, 3);
            Assert.Same(first, second);
        }

        #endregion

        #region Singleton

        [Fact]
        public void Instance_ReturnsSameObject()
        {
            var a = ChartDataTransferService.Instance;
            var b = ChartDataTransferService.Instance;
            Assert.Same(a, b);
        }

        #endregion

        #region TransferDataToCharts / ClearCurrentData

        [Fact]
        public void TransferDataToCharts_SetsCurrentData()
        {
            var pkg = new ChartDataPackage { SessionName = "test" };
            Svc.TransferDataToCharts(pkg);
            Assert.Same(pkg, Svc.CurrentData);
        }

        [Fact]
        public void TransferDataToCharts_FiresOnDataReady()
        {
            ChartDataPackage? received = null;
            void handler(ChartDataPackage p) => received = p;
            Svc.OnDataReady += handler;
            try
            {
                var pkg = new ChartDataPackage { SessionName = "event_test" };
                Svc.TransferDataToCharts(pkg);
                Assert.Same(pkg, received);
            }
            finally
            {
                Svc.OnDataReady -= handler;
            }
        }

        [Fact]
        public void ClearCurrentData_NullsCurrentData()
        {
            Svc.TransferDataToCharts(new ChartDataPackage { SessionName = "clear_test" });
            Svc.ClearCurrentData();
            Assert.Null(Svc.CurrentData);
        }

        #endregion

        #region RequestSwitchToCharts

        [Fact]
        public void RequestSwitchToCharts_FiresEvent()
        {
            bool fired = false;
            void handler() => fired = true;
            Svc.OnSwitchToChartsRequested += handler;
            try
            {
                Svc.RequestSwitchToCharts();
                Assert.True(fired);
            }
            finally
            {
                Svc.OnSwitchToChartsRequested -= handler;
            }
        }

        #endregion

        #region NotifyLogTimeSelected / NotifyChartTimeSelected

        [Fact]
        public void NotifyLogTimeSelected_FiresEvent()
        {
            DateTime? received = null;
            void handler(DateTime t) => received = t;
            Svc.OnLogTimeSelected += handler;
            try
            {
                var now = DateTime.Now;
                Svc.NotifyLogTimeSelected(now);
                Assert.Equal(now, received);
            }
            finally
            {
                Svc.OnLogTimeSelected -= handler;
            }
        }

        [Fact]
        public void NotifyChartTimeSelected_FiresEvent()
        {
            DateTime? received = null;
            void handler(DateTime t) => received = t;
            Svc.OnChartTimeSelected += handler;
            try
            {
                var now = DateTime.Now;
                Svc.NotifyChartTimeSelected(now);
                Assert.Equal(now, received);
            }
            finally
            {
                Svc.OnChartTimeSelected -= handler;
            }
        }

        #endregion

        #region BuildDataPackage — empty / basic

        [Fact]
        public void BuildDataPackage_EmptyLogs_ReturnsEmptyPackage()
        {
            var pkg = Svc.BuildDataPackage(new List<LogEntry>(), EmptyPreset(), "empty");
            Assert.Equal("empty", pkg.SessionName);
            Assert.Empty(pkg.Signals);
            Assert.Empty(pkg.States);
            Assert.Empty(pkg.Events);
            Assert.Empty(pkg.ThreadMessages);
            Assert.Empty(pkg.TimeStamps);
        }

        [Fact]
        public void BuildDataPackage_NoSelectionsNoData_ReturnsTimestampsOnly()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "some message"),
                MakeLog(T(1), "another message"),
                MakeLog(T(2), "third message")
            };
            var pkg = Svc.BuildDataPackage(logs, EmptyPreset(), "ts_only");
            Assert.Equal(3, pkg.TimeStamps.Count);
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_DuplicateTimestamps_Deduplicated()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "msg1"),
                MakeLog(T(0), "msg2"),
                MakeLog(T(1), "msg3")
            };
            var pkg = Svc.BuildDataPackage(logs, EmptyPreset(), "dedup");
            Assert.Equal(2, pkg.TimeStamps.Count);
        }

        #endregion

        #region BuildDataPackage — IO signals

        [Fact]
        public void BuildDataPackage_IOSignals_ParsesCorrectly()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=10.5,SubSys1_Comp2=20.0"),
                MakeLog(T(1), "IO_Mon:SubSys1,SubSys1_Comp1=11.5"),
                MakeLog(T(2), "IO_Mon:SubSys1,SubSys1_Comp1=12.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_test");

            Assert.Single(pkg.Signals);
            var sig = pkg.Signals[0];
            Assert.Equal("IO", sig.Category);
            Assert.Equal("Comp1", sig.Name);
            Assert.Equal(3, sig.SparsePoints!.Count);
            Assert.Equal(10.5, sig.SparsePoints[0].Value);
            Assert.Equal(11.5, sig.SparsePoints[1].Value);
            Assert.Equal(12.0, sig.SparsePoints[2].Value);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_MultipleComponents()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=10.5,SubSys1_Comp2=20.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1", "SubSys1|Comp2" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_multi");
            Assert.Equal(2, pkg.Signals.Count);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_SkipsNewStatus()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=New Status Active")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_newstatus");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_IOShortFormat_Parses()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO:SubSys1,SubSys1_Comp1=5.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_short");
            Assert.Single(pkg.Signals);
            Assert.Equal(5.0, pkg.Signals[0].SparsePoints![0].Value);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_UnselectedSubsystem_Ignored()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=10.0"),
                MakeLog(T(1), "IO_Mon:SubSys2,SubSys2_Comp1=20.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_filter");
            Assert.Single(pkg.Signals);
            Assert.Equal("Comp1", pkg.Signals[0].Name);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_NegativeValue()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=-3.75")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_negative");
            Assert.Single(pkg.Signals);
            Assert.Equal(-3.75, pkg.Signals[0].SparsePoints![0].Value, 6);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_StripsTempSuffix()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Motor1_MotTemp=45.0"),
                MakeLog(T(1), "IO_Mon:SubSys1,SubSys1_Motor1_DrvTemp=50.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_temp");
            Assert.Equal(2, pkg.Signals.Count);
        }

        #endregion

        #region BuildDataPackage — Axis signals

        [Fact]
        public void BuildDataPackage_AxisSignals_ParsesAxisMon()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,Pos=100.5,Vel=200.0"),
                MakeLog(T(1), "AxisMon:Sub1,Motor1,Pos=101.0")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_test");

            var posSignals = pkg.Signals.Where(s => s.Name.Contains("Pos")).ToList();
            var velSignals = pkg.Signals.Where(s => s.Name.Contains("Vel")).ToList();
            Assert.Single(posSignals);
            Assert.Single(velSignals);
            Assert.Equal("Axis", posSignals[0].Category);
            Assert.Equal(2, posSignals[0].SparsePoints!.Count);
            Assert.Single(velSignals[0].SparsePoints!);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_AxMShortFormat()
        {
            // AxM: format with trailing bare value treated as Trigger
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxM:Sub1,Motor1,42.5")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_axm");
            Assert.Single(pkg.Signals);
            Assert.Contains("Trigger", pkg.Signals[0].Name);
            Assert.Equal(42.5, pkg.Signals[0].SparsePoints![0].Value, 6);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_LagERenaming()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,LagE=0.5")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_lage");
            Assert.Single(pkg.Signals);
            Assert.Contains("LagErr", pkg.Signals[0].Name);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_TrgRenaming()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,Trg=1.0")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_trg");
            Assert.Single(pkg.Signals);
            Assert.Contains("Trigger", pkg.Signals[0].Name);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_UnselectedMotor_Ignored()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,Pos=10.0"),
                MakeLog(T(1), "AxisMon:Sub1,Motor2,Pos=20.0")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_filter");
            Assert.Single(pkg.Signals);
            Assert.Contains("Motor1", pkg.Signals[0].Name);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_NonNumericValue_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,Param=notanumber")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1|Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_nan");
            Assert.Empty(pkg.Signals);
        }

        #endregion

        #region BuildDataPackage — CHStep states

        [Fact]
        public void BuildDataPackage_CHStepStates_ParsesCorrectly()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "CHStep:CH1,StepMsg, State 5 <Parent1, SubID, 0, 100, 0, 0>")
            };
            var preset = EmptyPreset();
            preset.SelectedCHSteps = new List<string> { "Parent1|CH1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "chstep_test");
            Assert.Single(pkg.States);
            Assert.Equal("CH1", pkg.States[0].Name);
            Assert.Equal("Parent1", pkg.States[0].Category);
            Assert.Single(pkg.States[0].Intervals);
            Assert.Equal(5, pkg.States[0].Intervals[0].StateId);
        }

        [Fact]
        public void BuildDataPackage_CHStepStates_MergesConsecutiveSameState()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "CHStep:CH1,Msg, State 5 <Parent1, SubID, 0, 100, 0, 0>"),
                MakeLog(T(1), "CHStep:CH1,Msg, State 5 <Parent1, SubID, 0, 100, 0, 0>"),
                MakeLog(T(2), "CHStep:CH1,Msg, State 7 <Parent1, SubID, 0, 100, 0, 0>")
            };
            var preset = EmptyPreset();
            preset.SelectedCHSteps = new List<string> { "Parent1|CH1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "chstep_merge");
            Assert.Single(pkg.States);
            // Merged: two intervals (5 merged, then 7)
            Assert.Equal(2, pkg.States[0].Intervals.Count);
        }

        [Fact]
        public void BuildDataPackage_CHStepStates_UnselectedKey_Ignored()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "CHStep:CH1,Msg, State 5 <Parent1, SubID, 0, 100, 0, 0>"),
                MakeLog(T(1), "CHStep:CH2,Msg, State 3 <Parent2, SubID, 0, 100, 0, 0>")
            };
            var preset = EmptyPreset();
            preset.SelectedCHSteps = new List<string> { "Parent1|CH1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "chstep_filter");
            Assert.Single(pkg.States);
            Assert.Equal("CH1", pkg.States[0].Name);
        }

        [Fact]
        public void BuildDataPackage_CHStepStates_MalformedMessage_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "CHStep:CH1,missing state bracket data")
            };
            var preset = EmptyPreset();
            preset.SelectedCHSteps = new List<string> { "Parent1|CH1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "chstep_bad");
            Assert.Empty(pkg.States);
        }

        #endregion

        #region BuildDataPackage — Thread messages

        [Fact]
        public void BuildDataPackage_ThreadMessages_ParsesCorrectly()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "Thread message 1", "Worker"),
                MakeLog(T(1), "Thread message 2", "Worker"),
                MakeLog(T(2), "Other thread msg", "Other")
            };
            var preset = EmptyPreset();
            preset.SelectedThreads = new List<string> { "Worker" };

            var pkg = Svc.BuildDataPackage(logs, preset, "thread_test");
            Assert.Equal(2, pkg.ThreadMessages.Count);
            Assert.All(pkg.ThreadMessages, m => Assert.Equal("Worker", m.ThreadName));
        }

        [Fact]
        public void BuildDataPackage_ThreadMessages_CaseInsensitiveMatch()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "msg", "worker")
            };
            var preset = EmptyPreset();
            preset.SelectedThreads = new List<string> { "WORKER" };

            var pkg = Svc.BuildDataPackage(logs, preset, "thread_case");
            Assert.Single(pkg.ThreadMessages);
        }

        [Fact]
        public void BuildDataPackage_ThreadMessages_EmptyThreadName_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "msg", "")
            };
            var preset = EmptyPreset();
            preset.SelectedThreads = new List<string> { "" };

            var pkg = Svc.BuildDataPackage(logs, preset, "thread_empty");
            Assert.Empty(pkg.ThreadMessages);
        }

        #endregion

        #region BuildDataPackage — Events

        [Fact]
        public void BuildDataPackage_Events_ParsesEnqueueEvent()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "Enqueue event MY_EVENT from SubSystem ParamA=1 [Warning]", "Events")
            };
            var preset = EmptyPreset();
            preset.IncludeEvents = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "event_test");
            Assert.Single(pkg.Events);
            Assert.Equal("MY_EVENT", pkg.Events[0].Name);
            Assert.Equal("SubSystem", pkg.Events[0].Description);
            Assert.Equal("Warning", pkg.Events[0].Severity);
        }

        [Fact]
        public void BuildDataPackage_Events_NonEventThread_Ignored()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "Enqueue event MY_EVENT from Sub [Info]", "NotEvents")
            };
            var preset = EmptyPreset();
            preset.IncludeEvents = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "event_filter");
            Assert.Empty(pkg.Events);
        }

        [Fact]
        public void BuildDataPackage_Events_NoEventKeyword_UsesMessageAsName()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "SomeDirectMessage", "Events")
            };
            var preset = EmptyPreset();
            preset.IncludeEvents = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "event_nokey");
            Assert.Single(pkg.Events);
            Assert.Equal("SomeDirectMessage", pkg.Events[0].Name);
        }

        [Fact]
        public void BuildDataPackage_Events_StateName_ExtractedFromStateKeyword()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "Enqueue event STATE_CHANGE from Sub [Error]", "Events")
            };
            var preset = EmptyPreset();
            preset.IncludeEvents = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "event_state");
            Assert.Single(pkg.Events);
            Assert.Equal("STATE_CHANGE", pkg.Events[0].State);
        }

        [Fact]
        public void BuildDataPackage_Events_EmptyMessage_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "", "Events")
            };
            var preset = EmptyPreset();
            preset.IncludeEvents = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "event_empty");
            Assert.Empty(pkg.Events);
        }

        #endregion

        #region BuildDataPackage — Machine State (S6)

        [Fact]
        public void BuildDataPackage_MachineState_S6_ParsesTransitions()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "PlcMngr: STANDBY -> GET_READY", "Manager"),
                MakeLog(T(1), "PlcMngr: GET_READY -> READY", "Manager"),
                MakeLog(T(2), "some other log")
            };
            var preset = EmptyPreset();
            preset.IncludeMachineState = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "state_s6");
            Assert.Single(pkg.States);
            Assert.Equal("MachineState", pkg.States[0].Name);
            Assert.Equal(2, pkg.States[0].Intervals.Count);
            Assert.Equal("GET_READY", pkg.States[0].Intervals[0].StateName);
            Assert.Equal("READY", pkg.States[0].Intervals[1].StateName);
        }

        [Fact]
        public void BuildDataPackage_MachineState_S6_WrongThread_Ignored()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "PlcMngr: STANDBY -> GET_READY", "NotManager")
            };
            var preset = EmptyPreset();
            preset.IncludeMachineState = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "state_wrongthread");
            Assert.Empty(pkg.States);
        }

        #endregion

        #region BuildDataPackage — Machine State (S4-5 fallback)

        [Fact]
        public void BuildDataPackage_MachineState_S4Fallback_ParsesEnterStates()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "==== STATE_STANDBY - Enter"),
                MakeLog(T(1), "==== STATE_STANDBY - Exit"),
                MakeLog(T(2), "==== STATE_PRINT - Enter")
            };
            var preset = EmptyPreset();
            preset.IncludeMachineState = true;

            var pkg = Svc.BuildDataPackage(logs, preset, "state_s4");
            Assert.Single(pkg.States);
            Assert.Equal("MachineState", pkg.States[0].Name);
            // Only Enter transitions create intervals
            Assert.Equal(2, pkg.States[0].Intervals.Count);
            Assert.Equal("STANDBY", pkg.States[0].Intervals[0].StateName);
            Assert.Equal("PRINT", pkg.States[0].Intervals[1].StateName);
        }

        #endregion

        #region BuildDataPackage — combined features

        [Fact]
        public void BuildDataPackage_AllFeatures_CombinesResults()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=10.0"),
                MakeLog(T(1), "AxisMon:Sub1,Motor1,Pos=50.0"),
                MakeLog(T(2), "CHStep:CH1,StepMsg, State 3 <Parent1, SubID, 0, 100, 0, 0>"),
                MakeLog(T(3), "Thread message", "Worker"),
                MakeLog(T(4), "Enqueue event ALERT from Engine [Info]", "Events"),
                MakeLog(T(5), "PlcMngr: OFF -> STANDBY", "Manager")
            };
            var preset = new ExportPreset
            {
                SelectedIOComponents = new List<string> { "SubSys1|Comp1" },
                SelectedAxisComponents = new List<string> { "Sub1|Motor1" },
                SelectedCHSteps = new List<string> { "Parent1|CH1" },
                SelectedThreads = new List<string> { "Worker" },
                IncludeEvents = true,
                IncludeMachineState = true
            };

            var pkg = Svc.BuildDataPackage(logs, preset, "combined");
            Assert.Equal(6, pkg.TimeStamps.Count);
            Assert.True(pkg.Signals.Count >= 2); // IO + Axis
            Assert.True(pkg.States.Count >= 1);  // CHStep + MachineState
            Assert.Single(pkg.ThreadMessages);
            Assert.Single(pkg.Events);
        }

        #endregion

        #region SignalData materialization

        [Fact]
        public void SignalData_LazyMaterialization_ForwardFills()
        {
            var signal = new SignalData
            {
                Name = "test",
                DataLength = 5,
                SparsePoints = new List<KeyValuePair<int, double>>
                {
                    new(0, 10.0),
                    new(2, 20.0),
                    new(4, 30.0)
                }
            };

            var data = signal.Data!;
            Assert.Equal(5, data.Length);
            Assert.Equal(10.0, data[0]);
            Assert.Equal(10.0, data[1]); // forward-fill
            Assert.Equal(20.0, data[2]);
            Assert.Equal(20.0, data[3]); // forward-fill
            Assert.Equal(30.0, data[4]);
        }

        [Fact]
        public void SignalData_LazyMaterialization_LeadingNaN()
        {
            var signal = new SignalData
            {
                Name = "test",
                DataLength = 3,
                SparsePoints = new List<KeyValuePair<int, double>>
                {
                    new(2, 5.0)
                }
            };

            var data = signal.Data!;
            Assert.True(double.IsNaN(data[0]));
            Assert.True(double.IsNaN(data[1]));
            Assert.Equal(5.0, data[2]);
        }

        [Fact]
        public void SignalData_LazyMaterialization_EmptySparse()
        {
            var signal = new SignalData
            {
                Name = "test",
                DataLength = 3,
                SparsePoints = new List<KeyValuePair<int, double>>()
            };

            var data = signal.Data!;
            Assert.Equal(3, data.Length);
            Assert.All(data, d => Assert.True(double.IsNaN(d)));
        }

        [Fact]
        public void SignalData_DataLength_FallsBackToDataArray()
        {
            var signal = new SignalData
            {
                Name = "test",
                Data = new double[] { 1.0, 2.0, 3.0 }
            };
            Assert.Equal(3, signal.DataLength);
        }

        #endregion

        #region Edge cases

        [Fact]
        public void BuildDataPackage_NullMessages_DoNotCrash()
        {
            var logs = new List<LogEntry>
            {
                new() { Date = T(0), Message = null!, ThreadName = "" },
                MakeLog(T(1), "IO_Mon:SubSys1,SubSys1_Comp1=1.0")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "null_msg");
            Assert.Single(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_MalformedNoComma_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSysOnly")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSysOnly|X" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_nocomma");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_MalformedNoEquals_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,noequals")
            };
            var preset = EmptyPreset();
            preset.SelectedIOComponents = new List<string> { "SubSys1|noequals" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_noeq");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_MissingSecondComma_Skipped()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1Only")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1Only|X" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_nocomma2");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_SessionNamePreserved()
        {
            var pkg = Svc.BuildDataPackage(
                new List<LogEntry> { MakeLog(T(0), "msg") },
                EmptyPreset(),
                "MySession_2025");
            Assert.Equal("MySession_2025", pkg.SessionName);
        }

        [Fact]
        public void BuildDataPackage_CreatedAt_IsRecentTime()
        {
            var before = DateTime.Now;
            var pkg = Svc.BuildDataPackage(
                new List<LogEntry> { MakeLog(T(0), "msg") },
                EmptyPreset(),
                "time_test");
            var after = DateTime.Now;
            Assert.True(pkg.CreatedAt >= before && pkg.CreatedAt <= after);
        }

        [Fact]
        public void BuildDataPackage_IOSignals_SelectionWithPipeSeparator_Required()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "IO_Mon:SubSys1,SubSys1_Comp1=10.0")
            };
            var preset = EmptyPreset();
            // Selection without pipe separator should not match anything
            preset.SelectedIOComponents = new List<string> { "SubSys1Comp1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "io_nopipe");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_AxisSignals_SelectionWithPipeSeparator_Required()
        {
            var logs = new List<LogEntry>
            {
                MakeLog(T(0), "AxisMon:Sub1,Motor1,Pos=10.0")
            };
            var preset = EmptyPreset();
            preset.SelectedAxisComponents = new List<string> { "Sub1Motor1" };

            var pkg = Svc.BuildDataPackage(logs, preset, "axis_nopipe");
            Assert.Empty(pkg.Signals);
        }

        [Fact]
        public void BuildDataPackage_Progress_IsReported()
        {
            var reports = new List<(double pct, string msg)>();
            var progress = new Progress<(double pct, string msg)>(r => reports.Add(r));

            var logs = new List<LogEntry> { MakeLog(T(0), "msg") };
            // Use synchronous progress to ensure capture
            var syncProgress = new SyncProgress<(double pct, string msg)>(r => reports.Add(r));
            Svc.BuildDataPackage(logs, EmptyPreset(), "progress_test", syncProgress);

            Assert.True(reports.Count > 0);
            Assert.Contains(reports, r => r.pct >= 100);
        }

        #endregion

        /// <summary>
        /// Synchronous IProgress implementation for testing (avoids async dispatch issues).
        /// </summary>
        private class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public SyncProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }
    }
}
