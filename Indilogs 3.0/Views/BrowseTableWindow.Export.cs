using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow
    {
        private void ExportButton_Click(object? sender, RoutedEventArgs e)
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
            if (_dataTable == null) return;
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
    }
}
