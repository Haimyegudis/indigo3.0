using System.Windows;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Abstraction over window creation for testability and MVVM compliance.
    /// ViewModels should use this instead of calling "new Window()" directly.
    /// </summary>
    public interface IViewFactory
    {
        T Create<T>(params object[] args) where T : Window;
    }
}
