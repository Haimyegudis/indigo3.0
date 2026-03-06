using System.Collections.ObjectModel;
using System.ComponentModel;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel wrapper for a <see cref="SearchConditionGroup"/> (observable for UI binding).
    /// </summary>
    public class ConditionGroupVM : INotifyPropertyChanged
    {
        private ConditionOperator _operator = ConditionOperator.And;
        public ConditionOperator Operator
        {
            get => _operator;
            set { _operator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operator))); }
        }

        public ObservableCollection<ConditionVM> Conditions { get; } = new ObservableCollection<ConditionVM>();

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// ViewModel wrapper for a single <see cref="SearchCondition"/> (observable for UI binding).
    /// </summary>
    public class ConditionVM : INotifyPropertyChanged
    {
        private SearchField _field = SearchField.Any;
        public SearchField Field
        {
            get => _field;
            set { _field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Field))); }
        }

        private SearchOperator _operator = SearchOperator.Contains;
        public SearchOperator Operator
        {
            get => _operator;
            set { _operator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operator))); }
        }

        private string? _value;
        public string? Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }

        private bool _negate;
        public bool Negate
        {
            get => _negate;
            set { _negate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Negate))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
