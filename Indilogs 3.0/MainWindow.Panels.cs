using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndiLogs_3._0
{
    public partial class MainWindow
    {
        private Point _lastMousePosition;
        private bool _isDragging;
        // Per-tab panel width storage (default is 200 for all tabs)
        private Dictionary<int, double> _tabPanelWidths = new Dictionary<int, double>();
        private const double DEFAULT_PANEL_WIDTH = 200;
        private const int CHARTS_TAB_INDEX = AppConstants.TAB_CHARTS;
        private int _previousTabIndex = 0;

        public void AppLogsGrid_Sorting(object? sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                System.ComponentModel.ListSortDirection direction = (e.Column.SortDirection != System.ComponentModel.ListSortDirection.Ascending) ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending;
                e.Column.SortDirection = direction;
                _ = vm.SortAppLogs(e.Column.SortMemberPath, direction == System.ComponentModel.ListSortDirection.Ascending);
            }
        }

        // ==========================================
        //  FIXED SCREENSHOTS LOGIC (Zoom & Drag)
        // ==========================================

        private ScrollViewer? GetScreenshotScrollViewer() => this.FindName("ScreenshotScrollViewer") as ScrollViewer;

        private void OnScreenshotMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && DataContext is MainViewModel vm)
            {
                if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
                else vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }
            // Without Ctrl, let ScrollViewer handle normal scrolling
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
                string? tabName = null;
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
