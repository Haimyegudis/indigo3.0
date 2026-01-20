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
    public partial class PlcLogsGridControl : UserControl
    {
        public DataGrid InnerDataGrid => LogsDataGrid;

        // DependencyProperty for ItemsSource
        public static readonly DependencyProperty LogsSourceProperty =
            DependencyProperty.Register(
                nameof(LogsSource),
                typeof(System.Collections.IEnumerable),
                typeof(PlcLogsGridControl),
                new PropertyMetadata(null));

        public System.Collections.IEnumerable LogsSource
        {
            get => (System.Collections.IEnumerable)GetValue(LogsSourceProperty);
            set => SetValue(LogsSourceProperty, value);
        }

        // DependencyProperty to identify grid type (PLC or APP)
        public static readonly DependencyProperty GridTypeProperty =
            DependencyProperty.Register(
                nameof(GridType),
                typeof(string),
                typeof(PlcLogsGridControl),
                new PropertyMetadata("PLC"));

        public string GridType
        {
            get => (string)GetValue(GridTypeProperty);
            set => SetValue(GridTypeProperty, value);
        }

        private const string SettingsFileName = "GridColumnSettings.json";
        private string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3.0",
            SettingsFileName);

        public PlcLogsGridControl()
        {
            InitializeComponent();

            // Add column header right-click handler
            LogsDataGrid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu();
                LoadColumnSettings();
            };
        }

        private void AttachColumnHeaderContextMenu()
        {
            LogsDataGrid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
        }

        private void DataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var depObj = e.OriginalSource as DependencyObject;

            // Check if clicked on column header
            while (depObj != null && !(depObj is DataGridColumnHeader))
            {
                // Skip non-visual elements (like Run, which is inside TextBlock)
                if (!(depObj is System.Windows.Media.Visual || depObj is System.Windows.Media.Media3D.Visual3D))
                {
                    // Try to get the parent from LogicalTree instead
                    depObj = LogicalTreeHelper.GetParent(depObj);
                    continue;
                }

                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is DataGridColumnHeader header && header.Column != null)
            {
                // Show column visibility context menu
                var contextMenu = new ContextMenu();
                contextMenu.PlacementTarget = header;
                contextMenu.Placement = PlacementMode.Bottom;

                // Add menu item for managing columns
                var manageItem = new MenuItem
                {
                    Header = "☰ Manage Columns...",
                    FontWeight = FontWeights.Bold
                };
                manageItem.Click += (s, args) => ShowColumnManager();
                contextMenu.Items.Add(manageItem);

                contextMenu.Items.Add(new Separator());

                // Add menu items for each column
                foreach (var column in LogsDataGrid.Columns)
                {
                    if (column.Header != null && !string.IsNullOrEmpty(column.Header.ToString()))
                    {
                        var menuItem = new MenuItem
                        {
                            Header = column.Header.ToString(),
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
            var managerWindow = new Views.ColumnManagerWindow(LogsDataGrid);
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
            }
        }

        private void LogsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Delegate to parent handler if needed
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.MainLogsGrid_PreviewKeyDown(sender, e);
        }

        private void LogsDataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void LogsDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Delegate to parent handler if needed
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void LogsDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // Delegate to parent handler if needed
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_LoadingRow(sender, e);
        }

        private void LogsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void LogsDataGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            // Save column order
            SaveColumnSettings();
        }

        private void SaveColumnSettings()
        {
            try
            {
                // Load existing settings or create new
                GridSettings gridSettings = LoadGridSettings();

                // Determine which settings to update (PLC or APP)
                var columnSettings = new ColumnSettings();

                // Save column widths
                foreach (var column in LogsDataGrid.Columns)
                {
                    if (column.Header != null && !string.IsNullOrEmpty(column.Header.ToString()))
                    {
                        string header = column.Header.ToString();
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                // Update the appropriate settings based on GridType
                if (GridType == "APP")
                {
                    gridSettings.AppColumns = columnSettings;
                }
                else
                {
                    gridSettings.PlcColumns = columnSettings;
                }

                // Save to file
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                string json = JsonConvert.SerializeObject(gridSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);

                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Saved column settings to " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Error saving settings: " + ex.Message);
            }
        }

        private void LoadColumnSettings()
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = GridType == "APP" ? gridSettings.AppColumns : gridSettings.PlcColumns;

                if (columnSettings == null) return;

                // Apply column widths
                foreach (var column in LogsDataGrid.Columns)
                {
                    if (column.Header != null && !string.IsNullOrEmpty(column.Header.ToString()))
                    {
                        string header = column.Header.ToString();

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

                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Loaded column settings from " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[COLUMN SETTINGS] Error loading settings: " + ex.Message);
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
