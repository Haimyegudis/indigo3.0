using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Views;
using System.Windows.Markup;
using System.Windows.Media;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl : UserControl
    {
        // Events for log synchronization
        public event Action<DateTime> OnChartTimeClicked;

        private ChartDataService _dataService;
        private ChartSyncService _syncService;
        private ObservableCollection<ChartViewModel> _charts = new ObservableCollection<ChartViewModel>();
        private List<StateInterval> _globalStates = new List<StateInterval>();
        private string[] _timeData;

        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _totalDataLength = 0;
        private int _cursorIndex = 0;
        private bool _showStates = true;

        // Playback
        private DispatcherTimer _playbackTimer;
        private bool _isPlaying = false;
        private double _playbackSpeed = 1.0;

        // Color palette for signals
        private static readonly SKColor[] SignalColors = new[]
        {
            SKColor.Parse("#3B82F6"), // Blue
            SKColor.Parse("#EF4444"), // Red
            SKColor.Parse("#10B981"), // Green
            SKColor.Parse("#F59E0B"), // Orange
            SKColor.Parse("#8B5CF6"), // Purple
            SKColor.Parse("#EC4899"), // Pink
            SKColor.Parse("#06B6D4"), // Cyan
            SKColor.Parse("#84CC16"), // Lime
            SKColor.Parse("#F97316"), // Orange-red
            SKColor.Parse("#6366F1"), // Indigo
        };
        private int _colorIndex = 0;
        private bool _isSignalPanelVisible = true;
        private int _referenceLineCounter = 0;
        private bool _isGridLayout = false;
        private bool _isLightTheme = false;
        private ChartViewModel _selectedChart = null;

        public ChartTabControl()
        {
            InitializeComponent();

            // Read theme from settings immediately so charts created before Loaded event use correct theme
            try { _isLightTheme = !Properties.Settings.Default.IsDarkMode; }
            catch (Exception ex) { AppLogger.Error("Reading IsDarkMode setting failed", ex); }

            _dataService = new ChartDataService();
            _syncService = new ChartSyncService();

            ChartsContainer.ItemsSource = _charts;

            // Wire up toolbar events
            Toolbar.OnLoadCsvRequested += LoadCsv;
            Toolbar.OnPlayRequested += TogglePlayback;
            Toolbar.OnStopRequested += StopPlayback;
            Toolbar.OnSpeedChanged += speed =>
            {
                _playbackSpeed = speed;
                // Update running timer with new speed
                if (_isPlaying)
                {
                    _playbackTimer.Stop();
                    _playbackTimer.Interval = TimeSpan.FromMilliseconds(50 / _playbackSpeed);
                    _playbackTimer.Start();
                }
            };
            Toolbar.OnAddChartRequested += AddNewChart;
            Toolbar.OnRemoveChartRequested += RemoveSelectedChart;
            Toolbar.OnShowStatesChanged += SetShowStates;
            Toolbar.OnZoomFitRequested += ZoomFit;
            Toolbar.OnAddReferenceLineRequested += OpenAddReferenceLineDialog;
            Toolbar.OnTogglePanelRequested += ToggleSignalPanel;
            Toolbar.OnLayoutChanged += SetLayoutMode;
            Toolbar.OnSmoothChanged += SetSmoothingEnabled;
            Toolbar.OnSmoothWindowChanged += SetSmoothingWindowSize;
            Toolbar.OnExportCsvRequested += OnExportCsvRequested;

            // Wire up signal list events
            SignalList.OnItemDoubleClicked += OnSignalItemDoubleClicked;

            // Wire up timeline events
            StateTimeline.OnTimelineClicked += OnTimelineClick;
            StateTimeline.OnStateClicked += OnStateClick;

            // Setup playback timer
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Tick += PlaybackTimer_Tick;

            // Subscribe to In-Memory data transfer events
            ChartDataTransferService.Instance.OnDataReady += OnInMemoryDataReady;
            ChartDataTransferService.Instance.OnLogTimeSelected += OnLogTimeSelected;

            // Detect theme on load and when tab becomes visible
            Loaded += ChartTabControl_Loaded;
            Unloaded += ChartTabControl_Unloaded;
            IsVisibleChanged += ChartTabControl_IsVisibleChanged;
        }

        private void ChartTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            SyncThemeFromSettings();
        }

        private void ChartTabControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _playbackTimer?.Stop();
            ChartDataTransferService.Instance.OnDataReady -= OnInMemoryDataReady;
            ChartDataTransferService.Instance.OnLogTimeSelected -= OnLogTimeSelected;
        }

        private void ChartTabControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Re-sync theme every time the Charts tab becomes visible
            if (e.NewValue is bool isVisible && isVisible)
            {
                SyncThemeFromSettings();
            }
        }

        private void SyncThemeFromSettings()
        {
            try
            {
                bool isLight = !Properties.Settings.Default.IsDarkMode;
                if (isLight != _isLightTheme)
                {
                    _isLightTheme = isLight;
                    ApplyThemeToCharts();
                }
            }
            catch
            {
                _isLightTheme = false;
            }
        }

        /// <summary>
        /// Sets the theme for all chart views
        /// </summary>
        public void SetLightTheme(bool isLight)
        {
            _isLightTheme = isLight;
            ApplyThemeToCharts();
        }

        private void ApplyThemeToCharts()
        {
            foreach (var chart in _charts)
            {
                switch (chart.ViewType)
                {
                    case ChartViewType.Signal:
                        var graphView = FindGraphViewForChart(chart);
                        if (graphView != null)
                            graphView.IsLightTheme = _isLightTheme;
                        break;
                    case ChartViewType.Gantt:
                        var ganttView = FindGanttViewForChart(chart);
                        if (ganttView != null)
                            ganttView.IsLightTheme = _isLightTheme;
                        break;
                    case ChartViewType.Thread:
                        var threadView = FindThreadViewForChart(chart);
                        if (threadView != null)
                            threadView.IsLightTheme = _isLightTheme;
                        break;
                }
            }
        }

        public bool HasData => _dataService?.IsLoaded == true || _inMemoryDataLoaded;

        private bool _inMemoryDataLoaded = false;
        private ChartDataPackage _currentDataPackage;
        private List<ThreadMessageData> _threadMessages = new List<ThreadMessageData>();
        private List<StateData> _chStepStates = new List<StateData>();
        private List<EventMarkerData> _eventMarkers = new List<EventMarkerData>();
        private List<GapRegion> _timeGapRegions = new List<GapRegion>();

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

        /// <summary>
        /// Handles In-Memory data transfer from ExportConfigurationWindow
        /// </summary>
        private void OnInMemoryDataReady(ChartDataPackage dataPackage)
        {
            if (dataPackage == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = LoadInMemoryData(dataPackage);
            }));
        }

        /// <summary>
        /// Loads data directly from memory without file I/O.
        /// Heavy work runs on background threads; UI thread only does final assignments.
        /// </summary>
        public async Task LoadInMemoryData(ChartDataPackage dataPackage)
        {
            if (dataPackage == null) return;

            // Show loading overlay immediately
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Loading chart data...";
            LoadingDetail.Text = $"Processing {dataPackage.Signals?.Count ?? 0} signals";

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _currentDataPackage = dataPackage;
                _inMemoryDataLoaded = true;

                // Set total data length
                _totalDataLength = dataPackage.TimeStamps.Count;
                if (_totalDataLength == 0 && dataPackage.Signals.Any())
                {
                    _totalDataLength = dataPackage.Signals.Max(s => s.Data?.Length ?? 0);
                }

                LoadingDetail.Text = $"Building time index for {_totalDataLength:N0} points...";

                // ── Run ALL heavy work on background threads in parallel ──
                var stamps = dataPackage.TimeStamps;
                string[] timeArr = null;
                List<GapRegion> gapRegions = null;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    // 1) Build time mapping directly from DateTimes — no string parsing needed!
                    //    Old code: format→string→TryParse→DateTime (wasteful round-trip for 40K+ items)
                    _syncService.BuildTimeMappingFromDateTimes(stamps);

                    // 2) Format timestamps to strings (for display only) — parallel
                    if (stamps.Count > 0)
                    {
                        timeArr = new string[stamps.Count];
                        System.Threading.Tasks.Parallel.For(0, stamps.Count,
                            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                            i => { timeArr[i] = stamps[i].ToString("yyyy-MM-dd HH:mm:ss.ffffff"); });
                    }

                    // 3) Compute time gap regions (suppressed for IO terminal data)
                    gapRegions = dataPackage.SuppressGapDetection
                        ? new List<GapRegion>()
                        : ComputeTimeGapRegions(stamps);
                });

                _timeData = timeArr;
                _timeGapRegions = gapRegions ?? new List<GapRegion>();

                var bgMs = sw.ElapsedMilliseconds;
                AppLogger.Info($"[ChartLoad] Background work done in {bgMs}ms (time mapping + format + gaps)");

                LoadingDetail.Text = "Populating signal list...";

                // ── UI-thread work — kept minimal ──

                // Populate signal list (with virtualization, ~1900 items is fast)
                SignalList.SetDataPackage(dataPackage);

                // Extract global states from state data (MachineState for timeline)
                _globalStates.Clear();
                var machineState = dataPackage.States.FirstOrDefault(s =>
                    s.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase));

                if (machineState != null)
                {
                    _globalStates.AddRange(machineState.Intervals);
                }

                // Reset view
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;

                // Update timeline with machine states only
                StateTimeline.SetStates(_globalStates, _totalDataLength);

                // Store thread messages (NOT displayed automatically - user selects from list)
                _threadMessages = dataPackage.ThreadMessages ?? new List<ThreadMessageData>();

                // Store CHSTEP states (NOT displayed automatically - user selects from list)
                _chStepStates = dataPackage.States
                    .Where(s => !s.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Store event markers for display on charts
                _eventMarkers = dataPackage.Events ?? new List<EventMarkerData>();

                // Update empty state message
                EmptyStateMessage.Visibility = Visibility.Collapsed;

                // Clear existing charts
                _charts.Clear();

                // Ensure theme is current before creating charts
                SyncThemeFromSettings();

                // Add an empty chart — user selects signals manually from the list
                AddNewChart();

                // Update slider
                NavSlider.Maximum = _totalDataLength > 0 ? _totalDataLength - 1 : 100;

                RefreshChartViews();

                sw.Stop();
                AppLogger.Info($"[ChartLoad] Total LoadInMemoryData: {sw.ElapsedMilliseconds}ms (bg={bgMs}ms, UI={sw.ElapsedMilliseconds - bgMs}ms)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading In-Memory data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
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
        /// Refreshes all chart views (Signal, Gantt, Thread) with current data
        /// </summary>
        private void RefreshAllChartViews()
        {
            RefreshChartViews();
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

            ganttView.SetStates(chart.GanttStates, _totalDataLength);
            if (chart.EventMarkers != null)
                ganttView.SetEventMarkers(chart.EventMarkers);
            ganttView.GetXAxisLabel = GetXAxisLabel;
            ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);
            ganttView.SyncCursor(_cursorIndex);
            ganttView.IsLightTheme = _isLightTheme;

            // Wire up events for full synchronization (zoom, cursor, range)
            ganttView.OnTimeClicked += OnChartTimeClickedHandler;
            ganttView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
            ganttView.OnCursorMoved += (index) => SyncAllCursors(index);
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
            if (!HasData) return;

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

        private void RemoveChartButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChartViewModel chart)
            {
                _charts.Remove(chart);

                if (_charts.Count == 0)
                {
                    EmptyStateMessage.Visibility = Visibility.Visible;
                }
            }
        }

        private void ChartResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.Tag is ChartViewModel chart)
            {
                double newHeight = chart.ChartHeight + e.VerticalChange;
                chart.ChartHeight = Math.Max(100, Math.Min(600, newHeight));
            }
        }

        private void SetShowStates(bool show)
        {
            _showStates = show;
            foreach (var chart in _charts)
            {
                chart.States = show ? _globalStates : null;
            }
            RefreshChartViews();
        }

        private int _smoothWindowSize = 10;

        private void SetSmoothingEnabled(bool enabled)
        {
            foreach (var chart in _charts)
            {
                foreach (var series in chart.Series)
                {
                    if (enabled && series.SmoothedData == null)
                        series.CalculateSmoothing(_smoothWindowSize);
                    series.IsSmoothed = enabled;
                }
            }
        }

        private void SetSmoothingWindowSize(int windowSize)
        {
            _smoothWindowSize = windowSize;
            foreach (var chart in _charts)
            {
                foreach (var series in chart.Series)
                {
                    series.SmoothedData = null; // force recalculation
                    series.CalculateSmoothing(windowSize);
                    // IsSmoothed is already true (slider only fires when checked)
                }
            }
            RefreshChartViews();
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
                            ganttView.SetStates(chart.GanttStates, _totalDataLength);
                            if (chart.EventMarkers != null)
                                ganttView.SetEventMarkers(chart.EventMarkers);
                            ganttView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                            ganttView.SyncCursor(_cursorIndex);
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

        /// <summary>
        /// Computes time gap regions from timestamps (gaps >= 2 seconds)
        /// </summary>
        private List<GapRegion> ComputeTimeGapRegions(List<DateTime> timestamps)
        {
            var regions = new List<GapRegion>();
            if (timestamps == null || timestamps.Count < 2) return regions;

            const double threshold = 2.0; // seconds

            for (int i = 1; i < timestamps.Count; i++)
            {
                var diff = timestamps[i] - timestamps[i - 1];
                if (diff.TotalSeconds >= threshold)
                {
                    regions.Add(new GapRegion
                    {
                        StartIndex = i - 1,
                        EndIndex = i,
                        Duration = FormatGapDuration(diff),
                        StartTime = timestamps[i - 1].ToString("HH:mm:ss.ffffff"),
                        EndTime = timestamps[i].ToString("HH:mm:ss.ffffff")
                    });
                }
            }
            return regions;
        }

        private string FormatGapDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1)
                return $"{ts.TotalMinutes:F1}m";
            return $"{ts.TotalSeconds:F1}s";
        }

        /// <summary>
        /// Computes time gap regions from string timestamps (for CSV loaded data)
        /// </summary>
        private static readonly string[] _gapTimeFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.ffffff", "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss",
            "HH:mm:ss.ffffff", "HH:mm:ss.fff", "HH:mm:ss"
        };

        private List<GapRegion> ComputeTimeGapRegionsFromStrings(string[] timeData)
        {
            var regions = new List<GapRegion>();
            if (timeData == null || timeData.Length < 2) return regions;

            const double threshold = 2.0;

            DateTime prevTime = DateTime.MinValue;
            for (int i = 0; i < timeData.Length; i++)
            {
                // Use TryParseExact with known formats - much faster than TryParse
                if (DateTime.TryParseExact(timeData[i], _gapTimeFormats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime currentTime))
                {
                    if (prevTime != DateTime.MinValue)
                    {
                        var diff = currentTime - prevTime;
                        if (diff.TotalSeconds >= threshold)
                        {
                            regions.Add(new GapRegion
                            {
                                StartIndex = i - 1,
                                EndIndex = i,
                                Duration = FormatGapDuration(diff),
                                StartTime = prevTime.ToString("HH:mm:ss.ffffff"),
                                EndTime = currentTime.ToString("HH:mm:ss.ffffff")
                            });
                        }
                    }
                    prevTime = currentTime;
                }
            }
            return regions;
        }

        #region Navigation

        private void NavSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_totalDataLength == 0) return;

            int center = (int)e.NewValue;
            int viewSpan = _viewEndIndex - _viewStartIndex;

            int newStart = center - viewSpan / 2;
            int newEnd = newStart + viewSpan;

            if (newStart < 0) { newStart = 0; newEnd = viewSpan; }
            if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - viewSpan; }

            if (newStart != _viewStartIndex || newEnd != _viewEndIndex)
            {
                SyncAllViewRanges(newStart, newEnd);
            }
        }

        private void NavLeftButton_Click(object sender, RoutedEventArgs e)
        {
            int viewSpan = _viewEndIndex - _viewStartIndex;
            int shift = Math.Max(100, viewSpan / 10);

            int newStart = Math.Max(0, _viewStartIndex - shift);
            int newEnd = newStart + viewSpan;

            SyncAllViewRanges(newStart, newEnd);
        }

        private void NavRightButton_Click(object sender, RoutedEventArgs e)
        {
            int viewSpan = _viewEndIndex - _viewStartIndex;
            int shift = Math.Max(100, viewSpan / 10);

            int newEnd = Math.Min(_totalDataLength - 1, _viewEndIndex + shift);
            int newStart = newEnd - viewSpan;

            SyncAllViewRanges(newStart, newEnd);
        }

        #endregion

        #region Playback

        private void TogglePlayback()
        {
            if (_isPlaying)
            {
                PausePlayback();
            }
            else
            {
                StartPlayback();
            }
        }

        private void StartPlayback()
        {
            if (!HasData || _totalDataLength == 0)
            {
                MessageBox.Show("Please load CSV data first.", "Playback", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isPlaying = true;
            Toolbar.IsPlaying = true;

            // Set progressive mode on all chart views
            foreach (var chart in _charts)
            {
                var graphView = FindGraphViewForChart(chart);
                if (graphView != null)
                {
                    graphView.IsProgressiveMode = true;
                }
            }

            // Start from current view start if cursor is before it
            if (_cursorIndex < _viewStartIndex)
            {
                _cursorIndex = _viewStartIndex;
            }

            // Use consistent timer interval, speed is handled by step size in PlaybackTimer_Tick
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 FPS
            _playbackTimer.Start();
        }

        private void PausePlayback()
        {
            _isPlaying = false;
            Toolbar.IsPlaying = false;
            _playbackTimer.Stop();
        }

        private void StopPlayback()
        {
            PausePlayback();

            // Disable progressive mode
            foreach (var chart in _charts)
            {
                var graphView = FindGraphViewForChart(chart);
                if (graphView != null)
                {
                    graphView.IsProgressiveMode = false;
                }
            }

            // Reset cursor to view start
            _cursorIndex = _viewStartIndex;
            SyncAllCursors(_cursorIndex);
        }

        private void PlaybackTimer_Tick(object sender, EventArgs e)
        {
            if (!HasData || _totalDataLength == 0)
            {
                PausePlayback();
                return;
            }

            if (_cursorIndex >= _viewEndIndex)
            {
                // Reached end of view
                PausePlayback();
                return;
            }

            // Move cursor based on speed - faster speeds move more points per tick
            int stepSize = Math.Max(1, (int)(_playbackSpeed));
            _cursorIndex = Math.Min(_cursorIndex + stepSize, _viewEndIndex);

            // Force update all chart views explicitly
            SyncAllCursors(_cursorIndex);
        }

        #endregion

        #region Reference Lines

        private void OpenAddReferenceLineDialog()
        {
            if (_charts.Count == 0 || !HasData) return;

            // Use the selected chart, or find the last Signal chart, or fall back to last chart
            var chart = _selectedChart;
            if (chart == null || chart.ViewType != ChartViewType.Signal)
            {
                chart = _charts.LastOrDefault(c => c.ViewType == ChartViewType.Signal);
            }
            if (chart == null) chart = _charts.Last();

            // Get current cursor value and index from the graph view
            double currentValue = 0;
            int currentIndex = _cursorIndex;

            var graphView = FindGraphViewForChart(chart);
            if (graphView != null)
            {
                currentValue = graphView.GetCurrentCursorValue();
                currentIndex = graphView.GetCurrentCursorIndex();
            }
            else if (chart.Series.Count > 0)
            {
                var firstVisible = chart.Series.FirstOrDefault(s => s.IsVisible && s.Data != null);
                if (firstVisible != null && _cursorIndex >= 0 && _cursorIndex < firstVisible.Data.Length)
                {
                    currentValue = firstVisible.Data[_cursorIndex];
                    if (double.IsNaN(currentValue)) currentValue = 0;
                }
            }

            // Open the management window (allows add, view, edit, delete)
            var manageWindow = new Views.ManageReferenceLinesWindow(chart.ReferenceLines, currentValue, currentIndex);
            manageWindow.Owner = Window.GetWindow(this);
            manageWindow.ShowDialog();

            // Refresh chart views after closing
            RefreshChartViews();
        }

        #endregion

        #region Layout Mode

        private void SetLayoutMode(bool isGrid)
        {
            _isGridLayout = isGrid;
            UpdateChartsLayout();
        }

        private void UpdateChartsLayout()
        {
            if (_isGridLayout)
            {
                // Grid layout: 2 columns
                ChartsContainer.ItemsPanel = CreateGridItemsPanelTemplate();
            }
            else
            {
                // Stack layout: vertical list
                ChartsContainer.ItemsPanel = CreateStackItemsPanelTemplate();
            }

            // Re-apply theme after layout change (charts get recreated)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyThemeToCharts();
                // Re-wire up chart views after layout change
                foreach (var chart in _charts)
                {
                    WireUpChartView(chart);
                }
            }), DispatcherPriority.Loaded);
        }

        private ItemsPanelTemplate CreateGridItemsPanelTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(UniformGrid));
            factory.SetValue(UniformGrid.ColumnsProperty, 2);
            return new ItemsPanelTemplate(factory);
        }

        private ItemsPanelTemplate CreateStackItemsPanelTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            return new ItemsPanelTemplate(factory);
        }

        #endregion

        #region Export CSV

        private void OnExportCsvRequested()
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow?.DataContext is ViewModels.MainViewModel mainVM)
            {
                mainVM.ExportParsedDataCommand?.Execute(null);
            }
        }

        #endregion

        #region Panel Toggle

        private void ToggleSignalPanel(bool isVisible)
        {
            _isSignalPanelVisible = isVisible;

            if (_isSignalPanelVisible)
            {
                SignalListColumn.Width = new GridLength(220);
                SplitterColumn.Width = GridLength.Auto;
            }
            else
            {
                SignalListColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
            }
        }

        private void ToggleAxisButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SignalSeries series)
            {
                series.YAxisType = series.YAxisType == AxisType.Left ? AxisType.Right : AxisType.Left;
                RefreshChartViews();
            }
        }

        private void LegendItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;

            if (sender is FrameworkElement element && element.Tag is SignalSeries series)
            {
                // Find which chart contains this series
                foreach (var chart in _charts)
                {
                    if (chart.Series.Contains(series))
                    {
                        chart.Series.Remove(series);
                        RefreshChartViews();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        #endregion

        #region Detach Chart

        // Tracks detached chart windows: ChartViewModel -> Window
        private Dictionary<ChartViewModel, Window> _detachedWindows = new Dictionary<ChartViewModel, Window>();

        private void DetachChartButton_Click(object sender, RoutedEventArgs e)
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
            UIElement chartContent = null;

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
                    ganttView.SetStates(chart.GanttStates, _totalDataLength);
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
                            threadView.SetThreadData(chart.ThreadName, chart.ThreadMessages, _totalDataLength);
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

        #region Chart Selection

        private void SelectChart(ChartViewModel chart)
        {
            // Deselect previous
            if (_selectedChart != null)
            {
                _selectedChart.IsSelected = false;
            }

            // Select new
            _selectedChart = chart;
            if (_selectedChart != null)
            {
                _selectedChart.IsSelected = true;
            }
        }

        private void ChartBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChartViewModel chart)
            {
                SelectChart(chart);
            }
        }

        #endregion

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

        #region Per-Session State Persistence

        /// <summary>
        /// Captures the current chart state for session persistence.
        /// Returns null if no data is loaded (nothing to save).
        /// </summary>
        public SessionChartState SaveChartState()
        {
            if (!HasData && _charts.Count == 0) return null;

            // Stop playback before saving
            if (_isPlaying) PausePlayback();

            // Close detached windows before saving
            foreach (var kvp in _detachedWindows.ToList())
            {
                kvp.Key.IsDetached = false;
                kvp.Value.Close();
            }
            _detachedWindows.Clear();

            return new SessionChartState
            {
                DataPackage = _currentDataPackage,
                TimeData = _timeData,
                GlobalStates = _globalStates,
                ThreadMessages = _threadMessages,
                ChStepStates = _chStepStates,
                EventMarkers = _eventMarkers,
                TimeGapRegions = _timeGapRegions,
                TotalDataLength = _totalDataLength,
                InMemoryDataLoaded = _inMemoryDataLoaded,
                DataService = _dataService,
                SyncService = _syncService,
                Charts = _charts.ToList(),
                ViewStartIndex = _viewStartIndex,
                ViewEndIndex = _viewEndIndex,
                CursorIndex = _cursorIndex,
                ShowStates = _showStates,
                IsGridLayout = _isGridLayout,
                IsSignalPanelVisible = _isSignalPanelVisible,
                SmoothWindowSize = _smoothWindowSize,
                ColorIndex = _colorIndex,
                SelectedChartIndex = _selectedChart != null ? _charts.IndexOf(_selectedChart) : -1
            };
        }

        /// <summary>
        /// Restores a previously saved chart state.
        /// Returns true if state was restored, false if state was null (empty/new session).
        /// </summary>
        public bool RestoreChartState(SessionChartState state)
        {
            if (state == null)
            {
                ClearChartState();
                return false;
            }

            // Stop any running playback
            if (_isPlaying) PausePlayback();

            // Close any detached windows from previous view
            foreach (var kvp in _detachedWindows.ToList())
            {
                kvp.Key.IsDetached = false;
                kvp.Value.Close();
            }
            _detachedWindows.Clear();

            // Restore data state (all references, no copying)
            _currentDataPackage = state.DataPackage;
            _timeData = state.TimeData;
            _globalStates = state.GlobalStates ?? new List<StateInterval>();
            _threadMessages = state.ThreadMessages ?? new List<ThreadMessageData>();
            _chStepStates = state.ChStepStates ?? new List<StateData>();
            _eventMarkers = state.EventMarkers ?? new List<EventMarkerData>();
            _timeGapRegions = state.TimeGapRegions ?? new List<GapRegion>();
            _totalDataLength = state.TotalDataLength;
            _inMemoryDataLoaded = state.InMemoryDataLoaded;

            // Restore services
            _dataService = state.DataService ?? new ChartDataService();
            _syncService = state.SyncService ?? new ChartSyncService();

            // Restore navigation
            _viewStartIndex = state.ViewStartIndex;
            _viewEndIndex = state.ViewEndIndex;
            _cursorIndex = state.CursorIndex;

            // Restore UI preferences
            _showStates = state.ShowStates;
            _isGridLayout = state.IsGridLayout;
            _isSignalPanelVisible = state.IsSignalPanelVisible;
            _smoothWindowSize = state.SmoothWindowSize;
            _colorIndex = state.ColorIndex;

            // Restore signal list
            if (_currentDataPackage != null)
                SignalList.SetDataPackage(_currentDataPackage);
            else
                SignalList.SetDataPackage(null);

            // Restore chart panels
            _charts.Clear();
            foreach (var chart in state.Charts ?? new List<ChartViewModel>())
                _charts.Add(chart);

            // Restore selected chart
            _selectedChart = null;
            if (state.SelectedChartIndex >= 0 && state.SelectedChartIndex < _charts.Count)
            {
                _selectedChart = _charts[state.SelectedChartIndex];
                _selectedChart.IsSelected = true;
            }

            // Update state timeline
            StateTimeline.SetStates(_globalStates, _totalDataLength);

            // Update slider
            NavSlider.Maximum = _totalDataLength > 0 ? _totalDataLength - 1 : 100;

            // Update empty state
            EmptyStateMessage.Visibility = _charts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            // Signal panel visibility
            ToggleSignalPanel(_isSignalPanelVisible);

            // Layout mode
            UpdateChartsLayout();

            // Re-wire chart views after visual tree builds
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncThemeFromSettings();
                foreach (var chart in _charts)
                    WireUpChartView(chart);
                if (_totalDataLength > 0)
                {
                    SyncAllViewRanges(_viewStartIndex, _viewEndIndex);
                    SyncAllCursors(_cursorIndex);
                }
            }), DispatcherPriority.Loaded);

            return true;
        }

        /// <summary>
        /// Resets the chart tab to empty state (no data loaded).
        /// </summary>
        public void ClearChartState()
        {
            // Stop playback
            if (_isPlaying) PausePlayback();

            // Close detached windows
            foreach (var kvp in _detachedWindows.ToList())
            {
                kvp.Key.IsDetached = false;
                kvp.Value.Close();
            }
            _detachedWindows.Clear();

            // Clear data
            _currentDataPackage = null;
            _timeData = null;
            _globalStates = new List<StateInterval>();
            _threadMessages = new List<ThreadMessageData>();
            _chStepStates = new List<StateData>();
            _eventMarkers = new List<EventMarkerData>();
            _timeGapRegions = new List<GapRegion>();
            _totalDataLength = 0;
            _inMemoryDataLoaded = false;
            _colorIndex = 0;

            // Reset services
            _dataService = new ChartDataService();
            _syncService = new ChartSyncService();

            // Clear charts
            _charts.Clear();
            _selectedChart = null;

            // Reset view
            _viewStartIndex = 0;
            _viewEndIndex = 0;
            _cursorIndex = 0;

            // Reset UI
            EmptyStateMessage.Visibility = Visibility.Visible;
            NavSlider.Maximum = 100;
            StateTimeline.SetStates(new List<StateInterval>(), 0);
            SignalList.SetDataPackage(null);
        }

        #endregion
    }
}
