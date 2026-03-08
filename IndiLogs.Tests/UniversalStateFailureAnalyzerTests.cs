using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services.Analysis;
using Xunit;

namespace IndiLogs.Tests
{
    public class UniversalStateFailureAnalyzerTests
    {
        private readonly UniversalStateFailureAnalyzer _analyzer = new();

        private static LogEntry MakeLog(DateTime date, string message, string level = "Info", string logger = "TestLogger") =>
            new() { Date = date, Message = message, Level = level, Logger = logger };

        private static LogEntry MakeTransition(DateTime date, string from, string to) =>
            MakeLog(date, $"PlcMngr: {from} -> {to}");

        private static LogEntry MakeFailure(DateTime date, string message = "Critical failure occurred") =>
            MakeLog(date, message, "Error");

        private static LogSessionData MakeSession(
            List<LogEntry>? logs = null,
            List<LogEntry>? transitions = null,
            List<LogEntry>? criticalFailures = null)
        {
            return new LogSessionData
            {
                Logs = logs ?? new List<LogEntry>(),
                StateTransitions = transitions ?? new List<LogEntry>(),
                CriticalFailureEvents = criticalFailures ?? new List<LogEntry>()
            };
        }

        [Fact]
        public void Name_ReturnsExpectedValue()
        {
            Assert.Equal("Universal State Failure Analyzer", _analyzer.Name);
        }

        [Fact]
        public void Analyze_NullCriticalFailureEvents_ReturnsEmptyList()
        {
            var session = new LogSessionData
            {
                Logs = new List<LogEntry>(),
                StateTransitions = new List<LogEntry>(),
                CriticalFailureEvents = null!
            };

            var results = _analyzer.Analyze(session);

            Assert.Empty(results);
        }

        [Fact]
        public void Analyze_EmptyCriticalFailureEvents_ReturnsEmptyList()
        {
            var session = MakeSession(criticalFailures: new List<LogEntry>());

            var results = _analyzer.Analyze(session);

            Assert.Empty(results);
        }

        [Fact]
        public void Analyze_NoTransitionBeforeFailure_ReturnsEmptyList()
        {
            var failTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var transitionTime = new DateTime(2025, 1, 1, 11, 0, 0); // after failure

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var results = _analyzer.Analyze(session);

            Assert.Empty(results);
        }

        [Fact]
        public void Analyze_SingleFailureWithTransition_ReturnsOneResult()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var results = _analyzer.Analyze(session);

            Assert.Single(results);
        }

