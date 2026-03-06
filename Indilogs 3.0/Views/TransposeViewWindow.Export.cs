using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Services;
using Microsoft.Win32;

namespace IndiLogs_3._0.Views
{
    public partial class TransposeViewWindow
    {
        #region Row Visibility (Hide Rows)

        private void BtnManageRows_Click(object? sender, RoutedEventArgs e)
        {
            // Create a simple dialog to select which rows to hide
            var dialog = new Window
            {
                Title = "Hide/Show Property Rows",
                Width = 350,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (System.Windows.Media.Brush)FindResource("BgDark"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
            };

            var mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new TextBlock
            {
                Text = "Check properties to show, uncheck to hide:",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.Bold
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Scrollable list of checkboxes
            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stackPanel = new StackPanel();
            var checkBoxes = new Dictionary<string, CheckBox>();

            foreach (var prop in PropertyList)
            {
                var cb = new CheckBox
                {
                    Content = prop.Name,
                    IsChecked = !_hiddenProperties.Contains(prop.Name),
                    Margin = new Thickness(0, 3, 0, 3),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
                };
                checkBoxes[prop.Name] = cb;
                stackPanel.Children.Add(cb);
            }

            scrollViewer.Content = stackPanel;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var btnSelectAll = new Button { Content = "Select All", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
            btnSelectAll.Click += (s, args) =>
            {
                foreach (var cb in checkBoxes.Values)
                    cb.IsChecked = true;
            };

            var btnSelectNone = new Button { Content = "Select None", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
            btnSelectNone.Click += (s, args) =>
            {
                foreach (var cb in checkBoxes.Values)
                    cb.IsChecked = false;
            };

            var btnOk = new Button { Content = "OK", Width = 70, Margin = new Thickness(10, 0, 5, 0) };
            btnOk.Click += (s, args) =>
            {
                _hiddenProperties.Clear();
                foreach (var kvp in checkBoxes)
                {
                    if (kvp.Value.IsChecked != true)
                        _hiddenProperties.Add(kvp.Key);
                }
                RefreshDisplay();
                dialog.Close();
            };

            var btnCancel = new Button { Content = "Cancel", Width = 70 };
            btnCancel.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(btnSelectAll);
            buttonPanel.Children.Add(btnSelectNone);
            buttonPanel.Children.Add(btnOk);
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;
            dialog.ShowDialog();
        }

        #endregion

        #region Export

        private void BtnExportTranspose_Click(object? sender, RoutedEventArgs e)
        {
            if (_transposeTable == null || _transposeTable.Rows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"PrintAnalysis_Transpose_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportToCsv(dialog.FileName);
                    MessageBox.Show($"Exported to:\n{dialog.FileName}", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("CSV export failed", ex);
                    MessageBox.Show($"Error exporting:\n{ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();

            // Header
            var headers = new List<string>();
            foreach (DataColumn col in _transposeTable!.Columns)
            {
                headers.Add(EscapeCsv(col.ColumnName.Replace("\n", " ")));
            }
            sb.AppendLine(string.Join(",", headers));

            // Data - only export visible rows
            foreach (DataRow row in _transposeTable.Rows)
            {
                var propertyName = row["Property"]?.ToString() ?? "";
                if (_hiddenProperties.Contains(propertyName))
                    continue;

                var values = new List<string>();
                foreach (var item in row.ItemArray)
                {
                    values.Add(EscapeCsv(item?.ToString() ?? ""));
                }
                sb.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        #endregion
    }
}
