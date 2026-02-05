using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Controls;
using IndiLogs_3._0.Controls.Charts;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Manages the lifecycle of detached (torn-off) tabs.
    /// Handles detaching tabs from the MainWindow TabControl into floating windows
    /// and reattaching them back.
    /// </summary>
    public static class TabTearOffManager
    {
        private static readonly Dictionary<string, DetachedTabInfo> _detachedTabs = new Dictionary<string, DetachedTabInfo>();
        private static TabControl _mainTabControl;
        private static Window _mainWindow;

        /// <summary>
        /// Information about a detached tab
        /// </summary>
        private class DetachedTabInfo
        {
            public string Header { get; set; }
            public int OriginalIndex { get; set; }
            public UIElement Content { get; set; }
            public TabItem TabItem { get; set; }
            public DetachedTabWindow Window { get; set; }
        }

        /// <summary>
        /// Initialize with the main window and its TabControl
        /// </summary>
        public static void Initialize(Window mainWindow, TabControl mainTabControl)
        {
            _mainWindow = mainWindow;
            _mainTabControl = mainTabControl;
        }

        /// <summary>
        /// Returns true if the tab header content is a UserControl (detachable)
        /// </summary>
        public static bool IsTabDetachable(TabItem tabItem)
        {
            if (tabItem == null) return false;
            return tabItem.Content is UserControl;
        }

        /// <summary>
        /// Returns true if the tab with the given header is currently detached
        /// </summary>
        public static bool IsTabDetached(string header)
        {
            return _detachedTabs.ContainsKey(header);
        }

        /// <summary>
        /// Detaches a tab from the main TabControl into a floating window
        /// </summary>
        public static DetachedTabWindow DetachTab(TabItem tabItem, Point screenPosition)
        {
            if (tabItem == null || _mainTabControl == null || _mainWindow == null)
                return null;

            string header = tabItem.Header?.ToString();
            if (string.IsNullOrEmpty(header) || IsTabDetached(header))
                return null;

            // Only detach UserControl-based tabs
            if (!(tabItem.Content is UserControl))
                return null;

            var content = tabItem.Content as UIElement;
            int originalIndex = _mainTabControl.Items.IndexOf(tabItem);

            // Remove content from TabItem (clears the logical parent)
            tabItem.Content = null;

            // Create placeholder to show in the main tab
            var placeholder = CreatePlaceholder(header);
            tabItem.Content = placeholder;

            // Create the floating window
            var floatingWindow = new DetachedTabWindow(header, _mainWindow.DataContext);
            floatingWindow.SetContent(content);

            // Position the window at the drop point
            floatingWindow.Left = screenPosition.X - 100;
            floatingWindow.Top = screenPosition.Y - 30;

            // Track the detached tab
            var info = new DetachedTabInfo
            {
                Header = header,
                OriginalIndex = originalIndex,
                Content = content,
                TabItem = tabItem,
                Window = floatingWindow
            };
            _detachedTabs[header] = info;

            // Subscribe to reattach on close
            floatingWindow.RequestReattach += OnRequestReattach;

            // Show via WindowManager for proper multi-monitor handling
            WindowManager.OpenWindow(floatingWindow);

            // Select the next available tab in main window
            SelectNextAvailableTab(originalIndex);

            System.Diagnostics.Debug.WriteLine($"[TEAR-OFF] Detached tab: {header}");

            return floatingWindow;
        }

        /// <summary>
        /// Reattaches a tab back to the main TabControl
        /// </summary>
        public static void ReattachTab(string header)
        {
            if (!_detachedTabs.TryGetValue(header, out var info))
                return;

            // Remove content from floating window
            info.Window.ClearContent();

            // Remove the placeholder from the TabItem
            info.TabItem.Content = null;

            // Restore original content
            info.TabItem.Content = info.Content;

            // Remove from tracking
            _detachedTabs.Remove(header);

            // Select the restored tab
            _mainTabControl.SelectedItem = info.TabItem;

            System.Diagnostics.Debug.WriteLine($"[TEAR-OFF] Reattached tab: {header}");
        }

        /// <summary>
        /// Reattaches all detached tabs (called on app shutdown)
        /// </summary>
        public static void ReattachAll()
        {
            var headers = _detachedTabs.Keys.ToList();
            foreach (var header in headers)
            {
                if (_detachedTabs.TryGetValue(header, out var info))
                {
                    // Close the window without triggering reattach event
                    info.Window.RequestReattach -= OnRequestReattach;

                    // Clear content and restore
                    info.Window.ClearContent();
                    info.TabItem.Content = null;
                    info.TabItem.Content = info.Content;

                    info.Window.CloseFromManager();
                }
            }
            _detachedTabs.Clear();
        }

        /// <summary>
        /// Gets a detached control by type and tab header
        /// </summary>
        public static T GetDetachedControl<T>(string tabHeader) where T : class
        {
            if (_detachedTabs.TryGetValue(tabHeader, out var info))
            {
                return info.Content as T;
            }
            return null;
        }

        /// <summary>
        /// Gets all detached windows
        /// </summary>
        public static IEnumerable<DetachedTabWindow> GetDetachedWindows()
        {
            return _detachedTabs.Values.Select(info => info.Window);
        }

        /// <summary>
        /// Gets the number of non-detached tabs remaining
        /// </summary>
        public static int GetAttachedTabCount()
        {
            if (_mainTabControl == null) return 0;
            return _mainTabControl.Items.Count - _detachedTabs.Count;
        }

        private static void OnRequestReattach(string header)
        {
            ReattachTab(header);
        }

        private static void SelectNextAvailableTab(int fromIndex)
        {
            if (_mainTabControl == null) return;

            // Try the next tab first, then previous
            for (int i = 0; i < _mainTabControl.Items.Count; i++)
            {
                int checkIndex = (fromIndex + i + 1) % _mainTabControl.Items.Count;
                var tabItem = _mainTabControl.Items[checkIndex] as TabItem;
                if (tabItem != null && !IsTabDetached(tabItem.Header?.ToString()))
                {
                    _mainTabControl.SelectedIndex = checkIndex;
                    return;
                }
            }
        }

        private static UIElement CreatePlaceholder(string tabHeader)
        {
            var grid = new System.Windows.Controls.Grid
            {
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark")
            };

            var stackPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new TextBlock
            {
                Text = "\u29C9",  // ⧉ Unicode symbol for detached
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var message = new TextBlock
            {
                Text = $"\"{tabHeader}\" is in a separate window",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var reattachButton = new Button
            {
                Content = "Return to Main Window",
                Padding = new Thickness(20, 8, 20, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor"),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13
            };
            reattachButton.Click += (s, e) => ReattachTab(tabHeader);

            stackPanel.Children.Add(icon);
            stackPanel.Children.Add(message);
            stackPanel.Children.Add(reattachButton);
            grid.Children.Add(stackPanel);

            return grid;
        }
    }
}
