using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    // Scroll management, visual-tree helpers, and DataGrid event handlers
    // extracted from MainWindow.xaml.cs to reduce code-behind size.
    public partial class MainWindow
    {
        // ============================================
        //  Scroll State
        // ============================================

        private Dictionary<string, ScrollViewer> _scrollViewerCache = new Dictionary<string, ScrollViewer>();

        // Flag to distinguish between user clicks and code-driven scrolling
        private bool _isProgrammaticScroll = false;

        // Saved scroll position for preserving row position during filter changes
        private double _savedScrollOffset = 0;
        private int _savedLogIndexInView = -1;
        private double _savedLogOffsetInViewport = 0;

        // Deferred scroll-to-bottom for tabs not yet rendered (WPF TabControl content virtualization)
        private HashSet<string> _pendingScrollToBottom = new HashSet<string>();

        // True while a tab switch is in progress.
        // Set in TabControl_SelectionChanged (fires BEFORE the new tab renders) so that
        // the initial ScrollChanged caused by the newly-visible DataGrid's layout pass
        // does not call TriggerTimeSyncScroll and overwrite the pending sync log.
        // Cleared at DispatcherPriority.Loaded (after the layout/render pass completes).
        private bool _isTabSwitching = false;

        private int _scrollPreserveRetryCount = 0;
        private const int MAX_SCROLL_PRESERVE_RETRIES = 3;

        // Store the last user-initiated horizontal scroll position
        private double _lastUserHorizontalOffset = 0;
        private bool _isUserScrolling = false;

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
        //  Scroll-To Methods
        // ============================================

        private void MapsToLogRow(LogEntry log)
        {
            if (log == null) return;

            DataGrid? targetGrid = FindGridForLog(log);

            if (targetGrid == null) return;

            try
            {
                int logIndex = targetGrid.Items.IndexOf(log);
                if (logIndex < 0) return;

                // Try to get ScrollViewer from cache first
                ScrollViewer? scrollViewer = null;
                string gridName = targetGrid.Name;

                if (!string.IsNullOrEmpty(gridName) && _scrollViewerCache.ContainsKey(gridName))
                {
                    scrollViewer = _scrollViewerCache[gridName];
                }
                else
                {
                    // Fallback: search for it and wait for it to load
                    // Force layout update and apply template
                    targetGrid.UpdateLayout();
                    targetGrid.ApplyTemplate();

                    // Try multiple times with slight delays for lazy-loaded grids
                    for (int attempt = 0; attempt < 3 && scrollViewer == null; attempt++)
                    {
                        if (attempt > 0)
                        {
                            System.Threading.Thread.Sleep(10); // Small delay
                            targetGrid.UpdateLayout();
                        }

                        scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
                    }

                    if (scrollViewer != null && !string.IsNullOrEmpty(gridName))
                    {
                        _scrollViewerCache[gridName] = scrollViewer;
                    }
                }

                if (scrollViewer == null)
                {
                    // Schedule a retry after a short delay
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        MapsToLogRow(log);
                    }));
                    return;
                }

                // IMPORTANT: keep _isProgrammaticScroll = true for the ENTIRE operation,
                // including SelectedItem. Setting SelectedItem while the flag is false lets
                // any resulting SelectionChanged / RequestBringIntoView events reach the
                // ScrollChanged handler without protection, which triggers spurious time-sync
                // scrolls every time the user switches tabs.
                _isProgrammaticScroll = true;

                // Select the item first
                targetGrid.SelectedItem = log;

                // Only scroll if the item is not already visible in the current viewport.
                // VerticalOffset = index of the first visible item (item-based scrolling).
                // If the target row is within [firstVisible, lastVisible], skip the scroll
                // so that navigating between nearby marks doesn't jump the view unnecessarily.
                double firstVisible = scrollViewer.VerticalOffset;
                double lastVisible  = firstVisible + scrollViewer.ViewportHeight - 1;
                bool alreadyVisible = logIndex >= firstVisible && logIndex <= lastVisible;

                if (!alreadyVisible)
                {
                    // Use ScrollToVerticalOffset instead of ScrollIntoView.
                    // IMPORTANT: DataGrid.ScrollIntoView() calls InvalidateMeasure() internally,
                    // which is ASYNC — the actual scroll (and its ScrollChanged event) fires in
                    // the NEXT render pass, AFTER _isProgrammaticScroll has already been reset to
                    // false. This makes WPF treat that deferred ScrollChanged as a user scroll,
                    // creating a spurious pending time-sync and causing the "time drifts forward"
                    // bug every time the sync scroll fires.
                    //
                    // ScrollViewer.ScrollToVerticalOffset() is SYNCHRONOUS on a VirtualizingStackPanel
                    // in item-scroll mode (VerticalOffset == item index): ScrollChanged fires
                    // immediately within this call, while _isProgrammaticScroll is still true. ✓
                    //
                    // Offset = logIndex (NOT logIndex - 2): the time-sync captures the TOPMOST
                    // visible item in the source, so we must also place the matched item at the
                    // TOP of the target. Using logIndex - 2 caused a ~1-minute visual gap when
                    // logs were sparse (the 2 earlier rows could span many seconds / minutes).
                    scrollViewer.ScrollToVerticalOffset(logIndex);
                }

                _isProgrammaticScroll = false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("MapsToLogRow scroll failed", ex);
                _isProgrammaticScroll = false;
            }
        }

        /// <summary>
        /// Scrolls a specific tab's grid to its last row. Used on initial load to ensure
        /// all tabs (PLC, APP) show the bottom of the log.
        /// Unlike MapsToLogRow/FindGridForLog, this directly targets the correct grid
        /// without searching (which would always match PLC first for shared log objects).
        /// </summary>
        private void ScrollGridToBottom(string tabName)
        {
            try
            {
                DataGrid? grid = null;
                switch (tabName)
                {
                    case "PLC":
                        grid = PlcLogsTab?.LogsGrid?.InnerDataGrid;
                        break;
                    case "APP":
                        grid = AppLogsTab?.InnerDataGrid;
                        break;
                }

                if (grid == null || grid.Items.Count == 0)
                {
                    // Tab not rendered yet (WPF TabControl only renders the active tab).
                    // Store flag so we scroll when the user switches to this tab.
                    _pendingScrollToBottom.Add(tabName);
                    return;
                }

                // Use ScrollViewer.ScrollToEnd() directly — this is more reliable than
                // ScrollIntoView which can fail on freshly-rendered tabs due to
                // RequestBringIntoView handlers that suppress the scroll event.
                grid.UpdateLayout();
                var scrollViewer = FindVisualChild<ScrollViewer>(grid);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToEnd();
                    grid.SelectedItem = grid.Items[grid.Items.Count - 1];
                    _pendingScrollToBottom.Remove(tabName);
                }
                else
                {
                    // ScrollViewer not materialized yet — defer until tab is fully rendered
                    _pendingScrollToBottom.Add(tabName);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ScrollGridToBottom failed", ex);
            }
        }

        /// <summary>
        /// Scrolls to a log entry while preserving its visual position on screen.
        /// When clearing/applying filters, this keeps the selected row in the same position
        /// rather than jumping to the bottom or top of the viewport.
        ///
        /// NOTE: WPF DataGrid with VirtualizingStackPanel uses ITEM-BASED scrolling (VerticalOffset = item index),
        /// not pixel-based scrolling. So we save/restore in terms of item indices.
        /// </summary>
        private void ScrollToLogPreservingPosition(LogEntry log, bool preservePosition)
        {
            if (log == null) return;

            // Use dispatcher with ContextIdle priority - this runs after all rendering and layout is complete
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                try
                {
                    DataGrid? targetGrid = FindGridForLog(log);
                    string gridName = targetGrid?.Name ?? "";

                    if (targetGrid == null)
                    {
                        // Retry: grid items may not be populated yet after filter clear
                        if (_scrollPreserveRetryCount < MAX_SCROLL_PRESERVE_RETRIES)
                        {
                            _scrollPreserveRetryCount++;
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                            {
                                ScrollToLogPreservingPosition(log, preservePosition);
                            }));
                        }
                        else
                        {
                            _scrollPreserveRetryCount = 0;
                        }
                        return;
                    }

                    // Force layout update to ensure virtualized items are materialized
                    targetGrid.UpdateLayout();

                    int newLogIndex = targetGrid.Items.IndexOf(log);
                    if (newLogIndex < 0)
                    {
                        // Retry: items may not be fully loaded yet
                        if (_scrollPreserveRetryCount < MAX_SCROLL_PRESERVE_RETRIES)
                        {
                            _scrollPreserveRetryCount++;
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                            {
                                ScrollToLogPreservingPosition(log, preservePosition);
                            }));
                        }
                        else
                        {
                            _scrollPreserveRetryCount = 0;
                            MapsToLogRow(log); // Final fallback
                        }
                        return;
                    }

                    // Success - reset retry counter
                    _scrollPreserveRetryCount = 0;

                    // Get or find the ScrollViewer
                    ScrollViewer? scrollViewer = null;
                    if (!string.IsNullOrEmpty(gridName) && _scrollViewerCache.ContainsKey(gridName))
                    {
                        scrollViewer = _scrollViewerCache[gridName];
                    }
                    else
                    {
                        scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
                    }

                    if (scrollViewer == null)
                    {
                        MapsToLogRow(log); // Fallback to normal scrolling
                        return;
                    }

                    // For VirtualizingStackPanel, VerticalOffset is the INDEX of the first visible item (not pixels!)
                    // _savedLogOffsetInViewport is how many ITEMS from the first visible item the selected row was
                    // So: targetOffset = newLogIndex - savedItemOffsetInViewport

                    double targetOffset;
                    if (_savedLogOffsetInViewport >= 0)
                    {
                        // Calculate the scroll offset that will place this log at the same position in viewport
                        targetOffset = newLogIndex - _savedLogOffsetInViewport;
                    }
                    else
                    {
                        // Default: put the row near the middle of viewport
                        double viewportItems = scrollViewer.ViewportHeight; // This is in items, not pixels
                        targetOffset = newLogIndex - (viewportItems / 2);
                    }

                    // Clamp to valid scroll range
                    targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));

                    _isProgrammaticScroll = true;

                    // Select the item
                    targetGrid.SelectedItem = log;

                    // Scroll to position
                    scrollViewer.ScrollToVerticalOffset(targetOffset);

                    _isProgrammaticScroll = false;

                    // Clear saved position after using it
                    _savedLogIndexInView = -1;
                    _savedLogOffsetInViewport = -1;
                    _savedScrollOffset = -1;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Scroll position restore failed: {ex.Message}");
                    _isProgrammaticScroll = false;
                    _scrollPreserveRetryCount = 0;
                    MapsToLogRow(log); // Fallback to normal scrolling
                }
            }));
        }

        /// <summary>
        /// Saves the current scroll position before filter changes.
        /// Call this BEFORE applying any filter changes.
        ///
        /// NOTE: WPF DataGrid with VirtualizingStackPanel uses ITEM-BASED scrolling (VerticalOffset = item index),
        /// not pixel-based scrolling. So we save/restore in terms of item indices.
        /// </summary>
        public void SaveScrollPositionForLog(LogEntry log)
        {
            if (log == null) return;

            try
            {
                DataGrid? targetGrid = FindGridForLog(log);
                string gridName = targetGrid?.Name ?? "";

                if (targetGrid == null) return;

                int logIndex = targetGrid.Items.IndexOf(log);
                if (logIndex < 0) return;

                // Get ScrollViewer
                ScrollViewer? scrollViewer = null;
                if (!string.IsNullOrEmpty(gridName) && _scrollViewerCache.ContainsKey(gridName))
                {
                    scrollViewer = _scrollViewerCache[gridName];
                }
                else
                {
                    scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
                }

                if (scrollViewer == null) return;

                // For VirtualizingStackPanel, VerticalOffset IS the index of the first visible item
                // So the item's offset within the viewport = itemIndex - scrollOffset
                double currentScrollOffset = scrollViewer.VerticalOffset;

                // Save how many ITEMS from the top of the viewport this item is
                _savedLogIndexInView = logIndex;
                _savedLogOffsetInViewport = logIndex - currentScrollOffset;
                _savedScrollOffset = currentScrollOffset;
            }
            catch (Exception ex)
            {
                AppLogger.Error("SaveScrollPositionForLog failed", ex);
            }
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
