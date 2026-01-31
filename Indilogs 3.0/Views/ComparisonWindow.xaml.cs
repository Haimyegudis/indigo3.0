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

            ComparisonDebugLogger.Log("INIT", $"ComparisonWindow loaded. LeftScrollViewer={_leftScrollViewer != null}, RightScrollViewer={_rightScrollViewer != null}");
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

                        ComparisonDebugLogger.LogSelection("Left", leftLog.Date, nearestIndex, targetLog?.Date);

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

                        ComparisonDebugLogger.LogSelection("Right", rightLog.Date, nearestIndex, targetLog?.Date);

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

            ComparisonDebugLogger.Log("SCROLL", $"LeftGrid scroll change detected: VerticalChange={e.VerticalChange:F1}, VerticalOffset={e.VerticalOffset:F1}");
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

            ComparisonDebugLogger.Log("SCROLL", $"RightGrid scroll change detected: VerticalChange={e.VerticalChange:F1}, VerticalOffset={e.VerticalOffset:F1}");
            SyncScrollFromPane(RightDataGrid, LeftDataGrid, _vm.RightPane, _vm.LeftPane, "Right");
        }

        private void SyncScrollFromPane(DataGrid sourceGrid, DataGrid targetGrid,
            ViewModels.Components.ComparisonPaneViewModel sourcePane,
            ViewModels.Components.ComparisonPaneViewModel targetPane,
            string sourceName)
        {
            _isScrollSyncing = true;
            ComparisonDebugLogger.LogSeparator($"SCROLL SYNC from {sourceName}");

            try
            {
                var topLog = GetTopVisibleLog(sourceGrid, sourcePane);
                if (topLog != null)
                {
                    ComparisonDebugLogger.Log("SYNC", $"Top visible log in {sourceName}: Time={topLog.Date:HH:mm:ss.fff}");
                    ComparisonDebugLogger.Log("SYNC", $"  Message: {(topLog.Message?.Length > 60 ? topLog.Message.Substring(0, 60) + "..." : topLog.Message)}");

                    int nearestIndex = targetPane.BinarySearchNearest(topLog.Date);
                    ComparisonDebugLogger.Log("SYNC", $"BinarySearch found index {nearestIndex} in target pane (has {targetPane.FilteredLogs.Count} items)");

                    if (nearestIndex >= 0 && nearestIndex < targetPane.FilteredLogs.Count)
                    {
                        var targetLog = targetPane.FilteredLogs[nearestIndex];
                        var timeDelta = Math.Abs((topLog.Date - targetLog.Date).TotalMilliseconds);

                        ComparisonDebugLogger.Log("SYNC", $"Target log at index {nearestIndex}: Time={targetLog.Date:HH:mm:ss.fff}");
                        ComparisonDebugLogger.Log("SYNC", $"  Time delta: {timeDelta:F0}ms");
                        ComparisonDebugLogger.Log("SYNC", $"  Message: {(targetLog.Message?.Length > 60 ? targetLog.Message.Substring(0, 60) + "..." : targetLog.Message)}");

                        ScrollToIndex(targetGrid, nearestIndex, targetPane);
                    }
                    else
                    {
                        ComparisonDebugLogger.Log("SYNC", "ERROR: Invalid index from BinarySearch!");
                    }
                }
                else
                {
                    ComparisonDebugLogger.Log("SYNC", "ERROR: Could not determine top visible log!");
                }
            }
            finally
            {
                // Use dispatcher to reset flag after scroll animation completes
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isScrollSyncing = false;
                    ComparisonDebugLogger.Log("SYNC", "Scroll sync completed, flag reset");
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
            catch { }

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
• Sync OFF: Panes scroll independently

DEBUG:
• Check 'Debug' to log all comparison operations to file
• Log location shown when enabled",
                "Comparison Window Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void DebugCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ComparisonDebugLogger.IsEnabled = true;
            ComparisonDebugLogger.ClearLog();
            ComparisonDebugLogger.LogSeparator("DEBUG SESSION STARTED");
            ComparisonDebugLogger.Log("DEBUG", "Debug logging ENABLED");
            ComparisonDebugLogger.Log("DEBUG", $"Left Pane: Type={_vm.LeftPane.SelectedSourceType}, Filter={_vm.LeftPane.SelectedFilter ?? "(none)"}, Logs={_vm.LeftPane.FilteredLogs.Count}");
            ComparisonDebugLogger.Log("DEBUG", $"Right Pane: Type={_vm.RightPane.SelectedSourceType}, Filter={_vm.RightPane.SelectedFilter ?? "(none)"}, Logs={_vm.RightPane.FilteredLogs.Count}");
            ComparisonDebugLogger.Log("DEBUG", $"ShowDiffs={_vm.ShowDiffs}, IsSyncLocked={_vm.IsSyncLocked}");
            ComparisonDebugLogger.Log("DEBUG", $"IgnoreMaskPattern=\"{_vm.IgnoreMaskPattern ?? "(empty)"}\", IsMaskValid={_vm.IsMaskValid}");

            MessageBox.Show(
                $"Debug logging ENABLED!\n\n" +
                $"Log file:\n{ComparisonDebugLogger.LogPath}\n\n" +
                $"Now try:\n" +
                $"• Scrolling either pane (tests sync)\n" +
                $"• Selecting rows (tests selection sync)\n" +
                $"• Changing ignore pattern (tests masking)\n" +
                $"• Changing source type (tests diff calculation)\n\n" +
                $"All operations will be logged to the file.",
                "Debug Mode Enabled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void DebugCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ComparisonDebugLogger.LogSeparator("DEBUG SESSION ENDED");
            ComparisonDebugLogger.Log("DEBUG", "Debug logging DISABLED");

            var logPath = ComparisonDebugLogger.LogPath;
            ComparisonDebugLogger.IsEnabled = false;

            var result = MessageBox.Show(
                $"Debug logging disabled.\n\nOpen log file?\n{logPath}",
                "Debug Mode Disabled",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start("notepad.exe", logPath);
                }
                catch { }
            }
        }

        #endregion
    }
}
