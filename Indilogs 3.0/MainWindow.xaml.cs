using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    public partial class MainWindow : Window, ITabHost
    {
        public ObservableCollection<LogEntry>? MarkedAppLogs { get; set; }
        private Point _lastMousePosition;
        private bool _isDragging;
        // Per-tab panel width storage (default is 200 for all tabs)
        private System.Collections.Generic.Dictionary<int, double> _tabPanelWidths = new System.Collections.Generic.Dictionary<int, double>();
        private const double DEFAULT_PANEL_WIDTH = 200;
        private const int CHARTS_TAB_INDEX = AppConstants.TAB_CHARTS;
        private int _previousTabIndex = 0;

        public MainWindow()
        {
            InitializeComponent();

            // Resolve MainViewModel from the DI container (replaces XAML-based <vm:MainViewModel/>)
            DataContext = Bootstrapper.Resolve<MainViewModel>();

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
            // Unsubscribe singleton events to prevent memory leaks
            ChartDataTransferService.Instance.OnSwitchToChartsRequested -= SwitchToChartsTab;
            ChartDataTransferService.Instance.OnChartTimeSelected -= OnChartTimeSelected;

            // Save column settings (widths, order, visibility) before closing
            try
            {
                SaveAllGridColumnSettings(this);
            }
            catch (Exception ex) { AppLogger.Error("Saving grid column settings on close failed", ex); }

            TabTearOffManager.ReattachAll();
            base.OnClosed(e);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void SaveAllGridColumnSettings(DependencyObject parent)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Controls.PlcLogsGridControl grid)
                {
                    grid.SaveColumnSettings();
                }
                else
                {
                    SaveAllGridColumnSettings(child);
                }
            }
        }

        /// <summary>
        /// "+" button click: builds context menu with skipped components and shows it.
        /// </summary>
        private void AddBackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is IndiLogs_3._0.ViewModels.MainViewModel vm)
            {
                var skipped = vm.GetSkippedComponents();
                if (skipped.Count == 0) return;

                var menu = AddBackContextMenu;
                menu.Items.Clear();

                foreach (var (name, displayName) in skipped)
                {
                    var item = new System.Windows.Controls.MenuItem
                    {
                        Header = displayName,
                        CommandParameter = name,
                        Command = vm.AddBackComponentCommand
                    };
                    menu.Items.Add(item);
                }

                menu.PlacementTarget = sender as Button;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
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

        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
            else if (e.PropertyName == nameof(MainViewModel.SelectedLog))
            {
                // Update Log Details panel content when selected log changes
                UpdateLogDetailsField();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsLogDetailsPinned))
            {
                // When unpinning via pin button, collapse the panel back to auto-hide label
                if (sender is MainViewModel vm2 && !vm2.IsLogDetailsPinned)
                {
                    CollapseLogDetailsPanel();
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

        private void Window_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is MainViewModel vm) vm.OnFilesDropped(files);
            }
        }



        // --- Copy Logic ---
        public void MainLogsGrid_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // If text is selected inside a cell control, let it handle Ctrl+C natively
                var focused = Keyboard.FocusedElement;
                if (focused is System.Windows.Controls.TextBox tb && tb.SelectionLength > 0)
                    return;
                if (focused is System.Windows.Controls.RichTextBox rtb && !rtb.Selection.IsEmpty)
                    return;

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
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.ffffff").PadRight(maxTime);
                string level = (log.Level ?? "").PadRight(maxLevel + 2);
                string thread = (log.ThreadName ?? "").PadRight(maxThread + 2);
                string msg = log.Message ?? "";
                sb.AppendLine($"{time} {level} {thread} {msg}");
            }
            try { Clipboard.SetText(sb.ToString()); }
            catch (Exception ex) { AppLogger.Error("CopySelectedLogsToClipboard failed", ex); }
        }

        private void SearchTextBox_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.Visibility == Visibility.Visible) { tb.Focus(); tb.SelectAll(); }
        }

        private void TreeViewItem_PreviewMouseRightButtonDown(object? sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
            if (treeViewItem != null) { treeViewItem.Focus(); e.Handled = true; }
        }

        static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem)) source = VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        public void AppLogsGrid_Sorting(object? sender, DataGridSortingEventArgs e)
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

        private void OnScreenshotMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && DataContext is MainViewModel vm)
            {
                if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
                else vm.ZoomOutCommand.Execute(null);

                e.Handled = true;
            }
        }

        private void OnImageMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            var scrollViewer = GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            scrollViewer.PanningMode = PanningMode.None;

            _lastMousePosition = e.GetPosition(scrollViewer);
            _isDragging = true;

            if (sender is FrameworkElement el) el.CaptureMouse();

            scrollViewer.Cursor = Cursors.SizeAll;
        }

        private void OnImageMouseMove(object? sender, MouseEventArgs e)
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

        private void OnImageMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            var scrollViewer = GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            _isDragging = false;

            if (sender is FrameworkElement el) el.ReleaseMouseCapture();

            scrollViewer.Cursor = Cursors.Arrow;
            scrollViewer.PanningMode = PanningMode.Both;
        }

        private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tabControl && e.Source == tabControl)
            {
                var tabSw = System.Diagnostics.Stopwatch.StartNew();
                // TabControl_SelectionChanged fires SYNCHRONOUSLY as part of the user click,
                // BEFORE WPF defers the layout/render pass for the new tab's content.
                // This is the safest place to set _isTabSwitching because it is guaranteed to
                // be set BEFORE the newly-visible DataGrid fires its initial ScrollChanged.
                // (The ViewModel's SelectedTabIndex setter is also correct but binding
                // propagation can sometimes lag the visual state change by one render cycle.)
                _isTabSwitching = true;
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => _isTabSwitching = false));

                int newTabIndex = tabControl.SelectedIndex;
                _previousTabIndex = newTabIndex;

                string[] tabNames = { "PLC", "APP", "PLC Filtered", "Charts", "Systab", "Config Explorer" };
                string tabLabel = newTabIndex >= 0 && newTabIndex < tabNames.Length ? tabNames[newTabIndex] : $"Tab {newTabIndex}";
                AppLogger.Info($"[Tab] Switched to tab {newTabIndex} ({tabLabel}) — {tabSw.ElapsedMilliseconds}ms");

                // IMPORTANT: Don't change column widths here - they are controlled by IsLeftPanelVisible/IsRightPanelVisible
                // Just sync with the ViewModel state to ensure columns match the panel visibility
                if (DataContext is MainViewModel vm)
                {
                    SyncPanelColumnsWithViewModel(vm);
                }

                // Execute deferred scroll-to-bottom for tabs that weren't rendered on initial load
                // MainTabs indices: 0=PLC, 1=APP
                string tabName = null;
                switch (newTabIndex)
                {
                    case 0: tabName = "PLC"; break;
                    case 1: tabName = "APP"; break;
                }
                if (tabName != null && _pendingScrollToBottom.Contains(tabName))
                {
                    // Capture for closure — tabName may change by the time the lambda runs
                    string capturedTabName = tabName;
                    // Defer to ApplicationIdle to allow the tab content to fully render first.
                    // Priority ordering: Loaded(6) fires BEFORE ApplicationIdle(2), so any
                    // time-sync scroll has already been applied by the time we get here.
                    // If TimeSyncScrollWasApplied is set, skip scroll-to-bottom to keep the
                    // synced position (otherwise the pending scroll-to-bottom would overwrite it).
                    Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        new Action(() =>
                        {
                            if (DataContext is MainViewModel vm && vm.TimeSyncScrollWasApplied)
                            {
                                // Sync scroll was applied at Loaded priority — don't overwrite it
                                vm.TimeSyncScrollWasApplied = false;
                                _pendingScrollToBottom.Remove(capturedTabName);
                                return;
                            }
                            ScrollGridToBottom(capturedTabName);
                        }));
                }
            }
        }
        private void GraphsView_Loaded(object? sender, RoutedEventArgs e) { }
        private void Button_Click(object? sender, RoutedEventArgs e) { }

        private void SystabTree_SelectedItemChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            if (vm != null && e.NewValue is Models.SystabTopicNode node)
            {
                vm.SelectedSystabNode = node;
            }
        }

        // --- Log Details Panel: Auto-hide behavior ---
        // Use ClearValue to remove local overrides so XAML DataTriggers can control visibility
        private void LogDetailsLabel_MouseEnter(object? sender, System.Windows.Input.MouseEventArgs e)
        {
            LogDetailsContent.Visibility = Visibility.Visible;
            LogDetailsAutoHideLabel.Visibility = Visibility.Collapsed;
            LogDetailsContent.MouseLeave += LogDetailsContent_MouseLeave;
        }

        private void LogDetailsContent_MouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            if (vm != null && !vm.IsLogDetailsPinned)
            {
                CollapseLogDetailsPanel();
            }
            LogDetailsContent.MouseLeave -= LogDetailsContent_MouseLeave;
        }

        private void LogDetailsClose_Click(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            if (vm != null)
            {
                vm.IsLogDetailsPinned = false;
                CollapseLogDetailsPanel();
            }
        }

        /// <summary>
        /// Collapse the Log Details panel and restore the auto-hide label.
        /// Uses ClearValue to remove local overrides so XAML Style/DataTriggers resume control.
        /// </summary>
        private void CollapseLogDetailsPanel()
        {
            LogDetailsContent.ClearValue(VisibilityProperty);
            LogDetailsAutoHideLabel.ClearValue(VisibilityProperty);
        }

        private void LogDetailsFieldSelector_Changed(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateLogDetailsField();
        }

        public void UpdateLogDetailsField()
        {
            var vm = DataContext as ViewModels.MainViewModel;
            if (vm?.SelectedLog == null || LogDetailsFieldContent == null || LogDetailsFieldSelector == null) return;

            var log = vm.SelectedLog;
            switch (LogDetailsFieldSelector.SelectedIndex)
            {
                case 0: LogDetailsFieldContent.Text = log.Message ?? ""; break;
                case 1: LogDetailsFieldContent.Text = log.Exception ?? ""; break;
                case 2: LogDetailsFieldContent.Text = log.Data ?? ""; break;
                case 3: LogDetailsFieldContent.Text = log.Method ?? ""; break;
                case 4: LogDetailsFieldContent.Text = log.Pattern ?? ""; break;
            }
        }

        private void PlcLogsTab_Loaded(object? sender, RoutedEventArgs e)
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
        private void LeftPanelHideButton_Click(object? sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel closing while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsLeftPanelVisible = false;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void LeftPanelShowButton_Click(object? sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel opening while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsLeftPanelVisible = true;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void ActiveFilterItem_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;

            if (sender is FrameworkElement fe && fe.Tag is string key && DataContext is MainViewModel vm)
            {
                vm.ClearActiveFilterCommand.Execute(key);
            }
        }

        private void RightPanelHideButton_Click(object? sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel closing while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsRightPanelVisible = false;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void RightPanelShowButton_Click(object? sender, MouseButtonEventArgs e)
        {
            // Only respond to double-click to prevent accidental panel opening while scrolling
            if (e.ClickCount != 2) return;

            if (DataContext is MainViewModel vm)
            {
                vm.IsRightPanelVisible = true;
                // Column sync happens in ViewModel_PropertyChanged
            }
        }

        private void PanelShowButton_MouseEnter(object? sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Opacity = 1;
            }
        }

        private void PanelShowButton_MouseLeave(object? sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Opacity = 0;
            }
        }

        /// <summary>
        /// Switches to the Charts tab.
        /// </summary>
        private void SwitchToChartsTab()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MainTabs.SelectedIndex = CHARTS_TAB_INDEX;
            }));
        }

        /// <summary>
        /// Handles chart time click to sync with logs
        /// </summary>
        private void OnChartTimeSelected(DateTime time)
        {
            if (DataContext is MainViewModel vm)
            {
                // Delegate to VM which uses O(log N) binary search instead of O(N log N) OrderBy
                vm.NavigateToLogTime(time);
            }
        }

    }
}