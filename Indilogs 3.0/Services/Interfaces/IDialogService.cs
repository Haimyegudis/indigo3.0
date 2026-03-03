#nullable disable
using System.Windows;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Abstraction over MessageBox.Show for testability and MVVM compliance.
    /// ViewModels should use this instead of calling MessageBox directly.
    /// </summary>
    public interface IDialogService
    {
        void ShowInfo(string message, string title = "Info");
        void ShowWarning(string message, string title = "Warning");
        void ShowError(string message, string title = "Error");
        MessageBoxResult ShowConfirm(string message, string title = "Confirm");
        MessageBoxResult ShowYesNoCancel(string message, string title = "Confirm");
    }
}
