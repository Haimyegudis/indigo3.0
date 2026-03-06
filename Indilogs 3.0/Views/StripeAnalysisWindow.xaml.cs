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
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using WindowManager = IndiLogs_3._0.Services.WindowManager;

namespace IndiLogs_3._0.Views
{
    public partial class StripeAnalysisWindow : Window
    {
        private List<IndigoStripeEntry> _allEntries;
        private ICollectionView? _dataView;
        private readonly StripeDataParserService _parser;
        private string _selectedSearchColumn = "All Columns";

        // Debounce timer for search
        private DispatcherTimer _searchDebounceTimer;
        private const int SearchDebounceMs = 300;

        // Column order persistence
        private static readonly string ColumnOrderFilePath = AppPaths.StripeColumnOrder;

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
        public async Task LoadFromLogs(IEnumerable<LogEntry> logs)
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
        public async Task LoadFromJson(string json)
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
                case "InkId":
                    return entry.InkId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
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
                           ContainsText(entry.SpmStatus) ||
                           ContainsText(entry.StripeType) ||
                           ContainsText(entry.IlsScanMode) ||
                           ContainsText(entry.DataTransferControl) ||
                           ContainsText(entry.SpmScanDirection) ||
                           ContainsText(entry.SpmMeasureMode) ||
                           ContainsText(entry.StationStatus) ||
                           entry.SpreadId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.StripeId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.SliceIndex.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           entry.InkId.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
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
    }
}
