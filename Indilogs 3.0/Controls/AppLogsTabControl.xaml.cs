using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0;
using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public partial class AppLogsTabControl : UserControl
    {
        public DataGrid InnerDataGrid => AppLogsGrid;

        private const string SettingsFileName = "GridColumnSettings.json";
        private string SettingsFilePath => AppPaths.GridColumnSettings;
        private bool _isLoadingSettings;
        private bool _settingsLoaded;

        public static readonly DependencyProperty IsBinaryAppProperty =
            DependencyProperty.Register(
                nameof(IsBinaryApp),
                typeof(bool),
                typeof(AppLogsTabControl),
                new PropertyMetadata(false, OnIsBinaryAppChanged));

        public bool IsBinaryApp
        {
            get => (bool)GetValue(IsBinaryAppProperty);
            set => SetValue(IsBinaryAppProperty, value);
        }

        private static void OnIsBinaryAppChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (AppLogsTabControl)d;
            ctrl._settingsLoaded = false;
            ctrl.LoadColumnSettings();
        }

        public AppLogsTabControl()
        {
            InitializeComponent();

            // Add row loading handler for annotations
            AppLogsGrid.LoadingRow += OnRowLoading;

            // Add column management support
            AppLogsGrid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu();
                if (!_settingsLoaded) LoadColumnSettings();
                HookColumnResizeHandlers();
            };

            // Connect heatmap scroll event
            HeatmapControl.RequestScrollToLog += OnHeatmapRequestScrollToLog;
        }

        private void AppLogsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridRow)
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(dep)
                    : LogicalTreeHelper.GetParent(dep);
            if (dep is DataGridRow row)
                AppLogsGrid.SelectedItem = row.DataContext;
        }

        private void OnHeatmapRequestScrollToLog(LogEntry log)
        {
            if (log == null) return;
            AppLogsGrid.SelectedItem = log;
            AppLogsGrid.ScrollIntoView(log);
            AppLogsGrid.Focus();
        }

        // Handle annotation expansion manually
        private void OnRowLoading(object? sender, DataGridRowEventArgs e)
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

        private void Log_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
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

        private DataGridRow? FindRowForLog(LogEntry log)
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
            }
        }

        private void AppLogsGrid_Sorting(object? sender, DataGridSortingEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.AppLogsGrid_Sorting(sender, e);
        }

        private void AppLogsGrid_RequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void AppLogsGrid_Loaded(object? sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void AppLogsGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.DataGrid_LoadingRow(sender, e);
        }

        private void AppLogsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {

        }

        // Column filter button click handlers - pass button as parameter for positioning
        private void LoggerFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.OpenLoggerFilterCommand?.Execute(sender as FrameworkElement);
            e.Handled = true;
        }

        private void ThreadFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.OpenThreadFilterCommand?.Execute(sender as FrameworkElement);
            e.Handled = true;
        }

        private void MethodFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.OpenMethodFilterCommand?.Execute(sender as FrameworkElement);
            e.Handled = true;
        }

        private void ActionsButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

    }
}