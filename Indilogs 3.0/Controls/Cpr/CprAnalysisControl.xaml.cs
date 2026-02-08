using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.ViewModels;
using Microsoft.Win32;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprAnalysisControl : UserControl
    {
        private CprAnalysisViewModel _vm;
        private bool _isWired;

        // Station pair ComboBoxes (test + ref) for 6 pairs
        private ComboBox[] _testCombos = new ComboBox[6];
        private ComboBox[] _refCombos = new ComboBox[6];

        // Compare support
        private CprAnalysisViewModel _compareVm;
        private bool _isCompareVisible;
        private bool _isSyncingToCompare; // guard to prevent re-entry

        // Detached chart windows
        private readonly List<Window> _detachedWindows = new List<Window>();

        public CprAnalysisControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm = DataContext as CprAnalysisViewModel;
            if (_vm == null) return;

            if (!_isWired)
            {
                _isWired = true;
                _vm.GraphResultUpdated += OnGraphResultUpdated;
                _vm.ExportRequested += OnExportRequested;
                _vm.StationPairsChanged += OnStationPairsChanged;
                BuildStationPairsUI();

                // Wire zoom sync between charts
                ChartView.ZoomChanged += OnMainChartZoomChanged;
                CompareChartView.ZoomChanged += OnCompareChartZoomChanged;
            }

            UpdateChartTheme();
        }

        #region File Handling

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open CPR Data File",
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() == true && _vm != null)
            {
                _vm.LoadFileDirect(dlg.FileName);
            }
        }

        private void CompareFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open CPR Compare File",
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() == true && _vm != null)
            {
                _compareVm = new CprAnalysisViewModel();
                _compareVm.GraphResultUpdated += OnCompareGraphResultUpdated;
                _compareVm.LoadFileDirect(dlg.FileName);
                ShowCompareUI(true);
            }
        }

        private void RemoveCompare_Click(object sender, RoutedEventArgs e)
        {
            if (_compareVm != null)
            {
                _compareVm.GraphResultUpdated -= OnCompareGraphResultUpdated;
                _compareVm = null;
            }
            CompareChartView.SetGraphResult(null);
            ShowCompareUI(false);
        }

        private void ShowCompareUI(bool show)
        {
            _isCompareVisible = show;
            if (show)
            {
                // Charts: top/bottom
                CompareChartBorder.Visibility = Visibility.Visible;
                CompareChartRow.Height = new GridLength(1, GridUnitType.Star);
                RemoveCompareBtn.Visibility = Visibility.Visible;
                DetachCompareBtn.Visibility = Visibility.Visible;

                // Stats: side-by-side
                CompareStatsBorder.Visibility = Visibility.Visible;
                CompareStatsCol.Width = new GridLength(1, GridUnitType.Star);
                File1StatsLabel.Visibility = Visibility.Visible;

                // Bind compare stats grids to compare VM
                CompareStatsGrid.ItemsSource = _compareVm.StatsData;
                CompareOffsetSkewGrid.ItemsSource = _compareVm.OffsetSkewData;

                UpdateChartTheme();
            }
            else
            {
                // Charts: hide
                CompareChartBorder.Visibility = Visibility.Collapsed;
                CompareChartRow.Height = new GridLength(0);
                RemoveCompareBtn.Visibility = Visibility.Collapsed;
                DetachCompareBtn.Visibility = Visibility.Collapsed;

                // Stats: hide compare tables
                CompareStatsBorder.Visibility = Visibility.Collapsed;
                CompareStatsCol.Width = new GridLength(0);
                File1StatsLabel.Visibility = Visibility.Collapsed;

                // Unbind
                CompareStatsGrid.ItemsSource = null;
                CompareOffsetSkewGrid.ItemsSource = null;
            }
        }

        #endregion

        #region Export

        private void ExportPlot_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.CurrentResult != null)
                OnExportRequested(_vm.CurrentResult);
        }

        #endregion

        #region Sync to Compare

        /// <summary>
        /// Sync all current settings to the compare VM and apply.
        /// Called whenever the main graph updates.
        /// </summary>
        private void SyncToCompare()
        {
            if (_compareVm == null || !_isCompareVisible) return;
            if (_isSyncingToCompare) return;
            _isSyncingToCompare = true;
            try
            {
                // Sync graph type
                _compareVm.SelectedGraphType = _vm.SelectedGraphType;

                // Sync options
                _compareVm.RemoveDC = _vm.RemoveDC;
                _compareVm.AutoYAxis = _vm.AutoYAxis;
                _compareVm.SharedYAxis = _vm.SharedYAxis;
                _compareVm.YAxisFrom = _vm.YAxisFrom;
                _compareVm.YAxisTo = _vm.YAxisTo;
                _compareVm.SelectedSmoothing = _vm.SelectedSmoothing;
                _compareVm.SelectedBowDegree = _vm.SelectedBowDegree;
                _compareVm.IsYAxis = _vm.IsYAxis;
                _compareVm.BlanketCyclesText = _vm.BlanketCyclesText;
                _compareVm.HistoStationsText = _vm.HistoStationsText;

                // Sync filters: Revolution, Iteration, Cycle range, Column range
                // Only sync if the value exists in the compare VM's available options
                if (_vm.SelectedRevolution != null && _compareVm.Revolutions.Contains(_vm.SelectedRevolution))
                    _compareVm.SelectedRevolution = _vm.SelectedRevolution;

                if (_compareVm.Iterations.Contains(_vm.SelectedIteration))
                    _compareVm.SelectedIteration = _vm.SelectedIteration;

                if (_compareVm.Cycles.Contains(_vm.SelectedCycleFrom))
                    _compareVm.SelectedCycleFrom = _vm.SelectedCycleFrom;
                if (_compareVm.Cycles.Contains(_vm.SelectedCycleTo))
                    _compareVm.SelectedCycleTo = _vm.SelectedCycleTo;

                if (_compareVm.Columns.Contains(_vm.SelectedColumnFrom))
                    _compareVm.SelectedColumnFrom = _vm.SelectedColumnFrom;
                if (_compareVm.Columns.Contains(_vm.SelectedColumnTo))
                    _compareVm.SelectedColumnTo = _vm.SelectedColumnTo;

                // Copy station pairs
                for (int i = 0; i < 6; i++)
                {
                    _compareVm.StationTestSelections[i] = _vm.StationTestSelections[i];
                    _compareVm.StationRefSelections[i] = _vm.StationRefSelections[i];
                }

                _compareVm.Apply();
            }
            finally
            {
                _isSyncingToCompare = false;
            }
        }

        #endregion

        #region Graph Result Handlers

        private void OnGraphResultUpdated(CprGraphResult result)
        {
            ChartView.SetGraphResult(result);
            // Sync everything to compare when main updates
            SyncToCompare();
        }

        private void OnCompareGraphResultUpdated(CprGraphResult result)
        {
            CompareChartView.SetGraphResult(result);
        }

        private void OnExportRequested(CprGraphResult result)
        {
            var exportWindow = new Window
            {
                Title = "CPR Export - " + (result.Title ?? "Graph"),
                Width = 900,
                Height = 600,
                Background = (Brush)FindResource("BgDark"),
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var chartView = new CprChartView();
            exportWindow.Content = chartView;
            exportWindow.Show();
            chartView.SetGraphResult(result);
        }

        #endregion

        #region Detach

        private void DetachChart_Click(object sender, RoutedEventArgs e)
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
            catch
            {
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

        private void DetachCompareChart_Click(object sender, RoutedEventArgs e)
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
            catch
            {
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
                catch { }
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

        private void RefStationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm != null && RefStationCombo.SelectedItem is int val)
                _vm.RefStationValue = val;
        }

        private void SetRefStation_Click(object sender, RoutedEventArgs e)
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
                        if ((int)_refCombos[i].Items[j] == refVal)
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
            catch { }
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
            catch { }
            return null;
        }

        #endregion
    }
}
