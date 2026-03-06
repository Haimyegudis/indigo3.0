using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl
    {
        private void WireUpChartView(ChartViewModel chart)
        {
            // Route to appropriate wire-up method based on view type
            switch (chart.ViewType)
            {
                case ChartViewType.Gantt:
                    WireUpGanttView(chart);
                    return;
                case ChartViewType.Thread:
                    WireUpThreadView(chart);
                    return;
            }

            // Signal view - find the GraphView for this chart
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return;

            var graphView = FindVisualChild<ChartGraphView>(container);
            if (graphView == null) return;

            graphView.SetViewModel(chart);
            graphView.GetXAxisLabel = GetXAxisLabel;
            graphView.SyncViewRange(_viewStartIndex, _viewEndIndex);
            graphView.SyncCursor(_cursorIndex);
            graphView.IsLightTheme = _isLightTheme;

            // Pass time gap regions to graph view
            if (_timeGapRegions != null && _timeGapRegions.Count > 0)
                graphView.SetTimeGaps(_timeGapRegions);

            // Wire up events
            graphView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
            graphView.OnCursorMoved += (index) => SyncAllCursors(index);
            graphView.OnTimeClicked += OnChartTimeClickedHandler;
        }

        /// <summary>
        /// Wire up a Gantt view for synchronization
        /// </summary>
        private void WireUpGanttView(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return;

            var ganttView = FindVisualChild<ChartGanttView>(container);
            if (ganttView == null) return;

            // Use per-chart data length for independent-timeline Gantt charts (e.g. EM Statistics)
            bool hasOwnTimeline = chart.GanttDataLength.HasValue;
            int dataLen = hasOwnTimeline ? chart.GanttDataLength!.Value : _totalDataLength;

            ganttView.HasOwnTimeline = hasOwnTimeline;
            ganttView.SetStates(chart.GanttStates!, dataLen);
            if (chart.EventMarkers != null)
                ganttView.SetEventMarkers(chart.EventMarkers);
            ganttView.GetXAxisLabel = chart.GanttGetXAxisLabel ?? GetXAxisLabel;

            if (hasOwnTimeline)
                ganttView.SyncViewRange(0, dataLen - 1);
            else
                ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);

            ganttView.SyncCursor(_cursorIndex);
            ganttView.IsLightTheme = _isLightTheme;

            // Wire up events for synchronization — skip sync for independent-timeline charts
            if (!hasOwnTimeline)
            {
                ganttView.OnTimeClicked += OnChartTimeClickedHandler;
                ganttView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
                ganttView.OnCursorMoved += (index) => SyncAllCursors(index);
            }
        }

        /// <summary>
        /// Wire up a Thread view for synchronization
        /// </summary>
        private void WireUpThreadView(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return;

            var threadView = FindVisualChild<ChartThreadView>(container);
            if (threadView == null) return;

            // Set X-axis label function
            threadView.GetXAxisLabel = GetXAxisLabel;

            // Check if we have multiple thread groups or just one
            if (chart.ThreadMessages != null && chart.ThreadMessages.Count > 0)
            {
                // Group messages by thread name for multi-row display (INDICHARTSUIT style)
                var threadGroups = chart.ThreadMessages
                    .GroupBy(m => m.ThreadName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (threadGroups.Count > 1)
                {
                    // Multiple threads - use multi-row display
                    threadView.SetMultipleThreadData(threadGroups, _totalDataLength);
                }
                else
                {
                    // Single thread - use legacy method
                    threadView.SetThreadData(chart.ThreadName ?? "", chart.ThreadMessages, _totalDataLength);
                }
            }
            else
            {
                threadView.SetThreadData(chart.ThreadName ?? "", chart.ThreadMessages ?? new(), _totalDataLength);
            }

            if (chart.EventMarkers != null)
                threadView.SetEventMarkers(chart.EventMarkers);
            threadView.SyncViewRange(_viewStartIndex, _viewEndIndex);
            threadView.SyncCursor(_cursorIndex);
            threadView.IsLightTheme = _isLightTheme;

            // Wire up events
            threadView.OnTimeClicked += OnChartTimeClickedHandler;
            threadView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
            threadView.OnCursorMoved += (index) => SyncAllCursors(index);
        }

        #region Helpers

        private ChartGraphView? FindGraphViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartGraphView>(container);
        }

        private ChartGanttView? FindGanttViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartGanttView>(container);
        }

        private ChartThreadView? FindThreadViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartThreadView>(container);
        }

        private T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            if (parent is not System.Windows.Media.Visual && parent is not System.Windows.Media.Media3D.Visual3D) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        #endregion
    }
}
