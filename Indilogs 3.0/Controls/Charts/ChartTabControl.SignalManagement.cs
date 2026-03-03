#nullable disable
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
                Data = signalData.Data,
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
        private SignalData GetSignalDataByName(string signalName)
        {
            if (_currentDataPackage == null) return null;
            return _currentDataPackage.Signals.FirstOrDefault(s =>
                s.Name.Equals(signalName, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadCsv(string filePath)
        {
            try
            {
                // Clear in-memory data flag - we're loading from CSV now
                _inMemoryDataLoaded = false;
                _currentDataPackage = null;

                _dataService.Load(filePath);

                _totalDataLength = _dataService.TotalRows - _dataService.DataStartRow;

                // Load time data for sync
                _timeData = _dataService.GetTimeColumnData(0);
                _syncService.BuildTimeMapping(_timeData);

                // ===== Categorize all columns from the CSV =====
                // The CSV exported by our app uses these column name patterns:
                //   Axis:    "Subsys-Motor-Param [ThreadName]"  where Param is SetP/ActP/SetV/ActV/Trq/LagErr
                //   IO:      "Subsys-Component - Symbol-Value [IOs-I]" or [IOs-Q]
                //   CHStep:  "Parent§CHName§SubsysID-Data-Param" or column name contains "CHStep"
                //   Thread:  "ThreadName_Message"
                //   Events:  "Events_Message"
                //   State:   "Machine_State"
                var signalColumns = new List<SignalData>();
                var threadMessageColumns = new Dictionary<string, int>(); // ThreadName -> column index
                int eventsColIndex = -1;
                int stateColIndex = -1;

                for (int col = 0; col < _dataService.ColumnNames.Count; col++)
                {
                    string colName = _dataService.ColumnNames[col];
                    string lower = colName.ToLower().TrimStart('\uFEFF'); // Strip BOM from first column

                    // Skip Time and Unix_Time columns
                    if (lower == "time" || lower == "unix_time")
                        continue;

                    // Machine State column
                    if (lower == "machine_state" || lower == "state")
                    {
                        stateColIndex = col;
                        continue;
                    }

                    // Events_Message column
                    if (lower == "events_message" || lower.Contains("events_message"))
                    {
                        eventsColIndex = col;
                        continue;
                    }

                    // Thread message columns: end with _Message (e.g., "Manager_Message", "IOs_Message")
                    // But NOT "Events_Message" (already handled above)
                    if (lower.EndsWith("_message") && !lower.Contains("events"))
                    {
                        string threadName = colName.Substring(0, colName.Length - "_Message".Length);
                        threadMessageColumns[threadName] = col;
                        continue;
                    }

                    // Determine signal category from column name
                    string category = "All";
                    if (lower.Contains("§") || lower.StartsWith("chstep"))
                    {
                        // CHStep columns are handled separately below
                        continue;
                    }
                    else if (lower.Contains("[ios-") || lower.Contains("[io_mon"))
                    {
                        category = "IO";
                    }
                    else if (lower.Contains("-setp") || lower.Contains("-actp") || lower.Contains("-setv") ||
                             lower.Contains("-actv") || lower.Contains("-trq") || lower.Contains("-lagerr"))
                    {
                        category = "Axis";
                    }
                    else if (lower.Contains("-value") || lower.Contains("-mottemp") || lower.Contains("-drvtemp"))
                    {
                        // IO signal with Value/MotTemp/DrvTemp param suffix
                        category = "IO";
                    }

                    signalColumns.Add(new SignalData
                    {
                        Name = colName,
                        Category = category
                    });
                }

                // Extract events
                if (eventsColIndex >= 0)
                {
                    var csvEvents = _dataService.ExtractEvents(eventsColIndex, 0);
                    _eventMarkers = csvEvents.Select(e => new EventMarkerData
                    {
                        TimeIndex = e.Index,
                        Name = e.Message,
                        TimeStamp = DateTime.MinValue
                    }).ToList();
                }
                else
                {
                    _eventMarkers = new List<EventMarkerData>();
                }

                // Extract machine states
                _globalStates.Clear();
                if (stateColIndex < 0) stateColIndex = _dataService.FindColumnIndex("state");
                if (stateColIndex < 0) stateColIndex = _dataService.FindColumnIndex("machine_state");
                if (stateColIndex >= 0)
                {
                    _globalStates = _dataService.ExtractStates(stateColIndex);
                }

                // Detect and extract CHSTEP columns
                // CSV format: Parent§CHName§SubsysID-Data-Param [thread]
                // Group by Parent§CHName§SubsysID prefix, use the -Data-State column for intervals
                _chStepStates = new List<StateData>();
                var chStepGroups = new Dictionary<string, Dictionary<string, int>>(); // prefix -> {param -> colIndex}

                for (int col = 0; col < _dataService.ColumnNames.Count; col++)
                {
                    string colName = _dataService.ColumnNames[col];
                    if (!colName.Contains("§")) continue;
                    if (col == stateColIndex) continue;

                    // Parse: "Parent§CHName§SubsysID-Data-Param [thread]"
                    // Remove thread part: " [thread]"
                    string nameWithoutThread = colName;
                    int bracketIdx = colName.IndexOf(" [");
                    if (bracketIdx > 0)
                        nameWithoutThread = colName.Substring(0, bracketIdx);

                    // Find last '-' to split prefix and param
                    int lastDash = nameWithoutThread.LastIndexOf('-');
                    if (lastDash <= 0) continue;

                    string prefix = nameWithoutThread.Substring(0, lastDash);  // "Parent§CHName§SubsysID-Data"
                    string param = nameWithoutThread.Substring(lastDash + 1);   // "State", "StepMessage", etc.

                    if (!chStepGroups.ContainsKey(prefix))
                        chStepGroups[prefix] = new Dictionary<string, int>();

                    chStepGroups[prefix][param] = col;
                }

                foreach (var kvp in chStepGroups)
                {
                    string prefix = kvp.Key;    // "Parent§CHName§SubsysID-Data"
                    var paramCols = kvp.Value;

                    // Extract CH name from prefix
                    // prefix format: "Parent§CHName§SubsysID-Data"
                    // Strip trailing "-Data" if present
                    string chPrefix = prefix;
                    if (chPrefix.EndsWith("-Data", StringComparison.OrdinalIgnoreCase))
                        chPrefix = chPrefix.Substring(0, chPrefix.Length - 5);

                    string chName = chPrefix;
                    string parentName = "";
                    if (chPrefix.Contains("§"))
                    {
                        var parts = chPrefix.Split('§');
                        parentName = parts[0];
                        if (parts.Length >= 2) chName = parts[1];
                    }

                    // Use the "State" column for numeric state intervals
                    if (paramCols.TryGetValue("State", out int stateCol))
                    {
                        var intervals = _dataService.ExtractStates(stateCol);
                        if (intervals.Count > 0)
                        {
                            // Try to enrich StateName and build rich tooltip from CHStep columns
                            paramCols.TryGetValue("StepMessage", out int msgCol);
                            paramCols.TryGetValue("Parent", out int parentCol);
                            paramCols.TryGetValue("SubsysID", out int subsysCol);
                            paramCols.TryGetValue("PrevStepNo", out int prevStepCol);
                            paramCols.TryGetValue("DiffTime", out int diffTimeCol);
                            paramCols.TryGetValue("SubStepNo", out int subStepCol);
                            paramCols.TryGetValue("CHObjType", out int objTypeCol);

                            for (int i = 0; i < intervals.Count; i++)
                            {
                                var interval = intervals[i];
                                int dataRow = _dataService.DataStartRow + interval.StartIndex;

                                string stepMsg = msgCol > 0 ? _dataService.GetStringAt(dataRow, msgCol) : null;
                                if (!string.IsNullOrWhiteSpace(stepMsg))
                                    interval.StateName = stepMsg;

                                // Build rich tooltip text
                                var sb = new System.Text.StringBuilder();
                                sb.AppendLine($"CHStep: {chName}");
                                if (!string.IsNullOrEmpty(stepMsg))
                                    sb.AppendLine($"Step: {stepMsg}");
                                sb.AppendLine($"State: {interval.StateId}");
                                if (!string.IsNullOrEmpty(parentName))
                                    sb.AppendLine($"Parent: {parentName}");

                                string subsysVal = subsysCol > 0 ? _dataService.GetStringAt(dataRow, subsysCol) : null;
                                if (!string.IsNullOrWhiteSpace(subsysVal))
                                    sb.AppendLine($"SubsysID: {subsysVal}");

                                string prevStep = prevStepCol > 0 ? _dataService.GetStringAt(dataRow, prevStepCol) : null;
                                if (!string.IsNullOrWhiteSpace(prevStep))
                                    sb.AppendLine($"PrevStepNo: {prevStep}");

                                string diffTime = diffTimeCol > 0 ? _dataService.GetStringAt(dataRow, diffTimeCol) : null;
                                if (!string.IsNullOrWhiteSpace(diffTime))
                                    sb.AppendLine($"DiffTime: {diffTime}");

                                string subStep = subStepCol > 0 ? _dataService.GetStringAt(dataRow, subStepCol) : null;
                                if (!string.IsNullOrWhiteSpace(subStep))
                                    sb.AppendLine($"SubStepNo: {subStep}");

                                string objType = objTypeCol > 0 ? _dataService.GetStringAt(dataRow, objTypeCol) : null;
                                if (!string.IsNullOrWhiteSpace(objType))
                                    sb.AppendLine($"CHObjType: {objType}");

                                interval.TooltipText = sb.ToString().TrimEnd();
                                intervals[i] = interval;
                            }

                            _chStepStates.Add(new StateData
                            {
                                Name = chName,
                                Category = parentName,
                                Intervals = intervals
                            });
                        }
                    }
                }

                // Extract thread messages from CSV
                var threadMessages = new List<ThreadMessageData>();
                foreach (var kvp in threadMessageColumns)
                {
                    string threadName = kvp.Key;
                    int col = kvp.Value;

                    for (int row = 0; row < _totalDataLength; row++)
                    {
                        string msg = _dataService.GetStringAt(_dataService.DataStartRow + row, col);
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            threadMessages.Add(new ThreadMessageData
                            {
                                TimeIndex = row,
                                ThreadName = threadName,
                                Message = msg,
                                TimeStamp = DateTime.MinValue
                            });
                        }
                    }
                }

                // Build a full data package for the signal list (always use SetDataPackage
                // so that ALL category buttons work: Axis, IO, CHStep, Thread, Events)
                var dataPackage = new ChartDataPackage
                {
                    Signals = signalColumns,
                    States = _chStepStates,
                    ThreadMessages = threadMessages,
                    Events = _eventMarkers,
                    TimeStamps = new List<DateTime>()
                };
                SignalList.SetDataPackage(dataPackage);

                // Store thread messages for later use
                _threadMessages = threadMessages;

                // Compute time gap regions from string timestamps
                _timeGapRegions = ComputeTimeGapRegionsFromStrings(_timeData);

                // Reset view
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;

                // Update timeline
                StateTimeline.SetStates(_globalStates, _totalDataLength);

                // Update empty state message
                EmptyStateMessage.Visibility = Visibility.Collapsed;

                // Ensure theme is current before creating charts
                SyncThemeFromSettings();

                // Auto-add first chart if none exist
                if (_charts.Count == 0)
                {
                    AddNewChart();
                }

                // Update slider
                NavSlider.Maximum = _totalDataLength > 0 ? _totalDataLength - 1 : 100;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading CSV: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            ChartViewModel chart = _selectedChart;
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
            int dataLen = hasOwnTimeline ? chart.GanttDataLength.Value : _totalDataLength;

            ganttView.HasOwnTimeline = hasOwnTimeline;
            ganttView.SetStates(chart.GanttStates, dataLen);
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
                    threadView.SetThreadData(chart.ThreadName, chart.ThreadMessages, _totalDataLength);
                }
            }
            else
            {
                threadView.SetThreadData(chart.ThreadName, chart.ThreadMessages, _totalDataLength);
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

        private void AddSignalToChart(string signalName)
        {
            if (!HasData)
            {
                AppLogger.Warn($"[Chart] AddSignalToChart('{signalName}'): HasData=false, inMem={_inMemoryDataLoaded}, csv={_dataService?.IsLoaded}");
                return;
            }

            // Handle [Events] signal name (from CSV signal list)
            if (signalName == "[Events]")
            {
                AddEventsToChart();
                return;
            }

            // Add to the selected chart, or the last Signal chart, or create one if none exist
            if (_charts.Count == 0)
            {
                AddNewChart();
            }

            var chart = _selectedChart;
            if (chart == null || chart.ViewType != ChartViewType.Signal)
            {
                chart = _charts.LastOrDefault(c => c.ViewType == ChartViewType.Signal);
            }
            if (chart == null)
            {
                AddNewChart();
                chart = _charts.Last();
            }

            // Check if already added
            if (chart.Series.Any(s => s.Name == signalName)) return;

            double[] data = null;

            // Try In-Memory data first
            if (_inMemoryDataLoaded && _currentDataPackage != null)
            {
                var signalData = GetSignalDataByName(signalName);
                if (signalData != null)
                {
                    data = signalData.Data;
                }
                else
                {
                    AppLogger.Warn($"[Chart] Signal '{signalName}' not found in package ({_currentDataPackage.Signals?.Count ?? 0} signals). Available: {string.Join(", ", _currentDataPackage.Signals?.Take(5).Select(s => s.Name) ?? Array.Empty<string>())}...");
                }
            }

            // Fall back to CSV data if not found in memory
            if (data == null && _dataService?.IsLoaded == true)
            {
                int colIndex = _dataService.ColumnNames.IndexOf(signalName);
                if (colIndex >= 0)
                {
                    data = _dataService.GetColumnData(colIndex);
                }
            }

            if (data == null) return;

            var series = new SignalSeries
            {
                Name = signalName,
                Data = data,
                Color = SignalColors[_colorIndex % SignalColors.Length],
                IsVisible = true,
                YAxisType = AxisType.Left
            };

            _colorIndex++;
            chart.Series.Add(series);

            // Refresh chart view
            RefreshChartViews();
        }

        private void RemoveSelectedChart()
        {
            if (_charts.Count > 0)
            {
                _charts.RemoveAt(_charts.Count - 1);
            }

            if (_charts.Count == 0)
            {
                EmptyStateMessage.Visibility = Visibility.Visible;
            }
        }

        #region Helpers

        private ChartGraphView FindGraphViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartGraphView>(container);
        }

        private ChartGanttView FindGanttViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartGanttView>(container);
        }

        private ChartThreadView FindThreadViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartThreadView>(container);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

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
