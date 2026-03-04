using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // ── File loading, export, drag-drop ──

        public void ProcessFiles(string[] filePaths, Action<LogSessionData>? onLoadComplete = null)
            => SessionVM?.ProcessFiles(filePaths, onLoadComplete);

        private async Task LoadFile(object obj)
        {
            try
            {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log;*.csv|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|CPR Data (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                // Route CSV files to CPR tab instead of log processing
                var csvFiles = dialog.FileNames.Where(f => System.IO.Path.GetExtension(f).ToLower() == ".csv").ToArray();
                var logFiles = dialog.FileNames.Where(f => System.IO.Path.GetExtension(f).ToLower() != ".csv").ToArray();

                if (csvFiles.Length > 0)
                {
                    // Load the first CSV into CPR
                    SelectedTabIndex = AppConstants.TAB_CPR; // CPR tab
                    CprVM?.LoadFileDirect(csvFiles[0]);
                }

                if (logFiles.Length > 0)
                {
                    // For single non-session files, try routing to Different Logs tab
                    if (logFiles.Length == 1 && DifferentLogsVM != null)
                    {
                        var ext = System.IO.Path.GetExtension(logFiles[0]).ToLower();
                        bool isKnownSessionExt = ext == ".zip" || ext == ".log" || ext == ".file";
                        if (!isKnownSessionExt)
                        {
                            bool handled = await DifferentLogsVM.LoadFileAsync(logFiles[0]);
                            if (handled)
                            {
                                SelectedTabIndex = AppConstants.TAB_DIFFERENT_LOGS; // DIFFERENT LOGS tab
                                return;
                            }
                        }
                    }
                    ProcessFiles(logFiles);
                }
            }
            }
            catch (Exception ex) { AppLogger.Error("LoadFile failed", ex); }
        }

        private async Task ExportParsedData(object obj)
        {
            try
            {
            if (SessionVM.SelectedSession == null)
            {
                _dialogService.ShowInfo("No logs loaded.");
                return;
            }

            var selectedSession = SessionVM.SelectedSession;
            // S4-5 (binary APP): allow export even without parsed PLC logs — IO data comes from CSV
            bool hasLogs = selectedSession.Logs != null && selectedSession.Logs.Any();
            bool hasIoCsv = selectedSession.HasBinaryAppLogs &&
                            ((selectedSession.TerminalCsvBytes != null && selectedSession.TerminalCsvBytes.Keys.Any(k => System.IO.Path.GetFileName(k).StartsWith("Io-", StringComparison.OrdinalIgnoreCase))) ||
                             (selectedSession.TerminalLogFiles != null && selectedSession.TerminalLogFiles.Keys.Any(k => System.IO.Path.GetFileName(k).StartsWith("Io-", StringComparison.OrdinalIgnoreCase))));

            if (!hasLogs && !hasIoCsv)
            {
                _dialogService.ShowInfo("No logs loaded.");
                return;
            }

            if (_exportConfigWindow != null && _exportConfigWindow.IsLoaded)
            {
                WindowManager.ActivateWindow(_exportConfigWindow);
                return;
            }

            _exportConfigWindow = _viewFactory.Create<ExportConfigurationWindow>();
            var viewModel = new ExportConfigurationViewModel(selectedSession, _csvService, _dialogService);
            _exportConfigWindow.DataContext = viewModel;
            _exportConfigWindow.Closed += (s, e) => _exportConfigWindow = null;
            WindowManager.OpenWindow(_exportConfigWindow);
            }
            catch (Exception ex) { AppLogger.Error("ExportParsedData failed", ex); }
        }

        public async Task OnFilesDropped(string[] files)
        {
            try
            {
            if (files == null || files.Length == 0) return;

            // Check if any CSV files should be routed to CPR instead of log processing
            if (files.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLower();
                var fileName = System.IO.Path.GetFileName(files[0]).ToLower();

                // Route CSV files to CPR — EXCEPT event CSV files which are log events
                bool isEventCsv = fileName.StartsWith("event-history__from") || fileName.StartsWith("pressevents.");
                if (ext == ".csv" && !isEventCsv)
                {
                    // Switch to CPR tab and load
                    SelectedTabIndex = AppConstants.TAB_CPR; // CPR tab
                    CprVM?.LoadFileDirect(files[0]);
                    return;
                }

                // For single files that are NOT known session types (.zip, .log, .file),
                // try routing to Different Logs tab via plugin detection
                bool isKnownSessionExt = ext == ".zip" || ext == ".log" || ext == ".file";
                if (!isKnownSessionExt && DifferentLogsVM != null)
                {
                    bool handled = await DifferentLogsVM.LoadFileAsync(files[0]);
                    if (handled)
                    {
                        SelectedTabIndex = AppConstants.TAB_DIFFERENT_LOGS; // DIFFERENT LOGS tab
                        return;
                    }
                }
            }

            ProcessFiles(files);
            }
            catch (Exception ex) { AppLogger.Error("OnFilesDropped failed", ex); }
        }

        internal void LoadMultipleFiles(List<(string FilePath, string SessionName)> fileList)
        {
            if (fileList == null || fileList.Count == 0) return;

            // Get list of already loaded files
            var loadedFilePaths = SessionVM.LoadedSessions.Select(s => s.FilePath).ToList();

            // Show file selection window
            var fileSelectionWindow = _viewFactory.Create<Views.FileSelectionWindow>(fileList, loadedFilePaths);
            fileSelectionWindow.Owner = Application.Current.MainWindow;

            if (fileSelectionWindow.ShowDialog() == true)
            {
                var filesToLoad = fileSelectionWindow.FilesToLoad;

                if (filesToLoad != null && filesToLoad.Count > 0)
                {
                    // Load all files using ProcessFiles
                    ProcessFiles(filesToLoad.ToArray(), null);

                    _dialogService.ShowInfo($"Loaded {filesToLoad.Count} file(s).", "Open All Files");
                }
            }
        }
    }
}
