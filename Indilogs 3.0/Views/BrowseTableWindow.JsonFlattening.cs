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
                            var descendants = obj.Descendants().OfType<JValue>().ToList();
                            foreach (var token in descendants)
                            {
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
            }

            // Clean empty columns - remove columns that have no data
            resultTable = CleanEmptyColumns(resultTable);

            return resultTable;
        }

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
    }
}
