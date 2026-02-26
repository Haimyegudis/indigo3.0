using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public class StateFailureAnalyzer : ILogAnalyzer
    {
        public string Name => "State Failure Analysis (Fast)";

        public List<AnalysisResult> Analyze(LogSessionData session)
        {
            var results = new List<AnalysisResult>();

            // שימוש ברשימות המוכנות מראש (O(1) access)
            var failures = session.CriticalFailureEvents;
            var transitions = session.StateTransitions;
            var allLogs = session.Logs;

            // אם אין כישלונות, יוצאים מיד
            if (failures == null || failures.Count == 0) return results;

            // *** אופטימיזציה: פילטור מראש של שגיאות בלבד ***
            var errorLogs = allLogs
                .Where(l => string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Date)
                .ToList();

            foreach (var failEvent in failures)
            {
                // מציאת הסטייט האחרון שהתחיל לפני הכישלון (מתוך הרשימה המצומצמת)
                // מכיוון ש-transitions ממוין, אפשר למצוא ביעילות
                var lastTransition = transitions.LastOrDefault(t => t.Date <= failEvent.Date);

                if (lastTransition != null)
                {
                    var parts = lastTransition.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    string fromState = parts[0].Replace("PlcMngr:", "").Trim();
                    string targetState = parts.Length > 1 ? parts[1].Trim() : "Unknown";

                    double duration = (failEvent.Date - lastTransition.Date).TotalSeconds;

                    // *** אופטימיזציה: חיפוש רק ברשימת השגיאות שכבר ממוינת ***
                    var errorsInTransition = errorLogs
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

                    var result = new AnalysisResult
                    {
                        ProcessName          = $"FAILED: {fromState} -> {targetState}",
                        FailureTime          = failEvent.Date,
                        CriticalEventMessage = triggerEntry?.Message,   // null → block hidden (never fall back to SEND EVENT)
                        Status = AnalysisStatus.Failure,
                        Summary = $"CRITICAL FAILURE DETECTED\n" +
                                  $"----------------------------------------\n" +
                                  $"Origin State: {fromState}\n" +
                                  $"Target State (Failed): {targetState}\n" +
                                  $"Duration until failure: {duration:F2}s\n" +
                                  $"Failure Time: {failEvent.Date:HH:mm:ss}\n" +
                                  $"Total Errors found: {errorsInTransition.Count}",
                        ErrorsFound         = new List<string>(),
                        ErrorItems          = new List<FailureErrorItem>()
                    };

                    // Critical event row — navigates to failEvent in PLC tab
                    result.ErrorItems.Add(new FailureErrorItem
                    {
                        DisplayText = $"[CRITICAL EVENT] {failEvent.Message}",
                        LogEntry    = failEvent
                    });

                    foreach (var err in errorsInTransition)
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

            return results;
        }
    }
}