using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprAnalysisControl : UserControl
    {
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
