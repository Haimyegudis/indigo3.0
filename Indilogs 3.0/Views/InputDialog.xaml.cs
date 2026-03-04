using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class InputDialog : Window
    {
        public string InputText => InputBox.Text;

        public InputDialog(string title, string prompt, string defaultValue)
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = prompt;
            InputBox.Text = defaultValue ?? "";
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
