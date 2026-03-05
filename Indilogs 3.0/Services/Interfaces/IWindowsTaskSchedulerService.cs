using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IWindowsTaskSchedulerService
    {
        bool RegisterSchedule(ScheduledSearch schedule);
        bool UnregisterSchedule(Guid scheduleId);
        void SyncAll(IReadOnlyList<ScheduledSearch> schedules);
    }
}
