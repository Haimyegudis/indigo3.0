using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            WindowManager.OpenWindow(new HelpWindow());
            Close();
        }

        private void OpenFonts_Click(object sender, RoutedEventArgs e)
        {
            WindowManager.ShowDialog(new FontsWindow { DataContext = this.DataContext });
        }
    }
}
