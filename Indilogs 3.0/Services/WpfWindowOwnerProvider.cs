using System.Windows;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services
{
    public class WpfWindowOwnerProvider : IWindowOwnerProvider
    {
        public Window? GetOwner() => Application.Current?.MainWindow;
    }
}
