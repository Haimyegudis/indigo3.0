using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Models;
using Microsoft.Win32;

namespace IndiLogs_3._0.Views
{
    public partial class TransposeViewWindow : Window
    {
        private DataTable _transposeTable;
        private DataTable _displayTable; // Filtered table for display
        private List<IndigoStripeEntry> _allEntries;
        private List<IndigoStripeEntry> _filteredEntries;
        private HashSet<string> _hiddenProperties;
        private HashSet<int> _availableSpreads;

        // Properties to show in transpose view (in order) - Time removed as requested
        private static readonly List<(string Name, Func<IndigoStripeEntry, string> Getter)> PropertyList = new List<(string, Func<IndigoStripeEntry, string>)>
        {
            ("Type", e => e.StripeType),
            ("Spread", e => e.SpreadId.ToString()),
            ("Stripe", e => e.StripeId.ToString()),
            ("Slice", e => e.SliceIndex.ToString()),
            ("Group", e => e.SliceGroupIndex.ToString()),
            ("SliceId", e => e.SliceId.ToString()),
            ("InkId", e => e.InkId.ToString()),
            ("Stamp", e => e.SliceStamp.ToString()),
            ("Length(mm)", e => e.LengthMm.ToString("N2")),
            ("ScanLines", e => e.NScanLines.ToString()),
            ("vDev", e => e.VDeveloper.ToString()),
            ("vElec", e => e.VElectrode.ToString()),
            ("vSqg", e => e.VSqueegee.ToString()),
            ("vCln", e => e.VCleaner.ToString()),
            ("CRvDc", e => e.CrVDc.ToString()),
            ("CRvAc", e => e.CrVAc.ToString()),
            ("vAsid", e => e.VAsid.ToString()),
            ("HV Target", e => e.HvTarget ?? ""),
            ("LastInSprd", e => e.LastStripeInSpread.ToString()),
            ("NGroups", e => e.NSliceGroups.ToString()),
            ("BL Start(mm)", e => e.StartPosInBlanketLoopMm.ToString("N2")),
            ("SepId", e => e.ParentSeparationId.ToString()),
            ("EM Active", e => e.EmIsActive.ToString()),
            ("EM MeasId", e => e.EmMeasureId.ToString()),
            ("SPM", e => e.SpmStatus ?? ""),
            ("SPM MeasId", e => e.SpmMeasureId.ToString()),
            ("SPM Dir", e => e.SpmScanDirection ?? ""),
            ("SPM Mode", e => e.SpmMeasureMode ?? ""),
            ("SPM Strips", e => e.SpmNumOfStrips.ToString()),
            ("ILS Active", e => e.IlsIsActive.ToString()),
            ("ILS Len(mm)", e => e.IlsScanLenMm.ToString("N2")),
            ("ILS Mode", e => e.IlsScanMode ?? ""),
            ("ILS Speed", e => e.IlsScanSpeedUmSec.ToString()),
            ("Start(mm)", e => e.StartPosMm.ToString("N2")),
            ("End(mm)", e => e.EndPosMm.ToString("N2")),
            ("WebScale", e => e.WebRepeatLenScalingFactor.ToString("N6")),
            ("BL Repeat(mm)", e => e.BlanketLoopRepeatLenMm.ToString("N2")),
            ("BL T2Tot", e => e.BlanketLoopT2TotalLenUm.ToString()),
            ("1st BL", e => e.FirstInBlanketLoop.ToString()),
            ("Last BL", e => e.LastInBlanketLoop.ToString()),
            ("ImgToBru", e => e.ImageToBru.ToString()),
            ("DataXfer", e => e.DataTransferControl ?? ""),
            ("Report", e => e.ReportPrintDetails.ToString()),
            ("ReportId", e => e.ReportId.ToString()),
            ("Status", e => e.StationStatus),
            ("LenNS(mm)", e => e.LengthNotScaledMm.ToString("N2")),
        };

        public TransposeViewWindow()
        {
            InitializeComponent();
            _hiddenProperties = new HashSet<string>();
            _availableSpreads = new HashSet<int>();
        }

        public void LoadData(IEnumerable<IndigoStripeEntry> entries)
        {
            _allEntries = entries.ToList();
            _filteredEntries = _allEntries;

            // Extract available spreads for filter
            _availableSpreads = new HashSet<int>(_allEntries.Select(e => e.SpreadId).Distinct());
            PopulateSpreadFilter();

            BuildTransposeTableAsync();
        }

        private void PopulateSpreadFilter()
        {
            CmbSingleSpread.Items.Clear();
            CmbSingleSpread.Items.Add(new ComboBoxItem { Content = "All", Tag = -1 });

            foreach (var spread in _availableSpreads.OrderBy(s => s))
            {
                CmbSingleSpread.Items.Add(new ComboBoxItem { Content = spread.ToString(), Tag = spread });
            }

            CmbSingleSpread.SelectedIndex = 0;
        }

