using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using WindowManager = IndiLogs_3._0.Services.WindowManager;

namespace IndiLogs_3._0.Views
{
    public partial class StripeAnalysisWindow : Window
    {
        #region Column Settings Persistence

        private void LoadColumnSettings()
        {
            try
            {
                if (!File.Exists(ColumnOrderFilePath))
                    return;

                var json = File.ReadAllText(ColumnOrderFilePath);
                var savedSettings = JsonConvert.DeserializeObject<List<ColumnSettingsInfo>>(json, AppConstants.SafeJsonSettings);

                if (savedSettings == null || savedSettings.Count == 0)
                    return;

                // Apply saved settings
                var columns = StripeDataGrid.Columns.ToList();
                foreach (var col in columns)
                {
                    var saved = savedSettings.FirstOrDefault(s => s.Header == col.Header.ToString());
                    if (saved != null)
                    {
                        col.DisplayIndex = Math.Min(saved.DisplayIndex, columns.Count - 1);
                        col.Width = new DataGridLength(saved.Width);
                        col.Visibility = saved.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                TxtStatus.Text = "Column settings restored from previous session";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Loading column settings failed", ex);
            }
        }

        private void SaveColumnSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(ColumnOrderFilePath);
                if (directory != null && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var columnSettings = StripeDataGrid.Columns.Select(c => new ColumnSettingsInfo
                {
                    Header = c.Header?.ToString() ?? "",
                    DisplayIndex = c.DisplayIndex,
                    Width = c.ActualWidth > 0 ? c.ActualWidth : c.Width.Value,
                    IsVisible = c.Visibility == Visibility.Visible
                }).ToList();

                var json = JsonConvert.SerializeObject(columnSettings, Formatting.Indented);
                File.WriteAllText(ColumnOrderFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Saving column settings failed", ex);
            }
        }

        private class ColumnSettingsInfo
        {
            public string Header { get; set; } = "";
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        private void StripeDataGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
            TxtStatus.Text = "Column order saved";
        }

        private void BtnManageColumns_Click(object? sender, RoutedEventArgs e)
        {
            var managerWindow = new ColumnManagerWindow(StripeDataGrid);
            managerWindow.Owner = this;
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
                TxtStatus.Text = "Column visibility updated";
            }
        }

        private void BtnResetColumns_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Delete saved settings file
                if (File.Exists(ColumnOrderFilePath))
                    File.Delete(ColumnOrderFilePath);

                // Reset to default order and visibility
                for (int i = 0; i < StripeDataGrid.Columns.Count; i++)
                {
                    StripeDataGrid.Columns[i].DisplayIndex = i;
                    StripeDataGrid.Columns[i].Visibility = Visibility.Visible;
                }

                TxtStatus.Text = "Column settings reset to default";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting columns: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            SaveColumnSettings();
        }

        #endregion

        #region Event Handlers

        private void Filter_Changed(object? sender, RoutedEventArgs e)
        {
            _dataView?.Refresh();
            UpdateStatistics();
        }

        private void CmbSearchColumn_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Guard against null during initialization
            if (CmbSearchColumn == null || TxtSearch == null)
                return;

            _selectedSearchColumn = (CmbSearchColumn.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Columns";

            // Refresh filter if there's search text
            if (!string.IsNullOrEmpty(TxtSearch.Text))
            {
                _dataView?.Refresh();
                UpdateStatistics();
            }
        }

        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            // Debounce search to improve performance
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            _dataView?.Refresh();
            UpdateStatistics();
        }

        private void BtnClearSearch_Click(object? sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            CmbSearchColumn.SelectedIndex = 0;
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            _dataView?.Refresh();
            UpdateStatistics();
            TxtStatus.Text = "Data refreshed";
        }

