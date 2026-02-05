using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

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
            Toolbar.OnSpeedChanged += speed => _playbackSpeed = speed;
            Toolbar.OnAddChartRequested += AddNewChart;
            Toolbar.OnRemoveChartRequested += RemoveSelectedChart;
            Toolbar.OnShowStatesChanged += SetShowStates;
            Toolbar.OnZoomFitRequested += ZoomFit;

            // Wire up signal list events
            SignalList.OnSignalDoubleClicked += AddSignalToChart;

            // Wire up timeline events
            StateTimeline.OnTimelineClicked += OnTimelineClick;

            // Setup playback timer
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Tick += PlaybackTimer_Tick;
        }

        public bool HasData => _dataService?.IsLoaded == true;

        /// <summary>
        /// Sync cursor position from external source (log selection)
        /// </summary>
        public void SyncToTime(DateTime time)
        {
            if (!HasData) return;

            int index = _syncService.FindChartIndex(time);
            SetCursorPosition(index);
        }

        private void LoadCsv(string filePath)
        {
            try
            {
                _dataService.Load(filePath);

                // Update UI
                SignalList.SetSignals(_dataService.ColumnNames);
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
            // Find the GraphView for this chart
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return;

            var graphView = FindVisualChild<ChartGraphView>(container);
            if (graphView == null) return;

            graphView.SetViewModel(chart);
            graphView.GetXAxisLabel = GetXAxisLabel;
            graphView.SyncViewRange(_viewStartIndex, _viewEndIndex);
            graphView.SyncCursor(_cursorIndex);

            // Wire up events
            graphView.OnViewRangeChanged += (start, end) => SyncAllViewRanges(start, end);
            graphView.OnCursorMoved += (index) => SyncAllCursors(index);
            graphView.OnTimeClicked += OnChartTimeClickedHandler;
        }

        private void AddSignalToChart(string signalName)
        {
            if (!HasData) return;

            // Add to the last chart, or create one if none exist
            if (_charts.Count == 0)
            {
                AddNewChart();
            }

            var chart = _charts.Last();
            int colIndex = _dataService.ColumnNames.IndexOf(signalName);
            if (colIndex < 0) return;

            // Check if already added
            if (chart.Series.Any(s => s.Name == signalName)) return;

            // Load data
            double[] data = _dataService.GetColumnData(colIndex);

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

        private void SyncAllViewRanges(int start, int end)
        {
            _viewStartIndex = start;
            _viewEndIndex = end;

            foreach (var chart in _charts)
            {
                var graphView = FindGraphViewForChart(chart);
                graphView?.SyncViewRange(start, end);
            }

            StateTimeline.SyncViewRange(start, end);

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
                var graphView = FindGraphViewForChart(chart);
                graphView?.SyncCursor(index);
            }

            StateTimeline.SyncCursor(index);
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
        }

        private void OnTimelineClick(int index)
        {
            SetCursorPosition(index);
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

            _playbackTimer.Interval = TimeSpan.FromMilliseconds(50 / _playbackSpeed);
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
            if (_cursorIndex >= _viewEndIndex)
            {
                // Reached end of view
                PausePlayback();
                return;
            }

            _cursorIndex++;
            SyncAllCursors(_cursorIndex);
        }

        #endregion

        #region Helpers

        private ChartGraphView FindGraphViewForChart(ChartViewModel chart)
        {
            var container = ChartsContainer.ItemContainerGenerator.ContainerFromItem(chart) as FrameworkElement;
            if (container == null) return null;
            return FindVisualChild<ChartGraphView>(container);
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
