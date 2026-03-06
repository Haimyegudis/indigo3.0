using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0
{
    // Scroll state and scroll-to methods for DataGrid navigation.
    // Visual-tree helpers, grid lookup, DataGrid event handlers, and time-sync
    // scrolling live in MainWindow.DataGridEvents.cs.
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

    }
}
