using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0.Controls
{
    public partial class AppLogsTabControl : UserControl
    {
        public DataGrid InnerDataGrid => AppLogsGrid;

        public AppLogsTabControl()
        {
            InitializeComponent();
        }

        private void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.AppLogsGrid_Sorting(sender, e);
        }

        private void AppLogsGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void AppLogsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_Loaded(sender, e);
        }

        private void AppLogsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var parent = Window.GetWindow(this) as MainWindow;
            parent?.DataGrid_LoadingRow(sender, e);
        }
    }
}
