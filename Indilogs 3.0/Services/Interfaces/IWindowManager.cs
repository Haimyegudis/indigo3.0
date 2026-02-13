using System;
using System.Collections.Generic;
using System.Windows;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IWindowManager
    {
        void Initialize(Window mainWindow);
        void OpenWindow(Window childWindow, Window referenceWindow = null);
        bool? ShowDialog(Window dialogWindow, Window owner = null);
        void ActivateWindow(Window window);
        IEnumerable<Window> GetOpenWindows();
        T FindWindow<T>() where T : Window;
        bool ActivateExisting<T>() where T : Window;
        T GetOrCreate<T>(Func<T> factory, Window referenceWindow = null) where T : Window;
        void ActivateMainWindow();
    }
}
