using IndiLogs_3._0.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    // Drag-to-detach tab handlers extracted from MainWindow.xaml.cs
    // to reduce code-behind size.
    public partial class MainWindow
    {
        // ============================================
        //  Drag-to-Detach State
        // ============================================

        private Point _tabDragStartPoint;
        private bool _isTabDragging;
        private TabItem? _draggingTabItem;
        private System.Windows.Controls.Primitives.Popup? _dragPopup;

        // ============================================
        //  Drag-to-Detach Tab Handlers
        // ============================================

        private void MainTabs_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            // Only start drag if clicking on a TabItem header area
            var tabItem = FindTabItemFromPoint(e);
            if (tabItem == null || !TabTearOffManager.IsTabDetachable(tabItem))
                return;

            _tabDragStartPoint = e.GetPosition(null);
            _draggingTabItem = tabItem;
            _isTabDragging = false;
        }

        private void MainTabs_PreviewMouseMove(object? sender, MouseEventArgs e)
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

        private void MainTabs_PreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
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
                Background = new SolidColorBrush(Color.FromArgb(220, 27, 40, 56)),
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

        private TabItem? FindTabItemFromPoint(MouseButtonEventArgs e)
        {
            // Walk up the visual tree from the click source to find a TabItem
            DependencyObject? source = e.OriginalSource as DependencyObject;
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
        public void DetachTab_Click(object? sender, RoutedEventArgs e)
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
    }
}
