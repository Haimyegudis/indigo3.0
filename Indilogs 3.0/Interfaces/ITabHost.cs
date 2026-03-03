using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndiLogs_3._0.Interfaces
{
    /// <summary>
    /// Interface for windows that host tab content (MainWindow and DetachedTabWindow).
    /// Provides the methods that child controls need from their parent window.
    /// </summary>
    public interface ITabHost
    {
        void DataGrid_Loaded(object sender, RoutedEventArgs e);
        void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e);
        void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e);
        void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e);
    }
}
