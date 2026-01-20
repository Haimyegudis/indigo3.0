using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public partial class AppLogsTabControl : UserControl
    {
        public DataGrid InnerDataGrid => AppLogsGrid;

        private const string SettingsFileName = "GridColumnSettings.json";
        private string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3.0",
            SettingsFileName);

        public AppLogsTabControl()
        {
            InitializeComponent();

            // Add row loading handler for annotations
            AppLogsGrid.LoadingRow += OnRowLoading;

            // Add column management support
            AppLogsGrid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu();
                LoadColumnSettings();
            };
        }

        // Handle annotation expansion manually
        private void OnRowLoading(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                // Set initial visibility
                UpdateRowDetailsVisibility(e.Row, log);

                // IMPORTANT: Use weak event or check if already subscribed to avoid duplicates
                log.PropertyChanged -= Log_PropertyChanged;
                log.PropertyChanged += Log_PropertyChanged;

                // Store row reference in Tag for later updates
                e.Row.Tag = log;
            }
        }

        private void Log_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
                System.Diagnostics.Debug.WriteLine($"[APP ROW DETAILS] IsAnnotationExpanded={log.IsAnnotationExpanded}");

                // Find the row for this log
                Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    var row = FindRowForLog(log);
                    if (row != null)
                    {
                        UpdateRowDetailsVisibility(row, log);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private DataGridRow FindRowForLog(LogEntry log)
        {
            if (log == null) return null;

            var itemsSource = AppLogsGrid.ItemsSource as System.Collections.IList;
            if (itemsSource == null) return null;

            int index = itemsSource.IndexOf(log);
            if (index < 0) return null;

            return AppLogsGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

        private void UpdateRowDetailsVisibility(DataGridRow row, LogEntry log)
        {
            if (row == null || log == null) return;

            var newVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (row.DetailsVisibility != newVisibility)
            {
                row.DetailsVisibility = newVisibility;
                System.Diagnostics.Debug.WriteLine($"[APP ROW DETAILS] Set DetailsVisibility to: {newVisibility}");
            }
        }

        private void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.AppLogsGrid_Sorting(sender, e);
        }

        private void AppLogsGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void AppLogsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void AppLogsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_LoadingRow(sender, e);
        }

        private void AppLogsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void AttachColumnHeaderContextMenu()
        {
            AppLogsGrid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
        }

        private void DataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
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
                    string headerText = GetColumnHeaderText(column);
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

        private void ColumnVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
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

        private void AppLogsGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
        }

        private void SaveColumnSettings()
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = new ColumnSettings();

                foreach (var column in AppLogsGrid.Columns)
                {
                    string header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                gridSettings.AppColumns = columnSettings;

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                string json = JsonConvert.SerializeObject(gridSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);

                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Saved App column settings to " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Error saving App settings: " + ex.Message);
            }
        }

        private void LoadColumnSettings()
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = gridSettings.AppColumns;

                if (columnSettings == null) return;

                foreach (var column in AppLogsGrid.Columns)
                {
                    string header = GetColumnHeaderText(column);
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

                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Loaded App column settings from " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Error loading App settings: " + ex.Message);
            }
        }

        private GridSettings LoadGridSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonConvert.DeserializeObject<GridSettings>(json);
                }
                catch
                {
                    return new GridSettings();
                }
            }
            return new GridSettings();
        }
    }
}