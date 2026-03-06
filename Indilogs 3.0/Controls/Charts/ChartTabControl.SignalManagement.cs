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
        /// <summary>
        /// Creates an EM_Statistics Gantt chart from CSV content.
        /// The Gantt has its own independent time axis derived from the CSV timestamps.
        /// </summary>
        public void AddEmStatisticsGantt(string csvContent)
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return;

            try
            {
                var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(csvContent);
                if (states.Count == 0) return;

                // Remove existing EM Statistics chart if any
                var existing = _charts.FirstOrDefault(c => c.Title == "EM Statistics");
                if (existing != null) _charts.Remove(existing);

                // Build a custom X-axis label function using EM timestamps
                Func<int, string> emGetXAxisLabel = (index) =>
                {
                    if (index >= 0 && index < timestamps.Length)
                        return timestamps[index].ToString("HH:mm:ss");
                    return "";
                };

                var chart = new ChartViewModel
                {
                    Title = "EM Statistics",
                    ViewType = ChartViewType.Gantt,
                    GanttStates = states,
                    GanttDataLength = totalLength,
                    GanttGetXAxisLabel = emGetXAxisLabel,
                    ChartHeight = Math.Min(states.Count * 24 + 24, 474) // Capped: MAX_VISIBLE(450) + X_AXIS(20) + pad(4)
                };
                _charts.Add(chart);

                // Wire up after the item is rendered (uses WireUpGanttView which now respects per-chart values)
                var c = chart;
                Dispatcher.BeginInvoke(new Action(() => WireUpGanttView(c)),
                    System.Windows.Threading.DispatcherPriority.Loaded);

                EmptyStateMessage.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AppLogger.Error("AddEmStatisticsGantt failed", ex);
            }
        }

        /// <summary>
        /// Adds EM Statistics Gantt from pre-parsed stored data (triggered by signal list double-click).
        /// </summary>
        private void AddEmStatisticsFromStored()
        {
            if (_emStatisticsStates == null || _emStatisticsStates.Count == 0) return;

            // Check if already added
            var existing = _charts.FirstOrDefault(c => c.Title == "EM Statistics");
            if (existing != null) return;

            Func<int, string> emGetXAxisLabel = (index) =>
            {
                if (_emTimestamps != null && index >= 0 && index < _emTimestamps.Length)
                    return _emTimestamps[index].ToString("HH:mm:ss");
                return "";
            };

            var chart = new ChartViewModel
            {
                Title = "EM Statistics",
                ViewType = ChartViewType.Gantt,
                GanttStates = _emStatisticsStates,
                GanttDataLength = _emTotalLength,
                GanttGetXAxisLabel = emGetXAxisLabel,
                ChartHeight = Math.Min(_emStatisticsStates.Count * 24 + 24, 474) // Capped: MAX_VISIBLE(450) + X_AXIS(20) + pad(4)
            };
            _charts.Add(chart);

            var c = chart;
            Dispatcher.BeginInvoke(new Action(() => WireUpGanttView(c)),
                System.Windows.Threading.DispatcherPriority.Loaded);

            EmptyStateMessage.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Adds a signal from In-Memory data to a chart
        /// </summary>
        private void AddSignalToChartFromData(ChartViewModel chart, SignalData signalData)
        {
            if (chart == null || signalData == null) return;

            // Check if already added
            if (chart.Series.Any(s => s.Name == signalData.Name)) return;

            var series = new SignalSeries
            {
                Name = signalData.Name,
                Data = signalData.Data ?? Array.Empty<double>(),
                Color = SignalColors[_colorIndex % SignalColors.Length],
                IsVisible = true,
                YAxisType = AxisType.Left
            };

            _colorIndex++;
            chart.Series.Add(series);
        }

        /// <summary>
        /// Gets signal data by name from the current In-Memory package
        /// </summary>
        private SignalData? GetSignalDataByName(string signalName)
        {
            if (_currentDataPackage == null) return null;
            return _currentDataPackage.Signals.FirstOrDefault(s =>
                s.Name.Equals(signalName, StringComparison.OrdinalIgnoreCase));
        }

        private void AddNewChart()
        {
            var chart = new ChartViewModel
            {
                Title = $"Chart {_charts.Count + 1}",
                States = _showStates ? _globalStates : null
            };

            _charts.Add(chart);

            // Wire up chart view after it's added to visual tree
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WireUpChartView(chart);
            }), DispatcherPriority.Loaded);

            EmptyStateMessage.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handles double-click on signal list item - supports Signal, CHSTEP, and Thread types
        /// </summary>
        private void OnSignalItemDoubleClicked(SignalListItem item)
        {
            if (!HasData || item == null) return;

            switch (item.Category)
            {
                case SignalItemCategory.CHStep:
                    // Add Gantt chart for this specific CHSTEP
                    AddGanttForCHStep(item);
                    break;

                case SignalItemCategory.Thread:
                    // Add Thread marker view for this specific thread
                    AddThreadMarkerView(item);
                    break;

                case SignalItemCategory.Events:
                    // Add event markers to the current chart
                    AddEventsToChart();
                    break;

                case SignalItemCategory.EmStats:
                    // Add EM Statistics Gantt from pre-parsed data
                    AddEmStatisticsFromStored();
                    break;

                default:
                    // Regular signal - add to chart
                    AddSignalToChart(item.FullName);
                    break;
            }
        }

        /// <summary>
        /// Adds a CHSTEP to the Gantt chart view. Multiple CHSTEPs are merged into one chart
        /// with stacked rows (like Thread merging pattern).
        /// </summary>
        private void AddGanttForCHStep(SignalListItem item)
        {
            if (item.StateData == null) return;

            // Find existing Gantt chart (we merge all CHSTEPs into one view)
            var existingGanttChart = _charts.FirstOrDefault(c => c.ViewType == ChartViewType.Gantt);

            if (existingGanttChart != null)
            {
                // Check if this specific CHSTEP already exists in the merged view (compare both Name and Category/Parent)
                if (existingGanttChart.GanttStates != null &&
                    existingGanttChart.GanttStates.Any(s => s.Name == item.StateData.Name && s.Category == item.StateData.Category))
                    return;

                // Merge new CHSTEP into existing chart
                if (existingGanttChart.GanttStates == null)
                    existingGanttChart.GanttStates = new List<StateData>();

                existingGanttChart.GanttStates.Add(item.StateData);

                // Update title to show all CHSTEP names (with parent prefix)
                var chStepNames = existingGanttChart.GanttStates
                    .Select(s => !string.IsNullOrEmpty(s.Category) ? $"{s.Category}>{s.Name}" : s.Name).ToList();
                existingGanttChart.Title = $"GANTT: {string.Join(", ", chStepNames)}";

                // Update chart height based on number of CHSTEPs
                existingGanttChart.ChartHeight = Math.Max(120, chStepNames.Count * 28 + 30);

                // Re-wire up the Gantt view with merged data
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireUpGanttView(existingGanttChart);
                }), DispatcherPriority.Loaded);
            }
            else
            {
                // Create a new Gantt chart view model
                var chart = new ChartViewModel
                {
                    Title = $"GANTT: {(!string.IsNullOrEmpty(item.StateData.Category) ? $"{item.StateData.Category}>{item.StateData.Name}" : item.StateData.Name)}",
                    ViewType = ChartViewType.Gantt,
                    GanttStates = new List<StateData> { item.StateData },
                    ChartHeight = 120
                };

                _charts.Add(chart);

                // Wire up the Gantt view after it's added
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireUpGanttView(chart);
                }), DispatcherPriority.Loaded);
            }

            EmptyStateMessage.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Adds a Thread view for a specific thread - displayed in the chart area with hover tooltips.
        /// Multiple threads can be added to the same chart (INDICHARTSUIT style - stacked rows)
        /// </summary>
        private void AddThreadMarkerView(SignalListItem item)
        {
            if (item.ThreadMessages == null || item.ThreadMessages.Count == 0) return;

            // Find existing Thread view chart (we merge all threads into one view)
            var existingThreadChart = _charts.FirstOrDefault(c => c.ViewType == ChartViewType.Thread);

            if (existingThreadChart != null)
            {
                // Check if this specific thread already exists in the merged view
                if (existingThreadChart.ThreadMessages != null &&
                    existingThreadChart.ThreadMessages.Any(m => m.ThreadName == item.ThreadName))
                    return;

                // Merge new thread messages into existing chart
                if (existingThreadChart.ThreadMessages == null)
                    existingThreadChart.ThreadMessages = new List<ThreadMessageData>();

                existingThreadChart.ThreadMessages.AddRange(item.ThreadMessages);

                // Update title to show all thread names
                var threadNames = existingThreadChart.ThreadMessages
                    .Select(m => m.ThreadName)
                    .Distinct()
                    .ToList();
                int totalMsgs = existingThreadChart.ThreadMessages.Count;
                existingThreadChart.Title = $"THREADS: {string.Join(", ", threadNames)} ({totalMsgs} msgs)";

                // Update chart height based on number of threads
                existingThreadChart.ChartHeight = Math.Max(80, threadNames.Count * 28 + 30);

                // Re-wire up the Thread view with merged data
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireUpThreadView(existingThreadChart);
                }), DispatcherPriority.Loaded);
            }
            else
            {
                // Create a new Thread chart view model
                var chart = new ChartViewModel
                {
                    Title = $"THREAD: {item.ThreadName} ({item.ThreadMessages.Count} msgs)",
                    ViewType = ChartViewType.Thread,
                    ThreadName = item.ThreadName,
                    ThreadMessages = new List<ThreadMessageData>(item.ThreadMessages),
                    ChartHeight = 80
                };

                _charts.Add(chart);

                // Wire up the Thread view after it's added
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireUpThreadView(chart);
                }), DispatcherPriority.Loaded);
            }

            EmptyStateMessage.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Adds event markers (red dots) to the selected or last chart panel.
        /// Events can be added to any chart type: Signal, Gantt, or Thread.
        /// </summary>
        private void AddEventsToChart()
        {
            if (_eventMarkers == null || _eventMarkers.Count == 0) return;

            // Add to the selected chart, or last chart, or create one if none exist
            ChartViewModel? chart = _selectedChart;
            if (chart == null)
            {
                if (_charts.Count == 0)
                    AddNewChart();
                chart = _charts.Last();
            }

            // Check if already added
            if (chart.EventMarkers != null && chart.EventMarkers.Count > 0) return;

            // Convert EventMarkerData to EventMarker
            var markers = _eventMarkers.Select(e =>
            {
                // Use time string from _timeData when TimeStamp is MinValue (CSV mode)
                string timeStr;
                if (e.TimeStamp == DateTime.MinValue && _timeData != null && e.TimeIndex >= 0 && e.TimeIndex < _timeData.Length)
                    timeStr = _timeData[e.TimeIndex];
                else
                    timeStr = e.TimeStamp.ToString("HH:mm:ss.ffffff");

                return new EventMarker
                {
                    Index = e.TimeIndex,
                    Name = e.Name,
                    Message = !string.IsNullOrEmpty(e.Name) ? e.Name : "Event",
                    Time = timeStr,
                    Severity = e.Severity,
                    Description = e.Description
                };
            }).ToList();

            chart.EventMarkers = markers;

            // Refresh only the target chart (not all charts)
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
                    break;
                case ChartViewType.Gantt:
                    var ganttView = FindGanttViewForChart(chart);
                    if (ganttView != null)
                    {
                        ganttView.SetEventMarkers(markers);
                        ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    }
                    break;
                case ChartViewType.Thread:
                    var threadView = FindThreadViewForChart(chart);
                    if (threadView != null)
                    {
                        threadView.SetEventMarkers(markers);
                        threadView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    }
                    break;
            }
        }

    }
}
