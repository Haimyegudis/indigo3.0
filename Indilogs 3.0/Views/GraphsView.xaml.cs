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

        // ✅ בחירת צ'ארט בלחיצה
        private void Chart_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is SingleChartViewModel chart)
            {
                if (DataContext is GraphsViewModel vm)
                {
                    vm.SelectedChart = chart;
                }
                e.Handled = true;
            }
        }

        private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ActiveSignals_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void Timeline_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        // ✅ תיקון: גלילה על הצ'ארטים - בדיקה אם הגלילה היא על PlotView
        private void Charts_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // אם העכבר מעל PlotView - בלוק את הגלילה ותן ל-PlotView לטפל בזום
            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element is OxyPlot.Wpf.PlotView)
                {
                    // ✅ חשוב: בלוק את הגלילה של ScrollViewer
                    e.Handled = true;
                    // PlotView יטפל בזום דרך PlotView_PreviewMouseWheel
                    return;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }

            // אחרת - גלול את ה-ScrollViewer
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
        private void Charts_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle keyboard shortcuts if needed
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            // Handle tree expansion if needed
        }

        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
            // Handle tree collapse if needed
        }

        // ✅ חדש: זום מסונכרן על כל הצ'ארטים
        private void PlotView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; // מנע גלילה של העמוד

            if (sender is OxyPlot.Wpf.PlotView plotView && DataContext is GraphsViewModel vm)
            {
                var chart = plotView.DataContext as SingleChartViewModel;
                if (chart == null) return;

                vm.SelectedChart = chart;

                var xAxis = chart.PlotModel.Axes.FirstOrDefault(a => a is OxyPlot.Axes.DateTimeAxis);
                if (xAxis != null)
                {
                    double currentMin = xAxis.Minimum;
                    double currentMax = xAxis.Maximum;

                    if (double.IsNaN(currentMin) || currentMin == 0) currentMin = xAxis.AbsoluteMinimum;
                    if (double.IsNaN(currentMax) || currentMax == 0) currentMax = xAxis.AbsoluteMaximum;

                    double range = currentMax - currentMin;
                    if (range <= 0) return;

                    double center = (currentMin + currentMax) / 2;
                    double zoomFactor = e.Delta > 0 ? 0.85 : 1.15;
                    double newRange = range * zoomFactor;
                    double newMin = center - newRange / 2;
                    double newMax = center + newRange / 2;

                    if (newMin < xAxis.AbsoluteMinimum) newMin = xAxis.AbsoluteMinimum;
                    if (newMax > xAxis.AbsoluteMaximum) newMax = xAxis.AbsoluteMaximum;

                    if (newMin >= newMax || newMin == 0 || newMax == 0) return;

                    // ✅ עדכון כל הצ'ארטים בסנכרון
                    foreach (var c in vm.Charts)
                    {
                        if (c == null) continue;
                        c.SetXAxisLimits(newMin, newMax);
                        vm.AutoZoomYAxis(c, newMin, newMax);
                    }

                    try
                    {
                        var startTime = OxyPlot.Axes.DateTimeAxis.ToDateTime(newMin);
                        var endTime = OxyPlot.Axes.DateTimeAxis.ToDateTime(newMax);

                        if (startTime.Year > 1900 && endTime.Year > 1900)
                        {
                            vm.FilterStartTime = startTime;
                            vm.FilterEndTime = endTime;
                        }
                    }
                    catch { }
                }
            }
        }
    }
}