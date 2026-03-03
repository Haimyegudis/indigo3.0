using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Represents a single parameter row in the SYSTAB DataGrid.
    /// Each entry compares values across systab_saved, systab_default, systab_minimum, and systab_maximum files.
    /// </summary>
    public class SystabEntry
    {
        public string Parameter { get; set; } = "";
        public string Saved { get; set; } = "";
        public string Default { get; set; } = "";
        public string Minimum { get; set; } = "";
        public string Maximum { get; set; } = "";

        /// <summary>
        /// True when Saved != Default, used for row highlighting in the DataGrid.
        /// </summary>
        public bool IsDifferent { get; set; }
    }

    /// <summary>
    /// Represents a node in the SYSTAB TreeView.
    /// Top-level nodes are Topics, child nodes are "Station|Index" keys.
    /// Leaf nodes contain the parameter entries.
    /// </summary>
    public class SystabTopicNode : INotifyPropertyChanged
    {
        private string _name = "";
        private string _fullPath = "";
        private bool _isExpanded;
        private bool _isSelected;
        private bool _hasDifferences;

        /// <summary>
        /// Display name (topic name or "Station|Index" key).
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Full path for identification (e.g., "Topic/Station|Index").
        /// </summary>
        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this is a top-level Topic node (true) or a Station|Index leaf (false).
        /// </summary>
        public bool IsTopLevel { get; set; }

        public ObservableCollection<SystabTopicNode> Children { get; set; } = new ObservableCollection<SystabTopicNode>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether any entry in this node (or its children) has Saved != Default.
        /// </summary>
        public bool HasDifferences
        {
            get => _hasDifferences;
            set { _hasDifferences = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Parameter entries at this node level (leaf nodes only).
        /// </summary>
        public List<SystabEntry> Entries { get; set; } = new List<SystabEntry>();

        /// <summary>
        /// Total parameter count for this node and all descendants.
        /// </summary>
        public int Count { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
