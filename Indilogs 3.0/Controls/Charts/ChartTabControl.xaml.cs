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
        public event Action<DateTime>? OnChartTimeClicked;

        private ChartDataService _dataService;
        private ChartSyncService _syncService;
        private ObservableCollection<ChartViewModel> _charts = new ObservableCollection<ChartViewModel>();
        private List<StateInterval> _globalStates = new List<StateInterval>();
        private string[]? _timeData;

        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _totalDataLength = 0;
        private int _cursorIndex = 0;
        private bool _showStates = true;

        // Playback
        private DispatcherTimer _playbackTimer = null!;
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
        private bool _isGridLayout = false;
        private bool _isLightTheme = false;
        private ChartViewModel? _selectedChart = null;

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

        private void ChartTabControl_Loaded(object? sender, RoutedEventArgs e)
        {
            // Re-subscribe to events (may have been removed in Unloaded during tab switch)
            var svc = ChartDataTransferService.Instance;
            svc.OnDataReady -= OnInMemoryDataReady;
            svc.OnLogTimeSelected -= OnLogTimeSelected;
            svc.OnDataReady += OnInMemoryDataReady;
            svc.OnLogTimeSelected += OnLogTimeSelected;

            // If data was transferred while this tab was unloaded, pick it up now
            if (svc.CurrentData != null && svc.CurrentData != _currentDataPackage)
            {
                _ = LoadInMemoryData(svc.CurrentData);
            }

            SyncThemeFromSettings();
        }

        private void ChartTabControl_Unloaded(object? sender, RoutedEventArgs e)
        {
            _playbackTimer?.Stop();
            ChartDataTransferService.Instance.OnDataReady -= OnInMemoryDataReady;
            ChartDataTransferService.Instance.OnLogTimeSelected -= OnLogTimeSelected;
        }

        private void ChartTabControl_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Theme detection failed: {ex.Message}");
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
        private ChartDataPackage? _currentDataPackage;
        private List<ThreadMessageData> _threadMessages = new List<ThreadMessageData>();
        private List<StateData> _chStepStates = new List<StateData>();
        private List<EventMarkerData> _eventMarkers = new List<EventMarkerData>();
        private List<GapRegion> _timeGapRegions = new List<GapRegion>();

        // EM Statistics data (parsed once, displayed on demand via signal list double-click)
        private List<StateData>? _emStatisticsStates;
        private DateTime[]? _emTimestamps;
        private int _emTotalLength;

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

        private void RemoveChartButton_Click(object? sender, RoutedEventArgs e)
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

        private void ChartResizeThumb_DragDelta(object? sender, DragDeltaEventArgs e)
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
                            int gLen = hasOwn ? chart.GanttDataLength.Value : _totalDataLength;
                            ganttView.SetStates(chart.GanttStates, gLen);
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

        private void ChartBorder_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChartViewModel chart)
            {
                SelectChart(chart);
            }
        }

        #endregion
    }
}
