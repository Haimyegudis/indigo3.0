using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndiLogs_3._0.Views
{
    public partial class FilterWindow : Window
    {
        public FilterEditorViewModel ViewModel { get; private set; }
        public bool ShouldClearAllFilters { get; private set; }
        private FrameworkElement _anchorElement;

        public FilterWindow()
        {
            InitializeComponent();
            ViewModel = new FilterEditorViewModel();
            DataContext = ViewModel;
            this.Loaded += FilterWindow_Loaded;
        }

        public FilterWindow(IEnumerable<string> availableThreads, IEnumerable<string> availableLoggers)
        {
            InitializeComponent();
            ViewModel = new FilterEditorViewModel();

            // Populate available values for dropdowns
            if (availableThreads != null)
                ViewModel.AvailableThreads = availableThreads.Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

            if (availableLoggers != null)
                ViewModel.AvailableLoggers = availableLoggers.Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();

            DataContext = ViewModel;
            this.Loaded += FilterWindow_Loaded;
        }

        private void FilterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position after loaded
            if (_anchorElement != null)
            {
                PositionWindowNearElement();
            }
        }

        /// <summary>
        /// Position the window near the specified button/element
        /// </summary>
        public void PositionNearElement(FrameworkElement element)
        {
            _anchorElement = element;
        }

        private void PositionWindowNearElement()
        {
            if (_anchorElement == null) return;

            try
            {
                // Find the main window
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null) return;

                // Get the position of the anchor element relative to the main window
                var transform = _anchorElement.TransformToVisual(mainWindow);
                var positionInMainWindow = transform.Transform(new Point(0, _anchorElement.ActualHeight));

                // Convert to screen coordinates
                var mainWindowPosition = mainWindow.PointToScreen(new Point(0, 0));

                double screenX = mainWindowPosition.X + positionInMainWindow.X;
                double screenY = mainWindowPosition.Y + positionInMainWindow.Y;

                // Set the window position
                this.Left = screenX;
                this.Top = screenY;

                // Make sure window doesn't go off screen
                var screen = System.Windows.SystemParameters.WorkArea;
                if (this.Left + this.ActualWidth > screen.Right)
                    this.Left = screen.Right - this.ActualWidth;
                if (this.Top + this.ActualHeight > screen.Bottom)
                    this.Top = screenY - _anchorElement.ActualHeight - this.ActualHeight; // Show above
                if (this.Left < screen.Left)
                    this.Left = screen.Left;
                if (this.Top < screen.Top)
                    this.Top = screen.Top;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FILTER WINDOW] Position failed: {ex.Message}");
                // Fallback - center on owner
                if (this.Owner != null)
                {
                    this.Left = this.Owner.Left + (this.Owner.Width - this.Width) / 2;
                    this.Top = this.Owner.Top + (this.Owner.Height - this.Height) / 2;
                }
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            // Clear the filter tree and signal to clear all filters
            ViewModel.RootNodes.Clear();
            ViewModel.RootNodes.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });
            ShouldClearAllFilters = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OperatorTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.ContextMenu != null)
            {
                textBlock.ContextMenu.PlacementTarget = textBlock;
                textBlock.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }
    }
}