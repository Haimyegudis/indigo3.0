using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow : Window
    {
        private readonly string _tableName;
        private readonly byte[] _dbBytes;
        private DataTable _dataTable;
        private DataView _filteredView;
        private string _tempDbPath;
        private List<string> _columnNames;
        private long _totalRowCount;
        private List<string> _originalColumnOrder;
        private bool _columnsGenerated = false;
        private HashSet<string> _jsonColumnNames = new HashSet<string>();

        private static readonly string ColumnSettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3", "DbColumnSettings");

        private string ColumnSettingsFilePath => Path.Combine(ColumnSettingsFolder, $"{SanitizeFileName(_tableName)}.json");

        public BrowseTableWindow(string tableName, byte[] dbBytes)
        {
            InitializeComponent();
            _tableName = tableName;
            _dbBytes = dbBytes;
            LoadTableData();
        }

        private string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        }

        private void LoadTableData()
        {
            try
            {
                // OPTIMIZATION: Load data asynchronously using SQL query directly
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // Create a temporary database file (keep it for searches)
                        _tempDbPath = Path.Combine(Path.GetTempPath(), $"temp_browse_{Guid.NewGuid()}.db");
                        File.WriteAllBytes(_tempDbPath, _dbBytes);

                        using (var connection = new SQLiteConnection($"Data Source={_tempDbPath};Version=3;"))
                        {
                            connection.Open();

                            // Get total row count
                            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM \"{_tableName}\"", connection))
                            {
                                _totalRowCount = (long)cmd.ExecuteScalar();
                            }

                            // Get column names for search
                            _columnNames = new List<string>();
                            using (var cmd = new SQLiteCommand($"PRAGMA table_info([{_tableName}])", connection))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    _columnNames.Add(reader.GetString(1)); // Column name is at index 1
                                }
                            }

                            connection.Close();
                        }

                        // Load initial data (empty search = all rows, limited to 10000)
                        LoadDataWithSearch("");
                    }
                    catch (Exception ex)
                    {
                        // Clean up temp file on error
                        CleanupTempFile();

                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Error loading table data: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting table load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadDataWithSearch(string searchText)
        {
            try
            {
                DataTable dataTable = null;

                using (var connection = new SQLiteConnection($"Data Source={_tempDbPath};Version=3;"))
                {
                    connection.Open();

                    string query;
                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        // No search - load first 10000 rows
                        if (_totalRowCount > 10000)
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{_tableName}\" LIMIT 10000";
                            System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Loading first 10000 of {_totalRowCount} rows");
                        }
                        else
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{_tableName}\"";
                        }
                    }
                    else
                    {
                        // OPTIMIZATION: Use SQL LIKE for search - MUCH faster than DataView.RowFilter
                        var whereConditions = new List<string>();
                        string escapedSearch = searchText.Replace("'", "''");

                        foreach (var colName in _columnNames)
                        {
                            // Use CAST to convert all columns to text for searching
                            whereConditions.Add($"CAST([{colName}] AS TEXT) LIKE '%{escapedSearch}%'");
                        }

                        query = $"SELECT rowid AS ID, * FROM \"{_tableName}\" WHERE {string.Join(" OR ", whereConditions)} LIMIT 10000";
                        System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Searching for '{searchText}'");
                    }

                    using (var adapter = new SQLiteDataAdapter(query, connection))
                    {
                        dataTable = new DataTable();
                        adapter.Fill(dataTable);
                    }

                    connection.Close();
                }

                System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Loaded {dataTable.Rows.Count} rows from SQL");
                if (dataTable.Rows.Count > 0)
                {
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        var firstVal = dataTable.Rows[0][col]?.ToString() ?? "(null)";
                        System.Diagnostics.Debug.WriteLine($"[DB BROWSE] Col '{col.ColumnName}' first value length: {firstVal.Length}");
                    }
                }

                // Flatten JSON columns
                var flattenedTable = FlattenJsonColumns(dataTable);

                // Update UI on main thread
                Dispatcher.Invoke(() =>
                {
                    _dataTable = flattenedTable;
                    _filteredView = _dataTable.DefaultView;
                    DataBrowserGrid.ItemsSource = _filteredView;

                    // Update header info
                    TableNameText.Text = $"Table: {_tableName}";

                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        RowCountText.Text = $"{_dataTable.Rows.Count:N0} rows × {_dataTable.Columns.Count} columns";
                    }
                    else
                    {
                        RowCountText.Text = $"Found {_dataTable.Rows.Count:N0} rows × {_dataTable.Columns.Count} columns";
                    }

                    UpdateFilteredCount();

                    // Apply saved column settings after first load
                    if (!_columnsGenerated)
                    {
                        _columnsGenerated = true;
                        // Defer column settings application to after columns are generated
                        Dispatcher.BeginInvoke(new Action(LoadColumnSettings), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB BROWSE ERROR] {ex.Message}");
            }
        }

        #region JSON Flattening

        private DataTable FlattenJsonColumns(DataTable sourceTable)
        {
            if (sourceTable == null || sourceTable.Rows.Count == 0)
                return sourceTable;

            System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN] Source table has {sourceTable.Rows.Count} rows, {sourceTable.Columns.Count} columns");

            // Detect JSON columns by checking first non-null value in each column
            var jsonColumns = new List<string>();
            foreach (DataColumn col in sourceTable.Columns)
            {
                foreach (DataRow row in sourceTable.Rows)
                {
                    var value = row[col]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && IsJson(value))
                    {
                        jsonColumns.Add(col.ColumnName);
                        System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN] Detected JSON column: '{col.ColumnName}'");
                        break;
                    }
                }
            }

            if (jsonColumns.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[JSON FLATTEN] No JSON columns detected");
                return sourceTable;
            }

            _jsonColumnNames = new HashSet<string>(jsonColumns);

            // Collect all unique FIRST-LEVEL keys from all rows
            var firstLevelKeys = new Dictionary<string, HashSet<string>>(); // columnName -> keys
            foreach (var jsonCol in jsonColumns)
            {
                firstLevelKeys[jsonCol] = new HashSet<string>();
            }

            foreach (DataRow row in sourceTable.Rows)
            {
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = row[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue))
                    {
                        try
                        {
                            var token = JToken.Parse(jsonValue);
                            if (token is JObject obj)
                            {
                                foreach (var prop in obj.Properties())
                                {
                                    firstLevelKeys[jsonCol].Add(prop.Name);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // Expand first-level keys as columns, with their values as formatted JSON
            return ExpandFirstLevelJson(sourceTable, jsonColumns, firstLevelKeys);
        }

        private DataTable FormatJsonColumns(DataTable sourceTable, List<string> jsonColumns)
        {
            // Just format JSON for readability, don't expand into columns
            var resultTable = sourceTable.Clone();

            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

                foreach (DataColumn col in sourceTable.Columns)
                {
                    var value = sourceRow[col]?.ToString() ?? "";

                    if (jsonColumns.Contains(col.ColumnName) && IsJson(value))
                    {
                        try
                        {
                            // Format JSON with indentation for readability
                            var token = JToken.Parse(value);
                            newRow[col] = token.ToString(Formatting.Indented);
                        }
                        catch
                        {
                            newRow[col] = value;
                        }
                    }
                    else
                    {
                        newRow[col] = value;
                    }
                }

                resultTable.Rows.Add(newRow);
            }

            return resultTable;
        }

        private DataTable FlattenStructuredJson(DataTable sourceTable, List<string> jsonColumns, Dictionary<string, HashSet<string>> firstLevelKeys)
        {
            // Create new DataTable with flattened columns
            var resultTable = new DataTable();

            // Add non-JSON columns first, prioritizing ID and time-related columns
            var priorityColumnPatterns = new List<string> { "ID", "Time", "Timestamp", "DateTime", "Date" };
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add priority columns first
            foreach (var pattern in priorityColumnPatterns)
            {
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (col.ColumnName.Equals(pattern, StringComparison.OrdinalIgnoreCase) &&
                        !jsonColumns.Any(j => j.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase)) &&
                        !addedColumns.Contains(col.ColumnName))
                    {
                        resultTable.Columns.Add(col.ColumnName, typeof(string));
                        addedColumns.Add(col.ColumnName);
                    }
                }
            }

            // Add remaining non-JSON columns
            foreach (DataColumn col in sourceTable.Columns)
            {
                bool isJsonCol = jsonColumns.Any(j => j.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!isJsonCol && !addedColumns.Contains(col.ColumnName))
                {
                    resultTable.Columns.Add(col.ColumnName, typeof(string));
                    addedColumns.Add(col.ColumnName);
                }
            }

            // Collect ALL unique paths from ALL rows (not just sample)
            var allJsonPaths = new Dictionary<string, HashSet<string>>();
            foreach (var jsonCol in jsonColumns)
            {
                allJsonPaths[jsonCol] = new HashSet<string>();
            }

            foreach (DataRow row in sourceTable.Rows)
            {
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = row[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue))
                    {
                        try
                        {
                            var paths = GetJsonPaths(jsonValue);
                            foreach (var path in paths)
                            {
                                allJsonPaths[jsonCol].Add(path);
                            }
                        }
                        catch { }
                    }
                }
            }

            // Add flattened JSON columns (sorted for consistency)
            foreach (var jsonCol in jsonColumns)
            {
                var sortedPaths = allJsonPaths[jsonCol].OrderBy(p => p).ToList();
                System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN] Column '{jsonCol}' has {sortedPaths.Count} paths");
                foreach (var path in sortedPaths)
                {
                    var columnName = $"{jsonCol}.{path}";
                    if (!resultTable.Columns.Contains(columnName))
                    {
                        resultTable.Columns.Add(columnName, typeof(string));
                    }
                }
            }

            // Fill data
            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

                // Copy non-JSON values
                foreach (DataColumn col in sourceTable.Columns)
                {
                    bool isJsonCol = jsonColumns.Any(j => j.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase));
                    if (!isJsonCol)
                    {
                        var resultCol = resultTable.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c => c.ColumnName.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase));
                        if (resultCol != null)
                        {
                            newRow[resultCol.ColumnName] = sourceRow[col]?.ToString() ?? "";
                        }
                    }
                }

                // Extract and flatten JSON values
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = sourceRow[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue))
                    {
                        try
                        {
                            var flatValues = FlattenJson(jsonValue);
                            foreach (var kvp in flatValues)
                            {
                                var columnName = $"{jsonCol}.{kvp.Key}";
                                if (resultTable.Columns.Contains(columnName))
                                {
                                    newRow[columnName] = kvp.Value ?? "";
                                }
                            }
                        }
                        catch { }
                    }
                }

                resultTable.Rows.Add(newRow);
            }

            System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN] Result table has {resultTable.Columns.Count} columns, {resultTable.Rows.Count} rows");
            return resultTable;
        }

        private bool IsJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            return (value.StartsWith("{") && value.EndsWith("}")) ||
                   (value.StartsWith("[") && value.EndsWith("]"));
        }

        private HashSet<string> GetJsonPaths(string json)
        {
            var paths = new HashSet<string>();
            try
            {
                var token = JToken.Parse(json);
                CollectPaths(token, "", paths);
            }
            catch { }
            return paths;
        }

        private void CollectPaths(JToken token, string prefix, HashSet<string> paths)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        var newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                        CollectPaths(prop.Value, newPrefix, paths);
                    }
                    break;

                case JTokenType.Array:
                    // For arrays, just store the path with array indicator
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        paths.Add(prefix);
                    }
                    break;

                default:
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        paths.Add(prefix);
                    }
                    break;
            }
        }

        private Dictionary<string, string> FlattenJson(string json)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var token = JToken.Parse(json);
                FlattenToken(token, "", result);
                if (result.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN WARNING] FlattenJson returned 0 results for JSON of length {json.Length}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JSON FLATTEN ERROR] FlattenJson failed: {ex.Message}");
            }
            return result;
        }

        private void FlattenToken(JToken token, string prefix, Dictionary<string, string> result)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        var newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                        FlattenToken(prop.Value, newPrefix, result);
                    }
                    break;

                case JTokenType.Array:
                    var arr = (JArray)token;
                    // For arrays, show count or first few values
                    if (arr.Count > 0 && arr[0].Type == JTokenType.Object)
                    {
                        result[prefix] = $"[{arr.Count} items]";
                    }
                    else
                    {
                        // For simple arrays, show values
                        var values = arr.Select(v => v.ToString()).Take(5);
                        var display = string.Join(", ", values);
                        if (arr.Count > 5) display += "...";
                        result[prefix] = display;
                    }
                    break;

                default:
                    result[prefix] = token.ToString();
                    break;
            }
        }

        #endregion

        private void CleanupTempFile()
        {
            try
            {
                if (_tempDbPath != null && File.Exists(_tempDbPath))
                {
                    File.Delete(_tempDbPath);
                }
            }
            catch { }
        }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
        }

        private void ApplyFilter()
        {
            if (_tempDbPath == null || _columnNames == null)
                return;

            string searchText = SearchTextBox.Text?.Trim();

            // OPTIMIZATION: Use SQL query instead of DataView.RowFilter - MUCH FASTER!
            System.Threading.Tasks.Task.Run(() =>
            {
                LoadDataWithSearch(searchText);
            });
        }


        private void UpdateFilteredCount()
        {
            if (_dataTable == null)
                return;

            string searchText = SearchTextBox?.Text?.Trim();
            int currentCount = _dataTable.Rows.Count;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // No search active
                if (_totalRowCount > 10000)
                {
                    FilteredCountText.Text = $"Showing first 10,000 of {_totalRowCount:N0} total rows";
                }
                else
                {
                    FilteredCountText.Text = "";
                }
            }
            else
            {
                // Search active
                FilteredCountText.Text = $"Found {currentCount:N0} matching rows (limited to 10,000)";
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataTable == null || _dataTable.Rows.Count == 0)
                {
                    MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"{_tableName}.csv",
                    DefaultExt = ".csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    ExportToCsv(dialog.FileName);

                    MessageBox.Show($"{_dataTable.Rows.Count} rows exported successfully to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();

            // Write headers
            var headers = _dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName);
            sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsvValue(h))));

            // Write currently displayed rows
            foreach (DataRow row in _dataTable.Rows)
            {
                var values = row.ItemArray.Select(val => EscapeCsvValue(val?.ToString() ?? ""));
                sb.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            SaveColumnSettings();
            CleanupTempFile();
        }

        #region Column Settings Persistence

        private void DataBrowserGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            // Store original column order
            if (_originalColumnOrder == null)
                _originalColumnOrder = new List<string>();

            _originalColumnOrder.Add(e.PropertyName);

            // Move ID column to first position
            if (e.PropertyName == "ID")
            {
                e.Column.DisplayIndex = 0;
            }
        }

        private void DataBrowserGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            // Settings will be saved on window close
        }

        private void ManageColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            var managerWindow = new ColumnManagerWindow(DataBrowserGrid);
            managerWindow.Owner = this;
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                // Settings will be saved on close
            }
        }

        private void ResetColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Delete saved settings
                if (File.Exists(ColumnSettingsFilePath))
                    File.Delete(ColumnSettingsFilePath);

                // Reset column order and visibility
                for (int i = 0; i < DataBrowserGrid.Columns.Count; i++)
                {
                    DataBrowserGrid.Columns[i].DisplayIndex = i;
                    DataBrowserGrid.Columns[i].Visibility = Visibility.Visible;
                }

                // Move ID to first if it exists
                var idColumn = DataBrowserGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "ID");
                if (idColumn != null)
                    idColumn.DisplayIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting columns: {ex.Message}");
            }
        }

        private void LoadColumnSettings()
        {
            try
            {
                if (!File.Exists(ColumnSettingsFilePath))
                {
                    // Just ensure ID is first
                    var idColumn = DataBrowserGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "ID");
                    if (idColumn != null)
                        idColumn.DisplayIndex = 0;
                    return;
                }

                var json = File.ReadAllText(ColumnSettingsFilePath);
                var savedSettings = JsonConvert.DeserializeObject<List<DbColumnSettingsInfo>>(json);

                if (savedSettings == null || savedSettings.Count == 0)
                    return;

                // Apply saved settings
                foreach (var col in DataBrowserGrid.Columns)
                {
                    var header = col.Header?.ToString();
                    var saved = savedSettings.FirstOrDefault(s => s.Header == header);
                    if (saved != null)
                    {
                        col.DisplayIndex = Math.Min(saved.DisplayIndex, DataBrowserGrid.Columns.Count - 1);
                        col.Width = new DataGridLength(saved.Width);
                        col.Visibility = saved.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
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
                if (DataBrowserGrid.Columns.Count == 0)
                    return;

                if (!Directory.Exists(ColumnSettingsFolder))
                    Directory.CreateDirectory(ColumnSettingsFolder);

                var columnSettings = DataBrowserGrid.Columns.Select(c => new DbColumnSettingsInfo
                {
                    Header = c.Header?.ToString() ?? "",
                    DisplayIndex = c.DisplayIndex,
                    Width = c.ActualWidth > 0 ? c.ActualWidth : c.Width.Value,
                    IsVisible = c.Visibility == Visibility.Visible
                }).ToList();

                var json = JsonConvert.SerializeObject(columnSettings, Formatting.Indented);
                File.WriteAllText(ColumnSettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving column settings: {ex.Message}");
            }
        }

        private class DbColumnSettingsInfo
        {
            public string Header { get; set; }
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        #endregion
    }
}
