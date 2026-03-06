using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ScheduleEditorViewModel
    {
        private void LoadFromSchedule(ScheduledSearch schedule)
        {
            // Section 1: Details
            ScheduleName = schedule.Name;
            IsEnabled = schedule.IsEnabled;
            ScanModeSearch = schedule.ScanMode == ScanMode.SearchOnly;
            ScanModeStats = schedule.ScanMode == ScanMode.StatisticsOnly;
            ScanModeBoth = schedule.ScanMode == ScanMode.SearchAndStatistics;

            // Section 2: When
            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once: ScheduleTypeIndex = 0; break;
                case ScheduleType.Daily: ScheduleTypeIndex = 1; break;
                case ScheduleType.Weekly: ScheduleTypeIndex = 2; break;
                case ScheduleType.Interval: ScheduleTypeIndex = 3; break;
            }
            RunDate = schedule.RunDate;
            RunHour = schedule.RunTime.Hours.ToString("00");
            RunMinute = schedule.RunTime.Minutes.ToString("00");

            if (schedule.RunDays != null)
            {
                DaySun = schedule.RunDays.Contains(DayOfWeek.Sunday);
                DayMon = schedule.RunDays.Contains(DayOfWeek.Monday);
                DayTue = schedule.RunDays.Contains(DayOfWeek.Tuesday);
                DayWed = schedule.RunDays.Contains(DayOfWeek.Wednesday);
                DayThu = schedule.RunDays.Contains(DayOfWeek.Thursday);
                DayFri = schedule.RunDays.Contains(DayOfWeek.Friday);
                DaySat = schedule.RunDays.Contains(DayOfWeek.Saturday);
            }

            IntervalValue = schedule.RepeatIntervalValue.ToString();
            switch (schedule.IntervalUnit)
            {
                case IntervalUnit.Minutes: IntervalUnitIndex = 0; break;
                case IntervalUnit.Hours: IntervalUnitIndex = 1; break;
                case IntervalUnit.Days: IntervalUnitIndex = 2; break;
            }

            // Section 3: What to search
            bool isSimple = true;
            SearchPLC = schedule.Criteria?.SearchPLC ?? true;
            SearchAPP = schedule.Criteria?.SearchAPP ?? true;

            if (schedule.Criteria?.Groups != null && schedule.Criteria.Groups.Count > 0)
            {
                var allConds = schedule.Criteria.Groups.SelectMany(g => g.Conditions).ToList();
                if (allConds.Count == 1 && schedule.Criteria.Groups.Count == 1)
                {
                    SimpleField = allConds[0].Field;
                    SimpleSearchText = allConds[0].Value ?? "";
                    SimpleUseRegex = allConds[0].Operator == SearchOperator.Regex;
                }
                else if (allConds.Count > 0)
                {
                    isSimple = false;
                    AdvancedOperator = schedule.Criteria.Groups[0].Operator;
                    foreach (var c in allConds)
                    {
                        Conditions.Add(new ConditionRowViewModel
                        {
                            Field = c.Field,
                            Operator = c.Operator,
                            Value = c.Value ?? "",
                            Negate = c.Negate
                        });
                    }
                }
            }
            IsSimpleMode = isSimple;

            // Section 4: Where
            var existingLocIds = new HashSet<Guid>(schedule.Criteria?.LocationIds ?? new List<Guid>());
            foreach (var loc in _allLocations)
            {
                LocationItems.Add(new LocationCheckItem
                {
                    Id = loc.Id,
                    DisplayText = $"{loc.Name}  ({loc.Address} \u2014 {loc.BasePath})",
                    IsChecked = existingLocIds.Count == 0 || existingLocIds.Contains(loc.Id)
                });
            }
            OnPropertyChanged(nameof(HasLocations));

            // Section 5: Time filters
            var fileRelative = schedule.Criteria?.FileTimeFilter?.RelativeRange ?? RelativeTimeRange.None;
            FileFilterCustom = fileRelative == RelativeTimeRange.None;
            FileFilter24h = fileRelative == RelativeTimeRange.Last24Hours;
            FileFilterWeek = fileRelative == RelativeTimeRange.LastWeek;
            FileFromDate = schedule.Criteria?.FileTimeFilter?.From;
            FileToDate = schedule.Criteria?.FileTimeFilter?.To;

            var resRelative = schedule.Criteria?.ResultTimeFilter?.RelativeRange ?? RelativeTimeRange.None;
            ResultFilterCustom = resRelative == RelativeTimeRange.None;
            ResultFilter24h = resRelative == RelativeTimeRange.Last24Hours;
            ResultFilterWeek = resRelative == RelativeTimeRange.LastWeek;
            ResultFromDate = schedule.Criteria?.ResultTimeFilter?.From;
            ResultToDate = schedule.Criteria?.ResultTimeFilter?.To;

            // Section 6: Output
            OutputDirectory = schedule.OutputDirectory;

            // Section 7: Email
            var email = schedule.EmailConfig ?? new EmailNotificationConfig();
            EmailEnabled = email.IsEnabled;
            if (email.Recipients != null)
                foreach (var r in email.Recipients) Recipients.Add(r);
            TimingImmediate = email.Timing == EmailTiming.Immediately;
            TimingDeferred = email.Timing == EmailTiming.AtSpecificTime;
            EmailHour = email.SendTime.Hours.ToString("00");
            EmailMinute = email.SendTime.Minutes.ToString("00");
            CustomSubject = email.CustomSubject;
        }

        // ═══ Condition management ═══
        private void AddCondition()
        {
            Conditions.Add(new ConditionRowViewModel());
        }

        private void RemoveCondition(ConditionRowViewModel? row)
        {
            if (row != null)
                Conditions.Remove(row);
        }

        // ═══ Location helpers ═══
        private void AddLocation()
        {
            if (_viewFactory == null) return;
            var dialog = _viewFactory.Create<LocationDialog>("Add Search Location", "", "", "");
            if (dialog.ShowDialog() != true) return;

            var loc = new SearchLocation
            {
                Name = dialog.LocationName,
                Address = dialog.Address,
                BasePath = dialog.LocationPath
            };

            _locationService?.Add(loc);
            _allLocations.Add(loc);
            LocationItems.Add(new LocationCheckItem
            {
                Id = loc.Id,
                DisplayText = $"{loc.Name}  ({loc.Address} \u2014 {loc.BasePath})",
                IsChecked = true
            });
            OnPropertyChanged(nameof(HasLocations));
        }

        private void SetAllLocations(bool isChecked)
        {
            foreach (var item in LocationItems)
                item.IsChecked = isChecked;
        }

        // ═══ Output browse ═══
        private void BrowseOutput()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = OutputDirectory,
                Description = "Select output directory for search results"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                OutputDirectory = dlg.SelectedPath;
        }

        // ═══ Email ═══
        private async Task SendTestEmailAsync()
        {
            if (Recipients.Count == 0)
            {
                TestEmailStatus = "Add at least one recipient first.";
                return;
            }

            var testConfig = new EmailNotificationConfig();

            TestEmailStatus = "Sending test email via Outlook...";
            _isTestEmailRunning = true;
            try
            {
                if (_emailService != null)
                {
                    var (ok, msg) = await _emailService.TestConnectionAsync(testConfig, Recipients[0]);
                    TestEmailStatus = msg;
                }
                else
                {
                    TestEmailStatus = "Email service not available";
                }
            }
            finally
            {
                _isTestEmailRunning = false;
            }
        }

        private void AddRecipient()
        {
            string email = (NewRecipient ?? "").Trim();
            if (!string.IsNullOrEmpty(email) && email.Contains("@"))
            {
                Recipients.Add(email);
                NewRecipient = "";
            }
        }

        private void RemoveRecipient(string? email)
        {
            if (email != null)
                Recipients.Remove(email);
        }

    }
}
