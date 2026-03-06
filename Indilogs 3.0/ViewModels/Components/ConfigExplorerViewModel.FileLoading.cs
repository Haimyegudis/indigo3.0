using IndiLogs_3._0.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class ConfigExplorerViewModel
    {
        private void LoadSelectedFileContent()
        {
            ConfigSearchText = ""; // Reset search when changing files

            if (string.IsNullOrEmpty(SelectedConfigFile) || _sessionVM.SelectedSession == null)
            {
                ConfigFileContent = "";
                IsDbFileSelected = false;
                IsCsvFileSelected = false;
                CsvDataView = null;
                DbTreeNodes.Clear();
                return;
            }

            try
            {
                // Terminal logs mode: read from TerminalLogFiles dictionary
                if (_sessionVM.SelectedSession.HasBinaryAppLogs &&
                    _sessionVM.SelectedSession.TerminalLogFiles != null &&
                    _sessionVM.SelectedSession.TerminalLogFiles.ContainsKey(SelectedConfigFile))
                {
                    IsDbFileSelected = false;
                    DbTreeNodes.Clear();

                    string terminalContent = _sessionVM.SelectedSession.TerminalLogFiles[SelectedConfigFile];

                    // CSV files: parse into DataTable for grid display (async to avoid UI freeze)
                    if (SelectedConfigFile.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        IsCsvFileSelected = true;
                        ConfigFileContent = "";
                        CsvDataView = null;
                        _ = Task.Run(() =>
                        {
                            var view = ParseCsvToDataView(terminalContent);
                            _dispatcher.Post(() => CsvDataView = view);
                        });
                    }
                    else
                    {
                        IsCsvFileSelected = false;
                        CsvDataView = null;
                        ConfigFileContent = terminalContent;
                    }
                    return;
                }

                // Check if this is a SQLite database file
                if (SelectedConfigFile.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    IsDbFileSelected = true;
                    IsCsvFileSelected = false;
                    CsvDataView = null;
                    ConfigFileContent = ""; // Clear text content for DB files

                    if (_sessionVM.SelectedSession.DatabaseFiles != null &&
                        _sessionVM.SelectedSession.DatabaseFiles.ContainsKey(SelectedConfigFile))
                    {
                        // Load DB async to prevent UI freeze
                        _ = LoadSqliteToTreeAsync(_sessionVM.SelectedSession.DatabaseFiles[SelectedConfigFile]);
                    }
                    else
                    {
                        DbTreeNodes.Clear();
                    }
                    return;
                }

                // For non-DB files, clear tree and show text
                IsDbFileSelected = false;
                IsCsvFileSelected = false;
                CsvDataView = null;
                DbTreeNodes.Clear();

                // Handle JSON/text configuration files
                if (_sessionVM.SelectedSession.ConfigurationFiles == null ||
                    !_sessionVM.SelectedSession.ConfigurationFiles.ContainsKey(SelectedConfigFile))
                {
                    ConfigFileContent = "";
                    return;
                }

                string content = _sessionVM.SelectedSession.ConfigurationFiles[SelectedConfigFile];

                // Try to format JSON for better readability
                try
                {
                    if (SelectedConfigFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        content.TrimStart().StartsWith("{") ||
                        content.TrimStart().StartsWith("["))
                    {
                        dynamic? parsedJson = JsonConvert.DeserializeObject(content, AppConstants.SafeJsonSettings);
                        ConfigFileContent = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                    }
                    else
                    {
                        ConfigFileContent = content;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"JSON pretty-print failed: {ex.Message}");
                    ConfigFileContent = content;
                }
            }
            catch (Exception ex)
            {
                ConfigFileContent = $"Error displaying file content: {ex.Message}";
            }
        }

        private async Task DebouncedFilterConfigContent()
        {
            _searchDebounceToken?.Cancel();
            _searchDebounceToken = new CancellationTokenSource();
            var token = _searchDebounceToken.Token;
            try
            {
                await Task.Delay(SearchDebounceMs, token);
                if (!token.IsCancellationRequested)
                    FilterConfigContent();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { AppLogger.Error("[ConfigExplorer] Config content filter debounce failed", ex); }
        }

        private void FilterConfigContent()
        {
            // Filter text content
            if (string.IsNullOrWhiteSpace(ConfigSearchText))
            {
                FilteredConfigContent = ConfigFileContent;
                return;
            }

            // Simple line-by-line filtering
            if (!string.IsNullOrEmpty(ConfigFileContent))
            {
                var lines = ConfigFileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var filtered = lines.Where(line => line.IndexOf(ConfigSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                FilteredConfigContent = string.Join(Environment.NewLine, filtered);
            }
        }

        private void FilterCsvData()
        {
            if (CsvDataView == null || CsvDataView.Table == null) return;

            if (string.IsNullOrWhiteSpace(ConfigSearchText))
            {
                CsvDataView.RowFilter = "";
                return;
            }

            try
            {
                // Build a RowFilter that searches across all columns
                var table = CsvDataView.Table;
                var searchText = ConfigSearchText.Replace("'", "''"); // Escape single quotes
                var conditions = new List<string>();
                foreach (DataColumn col in table.Columns)
                {
                    conditions.Add($"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{searchText}%'");
                }
                CsvDataView.RowFilter = string.Join(" OR ", conditions);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"CSV filter expression failed: {ex.Message}");
                CsvDataView.RowFilter = "";
            }
        }

        private DataView? ParseCsvToDataView(string csvContent)
        {
            try
            {
                var dt = new DataTable();
                using (var reader = new StringReader(csvContent))
                {
                    // Parse header
                    string? headerLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(headerLine)) return null;

                    var headers = headerLine.Split(',');
                    foreach (var header in headers)
                    {
                        string colName = header.Trim();
                        if (string.IsNullOrEmpty(colName)) colName = $"Col{dt.Columns.Count}";
                        string uniqueName = colName;
                        int suffix = 2;
                        while (dt.Columns.Contains(uniqueName))
                            uniqueName = $"{colName}_{suffix++}";
                        dt.Columns.Add(uniqueName, typeof(string));
                    }

                    // Bulk insert mode — disables index maintenance during load
                    dt.BeginLoadData();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var values = line.Split(',');
                        var row = dt.NewRow();
                        for (int j = 0; j < dt.Columns.Count && j < values.Length; j++)
                            row[j] = values[j].Trim();
                        dt.Rows.Add(row);
                    }
                    dt.EndLoadData();
                }

                return dt.DefaultView;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"DataView creation failed: {ex.Message}");
                return null;
            }
        }
    }
}
