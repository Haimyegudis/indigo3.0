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
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the Schedule Editor dialog window.
    /// Configures name, timing, search criteria, locations, time filters, output, and email.
    /// </summary>
    public partial class ScheduleEditorViewModel : ViewModelBase
    {
        private readonly ScheduledSearch _schedule;
        private readonly List<SearchLocation> _allLocations;
        private readonly IDialogService? _dialogService;
        private readonly IViewFactory? _viewFactory;
        private readonly ISearchLocationService? _locationService;
        private readonly IEmailNotificationService? _emailService;

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
        public ICommand AddLocationCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand SendTestEmailCommand { get; }
        public ICommand AddRecipientCommand { get; }
        public ICommand RemoveRecipientCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        // ═══ Constructor ═══
        public ScheduleEditorViewModel(ScheduledSearch schedule, List<SearchLocation> locations, SearchCriteria parentCriteria, IDialogService? dialogService = null, IViewFactory? viewFactory = null, ISearchLocationService? locationService = null, IEmailNotificationService? emailService = null)
        {
            _schedule = schedule;
            _allLocations = locations;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _locationService = locationService;
            _emailService = emailService;

            // Commands
            AddConditionCommand = new RelayCommand(_ => AddCondition());
            RemoveConditionCommand = new RelayCommand(p => RemoveCondition(p as ConditionRowViewModel));
            SelectAllLocationsCommand = new RelayCommand(_ => SetAllLocations(true));
            SelectNoLocationsCommand = new RelayCommand(_ => SetAllLocations(false));
            AddLocationCommand = new RelayCommand(_ => AddLocation(), _ => _viewFactory != null);
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            SendTestEmailCommand = new RelayCommand(_ => _ = SendTestEmailAsync(), _ => !_isTestEmailRunning);
            AddRecipientCommand = new RelayCommand(_ => AddRecipient());
            RemoveRecipientCommand = new RelayCommand(p => RemoveRecipient(p as string));
            SaveCommand = new RelayCommand(p => Save(p as Window));
            CancelCommand = new RelayCommand(p => Cancel(p as Window));

            // Initialize from schedule
            LoadFromSchedule(schedule);
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
