using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ITabTearOffManager
    {
        void Initialize(Window mainWindow, TabControl mainTabControl);
        bool IsTabDetachable(TabItem tabItem);
        bool IsTabDetached(string header);
        DetachedTabWindow DetachTab(TabItem tabItem, Point screenPosition);
        void ReattachTab(string header);
        void ReattachAll();
        T GetDetachedControl<T>(string tabHeader) where T : class;
        IEnumerable<DetachedTabWindow> GetDetachedWindows();
        int GetAttachedTabCount();
    }
}
