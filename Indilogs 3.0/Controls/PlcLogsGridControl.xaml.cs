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

        public static readonly DependencyProperty IsBinaryAppProperty =
            DependencyProperty.Register(
                nameof(IsBinaryApp),
                typeof(bool),
                typeof(PlcLogsGridControl),
                new PropertyMetadata(false, OnIsBinaryAppChanged));

        public bool IsBinaryApp
        {
            get => (bool)GetValue(IsBinaryAppProperty);
            set => SetValue(IsBinaryAppProperty, value);
        }

        private static void OnIsBinaryAppChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (PlcLogsGridControl)d;
            ctrl._settingsLoaded = false;
            ctrl.LoadColumnSettings();
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

        private static void OnPluginColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PlcLogsGridControl)d).ApplyColumns(e.NewValue as IReadOnlyList<PluginColumnDef>);

        // ─────────────────────────────────────────────────────────────────────

        public PlcLogsGridControl()
        {
            InitializeComponent();

            LogsDataGrid.Loaded += (s, e) =>
            {
                AttachColumnHeaderContextMenu();
                if (!_settingsLoaded) LoadColumnSettings();
                HookColumnResizeHandlers();
            };

            LogsDataGrid.LoadingRow += OnRowLoading;

            HeatmapControl.RequestScrollToLog += OnHeatmapRequestScrollToLog;

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

        private void LogsDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridRow)
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(dep)
                    : LogicalTreeHelper.GetParent(dep);
            if (dep is DataGridRow row)
                LogsDataGrid.SelectedItem = row.DataContext;
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
                UpdateHeatmapBinaryAppFlag();
            }
            else if (e.PropertyName == "MarkedLogs")
            {
                EnsureMarkedLogsHook();
            }
        }

        private void UpdateHeatmapBinaryAppFlag()
        {
            var vm = DataContext;
            if (vm == null) { HeatmapControl.IsBinaryApp = false; return; }
            var prop = vm.GetType().GetProperty("HasBinaryAppLogs");
            bool val = prop != null && (bool)(prop.GetValue(vm) ?? false);
            HeatmapControl.IsBinaryApp = val;
        }

        private void OnMarkedLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            HeatmapControl.ScheduleRedraw();
        }

        private INotifyCollectionChanged? _hookedMarkedLogs;

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

            if (_hookedMarkedLogs != null)
                _hookedMarkedLogs.CollectionChanged -= OnMarkedLogsCollectionChanged;

            _hookedMarkedLogs = coll;
            coll.CollectionChanged += OnMarkedLogsCollectionChanged;
        }

        private void OnHeatmapRequestScrollToLog(LogEntry log)
        {
            LogsDataGrid.SelectedItem = log;
            LogsDataGrid.ScrollIntoView(log);
            LogsDataGrid.Focus();
        }

        private void OnRowLoading(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                UpdateRowDetailsVisibility(e.Row, log);

                log.PropertyChanged -= Log_PropertyChanged;
                log.PropertyChanged += Log_PropertyChanged;

                e.Row.Tag = log;
            }
        }

        private void Log_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogEntry.IsAnnotationExpanded) && sender is LogEntry log)
            {
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

        private DataGridRow? FindRowForLog(LogEntry log)
        {
            var itemsSource = LogsDataGrid.ItemsSource as System.Collections.IList;
            if (itemsSource == null) return null;

            int index = itemsSource.IndexOf(log);
            if (index < 0) return null;

            return LogsDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }

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

        private void LogsDataGrid_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            var parent = Window.GetWindow(this) as ITabHost;
            parent?.MainLogsGrid_PreviewKeyDown(sender, e);

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

        private void ThreadFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.OpenThreadFilterCommand?.Execute(sender as FrameworkElement);
            e.Handled = true;
        }
    }
}
