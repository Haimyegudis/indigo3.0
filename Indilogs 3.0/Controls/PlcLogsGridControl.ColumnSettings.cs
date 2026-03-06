using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0.Services;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Controls
{
    public partial class PlcLogsGridControl
    {
        private List<DataGridColumn>? _defaultColumns;

        private const string SettingsFileName = "GridColumnSettings.json";
        private string SettingsFilePath => AppPaths.GridColumnSettings;
        private bool _isLoadingSettings;
        private bool _settingsLoaded;

        private void ApplyColumns(IReadOnlyList<PluginColumnDef>? cols)
        {
            if (!LogsDataGrid.IsLoaded)
            {
                LogsDataGrid.Loaded += (s, e) => ApplyColumns(cols);
                return;
            }

            if (_defaultColumns == null)
                _defaultColumns = LogsDataGrid.Columns.ToList();

            LogsDataGrid.Columns.Clear();

            if (cols == null || cols.Count == 0)
            {
                foreach (var col in _defaultColumns)
                    LogsDataGrid.Columns.Add(col);
                return;
            }

            foreach (var def in cols)
            {
                Binding binding;
                string? fmt = null;

                switch (def.Field)
                {
                    case "Date":
                        fmt     = def.StringFormat ?? "yyyy-MM-dd HH:mm:ss.ffffff";
                        binding = new Binding("Date") { StringFormat = fmt };
                        break;
                    case "Level":
                    case "Message":
                    case "ThreadName":
                    case "Logger":
                    case "ProcessName":
                    case "Method":
                    case "Data":
                    case "Exception":
                    case "Pattern":
                        binding = new Binding(def.Field);
                        break;
                    default:
                        binding = new Binding($"ExtraFields[{def.Field}]");
                        break;
                }

                var col = new DataGridTextColumn
                {
                    Header   = def.Header,
                    Binding  = binding,
                    IsReadOnly = true,
                    Width    = def.Width < 0
                                   ? new DataGridLength(1, DataGridLengthUnitType.Star)
                                   : new DataGridLength(def.Width)
                };

                LogsDataGrid.Columns.Add(col);
            }
        }

        private void HookColumnResizeHandlers()
        {
            try
            {
                var headerPresenter = FindVisualChild<DataGridColumnHeadersPresenter>(LogsDataGrid);
                if (headerPresenter != null)
                {
                    var thumbs = FindVisualChildren<Thumb>(headerPresenter);
                    foreach (var thumb in thumbs)
                    {
                        thumb.DragCompleted -= Thumb_DragCompleted;
                        thumb.DragCompleted += Thumb_DragCompleted;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Hooking column resize handlers failed", ex);
            }
        }

        private void Thumb_DragCompleted(object? sender, DragCompletedEventArgs e)
        {
            SaveColumnSettings();
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) yield return found;
                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }

        private void AttachColumnHeaderContextMenu()
        {
            LogsDataGrid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
        }

        private void DataGrid_MouseRightButtonUp(object? sender, MouseButtonEventArgs e)
        {
            var depObj = e.OriginalSource as DependencyObject;

            while (depObj != null && !(depObj is DataGridColumnHeader))
            {
                if (!(depObj is System.Windows.Media.Visual || depObj is System.Windows.Media.Media3D.Visual3D))
                {
                    depObj = LogicalTreeHelper.GetParent(depObj);
                    continue;
                }
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is DataGridColumnHeader header && header.Column != null)
            {
                var contextMenu = new ContextMenu();
                contextMenu.PlacementTarget = header;
                contextMenu.Placement = PlacementMode.Bottom;

                var manageItem = new MenuItem
                {
                    Header = "☰ Manage Columns...",
                    FontWeight = FontWeights.Bold
                };
                manageItem.Click += (s, args) => ShowColumnManager();
                contextMenu.Items.Add(manageItem);
                contextMenu.Items.Add(new Separator());

                foreach (var column in LogsDataGrid.Columns)
                {
                    string? headerText = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(headerText))
                    {
                        var menuItem = new MenuItem
                        {
                            Header = headerText,
                            IsCheckable = true,
                            IsChecked = column.Visibility == Visibility.Visible,
                            Tag = column
                        };
                        menuItem.Click += ColumnVisibilityMenuItem_Click;
                        contextMenu.Items.Add(menuItem);
                    }
                }

                contextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void ColumnVisibilityMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is DataGridColumn column)
            {
                column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                SaveColumnSettings();
            }
        }

        private void ShowColumnManager()
        {
            var managerWindow = new Views.ColumnManagerWindow(LogsDataGrid);
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
            }
        }

        private void LogsDataGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            if (!_isLoadingSettings) SaveColumnSettings();
        }

        public void SaveColumnSettings()
        {
            if (_isLoadingSettings) return;
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = new ColumnSettings();

                foreach (var column in LogsDataGrid.Columns)
                {
                    string? header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                if (GridType == "APP")
                {
                    if (IsBinaryApp) gridSettings.AppColumnsS45 = columnSettings;
                    else gridSettings.AppColumns = columnSettings;
                }
                else
                {
                    if (IsBinaryApp) gridSettings.PlcColumnsS45 = columnSettings;
                    else gridSettings.PlcColumns = columnSettings;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                string json = JsonConvert.SerializeObject(gridSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Saving column settings failed", ex);
            }
        }

        private string? GetColumnHeaderText(DataGridColumn column)
        {
            if (column.Header == null)
                return null;

            if (column.Header is Button button)
            {
                string content = button.Content?.ToString() ?? "";
                if (content.StartsWith("🔍 "))
                    return content.Substring(3);
                return content;
            }

            if (column.Header is string headerString)
            {
                return headerString;
            }

            string? headerText = column.Header.ToString();

            if (string.IsNullOrEmpty(headerText) || headerText.StartsWith("System."))
                return null;

            if (headerText.Contains("."))
            {
                var parts = headerText.Split('.');
                return parts[parts.Length - 1];
            }

            return headerText;
        }

        private void LoadColumnSettings()
        {
            _isLoadingSettings = true;
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = GridType == "APP"
                    ? (IsBinaryApp ? gridSettings.AppColumnsS45 : gridSettings.AppColumns)
                    : (IsBinaryApp ? gridSettings.PlcColumnsS45 : gridSettings.PlcColumns);

                if (columnSettings == null || columnSettings.ColumnWidths.Count == 0) return;

                foreach (var column in LogsDataGrid.Columns)
                {
                    string? header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        if (columnSettings.ColumnWidths.ContainsKey(header))
                        {
                            column.Width = new DataGridLength(columnSettings.ColumnWidths[header]);
                        }

                        if (columnSettings.ColumnOrders.ContainsKey(header))
                        {
                            column.DisplayIndex = columnSettings.ColumnOrders[header];
                        }

                        if (columnSettings.ColumnVisibility.ContainsKey(header))
                        {
                            column.Visibility = columnSettings.ColumnVisibility[header] ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
                _settingsLoaded = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Loading column settings failed", ex);
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private GridSettings LoadGridSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonConvert.DeserializeObject<GridSettings>(json, AppConstants.SafeJsonSettings)
                        ?? new GridSettings();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to load grid settings: {ex.Message}");
                    return new GridSettings();
                }
            }
            return new GridSettings();
        }
    }
}
