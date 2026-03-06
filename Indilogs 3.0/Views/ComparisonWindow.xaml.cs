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
        private ScrollViewer? _leftScrollViewer;
        private ScrollViewer? _rightScrollViewer;

        public ComparisonWindow(LogComparisonViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel;
            DataContext = _vm;

            Loaded += ComparisonWindow_Loaded;
        }

        private void ComparisonWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Cache scroll viewers for performance
            _leftScrollViewer = GetScrollViewer(LeftDataGrid);
            _rightScrollViewer = GetScrollViewer(RightDataGrid);

            // Subscribe to selection changes to sync selections between panes
            LeftDataGrid.SelectionChanged += LeftDataGrid_SelectionChanged;
            RightDataGrid.SelectionChanged += RightDataGrid_SelectionChanged;
        }

        private bool _isSelectionSyncing = false;

        private void LeftDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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

        private void RightDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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

        #region User Interactions

        private void DataGrid_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem is LogEntry log)
            {
                // Navigate to this log in the main window
                _vm.GoToSourceCommand.Execute(log);
            }
        }

        private void DataGrid_PreviewKeyDown(object? sender, KeyEventArgs e)
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

        private void HelpButton_Click(object? sender, RoutedEventArgs e)
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
• Sync OFF: Panes scroll independently",
                "Comparison Window Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion
    }
}
