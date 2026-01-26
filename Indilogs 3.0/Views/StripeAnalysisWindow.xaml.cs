using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Views
{
    public partial class StripeAnalysisWindow : Window
    {
        private List<IndigoStripeEntry> _allEntries;
        private ICollectionView _dataView;
        private readonly StripeDataParserService _parser;
        private string _selectedSearchColumn = "All Columns";

        // Debounce timer for search
        private DispatcherTimer _searchDebounceTimer;
        private const int SearchDebounceMs = 300;

        // Column order persistence
        private static readonly string ColumnOrderFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs", "stripe_column_order.json");

        public StripeAnalysisWindow()
        {
            InitializeComponent();
            _parser = new StripeDataParserService();
            _allEntries = new List<IndigoStripeEntry>();

            // Initialize search debounce timer
            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            // Load saved column settings (order + visibility)
            LoadColumnSettings();
        }

        /// <summary>
        /// Load stripe data from log entries (async for better UI responsiveness)
        /// </summary>
        public async void LoadFromLogs(IEnumerable<LogEntry> logs)
        {
            try
            {
                TxtStatus.Text = "Parsing stripe data from logs...";
                StripeDataGrid.IsEnabled = false;

                // Parse on background thread for better responsiveness
                var logsList = logs.ToList();
                _allEntries = await Task.Run(() => _parser.ParseFromLogs(logsList));

                if (_allEntries.Count == 0)
                {
                    TxtStatus.Text = "No stripe data found in logs. Looking for stripeDescriptor JSON...";
                    StripeDataGrid.IsEnabled = true;
                    MessageBox.Show(
                        "No stripe data was found in the logs.\n\n" +
                        "Make sure the logs contain stripeDescriptor JSON data.\n" +
                        "You can also paste JSON directly using the 'Load JSON' option.",
                        "No Data Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SetupDataView();
                PopulateInkFilter();
                UpdateStatistics();

                StripeDataGrid.IsEnabled = true;
                TxtStatus.Text = $"Loaded {_allEntries.Count} stripe entries";
            }
            catch (Exception ex)
            {
                StripeDataGrid.IsEnabled = true;
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading stripe data:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Load stripe data directly from JSON string (async for better UI responsiveness)
        /// </summary>
        public async void LoadFromJson(string json)
        {
            try
            {
                TxtStatus.Text = "Parsing JSON...";
                StripeDataGrid.IsEnabled = false;

                // Parse on background thread
                _allEntries = await Task.Run(() => _parser.ParseFromJson(json));

                if (_allEntries.Count == 0)
                {
                    TxtStatus.Text = "No stripe data found in JSON";
                    StripeDataGrid.IsEnabled = true;
                    return;
                }

                SetupDataView();
                PopulateInkFilter();
                UpdateStatistics();

                StripeDataGrid.IsEnabled = true;
                TxtStatus.Text = $"Loaded {_allEntries.Count} stripe entries from JSON";
            }
            catch (Exception ex)
            {
                StripeDataGrid.IsEnabled = true;
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error parsing JSON:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupDataView()
        {
            _dataView = CollectionViewSource.GetDefaultView(_allEntries);
            _dataView.Filter = FilterEntry;
            StripeDataGrid.ItemsSource = _dataView;
        }

        private void PopulateInkFilter()
        {
            CmbInkFilter.Items.Clear();
            CmbInkFilter.Items.Add(new ComboBoxItem { Content = "All" });

            var uniqueInks = _allEntries
                .Select(e => e.DisplayInk)
                .Where(i => !string.IsNullOrEmpty(i))
                .Distinct()
                .OrderBy(i => i);

            foreach (var ink in uniqueInks)
            {
                CmbInkFilter.Items.Add(new ComboBoxItem { Content = ink });
            }

            CmbInkFilter.SelectedIndex = 0;
        }

        private bool FilterEntry(object obj)
        {
            if (!(obj is IndigoStripeEntry entry))
                return false;

            // Active stations only
            if (ChkActiveOnly.IsChecked == true && !entry.IsStationActive)
                return false;

            // Stripe type filter
            var typeFilter = (CmbStripeType.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(typeFilter) && typeFilter != "All")
            {
                if (entry.StripeType != typeFilter)
                    return false;
            }

            // Ink filter
            var inkFilter = (CmbInkFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(inkFilter) && inkFilter != "All")
            {
                if (entry.DisplayInk != inkFilter)
                    return false;
            }

            // Column-specific search
            var searchText = TxtSearch.Text?.Trim();
            if (!string.IsNullOrEmpty(searchText))
            {
                bool matchFound = SearchInColumn(entry, _selectedSearchColumn, searchText);
                if (!matchFound)
                    return false;
            }

            return true;
        }

        private bool SearchInColumn(IndigoStripeEntry entry, string column, string searchText)
        {
            // Helper for safe string search
            bool ContainsText(string value) =>
                !string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;

            switch (column)
            {
                case "Spread":
                    return entry.SpreadId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Stripe":
                    return entry.StripeId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Slice":
                    return entry.SliceIndex.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Type":
                    return ContainsText(entry.StripeType);
                case "Ink":
                    return ContainsText(entry.DisplayInk);
                case "HV Target":
                    return ContainsText(entry.HvTarget);
                case "vDeveloper":
                    return entry.VDeveloper.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "vElectrode":
                    return entry.VElectrode.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "vSqueegee":
                    return entry.VSqueegee.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "vCleaner":
                    return entry.VCleaner.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "CR vDc":
                    return entry.CrVDc.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "CR vAc":
                    return entry.CrVAc.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "vAsid":
                    return entry.VAsid.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "ScanLines":
                    return entry.NScanLines.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                case "SPM Status":
                    return ContainsText(entry.SpmStatus);
                case "ILS Mode":
                    return ContainsText(entry.IlsScanMode);
                case "All Columns":
                default:
                    // Search all text and numeric fields
                    return ContainsText(entry.HvTarget) ||
                           ContainsText(entry.DisplayInk) ||
                           ContainsText(entry.SpmStatus) ||
                           ContainsText(entry.StripeType) ||
                           ContainsText(entry.IlsScanMode) ||
                           ContainsText(entry.DataTransferControl) ||
                           ContainsText(entry.SpmScanDirection) ||
                           ContainsText(entry.SpmMeasureMode) ||
                           ContainsText(entry.InkName) ||
                           ContainsText(entry.StationStatus) ||
                           entry.SpreadId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.StripeId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.SliceIndex.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.VDeveloper.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.VElectrode.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.VSqueegee.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.VCleaner.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.CrVDc.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.CrVAc.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.VAsid.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.NScanLines.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private void UpdateStatistics()
        {
            if (_dataView == null)
                return;

            // Single pass through data for efficiency
            int totalCount = 0;
            int printCount = 0;
            int nullCount = 0;
            var uniqueStripes = new HashSet<(int, int)>();
            double totalLengthMm = 0;

            foreach (IndigoStripeEntry entry in _dataView)
            {
                totalCount++;
                if (entry.StripeType == "Print-Image") printCount++;
                else if (entry.StripeType == "Null-Gap") nullCount++;

                // Track unique stripes for length calculation
                var key = (entry.SpreadId, entry.StripeId);
                if (!uniqueStripes.Contains(key))
                {
                    uniqueStripes.Add(key);
                    totalLengthMm += entry.LengthMm;
                }
            }

            TxtTotalEntries.Text = totalCount.ToString("N0");
            TxtPrintStripes.Text = printCount.ToString("N0");
            TxtNullStripes.Text = nullCount.ToString("N0");
            TxtTotalLength.Text = (totalLengthMm / 1000.0).ToString("N2");
        }

        #region Column Settings Persistence

        private void LoadColumnSettings()
        {
            try
            {
                if (!File.Exists(ColumnOrderFilePath))
                    return;

                var json = File.ReadAllText(ColumnOrderFilePath);
                var savedSettings = JsonConvert.DeserializeObject<List<ColumnSettingsInfo>>(json);

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
                System.Diagnostics.Debug.WriteLine($"Error loading column settings: {ex.Message}");
            }
        }

        private void SaveColumnSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(ColumnOrderFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var columnSettings = StripeDataGrid.Columns.Select(c => new ColumnSettingsInfo
                {
                    Header = c.Header.ToString(),
                    DisplayIndex = c.DisplayIndex,
                    Width = c.ActualWidth > 0 ? c.ActualWidth : c.Width.Value,
                    IsVisible = c.Visibility == Visibility.Visible
                }).ToList();

                var json = JsonConvert.SerializeObject(columnSettings, Formatting.Indented);
                File.WriteAllText(ColumnOrderFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving column settings: {ex.Message}");
            }
        }

        private class ColumnSettingsInfo
        {
            public string Header { get; set; }
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        private void StripeDataGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
            TxtStatus.Text = "Column order saved";
        }

        private void BtnManageColumns_Click(object sender, RoutedEventArgs e)
        {
            var managerWindow = new ColumnManagerWindow(StripeDataGrid);
            managerWindow.Owner = this;
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                SaveColumnSettings();
                TxtStatus.Text = "Column visibility updated";
            }
        }

        private void BtnResetColumns_Click(object sender, RoutedEventArgs e)
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

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveColumnSettings();
        }

        #endregion

        #region Event Handlers

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _dataView?.Refresh();
            UpdateStatistics();
        }

        private void CmbSearchColumn_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce search to improve performance
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            _dataView?.Refresh();
            UpdateStatistics();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            CmbSearchColumn.SelectedIndex = 0;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _dataView?.Refresh();
            UpdateStatistics();
            TxtStatus.Text = "Data refreshed";
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
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
                "Timestamp", "SpreadId", "StripeId", "SliceIndex", "LengthMm", "StripeType", "InkName",
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
            foreach (IndigoStripeEntry entry in _dataView)
            {
                sb.AppendLine(string.Join(",",
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    entry.SpreadId,
                    entry.StripeId,
                    entry.SliceIndex,
                    entry.LengthMm.ToString("F2"),
                    EscapeCsv(entry.StripeType),
                    EscapeCsv(entry.InkName),
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
