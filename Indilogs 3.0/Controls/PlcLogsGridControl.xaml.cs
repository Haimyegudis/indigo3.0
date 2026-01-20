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

            LogsDataGrid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu();
                LoadColumnSettings();
            };

            // ✅ FIX: Manual annotation expansion management
            LogsDataGrid.LoadingRow += OnRowLoading;
        }

        // ✅ NEW: Handle annotation expansion manually (FIXED - no duplicate handlers)
        private void OnRowLoading(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                // Set initial visibility
                UpdateRowDetailsVisibility(e.Row, log);

                // IMPORTANT: Remove handler first to prevent duplicates
                log.PropertyChanged -= Log_PropertyChanged;
                log.PropertyChanged += Log_PropertyChanged;

                // Store row reference in Tag for later updates
                e.Row.Tag = log;
            }
        }

        // Separate method to avoid closure issues and duplicates
        private void Log_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
                System.Diagnostics.Debug.WriteLine($"[ROW DETAILS] IsAnnotationExpanded={log.IsAnnotationExpanded}");

                // Find the row for this log
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var row = FindRowForLog(log);
                    if (row != null)
                    {
                        UpdateRowDetailsVisibility(row, log);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // Helper to find the DataGridRow for a specific LogEntry
        private DataGridRow FindRowForLog(LogEntry log)
        {
            if (log == null) return null;

            var itemsSource = LogsDataGrid.ItemsSource as System.Collections.IList;
            if (itemsSource == null) return null;

            int index = itemsSource.IndexOf(log);
            if (index < 0) return null;

            return LogsDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

        // ✅ NEW: Update row details visibility
        private void UpdateRowDetailsVisibility(DataGridRow row, LogEntry log)
        {
            if (row == null || log == null) return;

            var newVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (row.DetailsVisibility != newVisibility)
            {
                row.DetailsVisibility = newVisibility;
                System.Diagnostics.Debug.WriteLine($"[ROW DETAILS] Set DetailsVisibility to: {newVisibility}");
            }
        }

        private void AttachColumnHeaderContextMenu()
        {
            LogsDataGrid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
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

                foreach (var column in LogsDataGrid.Columns)
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
            var managerWindow = new Views.ColumnManagerWindow(LogsDataGrid);
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
            }
        }

        private void LogsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.MainLogsGrid_PreviewKeyDown(sender, e);
        }

        private void LogsDataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void LogsDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void LogsDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_LoadingRow(sender, e);
        }

        private void LogsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void LogsDataGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
        }

        private void SaveColumnSettings()
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = new ColumnSettings();

                foreach (var column in LogsDataGrid.Columns)
                {
                    string header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                if (GridType == "APP")
                {
                    gridSettings.AppColumns = columnSettings;
                }
                else
                {
                    gridSettings.PlcColumns = columnSettings;
                }

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