using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ISearchSchedulerService : IDisposable
    {
        List<ScheduledSearch> Schedules { get; }
        event Action<ScheduledSearch> SearchStarted;
        event Action<ScheduledSearch, int, string> SearchCompleted;
        void Start();
        void Stop();
        void AddSchedule(ScheduledSearch schedule);
        void RemoveSchedule(Guid id);
        void UpdateSchedule(ScheduledSearch schedule);
        Task<string> RunNowAsync(ScheduledSearch schedule, CancellationToken ct = default);
        Task TriggerCheckAsync();
        void SaveSchedules();
    }
}
