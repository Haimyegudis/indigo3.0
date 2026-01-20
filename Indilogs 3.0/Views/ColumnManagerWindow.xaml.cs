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
                if (column.Header != null && !string.IsNullOrEmpty(column.Header.ToString()))
                {
                    Columns.Add(new ColumnInfo
                    {
                        Header = column.Header.ToString(),
                        IsVisible = column.Visibility == Visibility.Visible,
                        Column = column
                    });
                }
            }
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
