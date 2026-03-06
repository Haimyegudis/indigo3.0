using System;
using System.Collections.Generic;
using System.Data;
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
    public partial class BrowseTableWindow
    {
        #region JSON Flattening

        private DataTable? FlattenJsonColumns(DataTable? sourceTable)
        {
            if (sourceTable == null || sourceTable.Rows.Count == 0)
                return sourceTable;

            // Detect JSON columns by checking first non-null value in each column
            var jsonColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn col in sourceTable.Columns)
            {
                foreach (DataRow row in sourceTable.Rows)
                {
                    var value = row[col]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && IsJson(value))
                    {
                        jsonColumns.Add(col.ColumnName);
                        break;
                    }
                }
            }

            if (jsonColumns.Count == 0)
                return sourceTable;

            _jsonColumnNames = new HashSet<string>(jsonColumns);

            // Fully flatten JSON to individual values
            return FullyFlattenJson(sourceTable, jsonColumns);
        }

        /// <summary>
        /// Fully flattens JSON columns - each JSON path becomes a column with its primitive value.
        /// Example: {"Conductivity": {"CondFactor": 1.0}} becomes column "Conductivity.CondFactor" with value "1.0"
        /// </summary>
        private DataTable FullyFlattenJson(DataTable sourceTable, ICollection<string> jsonColumns)
        {
            var resultTable = new DataTable();

            // Add non-JSON columns first, prioritizing ID
            var priorityColumnPatterns = new List<string> { "ID", "Id", "Time", "Timestamp", "DateTime", "Date" };
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add priority columns first
            foreach (var pattern in priorityColumnPatterns)
            {
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (col.ColumnName.Equals(pattern, StringComparison.OrdinalIgnoreCase) &&
                        !jsonColumns.Contains(col.ColumnName) &&
                        !addedColumns.Contains(col.ColumnName))
                    {
                        resultTable.Columns.Add(col.ColumnName, typeof(object));
                        addedColumns.Add(col.ColumnName);
                    }
                }
            }

            // Add remaining non-JSON columns
            foreach (DataColumn col in sourceTable.Columns)
            {
                if (!jsonColumns.Contains(col.ColumnName) && !addedColumns.Contains(col.ColumnName))
                {
                    resultTable.Columns.Add(col.ColumnName, typeof(object));
                    addedColumns.Add(col.ColumnName);
                }
            }

            // Collect ALL unique JSON paths from ALL rows
            var allPaths = new HashSet<string>();
            int rowIndex = 0;
            foreach (DataRow row in sourceTable.Rows)
            {
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = row[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue) && IsJson(jsonValue))
                    {
                        try
                        {
                            var obj = JObject.Load(new JsonTextReader(new System.IO.StringReader(jsonValue)) { MaxDepth = AppConstants.JsonMaxDepth });
                            // Get all leaf values (JValue) and their paths
                            var descendants = obj.Descendants().OfType<JValue>().ToList();
                            foreach (var token in descendants)
                            {
                                // Sanitize path for DataGrid binding - replace dots and brackets
                                string path = SanitizeColumnName(token.Path);
                                allPaths.Add(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Collecting JSON paths failed", ex);
                        }
                    }
                }
                rowIndex++;
            }

            // Add columns for all JSON paths (sorted)
            foreach (var path in allPaths.OrderBy(p => p))
            {
                if (!resultTable.Columns.Contains(path))
                {
                    resultTable.Columns.Add(path, typeof(object));
                }
            }

            // Fill data
            rowIndex = 0;
            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

                // Copy non-JSON values
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (!jsonColumns.Contains(col.ColumnName) && resultTable.Columns.Contains(col.ColumnName))
                    {
                        newRow[col.ColumnName] = sourceRow[col] ?? DBNull.Value;
                    }
                }

                // Extract flattened JSON values
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = sourceRow[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue) && IsJson(jsonValue))
                    {
                        try
                        {
                            var obj = JObject.Load(new JsonTextReader(new System.IO.StringReader(jsonValue)) { MaxDepth = AppConstants.JsonMaxDepth });
                            foreach (var token in obj.Descendants().OfType<JValue>())
                            {
                                // Use sanitized column name to match the column we created
                                string path = SanitizeColumnName(token.Path);
                                if (resultTable.Columns.Contains(path))
                                {
                                    newRow[path] = token.Value ?? DBNull.Value;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Extracting flattened JSON values failed", ex);
                        }
                    }
                }

                resultTable.Rows.Add(newRow);
                rowIndex++;
            }

            // Clean empty columns - remove columns that have no data
            resultTable = CleanEmptyColumns(resultTable);

            return resultTable;
        }

        /// <summary>
        /// Removes columns that are entirely empty or null
        /// </summary>
        private DataTable CleanEmptyColumns(DataTable table)
        {
            var columnsToRemove = new List<string>();

            foreach (DataColumn col in table.Columns)
            {
                bool allEmpty = true;
                foreach (DataRow row in table.Rows)
                {
                    var val = row[col];
                    if (val != DBNull.Value && !string.IsNullOrEmpty(val?.ToString()))
                    {
                        allEmpty = false;
                        break;
                    }
                }
                if (allEmpty)
                {
                    columnsToRemove.Add(col.ColumnName);
                }
            }

            foreach (var colName in columnsToRemove)
            {
                table.Columns.Remove(colName);
            }

            return table;
        }

        /// <summary>
        /// Expands JSON columns into separate columns for each first-level key.
        /// Each first-level key becomes a column, and its value is shown as formatted JSON.
        /// Example: {"Conductivity": {...}, "Level": {...}} becomes columns "Conductivity" and "Level"
        /// </summary>
        private DataTable ExpandFirstLevelJson(DataTable sourceTable, ICollection<string> jsonColumns, Dictionary<string, HashSet<string>> firstLevelKeys)
        {
            var resultTable = new DataTable();

            // Add non-JSON columns first, prioritizing ID
            var priorityColumnPatterns = new List<string> { "ID", "Id", "Time", "Timestamp", "DateTime", "Date" };
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add priority columns first
            foreach (var pattern in priorityColumnPatterns)
            {
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (col.ColumnName.Equals(pattern, StringComparison.OrdinalIgnoreCase) &&
                        !jsonColumns.Contains(col.ColumnName) &&
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
                if (!jsonColumns.Contains(col.ColumnName) && !addedColumns.Contains(col.ColumnName))
                {
                    resultTable.Columns.Add(col.ColumnName, typeof(string));
                    addedColumns.Add(col.ColumnName);
                }
            }

            // Add columns for each first-level JSON key (sorted alphabetically)
            var allFirstLevelKeys = new HashSet<string>();
            foreach (var jsonCol in jsonColumns)
            {
                foreach (var key in firstLevelKeys[jsonCol])
                {
                    allFirstLevelKeys.Add(key);
                }
            }

            foreach (var key in allFirstLevelKeys.OrderBy(k => k))
            {
                if (!resultTable.Columns.Contains(key))
                {
                    resultTable.Columns.Add(key, typeof(string));
                }
            }

            // Fill data
            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

                // Copy non-JSON values
                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (!jsonColumns.Contains(col.ColumnName) && resultTable.Columns.Contains(col.ColumnName))
                    {
                        newRow[col.ColumnName] = sourceRow[col]?.ToString() ?? "";
                    }
                }

                // Extract first-level JSON values
                foreach (var jsonCol in jsonColumns)
                {
                    var jsonValue = sourceRow[jsonCol]?.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonValue))
                    {
                        try
                        {
                            var token = JToken.Load(new JsonTextReader(new System.IO.StringReader(jsonValue)) { MaxDepth = AppConstants.JsonMaxDepth });
                            if (token is JObject obj)
                            {
                                foreach (var prop in obj.Properties())
                                {
                                    if (resultTable.Columns.Contains(prop.Name))
                                    {
                                        // Format the value as JSON with key-value pairs
                                        var formattedValue = FormatJsonValue(prop.Value);
                                        newRow[prop.Name] = formattedValue;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing first-level JSON failed", ex);
                        }
                    }
                }

                resultTable.Rows.Add(newRow);
            }

            return resultTable;
        }

        /// <summary>
        /// Formats a JSON value for display in a cell.
        /// For objects, shows key-value pairs in a readable format.
        /// </summary>
        private string FormatJsonValue(JToken token)
        {
            if (token == null)
                return "";

            switch (token.Type)
            {
                case JTokenType.Object:
                    var obj = (JObject)token;
                    var pairs = new List<string>();
                    foreach (var prop in obj.Properties())
                    {
                        var value = FormatSimpleValue(prop.Value);
                        pairs.Add($"\"{prop.Name}\": {value}");
                    }
                    return "{\n  " + string.Join(",\n  ", pairs) + "\n}";

                case JTokenType.Array:
                    var arr = (JArray)token;
                    if (arr.Count == 0)
                        return "[]";
                    if (arr.Count <= 5 && arr.All(a => a.Type != JTokenType.Object && a.Type != JTokenType.Array))
                    {
                        // Simple array - show inline
                        return "[" + string.Join(", ", arr.Select(a => FormatSimpleValue(a))) + "]";
                    }
                    return $"[{arr.Count} items]";

                default:
                    return FormatSimpleValue(token);
            }
        }

        private string FormatSimpleValue(JToken token)
        {
            if (token == null)
                return "null";

            switch (token.Type)
            {
                case JTokenType.String:
                    return $"\"{token}\"";
                case JTokenType.Boolean:
                    return token.ToString().ToLower();
                case JTokenType.Null:
                    return "null";
                case JTokenType.Object:
                    return "{...}";
                case JTokenType.Array:
                    var arr = (JArray)token;
                    return $"[{arr.Count} items]";
                default:
                    return token.ToString();
            }
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
                        catch (Exception ex)
                        {
                            AppLogger.Error("Getting JSON paths failed", ex);
                        }
                    }
                }
            }

            // Add flattened JSON columns (sorted for consistency)
            foreach (var jsonCol in jsonColumns)
            {
                var sortedPaths = allJsonPaths[jsonCol].OrderBy(p => p).ToList();
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
                        catch (Exception ex)
                        {
                            AppLogger.Error("Flattening JSON values failed", ex);
                        }
                    }
                }

                resultTable.Rows.Add(newRow);
            }

            return resultTable;
        }

        private HashSet<string> GetJsonPaths(string json)
        {
            var paths = new HashSet<string>();
            try
            {
                var token = JToken.Load(new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = AppConstants.JsonMaxDepth });
                CollectPaths(token, "", paths);
            }
            catch (Exception ex)
            {
                AppLogger.Error("GetJsonPaths parsing failed", ex);
            }
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
                var token = JToken.Load(new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = AppConstants.JsonMaxDepth });
                FlattenToken(token, "", result);
            }
            catch (Exception ex)
            {
                AppLogger.Error("FlattenJson parsing failed", ex);
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

        private void ApplyFilter()
        {
            if (_tempDbPath == null || _columnNames == null)
                return;

            string? searchText = SearchTextBox.Text?.Trim();

            // OPTIMIZATION: Use SQL query instead of DataView.RowFilter - MUCH FASTER!
            System.Threading.Tasks.Task.Run(() =>
            {
                LoadDataWithSearch(searchText ?? "");
            });
        }


        private void UpdateFilteredCount()
        {
            if (_dataTable == null)
                return;

            string? searchText = SearchTextBox?.Text?.Trim();
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

        #region Column Settings Persistence

        private void DataBrowserGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
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

        private void DataBrowserGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            // Settings will be saved on window close
        }

        private void ManageColumnsButton_Click(object? sender, RoutedEventArgs e)
        {
            var managerWindow = new ColumnManagerWindow(DataBrowserGrid);
            managerWindow.Owner = this;
            if (managerWindow.ShowDialog() == true && managerWindow.WasApplied)
            {
                // Settings will be saved on close
            }
        }

        private void ResetColumnsButton_Click(object? sender, RoutedEventArgs e)
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
                AppLogger.Error("ResetColumnsButton_Click failed", ex);
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
                var savedSettings = JsonConvert.DeserializeObject<List<DbColumnSettingsInfo>>(json, AppConstants.SafeJsonSettings);

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
                AppLogger.Error("LoadColumnSettings failed", ex);
            }
        }

        private void SaveColumnSettings()
        {
            try
            {
                if (DataBrowserGrid.Columns.Count == 0)
                    return;

                if (!Directory.Exists(AppPaths.Root))
                    Directory.CreateDirectory(AppPaths.Root);

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
                AppLogger.Error("SaveColumnSettings failed", ex);
            }
        }

        private class DbColumnSettingsInfo
        {
            public string Header { get; set; } = "";
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        #endregion
    }
}