        private void BtnTranspose_Click(object? sender, RoutedEventArgs e)
        {
            if (_dataView == null || _allEntries == null || !_allEntries.Any())
            {
                MessageBox.Show("No data to transpose.", "Transpose", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Get currently filtered entries
            var filteredEntries = _dataView.Cast<IndigoStripeEntry>().ToList();

            if (filteredEntries.Count == 0)
            {
                MessageBox.Show("No entries match current filters.", "Transpose", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (filteredEntries.Count > 50)
            {
                var result = MessageBox.Show(
                    $"You have {filteredEntries.Count} entries. Transpose view works best with fewer entries (up to ~50).\n\n" +
                    "Do you want to continue with the first 50 entries?",
                    "Many Entries",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                    filteredEntries = filteredEntries.GetRange(0, Math.Min(50, filteredEntries.Count));
            }

            var transposeWindow = new TransposeViewWindow();
            transposeWindow.LoadData(filteredEntries);
            WindowManager.OpenWindow(transposeWindow, this);

            TxtStatus.Text = $"Opened transpose view with {filteredEntries.Count} entries";
        }

        private void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataView == null)
                {
                    MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"StripeAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    ExportToCsv(dialog.FileName);
                    TxtStatus.Text = $"Exported to {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show($"Data exported successfully to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data:\n{ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region CSV Export

        private void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();

            // Header - all columns
            sb.AppendLine(string.Join(",",
                "Timestamp", "SpreadId", "StripeId", "SliceIndex", "LengthMm", "StripeType", "InkId",
                "SliceGroupIndex", "SliceId", "SliceStamp", "ParentSeparationId",
                "VDeveloper", "VElectrode", "VSqueegee", "VCleaner",
                "CrVDc", "CrVAc", "VAsid",
                "HvTarget", "NScanLines",
                "EmIsActive", "EmMeasureId",
                "SpmStatus", "SpmMeasureId", "SpmScanDirection", "SpmMeasureMode", "SpmNumOfStrips",
                "IlsIsActive", "IlsScanLenMm", "IlsScanMode", "IlsScanSpeedUmSec",
                "StartPosMm", "EndPosMm",
                "WebRepeatLenScalingFactor", "BlanketLoopRepeatLenMm", "BlanketLoopT2TotalLenUm",
                "FirstInBlanketLoop", "LastInBlanketLoop", "StartPosInBlanketLoopMm",
                "LastStripeInSpread", "ImageToBru", "DataTransferControl",
                "ReportPrintDetails", "ReportId", "NSliceGroups",
                "IsStationActive", "IsHvMismatch"
            ));

            // Data
            foreach (IndigoStripeEntry entry in _dataView!)
            {
                sb.AppendLine(string.Join(",",
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffffff"),
                    entry.SpreadId,
                    entry.StripeId,
                    entry.SliceIndex,
                    entry.LengthMm.ToString("F2"),
                    EscapeCsv(entry.StripeType),
                    entry.InkId,
                    entry.SliceGroupIndex,
                    entry.SliceId,
                    entry.SliceStamp,
                    entry.ParentSeparationId,
                    entry.VDeveloper,
                    entry.VElectrode,
                    entry.VSqueegee,
                    entry.VCleaner,
                    entry.CrVDc,
                    entry.CrVAc,
                    entry.VAsid,
                    EscapeCsv(entry.HvTarget),
                    entry.NScanLines,
                    entry.EmIsActive,
                    entry.EmMeasureId,
                    EscapeCsv(entry.SpmStatus),
                    entry.SpmMeasureId,
                    EscapeCsv(entry.SpmScanDirection),
                    EscapeCsv(entry.SpmMeasureMode),
                    entry.SpmNumOfStrips,
                    entry.IlsIsActive,
                    entry.IlsScanLenMm.ToString("F2"),
                    EscapeCsv(entry.IlsScanMode),
                    entry.IlsScanSpeedUmSec,
                    entry.StartPosMm.ToString("F2"),
                    entry.EndPosMm.ToString("F2"),
                    entry.WebRepeatLenScalingFactor.ToString("F4"),
                    entry.BlanketLoopRepeatLenMm.ToString("F2"),
                    entry.BlanketLoopT2TotalLenUm,
                    entry.FirstInBlanketLoop,
                    entry.LastInBlanketLoop,
                    entry.StartPosInBlanketLoopMm.ToString("F2"),
                    entry.LastStripeInSpread,
                    entry.ImageToBru,
                    EscapeCsv(entry.DataTransferControl),
                    entry.ReportPrintDetails,
                    entry.ReportId,
                    entry.NSliceGroups,
                    entry.IsStationActive,
                    entry.IsHvMismatch
                ));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        #endregion
    }
}
