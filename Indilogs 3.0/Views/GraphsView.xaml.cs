using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    public partial class GraphsView : UserControl
    {
        public GraphsView()
        {
            InitializeComponent();
        }

        private void Chart_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is SingleChartViewModel chart)
            {
                if (DataContext is GraphsViewModel vm) vm.SelectedChart = chart;
                e.Handled = true;
            }
        }

        private void Charts_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // אם CTRL לחוץ - תן לאירוע לעבור לגרף (כדי שנעשה זום)
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            // אחרת - בצע גלילה רגילה
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        // ✅ פונקציית העזר לניתוב האירוע (חשוב!)
        private void PlotView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            PlotView_MouseWheel(sender, e);
        }
        // ✅ לוגיקת הזום המרכזית
        private void PlotView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            e.Handled = true; // חובה! עוצר את OxyPlot

            if (DataContext is GraphsViewModel vm)
            {
                vm.PerformZoom(e.Delta);
            }
        }

        // שאר הפונקציות (ללא שינוי)
        private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta); e.Handled = true; }
        }
        private void ActiveSignals_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta); e.Handled = true; }
        }
        private void Timeline_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta); e.Handled = true; }
        }
        private void PlotView_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is OxyPlot.Wpf.PlotView plotView) plotView.Focus();
        }
        private void Charts_PreviewKeyDown(object sender, KeyEventArgs e) { }
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e) { }
        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e) { }
        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) { }
    }
}