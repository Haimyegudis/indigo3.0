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

                // Set data to grid
                DataBrowserGrid.ItemsSource = _dataTable.DefaultView;

                // Update header info
                TableNameText.Text = $"Table: {_tableName}";
                RowCountText.Text = $"{_dataTable.Rows.Count} rows × {_dataTable.Columns.Count} columns";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading table data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"{_tableName}.csv",
                    DefaultExt = ".csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    ExportToCsv(dialog.FileName);
                    MessageBox.Show($"Data exported successfully to:\n{dialog.FileName}",
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

            // Write rows
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
    }
}
