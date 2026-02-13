using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    /// <summary>
    /// Base class for log grid controls (PlcLogsGridControl, AppLogsTabControl).
    /// Provides shared functionality: annotation row management, column settings, context menus.
    /// </summary>
    public abstract class BaseLogGridControl : UserControl
    {
        /// <summary>The inner DataGrid — implemented by derived classes to return their named grid.</summary>
        public abstract DataGrid InnerDataGrid { get; }

        /// <summary>Grid type key for column settings ("PLC" or "APP"). Override in derived classes.</summary>
        protected virtual string GridSettingsKey => "PLC";

        protected const string SettingsFileName = "GridColumnSettings.json";
        protected string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3.0",
            SettingsFileName);

        /// <summary>
        /// Call from derived constructor after InitializeComponent to wire up shared events.
        /// </summary>
        protected void InitializeSharedBehavior(DataGrid grid, LogHeatmapControl heatmap = null)
        {
            grid.LoadingRow += OnRowLoading;

            grid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu(grid);
                LoadColumnSettings(grid);
            };

            if (heatmap != null)
                heatmap.RequestScrollToLog += OnHeatmapRequestScrollToLog;
        }

        #region Heatmap

        protected void OnHeatmapRequestScrollToLog(LogEntry log)
        {
            if (log == null) return;
            var grid = InnerDataGrid;
            grid.SelectedItem = log;
            grid.ScrollIntoView(log);
            grid.Focus();
        }

        #endregion

        #region Row Annotation Management

        protected void OnRowLoading(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                UpdateRowDetailsVisibility(e.Row, log);

                // Remove handler first to prevent duplicates (row recycling)
                log.PropertyChanged -= Log_PropertyChanged;
                log.PropertyChanged += Log_PropertyChanged;

                e.Row.Tag = log;
            }
        }

        private void Log_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var row = FindRowForLog(log);
                    if (row != null)
                        UpdateRowDetailsVisibility(row, log);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        protected DataGridRow FindRowForLog(LogEntry log)
        {
            if (log == null) return null;
            var grid = InnerDataGrid;
            var itemsSource = grid.ItemsSource as System.Collections.IList;
            if (itemsSource == null) return null;

            int index = itemsSource.IndexOf(log);
            if (index < 0) return null;

            return grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

        protected void UpdateRowDetailsVisibility(DataGridRow row, LogEntry log)
        {
            if (row == null || log == null) return;

            var newVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (row.DetailsVisibility != newVisibility)
                row.DetailsVisibility = newVisibility;
        }

        #endregion

        #region Column Management

        protected void AttachColumnHeaderContextMenu(DataGrid grid)
        {
            grid.MouseRightButtonUp += DataGrid_MouseRightButtonUp;
        }

        private void DataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var depObj = e.OriginalSource as DependencyObject;

            while (depObj != null && !(depObj is DataGridColumnHeader))
            {
                if (!(depObj is Visual || depObj is System.Windows.Media.Media3D.Visual3D))
                {
                    depObj = LogicalTreeHelper.GetParent(depObj);
                    continue;
                }
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is DataGridColumnHeader header && header.Column != null)
            {
                var grid = InnerDataGrid;
                var contextMenu = new ContextMenu();
                contextMenu.PlacementTarget = header;
                contextMenu.Placement = PlacementMode.Bottom;

                var manageItem = new MenuItem
                {
                    Header = "\u2630 Manage Columns...",
                    FontWeight = FontWeights.Bold
                };
                manageItem.Click += (s, args) => ShowColumnManager(grid);
                contextMenu.Items.Add(manageItem);
                contextMenu.Items.Add(new Separator());

                foreach (var column in grid.Columns)
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
                SaveColumnSettings(InnerDataGrid);
            }
        }

        private void ShowColumnManager(DataGrid grid)
        {
            var managerWindow = new Views.ColumnManagerWindow(grid);
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings(grid);
            }
        }

        protected string GetColumnHeaderText(DataGridColumn column)
        {
            if (column.Header == null) return null;

            if (column.Header is Button button)
            {
                string content = button.Content?.ToString() ?? "";
                if (content.StartsWith("\uD83D\uDD0D "))
                    return content.Substring(3);
                return content;
            }

            if (column.Header is string headerString)
                return headerString;

            string headerText = column.Header.ToString();

            if (string.IsNullOrEmpty(headerText) || headerText.StartsWith("System."))
                return null;

            if (headerText.Contains("."))
            {
                var parts = headerText.Split('.');
                return parts[parts.Length - 1];
            }

            return headerText;
        }

        #endregion

        #region Column Settings Persistence

        protected void SaveColumnSettings(DataGrid grid)
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = new ColumnSettings();

                foreach (var column in grid.Columns)
                {
                    string header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnSettings.ColumnWidths[header] = column.ActualWidth;
                        columnSettings.ColumnOrders[header] = column.DisplayIndex;
                        columnSettings.ColumnVisibility[header] = column.Visibility == Visibility.Visible;
                    }
                }

                if (GridSettingsKey == "APP")
                    gridSettings.AppColumns = columnSettings;
                else
                    gridSettings.PlcColumns = columnSettings;

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                string json = JsonConvert.SerializeObject(gridSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Settings save failure is non-critical
            }
        }

        protected void LoadColumnSettings(DataGrid grid)
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = GridSettingsKey == "APP" ? gridSettings.AppColumns : gridSettings.PlcColumns;

                if (columnSettings == null) return;

                foreach (var column in grid.Columns)
                {
                    string header = GetColumnHeaderText(column);
                    if (!string.IsNullOrEmpty(header))
                    {
                        if (columnSettings.ColumnWidths.ContainsKey(header))
                            column.Width = new DataGridLength(columnSettings.ColumnWidths[header]);

                        if (columnSettings.ColumnOrders.ContainsKey(header))
                            column.DisplayIndex = columnSettings.ColumnOrders[header];

                        if (columnSettings.ColumnVisibility.ContainsKey(header))
                            column.Visibility = columnSettings.ColumnVisibility[header] ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                // Settings load failure is non-critical
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

        #endregion
    }
}
