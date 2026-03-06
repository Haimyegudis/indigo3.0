using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IndiLogs_3._0.Services
{
    public static partial class WindowManager
    {
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Window activation failed: {ex.Message}");
                window.Activate();
                window.Focus();
            }
        }

        /// <summary>
        /// Positions a window on the same screen as the reference window, centered
        /// </summary>
        private static void PositionOnSameScreen(Window window, Window? referenceWindow = null)
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

                    // Calculate centered position ON TOP of the reference window (main app)
                    double left, top;
                    if (refWindow != null && refWindow.IsLoaded && !double.IsNaN(refWindow.Left) && !double.IsNaN(refWindow.Top))
                    {
                        // Center on the reference window itself
                        double refCenterX = refWindow.Left + (refWindow.ActualWidth > 0 ? refWindow.ActualWidth : refWindow.Width) / 2;
                        double refCenterY = refWindow.Top + (refWindow.ActualHeight > 0 ? refWindow.ActualHeight : refWindow.Height) / 2;
                        left = refCenterX - windowWidth / 2;
                        top = refCenterY - windowHeight / 2;
                    }
                    else
                    {
                        // Fallback: center on monitor work area
                        left = workArea.Left + (workWidth - windowWidth) / 2;
                        top = workArea.Top + (workHeight - windowHeight) / 2;
                    }

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
            catch (Exception ex)
            {
                AppLogger.Warn($"Window positioning failed: {ex.Message}");
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
    }
}
