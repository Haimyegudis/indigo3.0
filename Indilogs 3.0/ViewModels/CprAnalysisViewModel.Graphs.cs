using System;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.ViewModels
{
    public partial class CprAnalysisViewModel
    {
        #region Apply & AutoApply

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
                CprGraphResult? result = null;

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
                        // Don't filter by revolution — the graph needs all revolution types
                        var revFilter = BuildFilterState();
                        revFilter.Revolution = "";
                        var allData = _dataService.ApplyBaseFilters(revFilter);
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
                AppLogger.Error("CPR analysis execution failed", ex);
            }
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
            foreach (var r in _dataService.GetRevolutions(_selectedMachine, _selectedCalibTime ?? ""))
                Revolutions.Add(r);
            if (Revolutions.Count > 0)
                SelectedRevolution = Revolutions[0];

            // Iterations
            Iterations.Clear();
            foreach (var it in _dataService.GetIterations(_selectedMachine, _selectedCalibTime ?? ""))
                Iterations.Add(it);
            if (Iterations.Count > 0)
                SelectedIteration = Iterations[0];

            // Cycles
            Cycles.Clear();
            foreach (var c in _dataService.GetCycles(_selectedMachine, _selectedCalibTime ?? ""))
                Cycles.Add(c);
            if (Cycles.Count > 0)
            {
                SelectedCycleFrom = Cycles.First();
                SelectedCycleTo = Cycles.Last();
            }

            // Columns
            Columns.Clear();
            foreach (var col in _dataService.GetColumns(_selectedMachine, _selectedCalibTime ?? ""))
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
                CalibrationTime = _selectedCalibTime ?? "",
                Revolution = _selectedRevolution ?? "",
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Offset/skew computation failed: {ex.Message}");
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
    }
}
