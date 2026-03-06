using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class ConfigExplorerViewModel
    {
        private async Task DebouncedFilterDbTree()
        {
            // Cancel previous search
            _searchDebounceToken?.Cancel();
            _searchDebounceToken = new CancellationTokenSource();
            var token = _searchDebounceToken.Token;

            try
            {
                // Wait for debounce period
                await Task.Delay(SearchDebounceMs, token);

                if (!token.IsCancellationRequested)
                {
                    // Run filter on background thread then update UI
                    await Task.Run(() =>
                    {
                        _dispatcher.Post(() => FilterDbTreeNodes());
                    }, token);
                }
            }
            catch (TaskCanceledException)
            {
                // Search was cancelled by newer search - this is expected
            }
            catch (Exception ex)
            {
                AppLogger.Error("[ConfigExplorer] DB tree filter debounce failed", ex);
            }
        }

        private void FilterDbTreeNodes()
        {
            // DbTreeNodes contains a root node "Tables (X)" with tables as children
            foreach (var rootNode in DbTreeNodes)
            {
                if (string.IsNullOrWhiteSpace(ConfigSearchText))
                {
                    // No filter - show all tables
                    rootNode.IsVisible = true;
                    foreach (var tableNode in rootNode.Children)
                    {
                        SetNodeVisibility(tableNode, true);
                    }
                }
                else
                {
                    string searchLower = ConfigSearchText.ToLower();
                    rootNode.IsVisible = true;

                    // Filter tables by name
                    foreach (var tableNode in rootNode.Children)
                    {
                        bool matches = tableNode.Name?.ToLower().Contains(searchLower) == true;
                        tableNode.IsVisible = matches;

                        // If table matches, show all its children (columns)
                        if (matches)
                        {
                            foreach (var child in tableNode.Children)
                            {
                                SetNodeVisibility(child, true);
                            }
                        }
                    }
                }
            }
        }

        private bool FilterTreeNode(DbTreeNode node, string searchLower)
        {
            bool selfMatches = (node.Name?.ToLower().Contains(searchLower) == true) ||
                               (node.Type?.ToLower().Contains(searchLower) == true) ||
                               (node.Schema?.ToLower().Contains(searchLower) == true);

            bool anyChildMatches = false;
            foreach (var child in node.Children)
            {
                bool childMatches = FilterTreeNode(child, searchLower);
                if (childMatches) anyChildMatches = true;
            }

            bool isVisible = selfMatches || anyChildMatches;
            node.IsVisible = isVisible;

            if (isVisible && node.Children.Count > 0)
            {
                node.IsExpanded = true;
            }

            return isVisible;
        }

        private void SetNodeVisibility(DbTreeNode node, bool visible)
        {
            node.IsVisible = visible;
            foreach (var child in node.Children)
            {
                SetNodeVisibility(child, visible);
            }
        }

        private async Task LoadSqliteToTreeAsync(byte[] dbBytes)
        {
            _dispatcher.Post(() =>
            {
                DbTreeNodes.Clear();
                _allDbTreeNodes.Clear();
            });

            DbTreeNode? tablesRoot = null;
            string? tempDbPath = null;

            try
            {
                // Do all DB work on background thread
                tablesRoot = await Task.Run(() =>
                {
                    tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                    File.WriteAllBytes(tempDbPath, dbBytes);

                    var root = new DbTreeNode
                    {
                        NodeType = "Root",
                        IsExpanded = true,
                        DatabaseFileName = SelectedConfigFile ?? ""
                    };

                    using (var connection = new SqliteConnection($"Data Source={tempDbPath};Mode=ReadOnly"))
                    {
                        connection.Open();

                        // Get all tables with their CREATE statements
                        var tablesInfo = new List<(string name, string sql)>();
                        using (var cmd = new SqliteCommand("SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string name = reader.GetString(0);
                                string sql = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                tablesInfo.Add((name, sql));
                            }
                        }

                        root.Name = $"Tables ({tablesInfo.Count})";

                        foreach (var (tableName, tableSql) in tablesInfo)
                        {
                            // Table node with schema
                            var tableNode = new DbTreeNode
                            {
                                Name = tableName,
                                Schema = tableSql,
                                NodeType = "Table",
                                IsExpanded = false,
                                DatabaseFileName = SelectedConfigFile ?? ""
                            };

                            // Get column info using PRAGMA
                            using (var cmd = new SqliteCommand($"PRAGMA table_info([{AppConstants.EscapeSqlBracketId(tableName)}])", connection))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    // cid, name, type, notnull, dflt_value, pk
                                    string colName = reader.GetString(1);
                                    string colType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                    bool notNull = reader.GetInt32(3) == 1;
                                    bool isPk = reader.GetInt32(5) == 1;

                                    // Build schema description
                                    string schemaDesc = $"\"{colName}\" {colType}";
                                    if (notNull) schemaDesc += " NOT NULL";
                                    if (isPk) schemaDesc += " PRIMARY KEY";

                                    var columnNode = new DbTreeNode
                                    {
                                        Name = colName,
                                        Type = colType,
                                        Schema = schemaDesc,
                                        NodeType = "Column"
                                    };

                                    tableNode.Children.Add(columnNode);
                                }
                            }

                            root.Children.Add(tableNode);
                        }
                    }

                    // Cleanup temp file
                    if (tempDbPath != null && File.Exists(tempDbPath))
                    {
                        try { File.Delete(tempDbPath); } catch (Exception ex) { AppLogger.Error("Temp DB cleanup failed", ex); }
                    }

                    return root;
                });

                // Update UI on main thread
                _dispatcher.Post(() =>
                {
                    DbTreeNodes.Add(tablesRoot);
                    _allDbTreeNodes.Add(tablesRoot);
                });
            }
            catch (Exception ex)
            {
                _dispatcher.Post(() =>
                {
                    DbTreeNodes.Add(new DbTreeNode { Name = $"Error: {ex.Message}", NodeType = "Error" });
                });
            }
        }

        private void BrowseTable(object? obj)
        {
            if (obj is DbTreeNode node && node.NodeType == "Table")
            {
                if (_sessionVM.SelectedSession?.DatabaseFiles == null || string.IsNullOrEmpty(node.DatabaseFileName))
                {
                    _dialogService.ShowWarning("No database file available.", "Error");
                    return;
                }

                if (!_sessionVM.SelectedSession.DatabaseFiles.ContainsKey(node.DatabaseFileName))
                {
                    _dialogService.ShowWarning($"Database file '{node.DatabaseFileName}' not found in session.", "Error");
                    return;
                }

                try
                {
                    var dbBytes = _sessionVM.SelectedSession.DatabaseFiles[node.DatabaseFileName];
                    var window = _viewFactory.Create<Views.BrowseTableWindow>(node.Name, dbBytes);
                    window.Owner = _windowOwner.GetOwner();

                    // Ensure window opens in front
                    window.Loaded += (s, e) =>
                    {
                        window.Activate();
                        window.Focus();
                    };

                    _windowManager.OpenWindow(window);

                    // Force to front after a short delay
                    _dispatcher.Post(() =>
                    {
                        window.Activate();
                        window.Focus();
                    }, DispatchPriority.Background);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Error opening table browser: {ex.Message}", "Error");
                }
            }
        }
    }
}
