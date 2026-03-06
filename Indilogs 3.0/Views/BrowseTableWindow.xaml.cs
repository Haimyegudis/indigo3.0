using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IndiLogs_3._0;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow : Window
    {
        private readonly string _tableName;
        private readonly byte[] _dbBytes;
        private DataTable? _dataTable;
        private DataTable? _originalDataTable; // Stores the full data for row expansion
        private DataView? _filteredView;
        private string? _tempDbPath;
        private List<string>? _columnNames;
        private long _totalRowCount;
        private List<string>? _originalColumnOrder;
        private bool _columnsGenerated = false;
        private HashSet<string> _jsonColumnNames = new HashSet<string>();

        private string ColumnSettingsFilePath => Path.Combine(AppPaths.Root, $"{AppPaths.DbColumnPrefix}{SanitizeFileName(_tableName)}.json");

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
                        string tempPath = Path.Combine(Path.GetTempPath(), $"temp_browse_{Guid.NewGuid()}.db");
                        try
                        {
                            File.WriteAllBytes(tempPath, _dbBytes);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to write temp DB file: {ex.Message}");
                            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (Exception cleanupEx) { AppLogger.Warn($"Temp file cleanup failed: {cleanupEx.Message}"); }
                            throw;
                        }
                        _tempDbPath = tempPath;

                        using (var connection = new SqliteConnection($"Data Source={_tempDbPath}"))
                        {
                            connection.Open();

                            // Get total row count
                            using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM \"{AppConstants.EscapeSqlIdentifier(_tableName)}\"", connection))
                            {
                                _totalRowCount = (long)(cmd.ExecuteScalar() ?? 0L);
                            }

                            // Get column names for search
                            _columnNames = new List<string>();
                            using (var cmd = new SqliteCommand($"PRAGMA table_info([{AppConstants.EscapeSqlBracketId(_tableName)}])", connection))
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

                        Dispatcher.BeginInvoke(() =>
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
                DataTable? dataTable = null;
                DataTable? compactTable = null;

                using (var connection = new SqliteConnection($"Data Source={_tempDbPath}"))
                {
                    connection.Open();

                    string query;
                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        // No search - load first 10000 rows
                        if (_totalRowCount > 10000)
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{AppConstants.EscapeSqlIdentifier(_tableName)}\" LIMIT 10000";
                        }
                        else
                        {
                            query = $"SELECT rowid AS ID, * FROM \"{AppConstants.EscapeSqlIdentifier(_tableName)}\"";
                        }
                    }
                    else
                    {
                        // OPTIMIZATION: Use SQL LIKE for search with parameterized query
                        var whereConditions = new List<string>();

                        if (_columnNames == null) return;
                        foreach (var colName in _columnNames)
                        {
                            whereConditions.Add($"CAST([{AppConstants.EscapeSqlBracketId(colName)}] AS TEXT) LIKE @searchParam");
                        }

                        query = $"SELECT rowid AS ID, * FROM \"{AppConstants.EscapeSqlIdentifier(_tableName)}\" WHERE {string.Join(" OR ", whereConditions)} LIMIT 10000";
                    }

                    using (var cmd = new SqliteCommand(query, connection))
                    {
                        if (!string.IsNullOrEmpty(searchText))
                        {
                            cmd.Parameters.AddWithValue("@searchParam", $"%{searchText}%");
                        }
                        using (var reader = cmd.ExecuteReader())
                        {
                            dataTable = new DataTable();
                            for (int i = 0; i < reader.FieldCount; i++)
                                dataTable.Columns.Add(reader.GetName(i), typeof(object));
                            while (reader.Read())
                            {
                                var row = dataTable.NewRow();
                                for (int i = 0; i < reader.FieldCount; i++)
                                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                                dataTable.Rows.Add(row);
                            }
                        }
                    }

                    connection.Close();
                }

                // Store original data and create compact view with ID + DATA columns
                _originalDataTable = dataTable;
                compactTable = CreateCompactTable(dataTable);

                // Update UI on main thread
                Dispatcher.BeginInvoke(() =>
                {
                    _dataTable = compactTable;
                    _filteredView = _dataTable.DefaultView;
                    DataBrowserGrid.ItemsSource = _filteredView;

                    // Update header info
                    TableNameText.Text = $"Table: {_tableName}";

                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        RowCountText.Text = $"{_originalDataTable.Rows.Count:N0} rows (double-click DATA to expand)";
                    }
                    else
                    {
                        RowCountText.Text = $"Found {_originalDataTable.Rows.Count:N0} rows (double-click DATA to expand)";
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
                AppLogger.Error("LoadDataWithSearch failed", ex);
            }
        }

        /// <summary>
        /// Creates a compact table with just ID and DATA columns.
        /// DATA contains JSON of all other columns, with proper nested JSON parsing.
        /// </summary>
        private DataTable CreateCompactTable(DataTable sourceTable)
        {
            var compactTable = new DataTable();
            compactTable.Columns.Add("ID", typeof(long));
            compactTable.Columns.Add("DATA", typeof(string));

            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = compactTable.NewRow();

                // Get ID
                newRow["ID"] = sourceRow["ID"];

                // Create JObject from all other columns - preserve nested JSON structure
                var dataObj = new JObject();
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (col.ColumnName != "ID")
                    {
                        var value = sourceRow[col];
                        if (value == DBNull.Value || value == null)
                        {
                            dataObj[col.ColumnName] = JValue.CreateNull();
                        }
                        else
                        {
                            var strValue = value.ToString() ?? "";
                            // Check if value is already JSON - parse it to preserve structure
                            if (IsJson(strValue))
                            {
                                try
                                {
                                    dataObj[col.ColumnName] = JToken.Load(new JsonTextReader(new System.IO.StringReader(strValue)) { MaxDepth = AppConstants.JsonMaxDepth });
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.Warn($"JSON parse failed for column '{col.ColumnName}': {ex.Message}");
                                    dataObj[col.ColumnName] = JToken.FromObject(value);
                                }
                            }
                            else
                            {
                                dataObj[col.ColumnName] = JToken.FromObject(value);
                            }
                        }
                    }
                }

                // Serialize with indentation for better display in cell
                newRow["DATA"] = dataObj.ToString(Formatting.None);
                compactTable.Rows.Add(newRow);
            }

            // Mark DATA as JSON column for double-click handling
            _jsonColumnNames.Clear();
            _jsonColumnNames.Add("DATA");

            return compactTable;
        }

        private bool IsJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            return (value.StartsWith("{") && value.EndsWith("}")) ||
                   (value.StartsWith("[") && value.EndsWith("]"));
        }

        /// <summary>
        /// Sanitizes a JSON path to be a valid DataGrid column name.
        /// WPF DataGrid interprets dots as property paths, so we replace them with underscores.
        /// Also replaces brackets for array indices.
        /// </summary>
        private string SanitizeColumnName(string path)
        {
            // Replace dots with underscores (WPF interprets . as property path)
            // Replace brackets with underscores (for array indices like [0])
            return path.Replace(".", "_").Replace("[", "_").Replace("]", "");
        }

        private void CleanupTempFile()
        {
            try
            {
                if (_tempDbPath != null && File.Exists(_tempDbPath))
                {
                    File.Delete(_tempDbPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("CleanupTempFile failed", ex);
            }
        }

        private void SearchTextBox_TextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object? sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
        }

        private void DataBrowserGrid_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Get the clicked cell
            var cell = e.OriginalSource as FrameworkElement;
            while (cell != null && !(cell is DataGridCell))
            {
                cell = cell.Parent as FrameworkElement;
            }

            if (cell is DataGridCell dataGridCell)
            {
                var column = dataGridCell.Column;
                if (column == null) return;

                var columnName = column.Header?.ToString();

                // Check if this is the DATA column
                if (columnName == "DATA")
                {
                    var rowView = dataGridCell.DataContext as DataRowView;
                    if (rowView != null)
                    {
                        var jsonValue = rowView["DATA"]?.ToString();
                        var rowId = rowView["ID"]?.ToString() ?? "";

                        if (!string.IsNullOrWhiteSpace(jsonValue) && IsJson(jsonValue))
                        {
                            // Open row detail window showing data as a table
                            var detailWindow = new RowDetailWindow(jsonValue, $"{_tableName} - Row {rowId}");
                            WindowManager.OpenWindow(detailWindow, this);
                        }
                    }
                }
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            SaveColumnSettings();
            CleanupTempFile();
        }
    }
}
