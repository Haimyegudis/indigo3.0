using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Views
{
    public partial class ComparisonWindow
    {
        private void LeftGrid_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            // Skip if this is a programmatic scroll or sync is in progress
            if (_isScrollSyncing || _isProgrammaticScroll || !_vm.IsSyncLocked)
                return;

            // Only sync on vertical scroll changes
            if (Math.Abs(e.VerticalChange) < 0.1)
                return;

            SyncScrollFromPane(LeftDataGrid, RightDataGrid, _vm.LeftPane, _vm.RightPane, "Left");
        }

        private void RightGrid_ScrollChanged(object? sender, ScrollChangedEventArgs e)
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

        private LogEntry? GetTopVisibleLog(DataGrid grid, ViewModels.Components.ComparisonPaneViewModel pane)
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Visual tree lookup failed: {ex.Message}");
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

        private DataGridRow? GetFirstVisibleRow(DataGrid grid, ScrollViewer scrollViewer)
        {
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
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Row visibility check failed: {ex.Message}");
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
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Scroll transform error: {ex.Message}");
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Scroll sync failed: {ex.Message}");
                _isProgrammaticScroll = false;
            }
        }

        private ScrollViewer? GetScrollViewer(DataGrid? grid)
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

        private ScrollViewer? FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer sv)
                return sv;
            if (obj is not Visual && obj is not System.Windows.Media.Media3D.Visual3D)
                return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
