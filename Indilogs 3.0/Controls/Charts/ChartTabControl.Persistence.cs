using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl
    {
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
            if (mainWindow?.DataContext is MainViewModel mainVM)
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

            // Clear singleton reference so Loaded event doesn't reload stale data
            ChartDataTransferService.Instance.ClearCurrentData();

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

            // Force visual refresh so stale chart visuals are cleared
            UpdateChartsLayout();
        }

        #endregion
    }
}
