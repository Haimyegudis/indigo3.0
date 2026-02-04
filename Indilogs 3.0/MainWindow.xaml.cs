using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
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
    public partial class MainWindow : Window
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

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            // Initialize WindowManager with main window
            WindowManager.Initialize(this);

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
            base.OnClosed(e);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToLog += MapsToLogRow;
                vm.RequestScrollToLogPreservePosition += ScrollToLogPreservingPosition;
                vm.RequestSaveScrollPosition += SaveScrollPositionForLog;
                vm.PropertyChanged += ViewModel_PropertyChanged;

                // Initialize column widths based on current ViewModel state
                SyncPanelColumnsWithViewModel(vm);
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

        private void MapsToLogRow(LogEntry log)
        {
            if (log == null)
            {
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Log is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Attempting to scroll to: {log.Date:HH:mm:ss.fff}");

            DataGrid targetGrid = null;

            // Determine which grid to scroll based on which contains the log
            if (PlcLogsTab?.LogsGrid?.InnerDataGrid != null && PlcLogsTab.LogsGrid.InnerDataGrid.Items.Contains(log))
            {
                targetGrid = PlcLogsTab.LogsGrid.InnerDataGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: MainLogsGrid");
            }
            else if (PlcFilteredTab?.LogsGrid?.InnerDataGrid != null && PlcFilteredTab.LogsGrid.InnerDataGrid.Items.Contains(log))
            {
                targetGrid = PlcFilteredTab.LogsGrid.InnerDataGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: FilteredLogsGrid");
            }
            else if (AppLogsTab?.InnerDataGrid != null && AppLogsTab.InnerDataGrid.Items.Contains(log))
            {
                targetGrid = AppLogsTab.InnerDataGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: AppLogsGrid");
            }

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
                    DataGrid targetGrid = null;
                    string gridName = "";

                    System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Looking for log in grids...");

                    // Find the grid containing this log
                    if (PlcLogsTab?.LogsGrid?.InnerDataGrid != null && PlcLogsTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                    {
                        targetGrid = PlcLogsTab.LogsGrid.InnerDataGrid;
                        gridName = "MainLogsGrid";
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Found in MainLogsGrid, Items.Count={targetGrid.Items.Count}");
                    }
                    else if (PlcFilteredTab?.LogsGrid?.InnerDataGrid != null && PlcFilteredTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                    {
                        targetGrid = PlcFilteredTab.LogsGrid.InnerDataGrid;
                        gridName = "FilteredLogsGrid";
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Found in FilteredLogsGrid, Items.Count={targetGrid.Items.Count}");
                    }
                    else if (AppLogsTab?.InnerDataGrid != null && AppLogsTab.InnerDataGrid.Items.Contains(log))
                    {
                        targetGrid = AppLogsTab.InnerDataGrid;
                        gridName = "AppLogsGrid";
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Found in AppLogsGrid, Items.Count={targetGrid.Items.Count}");
                    }

                    if (targetGrid == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] Log not found in any grid. PlcLogsTab Items.Count={PlcLogsTab?.LogsGrid?.InnerDataGrid?.Items?.Count ?? -1}");
                        return;
                    }

                    int newLogIndex = targetGrid.Items.IndexOf(log);
                    if (newLogIndex < 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[SCROLL PRESERVE] Log index not found");
                        return;
                    }

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

                    System.Diagnostics.Debug.WriteLine($"[SCROLL PRESERVE] ✅ Scrolled to offset {targetOffset} (log at row {_savedLogOffsetInViewport} from top of viewport)");
                }
                catch (Exception ex)
                {
                    _isProgrammaticScroll = false;
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
                DataGrid targetGrid = null;
                string gridName = "";

                // Find the grid containing this log
                if (PlcLogsTab?.LogsGrid?.InnerDataGrid != null && PlcLogsTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                {
                    targetGrid = PlcLogsTab.LogsGrid.InnerDataGrid;
                    gridName = "MainLogsGrid";
                }
                else if (PlcFilteredTab?.LogsGrid?.InnerDataGrid != null && PlcFilteredTab.LogsGrid.InnerDataGrid.Items.Contains(log))
                {
                    targetGrid = PlcFilteredTab.LogsGrid.InnerDataGrid;
                    gridName = "FilteredLogsGrid";
                }
                else if (AppLogsTab?.InnerDataGrid != null && AppLogsTab.InnerDataGrid.Items.Contains(log))
                {
                    targetGrid = AppLogsTab.InnerDataGrid;
                    gridName = "AppLogsGrid";
                }

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
            }
        }
        private void GraphsView_Loaded(object sender, RoutedEventArgs e) { }
        private void Button_Click(object sender, RoutedEventArgs e) { }

        private void PlcLogsTab_Loaded(object sender, RoutedEventArgs e)
        {

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
    }
}