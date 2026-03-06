using IndiLogs_3._0.Interfaces;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
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
    public partial class MainWindow : Window, ITabHost
    {
        public ObservableCollection<LogEntry>? MarkedAppLogs { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            // Resolve MainViewModel from the DI container (replaces XAML-based <vm:MainViewModel/>)
            DataContext = Bootstrapper.Resolve<MainViewModel>();

            this.Loaded += MainWindow_Loaded;

            // Initialize WindowManager with main window
            WindowManager.Initialize(this);

            // Initialize TabTearOffManager
            TabTearOffManager.Initialize(this, MainTabs, Bootstrapper.Resolve<Services.Interfaces.IWindowManager>());

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
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) return;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Controls.PlcLogsGridControl plcGrid)
                    plcGrid.SaveColumnSettings();
                else if (child is Controls.AppLogsTabControl appGrid)
                    appGrid.SaveColumnSettings();
                else
                    SaveAllGridColumnSettings(child);
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
                _ = vm.OnFilesDropped(args);
            }
        }

        private void Window_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is MainViewModel vm) _ = vm.OnFilesDropped(files);
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
            var innerGrid = PlcLogsTab?.LogsGrid?.InnerDataGrid;
            if (innerGrid == null || innerGrid.SelectedItems.Count == 0) return;
            var sb = new StringBuilder();
            var selectedLogs = innerGrid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();
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
            TreeViewItem? treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
            if (treeViewItem != null) { treeViewItem.Focus(); e.Handled = true; }
        }

        static TreeViewItem? VisualUpwardSearch(DependencyObject? source)
        {
            while (source != null && source is not TreeViewItem)
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

    }
}