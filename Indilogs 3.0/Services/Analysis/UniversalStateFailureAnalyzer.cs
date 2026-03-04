using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public class UniversalStateFailureAnalyzer : ILogAnalyzer
    {
        public string Name => "Universal State Failure Analyzer";

        public List<AnalysisResult> Analyze(LogSessionData session)
        {
            var results = new List<AnalysisResult>();

            var transitions = session.StateTransitions;
            var criticalFailures = session.CriticalFailureEvents;
            var allLogs = session.Logs;

            if (criticalFailures == null || criticalFailures.Count == 0)
                return results;

            // *** Optimization: pre-sort errors by time ***
            var errorLogs = allLogs
                .Where(l => string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Date)
                .ToList();

            foreach (var failEvent in criticalFailures)
            {
                var lastTransition = transitions.LastOrDefault(t => t.Date <= failEvent.Date);

                if (lastTransition != null)
                {
                    StateTransitionHelper.TryParseTransition(lastTransition.Message, out string? fromState, out string? targetState);
                    if (targetState == null) targetState = "Unknown";

                    double duration = (failEvent.Date - lastTransition.Date).TotalSeconds;

                    // *** Optimization: precise binary search instead of full scan ***
                    var errorsInContext = errorLogs
                        .Where(l => l.Date >= lastTransition.Date && l.Date <= failEvent.Date)
                        .ToList();

                    // Find the "Enqueue event PLC_FAILURE_STATE_CHANGE" entry as the trigger.
                    // Use a wide window (10s after failEvent) and Contains checks for robustness.
                    var triggerEntry = allLogs
                        .Where(l => l.Date >= lastTransition.Date && l.Date <= failEvent.Date.AddSeconds(10))
                        .LastOrDefault(l => l.Message != null &&
                                           l.Message.IndexOf("Enqueue event",
                                               StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           l.Message.IndexOf("PLC_FAILURE_STATE_CHANGE",
                                               StringComparison.OrdinalIgnoreCase) >= 0);

                    // --- Fix: specific and accurate name ---
                    string specificTitle = $"FAILED: {fromState} -> {targetState}";

                    var result = new AnalysisResult
                    {
                        ProcessName          = $"{specificTitle} ({failEvent.Date:HH:mm:ss})",
                        FailureTime          = failEvent.Date,
                        CriticalEventMessage = triggerEntry?.Message,   // null → block hidden (never fall back to SEND EVENT)
                        Status = AnalysisStatus.Failure,
                        Summary = $"FAILURE DETECTED during transition\n" +
                                  $"----------------------------------------\n" +
                                  $"From State: {fromState}\n" +
                                  $"To State (Target): {targetState}\n" +
                                  $"Failure Time: {failEvent.Date:HH:mm:ss.ffffff}\n" +
                                  $"Duration before crash: {duration:F2}s\n" +
                                  $"Errors found: {errorsInContext.Count}",
                        ErrorsFound         = new List<string>(),
                        ErrorItems          = new List<FailureErrorItem>()
                    };

                    // Critical event row — navigates to failEvent in PLC tab
                    result.ErrorItems.Add(new FailureErrorItem
                    {
                        DisplayText = $"[CRITICAL EVENT] {failEvent.Message}",
                        LogEntry    = failEvent
                    });

                    foreach (var err in errorsInContext)
                    {
                        result.ErrorItems.Add(new FailureErrorItem
                        {
                            DisplayText = $"[{err.Date:HH:mm:ss}] {err.Logger}: {err.Message}",
                            LogEntry    = err
                        });
                        result.ErrorsFound.Add($"[{err.Date:HH:mm:ss}] {err.Logger}: {err.Message}");
                    }

                    results.Add(result);
                }
            }

            // Sort chronologically: earliest failure first (top), latest last (bottom)
            return results.OrderBy(r => r.FailureTime).ToList();
        }
    }
}