        [Fact]
        public void Analyze_ResultHasFailureStatus()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Equal(AnalysisStatus.Failure, result.Status);
        }

        [Fact]
        public void Analyze_ResultProcessNameContainsStateTransition()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Contains("Setup", result.ProcessName);
            Assert.Contains("Printing", result.ProcessName);
            Assert.Contains("FAILED", result.ProcessName);
        }

        [Fact]
        public void Analyze_ResultFailureTimeMatchesCriticalEvent()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Equal(failTime, result.FailureTime);
        }

        [Fact]
        public void Analyze_SummaryContainsDurationAndStates()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Contains("Setup", result.Summary);
            Assert.Contains("Printing", result.Summary);
            Assert.Contains("30.00s", result.Summary);
            Assert.Contains("FAILURE DETECTED", result.Summary);
        }

        [Fact]
        public void Analyze_ErrorLogsBetweenTransitionAndFailure_IncludedInResult()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var errorTime = new DateTime(2025, 1, 1, 10, 0, 15);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var errorLog = MakeLog(errorTime, "Motor overheat detected", "Error", "MotorLogger");

            var session = MakeSession(
                logs: new List<LogEntry> { errorLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            // First ErrorItem is always the critical event itself
            Assert.True(result.ErrorItems.Count >= 2);
            Assert.Contains(result.ErrorItems, e => e.DisplayText.Contains("Motor overheat detected"));
            Assert.Contains(result.ErrorsFound, e => e.Contains("Motor overheat detected"));
        }

        [Fact]
        public void Analyze_ErrorLogsOutsideWindow_NotIncluded()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var earlyError = MakeLog(new DateTime(2025, 1, 1, 9, 59, 0), "Early error", "Error", "TestLogger");
            var lateError = MakeLog(new DateTime(2025, 1, 1, 10, 1, 0), "Late error", "Error", "TestLogger");

            var session = MakeSession(
                logs: new List<LogEntry> { earlyError, lateError },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.DoesNotContain(result.ErrorItems, e => e.DisplayText.Contains("Early error"));
            Assert.DoesNotContain(result.ErrorItems, e => e.DisplayText.Contains("Late error"));
        }

        [Fact]
        public void Analyze_NonErrorLogs_NotIncludedAsErrors()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var infoLog = MakeLog(new DateTime(2025, 1, 1, 10, 0, 15), "Info message", "Info", "TestLogger");
            var warnLog = MakeLog(new DateTime(2025, 1, 1, 10, 0, 16), "Warning message", "Warn", "TestLogger");

            var session = MakeSession(
                logs: new List<LogEntry> { infoLog, warnLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            // Only the critical event item (no error logs matched)
            Assert.Single(result.ErrorItems);
            Assert.Empty(result.ErrorsFound);
        }

        [Fact]
        public void Analyze_TriggerEntryFound_SetsCriticalEventMessage()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var triggerLog = MakeLog(
                new DateTime(2025, 1, 1, 10, 0, 25),
                "Enqueue event PLC_FAILURE_STATE_CHANGE for handler");

            var session = MakeSession(
                logs: new List<LogEntry> { triggerLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.NotNull(result.CriticalEventMessage);
            Assert.Contains("PLC_FAILURE_STATE_CHANGE", result.CriticalEventMessage);
        }

        [Fact]
        public void Analyze_NoTriggerEntry_CriticalEventMessageIsNull()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Null(result.CriticalEventMessage);
        }

        [Fact]
        public void Analyze_TriggerEntryWithin10sAfterFailure_StillFound()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var triggerLog = MakeLog(
                new DateTime(2025, 1, 1, 10, 0, 38), // 8s after failure, within 10s window
                "Enqueue event PLC_FAILURE_STATE_CHANGE");

            var session = MakeSession(
                logs: new List<LogEntry> { triggerLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.NotNull(result.CriticalEventMessage);
        }

        [Fact]
        public void Analyze_TriggerEntryBeyond10sAfterFailure_NotFound()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var triggerLog = MakeLog(
                new DateTime(2025, 1, 1, 10, 0, 41), // 11s after failure, outside window
                "Enqueue event PLC_FAILURE_STATE_CHANGE");

            var session = MakeSession(
                logs: new List<LogEntry> { triggerLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Null(result.CriticalEventMessage);
        }

        [Fact]
        public void Analyze_MultipleFailures_ResultsSortedByFailureTimeAscending()
        {
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 11, 0, 0);
            var t3 = new DateTime(2025, 1, 1, 12, 0, 0);

            var fail1 = MakeFailure(new DateTime(2025, 1, 1, 12, 0, 30), "Failure C");
            var fail2 = MakeFailure(new DateTime(2025, 1, 1, 10, 0, 30), "Failure A");
            var fail3 = MakeFailure(new DateTime(2025, 1, 1, 11, 0, 30), "Failure B");

            var session = MakeSession(
                transitions: new List<LogEntry>
                {
                    MakeTransition(t1, "Idle", "Setup"),
                    MakeTransition(t2, "Setup", "Printing"),
                    MakeTransition(t3, "Printing", "Cleanup")
                },
                criticalFailures: new List<LogEntry> { fail1, fail2, fail3 }
            );

            var results = _analyzer.Analyze(session);

            Assert.Equal(3, results.Count);
            Assert.True(results[0].FailureTime < results[1].FailureTime);
            Assert.True(results[1].FailureTime < results[2].FailureTime);
        }

        [Fact]
        public void Analyze_UsesLastTransitionBeforeFailure()
        {
            var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
            var t2 = new DateTime(2025, 1, 1, 10, 0, 20);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);

            var session = MakeSession(
                transitions: new List<LogEntry>
                {
                    MakeTransition(t1, "Idle", "Setup"),
                    MakeTransition(t2, "Setup", "Printing")
                },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            // Should use the second (latest) transition "Setup -> Printing"
            Assert.Contains("Printing", result.ProcessName);
            Assert.Contains("10.00s", result.Summary); // 30 - 20 = 10s
        }

        [Fact]
        public void Analyze_TransitionWithoutArrow_TargetStateDefaultsToUnknown()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var transition = MakeLog(transitionTime, "No arrow in this message");

            var session = MakeSession(
                transitions: new List<LogEntry> { transition },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Contains("Unknown", result.ProcessName);
        }

        [Fact]
        public void Analyze_FirstErrorItemIsCriticalEvent()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var failEvent = MakeFailure(failTime, "PLC crash detected");

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { failEvent }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.StartsWith("[CRITICAL EVENT]", result.ErrorItems[0].DisplayText);
            Assert.Contains("PLC crash detected", result.ErrorItems[0].DisplayText);
            Assert.Same(failEvent, result.ErrorItems[0].LogEntry);
        }

        [Fact]
        public void Analyze_ErrorItemsHaveCorrectLogEntryReferences()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var errorLog = MakeLog(new DateTime(2025, 1, 1, 10, 0, 15), "Sensor fault", "Error", "SensorLogger");

            var session = MakeSession(
                logs: new List<LogEntry> { errorLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            var sensorItem = result.ErrorItems.First(e => e.DisplayText.Contains("Sensor fault"));
            Assert.Same(errorLog, sensorItem.LogEntry);
        }

        [Fact]
        public void Analyze_MultipleErrorsInWindow_AllCapturedInOrder()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var err1 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 5), "Error alpha", "Error", "L1");
            var err2 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 10), "Error beta", "Error", "L2");
            var err3 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 20), "Error gamma", "Error", "L3");

            var session = MakeSession(
                logs: new List<LogEntry> { err1, err2, err3 },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Idle", "Setup") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            // 1 critical event + 3 errors = 4 items
            Assert.Equal(4, result.ErrorItems.Count);
            Assert.Equal(3, result.ErrorsFound.Count);
            Assert.Contains("Error alpha", result.ErrorsFound[0]);
            Assert.Contains("Error beta", result.ErrorsFound[1]);
            Assert.Contains("Error gamma", result.ErrorsFound[2]);
        }

        [Fact]
        public void Analyze_SummaryContainsErrorCount()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var err1 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 10), "Err1", "Error");
            var err2 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 20), "Err2", "Error");

            var session = MakeSession(
                logs: new List<LogEntry> { err1, err2 },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Idle", "Setup") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Contains("Errors found: 2", result.Summary);
        }

        [Fact]
        public void Analyze_ErrorLevelCaseInsensitive()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var err1 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 10), "Uppercase error", "ERROR");
            var err2 = MakeLog(new DateTime(2025, 1, 1, 10, 0, 15), "Mixed case error", "error");

            var session = MakeSession(
                logs: new List<LogEntry> { err1, err2 },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Idle", "Setup") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Equal(2, result.ErrorsFound.Count);
        }

        [Fact]
        public void Analyze_TransitionAtExactSameTimeAsFailure_StillUsed()
        {
            var time = new DateTime(2025, 1, 1, 10, 0, 0);

            var session = MakeSession(
                transitions: new List<LogEntry> { MakeTransition(time, "Idle", "Setup") },
                criticalFailures: new List<LogEntry> { MakeFailure(time) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.Contains("0.00s", result.Summary);
            Assert.Contains("Setup", result.ProcessName);
        }

        [Fact]
        public void Analyze_TriggerSearchIsCaseInsensitive()
        {
            var transitionTime = new DateTime(2025, 1, 1, 10, 0, 0);
            var failTime = new DateTime(2025, 1, 1, 10, 0, 30);
            var triggerLog = MakeLog(
                new DateTime(2025, 1, 1, 10, 0, 25),
                "ENQUEUE EVENT plc_failure_state_change handler");

            var session = MakeSession(
                logs: new List<LogEntry> { triggerLog },
                transitions: new List<LogEntry> { MakeTransition(transitionTime, "Setup", "Printing") },
                criticalFailures: new List<LogEntry> { MakeFailure(failTime) }
            );

            var result = _analyzer.Analyze(session).Single();

            Assert.NotNull(result.CriticalEventMessage);
        }
    }
}
