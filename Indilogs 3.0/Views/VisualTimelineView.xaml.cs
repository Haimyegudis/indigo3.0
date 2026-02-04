using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    public partial class VisualTimelineView : UserControl
    {
        public VisualTimelineView()
        {
            InitializeComponent();

            // Connect heatmap scroll event
            VisualHeatmapControl.RequestScrollToLog += OnHeatmapRequestScrollToLog;
        }

        private void OnHeatmapRequestScrollToLog(LogEntry log)
        {
            if (log == null) return;

            // Select and scroll to the log entry in the detail grid
            DetailLogGrid.SelectedItem = log;
            DetailLogGrid.ScrollIntoView(log);
            DetailLogGrid.Focus();
        }

        private void OnStateClicked(object sender, TimelineState e)
        {
            if (DataContext is VisualTimelineViewModel vm)
            {
                vm.SelectedState = e;
            }
        }

        private void OnMarkerClicked(object sender, TimelineMarker e)
        {
            if (DataContext is VisualTimelineViewModel vm)
            {
                // איוונט - אין לאן לקפוץ
                if (e.Type == TimelineMarkerType.Event) return;

                // שגיאה - מוצאים את הסטייט, בוחרים אותו, וגוללים אליו בטבלה למטה
                if (e.OriginalLog != null && vm.States != null)
                {
                    var parentState = vm.States.FirstOrDefault(s => s.RelatedLogs.Contains(e.OriginalLog));
                    if (parentState != null)
                    {
                        vm.SelectedState = parentState;

                        // גלילה בטבלה למטה
                        DetailLogGrid.UpdateLayout();
                        DetailLogGrid.ScrollIntoView(e.OriginalLog);
                        DetailLogGrid.SelectedItem = e.OriginalLog;
                    }
                }
            }
        }
    }
}