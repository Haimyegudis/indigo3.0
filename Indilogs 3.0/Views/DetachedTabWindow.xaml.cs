using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    /// <summary>
    /// A floating window that hosts a detached tab's content.
    /// Implements ITabHost so child controls (PlcLogsGridControl, AppLogsTabControl)
    /// can call the same methods they use in MainWindow.
    /// </summary>
    public partial class DetachedTabWindow : Window, ITabHost
    {
        private string _tabHeader;
        private bool _isClosingFromManager = false;

        // Scroll management (mirrors MainWindow)
        private Dictionary<string, ScrollViewer> _scrollViewerCache = new Dictionary<string, ScrollViewer>();
        private bool _isProgrammaticScroll = false;
        private double _lastUserHorizontalOffset = 0;
        private bool _isUserScrolling = false;

        /// <summary>
        /// The tab header name this window is hosting (e.g. "PLC LOGS")
        /// </summary>
        public string TabHeader => _tabHeader;

        /// <summary>
        /// Event raised when the user closes this window (to trigger reattach)
        /// </summary>
        public event Action<string> RequestReattach;

        public DetachedTabWindow(string tabHeader, object dataContext)
        {
            InitializeComponent();
            _tabHeader = tabHeader;
            Title = tabHeader;
            DataContext = dataContext;
        }

        /// <summary>
        /// Sets the content of this floating window
        /// </summary>
        public void SetContent(UIElement content)
        {
            ContentHost.Content = content;
        }

        /// <summary>
        /// Clears the content before reattaching to main window
        /// </summary>
        public UIElement ClearContent()
        {
            var content = ContentHost.Content as UIElement;
            ContentHost.Content = null;
            return content;
        }

        /// <summary>
        /// Close without triggering reattach (used by TabTearOffManager.ReattachAll)
        /// </summary>
        public void CloseFromManager()
        {
            _isClosingFromManager = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Only request reattach if the user closed the window (not the manager)
            if (!_isClosingFromManager)
            {
                RequestReattach?.Invoke(_tabHeader);
            }
        }

        // ============================================
        //  ITabHost Implementation
        //  (mirrors MainWindow logic for child controls)
        // ============================================

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
                        // Try to infer grid name from context
                        if (grid.ItemsSource is System.Collections.IEnumerable && DataContext is MainViewModel vm)
                        {
                            if (grid.ItemsSource == vm.Logs) gridName = "MainLogsGrid";
                            else if (grid.ItemsSource == vm.FilteredLogs) gridName = "FilteredLogsGrid";
                            else if (grid.ItemsSource == vm.AppDevLogsFiltered) gridName = "AppLogsGrid";
                        }
                    }

                    if (!string.IsNullOrEmpty(gridName))
                    {
                        _scrollViewerCache[gridName] = scrollViewer;
                        System.Diagnostics.Debug.WriteLine($"[DETACHED CACHE] Cached ScrollViewer for grid: {gridName}");
                    }

                    scrollViewer.ScrollChanged += (s, args) =>
                    {
                        if (args.HorizontalChange != 0 && !_isProgrammaticScroll && !_isUserScrolling)
                        {
                            scrollViewer.ScrollToHorizontalOffset(_lastUserHorizontalOffset);
                        }
                        else if (args.HorizontalChange != 0 && _isUserScrolling)
                        {
                            _lastUserHorizontalOffset = scrollViewer.HorizontalOffset;
                        }

                        // Time-sync on vertical scroll
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

        public void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is LogEntry log)
            {
                e.Row.DetailsVisibility = (log.HasAnnotation && log.IsAnnotationExpanded)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                CopySelectedLogsToClipboard(sender as DataGrid);
            }
        }

        public void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                System.ComponentModel.ListSortDirection direction =
                    (e.Column.SortDirection != System.ComponentModel.ListSortDirection.Ascending)
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending;
                e.Column.SortDirection = direction;
                vm.SortAppLogs(e.Column.SortMemberPath, direction == System.ComponentModel.ListSortDirection.Ascending);
            }
        }

        // ============================================
        //  Helper Methods
        // ============================================

        private void TriggerTimeSyncScroll(DataGrid sourceGrid, string gridName)
        {
            if (!(DataContext is MainViewModel vm) || !vm.IsTimeSyncEnabled)
                return;

            var scrollViewer = FindVisualChild<ScrollViewer>(sourceGrid);
            if (scrollViewer == null)
                return;

            int firstVisibleIndex = (int)scrollViewer.VerticalOffset;
            if (firstVisibleIndex < 0 || firstVisibleIndex >= sourceGrid.Items.Count)
                return;

            var firstVisibleItem = sourceGrid.Items[firstVisibleIndex];
            if (!(firstVisibleItem is LogEntry logEntry))
                return;

            string sourceType = "PLC";
            if (gridName.Contains("App") || sourceGrid.ItemsSource == vm.AppDevLogsFiltered)
                sourceType = "APP";
            else if (gridName.Contains("Filtered"))
                sourceType = "PLCFiltered";

            vm.RequestSyncScroll(logEntry.Date, sourceType);
        }

        private void CopySelectedLogsToClipboard(DataGrid grid)
        {
            if (grid == null || grid.SelectedItems.Count == 0) return;
            var sb = new StringBuilder();
            var selectedLogs = grid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();
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

        /// <summary>
        /// Gets a cached ScrollViewer for a grid name (used for cross-window sync)
        /// </summary>
        public ScrollViewer GetCachedScrollViewer(string gridName)
        {
            _scrollViewerCache.TryGetValue(gridName, out var sv);
            return sv;
        }

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
    }
}
