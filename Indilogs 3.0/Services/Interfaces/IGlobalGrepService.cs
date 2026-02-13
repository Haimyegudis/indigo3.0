using IndiLogs_3._0.Models;
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
            IProgress<(int current, int total, string status)> progress,
            CancellationToken cancellationToken);

        Task<List<GrepResult>> SearchExternalFilesAsync(
            string path, string searchQuery, bool useRegex, bool searchPLC, bool searchAPP,
            IProgress<(int current, int total, string status)> progress, CancellationToken cancellationToken);
    }
}
