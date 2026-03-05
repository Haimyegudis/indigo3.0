using IndiLogs_3._0.Models;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public partial class CsvExportService : Interfaces.ICsvExportService
    {
        private readonly Interfaces.IDialogService? _dialogService;
        private readonly Interfaces.IDispatcher? _dispatcher;

        public CsvExportService(Interfaces.IDialogService? dialogService = null, Interfaces.IDispatcher? dispatcher = null)
        {
            _dialogService = dialogService;
            _dispatcher = dispatcher;
        }

        private readonly string[] _axisParams = new[] { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr", "Trigger" };
        private readonly string[] _chStepParams = new[] { "StepMessage", "SubStepNo", "CHObjType", "PrevStepNo", "DiffTime", "State", "Parent", "SubsysID" };
        private readonly string[] _ioStatusParams = new[] { "Value", "eIoStatus" };
        private readonly string[] _motTempParams = new[] { "MotTemp", "eIoStatus" };
        private readonly string[] _drvTempParams = new[] { "DrvTemp", "eIoStatus" };

        // eIoStatus enum string mapping
        private static string DecodeIoStatus(string statusStr)
        {
            if (string.IsNullOrEmpty(statusStr)) return "Op"; // default Operational
            switch (statusStr.Trim())
            {
                case "Inv": return "InvalidHW";
                case "NM": return "NotMonitored";
                case "NA": return "NotActive";
                case "Op": return "Operational";
                case "OpL": return "OperationalLow";
                case "OpH": return "OperationalHigh";
                case "PnL": return "PanicLow";
                case "PnH": return "PanicHigh";
                default: return statusStr.Trim();
            }
        }

        // Progress reporting
        public interface IProgress
        {
            void Report(int percentage, string status, string details = "");
            bool IsCancelled { get; }
        }

        public async Task<string?> ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset? preset = null)
        {
            if (preset != null)
            {
                return await ExportLogsWithPresetAsync(logs, defaultFileName, preset).ConfigureAwait(false);
            }

            return await ExportLogsOriginalAsync(logs, defaultFileName).ConfigureAwait(false);
        }

        private async Task<string?> ExportLogsOriginalAsync(IEnumerable<LogEntry> logs, string defaultFileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_CombinedData.csv",
                InitialDirectory = AppPaths.Root
            };

            if (saveFileDialog.ShowDialog() != true) return null;

            string filePath = saveFileDialog.FileName;

            // Show progress window (NON-MODAL)
            var progressWindow = new ExportProgressWindow();
            progressWindow.Show(); // NON-MODAL - allows user to continue working

            var progressReporter = new ProgressReporter(progressWindow);

            // Run export in background - don't block UI
            _ = Task.Run(() =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, preset: null, progressReporter);
                    progressWindow.Complete(true, $"Saved to:\n{Path.GetFileName(filePath)}");
                }
                catch (OperationCanceledException)
                {
                    progressWindow.Complete(false, "Export cancelled by user");
                }
                catch (Exception ex)
                {
                    progressWindow.Complete(false, $"Error: {ex.Message}");
                }
            });

            // Return file path immediately - export continues in background
            return filePath;
        }

        private class ProgressReporter : IProgress
        {
            private readonly ExportProgressWindow _window;

            public ProgressReporter(ExportProgressWindow window)
            {
                _window = window;
            }

            public void Report(int percentage, string status, string details = "")
            {
                _window.UpdateProgress(percentage, status, details);
            }

            public bool IsCancelled => _window.IsCancelled;
        }
    }
}
