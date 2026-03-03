#nullable disable
using System;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl
    {
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
            var manageWindow = new ManageReferenceLinesWindow(chart.ReferenceLines, currentValue, currentIndex);
            manageWindow.Owner = Window.GetWindow(this);
            manageWindow.ShowDialog();

            // Refresh chart views after closing
            RefreshChartViews();
        }

        #endregion
    }
}
