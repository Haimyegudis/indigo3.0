using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    public class LoggerNode : INotifyPropertyChanged
    {
        public string Name { get; set; }        // Node name (e.g. "indigo")
        public string FullPath { get; set; }    // Full path (e.g. "com.indigo")
        public int Count { get; set; }          // Number of logs under this node
        public ObservableCollection<LoggerNode> Children { get; set; } = new ObservableCollection<LoggerNode>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // --- Visual marking of a hidden logger ---
        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set { _isHidden = value; OnPropertyChanged(); }
        }

        // --- Visual marking of an active logger (highlighted in green) ---
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public string DisplayText => $"{Name} ({Count})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}