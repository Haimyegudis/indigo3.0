using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    // Visual-tree helpers, DataGrid grid lookup, event handlers, and time-sync scrolling
    // extracted from MainWindow.Scrolling.cs to keep files under 400 lines.
    public partial class MainWindow
    {
        // ============================================
        //  Visual-Tree Helpers
        // ============================================

        // Helper to find child in visual tree
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        // Helper to find parent in visual tree
        private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child is not Visual && child is not System.Windows.Media.Media3D.Visual3D) return null;
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        // ============================================
        //  Grid Lookup
        // ============================================

        /// <summary>
        /// Finds the DataGrid containing a specific log entry, searching both
        /// local (attached) tabs and detached floating windows.
        /// </summary>
        private DataGrid? FindGridForLog(LogEntry log)
        {
            if (log == null) return null;

            // Check local (still-attached) grids first
            if (PlcLogsTab?.LogsGrid?.InnerDataGrid != null && PlcLogsTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                return PlcLogsTab.LogsGrid.InnerDataGrid;
            if (AppLogsTab?.InnerDataGrid != null && AppLogsTab.InnerDataGrid.Items.Contains(log))
                return AppLogsTab.InnerDataGrid;

            // Check detached windows
            var detachedPlc = TabTearOffManager.GetDetachedControl<Controls.PlcLogsTabControl>("PLC LOGS");
            if (detachedPlc?.LogsGrid?.InnerDataGrid?.Items.Contains(log) == true)
                return detachedPlc.LogsGrid.InnerDataGrid;

            var detachedApp = TabTearOffManager.GetDetachedControl<Controls.AppLogsTabControl>("APP");
            if (detachedApp?.InnerDataGrid?.Items.Contains(log) == true)
                return detachedApp.InnerDataGrid;

            return null;
        }

        // ============================================
        //  DataGrid Event Handlers
        // ============================================

        public void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                // Show row details if annotation exists and should be expanded
                // This is a backup for the XAML binding
                e.Row.DetailsVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void DataGrid_RequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
        {
            // If this event wasn't triggered by our code (MapsToLogRow), suppress it.
            // This stops the DataGrid from jumping to the end of the line when clicking a long message.
            if (!_isProgrammaticScroll)
            {
                e.Handled = true;
            }
        }

        // Additional handler for Cells and Rows - more aggressive prevention
        private void DataGrid_Cell_RequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
        {
            // ALWAYS suppress RequestBringIntoView from cells and rows unless it's our code
            if (!_isProgrammaticScroll)
            {
                e.Handled = true;
            }
        }

        // Prevent horizontal scroll on cell click
        private void DataGrid_Cell_MouseDown(object? sender, MouseButtonEventArgs e)
        {
            // When user clicks a cell, prevent auto-scrolling by keeping focus on the row, not cell
            if (sender is DataGridCell cell)
            {
                // Find the parent DataGrid
                var grid = FindVisualParent<DataGrid>(cell);
                if (grid != null)
                {
                    // Get the row
                    var row = FindVisualParent<DataGridRow>(cell);
                    if (row != null && !row.IsSelected)
                    {
                        // Select the row without bringing the cell into view
                        row.IsSelected = true;
                        e.Handled = true;
                    }
                }
            }
        }

        public void DataGrid_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(grid);
                if (scrollViewer != null)
                {
                    string gridName = grid.Name ?? "";
                    if (string.IsNullOrEmpty(gridName))
                    {
                        var parent = FindVisualParent<TabItem>(grid);
                        if (parent != null)
                        {
                            var header = parent.Header?.ToString();
                            if (header == "PLC LOGS") gridName = "MainLogsGrid";
                            else if (header == "APP") gridName = "AppLogsGrid";
                        }
                    }

                    if (!string.IsNullOrEmpty(gridName))
                    {
                        // Guard: skip if same ScrollViewer already subscribed (prevents duplicate event handlers)
                        if (_scrollViewerCache.TryGetValue(gridName, out var existingSv) && existingSv == scrollViewer)
                            return;
                        _scrollViewerCache[gridName] = scrollViewer;
                    }

                    scrollViewer.ScrollChanged += (s, args) =>
                    {
                        // Horizontal scroll prevention
                        if (args.HorizontalChange != 0 && !_isProgrammaticScroll && !_isUserScrolling)
                        {
                            scrollViewer.ScrollToHorizontalOffset(_lastUserHorizontalOffset);
                        }
                        else if (args.HorizontalChange != 0 && _isUserScrolling)
                        {
                            _lastUserHorizontalOffset = scrollViewer.HorizontalOffset;
                        }

                        // Time-Sync on vertical scroll — but NOT during tab switches or
                        // programmatic scrolls.  _isTabSwitching is set in
                        // TabControl_SelectionChanged (before WPF renders the new tab)
                        // so the initial layout-pass ScrollChanged of a newly-visible
                        // DataGrid is always suppressed here.
                        if (args.VerticalChange != 0 && !_isProgrammaticScroll && !_isTabSwitching)
                        {
                            TriggerTimeSyncScroll(grid, gridName);
                        }
                    };

                    scrollViewer.PreviewMouseWheel += (s, args) => { _isUserScrolling = true; };
                    scrollViewer.PreviewMouseDown += (s, args) => { _isUserScrolling = true; };
                    scrollViewer.PreviewMouseUp += (s, args) => {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => {
                                _isUserScrolling = false;
                                _lastUserHorizontalOffset = scrollViewer.HorizontalOffset;
                            })
                        );
                    };
                }
            }
        }

        private void TriggerTimeSyncScroll(DataGrid sourceGrid, string gridName)
        {
            if (!(DataContext is MainViewModel vm) || !vm.IsTimeSyncEnabled)
                return;

            // Get the first visible item in the grid
            var scrollViewer = FindVisualChild<ScrollViewer>(sourceGrid);
            if (scrollViewer == null)
                return;

            // Calculate which row is at the top of the viewport
            int firstVisibleIndex = (int)scrollViewer.VerticalOffset;
            if (firstVisibleIndex < 0 || firstVisibleIndex >= sourceGrid.Items.Count)
                return;

            var firstVisibleItem = sourceGrid.Items[firstVisibleIndex];
            if (!(firstVisibleItem is LogEntry logEntry))
                return;

            // Identify source grid type
            string sourceType = "PLC";
            if (gridName.Contains("App") || sourceGrid.ItemsSource == vm.FilterVM?.AppDevLogsFiltered)
                sourceType = "APP";

            // Pass the full LogEntry so RequestSyncScroll can use SyncedTime for ms precision
            vm.RequestSyncScroll(logEntry, sourceType);
        }
    }
}
