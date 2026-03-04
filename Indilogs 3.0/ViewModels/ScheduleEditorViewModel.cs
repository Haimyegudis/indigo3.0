using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the Schedule Editor dialog window.
    /// Configures name, timing, search criteria, locations, time filters, output, and email.
    /// </summary>
    public class ScheduleEditorViewModel : ViewModelBase
    {
        private readonly ScheduledSearch _schedule;
        private readonly List<SearchLocation> _allLocations;
        private readonly IDialogService _dialogService;

        // ═══ Section 1: Schedule Details ═══
        private string? _scheduleName;
        public string? ScheduleName
        {
            get => _scheduleName;
            set => SetField(ref _scheduleName, value);
        }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetField(ref _isEnabled, value);
        }

        private bool _scanModeSearch;
        public bool ScanModeSearch
        {
            get => _scanModeSearch;
            set
            {
                if (SetField(ref _scanModeSearch, value) && value)
                    OnPropertyChanged(nameof(NeedsSearch));
            }
        }

        private bool _scanModeStats;
        public bool ScanModeStats
        {
            get => _scanModeStats;
            set
            {
                if (SetField(ref _scanModeStats, value) && value)
                    OnPropertyChanged(nameof(NeedsSearch));
            }
        }

        private bool _scanModeBoth;
        public bool ScanModeBoth
        {
            get => _scanModeBoth;
            set
            {
                if (SetField(ref _scanModeBoth, value) && value)
                    OnPropertyChanged(nameof(NeedsSearch));
            }
        }

        public bool NeedsSearch => ScanModeSearch || ScanModeBoth;

        // ═══ Section 2: When to Run ═══
        private int _scheduleTypeIndex;
        public int ScheduleTypeIndex
        {
            get => _scheduleTypeIndex;
            set
            {
                if (SetField(ref _scheduleTypeIndex, value))
                {
                    OnPropertyChanged(nameof(IsOnce));
                    OnPropertyChanged(nameof(IsWeekly));
                    OnPropertyChanged(nameof(IsInterval));
                    OnPropertyChanged(nameof(TimeLabelText));
                }
            }
        }

        public bool IsOnce => ScheduleTypeIndex == 0;
        public bool IsWeekly => ScheduleTypeIndex == 2;
        public bool IsInterval => ScheduleTypeIndex == 3;
        public string TimeLabelText => ScheduleTypeIndex == 3 ? "Start Time (HH:mm)" : "Run Time (HH:mm)";

        private DateTime? _runDate;
        public DateTime? RunDate
        {
            get => _runDate;
            set => SetField(ref _runDate, value);
        }

        private string? _runHour;
        public string? RunHour
        {
            get => _runHour;
            set => SetField(ref _runHour, value);
        }

        private string? _runMinute;
        public string? RunMinute
        {
            get => _runMinute;
            set => SetField(ref _runMinute, value);
        }

        // Day of week checkboxes
        private bool _daySun, _dayMon, _dayTue, _dayWed, _dayThu, _dayFri, _daySat;
        public bool DaySun { get => _daySun; set => SetField(ref _daySun, value); }
        public bool DayMon { get => _dayMon; set => SetField(ref _dayMon, value); }
        public bool DayTue { get => _dayTue; set => SetField(ref _dayTue, value); }
        public bool DayWed { get => _dayWed; set => SetField(ref _dayWed, value); }
        public bool DayThu { get => _dayThu; set => SetField(ref _dayThu, value); }
        public bool DayFri { get => _dayFri; set => SetField(ref _dayFri, value); }
        public bool DaySat { get => _daySat; set => SetField(ref _daySat, value); }

        private string? _intervalValue;
        public string? IntervalValue
        {
            get => _intervalValue;
            set => SetField(ref _intervalValue, value);
        }

        private int _intervalUnitIndex;
        public int IntervalUnitIndex
        {
            get => _intervalUnitIndex;
            set => SetField(ref _intervalUnitIndex, value);
        }

        // ═══ Section 3: What to Search ═══
        private bool _isSimpleMode;
        public bool IsSimpleMode
        {
            get => _isSimpleMode;
            set
            {
                if (SetField(ref _isSimpleMode, value))
                {
                    OnPropertyChanged(nameof(IsAdvancedMode));
                    if (!value && Conditions.Count == 0)
                        AddCondition();
                }
            }
        }

        public bool IsAdvancedMode
        {
            get => !_isSimpleMode;
            set => IsSimpleMode = !value;
        }

        // Simple search
        public Array SearchFieldValues => Enum.GetValues(typeof(SearchField));

        private SearchField _simpleField;
        public SearchField SimpleField
        {
            get => _simpleField;
            set => SetField(ref _simpleField, value);
        }

        private string? _simpleSearchText;
        public string? SimpleSearchText
        {
            get => _simpleSearchText;
            set => SetField(ref _simpleSearchText, value);
        }

        private bool _simpleUseRegex;
        public bool SimpleUseRegex
        {
            get => _simpleUseRegex;
            set => SetField(ref _simpleUseRegex, value);
        }

        // Advanced conditions
        public Array ConditionOperatorValues => Enum.GetValues(typeof(ConditionOperator));

        private ConditionOperator _advancedOperator;
        public ConditionOperator AdvancedOperator
        {
            get => _advancedOperator;
            set => SetField(ref _advancedOperator, value);
        }

        public ObservableCollection<ConditionRowViewModel> Conditions { get; } = new ObservableCollection<ConditionRowViewModel>();

        // Log types
        private bool _searchPLC;
        public bool SearchPLC
        {
            get => _searchPLC;
            set => SetField(ref _searchPLC, value);
        }

        private bool _searchAPP;
        public bool SearchAPP
        {
            get => _searchAPP;
            set => SetField(ref _searchAPP, value);
        }

        // ═══ Section 4: Where to Search ═══
        public ObservableCollection<LocationCheckItem> LocationItems { get; } = new ObservableCollection<LocationCheckItem>();

        public bool HasLocations => LocationItems.Count > 0;

        // ═══ Section 5: Time Filters ═══
        // File time filter
        private bool _fileFilterCustom;
        public bool FileFilterCustom
        {
            get => _fileFilterCustom;
            set
            {
                if (SetField(ref _fileFilterCustom, value))
                    OnPropertyChanged(nameof(FileFilterIsCustom));
            }
        }

        private bool _fileFilter24h;
        public bool FileFilter24h
        {
            get => _fileFilter24h;
            set => SetField(ref _fileFilter24h, value);
        }

        private bool _fileFilterWeek;
        public bool FileFilterWeek
        {
            get => _fileFilterWeek;
            set => SetField(ref _fileFilterWeek, value);
        }

        public bool FileFilterIsCustom => FileFilterCustom;

        private DateTime? _fileFromDate;
        public DateTime? FileFromDate
        {
            get => _fileFromDate;
            set => SetField(ref _fileFromDate, value);
        }

        private DateTime? _fileToDate;
        public DateTime? FileToDate
        {
            get => _fileToDate;
            set => SetField(ref _fileToDate, value);
        }

        // Result time filter
        private bool _resultFilterCustom;
        public bool ResultFilterCustom
        {
            get => _resultFilterCustom;
            set
            {
                if (SetField(ref _resultFilterCustom, value))
                    OnPropertyChanged(nameof(ResultFilterIsCustom));
            }
        }

        private bool _resultFilter24h;
        public bool ResultFilter24h
        {
            get => _resultFilter24h;
            set => SetField(ref _resultFilter24h, value);
        }

        private bool _resultFilterWeek;
        public bool ResultFilterWeek
        {
            get => _resultFilterWeek;
            set => SetField(ref _resultFilterWeek, value);
        }

        public bool ResultFilterIsCustom => ResultFilterCustom;

        private DateTime? _resultFromDate;
        public DateTime? ResultFromDate
        {
            get => _resultFromDate;
            set => SetField(ref _resultFromDate, value);
        }

        private DateTime? _resultToDate;
        public DateTime? ResultToDate
        {
            get => _resultToDate;
            set => SetField(ref _resultToDate, value);
        }

        // ═══ Section 6: Output ═══
        private string? _outputDirectory;
        public string? OutputDirectory
        {
            get => _outputDirectory;
            set => SetField(ref _outputDirectory, value);
        }

        // ═══ Section 7: Email ═══
        private bool _emailEnabled;
        public bool EmailEnabled
        {
            get => _emailEnabled;
            set => SetField(ref _emailEnabled, value);
        }

        private string? _smtpHost;
        public string? SmtpHost
        {
            get => _smtpHost;
            set => SetField(ref _smtpHost, value);
        }

        private string? _smtpPort;
        public string? SmtpPort
        {
            get => _smtpPort;
            set => SetField(ref _smtpPort, value);
        }

        private bool _useSsl;
        public bool UseSsl
        {
            get => _useSsl;
            set => SetField(ref _useSsl, value);
        }

        private bool _authNone;
        public bool AuthNone
        {
            get => _authNone;
            set
            {
                if (SetField(ref _authNone, value))
                    OnPropertyChanged(nameof(AuthIsUserPass));
            }
        }

        private bool _authWindows;
        public bool AuthWindows
        {
            get => _authWindows;
            set
            {
                if (SetField(ref _authWindows, value))
                    OnPropertyChanged(nameof(AuthIsUserPass));
            }
        }

        private bool _authUserPass;
        public bool AuthUserPass
        {
            get => _authUserPass;
            set
            {
                if (SetField(ref _authUserPass, value))
                    OnPropertyChanged(nameof(AuthIsUserPass));
            }
        }

        public bool AuthIsUserPass => AuthUserPass;

        private string? _smtpUsername;
        public string? SmtpUsername
        {
            get => _smtpUsername;
            set => SetField(ref _smtpUsername, value);
        }

        private string? _smtpPassword;
        public string? SmtpPassword
        {
            get => _smtpPassword;
            set => SetField(ref _smtpPassword, value);
        }

        private string? _fromAddress;
        public string? FromAddress
        {
            get => _fromAddress;
            set => SetField(ref _fromAddress, value);
        }

        private string? _testEmailStatus;
        public string? TestEmailStatus
        {
            get => _testEmailStatus;
            set => SetField(ref _testEmailStatus, value);
        }

        public ObservableCollection<string> Recipients { get; } = new ObservableCollection<string>();

        private string? _newRecipient;
        public string? NewRecipient
        {
            get => _newRecipient;
            set => SetField(ref _newRecipient, value);
        }

        private bool _timingImmediate;
        public bool TimingImmediate
        {
            get => _timingImmediate;
            set
            {
                if (SetField(ref _timingImmediate, value))
                    OnPropertyChanged(nameof(TimingIsDeferred));
            }
        }

        private bool _timingDeferred;
        public bool TimingDeferred
        {
            get => _timingDeferred;
            set
            {
                if (SetField(ref _timingDeferred, value))
                    OnPropertyChanged(nameof(TimingIsDeferred));
            }
        }

        public bool TimingIsDeferred => TimingDeferred;

        private string? _emailHour;
        public string? EmailHour
        {
            get => _emailHour;
            set => SetField(ref _emailHour, value);
        }

        private string? _emailMinute;
        public string? EmailMinute
        {
            get => _emailMinute;
            set => SetField(ref _emailMinute, value);
        }

        private string? _customSubject;
        public string? CustomSubject
        {
            get => _customSubject;
            set => SetField(ref _customSubject, value);
        }

        private bool _isTestEmailRunning;

        // ═══ Commands ═══
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }
        public ICommand SelectAllLocationsCommand { get; }
        public ICommand SelectNoLocationsCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand SendTestEmailCommand { get; }
        public ICommand AddRecipientCommand { get; }
        public ICommand RemoveRecipientCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        // ═══ Constructor ═══
        public ScheduleEditorViewModel(ScheduledSearch schedule, List<SearchLocation> locations, SearchCriteria parentCriteria, IDialogService? dialogService = null)
        {
            _schedule = schedule;
            _allLocations = locations;
            _dialogService = dialogService;

            // Commands
            AddConditionCommand = new RelayCommand(_ => AddCondition());
            RemoveConditionCommand = new RelayCommand(p => RemoveCondition(p as ConditionRowViewModel));
            SelectAllLocationsCommand = new RelayCommand(_ => SetAllLocations(true));
            SelectNoLocationsCommand = new RelayCommand(_ => SetAllLocations(false));
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            SendTestEmailCommand = new RelayCommand(_ => _ = SendTestEmailAsync(), _ => !_isTestEmailRunning);
            AddRecipientCommand = new RelayCommand(_ => AddRecipient());
            RemoveRecipientCommand = new RelayCommand(p => RemoveRecipient(p as string));
            SaveCommand = new RelayCommand(p => Save(p as Window));
            CancelCommand = new RelayCommand(p => Cancel(p as Window));

            // Initialize from schedule
            LoadFromSchedule(schedule);
        }

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
            SmtpHost = email.SmtpHost;
            SmtpPort = email.SmtpPort.ToString();
            UseSsl = email.UseSsl;
            AuthNone = email.AuthMode == SmtpAuthMode.None;
            AuthWindows = email.AuthMode == SmtpAuthMode.WindowsIntegrated;
            AuthUserPass = email.AuthMode == SmtpAuthMode.UsernamePassword;
            SmtpUsername = email.SmtpUsername;
            SmtpPassword = email.SmtpPassword;
            FromAddress = email.FromAddress;
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

            var testAuthMode = AuthWindows ? SmtpAuthMode.WindowsIntegrated
                : AuthUserPass ? SmtpAuthMode.UsernamePassword
                : SmtpAuthMode.None;

            var testConfig = new EmailNotificationConfig
            {
                SmtpHost = (SmtpHost ?? "").Trim(),
                SmtpPort = int.TryParse(SmtpPort, out int tp) ? tp : 25,
                UseSsl = UseSsl,
                AuthMode = testAuthMode,
                SmtpUsername = testAuthMode == SmtpAuthMode.UsernamePassword ? (SmtpUsername ?? "").Trim() : null,
                SmtpPassword = testAuthMode == SmtpAuthMode.UsernamePassword ? SmtpPassword : null,
                FromAddress = (FromAddress ?? "").Trim(),
                FromDisplayName = "IndiLogs 3.0"
            };

            TestEmailStatus = "Sending test email...";
            _isTestEmailRunning = true;
            try
            {
                using (var emailSvc = new EmailNotificationService())
                {
                    var (ok, msg) = await emailSvc.TestConnectionAsync(testConfig, Recipients[0]);
                    TestEmailStatus = msg;
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
                                Value = SimpleSearchText.Trim()
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
                if (string.IsNullOrWhiteSpace(SmtpHost))
                {
                    _dialogService?.ShowWarning("SMTP server is required when email is enabled.", "Validation");
                    return;
                }
                if (string.IsNullOrWhiteSpace(FromAddress) || !FromAddress.Contains("@"))
                {
                    _dialogService?.ShowWarning("A valid From address is required.", "Validation");
                    return;
                }
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

                var authMode = AuthWindows ? SmtpAuthMode.WindowsIntegrated
                    : AuthUserPass ? SmtpAuthMode.UsernamePassword
                    : SmtpAuthMode.None;

                _schedule.EmailConfig = new EmailNotificationConfig
                {
                    IsEnabled = true,
                    SmtpHost = SmtpHost.Trim(),
                    SmtpPort = int.TryParse(SmtpPort, out int port) ? port : 25,
                    UseSsl = UseSsl,
                    AuthMode = authMode,
                    SmtpUsername = authMode == SmtpAuthMode.UsernamePassword ? (SmtpUsername ?? "").Trim() : null,
                    SmtpPassword = authMode == SmtpAuthMode.UsernamePassword ? SmtpPassword : null,
                    FromAddress = FromAddress.Trim(),
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

    /// <summary>
    /// Represents a single condition row in the advanced search conditions editor.
    /// </summary>
    public class ConditionRowViewModel : ViewModelBase
    {
        public Array SearchFieldValues => Enum.GetValues(typeof(SearchField));
        public Array SearchOperatorValues => Enum.GetValues(typeof(SearchOperator));

        private SearchField _field = SearchField.Any;
        public SearchField Field
        {
            get => _field;
            set => SetField(ref _field, value);
        }

        private SearchOperator _operator = SearchOperator.Contains;
        public SearchOperator Operator
        {
            get => _operator;
            set => SetField(ref _operator, value);
        }

        private string _value = "";
        public string Value
        {
            get => _value;
            set => SetField(ref _value, value);
        }

        private bool _negate;
        public bool Negate
        {
            get => _negate;
            set => SetField(ref _negate, value);
        }
    }

    /// <summary>
    /// Represents a location checkbox item in the location list.
    /// </summary>
    public class LocationCheckItem : ViewModelBase
    {
        public Guid Id { get; set; }

        private string? _displayText;
        public string? DisplayText
        {
            get => _displayText;
            set => SetField(ref _displayText, value);
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetField(ref _isChecked, value);
        }
    }
}
