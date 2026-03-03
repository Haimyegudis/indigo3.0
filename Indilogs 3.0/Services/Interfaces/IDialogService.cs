namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Framework-agnostic result from confirmation dialogs.
    /// Replaces WPF-specific MessageBoxResult so ViewModels stay UI-independent.
    /// </summary>
    public enum DialogResult
    {
        OK,
        Yes,
        No,
        Cancel
    }

    /// <summary>
    /// Abstraction over MessageBox.Show for testability and MVVM compliance.
    /// ViewModels should use this instead of calling MessageBox directly.
    /// </summary>
    public interface IDialogService
    {
        void ShowInfo(string message, string title = "Info");
        void ShowWarning(string message, string title = "Warning");
        void ShowError(string message, string title = "Error");
        DialogResult ShowConfirm(string message, string title = "Confirm");
        DialogResult ShowYesNoCancel(string message, string title = "Confirm");
    }
}
