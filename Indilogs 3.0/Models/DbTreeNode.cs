using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    public class DbTreeNode : INotifyPropertyChanged
    {
        private bool _isExpanded = false;
        private bool _isVisible = true;
        private string _name;
        private string _type;
        private string _schema;

        // Name column (table name, column name, or value identifier)
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        // Type column (INTEGER, TEXT, REAL, BLOB, etc.)
        public string Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        // Schema column (CREATE TABLE statement or column definition)
        public string Schema
        {
            get => _schema;
            set { _schema = value; OnPropertyChanged(); }
        }

        // Node type: "Root", "Table", "Column", "DataRow"
        public string NodeType { get; set; }

        // Store reference to parent database file name
        public string DatabaseFileName { get; set; }

        // For data rows - stores the actual row data as key-value pairs
        public ObservableCollection<DbFieldValue> FieldValues { get; set; } = new ObservableCollection<DbFieldValue>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DbTreeNode> Children { get; set; } = new ObservableCollection<DbTreeNode>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Represents a field value in a data row
    public class DbFieldValue : INotifyPropertyChanged
    {
        public string ColumnName { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
