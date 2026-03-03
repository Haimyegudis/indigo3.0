using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public interface ILogAnalyzer
    {
        string Name { get; }
        // Changed: receives the entire session to access the pre-built lists
        List<AnalysisResult> Analyze(LogSessionData session);
    }
}