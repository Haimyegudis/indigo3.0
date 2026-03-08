using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0.Models
{
    public enum NodeType { Group, Condition }

    public class FilterNode : INotifyPropertyChanged
    {
        private NodeType _type;
        private string _logicalOperator = "AND";
        private string _field = "Message";
        private string _operator = "Contains";
        private string _value = "";

        // Cached compiled regex for "Regex" operator — avoids per-entry recompilation
        [Newtonsoft.Json.JsonIgnore]
        private Regex? _compiledRegex;
        [Newtonsoft.Json.JsonIgnore]
        private string? _compiledRegexPattern;

        [Newtonsoft.Json.JsonIgnore]
        public Regex? CompiledRegex
        {
            get
            {
                if (_operator != "Regex" || string.IsNullOrEmpty(_value))
                    return null;
                if (_compiledRegex == null || _compiledRegexPattern != _value)
                {
                    _compiledRegexPattern = _value;
                    try { _compiledRegex = new Regex(_value, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)); }
                    catch (ArgumentException) { _compiledRegex = null; }
                }
                return _compiledRegex;
            }
        }

        public NodeType Type { get => _type; set { _type = value; OnPropertyChanged(); } }
        public string LogicalOperator { get => _logicalOperator; set { _logicalOperator = value; OnPropertyChanged(); } }
        public ObservableCollection<FilterNode> Children { get; set; } = new ObservableCollection<FilterNode>();
        public string Field { get => _field; set { _field = value; OnPropertyChanged(); } }
        public string Operator { get => _operator; set { _operator = value; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value; _compiledRegex = null; _compiledRegexPattern = null; OnPropertyChanged(); } }

        /// <summary>
        /// When false, this condition is temporarily disabled (cleared) but not deleted.
        /// Reloading a config will re-enable it.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsEnabled { get; set; } = true;

        public FilterNode DeepClone()
        {
            var clone = new FilterNode
            {
                Type = this.Type,
                LogicalOperator = this.LogicalOperator,
                Field = this.Field,
                Operator = this.Operator,
                Value = this.Value,
                Children = new ObservableCollection<FilterNode>()
            };
            foreach (var child in this.Children) clone.Children.Add(child.DeepClone());
            return clone;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}