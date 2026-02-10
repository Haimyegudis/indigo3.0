using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    public partial class MainWindow : Window, ITabHost
    {
        public ObservableCollection<LogEntry> MarkedAppLogs { get; set; }
        private Point _lastMousePosition;
        private bool _isDragging;
        private Dictionary<string, ScrollViewer> _scrollViewerCache = new Dictionary<string, ScrollViewer>();

        // Flag to distinguish between user clicks and code-driven scrolling
        private bool _isProgrammaticScroll = false;

        // Saved scroll position for preserving row position during filter changes
        private double _savedScrollOffset = 0;
        private int _savedLogIndexInView = -1;
        private double _savedLogOffsetInViewport = 0;

        // Per-tab panel width storage (default is 200 for all tabs)
        private System.Collections.Generic.Dictionary<int, double> _tabPanelWidths = new System.Collections.Generic.Dictionary<int, double>();
        private const double DEFAULT_PANEL_WIDTH = 200;
        private int _previousTabIndex = 0;

        // Deferred scroll-to-bottom for tabs not yet rendered (WPF TabControl content virtualization)
        private System.Collections.Generic.HashSet<string> _pendingScrollToBottom = new System.Collections.Generic.HashSet<string>();

        // Drag-to-detach support
        private Point _tabDragStartPoint;
        private bool _isTabDragging;
        private TabItem _draggingTabItem;
        private System.Windows.Controls.Primitives.Popup _dragPopup;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            // Initialize WindowManager with main window
            WindowManager.Initialize(this);

            // Initialize TabTearOffManager
            TabTearOffManager.Initialize(this, MainTabs);

            // Setup drag-to-detach on tab headers
            MainTabs.PreviewMouseLeftButtonDown += MainTabs_PreviewMouseLeftButtonDown;
            MainTabs.PreviewMouseMove += MainTabs_PreviewMouseMove;
            MainTabs.PreviewMouseLeftButtonUp += MainTabs_PreviewMouseLeftButtonUp;

            // Subscribe to chart data transfer events
            ChartDataTransferService.Instance.OnSwitchToChartsRequested += SwitchToChartsTab;
            ChartDataTransferService.Instance.OnChartTimeSelected += OnChartTimeSelected;

            // Check arguments (Open with...)
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                var files = new string[args.Length - 1];
                Array.Copy(args, 1, files, 0, files.Length);
                this.Loaded += (s, e) => HandleExternalArguments(files);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            TabTearOffManager.ReattachAll();
            base.OnClosed(e);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        // ============================================
        //  Drag-to-Detach Tab Handlers
        // ============================================

        private void MainTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only start drag if clicking on a TabItem header area
            var tabItem = FindTabItemFromPoint(e);
            if (tabItem == null || !TabTearOffManager.IsTabDetachable(tabItem))
                return;

            _tabDragStartPoint = e.GetPosition(null);
            _draggingTabItem = tabItem;
            _isTabDragging = false;
        }

        private void MainTabs_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingTabItem == null || e.LeftButton != MouseButtonState.Pressed)
            {
                CleanupDrag();
                return;
            }

            Point currentPos = e.GetPosition(null);
            Vector diff = currentPos - _tabDragStartPoint;

            // Check if we've moved beyond the drag threshold
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance * 2 ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance * 2)
            {
                if (!_isTabDragging)
                {
                    _isTabDragging = true;
                    ShowDragPopup(_draggingTabItem.Header?.ToString() ?? "");
                }

                // Update popup position
                UpdateDragPopupPosition();

                // Check if the cursor has left the tab header area
                Point screenPos = PointToScreen(currentPos);
                Point tabControlScreenPos = MainTabs.PointToScreen(new Point(0, 0));
                double tabHeaderHeight = 35; // Approximate tab header height

                bool outsideTabHeaders = screenPos.Y < tabControlScreenPos.Y - 20 ||
                                          screenPos.Y > tabControlScreenPos.Y + tabHeaderHeight + 20 ||
                                          screenPos.X < tabControlScreenPos.X - 50 ||
                                          screenPos.X > tabControlScreenPos.X + MainTabs.ActualWidth + 50;

                if (outsideTabHeaders)
                {
                    var tabItem = _draggingTabItem;
                    CleanupDrag();

                    // Detach the tab at the current mouse screen position
                    TabTearOffManager.DetachTab(tabItem, screenPos);
                }
            }
        }

        private void MainTabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CleanupDrag();
        }

        private void CleanupDrag()
        {
            _draggingTabItem = null;
            _isTabDragging = false;
            HideDragPopup();
        }

        private void ShowDragPopup(string headerText)
        {
            if (_dragPopup != null) return;

            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 27, 40, 56)),
                BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 6, 12, 6),
                Child = new TextBlock
                {
                    Text = headerText,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };

            _dragPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                HorizontalOffset = 15,
                VerticalOffset = 10,
                IsOpen = true,
                IsHitTestVisible = false
            };
        }

        private void UpdateDragPopupPosition()
        {
            if (_dragPopup == null) return;
            // Force popup to re-position by toggling placement
            _dragPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            _dragPopup.HorizontalOffset = 15;
            _dragPopup.VerticalOffset = 10;
        }

        private void HideDragPopup()
        {
            if (_dragPopup != null)
            {
                _dragPopup.IsOpen = false;
                _dragPopup = null;
            }
        }

        private TabItem FindTabItemFromPoint(MouseButtonEventArgs e)
        {
            // Walk up the visual tree from the click source to find a TabItem
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is TabItem))
            {
                // Stop if we've gone past the tab header into content
                if (source is TabControl) return null;

                // Use VisualTreeHelper for Visual/Visual3D, LogicalTreeHelper for ContentElements (e.g. Run)
                if (source is System.Windows.Media.Visual || source is System.Windows.Media.Media3D.Visual3D)
                    source = VisualTreeHelper.GetParent(source);
                else
                    source = LogicalTreeHelper.GetParent(source);
            }

            if (source is TabItem tabItem && MainTabs.Items.Contains(tabItem))
                return tabItem;

            return null;
        }

        /// <summary>
        /// Detach button click handler (called from tab header buttons)
        /// </summary>
        public void DetachTab_Click(object sender, RoutedEventArgs e)
        {
            // Find the TabItem that contains this button
            if (sender is Button button)
            {
                var tabItem = FindVisualParent<TabItem>(button);
                if (tabItem != null && TabTearOffManager.IsTabDetachable(tabItem))
                {
                    Point screenPos = PointToScreen(Mouse.GetPosition(this));
                    TabTearOffManager.DetachTab(tabItem, screenPos);
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToLog += MapsToLogRow;
                vm.RequestScrollToLogPreservePosition += ScrollToLogPreservingPosition;
                vm.RequestSaveScrollPosition += SaveScrollPositionForLog;
                vm.RequestScrollToBottom += ScrollGridToBottom;
                vm.PropertyChanged += ViewModel_PropertyChanged;

                // Initialize column widths based on current ViewModel state
                SyncPanelColumnsWithViewModel(vm);

                // Connect Chart tab to ChartVM for bidirectional sync
                if (ChartTab != null && vm.ChartVM != null)
                {
                    vm.ChartVM.SetChartControl(ChartTab);
                }
            }
        }

        private void SyncPanelColumnsWithViewModel(MainViewModel vm)
        {
            // Left panel
            if (vm.IsLeftPanelVisible)
            {
                LeftPanelColumn.Width = new GridLength(200);
                LeftSplitterColumn.Width = GridLength.Auto;
            }
            else
            {
                LeftPanelColumn.Width = new GridLength(0);
                LeftSplitterColumn.Width = new GridLength(0);
            }

            // Right panel
            if (vm.IsRightPanelVisible)
            {
                RightPanelColumn.Width = new GridLength(200);
                RightSplitterColumn.Width = GridLength.Auto;
            }
            else
            {
                RightPanelColumn.Width = new GridLength(0);
                RightSplitterColumn.Width = new GridLength(0);
            }

            System.Diagnostics.Debug.WriteLine($"[PANEL SYNC] Initialized: Left={vm.IsLeftPanelVisible}, Right={vm.IsRightPanelVisible}");
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Sync column widths with ViewModel panel visibility
            if (e.PropertyName == nameof(MainViewModel.IsLeftPanelVisible))
            {
                if (sender is MainViewModel vm)
                {
                    if (vm.IsLeftPanelVisible)
                    {
                        LeftPanelColumn.Width = new GridLength(200);
                        LeftSplitterColumn.Width = GridLength.Auto;
                    }
                    else
                    {
                        LeftPanelColumn.Width = new GridLength(0);
                        LeftSplitterColumn.Width = new GridLength(0);
                    }
                    System.Diagnostics.Debug.WriteLine($"[PANEL SYNC] Left panel visibility changed to {vm.IsLeftPanelVisible}, Column width = {LeftPanelColumn.Width}");
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsRightPanelVisible))
            {
                if (sender is MainViewModel vm)
                {
                    if (vm.IsRightPanelVisible)
                    {
                        RightPanelColumn.Width = new GridLength(200);
                        RightSplitterColumn.Width = GridLength.Auto;
                    }
                    else
                    {
                        RightPanelColumn.Width = new GridLength(0);
                        RightSplitterColumn.Width = new GridLength(0);
                    }
                    System.Diagnostics.Debug.WriteLine($"[PANEL SYNC] Right panel visibility changed to {vm.IsRightPanelVisible}, Column width = {RightPanelColumn.Width}");
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
            {
                if (sender is MainViewModel vm)
                {
                    // Update Charts SkiaSharp theme
                    ChartTab?.SetLightTheme(!vm.IsDarkMode);

                    // Update CPR Charts SkiaSharp theme
                    CprTab?.UpdateChartTheme();
                }
            }
        }

        public void HandleExternalArguments(string[] args)
        {
            if (args != null && args.Length > 0 && DataContext is MainViewModel vm)
            {
                vm.OnFilesDropped(args);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is MainViewModel vm) vm.OnFilesDropped(files);
            }
        }

        /// <summary>
        /// Finds the DataGrid containing a specific log entry, searching both
        /// local (attached) tabs and detached floating windows.
        /// </summary>
        private DataGrid FindGridForLog(LogEntry log)
        {
            if (log == null) return null;

            // Check local (still-attached) grids first
            if (PlcLogsTab?.LogsGrid?.InnerDataGrid != null && PlcLogsTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                return PlcLogsTab.LogsGrid.InnerDataGrid;
            if (PlcFilteredTab?.LogsGrid?.InnerDataGrid != null && PlcFilteredTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                return PlcFilteredTab.LogsGrid.InnerDataGrid;
            if (AppLogsTab?.InnerDataGrid != null && AppLogsTab.InnerDataGrid.Items.Contains(log))
                return AppLogsTab.InnerDataGrid;

            // Check detached windows
            var detachedPlc = TabTearOffManager.GetDetachedControl<Controls.PlcLogsTabControl>("PLC LOGS");
            if (detachedPlc?.LogsGrid?.InnerDataGrid?.Items.Contains(log) == true)
                return detachedPlc.LogsGrid.InnerDataGrid;

            var detachedFiltered = TabTearOffManager.GetDetachedControl<Controls.PlcFilteredTabControl>("PLC (FILTERED)");
            if (detachedFiltered?.LogsGrid?.InnerDataGrid?.Items.Contains(log) == true)
                return detachedFiltered.LogsGrid.InnerDataGrid;

            var detachedApp = TabTearOffManager.GetDetachedControl<Controls.AppLogsTabControl>("APP");
            if (detachedApp?.InnerDataGrid?.Items.Contains(log) == true)
                return detachedApp.InnerDataGrid;

            return null;
        }

        private void MapsToLogRow(LogEntry log)
        {
            if (log == null)
            {
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Log is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Attempting to scroll to: {log.Date:HH:mm:ss.fff}");

            DataGrid targetGrid = FindGridForLog(log);

            if (targetGrid == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] No grid found!");
                return;
            }

            try
            {
                int logIndex = targetGrid.Items.IndexOf(log);
                if (logIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Log not found in grid items");
                    return;
                }

                // Try to get ScrollViewer from cache first
                ScrollViewer scrollViewer = null;
                string gridName = targetGrid.Name;

                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Looking for ScrollViewer. Grid name: '{gridName}', Cache count: {_scrollViewerCache.Count}");
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Cached grids: {string.Join(", ", _scrollViewerCache.Keys)}");

                if (!string.IsNullOrEmpty(gridName) && _scrollViewerCache.ContainsKey(gridName))
                {
                    scrollViewer = _scrollViewerCache[gridName];
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Using cached ScrollViewer for {gridName}");
                }
                else
                {
                    // Fallback: search for it and wait for it to load
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Cache miss for '{gridName}', searching visual tree...");

                    // Force layout update and apply template
                    targetGrid.UpdateLayout();
                    targetGrid.ApplyTemplate();

                    // Try multiple times with slight delays for lazy-loaded grids
                    for (int attempt = 0; attempt < 3 && scrollViewer == null; attempt++)
                    {
                        if (attempt > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Retry attempt {attempt}...");
                            System.Threading.Thread.Sleep(10); // Small delay
                            targetGrid.UpdateLayout();
                        }

                        scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
                    }

                    if (scrollViewer != null && !string.IsNullOrEmpty(gridName))
                    {
                        _scrollViewerCache[gridName] = scrollViewer;
                        System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ✅ Found and cached ScrollViewer for {gridName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ⚠️ Still couldn't find ScrollViewer after multiple attempts");
                    }
                }

                if (scrollViewer == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ❌ ScrollViewer not found for '{gridName}'. Deferring scroll...");

                    // Schedule a retry after a short delay
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] 🔄 Retrying scroll for {gridName}...");
                        MapsToLogRow(log);
                    }));
                    return;
                }

                // Allow scrolling for this programmatic action
                _isProgrammaticScroll = true;

                // Select the item first
                targetGrid.SelectedItem = log;

                // Use ScrollIntoView to properly scroll to the row
                targetGrid.ScrollIntoView(log);

                // If we want to scroll to top of list (index 0), scroll to top
                if (logIndex == 0)
                {
                    scrollViewer.ScrollToTop();
                }

                _isProgrammaticScroll = false;

                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ✅ Successfully scrolled to index {logIndex} in {gridName}");
            }
            catch (Exception ex)
            {
                _isProgrammaticScroll = false;
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Scrolls a specific tab's grid to its last row. Used on initial load to ensure
        /// all tabs (PLC, FILTERED, APP) show the bottom of the log.
        /// Unlike MapsToLogRow/FindGridForLog, this directly targets the correct grid
        /// without searching (which would always match PLC first for shared log objects).
        /// </summary>
        private void ScrollGridToBottom(string tabName)
        {
            try
            {
                DataGrid grid = null;
                switch (tabName)
                {
                    case "PLC":
                        grid = PlcLogsTab?.LogsGrid?.InnerDataGrid;
                        break;
                    case "FILTERED":
                        grid = PlcFilteredTab?.LogsGrid?.InnerDataGrid;
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
                    System.Diagnostics.Debug.WriteLine($"[SCROLL BOTTOM] {tabName}: deferred (tab not yet rendered)");
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
                    System.Diagnostics.Debug.WriteLine($"[SCROLL BOTTOM] {tabName}: scrolled to end (items={grid.Items.Count})");
                }
                else
                {
                    // ScrollViewer not materialized yet — defer until tab is fully rendered
                    _pendingScrollToBottom.Add(tabName);
                    System.Diagnostics.Debug.WriteLine($"[SCROLL BOTTOM] {tabName}: deferred (no ScrollViewer yet)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SCROLL BOTTOM] {tabName} error: {ex.Message}");
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
        private int _scrollPreserveRetryCount = 0;
        private const int MAX_SCROLL_PRESERVE_RETRIES = 3;

        private void ScrollToLogPreservingPosition(LogEntry log, bool preservePosition)
        {
            if (log == null) return;

            // Use dispatcher with ContextIdle priority - this runs after all rendering and layout is complete
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Looking for log in grids... (retry={_scrollPreserveRetryCount})");

                    DataGrid targetGrid = FindGridForLog(log);
                    string gridName = targetGrid?.Name ?? "";

                    if (targetGrid == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Log not found in any grid. PlcLogsTab Items.Count={PlcLogsTab?.LogsGrid?.InnerDataGrid?.Items?.Count ?? -1}");

                        // Retry: grid items may not be populated yet after filter clear
                        if (_scrollPreserveRetryCount < MAX_SCROLL_PRESERVE_RETRIES)
                        {
                            _scrollPreserveRetryCount++;
                            System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Scheduling retry #{_scrollPreserveRetryCount}...");
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
                        System.Diagnostics.Debug.WriteLine("[SCROLL PRESERVE] Log index not found after UpdateLayout");

                        // Retry: items may not be fully loaded yet
                        if (_scrollPreserveRetryCount < MAX_SCROLL_PRESERVE_RETRIES)
                        {
                            _scrollPreserveRetryCount++;
                            System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Scheduling retry #{_scrollPreserveRetryCount}...");
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
                    ScrollViewer scrollViewer = null;
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
                        System.Diagnostics.Debug.WriteLine("[SCROLL PRESERVE] ScrollViewer not found");
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
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] NewIndex={newLogIndex}, SavedItemOffset={_savedLogOffsetInViewport}, TargetScrollOffset={targetOffset}");
                    }
                    else
                    {
                        // Default: put the row near the middle of viewport
                        double viewportItems = scrollViewer.ViewportHeight; // This is in items, not pixels
                        targetOffset = newLogIndex - (viewportItems / 2);
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] No saved offset, centering row at index {newLogIndex}, ViewportItems={viewportItems}");
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

                    System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Scrolled to offset {targetOffset} (log at row {_savedLogOffsetInViewport} from top of viewport)");
                }
                catch (Exception ex)
                {
                    _isProgrammaticScroll = false;
                    _scrollPreserveRetryCount = 0;
                    System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Error: {ex.Message}");
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
                DataGrid targetGrid = FindGridForLog(log);
                string gridName = targetGrid?.Name ?? "";

                if (targetGrid == null) return;

                int logIndex = targetGrid.Items.IndexOf(log);
                if (logIndex < 0) return;

                // Get ScrollViewer
                ScrollViewer scrollViewer = null;
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

                System.Diagnostics.Debug.WriteLine($"[SCROLL SAVE] Index={logIndex}, ScrollOffset={currentScrollOffset}, ItemOffsetInViewport={_savedLogOffsetInViewport}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SCROLL SAVE] Error: {ex.Message}");
            }
        }

        public void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
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

        private void DataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // If this event wasn't triggered by our code (MapsToLogRow), suppress it.
            // This stops the DataGrid from jumping to the end of the line when clicking a long message.
            if (!_isProgrammaticScroll)
            {
                e.Handled = true;
            }
        }

        // Additional handler for Cells and Rows - more aggressive prevention
        private void DataGrid_Cell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // ALWAYS suppress RequestBringIntoView from cells and rows unless it's our code
            if (!_isProgrammaticScroll)
            {
                e.Handled = true;
            }
        }

        // Prevent horizontal scroll on cell click
        private void DataGrid_Cell_MouseDown(object sender, MouseButtonEventArgs e)
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

        // Helper to find parent in visual tree
        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        // Store the last user-initiated horizontal scroll position
        private double _lastUserHorizontalOffset = 0;
        private bool _isUserScrolling = false;

        public void DataGrid_Loaded(object sender, RoutedEventArgs e)
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
                            else if (header == "PLC (FILTERED)") gridName = "FilteredLogsGrid";
                            else if (header == "APP") gridName = "AppLogsGrid";
                        }
                    }

                    if (!string.IsNullOrEmpty(gridName))
                    {
                        _scrollViewerCache[gridName] = scrollViewer;
                        System.Diagnostics.Debug.WriteLine($"[CACHE] Cached ScrollViewer for grid: {gridName}");
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

                        // ✅ FIX: Time-Sync on ANY vertical scroll (not just wheel/drag)
                        if (args.VerticalChange != 0 && !_isProgrammaticScroll)
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
            if (gridName.Contains("App") || sourceGrid.ItemsSource == vm.AppDevLogsFiltered)
                sourceType = "APP";
            else if (gridName.Contains("Filtered"))
                sourceType = "PLCFiltered";

            // Trigger the sync
            vm.RequestSyncScroll(logEntry.Date, sourceType);
        }

        // Helper to find child in visual tree
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
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


        // --- Copy Logic ---
        public void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                CopySelectedLogsToClipboard();
            }
        }

        private void CopySelectedLogsToClipboard()
        {
            if (PlcLogsTab?.LogsGrid?.InnerDataGrid?.SelectedItems.Count == 0) return;
            var sb = new StringBuilder();
            var selectedLogs = PlcLogsTab.LogsGrid.InnerDataGrid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();
            int maxTime = 24;
            int maxLevel = Math.Max(5, selectedLogs.Max(l => (l.Level ?? "").Length));
            int maxThread = Math.Max(10, selectedLogs.Max(l => (l.ThreadName ?? "").Length));

            foreach (var log in selectedLogs)
            {
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff").PadRight(maxTime);
                string level = (log.Level ?? "").PadRight(maxLevel + 2);
                string thread = (log.ThreadName ?? "").PadRight(maxThread + 2);
                string msg = log.Message ?? "";
                sb.AppendLine($"{time} {level} {thread} {msg}");
            }
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        private void SearchTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.Visibility == Visibility.Visible) { tb.Focus(); tb.SelectAll(); }
        }

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
            if (treeViewItem != null) { treeViewItem.Focus(); e.Handled = true; }
        }

        static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem)) source = VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        public void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                System.ComponentModel.ListSortDirection direction = (e.Column.SortDirection != System.ComponentModel.ListSortDirection.Ascending) ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending;
                e.Column.SortDirection = direction;
                vm.SortAppLogs(e.Column.SortMemberPath, direction == System.ComponentModel.ListSortDirection.Ascending);
            }
        }

        // ==========================================
        //  FIXED SCREENSHOTS LOGIC (Zoom & Drag)
        // ==========================================

        private ScrollViewer GetScreenshotScrollViewer() => this.FindName("ScreenshotScrollViewer") as ScrollViewer;

        private void OnScreenshotMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && DataContext is MainViewModel vm)
            {
                if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
                else vm.ZoomOutCommand.Execute(null);

                e.Handled = true;
            }
        }

        private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            scrollViewer.PanningMode = PanningMode.None;

            _lastMousePosition = e.GetPosition(scrollViewer);
            _isDragging = true;

            if (sender is FrameworkElement el) el.CaptureMouse();

            scrollViewer.Cursor = Cursors.SizeAll;
        }

        private void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var scrollViewer = GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            Point currentPos = e.GetPosition(scrollViewer);

            double deltaX = _lastMousePosition.X - currentPos.X;
            double deltaY = _lastMousePosition.Y - currentPos.Y;

            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + deltaX);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);

            _lastMousePosition = currentPos;
        }

        private void OnImageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            _isDragging = false;

            if (sender is FrameworkElement el) el.ReleaseMouseCapture();

            scrollViewer.Cursor = Cursors.Arrow;
            scrollViewer.PanningMode = PanningMode.Both;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tabControl && e.Source == tabControl)
            {
                int newTabIndex = tabControl.SelectedIndex;
                _previousTabIndex = newTabIndex;

                // IMPORTANT: Don't change column widths here - they are controlled by IsLeftPanelVisible/IsRightPanelVisible
                // Just sync with the ViewModel state to ensure columns match the panel visibility
                if (DataContext is MainViewModel vm)
                {
                    SyncPanelColumnsWithViewModel(vm);
                }

                // Execute deferred scroll-to-bottom for tabs that weren't rendered on initial load
                // MainTabs indices: 0=PLC, 1=FILTERED, 2=APP
                string tabName = null;
                switch (newTabIndex)
                {
                    case 0: tabName = "PLC"; break;
                    case 1: tabName = "FILTERED"; break;
                    case 2: tabName = "APP"; break;
                }
                if (tabName != null && _pendingScrollToBottom.Contains(tabName))
                {
                    // Defer to ApplicationIdle to allow the tab content to fully render first
                    Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        new Action(() => ScrollGridToBottom(tabName)));
                }
            }
        }
        private void GraphsView_Loaded(object sender, RoutedEventArgs e) { }
        private void Button_Click(object sender, RoutedEventArgs e) { }

        private void PlcLogsTab_Loaded(object sender, RoutedEventArgs e)
        {
            // Wire up log selection to chart sync
            if (sender is Controls.PlcLogsTabControl plcTab && plcTab.LogsGrid?.InnerDataGrid != null)
            {
                plcTab.LogsGrid.InnerDataGrid.SelectionChanged += (s, args) =>
                {
                    if (DataContext is MainViewModel vm && args.AddedItems.Count > 0 && args.AddedItems[0] is LogEntry entry)
                    {
                        vm.OnLogEntrySelected(entry);
                    }
                };
            }
        }

        // Panel toggle button handlers - require double-click to prevent accidental toggles
        private void LeftPanelHideButton_Click(object sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel closing while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsLeftPanelVisible = false;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void LeftPanelShowButton_Click(object sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel opening while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsLeftPanelVisible = true;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void DebugGridLayout(string context)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG {context}] ========================================");
                System.Diagnostics.Debug.WriteLine($"[DEBUG {context}] MainContentGrid ActualWidth: {MainContentGrid.ActualWidth}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG {context}] Column Definitions:");

                for (int i = 0; i < MainContentGrid.ColumnDefinitions.Count; i++)
                {
                    var col = MainContentGrid.ColumnDefinitions[i];
                    System.Diagnostics.Debug.WriteLine($"[DEBUG {context}]   Col[{i}]: Width={col.Width}, ActualWidth={col.ActualWidth:F1}");
                }
                System.Diagnostics.Debug.WriteLine($"[DEBUG {context}] ========================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG ERROR] {ex.Message}");
            }
        }

        private void RightPanelHideButton_Click(object sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel closing while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsRightPanelVisible = false;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void RightPanelShowButton_Click(object sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel opening while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsRightPanelVisible = true;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void PanelShowButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Opacity = 1;
            }
        }

        private void PanelShowButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Opacity = 0;
            }
        }

        /// <summary>
        /// Switches to the Charts tab (index 8)
        /// </summary>
        private void SwitchToChartsTab()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MainTabs.SelectedIndex = 8; // Charts tab
            }));
        }

        /// <summary>
        /// Handles chart time click to sync with logs
        /// </summary>
        private void OnChartTimeSelected(DateTime time)
        {
            if (DataContext is MainViewModel vm)
            {
                // Find the log entry closest to this time
                var closestLog = vm.FilteredLogs?
                    .OrderBy(l => Math.Abs((l.Date - time).TotalMilliseconds))
                    .FirstOrDefault();

                if (closestLog != null)
                {
                    // Scroll to the log and select it
                    MapsToLogRow(closestLog);
                }
            }
        }

        private void CprTab_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}