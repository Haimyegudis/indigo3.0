using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    /// <summary>
    /// Interaction logic for GraphsView.xaml
    /// GraphsView displays signal graphs, component tree, and state timeline
    /// </summary>
    public partial class GraphsView : UserControl
    {
        public GraphsView()
        {
            InitializeComponent();

            // Subscribe to lifecycle events
            Loaded += GraphsView_Loaded;
            Unloaded += GraphsView_Unloaded;
        }

        /// <summary>
        /// Called when the view is loaded and visible
        /// </summary>
        private void GraphsView_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialization logic
            // DataContext should be GraphsViewModel (set from MainViewModel)

            if (DataContext is GraphsViewModel vm)
            {
                // Optional: Auto-expand first chart if available
                if (vm.Charts != null && vm.Charts.Count > 0)
                {
                    vm.SelectedChart = vm.Charts[0];
                }
            }
        }

        /// <summary>
        /// Called when the view is being unloaded
        /// Cleanup resources and stop running processes
        /// </summary>
        private void GraphsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GraphsViewModel vm)
            {
                // Stop playback timer if running
                if (vm.IsPlaying)
                {
                    vm.PauseCommand?.Execute(null);
                }

                // Clear any large collections to free memory
                // The ViewModel will handle its own cleanup
            }
        }

        /// <summary>
        /// TreeView MouseWheel - Vertical Scroll Only
        /// </summary>
        private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Active Signals MouseWheel - Vertical Scroll Only
        /// </summary>
        private void ActiveSignals_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Timeline MouseWheel - Vertical Scroll Only
        /// </summary>
        private void Timeline_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Charts Area - MouseWheel for Vertical Scroll
        /// </summary>
        private void Charts_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Charts Area - Arrow Keys for Horizontal Scroll
        /// </summary>
        private void Charts_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                const double scrollAmount = 50;

                switch (e.Key)
                {
                    case Key.Left:
                        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - scrollAmount);
                        e.Handled = true;
                        break;

                    case Key.Right:
                        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                        e.Handled = true;
                        break;

                    case Key.Up:
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
                        e.Handled = true;
                        break;

                    case Key.Down:
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
                        e.Handled = true;
                        break;
                }
            }
        }

        /// <summary>
        /// TreeView Item Expanded - Dynamic Width Adjustment
        /// </summary>
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            UpdateLeftPanelWidth();
        }

        /// <summary>
        /// TreeView Item Collapsed - Dynamic Width Adjustment
        /// </summary>
        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
            UpdateLeftPanelWidth();
        }

        /// <summary>
        /// Update left panel width based on tree expansion
        /// </summary>
        private void UpdateLeftPanelWidth()
        {
            // Measure the TreeView's desired width
            if (ComponentTreeView != null)
            {
                ComponentTreeView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double desiredWidth = ComponentTreeView.DesiredSize.Width;

                // Clamp between 300 and 600
                double newWidth = Math.Max(300, Math.Min(600, desiredWidth + 40)); // +40 for padding

                // Find the parent Grid and update its Width
                var parentGrid = ComponentTreeView.Parent;
                while (parentGrid != null && !(parentGrid is Grid))
                {
                    parentGrid = LogicalTreeHelper.GetParent(parentGrid as DependencyObject);
                }

                if (parentGrid is Grid grid)
                {
                    grid.Width = newWidth;
                }
            }
        }

        /// <summary>
        /// Optional: Handle mouse wheel events for chart interaction
        /// Can be used for zooming with Ctrl+Wheel
        /// </summary>
        private void Chart_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+Wheel for zoom
                // This can be implemented in the ViewModel if needed
                e.Handled = true;
            }
        }

        /// <summary>
        /// Optional: Handle drag completed on GridSplitter
        /// Can be used to save panel sizes to settings
        /// </summary>
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Optional: Persist the new panel size to user settings
            // For example: save left panel width

            // This is handled automatically by WPF GridSplitter
            // No additional code needed unless you want to save preferences
        }

        /// <summary>
        /// Optional: Handle keyboard shortcuts
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (DataContext is GraphsViewModel vm)
            {
                // Example keyboard shortcuts
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    switch (e.Key)
                    {
                        case Key.OemPlus:
                        case Key.Add:
                            // Ctrl+Plus: Add new chart
                            vm.AddChartCommand?.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.OemMinus:
                        case Key.Subtract:
                            // Ctrl+Minus: Remove chart
                            vm.RemoveChartCommand?.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.R:
                            // Ctrl+R: Reset zoom
                            vm.ResetZoomCommand?.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.Space:
                            // Ctrl+Space: Play/Pause
                            if (vm.IsPlaying)
                                vm.PauseCommand?.Execute(null);
                            else
                                vm.PlayCommand?.Execute(null);
                            e.Handled = true;
                            break;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    // Escape: Pause playback if running
                    if (vm.IsPlaying)
                    {
                        vm.PauseCommand?.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}