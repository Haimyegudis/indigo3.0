using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ScheduleEditorViewModel
    {
        // ═══ Save / Cancel ═══
        private void Save(Window? window)
        {
            if (window == null) return;

            // ═══ VALIDATION ═══
            if (string.IsNullOrWhiteSpace(ScheduleName))
            {
                _dialogService?.ShowWarning("Schedule name is required.", "Validation");
                return;
            }

            int hours = 0, minutes = 0;
            bool needsTime = ScheduleTypeIndex <= 2;
            if (needsTime)
            {
                if (!int.TryParse(RunHour, out hours) || hours < 0 || hours > 23)
                {
                    _dialogService?.ShowWarning("Hours must be 0\u201323.", "Validation");
                    return;
                }
                if (!int.TryParse(RunMinute, out minutes) || minutes < 0 || minutes > 59)
                {
                    _dialogService?.ShowWarning("Minutes must be 0\u201359.", "Validation");
                    return;
                }
            }

            if (ScheduleTypeIndex == 0 && RunDate == null)
            {
                _dialogService?.ShowWarning("Please select a date for 'Once' schedule.", "Validation");
                return;
            }

            if (ScheduleTypeIndex == 2 && !DaySun && !DayMon && !DayTue && !DayWed && !DayThu && !DayFri && !DaySat)
            {
                _dialogService?.ShowWarning("Select at least one day for weekly schedule.", "Validation");
                return;
            }

            int intervalVal = 1;
            if (ScheduleTypeIndex == 3)
            {
                if (!int.TryParse(IntervalValue, out intervalVal) || intervalVal < 1)
                {
                    _dialogService?.ShowWarning("Interval value must be at least 1.", "Validation");
                    return;
                }
            }

            bool isStatsOnly = ScanModeStats;
            bool needsSearch = !isStatsOnly;

            if (needsSearch && IsSimpleMode && string.IsNullOrWhiteSpace(SimpleSearchText))
            {
                _dialogService?.ShowWarning("Please enter search text.", "Validation");
                return;
            }
            if (needsSearch && !IsSimpleMode && Conditions.All(c => string.IsNullOrWhiteSpace(c.Value)))
            {
                _dialogService?.ShowWarning("Add at least one search condition with a value.", "Validation");
                return;
            }
            if (!SearchPLC && !SearchAPP)
            {
                _dialogService?.ShowWarning("Select at least PLC or APP log type.", "Validation");
                return;
            }
            if (LocationItems.Count > 0 && !LocationItems.Any(l => l.IsChecked))
            {
                _dialogService?.ShowWarning("Select at least one search location.", "Validation");
                return;
            }

            // ═══ BUILD SEARCH CRITERIA ═══
            var criteria = new SearchCriteria
            {
                SearchPLC = SearchPLC,
                SearchAPP = SearchAPP,
                LocationIds = LocationItems
                    .Where(l => l.IsChecked)
                    .Select(l => l.Id).ToList()
            };

            // File time filter
            if (FileFilter24h)
                criteria.FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            else if (FileFilterWeek)
                criteria.FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            else if (FileFromDate.HasValue || FileToDate.HasValue)
                criteria.FileTimeFilter = new TimeRangeFilter { From = FileFromDate, To = FileToDate };

            // Result time filter
            if (ResultFilter24h)
                criteria.ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            else if (ResultFilterWeek)
                criteria.ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            else if (ResultFromDate.HasValue || ResultToDate.HasValue)
                criteria.ResultTimeFilter = new TimeRangeFilter { From = ResultFromDate, To = ResultToDate };

            // Search conditions
            if (needsSearch)
            {
                if (IsSimpleMode)
                {
                    criteria.Groups.Add(new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition
                            {
                                Field = SimpleField,
                                Operator = SimpleUseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                                Value = SimpleSearchText?.Trim() ?? ""
                            }
                        }
                    });
                }
                else
                {
                    var group = new SearchConditionGroup
                    {
                        Operator = AdvancedOperator
                    };
                    foreach (var cond in Conditions)
                    {
                        if (string.IsNullOrWhiteSpace(cond.Value)) continue;
                        group.Conditions.Add(new SearchCondition
                        {
                            Field = cond.Field,
                            Operator = cond.Operator,
                            Value = cond.Value.Trim(),
                            Negate = cond.Negate
                        });
                    }
                    if (group.Conditions.Count > 0)
                        criteria.Groups.Add(group);
                }
            }

            // ═══ APPLY TO SCHEDULE ═══
            _schedule.Name = ScheduleName.Trim();
            _schedule.IsEnabled = IsEnabled;
            switch (ScheduleTypeIndex)
            {
                case 0: _schedule.ScheduleType = ScheduleType.Once; break;
                case 1: _schedule.ScheduleType = ScheduleType.Daily; break;
                case 2: _schedule.ScheduleType = ScheduleType.Weekly; break;
                case 3: _schedule.ScheduleType = ScheduleType.Interval; break;
            }
            _schedule.RunDate = ScheduleTypeIndex == 0 ? RunDate : null;
            _schedule.RunTime = new TimeSpan(hours, minutes, 0);

            var runDays = new HashSet<DayOfWeek>();
            if (DaySun) runDays.Add(DayOfWeek.Sunday);
            if (DayMon) runDays.Add(DayOfWeek.Monday);
            if (DayTue) runDays.Add(DayOfWeek.Tuesday);
            if (DayWed) runDays.Add(DayOfWeek.Wednesday);
            if (DayThu) runDays.Add(DayOfWeek.Thursday);
            if (DayFri) runDays.Add(DayOfWeek.Friday);
            if (DaySat) runDays.Add(DayOfWeek.Saturday);
            _schedule.RunDays = runDays;

            _schedule.RepeatIntervalValue = intervalVal;
            _schedule.IntervalUnit = IntervalUnitIndex == 0 ? IntervalUnit.Minutes
                : IntervalUnitIndex == 1 ? IntervalUnit.Hours
                : IntervalUnit.Days;

            _schedule.OutputDirectory = (OutputDirectory ?? "").Trim();
            _schedule.ScanMode = ScanModeStats ? ScanMode.StatisticsOnly
                : ScanModeBoth ? ScanMode.SearchAndStatistics
                : ScanMode.SearchOnly;
            _schedule.Criteria = criteria;

            // ═══ EMAIL CONFIG ═══
            if (EmailEnabled)
            {
                if (Recipients.Count == 0)
                {
                    _dialogService?.ShowWarning("Add at least one email recipient.", "Validation");
                    return;
                }

                int emailSendHour = 0, emailSendMin = 0;
                if (TimingDeferred)
                {
                    if (!int.TryParse(EmailHour, out emailSendHour) || emailSendHour < 0 || emailSendHour > 23)
                    {
                        _dialogService?.ShowWarning("Email send hour must be 0\u201323.", "Validation");
                        return;
                    }
                    if (!int.TryParse(EmailMinute, out emailSendMin) || emailSendMin < 0 || emailSendMin > 59)
                    {
                        _dialogService?.ShowWarning("Email send minutes must be 0\u201359.", "Validation");
                        return;
                    }
                }

                _schedule.EmailConfig = new EmailNotificationConfig
                {
                    IsEnabled = true,
                    Recipients = new List<string>(Recipients),
                    Timing = TimingDeferred ? EmailTiming.AtSpecificTime : EmailTiming.Immediately,
                    SendTime = new TimeSpan(emailSendHour, emailSendMin, 0),
                    CustomSubject = string.IsNullOrWhiteSpace(CustomSubject) ? null : CustomSubject.Trim()
                };
            }
            else
            {
                _schedule.EmailConfig = null;
            }

            window.DialogResult = true;
            window.Close();
        }

        private void Cancel(Window? window)
        {
            if (window == null) return;
            window.DialogResult = false;
            window.Close();
        }
    }
}
