using System;
using System.Collections.ObjectModel;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ScheduleEditorViewModel
    {
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
