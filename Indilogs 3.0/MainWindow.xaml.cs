using IndiLogs_3._0.Models;
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

        // Per-tab panel width storage (default is 200 for all tabs)
        private System.Collections.Generic.Dictionary<int, double> _tabPanelWidths = new System.Collections.Generic.Dictionary<int, double>();
        private const double DEFAULT_PANEL_WIDTH = 200;
        private int _previousTabIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

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
                vm.PropertyChanged += ViewModel_PropertyChanged;

            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // No longer needed - TimeSync button styling is now handled via XAML binding in HeaderControl
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

                // Scroll to the exact vertical position
                scrollViewer.ScrollToVerticalOffset(logIndex);

                // Select the item
                targetGrid.SelectedItem = log;

                _isProgrammaticScroll = false;

                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ✅ Successfully scrolled to index {logIndex} in {gridName}");
            }
            catch (Exception ex)
            {
                _isProgrammaticScroll = false;
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Error: {ex.Message}");
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

                // Find the left panel column definition
                var mainGrid = this.Content as Grid;
                if (mainGrid != null && mainGrid.RowDefinitions.Count > 1)
                {
                    var contentGrid = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 1);
                    if (contentGrid != null && contentGrid.ColumnDefinitions.Count > 0)
                    {
                        var leftPanelColumn = contentGrid.ColumnDefinitions[0];

                        // Save current width for previous tab
                        if (leftPanelColumn.Width.IsAbsolute && leftPanelColumn.Width.Value > 0 && _previousTabIndex != 7)
                        {
                            _tabPanelWidths[_previousTabIndex] = leftPanelColumn.Width.Value;
                        }

                        // Restore width for other tabs (or use default)
                        double newWidth = _tabPanelWidths.ContainsKey(newTabIndex) ? _tabPanelWidths[newTabIndex] : DEFAULT_PANEL_WIDTH;
                        leftPanelColumn.Width = new GridLength(newWidth);

                        _previousTabIndex = newTabIndex;
                    }
                }
            }
        }
        private void GraphsView_Loaded(object sender, RoutedEventArgs e) { }
        private void Button_Click(object sender, RoutedEventArgs e) { }

        private void PlcLogsTab_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}