using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Services.Analysis
{
    public class StateFailureAnalyzer : ILogAnalyzer
    {
        public string Name => "State Failure Analysis (Fast)";

        public List<AnalysisResult> Analyze(LogSessionData session)
        {
            var results = new List<AnalysisResult>();

            // שימוש ברשימות המוכנות מראש
            var failures = session.CriticalFailureEvents;
            var transitions = session.StateTransitions;
            var allLogs = session.Logs;

            // אם אין כישלונות, יוצאים מיד
            if (failures == null || failures.Count == 0) return results;

            // פילטור מראש של שגיאות בלבד
            var errorLogs = allLogs
                .Where(l => string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Date)
                .ToList();

            // יצירת מופע לשירות ה-AI
            var llmService = new LlmService();

            foreach (var failEvent in failures)
            {
                // מציאת הסטייט האחרון שהתחיל לפני הכישלון
                var lastTransition = transitions.LastOrDefault(t => t.Date <= failEvent.Date);

                if (lastTransition != null)
                {
                    var parts = lastTransition.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    string fromState = parts[0].Replace("PlcMngr:", "").Trim();
                    string targetState = parts.Length > 1 ? parts[1].Trim() : "Unknown";

                    double duration = (failEvent.Date - lastTransition.Date).TotalSeconds;

                    // חיפוש רק ברשימת השגיאות הרלוונטיות לחלון הזמן הזה
                    var errorsInTransition = errorLogs
                        .Where(l => l.Date >= lastTransition.Date && l.Date <= failEvent.Date)
                        .ToList();

                    // === שילוב ה-AI: בדיקה וניתוח ===
                    string aiInsightText = null;
                    if (errorsInTransition.Count > 0)
                    {
                        // שליחת הלוגים ל-Ollama (מומלץ לבצע async במערכת גדולה יותר)
                        aiInsightText = llmService.GetAiAnalysis(errorsInTransition, $"{fromState} -> {targetState}");
                    }

                    var result = new AnalysisResult
                    {
                        ProcessName = $"FAILED: {fromState} -> {targetState}",
                        Status = AnalysisStatus.Failure,
                        Summary = $"CRITICAL FAILURE DETECTED\n" +
                                  $"----------------------------------------\n" +
                                  $"Origin State: {fromState}\n" +
                                  $"Target State (Failed): {targetState}\n" +
                                  $"Duration until failure: {duration:F2}s\n" +
                                  $"Failure Time: {failEvent.Date:HH:mm:ss}\n" +
                                  $"Total Errors found: {errorsInTransition.Count}",

                        // כאן נכנס הניתוח מה-AI כדי שה-UI יוכל להציג אותו
                        AiInsight = aiInsightText,

                        ErrorsFound = new List<string>()
                    };

                    result.ErrorsFound.Add($"[CRITICAL EVENT] {failEvent.Message}");

                    foreach (var err in errorsInTransition)
                    {
                        result.ErrorsFound.Add($"[{err.Date:HH:mm:ss}] {err.Logger}: {err.Message}");
                    }

                    results.Add(result);
                }
            }

            return results;
        }
    }
}