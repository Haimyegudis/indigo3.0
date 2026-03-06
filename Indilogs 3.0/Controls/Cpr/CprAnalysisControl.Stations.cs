using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprAnalysisControl : UserControl
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

        #region Station Pairs UI

        private void BuildStationPairsUI()
        {
            var panel = new StackPanel();

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = $"{(char)('A' + i * 2)}{(char)('B' + i * 2)}:",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 0);

                var testCombo = CreateStationCombo(1, 6);
                testCombo.SelectedIndex = i; // default: test = i+1
                testCombo.SelectionChanged += (s, ev) =>
                {
                    if (testCombo.SelectedItem is int val && _vm != null)
                    {
                        _vm.StationTestSelections[idx] = val;
                        _vm.OnStationPairChanged();
                    }
                };
                Grid.SetColumn(testCombo, 1);
                _testCombos[i] = testCombo;

                var separator = new TextBlock
                {
                    Text = "/",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 3, 0)
                };
                Grid.SetColumn(separator, 2);

                var refCombo = CreateStationCombo(0, 6);
                refCombo.SelectedIndex = 0; // default: ref = 0
                refCombo.SelectionChanged += (s, ev) =>
                {
                    if (refCombo.SelectedItem is int val && _vm != null)
                    {
                        _vm.StationRefSelections[idx] = val;
                        _vm.OnStationPairChanged();
                    }
                };
                Grid.SetColumn(refCombo, 3);
                _refCombos[i] = refCombo;

                row.Children.Add(label);
                row.Children.Add(testCombo);
                row.Children.Add(separator);
                row.Children.Add(refCombo);

                panel.Children.Add(row);
            }

            StationPairsControl.Items.Clear();
            StationPairsControl.Items.Add(panel);
        }

        private ComboBox CreateStationCombo(int from, int to)
        {
            var combo = new ComboBox
            {
                FontSize = 11,
                Height = 22,
                Background = (Brush)FindResource("BgCard"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor")
            };

            for (int v = from; v <= to; v++)
                combo.Items.Add(v);

            return combo;
        }

        #endregion

        #region Set Ref Station

        private void RefStationCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm != null && RefStationCombo.SelectedItem is int val)
                _vm.RefStationValue = val;
        }

        private void SetRefStation_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            int refVal = 0;
            if (RefStationCombo.SelectedItem is int val)
                refVal = val;

            // Set all ref stations to the selected value - batch update without triggering
            // individual auto-apply for each station
            _vm.SetAllRefStationsBatch(refVal);

            // Update UI combos to reflect new values
            OnStationPairsChanged();
        }

        private void OnStationPairsChanged()
        {
            if (_vm == null) return;
            for (int i = 0; i < 6; i++)
            {
                if (_refCombos[i] != null)
                {
                    int refVal = _vm.StationRefSelections[i];
                    for (int j = 0; j < _refCombos[i].Items.Count; j++)
                    {
                        if ((int)_refCombos[i].Items[j]! == refVal)
                        {
                            _refCombos[i].SelectedIndex = j;
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Zoom Sync

        private bool _isSyncingZoom;

        private void OnMainChartZoomChanged(double xMin, double xMax, double yMin, double yMax)
        {
            if (_isSyncingZoom || !_isCompareVisible) return;
            _isSyncingZoom = true;
            try
            {
                CompareChartView.SetZoomRange(xMin, xMax, yMin, yMax);
            }
            finally
            {
                _isSyncingZoom = false;
            }
        }

        private void OnCompareChartZoomChanged(double xMin, double xMax, double yMin, double yMax)
        {
            if (_isSyncingZoom) return;
            _isSyncingZoom = true;
            try
            {
                ChartView.SetZoomRange(xMin, xMax, yMin, yMax);
            }
            finally
            {
                _isSyncingZoom = false;
            }
        }

        #endregion

        #region Theme

        public void UpdateChartTheme()
        {
            try
            {
                var bgColor = TryGetColor("BgDark");
                var gridColor = TryGetColor("BorderColor");
                var textColor = TryGetColor("TextPrimary");

                if (bgColor.HasValue)
                {
                    ChartView.SetThemeColors(bgColor.Value, gridColor ?? Colors.Gray, textColor ?? Colors.White);
                    CompareChartView.SetThemeColors(bgColor.Value, gridColor ?? Colors.Gray, textColor ?? Colors.White);
                }

                // Also update detached windows
                UpdateDetachedThemes();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UpdateChartTheme failed", ex);
            }
        }

        private Color? TryGetColor(string resourceKey)
        {
            try
            {
                var res = FindResource(resourceKey);
                if (res is SolidColorBrush scb) return scb.Color;
                if (res is LinearGradientBrush lgb && lgb.GradientStops.Count > 0)
                    return lgb.GradientStops[0].Color;
                if (res is Color c) return c;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"TryGetColor failed for key '{resourceKey}'", ex);
            }
            return null;
        }

        #endregion
    }
}
