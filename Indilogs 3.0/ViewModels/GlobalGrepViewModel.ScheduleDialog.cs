#nullable disable
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        /// <summary>
        /// Opens the Schedule Editor dialog for the given schedule.
        /// Returns true if the user saved, false if cancelled.
        /// </summary>
        private bool ShowScheduleDialog(string title, ScheduledSearch schedule)
        {
            var vm = new ScheduleEditorViewModel(schedule, Locations.ToList(), BuildFullCriteria(), _dialogService);
            var window = new Views.ScheduleEditorWindow { DataContext = vm, Title = title };
            window.Owner = Application.Current.MainWindow;
            return window.ShowDialog() == true;
        }
    }
}
