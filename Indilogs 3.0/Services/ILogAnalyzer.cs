using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public interface ILogAnalyzer
    {
        string Name { get; }
        // שינוי: מקבל את כל הסשן כדי לגשת לרשימות המוכנות
        List<AnalysisResult> Analyze(LogSessionData session);
    }
}