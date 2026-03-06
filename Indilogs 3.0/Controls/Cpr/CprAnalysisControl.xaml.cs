using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.ViewModels;
using Microsoft.Win32;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprAnalysisControl : UserControl
    {
        private CprAnalysisViewModel? _vm;
        private bool _isWired;

        // Station pair ComboBoxes (test + ref) for 6 pairs
        private ComboBox[] _testCombos = new ComboBox[6];
        private ComboBox[] _refCombos = new ComboBox[6];

        // Compare support
        private CprAnalysisViewModel? _compareVm;
        private bool _isCompareVisible;
        private bool _isSyncingToCompare; // guard to prevent re-entry

        // Detached chart windows
        private readonly List<Window> _detachedWindows = new List<Window>();

        public CprAnalysisControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += (s, e) => EnsureVmWired();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            EnsureVmWired();
            UpdateChartTheme();
        }

        /// <summary>
        /// Ensure the ViewModel is resolved and events are wired.
        /// Called from OnLoaded and also from click handlers as a fallback.
        /// </summary>
        private void EnsureVmWired()
        {
            if (_vm == null)
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
        }

        #region File Handling

        private void ChooseFile_Click(object? sender, RoutedEventArgs e)
        {
            EnsureVmWired();

            var dlg = new OpenFileDialog
            {
                Title = "Open CPR Data File",
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() == true)
            {
                if (_vm != null)
                {
                    _vm.LoadFileDirect(dlg.FileName);
                }
                else
                {
                    System.Windows.MessageBox.Show("Internal error: CPR ViewModel not initialized. Please switch to another tab and back, then try again.",
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
        }

        private void CompareFile_Click(object? sender, RoutedEventArgs e)
        {
            EnsureVmWired();

            var dlg = new OpenFileDialog
            {
                Title = "Open CPR Compare File",
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() == true)
            {
                _compareVm = new CprAnalysisViewModel(new Services.DialogService());
                _compareVm.GraphResultUpdated += OnCompareGraphResultUpdated;
                _compareVm.LoadFileDirect(dlg.FileName);
                ShowCompareUI(true);
            }
        }

        private void RemoveCompare_Click(object? sender, RoutedEventArgs e)
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
                CompareStatsGrid.ItemsSource = _compareVm!.StatsData;
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

        private void ExportPlot_Click(object? sender, RoutedEventArgs e)
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
            if (_compareVm == null || !_isCompareVisible || _vm == null) return;
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
    }
}
