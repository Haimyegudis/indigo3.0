using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
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
            if (e.PropertyName == nameof(MainViewModel.IsTimeSyncEnabled) && DataContext is MainViewModel vm)
            {
                UpdateTimeSyncButtonVisual(vm.IsTimeSyncEnabled);
            }
        }

        private void UpdateTimeSyncButtonVisual(bool isEnabled)
        {
            if (TimeSyncButton != null)
            {
                TimeSyncButton.Background = isEnabled ?
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")) :
                    new SolidColorBrush(Colors.Transparent);

                if (TimeSyncButton.Content is TextBlock textBlock)
                {
                    textBlock.Foreground = isEnabled ?
                        new SolidColorBrush(Colors.White) :
                        (Brush)FindResource("TextPrimary");
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
            if (MainLogsGrid != null && MainLogsGrid.Items.Contains(log))
            {
                targetGrid = MainLogsGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: MainLogsGrid");
            }
            else if (FilteredLogsGrid != null && FilteredLogsGrid.Items.Contains(log))
            {
                targetGrid = FilteredLogsGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: FilteredLogsGrid");
            }
            else if (AppLogsGrid != null && AppLogsGrid.Items.Contains(log))
            {
                targetGrid = AppLogsGrid;
                System.Diagnostics.Debug.WriteLine("[SCROLL TO LOG] Target: AppLogsGrid");
            }

            if (targetGrid == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] No grid found! MainLogsGrid={MainLogsGrid != null}, FilteredLogsGrid={FilteredLogsGrid != null}, AppLogsGrid={AppLogsGrid != null}");
                return;
            }

            try
            {
                // Find the index of the log in the grid
                int logIndex = targetGrid.Items.IndexOf(log);
                if (logIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Log not found in grid items");
                    return;
                }

                // Get the ScrollViewer
                var scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
                if (scrollViewer == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] ScrollViewer not found");
                    return;
                }

                // Allow scrolling only for this specific programmatic action
                _isProgrammaticScroll = true;

                // Scroll to the exact vertical position (make this log the FIRST visible row)
                scrollViewer.ScrollToVerticalOffset(logIndex);

                // Also select the item
                targetGrid.SelectedItem = log;

                _isProgrammaticScroll = false;

                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Successfully scrolled to index {logIndex} in {targetGrid.Name}");
            }
            catch (Exception ex)
            {
                _isProgrammaticScroll = false;
                System.Diagnostics.Debug.WriteLine($"[SCROLL TO LOG] Error: {ex.Message}");
            }
        }

        // --- CRITICAL FIX: PREVENTS AUTO-SCROLLING HORIZONTALLY ON CLICK ---
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

        private void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                // Find the ScrollViewer inside the DataGrid
                var scrollViewer = FindVisualChild<ScrollViewer>(grid);
                if (scrollViewer != null)
                {
                    // Subscribe to scroll changes
                    scrollViewer.ScrollChanged += (s, args) =>
                    {
                        // If horizontal offset changed and it wasn't programmatic scroll or user scroll
                        if (args.HorizontalChange != 0 && !_isProgrammaticScroll && !_isUserScrolling)
                        {
                            // This is auto-scroll from cell selection - prevent it!
                            scrollViewer.ScrollToHorizontalOffset(_lastUserHorizontalOffset);
                        }
                        else if (args.HorizontalChange != 0 && _isUserScrolling)
                        {
                            // User initiated scroll - remember this position
                            _lastUserHorizontalOffset = scrollViewer.HorizontalOffset;
                        }

                        // Time-Sync: Trigger sync on vertical scroll
                        if (args.VerticalChange != 0 && _isUserScrolling && !_isProgrammaticScroll)
                        {
                            TriggerTimeSyncScroll(grid);
                        }
                    };

                    // Detect user-initiated scrolling
                    scrollViewer.PreviewMouseWheel += (s, args) => { _isUserScrolling = true; };
                    scrollViewer.PreviewMouseDown += (s, args) => { _isUserScrolling = true; };
                    scrollViewer.PreviewMouseUp += (s, args) => {
                        _isUserScrolling = false;
                        _lastUserHorizontalOffset = scrollViewer.HorizontalOffset;
                    };
                }
            }
        }

        private void TriggerTimeSyncScroll(DataGrid sourceGrid)
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

            // Determine which grid this is
            string gridName = sourceGrid.Name;
            if (string.IsNullOrEmpty(gridName))
            {
                // Try to identify by items source
                if (sourceGrid.ItemsSource == vm.Logs)
                    gridName = "PLC";
                else if (sourceGrid.ItemsSource == vm.FilteredLogs)
                    gridName = "PLCFiltered";
                else if (sourceGrid.ItemsSource == vm.AppDevLogsFiltered)
                    gridName = "APP";
            }
            else
            {
                // Parse the name (MainLogsGrid, FilteredLogsGrid, AppLogsGrid)
                if (gridName.Contains("Main"))
                    gridName = "PLC";
                else if (gridName.Contains("Filtered"))
                    gridName = "PLCFiltered";
                else if (gridName.Contains("App"))
                    gridName = "APP";
            }

            // DEBUG: Log the scroll trigger
            System.Diagnostics.Debug.WriteLine($"[TIME-SYNC TRIGGER] Grid: {gridName}, Time: {logEntry.Date:HH:mm:ss.fff}, Index: {firstVisibleIndex}");

            // Trigger the sync
            vm.RequestSyncScroll(logEntry.Date, gridName);
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
        private void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                CopySelectedLogsToClipboard();
            }
        }

        private void CopySelectedLogsToClipboard()
        {
            if (MainLogsGrid.SelectedItems.Count == 0) return;
            var sb = new StringBuilder();
            var selectedLogs = MainLogsGrid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();
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

        private void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
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

                    
                        else
                        {
                            // Restore width for other tabs (or use default)
                            double newWidth = _tabPanelWidths.ContainsKey(newTabIndex) ? _tabPanelWidths[newTabIndex] : DEFAULT_PANEL_WIDTH;
                            leftPanelColumn.Width = new GridLength(newWidth);
                        }

                        _previousTabIndex = newTabIndex;
                    }
                }
            }
        }
        private void GraphsView_Loaded(object sender, RoutedEventArgs e) { }
        private void Button_Click(object sender, RoutedEventArgs e) { }
    }
}