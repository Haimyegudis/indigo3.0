using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IGlobalGrepService
    {
        Task<List<GrepResult>> SearchLoadedSessionsAsync(
            IEnumerable<LogSessionData> loadedSessions,
            string searchQuery,
            bool useRegex,
            bool searchMessage,
            bool searchException,
            bool searchMethod,
            bool searchData,
            IProgress<(int current, int total, string status)>? progress,
            CancellationToken cancellationToken);

        Task<List<GrepResult>> SearchLoadedSessionsWithCriteriaAsync(
            IEnumerable<LogSessionData> loadedSessions,
            SearchCriteria criteria,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken,
            Action<GrepResult>? onResult = null);

        Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken);

        Task<List<GrepResult>> SearchMultiLocationAsync(
            SearchCriteria criteria,
            IReadOnlyList<SearchLocation> locations,
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken,
            Action<GrepResult>? onResult = null);

        bool EvaluateCriteria(LogEntry entry, SearchCriteria criteria);
        bool EvaluateGroup(LogEntry entry, SearchConditionGroup group);
        bool EvaluateCondition(LogEntry entry, SearchCondition condition);
        string DetermineMatchedFields(LogEntry entry, SearchCriteria criteria);
    }
}
