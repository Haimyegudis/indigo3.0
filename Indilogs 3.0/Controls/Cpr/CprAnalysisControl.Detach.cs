using System;
using System.Windows;
using System.Windows.Media;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprAnalysisControl
    {
        #region Detach

        private void DetachChart_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm?.CurrentResult == null) return;

            var result = _vm.CurrentResult;
            var window = new Window
            {
                Title = "CPR - " + (result.Title ?? "Graph"),
                Width = 1000,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.CanResize
            };

            // Apply theme
            try
            {
                window.Background = (Brush)FindResource("BgDark");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Theme resource lookup failed: {ex.Message}");
                window.Background = System.Windows.Media.Brushes.Black;
            }

            var chartView = new CprChartView();
            window.Content = chartView;

            // Apply theme colors
            var bgColor = TryGetColor("BgDark");
            var gridColor = TryGetColor("BorderColor");
            var textColor = TryGetColor("TextPrimary");
            if (bgColor.HasValue)
                chartView.SetThemeColors(bgColor.Value, gridColor ?? Colors.Gray, textColor ?? Colors.White);

            // Set current graph data
            chartView.SetGraphResult(result);

            // Sync zoom from main chart to detached
            ChartView.ZoomChanged += (xMin, xMax, yMin, yMax) =>
            {
                if (window.IsLoaded)
                    chartView.SetZoomRange(xMin, xMax, yMin, yMax);
            };

            // Sync zoom from detached back to main
            chartView.ZoomChanged += (xMin, xMax, yMin, yMax) =>
            {
                if (!_isSyncingZoom)
                {
                    _isSyncingZoom = true;
                    try { ChartView.SetZoomRange(xMin, xMax, yMin, yMax); }
                    finally { _isSyncingZoom = false; }
                }
            };

            // Update detached chart when main graph updates
            void OnMainUpdated(CprGraphResult r) => chartView.SetGraphResult(r);
            _vm.GraphResultUpdated += OnMainUpdated;

            // Track and cleanup on close
            _detachedWindows.Add(window);
            window.Closed += (s, args) =>
            {
                _detachedWindows.Remove(window);
                if (_vm != null)
                    _vm.GraphResultUpdated -= OnMainUpdated;
            };

            window.Show();
        }

        private void DetachCompareChart_Click(object? sender, RoutedEventArgs e)
        {
            if (_compareVm?.CurrentResult == null) return;

            var result = _compareVm.CurrentResult;
            var window = new Window
            {
                Title = "CPR Compare - " + (result.Title ?? "Graph"),
                Width = 1000,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.CanResize
            };

            // Apply theme
            try
            {
                window.Background = (Brush)FindResource("BgDark");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Theme resource lookup failed: {ex.Message}");
                window.Background = System.Windows.Media.Brushes.Black;
            }

            var chartView = new CprChartView();
            window.Content = chartView;

            // Apply theme colors
            var bgColor = TryGetColor("BgDark");
            var gridColor = TryGetColor("BorderColor");
            var textColor = TryGetColor("TextPrimary");
            if (bgColor.HasValue)
                chartView.SetThemeColors(bgColor.Value, gridColor ?? Colors.Gray, textColor ?? Colors.White);

            // Set current graph data
            chartView.SetGraphResult(result);

            // Sync zoom from compare chart to detached
            CompareChartView.ZoomChanged += (xMin, xMax, yMin, yMax) =>
            {
                if (window.IsLoaded)
                    chartView.SetZoomRange(xMin, xMax, yMin, yMax);
            };

            // Sync zoom from detached back to compare
            chartView.ZoomChanged += (xMin, xMax, yMin, yMax) =>
            {
                if (!_isSyncingZoom)
                {
                    _isSyncingZoom = true;
                    try { CompareChartView.SetZoomRange(xMin, xMax, yMin, yMax); }
                    finally { _isSyncingZoom = false; }
                }
            };

            // Update detached chart when compare graph updates
            void OnCompareUpdated(CprGraphResult r) => chartView.SetGraphResult(r);
            _compareVm.GraphResultUpdated += OnCompareUpdated;

            // Track and cleanup on close
            _detachedWindows.Add(window);
            window.Closed += (s, args) =>
            {
                _detachedWindows.Remove(window);
                if (_compareVm != null)
                    _compareVm.GraphResultUpdated -= OnCompareUpdated;
            };

            window.Show();
        }

        /// <summary>
        /// Update theme on all detached windows
        /// </summary>
        private void UpdateDetachedThemes()
        {
            var bgColor = TryGetColor("BgDark");
            var gridColor = TryGetColor("BorderColor");
            var textColor = TryGetColor("TextPrimary");
            if (!bgColor.HasValue) return;

            foreach (var w in _detachedWindows)
            {
                try
                {
                    w.Background = (Brush)FindResource("BgDark");
                    if (w.Content is CprChartView cv)
                        cv.SetThemeColors(bgColor.Value, gridColor ?? Colors.Gray, textColor ?? Colors.White);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Updating detached window theme failed", ex);
                }
            }
        }

        #endregion
    }
}
