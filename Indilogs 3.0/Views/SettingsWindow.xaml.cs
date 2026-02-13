using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _isChildDialogOpen = false;

        public SettingsWindow()
        {
            InitializeComponent();
            Deactivated += SettingsWindow_Deactivated;
        }

        private void SettingsWindow_Deactivated(object sender, System.EventArgs e)
        {
            // Don't close if a child dialog (like Fonts) is open
            if (!_isChildDialogOpen)
                Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            _isChildDialogOpen = true;
            WindowManager.OpenWindow(new HelpWindow());
            Close();
        }

        private void OpenFonts_Click(object sender, RoutedEventArgs e)
        {
            _isChildDialogOpen = true;
            WindowManager.ShowDialog(new FontsWindow { DataContext = this.DataContext });
            _isChildDialogOpen = false;
        }
    }
}
