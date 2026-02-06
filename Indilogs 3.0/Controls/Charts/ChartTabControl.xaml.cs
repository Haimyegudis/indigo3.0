using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;
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

            // Wire up signal list events
            SignalList.OnItemDoubleClicked += OnSignalItemDoubleClicked;
            SignalList.OnSignalDoubleClicked += AddSignalToChart; // Legacy for simple signal names

            // Wire up timeline events
            StateTimeline.OnTimelineClicked += OnTimelineClick;
            StateTimeline.OnStateClicked += OnStateClick;

            // Setup playback timer
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Tick += PlaybackTimer_Tick;

            // Subscribe to In-Memory data transfer events
            ChartDataTransferService.Instance.OnDataReady += OnInMemoryDataReady;
            ChartDataTransferService.Instance.OnLogTimeSelected += OnLogTimeSelected;

            // Detect theme on load
            Loaded += ChartTabControl_Loaded;
        }

        private void ChartTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Detect if app is in light theme by checking app settings
            try
            {
                _isLightTheme = !Properties.Settings.Default.IsDarkMode;
                ApplyThemeToCharts();
            }
            catch
            {
                // Default to dark theme
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
                LoadInMemoryData(dataPackage);
            }));
        }

        /// <summary>
        /// Loads data directly from memory without file I/O
        /// </summary>
        public void LoadInMemoryData(ChartDataPackage dataPackage)
        {
            if (dataPackage == null) return;

            try
            {
                _currentDataPackage = dataPackage;
                _inMemoryDataLoaded = true;

                // Use SetDataPackage for full support of CHSTEP and Thread items
                SignalList.SetDataPackage(dataPackage);

                // Set total data length
                _totalDataLength = dataPackage.TimeStamps.Count;
                if (_totalDataLength == 0 && dataPackage.Signals.Any())
                {
                    _totalDataLength = dataPackage.Signals.Max(s => s.Data?.Length ?? 0);
                }

                // Build time mapping for sync
                if (dataPackage.TimeStamps.Any())
                {
                    _timeData = dataPackage.TimeStamps.Select(t => t.ToString("yyyy-MM-dd HH:mm:ss.fff")).ToArray();
                    _syncService.BuildTimeMapping(_timeData);
                }

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

                // Clear existing charts - user will add signals manually
                _charts.Clear();

                // Add an empty chart ready for user to add signals
                AddNewChart();

                // Update slider
                NavSlider.Maximum = _totalDataLength > 0 ? _totalDataLength - 1 : 100;

                RefreshChartViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading In-Memory data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                _dataService.Load(filePath);

                // Build signal list - include Events if the column exists
                var signals = new List<string>(_dataService.ColumnNames);
                int eventsCol = _dataService.FindEventsColumnIndex();
                if (eventsCol >= 0)
                {
                    var csvEvents = _dataService.ExtractEvents(eventsCol, 0);
                    // Convert to EventMarkerData for unified handling
                    _eventMarkers = csvEvents.Select(e => new EventMarkerData
                    {
                        TimeIndex = e.Index,
                        Name = e.Message,
                        TimeStamp = DateTime.MinValue // Will use time string instead
                    }).ToList();

                    if (!signals.Contains("[Events]"))
                        signals.Add("[Events]");
                }
                else
                {
                    _eventMarkers = new List<EventMarkerData>();
                }

                // Update UI
                SignalList.SetSignals(signals);
                _totalDataLength = _dataService.TotalRows - _dataService.DataStartRow;

                // Load time data for sync
                _timeData = _dataService.GetTimeColumnData(0);
                _syncService.BuildTimeMapping(_timeData);

                // Detect state column and extract states
                int stateCol = _dataService.FindColumnIndex("state");
                if (stateCol < 0) stateCol = _dataService.FindColumnIndex("machine_state");
                if (stateCol >= 0)
                {
                    _globalStates = _dataService.ExtractStates(stateCol);
                }

                // Detect CHSTEP columns (columns named CHStep_*, CHSTEP_*, etc.)
                _chStepStates = new List<StateData>();
                for (int col = 0; col < _dataService.ColumnNames.Count; col++)
                {
                    string colName = _dataService.ColumnNames[col];
                    if (colName.IndexOf("CHStep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        colName.IndexOf("CHSTEP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Skip if this is the same as the main state column
                        if (col == stateCol) continue;

                        var intervals = _dataService.ExtractStates(col);
                        if (intervals.Count > 0)
                        {
                            _chStepStates.Add(new StateData
                            {
                                Name = colName,
                                Intervals = intervals
                            });
                        }
                    }
                }

                // If CHSTEP columns were found, update signal list with them
                if (_chStepStates.Count > 0)
                {
                    // Re-populate signal list using SetDataPackage-like approach
                    // by adding CHSTEP items
                    var dataPackage = new ChartDataPackage
                    {
                        Signals = signals.Where(s => s != "[Events]").Select(s => new SignalData { Name = s, Category = "All" }).ToList(),
                        States = _chStepStates,
                        ThreadMessages = new List<ThreadMessageData>(),
                        Events = _eventMarkers ?? new List<EventMarkerData>(),
                        TimeStamps = new List<DateTime>()
                    };
                    SignalList.SetDataPackage(dataPackage);
                }

                // Reset view
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;

                // Update timeline
                StateTimeline.SetStates(_globalStates, _totalDataLength);

                // Update empty state message
                EmptyStateMessage.Visibility = Visibility.Collapsed;

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
                // Check if this specific CHSTEP already exists in the merged view
                if (existingGanttChart.GanttStates != null &&
                    existingGanttChart.GanttStates.Any(s => s.Name == item.StateData.Name))
                    return;

                // Merge new CHSTEP into existing chart
                if (existingGanttChart.GanttStates == null)
                    existingGanttChart.GanttStates = new List<StateData>();

                existingGanttChart.GanttStates.Add(item.StateData);

                // Update title to show all CHSTEP names
                var chStepNames = existingGanttChart.GanttStates.Select(s => s.Name).ToList();
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
                    Title = $"GANTT: {item.StateData.Name}",
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
                    timeStr = e.TimeStamp.ToString("HH:mm:ss.fff");

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

            // Refresh all views (Signal, Gantt, and Thread)
            RefreshAllChartViews();
        }

        /// <summary>
        /// Refreshes all chart views (Signal, Gantt, Thread) with current data
        /// </summary>
        private void RefreshAllChartViews()
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
                            WireUpThreadView(chart);
                            if (chart.EventMarkers != null)
                                threadView.SetEventMarkers(chart.EventMarkers);
                        }
                        break;
                }
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
            if (!HasData)
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] No data loaded for signal: {signalName}");
                return;
            }

            // Handle [Events] signal name (from CSV signal list)
            if (signalName == "[Events]")
            {
                AddEventsToChart();
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Adding signal: {signalName}");

            // Add to the last chart, or create one if none exist
            if (_charts.Count == 0)
            {
                AddNewChart();
            }

            var chart = _charts.Last();

            // Check if already added
            if (chart.Series.Any(s => s.Name == signalName))
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Signal already exists: {signalName}");
                return;
            }

            double[] data = null;

            // Try In-Memory data first
            if (_inMemoryDataLoaded && _currentDataPackage != null)
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Searching in In-Memory data, package has {_currentDataPackage.Signals.Count} signals");
                var signalData = GetSignalDataByName(signalName);
                if (signalData != null)
                {
                    data = signalData.Data;
                    System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Found signal in package: {signalName}, data length: {data?.Length}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Signal NOT found in package: {signalName}");
                    // List all available signal names for debugging
                    System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Available signals: {string.Join(", ", _currentDataPackage.Signals.Select(s => s.Name).Take(20))}...");
                }
            }
            // Fall back to CSV data
            else if (_dataService?.IsLoaded == true)
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Searching in CSV data");
                int colIndex = _dataService.ColumnNames.IndexOf(signalName);
                if (colIndex >= 0)
                {
                    data = _dataService.GetColumnData(colIndex);
                    System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Found signal in CSV: {signalName}, column: {colIndex}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Signal NOT found in CSV: {signalName}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] No data source available");
            }

            if (data == null)
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] No data for signal: {signalName}");
                return;
            }

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
            System.Diagnostics.Debug.WriteLine($"[AddSignalToChart] Signal added successfully: {signalName}, chart now has {chart.Series.Count} series");

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
                var graphView = FindGraphViewForChart(chart);
                if (graphView != null)
                {
                    graphView.SetViewModel(chart);
                    graphView.SyncViewRange(_viewStartIndex, _viewEndIndex);
                    graphView.SyncCursor(_cursorIndex);
                }
            }
        }

        private string GetXAxisLabel(int index)
        {
            return _syncService.FormatTimeForDisplay(index);
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

            var chart = _charts.Last();

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

            var dialog = new AddReferenceLineWindow(currentValue, currentIndex);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultLine != null)
            {
                _referenceLineCounter++;
                var line = dialog.ResultLine;
                if (string.IsNullOrWhiteSpace(line.Name))
                {
                    line.Name = line.Type == ReferenceLineType.Horizontal
                        ? $"H{_referenceLineCounter}"
                        : $"V{_referenceLineCounter}";
                }

                chart.ReferenceLines.Add(line);
                RefreshChartViews();
            }
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
                catch { }
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
    }
}
