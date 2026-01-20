using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow : Window
    {
        private readonly string _tableName;
        private readonly byte[] _dbBytes;
        private DataTable _dataTable;
        private DataView _filteredView;

        public BrowseTableWindow(string tableName, byte[] dbBytes)
        {
            InitializeComponent();
            _tableName = tableName;
            _dbBytes = dbBytes;
            LoadTableData();
        }

        private void LoadTableData()
        {
            try
            {
                // Create a temporary database file
                string tempDbPath = Path.Combine(Path.GetTempPath(), $"temp_browse_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, _dbBytes);

                using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Version=3;"))
                {
                    connection.Open();

                    // Load table data
                    string query = $"SELECT rowid AS ID, * FROM \"{_tableName}\"";
                    using (var adapter = new SQLiteDataAdapter(query, connection))
                    {
                        _dataTable = new DataTable();
                        adapter.Fill(_dataTable);
                    }

                    connection.Close();
                }

                // Clean up temp file
                try { File.Delete(tempDbPath); } catch { }

                // Set data to grid using DataView for filtering
                _filteredView = _dataTable.DefaultView;
                DataBrowserGrid.ItemsSource = _filteredView;

                // Update header info
                TableNameText.Text = $"Table: {_tableName}";
                RowCountText.Text = $"{_dataTable.Rows.Count} rows × {_dataTable.Columns.Count} columns";
                UpdateFilteredCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading table data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
        }

        private void ApplyFilter()
        {
            if (_dataTable == null || _filteredView == null)
                return;

            string searchText = SearchTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(searchText))
            {
                _filteredView.RowFilter = string.Empty;
            }
            else
            {
                // Build filter for all columns
                var filters = new List<string>();
                foreach (DataColumn col in _dataTable.Columns)
                {
                    // Use LIKE for string matching across all column types
                    filters.Add($"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{EscapeFilterValue(searchText)}%'");
                }
                _filteredView.RowFilter = string.Join(" OR ", filters);
            }

            UpdateFilteredCount();
        }

        private string EscapeFilterValue(string value)
        {
            // Escape special characters for DataView filter
            return value.Replace("'", "''")
                        .Replace("[", "[[]")
                        .Replace("]", "[]]")
                        .Replace("*", "[*]")
                        .Replace("%", "[%]");
        }

        private void UpdateFilteredCount()
        {
            if (_filteredView == null || _dataTable == null)
                return;

            int filteredCount = _filteredView.Count;
            int totalCount = _dataTable.Rows.Count;

            if (filteredCount == totalCount)
            {
                FilteredCountText.Text = "";
            }
            else
            {
                FilteredCountText.Text = $"Showing {filteredCount} of {totalCount} rows";
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isFiltered = _filteredView != null && _filteredView.Count < _dataTable.Rows.Count;
                string prompt = isFiltered
                    ? $"Export {_filteredView.Count} filtered rows or all {_dataTable.Rows.Count} rows?"
                    : $"Export all {_dataTable.Rows.Count} rows?";

                MessageBoxResult result = MessageBoxResult.Yes;
                if (isFiltered)
                {
                    result = MessageBox.Show(
                        $"{prompt}\n\nClick Yes to export filtered data only, No to export all data.",
                        "Export Options",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel)
                        return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"{_tableName}.csv",
                    DefaultExt = ".csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    bool exportFiltered = result == MessageBoxResult.Yes && isFiltered;
                    ExportToCsv(dialog.FileName, exportFiltered);

                    int exportedRows = exportFiltered ? _filteredView.Count : _dataTable.Rows.Count;
                    MessageBox.Show($"{exportedRows} rows exported successfully to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(string filePath, bool exportFilteredOnly = false)
        {
            var sb = new StringBuilder();

            // Write headers
            var headers = _dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName);
            sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsvValue(h))));

            // Write rows (either filtered or all)
            if (exportFilteredOnly && _filteredView != null)
            {
                foreach (DataRowView rowView in _filteredView)
                {
                    var values = rowView.Row.ItemArray.Select(val => EscapeCsvValue(val?.ToString() ?? ""));
                    sb.AppendLine(string.Join(",", values));
                }
            }
            else
            {
                foreach (DataRow row in _dataTable.Rows)
                {
                    var values = row.ItemArray.Select(val => EscapeCsvValue(val?.ToString() ?? ""));
                    sb.AppendLine(string.Join(",", values));
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
