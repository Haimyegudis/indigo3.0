#nullable disable
using IndiLogs_3._0.Services.Interfaces;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// WPF implementation of IDialogService using MessageBox.
    /// </summary>
    public class DialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "Info")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title = "Warning")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title = "Error")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public DialogResult ShowConfirm(string message, string title = "Confirm")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes ? DialogResult.Yes : DialogResult.No;
        }

        public DialogResult ShowYesNoCancel(string message, string title = "Confirm")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
            {
                MessageBoxResult.Yes => DialogResult.Yes,
                MessageBoxResult.No  => DialogResult.No,
                _                    => DialogResult.Cancel
            };
        }
    }
}
