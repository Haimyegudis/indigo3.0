using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl
    {
        /// <summary>
        /// Sync cursor position from external source (log selection)
        /// </summary>
        public void SyncToTime(DateTime time)
        {
            if (!HasData) return;

            int index = _syncService.FindChartIndex(time);
            SetCursorPosition(index);
        }

        /// <summary>
        /// Handles log time selection for bidirectional sync
        /// </summary>
        private void OnLogTimeSelected(DateTime time)
        {
            if (!HasData) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncToTime(time);
            }));
        }

        private void SyncAllViewRanges(int start, int end)
        {
            _viewStartIndex = start;
            _viewEndIndex = end;

            foreach (var chart in _charts)
            {
                switch (chart.ViewType)
                {
                    case ChartViewType.Signal:
                        var graphView = FindGraphViewForChart(chart);
                        graphView?.SyncViewRange(start, end);
                        break;
                    case ChartViewType.Gantt:
                        var ganttView = FindGanttViewForChart(chart);
                        ganttView?.SyncViewRange(start, end);
                        break;
                    case ChartViewType.Thread:
                        var threadView = FindThreadViewForChart(chart);
                        threadView?.SyncViewRange(start, end);
                        break;
                }
            }

            StateTimeline.SyncViewRange(start, end);

            // Sync detached windows
            SyncDetachedWindows(viewStart: start, viewEnd: end);

            // Update slider position
            if (_totalDataLength > 0)
            {
                double center = (start + end) / 2.0;
                NavSlider.Value = center;
            }
        }

        private void SyncAllCursors(int index)
        {
            _cursorIndex = index;

            foreach (var chart in _charts)
            {
                switch (chart.ViewType)
                {
                    case ChartViewType.Signal:
                        var graphView = FindGraphViewForChart(chart);
                        graphView?.SyncCursor(index);
                        break;
                    case ChartViewType.Gantt:
                        var ganttView = FindGanttViewForChart(chart);
                        ganttView?.SyncCursor(index);
                        break;
                    case ChartViewType.Thread:
                        var threadView = FindThreadViewForChart(chart);
                        threadView?.SyncCursor(index);
                        break;
                }
            }

            StateTimeline.SyncCursor(index);

            // Sync detached windows
            SyncDetachedWindows(cursor: index);
        }

        private void SetCursorPosition(int index)
        {
            _cursorIndex = Math.Max(0, Math.Min(index, _totalDataLength - 1));
            SyncAllCursors(_cursorIndex);

            // Ensure cursor is in view
            if (_cursorIndex < _viewStartIndex || _cursorIndex > _viewEndIndex)
            {
                int viewSpan = _viewEndIndex - _viewStartIndex;
                int newStart = _cursorIndex - viewSpan / 2;
                int newEnd = newStart + viewSpan;

                if (newStart < 0) { newStart = 0; newEnd = viewSpan; }
                if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - viewSpan; }

                SyncAllViewRanges(newStart, newEnd);
            }
        }

        private void OnChartTimeClickedHandler(int index)
        {
            if (!HasData) return;

            DateTime time = _syncService.GetTimeForIndex(index);
            OnChartTimeClicked?.Invoke(time);

            // Also notify the transfer service for cross-component sync
            ChartDataTransferService.Instance.NotifyChartTimeSelected(time);
        }

        private void OnTimelineClick(int index)
        {
            SetCursorPosition(index);
        }

        private void OnStateClick(int startIndex, int endIndex)
        {
            // Zoom to show the state time window
            SyncAllViewRanges(startIndex, endIndex);
        }

        private void ZoomFit()
        {
            if (_totalDataLength > 0)
            {
                SyncAllViewRanges(0, _totalDataLength - 1);
            }
        }

        /// <summary>
        /// Refreshes all chart views (Signal, Gantt, Thread) with current data
        /// </summary>
        private void RefreshAllChartViews()
        {
            RefreshChartViews();
        }

        private void RefreshChartViews()
        {
            foreach (var chart in _charts)
            {
                switch (chart.ViewType)
                {
                    case ChartViewType.Signal:
                        var graphView = FindGraphViewForChart(chart);
                        if (graphView != null)
                        {
                            graphView.SetViewModel(chart);
                            graphView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                            graphView.SyncCursor(_cursorIndex);
                            if (_timeGapRegions != null && _timeGapRegions.Count > 0)
                                graphView.SetTimeGaps(_timeGapRegions);
                        }
                        else
                        {
                            // Chart not yet wired up - schedule wiring
                            var c = chart;
                            Dispatcher.BeginInvoke(new Action(() => WireUpChartView(c)), DispatcherPriority.Loaded);
                        }
                        break;
                    case ChartViewType.Gantt:
                        var ganttView = FindGanttViewForChart(chart);
                        if (ganttView != null)
                        {
                            bool hasOwn = chart.GanttDataLength.HasValue;
                            int gLen = hasOwn ? chart.GanttDataLength!.Value : _totalDataLength;
                            ganttView.SetStates(chart.GanttStates!, gLen);
                            if (chart.EventMarkers != null)
                                ganttView.SetEventMarkers(chart.EventMarkers);
                            // Independent-timeline charts keep their own view range
                            if (!hasOwn)
                            {
                                ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                                ganttView.SyncCursor(_cursorIndex);
                            }
                        }
                        break;
                    case ChartViewType.Thread:
                        var threadView = FindThreadViewForChart(chart);
                        if (threadView != null)
                        {
                            threadView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                            threadView.SyncCursor(_cursorIndex);
                        }
                        break;
                }
            }
        }

        private string GetXAxisLabel(int index)
        {
            return _syncService.FormatTimeForDisplay(index);
        }

        #region Detach Chart

        // Tracks detached chart windows: ChartViewModel -> Window
        private Dictionary<ChartViewModel, Window> _detachedWindows = new Dictionary<ChartViewModel, Window>();

        private void DetachChartButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChartViewModel chart)
            {
                DetachChart(chart);
            }
        }

        private void DetachChart(ChartViewModel chart)
        {
            if (chart == null || _detachedWindows.ContainsKey(chart)) return;

            // Create a new floating window
            var window = new Window
            {
                Title = chart.Title,
                Width = 800,
                Height = chart.ChartHeight + 80,
                MinWidth = 400,
                MinHeight = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = FindResource("BgDark") as System.Windows.Media.Brush,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.CanResize
            };

            // Apply the theme resources
            foreach (var key in new[] { "BgDark", "BgPanel", "BgCard", "TextPrimary", "TextSecondary", "BorderColor", "PrimaryColor", "BgCardHover" })
            {
                try
                {
                    var resource = FindResource(key);
                    if (resource != null)
                        window.Resources[key] = resource;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Copying resource '{key}' to floating window failed", ex);
                }
            }

            // Create the appropriate chart view for the floating window
            UIElement? chartContent = null;

            switch (chart.ViewType)
            {
                case ChartViewType.Signal:
                    var graphView = new ChartGraphView();
                    graphView.SetViewModel(chart);
                    graphView.GetXAxisLabel = GetXAxisLabel;
                    graphView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    graphView.SyncCursor(_cursorIndex);
                    graphView.IsLightTheme = _isLightTheme;
                    if (_timeGapRegions != null && _timeGapRegions.Count > 0)
                        graphView.SetTimeGaps(_timeGapRegions);
                    graphView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
                    graphView.OnCursorMoved += (index) => SyncAllCursors(index);
                    graphView.OnTimeClicked += OnChartTimeClickedHandler;
                    chartContent = graphView;
                    break;

                case ChartViewType.Gantt:
                    var ganttView = new ChartGanttView();
                    ganttView.SetStates(chart.GanttStates!, _totalDataLength);
                    if (chart.EventMarkers != null)
                        ganttView.SetEventMarkers(chart.EventMarkers);
                    ganttView.GetXAxisLabel = GetXAxisLabel;
                    ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    ganttView.SyncCursor(_cursorIndex);
                    ganttView.IsLightTheme = _isLightTheme;
                    ganttView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
                    ganttView.OnCursorMoved += (index) => SyncAllCursors(index);
                    ganttView.OnTimeClicked += OnChartTimeClickedHandler;
                    chartContent = ganttView;
                    break;

                case ChartViewType.Thread:
                    var threadView = new ChartThreadView();
                    threadView.GetXAxisLabel = GetXAxisLabel;
                    if (chart.ThreadMessages != null && chart.ThreadMessages.Count > 0)
                    {
                        var threadGroups = chart.ThreadMessages
                            .GroupBy(m => m.ThreadName)
                            .ToDictionary(g => g.Key, g => g.ToList());
                        if (threadGroups.Count > 1)
                            threadView.SetMultipleThreadData(threadGroups, _totalDataLength);
                        else
                            threadView.SetThreadData(chart.ThreadName ?? "", chart.ThreadMessages, _totalDataLength);
                    }
                    if (chart.EventMarkers != null)
                        threadView.SetEventMarkers(chart.EventMarkers);
                    threadView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    threadView.SyncCursor(_cursorIndex);
                    threadView.IsLightTheme = _isLightTheme;
                    threadView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
                    threadView.OnCursorMoved += (index) => SyncAllCursors(index);
                    threadView.OnTimeClicked += OnChartTimeClickedHandler;
                    chartContent = threadView;
                    break;
            }

            if (chartContent == null) return;

            // Wrap content in a border for a nice look
            var container = new Border
            {
                Background = FindResource("BgPanel") as System.Windows.Media.Brush,
                Child = chartContent
            };

            window.Content = container;

            // Hide the chart from the main charts list (keep it in the collection for sync)
            chart.IsDetached = true;

            // Track the window
            _detachedWindows[chart] = window;

            // When window closes, reattach the chart
            window.Closed += (s, args) =>
            {
                if (_detachedWindows.ContainsKey(chart))
                {
                    _detachedWindows.Remove(chart);
                    chart.IsDetached = false;

                    // Refresh the chart back in the main container
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        WireUpChartView(chart);
                    }), DispatcherPriority.Loaded);
                }
            };

            window.Show();
        }

        /// <summary>
        /// Syncs detached chart windows with current view range and cursor
        /// </summary>
        private void SyncDetachedWindows(int? viewStart = null, int? viewEnd = null, int? cursor = null)
        {
            foreach (var kvp in _detachedWindows)
            {
                var chart = kvp.Key;
                var window = kvp.Value;

                if (window.Content is Border border && border.Child is UIElement element)
                {
                    if (viewStart.HasValue && viewEnd.HasValue)
                    {
                        if (element is ChartGraphView gv) gv.SyncViewRange(viewStart.Value, viewEnd.Value);
                        else if (element is ChartGanttView gantV) gantV.SyncViewRange(viewStart.Value, viewEnd.Value);
                        else if (element is ChartThreadView tv) tv.SyncViewRange(viewStart.Value, viewEnd.Value);
                    }
                    if (cursor.HasValue)
                    {
                        if (element is ChartGraphView gv) gv.SyncCursor(cursor.Value);
                        else if (element is ChartGanttView gantV) gantV.SyncCursor(cursor.Value);
                        else if (element is ChartThreadView tv) tv.SyncCursor(cursor.Value);
                    }
                }
            }
        }

        #endregion
    }
}