        private async void BuildTransposeTableAsync()
        {
            if (_filteredEntries == null || _filteredEntries.Count == 0)
            {
                TxtInfo.Text = "No entries to display";
                return;
            }

            // Show loading for large datasets
            bool showLoading = _filteredEntries.Count > 100;
            if (showLoading)
            {
                LoadingPanel.Visibility = Visibility.Visible;
                TxtLoading.Text = $"Loading {_filteredEntries.Count} entries...";
                TransposeGrid.IsEnabled = false;
            }

            try
            {
                // Build table on background thread for performance
                var table = await Task.Run(() => BuildTransposeTableInternal());

                // Update UI on main thread
                _transposeTable = table;

                // Apply row filter by creating filtered display table
                ApplyRowFilter();

                SetupDataGrid();
                TransposeGrid.ItemsSource = _displayTable.DefaultView;

                UpdateInfo();
            }
            finally
            {
                if (showLoading)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    TransposeGrid.IsEnabled = true;
                }
            }
        }

        private void ApplyRowFilter()
        {
            // Create a new table with only visible rows
            _displayTable = _transposeTable.Clone();

            foreach (DataRow row in _transposeTable.Rows)
            {
                var propertyName = row["Property"]?.ToString();
                if (!_hiddenProperties.Contains(propertyName))
                {
                    _displayTable.ImportRow(row);
                }
            }
        }

        private void RefreshDisplay()
        {
            if (_transposeTable == null) return;

            ApplyRowFilter();
            SetupDataGrid();
            TransposeGrid.ItemsSource = _displayTable.DefaultView;
            UpdateInfo();
        }

        private DataTable BuildTransposeTableInternal()
        {
            var table = new DataTable();

            // First column: Property name
            table.Columns.Add("Property", typeof(string));

            // Add one column for each entry - using just spread number as header
            for (int i = 0; i < _filteredEntries.Count; i++)
            {
                var entry = _filteredEntries[i];
                // Column header: just the spread number
                string colName = $"S{entry.SpreadId}";

                // Handle duplicate column names by adding index
                int suffix = 1;
                string originalColName = colName;
                while (table.Columns.Contains(colName))
                {
                    colName = $"{originalColName}_{suffix++}";
                }

                table.Columns.Add(colName, typeof(string));
            }

            // Add rows for each property (pre-compute getters for performance)
            var propertyGetters = PropertyList.ToArray();

            foreach (var prop in propertyGetters)
            {
                var row = table.NewRow();
                row["Property"] = prop.Name;

                for (int i = 0; i < _filteredEntries.Count; i++)
                {
                    try
                    {
                        row[i + 1] = prop.Getter(_filteredEntries[i]);
                    }
                    catch
                    {
                        row[i + 1] = "";
                    }
                }

                table.Rows.Add(row);
            }

            return table;
        }

        private void SetupDataGrid()
        {
            TransposeGrid.Columns.Clear();

            // Property column (frozen)
            var propColumn = new DataGridTextColumn
            {
                Header = "Property",
                Binding = new System.Windows.Data.Binding("[Property]"),
                Width = 100,
                FontWeight = FontWeights.Bold
            };
            TransposeGrid.Columns.Add(propColumn);

            // Entry columns - limit column width for better performance with many columns
            int colWidth = _filteredEntries.Count > 50 ? 70 : 90;

            for (int i = 1; i < _transposeTable.Columns.Count; i++)
            {
                var col = new DataGridTextColumn
                {
                    Header = _transposeTable.Columns[i].ColumnName,
                    Binding = new System.Windows.Data.Binding($"[{i}]"), // Use index-based binding for performance
                    Width = colWidth
                };
                TransposeGrid.Columns.Add(col);
            }
        }

        private void UpdateInfo()
        {
            int visibleProperties = PropertyList.Count - _hiddenProperties.Count;
            TxtInfo.Text = $"{_filteredEntries.Count} entries, {visibleProperties}/{PropertyList.Count} properties";
            TxtStatus.Text = $"Spreads: {string.Join(", ", _filteredEntries.Select(e => e.SpreadId).Distinct().OrderBy(s => s).Take(10))}" +
                             (_filteredEntries.Select(e => e.SpreadId).Distinct().Count() > 10 ? "..." : "");
        }

        #region Spread Filter

        private void BtnApplySpreadFilter_Click(object sender, RoutedEventArgs e)
        {
            ApplySpreadFilter();
        }

