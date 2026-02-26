using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IChartDataTransferService
    {
        event Action<ChartDataPackage> OnDataReady;
        event Action OnSwitchToChartsRequested;
        event Action<DateTime> OnLogTimeSelected;
        event Action<DateTime> OnChartTimeSelected;

        void TransferDataToCharts(ChartDataPackage data);
        void RequestSwitchToCharts();
        void NotifyLogTimeSelected(DateTime time);
        void NotifyChartTimeSelected(DateTime time);
        ChartDataPackage BuildDataPackage(IEnumerable<LogEntry> logs, ExportPreset preset, string sessionName);

        /// <summary>
        /// Release the CurrentData reference after charts have consumed the data.
        /// Allows GC to collect the ChartDataPackage if no other references exist.
        /// </summary>
        void ClearCurrentData();
    }
}
