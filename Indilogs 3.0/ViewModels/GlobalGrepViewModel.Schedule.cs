using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Schedule Management

        /// <summary>
        /// Builds a combined SearchCriteria from ALL current UI settings:
        /// quick search text (Section 2), structured conditions (Section 3),
        /// PLC/APP toggles, and time filters.
        /// </summary>
        private SearchCriteria BuildFullCriteria()
        {
            // Start with structured conditions + PLC/APP + time filters
            var criteria = BuildCriteria();

            // If there's also quick search text, add it as an additional group
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var quickGroup = new SearchConditionGroup
                {
                    Operator = ConditionOperator.Or,
                    Conditions = new List<SearchCondition>
                    {
                        new SearchCondition
                        {
                            Field = SelectedQuickSearchField,
                            Operator = UseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                            Value = SearchQuery
                        }
                    }
                };
                criteria.Groups.Insert(0, quickGroup);
            }

            return criteria;
        }

        private void AddSchedule()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true },
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(8, 0, 0),
                OutputDirectory = AppPaths.Root
            };

            if (!ShowScheduleDialog("New Scheduled Search", schedule)) return;

            _schedulerService.AddSchedule(schedule);
            _taskSchedulerService.RegisterSchedule(schedule);
            Schedules.Add(schedule);
            StatusMessage = $"Schedule '{schedule.Name}' added.";

            // Immediately check if this schedule should run now
            _ = _schedulerService.TriggerCheckAsync();
        }

        private void EditSchedule()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;

            if (!ShowScheduleDialog("Edit Scheduled Search", schedule)) return;

            _schedulerService.UpdateSchedule(schedule);
            _taskSchedulerService.RegisterSchedule(schedule);
            var idx = Schedules.IndexOf(schedule);
            if (idx >= 0)
            {
                Schedules.RemoveAt(idx);
                Schedules.Insert(idx, schedule);
                SelectedSchedule = schedule;
            }
            StatusMessage = $"Schedule '{schedule.Name}' updated.";

            // Immediately check if this schedule should run now
            _ = _schedulerService.TriggerCheckAsync();

        }

        // ShowScheduleDialog is in GlobalGrepViewModel.ScheduleDialog.cs

        private void RemoveSchedule()
        {
            if (SelectedSchedule == null) return;
            if (_dialogService.ShowConfirm($"Remove schedule '{SelectedSchedule.Name}'?", "Confirm") != DialogResult.Yes) return;
            _taskSchedulerService.UnregisterSchedule(SelectedSchedule.Id);
            _schedulerService.RemoveSchedule(SelectedSchedule.Id);
            Schedules.Remove(SelectedSchedule);
        }

        private async Task RunScheduleNowAsync()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;
            var scheduleName = schedule.Name;

            // "Start" = run immediately, no waiting
            RequestCloseForScheduledRun?.Invoke(scheduleName);
            AppLogger.Info($"[Grep] Running scheduled search '{scheduleName}' now...");

            try
            {
                var reportPath = await _schedulerService.RunNowAsync(schedule);
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' completed: {schedule.LastRunStatus}");

                _dispatcher.Post(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, reportPath);
                });
            }
            catch (OperationCanceledException)
            {
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' cancelled");
                _dispatcher.Post(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Grep] Scheduled search '{scheduleName}' failed", ex);
                _dispatcher.Post(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
        }

        #endregion
    }
}
