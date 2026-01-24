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
        private string _tempDbPath;
        private List<string> _columnNames;
        private long _totalRowCount;

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
                // OPTIMIZATION: Load data asynchronously using SQL query directly
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // Create a temporary database file (keep it for searches)
                        _tempDbPath = Path.Combine(Path.GetTempPath(), $"temp_browse_{Guid.NewGuid()}.db");
                        File.WriteAllBytes(_tempDbPath, _dbBytes);

                        using (var connection = new SQLiteConnection($"Data Source={_tempDbPath};Version=3;"))
                        {
                            connection.Open();

                            // Get total row count
                            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM \"{_tableName}\"", connection))
                            {
                                _totalRowCount = (long)cmd.ExecuteScalar();
                            }

                            // Get column names for search
                            _columnNames = new List<string>();
                            using (var cmd = new SQLiteCommand($"PRAGMA table_info([{_tableName}])", connection))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    _columnNames.Add(reader.GetString(1)); // Column name is at index 1
                                }
                            }

                            connection.Close();
                        }

                        // Load initial data (empty search = all rows, limited to 10000)
                        LoadDataWithSearch("");
                    }
                    catch (Exception ex)
                    {
                        // Clean up temp file on error
                        CleanupTempFile();

                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Error loading table data: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting table load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadDataWithSearch(string searchText)
        {
            try
            {
                DataTable dataTable = null;

                using (var connection = new SQLiteConnection($"Data Source={_tempDbPath};Version=3;"))
                {
                    connection.Open();

                    string query;
                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        // No search - load first 10000 rows
                        if (_totalRowCount > 10000)
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{_tableName}\" LIMIT 10000";
                            System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Loading first 10000 of {_totalRowCount} rows");
                        }
                        else
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{_tableName}\"";
                        }
                    }
                    else
                    {
                        // OPTIMIZATION: Use SQL LIKE for search - MUCH faster than DataView.RowFilter
                        var whereConditions = new List<string>();
                        string escapedSearch = searchText.Replace("'", "''");

                        foreach (var colName in _columnNames)
                        {
                            // Use CAST to convert all columns to text for searching
                            whereConditions.Add($"CAST([{colName}] AS TEXT) LIKE '%{escapedSearch}%'");
                        }

                        query = $"SELECT rowid AS ID, * FROM \"{_tableName}\" WHERE {string.Join(" OR ", whereConditions)} LIMIT 10000";
                        System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Searching for '{searchText}'");
                    }

                    using (var adapter = new SQLiteDataAdapter(query, connection))
                    {
                        dataTable = new DataTable();
                        adapter.Fill(dataTable);
                    }

                    connection.Close();
                }

                // Update UI on main thread
                Dispatcher.Invoke(() =>
                {
                    _dataTable = dataTable;
                    _filteredView = _dataTable.DefaultView;
                    DataBrowserGrid.ItemsSource = _filteredView;

                    // Update header info
                    TableNameText.Text = $"Table: {_tableName}";

                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        RowCountText.Text = $"{_dataTable.Rows.Count:N0} rows × {_dataTable.Columns.Count} columns";
                    }
                    else
                    {
                        RowCountText.Text = $"Found {_dataTable.Rows.Count:N0} rows × {_dataTable.Columns.Count} columns";
                    }

                    UpdateFilteredCount();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB BROWSE ERROR] {ex.Message}");
            }
        }

        private void CleanupTempFile()
        {
            try
            {
                if (_tempDbPath != null && File.Exists(_tempDbPath))
                {
                    File.Delete(_tempDbPath);
                }
            }
            catch { }
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
            if (_tempDbPath == null || _columnNames == null)
                return;

            string searchText = SearchTextBox.Text?.Trim();

            // OPTIMIZATION: Use SQL query instead of DataView.RowFilter - MUCH FASTER!
            System.Threading.Tasks.Task.Run(() =>
            {
                LoadDataWithSearch(searchText);
            });
        }


        private void UpdateFilteredCount()
        {
            if (_dataTable == null)
                return;

            string searchText = SearchTextBox?.Text?.Trim();
            int currentCount = _dataTable.Rows.Count;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // No search active
                if (_totalRowCount > 10000)
                {
                    FilteredCountText.Text = $"Showing first 10,000 of {_totalRowCount:N0} total rows";
                }
                else
                {
                    FilteredCountText.Text = "";
                }
            }
            else
            {
                // Search active
                FilteredCountText.Text = $"Found {currentCount:N0} matching rows (limited to 10,000)";
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataTable == null || _dataTable.Rows.Count == 0)
                {
                    MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    ExportToCsv(dialog.FileName);

                    MessageBox.Show($"{_dataTable.Rows.Count} rows exported successfully to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();

            // Write headers
            var headers = _dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName);
            sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsvValue(h))));

            // Write currently displayed rows
            foreach (DataRow row in _dataTable.Rows)
            {
                var values = row.ItemArray.Select(val => EscapeCsvValue(val?.ToString() ?? ""));
                sb.AppendLine(string.Join(",", values));
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            CleanupTempFile();
        }
    }
}
