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

        // ✅ גלילה על אזור הצ'ארטים
        private void Charts_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // אם CTRL לחוץ - אל תעשה כלום, תן ל-PlotView לטפל בזום
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return; // אל תסמן e.Handled, תן לאירוע להמשיך
            }

            // גלילה רגילה
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

        // ✅ טיפול בכניסת עכבר ל-PlotView
        private void PlotView_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is OxyPlot.Wpf.PlotView plotView)
            {
                plotView.Focus(); // תן פוקוס כדי שהזום יעבוד
                System.Diagnostics.Debug.WriteLine("🖱️ Mouse entered PlotView - Focus set");
            }
        }

        // ✅ זום מסונכרן - מופעל רק עם CTRL+גלילה
        // שימוש ב-MouseWheel (לא Preview) כדי לתפוס אחרי OxyPlot
        private void PlotView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🖱️ PlotView_MouseWheel - CTRL: {Keyboard.Modifiers.HasFlag(ModifierKeys.Control)}, Delta: {e.Delta}");

            // ✅ זום רק עם CTRL
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                System.Diagnostics.Debug.WriteLine("⏭️ No CTRL, skipping zoom");
                return; // תן ל-OxyPlot לטפל או לגלילה הרגילה לעבוד
            }

            e.Handled = true; // עצור את הגלילה והזום המובנה של OxyPlot
            System.Diagnostics.Debug.WriteLine("🔍 Starting zoom operation");

            if (sender is OxyPlot.Wpf.PlotView plotView && DataContext is GraphsViewModel vm)
            {
                var chart = plotView.DataContext as SingleChartViewModel;
                if (chart == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Chart is null");
                    return;
                }

                // סמן את הצ'ארט הנוכחי כפעיל
                vm.SelectedChart = chart;

                var xAxis = chart.PlotModel.Axes.FirstOrDefault(a => a is OxyPlot.Axes.DateTimeAxis);
                if (xAxis == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ X Axis is null");
                    return;
                }

                double currentMin = xAxis.Minimum;
                double currentMax = xAxis.Maximum;

                // אם אין ערכים, נשתמש ב-Absolute
                if (double.IsNaN(currentMin) || currentMin == 0) currentMin = xAxis.AbsoluteMinimum;
                if (double.IsNaN(currentMax) || currentMax == 0) currentMax = xAxis.AbsoluteMaximum;

                System.Diagnostics.Debug.WriteLine($"📊 Current range: {currentMin} to {currentMax}");

                double range = currentMax - currentMin;
                if (range <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Invalid range");
                    return;
                }

                // חישוב הזום
                double center = (currentMin + currentMax) / 2;
                double zoomFactor = e.Delta > 0 ? 0.85 : 1.15; // זום פנימה/החוצה
                double newRange = range * zoomFactor;
                double newMin = center - newRange / 2;
                double newMax = center + newRange / 2;

                // הגבלה לטווח המותר
                if (newMin < xAxis.AbsoluteMinimum) newMin = xAxis.AbsoluteMinimum;
                if (newMax > xAxis.AbsoluteMaximum) newMax = xAxis.AbsoluteMaximum;

                if (newMin >= newMax || newMin == 0 || newMax == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Invalid new range");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ New range: {newMin} to {newMax} (factor: {zoomFactor})");

                // ✅ עדכון כל הצ'ארטים בסנכרון
                int chartCount = 0;
                foreach (var c in vm.Charts)
                {
                    if (c?.PlotModel == null) continue;
                    
                    c.SetXAxisLimits(newMin, newMax);
                    vm.AutoZoomYAxis(c, newMin, newMax);
                    chartCount++;
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ Updated {chartCount} charts");

                // ✅ עדכון שדות הזמן בממשק
                try
                {
                    var startTime = OxyPlot.Axes.DateTimeAxis.ToDateTime(newMin);
                    var endTime = OxyPlot.Axes.DateTimeAxis.ToDateTime(newMax);

                    if (startTime.Year > 1900 && endTime.Year > 1900)
                    {
                        vm.FilterStartTime = startTime;
                        vm.FilterEndTime = endTime;
                        
                        System.Diagnostics.Debug.WriteLine($"✅ Filter times: {startTime:HH:mm:ss.fff} - {endTime:HH:mm:ss.fff}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error updating times: {ex.Message}");
                }
                
                // ✅ עדכון רקעי המצבים
                vm.PlotStateBackgrounds();
            }
        }

        // ✅ גם PreviewMouseWheel - לתפוס לפני OxyPlot
        private void PlotView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // קריאה לפונקציה המרכזית
            PlotView_MouseWheel(sender, e);
        }

        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {

        }
    }
}