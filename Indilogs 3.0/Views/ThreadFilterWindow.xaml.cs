using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class ThreadFilterWindow : Window
    {
        // שינוי: רשימה במקום משתנה בודד
        public List<string> SelectedThreads { get; private set; }
        public bool ShouldClear { get; private set; }
        private List<string> _allThreads;
        private FrameworkElement _anchorElement;

        public ThreadFilterWindow(IEnumerable<string> threads)
        {
            InitializeComponent();
            _allThreads = threads.OrderBy(t => t).ToList();
            ThreadsList.ItemsSource = _allThreads;

            this.Loaded += ThreadFilterWindow_Loaded;
        }

        private void ThreadFilterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position after loaded
            if (_anchorElement != null)
            {
                PositionWindowNearElement();
            }
            SearchBox.Focus();
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

                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Anchor: {_anchorElement.ActualWidth}x{_anchorElement.ActualHeight}");
                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Position in MainWindow: {positionInMainWindow.X}, {positionInMainWindow.Y}");
                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] MainWindow screen pos: {mainWindowPosition.X}, {mainWindowPosition.Y}");
                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Final screen pos: {screenX}, {screenY}");

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

                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Final adjusted pos: Left={this.Left}, Top={this.Top}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[THREAD FILTER] Position failed: {ex.Message}");
                // Fallback - center on owner
                if (this.Owner != null)
                {
                    this.Left = this.Owner.Left + (this.Owner.Width - this.Width) / 2;
                    this.Top = this.Owner.Top + (this.Owner.Height - this.Height) / 2;
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(filter))
            {
                ThreadsList.ItemsSource = _allThreads;
            }
            else
            {
                ThreadsList.ItemsSource = _allThreads.Where(t => t.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // שינוי: איסוף כל הפריטים שנבחרו
            if (ThreadsList.SelectedItems.Count > 0)
            {
                SelectedThreads = ThreadsList.SelectedItems.Cast<string>().ToList();
                DialogResult = true;
                Close();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ShouldClear = true;
            DialogResult = true;
            Close();
        }

        private void ThreadsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // דאבל קליק עדיין יבחר את הפריט הנוכחי וייצא
            if (ThreadsList.SelectedItem != null)
            {
                SelectedThreads = new List<string> { ThreadsList.SelectedItem.ToString() };
                DialogResult = true;
                Close();
            }
        }

        /// <summary>
        /// Close window when it loses focus (clicked outside)
        /// </summary>
        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Close the window when user clicks outside - but only if not already closing
            try
            {
                if (this.IsLoaded && this.IsVisible)
                {
                    this.Close();
                }
            }
            catch
            {
                // Ignore if already closing
            }
        }
    }
}
