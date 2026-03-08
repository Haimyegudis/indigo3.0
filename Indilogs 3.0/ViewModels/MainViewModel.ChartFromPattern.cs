using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Views;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        public ICommand CreateChartFromPatternCommand { get; private set; } = null!;

        private void InitChartFromPatternCommand()
        {
            CreateChartFromPatternCommand = new RelayCommand(
                o => CreateChartFromPattern(),
                o => SelectedLog != null && (SessionVM.AllLogsCache?.Count > 0 || SessionVM.AllAppLogsCache?.Count > 0));
        }

        private async void CreateChartFromPattern()
        {
            var log = SelectedLog;
            if (log == null) return;

            var window = _viewFactory.Create<PatternChartWindow>(log.Message ?? "");
            window.Owner = _windowOwner.GetOwner();
            if (window.ShowDialog() != true) return;

            string pattern = window.ExtractionPattern;
            string chartName = window.ChartName;
            bool includeState = window.IncludeMachineState;
            var searchField = window.SearchField;

            // Use PLC logs or App logs depending on which tab is active
            var logs = SelectedTabIndex == AppConstants.TAB_APP
                ? SessionVM.AllAppLogsCache
                : SessionVM.AllLogsCache;

            if (logs == null || logs.Count == 0)
            {
                _dialogService.ShowInfo("No logs loaded to scan.", "No Data");
                return;
            }

            string sessionName = SessionVM.SelectedSession?.FileName ?? "Session";
            SessionVM.StatusMessage = $"Building chart from pattern: {chartName}...";

            var package = await Task.Run(() =>
                PatternChartService.BuildFromPattern(logs, pattern, chartName, searchField, includeState, sessionName))
                .ConfigureAwait(true);

            if (package.Signals.Count == 0 || (package.Signals[0].SparsePoints?.Count ?? 0) == 0)
            {
                _dialogService.ShowInfo("Pattern did not match any data in the logs.", "No Matches");
                SessionVM.StatusMessage = "Ready";
                return;
            }

            ChartDataTransferService.Instance.TransferDataToCharts(package);
            ChartDataTransferService.Instance.RequestSwitchToCharts();
            SessionVM.StatusMessage = $"Chart '{chartName}' created with {package.Signals[0].SparsePoints?.Count ?? 0} data points";
        }
    }
}
