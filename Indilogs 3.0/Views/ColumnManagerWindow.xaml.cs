using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0.Views
{
    public partial class ColumnManagerWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private ObservableCollection<ColumnInfo> _columns;
        public ObservableCollection<ColumnInfo> Columns
        {
            get => _columns;
            set
            {
                _columns = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Columns)));
            }
        }

        public bool WasApplied { get; private set; }

        public ColumnManagerWindow(DataGrid dataGrid)
        {
            InitializeComponent();
            DataContext = this;

            // Populate columns from DataGrid
            Columns = new ObservableCollection<ColumnInfo>();
            foreach (var column in dataGrid.Columns)
            {
                string headerText = GetColumnHeaderText(column);
                if (!string.IsNullOrEmpty(headerText))
                {
                    Columns.Add(new ColumnInfo
                    {
                        Header = headerText,
                        IsVisible = column.Visibility == Visibility.Visible,
                        Column = column
                    });
                }
            }
        }

        /// <summary>
        /// Extracts the display text from a column header, handling buttons and complex headers
        /// </summary>
        private string GetColumnHeaderText(DataGridColumn column)
        {
            if (column.Header == null)
                return null;

            // If header is a Button, extract just the text content
            if (column.Header is Button button)
            {
                string content = button.Content?.ToString() ?? "";
                // Remove filter icon prefix if present (e.g., "🔍 Logger" -> "Logger")
                if (content.StartsWith("🔍 "))
                    return content.Substring(3);
                return content;
            }

            // If header is a string, return it directly
            if (column.Header is string headerString)
            {
                return headerString;
            }

            // For other types, try ToString and extract the last part if it's a path
            string headerText = column.Header.ToString();

            // Skip empty or type name strings
            if (string.IsNullOrEmpty(headerText) || headerText.StartsWith("System."))
                return null;

            // If it looks like a dotted path (e.g., "System.Window.Control.Button"), take the last part
            if (headerText.Contains("."))
            {
                var parts = headerText.Split('.');
                return parts[parts.Length - 1];
            }

            return headerText;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Apply visibility changes
            foreach (var columnInfo in Columns)
            {
                columnInfo.Column.Visibility = columnInfo.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            WasApplied = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset all columns to visible
            foreach (var columnInfo in Columns)
            {
                columnInfo.IsVisible = true;
            }
        }
    }

    public class ColumnInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isVisible;
        public string Header { get; set; }
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
        public DataGridColumn Column { get; set; }
    }
}
