using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using IndiLogs_3._0;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow
    {
        private void ApplyFilter()
        {
            if (_tempDbPath == null || _columnNames == null)
                return;

            string? searchText = SearchTextBox.Text?.Trim();

            // OPTIMIZATION: Use SQL query instead of DataView.RowFilter - MUCH FASTER!
            System.Threading.Tasks.Task.Run(() =>
            {
                LoadDataWithSearch(searchText ?? "");
            });
        }


        private void UpdateFilteredCount()
        {
            if (_dataTable == null)
                return;

            string? searchText = SearchTextBox?.Text?.Trim();
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

        #region Column Settings Persistence

        private void DataBrowserGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            // Store original column order
            if (_originalColumnOrder == null)
                _originalColumnOrder = new List<string>();

            _originalColumnOrder.Add(e.PropertyName);

            // Move ID column to first position
            if (e.PropertyName == "ID")
            {
                e.Column.DisplayIndex = 0;
            }
        }

        private void DataBrowserGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            // Settings will be saved on window close
        }

        private void ManageColumnsButton_Click(object? sender, RoutedEventArgs e)
        {
            var managerWindow = new ColumnManagerWindow(DataBrowserGrid);
            managerWindow.Owner = this;
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                // Settings will be saved on close
            }
        }

        private void ResetColumnsButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Delete saved settings
                if (File.Exists(ColumnSettingsFilePath))
                    File.Delete(ColumnSettingsFilePath);

                // Reset column order and visibility
                for (int i = 0; i < DataBrowserGrid.Columns.Count; i++)
                {
                    DataBrowserGrid.Columns[i].DisplayIndex = i;
                    DataBrowserGrid.Columns[i].Visibility = Visibility.Visible;
                }

                // Move ID to first if it exists
                var idColumn = DataBrowserGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "ID");
                if (idColumn != null)
                    idColumn.DisplayIndex = 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("ResetColumnsButton_Click failed", ex);
            }
        }

        private void LoadColumnSettings()
        {
            try
            {
                if (!File.Exists(ColumnSettingsFilePath))
                {
                    // Just ensure ID is first
                    var idColumn = DataBrowserGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "ID");
                    if (idColumn != null)
                        idColumn.DisplayIndex = 0;
                    return;
                }

                var json = File.ReadAllText(ColumnSettingsFilePath);
                var savedSettings = JsonConvert.DeserializeObject<List<DbColumnSettingsInfo>>(json, AppConstants.SafeJsonSettings);

                if (savedSettings == null || savedSettings.Count == 0)
                    return;

                // Apply saved settings
                foreach (var col in DataBrowserGrid.Columns)
                {
                    var header = col.Header?.ToString();
                    var saved = savedSettings.FirstOrDefault(s => s.Header == header);
                    if (saved != null)
                    {
                        col.DisplayIndex = Math.Min(saved.DisplayIndex, DataBrowserGrid.Columns.Count - 1);
                        col.Width = new DataGridLength(saved.Width);
                        col.Visibility = saved.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadColumnSettings failed", ex);
            }
        }

        private void SaveColumnSettings()
        {
            try
            {
                if (DataBrowserGrid.Columns.Count == 0)
                    return;

                if (!Directory.Exists(AppPaths.Root))
                    Directory.CreateDirectory(AppPaths.Root);

                var columnSettings = DataBrowserGrid.Columns.Select(c => new DbColumnSettingsInfo
                {
                    Header = c.Header?.ToString() ?? "",
                    DisplayIndex = c.DisplayIndex,
                    Width = c.ActualWidth > 0 ? c.ActualWidth : c.Width.Value,
                    IsVisible = c.Visibility == Visibility.Visible
                }).ToList();

                var json = JsonConvert.SerializeObject(columnSettings, Formatting.Indented);
                File.WriteAllText(ColumnSettingsFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("SaveColumnSettings failed", ex);
            }
        }

        private class DbColumnSettingsInfo
        {
            public string Header { get; set; } = "";
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        #endregion
    }
}
