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

            // *** אופטימיזציה: מיון מראש של שגיאות לפי זמן ***
            var errorLogs = allLogs
                .Where(l => string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Date)
                .ToList();

            foreach (var failEvent in criticalFailures)
            {
                var lastTransition = transitions.LastOrDefault(t => t.Date <= failEvent.Date);

                if (lastTransition != null)
                {
                    var parts = lastTransition.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    string fromState = parts[0].Replace("PlcMngr:", "").Trim();
                    string targetState = parts.Length > 1 ? parts[1].Trim() : "Unknown";

                    double duration = (failEvent.Date - lastTransition.Date).TotalSeconds;

                    // *** אופטימיזציה: חיפוש בינארי בדיוק במקום סריקה מלאה ***
                    var errorsInContext = errorLogs
                        .Where(l => l.Date >= lastTransition.Date && l.Date <= failEvent.Date)
                        .ToList();

                    // --- תיקון: שם ספציפי ומדויק ---
                    string specificTitle = $"FAILED: {fromState} -> {targetState}";

                    var result = new AnalysisResult
                    {
                        ProcessName = $"{specificTitle} ({failEvent.Date:HH:mm:ss})",
                        Status = AnalysisStatus.Failure,
                        Summary = $"FAILURE DETECTED during transition\n" +
                                  $"----------------------------------------\n" +
                                  $"From State: {fromState}\n" +
                                  $"To State (Target): {targetState}\n" +
                                  $"Failure Time: {failEvent.Date:HH:mm:ss.fff}\n" +
                                  $"Duration before crash: {duration:F2}s\n" +
                                  $"Errors found: {errorsInContext.Count}",
                        ErrorsFound = new List<string>()
                    };

                    result.ErrorsFound.Add($"[CRITICAL EVENT] {failEvent.Message}");

                    foreach (var err in errorsInContext)
                    {
                        result.ErrorsFound.Add($"[{err.Date:HH:mm:ss}] {err.Logger}: {err.Message}");
                    }

                    results.Add(result);
                }
            }

            return results.OrderBy(r => r.ProcessName).ToList();
        }
    }
}