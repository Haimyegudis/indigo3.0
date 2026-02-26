using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    /// <summary>
    /// Code-behind for ComparisonWindow.
    /// Handles scroll synchronization and user interactions.
    /// </summary>
    public partial class ComparisonWindow : Window
    {
        private LogComparisonViewModel _vm;
        private bool _isScrollSyncing = false;
        private bool _isProgrammaticScroll = false;
        private ScrollViewer _leftScrollViewer;
        private ScrollViewer _rightScrollViewer;

        public ComparisonWindow(LogComparisonViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel;
            DataContext = _vm;

            Loaded += ComparisonWindow_Loaded;
        }

        private void ComparisonWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Cache scroll viewers for performance
            _leftScrollViewer = GetScrollViewer(LeftDataGrid);
            _rightScrollViewer = GetScrollViewer(RightDataGrid);

            // Subscribe to selection changes to sync selections between panes
            LeftDataGrid.SelectionChanged += LeftDataGrid_SelectionChanged;
            RightDataGrid.SelectionChanged += RightDataGrid_SelectionChanged;
        }

        private bool _isSelectionSyncing = false;

        private void LeftDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSelectionSyncing || !_vm.IsSyncLocked)
                return;

            if (LeftDataGrid.SelectedItem is LogEntry leftLog)
            {
                _isSelectionSyncing = true;
                try
                {
                    // Find and select corresponding log in right pane
                    int nearestIndex = _vm.RightPane.BinarySearchNearest(leftLog.Date);
                    if (nearestIndex >= 0 && nearestIndex < _vm.RightPane.FilteredLogs.Count)
                    {
                        var targetLog = _vm.RightPane.FilteredLogs[nearestIndex];
                        _vm.RightPane.SelectedLog = targetLog;

                        // Also scroll the right pane to show the selected item
                        RightDataGrid.ScrollIntoView(targetLog);
                    }
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() => _isSelectionSyncing = false),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void RightDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSelectionSyncing || !_vm.IsSyncLocked)
                return;

            if (RightDataGrid.SelectedItem is LogEntry rightLog)
            {
                _isSelectionSyncing = true;
                try
                {
                    // Find and select corresponding log in left pane
                    int nearestIndex = _vm.LeftPane.BinarySearchNearest(rightLog.Date);
                    if (nearestIndex >= 0 && nearestIndex < _vm.LeftPane.FilteredLogs.Count)
                    {
                        var targetLog = _vm.LeftPane.FilteredLogs[nearestIndex];
                        _vm.LeftPane.SelectedLog = targetLog;

                        // Also scroll the left pane to show the selected item
                        LeftDataGrid.ScrollIntoView(targetLog);
                    }
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() => _isSelectionSyncing = false),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        #region Scroll Synchronization

        private void LeftGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Skip if this is a programmatic scroll or sync is in progress
            if (_isScrollSyncing || _isProgrammaticScroll || !_vm.IsSyncLocked)
                return;

            // Only sync on vertical scroll changes
            if (Math.Abs(e.VerticalChange) < 0.1)
                return;

            SyncScrollFromPane(LeftDataGrid, RightDataGrid, _vm.LeftPane, _vm.RightPane, "Left");
        }

        private void RightGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Skip if this is a programmatic scroll or sync is in progress
            if (_isScrollSyncing || _isProgrammaticScroll || !_vm.IsSyncLocked)
                return;

            // Only sync on vertical scroll changes
            if (Math.Abs(e.VerticalChange) < 0.1)
                return;

            SyncScrollFromPane(RightDataGrid, LeftDataGrid, _vm.RightPane, _vm.LeftPane, "Right");
        }

        private void SyncScrollFromPane(DataGrid sourceGrid, DataGrid targetGrid,
            ViewModels.Components.ComparisonPaneViewModel sourcePane,
            ViewModels.Components.ComparisonPaneViewModel targetPane,
            string sourceName)
        {
            _isScrollSyncing = true;

            try
            {
                var topLog = GetTopVisibleLog(sourceGrid, sourcePane);
                if (topLog != null)
                {
                    int nearestIndex = targetPane.BinarySearchNearest(topLog.Date);

                    if (nearestIndex >= 0 && nearestIndex < targetPane.FilteredLogs.Count)
                    {
                        ScrollToIndex(targetGrid, nearestIndex, targetPane);
                    }
                }
            }
            finally
            {
                // Use dispatcher to reset flag after scroll animation completes
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isScrollSyncing = false;
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private LogEntry GetTopVisibleLog(DataGrid grid, ViewModels.Components.ComparisonPaneViewModel pane)
        {
            var scrollViewer = grid == LeftDataGrid ? _leftScrollViewer : _rightScrollViewer;
            if (scrollViewer == null)
            {
                scrollViewer = GetScrollViewer(grid);
                if (grid == LeftDataGrid)
                    _leftScrollViewer = scrollViewer;
                else
                    _rightScrollViewer = scrollViewer;
            }

            if (scrollViewer == null || pane.FilteredLogs.Count == 0)
                return null;

            // Method 1: Try to find the first visible row using ItemContainerGenerator
            try
            {
                var firstVisibleRow = GetFirstVisibleRow(grid, scrollViewer);
                if (firstVisibleRow?.Item is LogEntry log)
                {
                    return log;
                }
            }
            catch
            {
                // Fall back to offset calculation
            }

            // Method 2: Estimate based on scroll offset
            // Get the actual row height from the first rendered row if possible
            double rowHeight = GetEstimatedRowHeight(grid);
            int firstVisibleIndex = (int)(scrollViewer.VerticalOffset / rowHeight);

            // Clamp to valid range
            firstVisibleIndex = Math.Max(0, Math.Min(firstVisibleIndex, pane.FilteredLogs.Count - 1));

            return pane.FilteredLogs[firstVisibleIndex];
        }

        private double GetEstimatedRowHeight(DataGrid grid)
        {
            // Try to get actual row height from a rendered row
            try
            {
                for (int i = 0; i < Math.Min(10, grid.Items.Count); i++)
                {
                    var row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row != null && row.ActualHeight > 0)
                    {
                        return row.ActualHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("GetEstimatedRowHeight failed", ex);
            }

            return 25.0; // Default estimate
        }

        private DataGridRow GetFirstVisibleRow(DataGrid grid, ScrollViewer scrollViewer)
        {
            if (scrollViewer == null)
                return null;

            // Start from an estimated index based on scroll position
            double rowHeight = GetEstimatedRowHeight(grid);
            int estimatedStart = Math.Max(0, (int)(scrollViewer.VerticalOffset / rowHeight) - 2);

            // Check rows starting from the estimated position
            for (int i = estimatedStart; i < Math.Min(estimatedStart + 20, grid.Items.Count); i++)
            {
                var row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                if (row != null)
                {
                    try
                    {
                        var transform = row.TransformToAncestor(scrollViewer);
                        var rowTop = transform.Transform(new Point(0, 0)).Y;

                        if (rowTop >= -1 && rowTop < scrollViewer.ViewportHeight)
                        {
                            return row;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return null;
        }

        private void ScrollToIndex(DataGrid grid, int index, ViewModels.Components.ComparisonPaneViewModel pane)
        {
            if (index < 0 || index >= pane.FilteredLogs.Count)
                return;

            var item = pane.FilteredLogs[index];
            if (item == null)
                return;

            // Mark this as programmatic scroll to prevent re-triggering sync
            _isProgrammaticScroll = true;

            try
            {
                // Scroll the item into view
                grid.UpdateLayout();
                grid.ScrollIntoView(item);

                // Try to position the item at the top of the viewport
                var scrollViewer = grid == LeftDataGrid ? _leftScrollViewer : _rightScrollViewer;
                if (scrollViewer == null)
                    scrollViewer = GetScrollViewer(grid);

                if (scrollViewer != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                            if (row != null)
                            {
                                var transform = row.TransformToAncestor(scrollViewer);
                                var rowTop = transform.Transform(new Point(0, 0)).Y;

                                // Only adjust if the row is not already near the top
                                if (Math.Abs(rowTop) > 5)
                                {
                                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + rowTop);
                                }
                            }
                        }
                        catch
                        {
                            // Ignore transform errors
                        }
                        finally
                        {
                            // Reset programmatic scroll flag after a delay
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _isProgrammaticScroll = false;
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    _isProgrammaticScroll = false;
                }
            }
            catch
            {
                _isProgrammaticScroll = false;
            }
        }

        private ScrollViewer GetScrollViewer(DataGrid grid)
        {
            if (grid == null)
                return null;

            // Search for ScrollViewer in visual tree
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                var child = VisualTreeHelper.GetChild(grid, i);

                if (child is ScrollViewer sv)
                    return sv;

                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private ScrollViewer FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer sv)
                return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion

        #region User Interactions

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem is LogEntry log)
            {
                // Navigate to this log in the main window
                _vm.GoToSourceCommand.Execute(log);
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem is LogEntry log)
            {
                switch (e.Key)
                {
                    case Key.Space:
                        // Toggle mark
                        log.IsMarked = !log.IsMarked;
                        e.Handled = true;
                        break;

                    case Key.Enter:
                        // Go to source
                        _vm.GoToSourceCommand.Execute(log);
                        e.Handled = true;
                        break;
                }
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            // Show detailed help message
            MessageBox.Show(
@"=== COMPARISON WINDOW HELP ===

HOW IT WORKS:
1. Select a source type for each pane (left/right)
2. If both panes show the SAME source (same type + filter), rows are compared by INDEX (row 1 vs row 1, row 2 vs row 2, etc.)
3. If panes show DIFFERENT sources, rows are compared by TIMESTAMP (finds nearest time match)
4. 'Show Diffs' highlights the differences in each message

SOURCE TYPES:
• AllPLC / AllAPP - All logs from PLC or APP
• ByThread - Filter by thread name (from PLC logs)
• ByThreadFromApp - Filter by thread name (from APP logs)
• ByLogger / ByLoggerFromPLC - Filter by logger name
• ByMethod / ByMethodFromPLC - Filter by method name
• ByPattern - Filter by pattern (PLC logs only)

IGNORE PATTERN (REGEX):
Ignores dynamic content when comparing. Matched text is replaced with '#'.

EXAMPLES:
• \d+                → Ignore all numbers (123, 456)
• [a-f0-9-]{36}      → Ignore GUIDs
• \d{2}:\d{2}:\d{2}  → Ignore timestamps (12:34:56)
• 0x[0-9a-fA-F]+     → Ignore hex addresses
• \[.*?\]            → Ignore [bracketed content]
• Thread-\d+         → Ignore Thread-123

COMBINE WITH | :  \d+|Thread-\d+

Red border = invalid pattern

SYNC:
• Sync ON: Scrolling/selecting syncs both panes by timestamp
• Sync OFF: Panes scroll independently",
                "Comparison Window Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion
    }
}
