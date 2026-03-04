using IndiLogs_3._0;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;
using Newtonsoft.Json;
using IndiLogs_3._0.Services;

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
                new PropertyMetadata(null, OnLogsSourceChanged));

        private static void OnLogsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PlcLogsGridControl ctrl)
                ctrl.EnsureMarkedLogsHook();
        }

        public System.Collections.IEnumerable? LogsSource
        {
            get => (System.Collections.IEnumerable?)GetValue(LogsSourceProperty);
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

        // ── Plugin dynamic columns ────────────────────────────────────────────

        public static readonly DependencyProperty PluginColumnsProperty =
            DependencyProperty.Register(
                nameof(PluginColumns),
                typeof(IReadOnlyList<PluginColumnDef>),
                typeof(PlcLogsGridControl),
                new PropertyMetadata(null, OnPluginColumnsChanged));

        /// <summary>
        /// When set to a non-null list IndiLogs replaces the DataGrid's static
        /// columns with plugin-defined ones.  Set to null to restore defaults.
        /// </summary>
        public IReadOnlyList<PluginColumnDef>? PluginColumns
        {
            get => (IReadOnlyList<PluginColumnDef>?)GetValue(PluginColumnsProperty);
            set => SetValue(PluginColumnsProperty, value);
        }

        // Snapshot of the original XAML columns taken on first use
        private List<DataGridColumn>? _defaultColumns;

        private static void OnPluginColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PlcLogsGridControl)d).ApplyColumns(e.NewValue as IReadOnlyList<PluginColumnDef>);

        private void ApplyColumns(IReadOnlyList<PluginColumnDef>? cols)
        {
            // Wait until the DataGrid is actually loaded
            if (!LogsDataGrid.IsLoaded)
            {
                LogsDataGrid.Loaded += (s, e) => ApplyColumns(cols);
                return;
            }

            // Save the original XAML-defined columns the first time we are called
            if (_defaultColumns == null)
                _defaultColumns = LogsDataGrid.Columns.ToList();

            LogsDataGrid.Columns.Clear();

            // null or empty → restore XAML defaults
            if (cols == null || cols.Count == 0)
            {
                foreach (var col in _defaultColumns)
                    LogsDataGrid.Columns.Add(col);
                return;
            }

            // Build columns from plugin definitions
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
                        // ExtraFields dictionary key
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

        // ─────────────────────────────────────────────────────────────────────

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
                HookColumnResizeHandlers();
            };

            // FIX: Manual annotation expansion management
            LogsDataGrid.LoadingRow += OnRowLoading;

            // Connect heatmap scroll event
            HeatmapControl.RequestScrollToLog += OnHeatmapRequestScrollToLog;

            // Listen for ShowSyncedTimeColumn changes (DataGridColumn is not in visual tree, so XAML binding doesn't work)
            // Also watch MarkedLogs so the heat map redraws when logs are marked/unmarked
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged oldVm)
                {
                    oldVm.PropertyChanged -= OnViewModelPropertyChanged;
                }
                if (e.NewValue is INotifyPropertyChanged newVm)
                {
                    newVm.PropertyChanged += OnViewModelPropertyChanged;
                }
                EnsureMarkedLogsHook();
                UpdateHeatmapBinaryAppFlag();
            };
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender == null) return;

            if (e.PropertyName == "ShowSyncedTimeColumn")
            {
                var prop = sender.GetType().GetProperty("ShowSyncedTimeColumn");
                if (prop != null)
                {
                    bool show = (bool)(prop.GetValue(sender) ?? false);
                    var syncCol = LogsDataGrid.Columns.FirstOrDefault(c => c.Header as string == "Synced Time");
                    if (syncCol != null)
                        syncCol.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else if (e.PropertyName == "SelectedSession")
            {
                // HasBinaryAppLogs is computed from SelectedSession; re-sync the heat map flag
                UpdateHeatmapBinaryAppFlag();
            }
            else if (e.PropertyName == "MarkedLogs")
            {
                // MarkedLogs collection reference changed (e.g. session switch); re-hook
                EnsureMarkedLogsHook();
            }
        }

        /// <summary>
        /// Reads HasBinaryAppLogs from the current DataContext and pushes the value
        /// to the embedded HeatmapControl so it colours ==== STATE messages correctly.
        /// Called on DataContext change and on SelectedSession property change.
        /// </summary>
        private void UpdateHeatmapBinaryAppFlag()
        {
            var vm = DataContext;
            if (vm == null) { HeatmapControl.IsBinaryApp = false; return; }
            var prop = vm.GetType().GetProperty("HasBinaryAppLogs");
            bool val = prop != null && (bool)(prop.GetValue(vm) ?? false);
            HeatmapControl.IsBinaryApp = val;
        }

        /// <summary>Redraw heat map when a log is marked or unmarked.</summary>
        private void OnMarkedLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            HeatmapControl.ScheduleRedraw();
        }

        private INotifyCollectionChanged? _hookedMarkedLogs;

        /// <summary>
        /// Ensures the MarkedLogs collection-changed handler is subscribed to the
        /// current DataContext's MarkedLogs property (guards against null DataContext
        /// on first load and re-subscription when LogsSource changes).
        /// </summary>
        private void EnsureMarkedLogsHook()
        {
            var vm = DataContext;
            if (vm == null) return;

            var markedProp = vm.GetType().GetProperty("MarkedLogs");
            if (markedProp == null)
                return;

            var coll = markedProp.GetValue(vm) as INotifyCollectionChanged;
            if (coll == null)
                return;
            if (ReferenceEquals(coll, _hookedMarkedLogs)) return;

            // Unsubscribe from previous collection
            if (_hookedMarkedLogs != null)
                _hookedMarkedLogs.CollectionChanged -= OnMarkedLogsCollectionChanged;

            _hookedMarkedLogs = coll;
            coll.CollectionChanged += OnMarkedLogsCollectionChanged;
        }

        private void HookColumnResizeHandlers()
        {
            // Find all Thumb controls in column headers (used for resize handles)
            // and listen to DragCompleted to save column widths after resize
            try
            {
                var headerPresenter = FindVisualChild<DataGridColumnHeadersPresenter>(LogsDataGrid);
                if (headerPresenter != null)
                {
                    var thumbs = FindVisualChildren<Thumb>(headerPresenter);
                    foreach (var thumb in thumbs)
                    {
                        thumb.DragCompleted -= Thumb_DragCompleted; // prevent duplicates
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
            // Column resize finished — save settings
            SaveColumnSettings();
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
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
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) yield return found;
                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }

        private void OnHeatmapRequestScrollToLog(LogEntry log)
        {
            // Select and scroll to the log entry
            LogsDataGrid.SelectedItem = log;
            LogsDataGrid.ScrollIntoView(log);

            // Focus the grid so user can continue navigating
            LogsDataGrid.Focus();
        }

        // NEW: Handle annotation expansion manually (FIXED - no duplicate handlers)
        private void OnRowLoading(object? sender, DataGridRowEventArgs e)
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
        private void Log_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
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
        private DataGridRow? FindRowForLog(LogEntry log)
        {
            var itemsSource = LogsDataGrid.ItemsSource as System.Collections.IList;
            if (itemsSource == null) return null;

            int index = itemsSource.IndexOf(log);
            if (index < 0) return null;

            return LogsDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

        // NEW: Update row details visibility
        private void UpdateRowDetailsVisibility(DataGridRow row, LogEntry log)
        {
            var newVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (row.DetailsVisibility != newVisibility)
            {
                row.DetailsVisibility = newVisibility;
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

        private void LogsDataGrid_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.MainLogsGrid_PreviewKeyDown(sender, e);

            // Space = mark/unmark rows. Redraw heatmap so green marks appear immediately.
            // This is safe: always called on the UI thread (keypress).
            if (e.Key == Key.Space)
                HeatmapControl.ScheduleRedraw();
        }

        private void LogsDataGrid_RequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void LogsDataGrid_Loaded(object? sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void LogsDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.DataGrid_LoadingRow(sender, e);
        }

        private void LogsDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
        }

        // Column filter button click handlers – mirrors AppLogsTabControl pattern.
        // DataGrid column headers live outside the normal visual tree, so Command
        // bindings with RelativeSource AncestorType=DataGrid fail silently.
        // Using Click handlers with manual DataContext resolution works reliably.
        private void ThreadFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.OpenThreadFilterCommand?.Execute(sender as FrameworkElement);
            e.Handled = true;
        }

        private void LogsDataGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
        }

        public void SaveColumnSettings()
        {
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
                    gridSettings.AppColumns = columnSettings;
                }
                else
                {
                    gridSettings.PlcColumns = columnSettings;
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
        private void LoadColumnSettings()
        {
            try
            {
                GridSettings gridSettings = LoadGridSettings();
                var columnSettings = GridType == "APP" ? gridSettings.AppColumns : gridSettings.PlcColumns;

                if (columnSettings == null) return;

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
            }
            catch (Exception ex)
            {
                AppLogger.Error("Loading column settings failed", ex);
            }
        }

        private GridSettings LoadGridSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonConvert.DeserializeObject<GridSettings>(json, new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth })
                        ?? new GridSettings();
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
