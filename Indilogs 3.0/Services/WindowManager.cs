using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Manages child windows to ensure they open on the same screen as the main window
    /// and allows easy switching between windows without minimizing.
    /// Windows are NON-MODAL - user can freely switch between any window.
    /// </summary>
    public static class WindowManager
    {
        private static readonly List<WeakReference<Window>> _childWindows = new List<WeakReference<Window>>();
        private static Window _mainWindow;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Initialize with the main application window
        /// </summary>
        public static void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        /// <summary>
        /// Opens a child window on the same screen as the main window,
        /// centered on that screen. Window is NON-MODAL - user can switch freely.
        /// Does NOT set Owner to allow independent window operation.
        /// </summary>
        public static void OpenWindow(Window childWindow, Window referenceWindow = null)
        {
            if (childWindow == null) return;

            // DON'T set Owner for non-modal windows - this allows free switching between windows
            // Owner would make the window stay on top of owner and block interaction

            // Track child window
            TrackWindow(childWindow);

            // Store reference for positioning after window is loaded
            var refWindow = referenceWindow ?? _mainWindow;

            // Position window on same screen - do it before Show()
            PositionOnSameScreen(childWindow, refWindow);

            // Handle Loaded event - reposition and bring to front after window is fully loaded
            childWindow.Loaded += (s, e) =>
            {
                PositionOnSameScreen(childWindow, refWindow);
                BringWindowToFront(childWindow);
            };

            // Show the window first
            childWindow.Show();

            // Force window to front using aggressive approach
            BringWindowToFront(childWindow);
        }

        /// <summary>
        /// Aggressively brings a window to the foreground using multiple Win32 techniques
        /// </summary>
        private static void BringWindowToFront(Window window)
        {
            if (window == null) return;

            try
            {
                // First, use WPF methods
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();

                // Get the window handle
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                // Get foreground window info
                var foregroundWindow = GetForegroundWindow();
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
                uint currentThreadId = GetCurrentThreadId();

                // Attach to foreground thread to steal focus
                if (foregroundThreadId != currentThreadId)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, true);
                    SetForegroundWindow(hwnd);
                    BringWindowToTop(hwnd);
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
                else
                {
                    SetForegroundWindow(hwnd);
                    BringWindowToTop(hwnd);
                }

                // Final WPF activation
                window.Activate();
                window.Focus();
            }
            catch
            {
                // Fallback to basic activation
                window.Activate();
                window.Focus();
            }
        }

        /// <summary>
        /// Shows a MODAL dialog window on the same screen as the main window.
        /// This blocks interaction with other windows until closed.
        /// </summary>
        public static bool? ShowDialog(Window dialogWindow, Window owner = null)
        {
            if (dialogWindow == null) return null;

            // For dialogs, we DO set owner to make them modal
            if (owner != null)
            {
                dialogWindow.Owner = owner;
            }
            else if (_mainWindow != null)
            {
                dialogWindow.Owner = _mainWindow;
            }

            // Position on same screen as owner/main window
            PositionOnSameScreen(dialogWindow, owner);

            return dialogWindow.ShowDialog();
        }

        /// <summary>
        /// Positions a window on the same screen as the reference window, centered
        /// </summary>
        private static void PositionOnSameScreen(Window window, Window referenceWindow = null)
        {
            if (window == null) return;

            // Get the reference window (provided or main window)
            var refWindow = referenceWindow ?? _mainWindow;
            if (refWindow == null || !refWindow.IsLoaded)
            {
                // Fallback to center screen
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            // Set to manual so we can position it ourselves
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            try
            {
                // Get the handle of the reference window
                var hwnd = new WindowInteropHelper(refWindow).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    return;
                }

                // Get the monitor info
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };

                if (GetMonitorInfo(monitor, ref info))
                {
                    var workArea = info.rcWork;
                    var workWidth = workArea.Right - workArea.Left;
                    var workHeight = workArea.Bottom - workArea.Top;

                    // Get window dimensions - use ActualWidth/Height if available, otherwise use Width/Height
                    double windowWidth = window.Width;
                    double windowHeight = window.Height;

                    // If Width/Height are NaN, use default or ActualWidth/Height
                    if (double.IsNaN(windowWidth) || windowWidth <= 0)
                        windowWidth = window.ActualWidth > 0 ? window.ActualWidth : 800;
                    if (double.IsNaN(windowHeight) || windowHeight <= 0)
                        windowHeight = window.ActualHeight > 0 ? window.ActualHeight : 600;

                    // Calculate centered position on the SAME monitor as the reference window
                    var left = workArea.Left + (workWidth - windowWidth) / 2;
                    var top = workArea.Top + (workHeight - windowHeight) / 2;

                    // Ensure window is within bounds of the target monitor
                    if (left < workArea.Left) left = workArea.Left;
                    if (top < workArea.Top) top = workArea.Top;
                    if (left + windowWidth > workArea.Right) left = workArea.Right - windowWidth;
                    if (top + windowHeight > workArea.Bottom) top = workArea.Bottom - windowHeight;

                    window.Left = left;
                    window.Top = top;
                }
                else
                {
                    // Fallback: position relative to reference window
                    window.Left = refWindow.Left + 50;
                    window.Top = refWindow.Top + 50;
                }
            }
            catch
            {
                // Fallback: position relative to reference window if available
                if (refWindow != null && refWindow.IsLoaded)
                {
                    window.Left = refWindow.Left + 50;
                    window.Top = refWindow.Top + 50;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
        }

        /// <summary>
        /// Track a window for easy switching
        /// </summary>
        private static void TrackWindow(Window window)
        {
            // Clean up dead references
            CleanupDeadReferences();

            // Add weak reference
            _childWindows.Add(new WeakReference<Window>(window));

            // Subscribe to closed event for cleanup
            window.Closed += (s, e) => CleanupDeadReferences();
        }

        /// <summary>
        /// Activates a window, bringing it to front without minimizing others
        /// </summary>
        public static void ActivateWindow(Window window)
        {
            if (window == null) return;

            // Restore if minimized
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            // Bring to front and activate
            window.Activate();
            window.Focus();
        }

        /// <summary>
        /// Gets all currently open child windows
        /// </summary>
        public static IEnumerable<Window> GetOpenWindows()
        {
            CleanupDeadReferences();

            foreach (var weakRef in _childWindows)
            {
                if (weakRef.TryGetTarget(out Window window) && window.IsVisible)
                {
                    yield return window;
                }
            }
        }

        /// <summary>
        /// Finds an open window of a specific type
        /// </summary>
        public static T FindWindow<T>() where T : Window
        {
            CleanupDeadReferences();

            foreach (var weakRef in _childWindows)
            {
                if (weakRef.TryGetTarget(out Window window) && window is T typedWindow && window.IsVisible)
                {
                    return typedWindow;
                }
            }

            return null;
        }

        /// <summary>
        /// Activates an existing window of a specific type, or returns false if none exists
        /// </summary>
        public static bool ActivateExisting<T>() where T : Window
        {
            var existing = FindWindow<T>();
            if (existing != null)
            {
                ActivateWindow(existing);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets or creates a window of a specific type. If one exists, it's activated and returned.
        /// Otherwise, the factory is called to create a new one.
        /// </summary>
        public static T GetOrCreate<T>(Func<T> factory, Window referenceWindow = null) where T : Window
        {
            var existing = FindWindow<T>();
            if (existing != null)
            {
                ActivateWindow(existing);
                return existing;
            }

            var newWindow = factory();
            OpenWindow(newWindow, referenceWindow);
            return newWindow;
        }

        private static void CleanupDeadReferences()
        {
            _childWindows.RemoveAll(weakRef => !weakRef.TryGetTarget(out _));
        }

        /// <summary>
        /// Brings the main window to front
        /// </summary>
        public static void ActivateMainWindow()
        {
            if (_mainWindow != null)
            {
                ActivateWindow(_mainWindow);
            }
        }
    }
}
