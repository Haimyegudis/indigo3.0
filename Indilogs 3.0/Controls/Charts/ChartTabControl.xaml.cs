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
