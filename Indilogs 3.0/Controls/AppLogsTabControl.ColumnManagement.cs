using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Controls
{
    public partial class AppLogsTabControl
    {
        private void AttachColumnHeaderContextMenu()
        {
            AppLogsGrid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
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

                foreach (var column in AppLogsGrid.Columns)
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
            var managerWindow = new Views.ColumnManagerWindow(AppLogsGrid);
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
            }
        }

        /// <summary>
        /// Extracts the display text from a column header, handling buttons and complex headers
        /// </summary>
        private string? GetColumnHeaderText(DataGridColumn column)
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
            string? headerText = column.Header.ToString();

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

        private void AppLogsGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            if (!_isLoadingSettings) SaveColumnSettings();
        }

        private void HookColumnResizeHandlers()
        {
            try
            {
                var headerPresenter = FindVisualChild<DataGridColumnHeadersPresenter>(AppLogsGrid);
                if (headerPresenter == null) return;
                foreach (var thumb in FindVisualChildren<Thumb>(headerPresenter))
                {
                    thumb.DragCompleted -= Thumb_DragCompleted;
                    thumb.DragCompleted += Thumb_DragCompleted;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Hooking column resize handlers failed", ex);
            }
        }

        private void Thumb_DragCompleted(object? sender, DragCompletedEventArgs e) => SaveColumnSettings();

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var desc in FindVisualChildren<T>(child))
                    yield return desc;
            }
        }

        public void SaveColumnSettings()
        {
            if (_isLoadingSettings) return;
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = new ColumnSettings();

                foreach (var column in AppLogsGrid.Columns)
                {
                    string? header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                if (IsBinaryApp) gridSettings.AppColumnsS45 = columnSettings;
                else gridSettings.AppColumns = columnSettings;

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                string json = JsonConvert.SerializeObject(gridSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Saving column settings failed", ex);
            }
        }

        private void LoadColumnSettings()
        {
            _isLoadingSettings = true;
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = IsBinaryApp ? gridSettings.AppColumnsS45 : gridSettings.AppColumns;

                if (columnSettings == null || columnSettings.ColumnWidths.Count == 0) return;

                foreach (var column in AppLogsGrid.Columns)
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
                    return JsonConvert.DeserializeObject<GridSettings>(json, AppConstants.SafeJsonSettings) ?? new GridSettings();
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
