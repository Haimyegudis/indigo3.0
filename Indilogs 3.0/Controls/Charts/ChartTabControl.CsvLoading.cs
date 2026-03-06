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
                _chStepStates = new List<StateData>();
                var chStepGroups = new Dictionary<string, Dictionary<string, int>>(); // prefix -> {param -> colIndex}

                for (int col = 0; col < _dataService.ColumnNames.Count; col++)
                {
                    string colName = _dataService.ColumnNames[col];
                    if (!colName.Contains("§")) continue;
                    if (col == stateColIndex) continue;

                    // Parse: "Parent§CHName§SubsysID-Data-Param [thread]"
                    string nameWithoutThread = colName;
                    int bracketIdx = colName.IndexOf(" [");
                    if (bracketIdx > 0)
                        nameWithoutThread = colName.Substring(0, bracketIdx);

                    int lastDash = nameWithoutThread.LastIndexOf('-');
                    if (lastDash <= 0) continue;

                    string prefix = nameWithoutThread.Substring(0, lastDash);
                    string param = nameWithoutThread.Substring(lastDash + 1);

                    if (!chStepGroups.ContainsKey(prefix))
                        chStepGroups[prefix] = new Dictionary<string, int>();

                    chStepGroups[prefix][param] = col;
                }

                foreach (var kvp in chStepGroups)
                {
                    string prefix = kvp.Key;
                    var paramCols = kvp.Value;

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

                    if (paramCols.TryGetValue("State", out int stateCol))
                    {
                        var intervals = _dataService.ExtractStates(stateCol);
                        if (intervals.Count > 0)
                        {
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

                                string? stepMsg = msgCol > 0 ? _dataService.GetStringAt(dataRow, msgCol) : null;
                                if (!string.IsNullOrWhiteSpace(stepMsg))
                                    interval.StateName = stepMsg;

                                var sb = new System.Text.StringBuilder();
                                sb.AppendLine($"CHStep: {chName}");
                                if (!string.IsNullOrEmpty(stepMsg))
                                    sb.AppendLine($"Step: {stepMsg}");
                                sb.AppendLine($"State: {interval.StateId}");
                                if (!string.IsNullOrEmpty(parentName))
                                    sb.AppendLine($"Parent: {parentName}");

                                string? subsysVal = subsysCol > 0 ? _dataService.GetStringAt(dataRow, subsysCol) : null;
                                if (!string.IsNullOrWhiteSpace(subsysVal))
                                    sb.AppendLine($"SubsysID: {subsysVal}");

                                string? prevStep = prevStepCol > 0 ? _dataService.GetStringAt(dataRow, prevStepCol) : null;
                                if (!string.IsNullOrWhiteSpace(prevStep))
                                    sb.AppendLine($"PrevStepNo: {prevStep}");

                                string? diffTime = diffTimeCol > 0 ? _dataService.GetStringAt(dataRow, diffTimeCol) : null;
                                if (!string.IsNullOrWhiteSpace(diffTime))
                                    sb.AppendLine($"DiffTime: {diffTime}");

                                string? subStep = subStepCol > 0 ? _dataService.GetStringAt(dataRow, subStepCol) : null;
                                if (!string.IsNullOrWhiteSpace(subStep))
                                    sb.AppendLine($"SubStepNo: {subStep}");

                                string? objType = objTypeCol > 0 ? _dataService.GetStringAt(dataRow, objTypeCol) : null;
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

                // Build a full data package for the signal list
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

            double[]? data = null;

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
    }
}
