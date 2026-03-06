using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// In-app scheduler that checks every 60 seconds for scheduled searches and executes them.
    /// Results are saved to the configured output directory as JSON and CSV.
    /// </summary>
    public partial class SearchSchedulerService : ISearchSchedulerService
    {
        private static readonly string ScheduleFile = AppPaths.SearchSchedules;

        private readonly IGlobalGrepService _grepService;
        private readonly ISearchLocationService _locationService;
        private readonly IEmailNotificationService _emailService;
        private readonly System.Timers.Timer _timer;
        private volatile bool _isRunning;

        public List<ScheduledSearch> Schedules { get; private set; } = new List<ScheduledSearch>();

        /// <summary>
        /// Raised when a scheduled search starts.
        /// </summary>
        public event Action<ScheduledSearch>? SearchStarted;

        /// <summary>
        /// Raised when a scheduled search completes (or fails).
        /// </summary>
        public event Action<ScheduledSearch, int, string?>? SearchCompleted;

        public SearchSchedulerService(IGlobalGrepService grepService, ISearchLocationService locationService, IEmailNotificationService emailService)
        {
            _grepService = grepService;
            _locationService = locationService;
            _emailService = emailService;
            LoadSchedules();

            _timer = new System.Timers.Timer(60_000); // Check every 60 seconds
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            _timer.Start();
            AppLogger.Info("[Scheduler] Search scheduler started");
        }

        public void Stop()
        {
            _timer.Stop();
            AppLogger.Info("[Scheduler] Search scheduler stopped");
        }

        public void AddSchedule(ScheduledSearch schedule)
        {
            Schedules.Add(schedule);
            SaveSchedules();
        }

        public void RemoveSchedule(Guid id)
        {
            Schedules.RemoveAll(s => s.Id == id);
            SaveSchedules();
        }

        public void UpdateSchedule(ScheduledSearch schedule)
        {
            var idx = Schedules.FindIndex(s => s.Id == schedule.Id);
            if (idx >= 0) Schedules[idx] = schedule;
            SaveSchedules();
        }

        internal bool ShouldRun(ScheduledSearch schedule, DateTime now)
        {
            if (!schedule.IsEnabled) return false;

            bool result;
            string reason;

            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once:
                    if (schedule.LastRunTime != null) { reason = "already ran"; result = false; break; }
                    if (schedule.RunDate.HasValue)
                    {
                        var target = schedule.RunDate.Value.Date.Add(schedule.RunTime);
                        result = now >= target;
                        reason = result ? "target time reached" : $"waiting for {target:yyyy-MM-dd HH:mm}";
                    }
                    else
                    {
                        result = now.TimeOfDay >= schedule.RunTime;
                        reason = result ? "run time reached" : $"waiting for {schedule.RunTime:hh\\:mm}";
                    }
                    break;

                case ScheduleType.Daily:
                    if (now.TimeOfDay < schedule.RunTime)
                    {
                        reason = $"waiting for {schedule.RunTime:hh\\:mm}";
                        result = false;
                    }
                    else if (schedule.LastRunTime != null && schedule.LastRunTime.Value.Date >= now.Date)
                    {
                        reason = "already ran today";
                        result = false;
                    }
                    else
                    {
                        reason = "daily run time reached";
                        result = true;
                    }
                    break;

                case ScheduleType.Weekly:
                    if (!schedule.RunDays.Contains(now.DayOfWeek))
                    {
                        reason = $"today ({now.DayOfWeek}) not in scheduled days";
                        result = false;
                    }
                    else if (now.TimeOfDay < schedule.RunTime)
                    {
                        reason = $"waiting for {schedule.RunTime:hh\\:mm}";
                        result = false;
                    }
                    else if (schedule.LastRunTime != null && schedule.LastRunTime.Value.Date >= now.Date)
                    {
                        reason = "already ran today";
                        result = false;
                    }
                    else
                    {
                        reason = "weekly run time reached";
                        result = true;
                    }
                    break;

                case ScheduleType.Interval:
                    if (schedule.LastRunTime == null)
                    {
                        // First run: respect start time if set
                        if (schedule.RunTime.TotalSeconds > 0 && now.TimeOfDay < schedule.RunTime)
                        {
                            reason = $"waiting for start time {schedule.RunTime:hh\\:mm}";
                            result = false;
                        }
                        else
                        {
                            reason = "first run (no previous execution)";
                            result = true;
                        }
                    }
                    else
                    {
                        double elapsed = (now - schedule.LastRunTime.Value).TotalMinutes;
                        int interval = schedule.RepeatIntervalMinutes;
                        result = elapsed >= interval;
                        reason = result ? $"interval elapsed ({elapsed:F0}/{interval} min)"
                            : $"waiting ({elapsed:F0}/{interval} min)";
                    }
                    break;

                default:
                    reason = "unknown schedule type";
                    result = false;
                    break;
            }

            if (result)
                AppLogger.Info($"[Scheduler] Schedule \"{schedule.Name}\" ({schedule.ScheduleType}) → WILL RUN ({reason})");
            else
                AppLogger.Info($"[Scheduler] Schedule \"{schedule.Name}\" ({schedule.ScheduleType}) → SKIPPED ({reason})");

            return result;
        }

        private void LoadSchedules()
        {
            try
            {
                if (File.Exists(ScheduleFile))
                    Schedules = JsonConvert.DeserializeObject<List<ScheduledSearch>>(File.ReadAllText(ScheduleFile), AppConstants.SafeJsonSettings)
                                ?? new List<ScheduledSearch>();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Scheduler] Failed to load schedules: {ex.Message}");
                Schedules = new List<ScheduledSearch>();
            }
        }

        public void SaveSchedules()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScheduleFile)!);
                File.WriteAllText(ScheduleFile, JsonConvert.SerializeObject(Schedules, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Scheduler] Failed to save schedules: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _emailService?.Dispose();
        }
    }
}
