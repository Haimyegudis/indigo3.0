using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Manages child windows to ensure they open on the same screen as the main window
    /// and allows easy switching between windows without minimizing.
    /// Windows are NON-MODAL - user can freely switch between any window.
    /// </summary>
    public static partial class WindowManager
    {
        private static readonly List<WeakReference<Window>> _childWindows = new List<WeakReference<Window>>();
        private static Window? _mainWindow;

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
        public static void OpenWindow(Window childWindow, Window? referenceWindow = null)
        {
            if (childWindow == null) return;
            var winSw = Stopwatch.StartNew();

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
            AppLogger.Info($"[Window] Opened {childWindow.GetType().Name} — {winSw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Shows a MODAL dialog window on the same screen as the main window.
        /// This blocks interaction with other windows until closed.
        /// </summary>
        public static bool? ShowDialog(Window dialogWindow, Window? owner = null)
        {
            if (dialogWindow == null) return null;
            var dlgSw = Stopwatch.StartNew();

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

            var result = dialogWindow.ShowDialog();
            AppLogger.Info($"[Window] ShowDialog {dialogWindow.GetType().Name} — {dlgSw.ElapsedMilliseconds}ms");
            return result;
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
                if (weakRef.TryGetTarget(out Window? window) && window.IsVisible)
                {
                    yield return window;
                }
            }
        }

        /// <summary>
        /// Finds an open window of a specific type
        /// </summary>
        public static T? FindWindow<T>() where T : Window
        {
            CleanupDeadReferences();

            foreach (var weakRef in _childWindows)
            {
                if (weakRef.TryGetTarget(out Window? window) && window is T typedWindow && window.IsVisible)
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
        public static T GetOrCreate<T>(Func<T> factory, Window? referenceWindow = null) where T : Window
        {
            var winSw = Stopwatch.StartNew();
            var existing = FindWindow<T>();
            if (existing != null)
            {
                ActivateWindow(existing);
                AppLogger.Info($"[Window] GetOrCreate<{typeof(T).Name}> reused existing — {winSw.ElapsedMilliseconds}ms");
                return existing;
            }

            var newWindow = factory();
            OpenWindow(newWindow, referenceWindow);
            AppLogger.Info($"[Window] GetOrCreate<{typeof(T).Name}> created new — {winSw.ElapsedMilliseconds}ms");
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

    /// <summary>
    /// Instance adapter that delegates to the static <see cref="WindowManager"/>.
    /// Allows <see cref="Interfaces.IWindowManager"/> to be resolved via the Bootstrapper.
    /// </summary>
    internal sealed class WindowManagerAdapter : Interfaces.IWindowManager
    {
        public void Initialize(Window mainWindow) => WindowManager.Initialize(mainWindow);
        public void OpenWindow(Window childWindow, Window? referenceWindow = null) => WindowManager.OpenWindow(childWindow, referenceWindow);
        public bool? ShowDialog(Window dialogWindow, Window? owner = null) => WindowManager.ShowDialog(dialogWindow, owner);
        public void ActivateWindow(Window window) => WindowManager.ActivateWindow(window);
        public IEnumerable<Window> GetOpenWindows() => WindowManager.GetOpenWindows();
        public T? FindWindow<T>() where T : Window => WindowManager.FindWindow<T>();
        public bool ActivateExisting<T>() where T : Window => WindowManager.ActivateExisting<T>();
        public T GetOrCreate<T>(Func<T> factory, Window? referenceWindow = null) where T : Window => WindowManager.GetOrCreate(factory, referenceWindow);
        public void ActivateMainWindow() => WindowManager.ActivateMainWindow();
    }
}
