using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services.Cpr;

namespace IndiLogs_3._0.ViewModels
{
    public class CprAnalysisViewModel : INotifyPropertyChanged
    {
        private readonly CprDataService _dataService = new CprDataService();
        private readonly CprAnalysisService _analysisService = new CprAnalysisService();
        private bool _isLoadingFilters; // guard against cascading Apply calls during population

        public CprAnalysisViewModel()
        {
            LoadFileCommand = new RelayCommand(_ => LoadFile());
            ExportCommand = new RelayCommand(_ => Export(), _ => CurrentResult != null);
            SetRefCommand = new RelayCommand(_ => SetRefStation());

            // Init station pairs (default: test=1-6, ref=0)
            for (int i = 0; i < 6; i++)
            {
                StationTestSelections[i] = i + 1;
                StationRefSelections[i] = 0;
            }

            // Init smoothing options
            for (int i = 1; i <= 13; i += 2) SmoothingOptions.Add(i);
            SelectedSmoothing = 1;

            // Init bow degree options
            for (int i = 2; i <= 8; i++) BowDegreeOptions.Add(i);
            SelectedBowDegree = 3;
        }

        #region Properties

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        private CprGraphType _selectedGraphType = CprGraphType.Colors;
        public CprGraphType SelectedGraphType
        {
            get => _selectedGraphType;
            set
            {
                _selectedGraphType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBlanketCyclesVisible));
                OnPropertyChanged(nameof(IsHistoStationsVisible));
                AutoApply();
            }
        }

        // Machine numbers
        public ObservableCollection<int> MachineNumbers { get; } = new ObservableCollection<int>();
        private int _selectedMachine;
        public int SelectedMachine
        {
            get => _selectedMachine;
            set { _selectedMachine = value; OnPropertyChanged(); OnMachineChanged(); }
        }

        // Calibration times
        public ObservableCollection<string> CalibrationTimes { get; } = new ObservableCollection<string>();
        private string _selectedCalibTime;
        public string SelectedCalibTime
        {
            get => _selectedCalibTime;
            set { _selectedCalibTime = value; OnPropertyChanged(); OnCalibTimeChanged(); }
        }

        // Revolutions
        public ObservableCollection<string> Revolutions { get; } = new ObservableCollection<string>();
        private string _selectedRevolution;
        public string SelectedRevolution
        {
            get => _selectedRevolution;
            set { _selectedRevolution = value; OnPropertyChanged(); AutoApply(); }
        }

        // Iterations
        public ObservableCollection<int> Iterations { get; } = new ObservableCollection<int>();
        private int _selectedIteration;
        public int SelectedIteration
        {
            get => _selectedIteration;
            set { _selectedIteration = value; OnPropertyChanged(); AutoApply(); }
        }

        // Cycles
        public ObservableCollection<int> Cycles { get; } = new ObservableCollection<int>();
        private int _selectedCycleFrom;
        public int SelectedCycleFrom
        {
            get => _selectedCycleFrom;
            set { _selectedCycleFrom = value; OnPropertyChanged(); AutoApply(); }
        }
        private int _selectedCycleTo;
        public int SelectedCycleTo
        {
            get => _selectedCycleTo;
            set { _selectedCycleTo = value; OnPropertyChanged(); AutoApply(); }
        }

        // Columns
        public ObservableCollection<int> Columns { get; } = new ObservableCollection<int>();
        private int _selectedColumnFrom;
        public int SelectedColumnFrom
        {
            get => _selectedColumnFrom;
            set { _selectedColumnFrom = value; OnPropertyChanged(); AutoApply(); }
        }
        private int _selectedColumnTo;
        public int SelectedColumnTo
        {
            get => _selectedColumnTo;
            set { _selectedColumnTo = value; OnPropertyChanged(); AutoApply(); }
        }

        // Station pairs (6 test + 6 ref)
        public int[] StationTestSelections { get; } = new int[6];
        public int[] StationRefSelections { get; } = new int[6];

        // Ref station for "Set" button
        private int _refStationValue;
        public int RefStationValue
        {
            get => _refStationValue;
            set { _refStationValue = value; OnPropertyChanged(); }
        }

        // Checkboxes
        private bool _removeDC;
        public bool RemoveDC
        {
            get => _removeDC;
            set { _removeDC = value; OnPropertyChanged(); AutoApply(); }
        }

        private bool _autoYAxis = true;
        public bool AutoYAxis
        {
            get => _autoYAxis;
            set { _autoYAxis = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsManualYVisible)); AutoApply(); }
        }

        public bool IsManualYVisible => !_autoYAxis;

        private bool _sharedYAxis;
        public bool SharedYAxis
        {
            get => _sharedYAxis;
            set { _sharedYAxis = value; OnPropertyChanged(); AutoApply(); }
        }

        // Y-axis range
        private string _yAxisFrom = "-200";
        public string YAxisFrom
        {
            get => _yAxisFrom;
            set { _yAxisFrom = value; OnPropertyChanged(); AutoApply(); }
        }

        private string _yAxisTo = "200";
        public string YAxisTo
        {
            get => _yAxisTo;
            set { _yAxisTo = value; OnPropertyChanged(); AutoApply(); }
        }

        // Axis selection
        private bool _isYAxis = true;
        public bool IsYAxis
        {
            get => _isYAxis;
            set { _isYAxis = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsXAxis)); AutoApply(); }
        }
        public bool IsXAxis
        {
            get => !_isYAxis;
            set { _isYAxis = !value; OnPropertyChanged(nameof(IsYAxis)); OnPropertyChanged(); AutoApply(); }
        }

        // Smoothing
        public ObservableCollection<int> SmoothingOptions { get; } = new ObservableCollection<int>();
        private int _selectedSmoothing = 1;
        public int SelectedSmoothing
        {
            get => _selectedSmoothing;
            set { _selectedSmoothing = value; OnPropertyChanged(); AutoApply(); }
        }

        // Bow degree
        public ObservableCollection<int> BowDegreeOptions { get; } = new ObservableCollection<int>();
        private int _selectedBowDegree = 3;
        public int SelectedBowDegree
        {
            get => _selectedBowDegree;
            set { _selectedBowDegree = value; OnPropertyChanged(); AutoApply(); }
        }

        // Blanket cycles text
        private string _blanketCyclesText = "";
        public string BlanketCyclesText
        {
            get => _blanketCyclesText;
            set { _blanketCyclesText = value; OnPropertyChanged(); }
        }
        public bool IsBlanketCyclesVisible => _selectedGraphType == CprGraphType.BlanketCycles;

        // Histogram stations text
        private string _histoStationsText = "1 2 3 4 5 6";
        public string HistoStationsText
        {
            get => _histoStationsText;
            set { _histoStationsText = value; OnPropertyChanged(); }
        }
        public bool IsHistoStationsVisible => _selectedGraphType == CprGraphType.Histogram;

        // Result data
        private CprGraphResult _currentResult;
        public CprGraphResult CurrentResult
        {
            get => _currentResult;
            set { _currentResult = value; OnPropertyChanged(); }
        }

        // Stats tables
        public ObservableCollection<CprStatsRow> StatsData { get; } = new ObservableCollection<CprStatsRow>();
        public ObservableCollection<CprOffsetSkewRow> OffsetSkewData { get; } = new ObservableCollection<CprOffsetSkewRow>();

        #endregion

        #region Commands

        public ICommand LoadFileCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand SetRefCommand { get; }

        // Event to request file dialog from View
        public event Action<Action<string>> RequestFileDialog;
        // Event to update chart
        public event Action<CprGraphResult> GraphResultUpdated;
        // Event to request export
        public event Action<CprGraphResult> ExportRequested;

        #endregion

        #region Command Implementations

        private void LoadFile()
        {
            RequestFileDialog?.Invoke(path =>
            {
                if (string.IsNullOrEmpty(path)) return;

                try
                {
                    _dataService.LoadCsv(path);
                    FilePath = path;
                    PopulateAndAutoApply();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error loading CSV: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            });
        }

        /// <summary>
        /// Load CSV directly by path (called from code-behind file dialog)
        /// </summary>
        public void LoadFileDirect(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                _dataService.LoadCsv(path);
                FilePath = path;
                PopulateAndAutoApply();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading CSV: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Populate all filters from loaded data and immediately apply the graph
        /// </summary>
        private void PopulateAndAutoApply()
        {
            _isLoadingFilters = true;
            try
            {
                PopulateMachineNumbers();
            }
            finally
            {
                _isLoadingFilters = false;
            }

            // Now auto-apply with the populated filters
            Apply();
        }

        /// <summary>
        /// Auto-apply: called from property setters to immediately refresh graph
        /// </summary>
        private void AutoApply()
        {
            if (_isLoadingFilters) return;
            if (!_dataService.IsLoaded) return;
            Apply();
        }

        public void Apply()
        {
            if (!_dataService.IsLoaded) return;
            if (_isLoadingFilters) return;

            var filter = BuildFilterState();
            var pairs = BuildStationPairs();

            try
            {
                CprGraphResult result = null;

                switch (_selectedGraphType)
                {
                    case CprGraphType.Colors:
                        result = _analysisService.ComputeColors(_dataService.ApplyFilters(filter), filter, pairs);
                        break;
                    case CprGraphType.Columns:
                        result = _analysisService.ComputeColumns(_dataService.ApplyFilters(filter), filter, pairs[0]);
                        break;
                    case CprGraphType.BlanketCycles:
                        int[] wantedCycles = ParseIntList(_blanketCyclesText);
                        var blanketData = _dataService.ApplyBaseFilters(filter);
                        result = _analysisService.ComputeBlanketCycles(blanketData, filter, pairs[0], wantedCycles);
                        break;
                    case CprGraphType.XScaling:
                        result = _analysisService.ComputeXScaling(_dataService.ApplyFilters(filter), filter);
                        break;
                    case CprGraphType.DFT:
                        result = _analysisService.ComputeDFT(_dataService.ApplyFilters(filter), filter, pairs[0]);
                        break;
                    case CprGraphType.Histogram:
                        int[] stations = ParseIntList(_histoStationsText);
                        result = _analysisService.ComputeHistogram(_dataService.ApplyFilters(filter), filter, stations);
                        break;
                    case CprGraphType.Revolutions:
                        var allData = _dataService.ApplyBaseFilters(filter);
                        result = _analysisService.ComputeRevolutions(allData, filter, pairs[0]);
                        break;
                    case CprGraphType.MissingData:
                        result = _analysisService.ComputeMissingData(_dataService.ApplyFilters(filter), filter);
                        break;
                    case CprGraphType.Skew:
                        result = _analysisService.ComputeSkew(_dataService.ApplyBaseFilters(filter), filter);
                        break;
                    case CprGraphType.SkewAlongBracket:
                        result = _analysisService.ComputeSkewAlongBracket(_dataService.ApplyFilters(filter), filter, pairs[0]);
                        break;
                }

                if (result != null)
                {
                    CurrentResult = result;
                    GraphResultUpdated?.Invoke(result);
                }

                // Update stats
                UpdateStats(filter);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CPR Apply error: {ex.Message}");
            }
        }

        private void Export()
        {
            if (_currentResult != null)
                ExportRequested?.Invoke(_currentResult);
        }

        private void SetRefStation()
        {
            SetAllRefStationsBatch(_refStationValue);
        }

        /// <summary>
        /// Set all 6 ref stations in one batch, only fires Apply once at the end.
        /// </summary>
        public void SetAllRefStationsBatch(int refVal)
        {
            for (int i = 0; i < 6; i++)
                StationRefSelections[i] = refVal;
            OnPropertyChanged(nameof(StationRefSelections));
            StationPairsChanged?.Invoke();
            Apply();
        }

        public event Action StationPairsChanged;

        /// <summary>
        /// Called from code-behind when station pair combos change
        /// </summary>
        public void OnStationPairChanged()
        {
            AutoApply();
        }

        #endregion

        #region Filter Population (cascading)

        private void PopulateMachineNumbers()
        {
            MachineNumbers.Clear();
            foreach (var sn in _dataService.GetMachineNumbers())
                MachineNumbers.Add(sn);

            if (MachineNumbers.Count > 0)
                SelectedMachine = MachineNumbers[0];
        }

        private void OnMachineChanged()
        {
            if (!_dataService.IsLoaded) return;

            CalibrationTimes.Clear();
            foreach (var t in _dataService.GetCalibrationTimes(_selectedMachine))
                CalibrationTimes.Add(t);

            if (CalibrationTimes.Count > 0)
                SelectedCalibTime = CalibrationTimes[0];
        }

        private void OnCalibTimeChanged()
        {
            if (!_dataService.IsLoaded) return;

            // Revolutions
            Revolutions.Clear();
            foreach (var r in _dataService.GetRevolutions(_selectedMachine, _selectedCalibTime))
                Revolutions.Add(r);
            if (Revolutions.Count > 0)
                SelectedRevolution = Revolutions[0];

            // Iterations
            Iterations.Clear();
            foreach (var it in _dataService.GetIterations(_selectedMachine, _selectedCalibTime))
                Iterations.Add(it);
            if (Iterations.Count > 0)
                SelectedIteration = Iterations[0];

            // Cycles
            Cycles.Clear();
            foreach (var c in _dataService.GetCycles(_selectedMachine, _selectedCalibTime))
                Cycles.Add(c);
            if (Cycles.Count > 0)
            {
                SelectedCycleFrom = Cycles.First();
                SelectedCycleTo = Cycles.Last();
            }

            // Columns
            Columns.Clear();
            foreach (var col in _dataService.GetColumns(_selectedMachine, _selectedCalibTime))
                Columns.Add(col);
            if (Columns.Count > 0)
            {
                SelectedColumnFrom = Columns.First();
                SelectedColumnTo = Columns.Last();
            }
        }

        #endregion

        #region Helpers

        private CprFilterState BuildFilterState()
        {
            double yFrom = -200, yTo = 200;
            double.TryParse(_yAxisFrom, out yFrom);
            double.TryParse(_yAxisTo, out yTo);

            return new CprFilterState
            {
                MachineSN = _selectedMachine,
                CalibrationTime = _selectedCalibTime,
                Revolution = _selectedRevolution,
                Iteration = _selectedIteration,
                CycleFrom = _selectedCycleFrom,
                CycleTo = _selectedCycleTo,
                ColumnFrom = _selectedColumnFrom,
                ColumnTo = _selectedColumnTo,
                Axis = _isYAxis ? "Y" : "X",
                RemoveDC = _removeDC,
                AutoYAxis = _autoYAxis,
                SharedYAxis = _sharedYAxis,
                SmoothingWindow = _selectedSmoothing,
                BowDegree = _selectedBowDegree,
                YAxisFrom = yFrom,
                YAxisTo = yTo
            };
        }

        private CprStationPair[] BuildStationPairs()
        {
            var pairs = new CprStationPair[6];
            for (int i = 0; i < 6; i++)
            {
                pairs[i] = new CprStationPair
                {
                    TestStation = StationTestSelections[i],
                    RefStation = StationRefSelections[i]
                };
            }
            return pairs;
        }

        private void UpdateStats(CprFilterState filter)
        {
            try
            {
                var baseData = _dataService.ApplyBaseFilters(filter);
                if (baseData.Count == 0) return;

                var statsRows = _analysisService.ComputeStats(baseData);
                StatsData.Clear();
                foreach (var row in statsRows)
                    StatsData.Add(row);

                var osRows = _analysisService.ComputeOffsetSkew(baseData, filter.Axis);
                OffsetSkewData.Clear();
                foreach (var row in osRows)
                    OffsetSkewData.Add(row);
            }
            catch
            {
            }
        }

        private static int[] ParseIntList(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new int[0];
            return text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => { int.TryParse(s.Trim(), out int v); return v; })
                .Where(v => v > 0)
                .ToArray();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
