using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs.Tests
{
    /// <summary>
    /// Test double for IDialogService. All methods return safe defaults
    /// so ViewModel tests don't need real UI dialogs.
    /// </summary>
    public class TestDialogService : IDialogService
    {
        public DialogResult ConfirmResult { get; set; } = DialogResult.Yes;
        public DialogResult YesNoCancelResult { get; set; } = DialogResult.Yes;

        public int InfoCallCount { get; private set; }
        public int WarningCallCount { get; private set; }
        public int ErrorCallCount { get; private set; }
        public int ConfirmCallCount { get; private set; }
        public string? LastMessage { get; private set; }

        public void ShowInfo(string message, string title = "Info")
        {
            LastMessage = message;
            InfoCallCount++;
        }

        public void ShowWarning(string message, string title = "Warning")
        {
            LastMessage = message;
            WarningCallCount++;
        }

        public void ShowError(string message, string title = "Error")
        {
            LastMessage = message;
            ErrorCallCount++;
        }

        public DialogResult ShowConfirm(string message, string title = "Confirm")
        {
            LastMessage = message;
            ConfirmCallCount++;
            return ConfirmResult;
        }

        public DialogResult ShowYesNoCancel(string message, string title = "Confirm")
        {
            LastMessage = message;
            return YesNoCancelResult;
        }
    }
}
