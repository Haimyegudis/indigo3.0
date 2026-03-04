using System.Windows;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Provides the main window reference for dialog ownership.
    /// Replaces direct Application.Current.MainWindow access in ViewModels.
    /// </summary>
    public interface IWindowOwnerProvider
    {
        Window? GetOwner();
    }
}
