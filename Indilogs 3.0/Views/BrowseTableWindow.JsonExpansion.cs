using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IndiLogs_3._0;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Views
{
    public partial class BrowseTableWindow
    {
        private DataTable ExpandFirstLevelJson(DataTable sourceTable, ICollection<string> jsonColumns, Dictionary<string, HashSet<string>> firstLevelKeys)
        {
            var resultTable = new DataTable();

            var priorityColumnPatterns = new List<string> { "ID", "Id", "Time", "Timestamp", "DateTime", "Date" };
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            foreach (DataColumn col in sourceTable.Columns)
            {
                if (!jsonColumns.Contains(col.ColumnName) && !addedColumns.Contains(col.ColumnName))
                {
                    resultTable.Columns.Add(col.ColumnName, typeof(string));
                    addedColumns.Add(col.ColumnName);
                }
            }

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

            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

                foreach (DataColumn col in sourceTable.Columns)
                {
                    if (!jsonColumns.Contains(col.ColumnName) && resultTable.Columns.Contains(col.ColumnName))
                    {
                        newRow[col.ColumnName] = sourceRow[col]?.ToString() ?? "";
                    }
                }

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
            var resultTable = new DataTable();

            var priorityColumnPatterns = new List<string> { "ID", "Time", "Timestamp", "DateTime", "Date" };
            var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            foreach (DataColumn col in sourceTable.Columns)
            {
                bool isJsonCol = jsonColumns.Any(j => j.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!isJsonCol && !addedColumns.Contains(col.ColumnName))
                {
                    resultTable.Columns.Add(col.ColumnName, typeof(string));
                    addedColumns.Add(col.ColumnName);
                }
            }

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

            foreach (DataRow sourceRow in sourceTable.Rows)
            {
                var newRow = resultTable.NewRow();

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
                    if (arr.Count > 0 && arr[0].Type == JTokenType.Object)
                    {
                        result[prefix] = $"[{arr.Count} items]";
                    }
                    else
                    {
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
    }
}