        private void BtnShowAll_Click(object sender, RoutedEventArgs e)
        {
            TxtSpreadFrom.Text = "";
            TxtSpreadTo.Text = "";
            CmbSingleSpread.SelectedIndex = 0;
            _filteredEntries = _allEntries;
            BuildTransposeTableAsync();
        }

        private void ApplySpreadFilter()
        {
            // Check single spread first
            var selectedItem = CmbSingleSpread.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Tag is int singleSpread && singleSpread >= 0)
            {
                _filteredEntries = _allEntries.Where(e => e.SpreadId == singleSpread).ToList();
                TxtSpreadFrom.Text = "";
                TxtSpreadTo.Text = "";
            }
            else
            {
                // Range filter
                int? fromSpread = null;
                int? toSpread = null;

                if (int.TryParse(TxtSpreadFrom.Text.Trim(), out int from))
                    fromSpread = from;
                if (int.TryParse(TxtSpreadTo.Text.Trim(), out int to))
                    toSpread = to;

                if (fromSpread.HasValue || toSpread.HasValue)
                {
                    _filteredEntries = _allEntries.Where(e =>
                    {
                        if (fromSpread.HasValue && e.SpreadId < fromSpread.Value)
                            return false;
                        if (toSpread.HasValue && e.SpreadId > toSpread.Value)
                            return false;
                        return true;
                    }).ToList();
                }
                else
                {
                    _filteredEntries = _allEntries;
                }
            }

            if (_filteredEntries.Count == 0)
            {
                MessageBox.Show("No entries match the filter criteria.", "Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                _filteredEntries = _allEntries;
                return;
            }

            BuildTransposeTableAsync();
        }

        #endregion

        #region Row Visibility (Hide Rows)

        private void BtnManageRows_Click(object sender, RoutedEventArgs e)
        {
            // Create a simple dialog to select which rows to hide
            var dialog = new Window
            {
                Title = "Hide/Show Property Rows",
                Width = 350,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (System.Windows.Media.Brush)FindResource("BgDark"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
            };

            var mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new TextBlock
            {
                Text = "Check properties to show, uncheck to hide:",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.Bold
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Scrollable list of checkboxes
            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stackPanel = new StackPanel();
            var checkBoxes = new Dictionary<string, CheckBox>();

            foreach (var prop in PropertyList)
            {
                var cb = new CheckBox
                {
                    Content = prop.Name,
                    IsChecked = !_hiddenProperties.Contains(prop.Name),
                    Margin = new Thickness(0, 3, 0, 3),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
                };
                checkBoxes[prop.Name] = cb;
                stackPanel.Children.Add(cb);
            }

            scrollViewer.Content = stackPanel;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var btnSelectAll = new Button { Content = "Select All", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
            btnSelectAll.Click += (s, args) =>
            {
                foreach (var cb in checkBoxes.Values)
                    cb.IsChecked = true;
            };

            var btnSelectNone = new Button { Content = "Select None", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
            btnSelectNone.Click += (s, args) =>
            {
                foreach (var cb in checkBoxes.Values)
                    cb.IsChecked = false;
            };

            var btnOk = new Button { Content = "OK", Width = 70, Margin = new Thickness(10, 0, 5, 0) };
            btnOk.Click += (s, args) =>
            {
                _hiddenProperties.Clear();
                foreach (var kvp in checkBoxes)
                {
                    if (kvp.Value.IsChecked != true)
                        _hiddenProperties.Add(kvp.Key);
                }
                RefreshDisplay();
                dialog.Close();
            };

            var btnCancel = new Button { Content = "Cancel", Width = 70 };
            btnCancel.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(btnSelectAll);
            buttonPanel.Children.Add(btnSelectNone);
            buttonPanel.Children.Add(btnOk);
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;
            dialog.ShowDialog();
        }

        #endregion

        #region Export

        private void BtnExportTranspose_Click(object sender, RoutedEventArgs e)
        {
            if (_transposeTable == null || _transposeTable.Rows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"PrintAnalysis_Transpose_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportToCsv(dialog.FileName);
                    MessageBox.Show($"Exported to:\n{dialog.FileName}", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting:\n{ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();

            // Header
            var headers = new List<string>();
            foreach (DataColumn col in _transposeTable.Columns)
            {
                headers.Add(EscapeCsv(col.ColumnName.Replace("\n", " ")));
            }
            sb.AppendLine(string.Join(",", headers));

            // Data - only export visible rows
            foreach (DataRow row in _transposeTable.Rows)
            {
                var propertyName = row["Property"]?.ToString();
                if (_hiddenProperties.Contains(propertyName))
                    continue;

                var values = new List<string>();
                foreach (var item in row.ItemArray)
                {
                    values.Add(EscapeCsv(item?.ToString() ?? ""));
                }
                sb.AppendLine(string.Join(",", values));
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
