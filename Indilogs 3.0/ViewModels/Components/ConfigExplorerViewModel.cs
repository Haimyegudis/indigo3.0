using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages configuration files and database browser functionality
    /// </summary>
    public class ConfigExplorerViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;

        // Configuration file management
        public ObservableCollection<string> ConfigurationFiles { get; set; }
        private Dictionary<string, string> _configFilesPathMap = new Dictionary<string, string>();

        private string? _selectedConfigFile;
        public string? SelectedConfigFile
        {
            get => _selectedConfigFile;
            set
            {
                if (_selectedConfigFile != value)
                {
                    _selectedConfigFile = value;
                    OnPropertyChanged();
                    LoadSelectedFileContent();
                }
            }
        }

        private string? _configFileContent;
        public string? ConfigFileContent
        {
            get => _configFileContent;
            set
            {
                _configFileContent = value;
                OnPropertyChanged();
                // Sync FilteredConfigContent when content changes (for search to work)
                FilteredConfigContent = value;
            }
        }

        private string? _filteredConfigContent;
        public string? FilteredConfigContent
        {
            get => _filteredConfigContent;
            set
            {
                _filteredConfigContent = value;
                OnPropertyChanged();
            }
        }

        // Search in config tab
        private string _configSearchText = "";
        public string ConfigSearchText
        {
            get => _configSearchText;
            set
            {
                if (_configSearchText != value)
                {
                    _configSearchText = value;
                    OnPropertyChanged();

                    // Use debounce for DB tree filtering to avoid lag
                    if (IsDbFileSelected)
                    {
                        _ = DebouncedFilterDbTree();
                    }
                    else if (IsCsvFileSelected)
                    {
                        FilterCsvData();
                    }
                    else
                    {
                        _ = DebouncedFilterConfigContent();
                    }
                }
            }
        }

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

        // Database browser
        private ObservableCollection<DbTreeNode> _dbTreeNodes = new ObservableCollection<DbTreeNode>();
        public ObservableCollection<DbTreeNode> DbTreeNodes
        {
            get => _dbTreeNodes;
            set
            {
                _dbTreeNodes = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<DbTreeNode> _allDbTreeNodes = new ObservableCollection<DbTreeNode>();

        // Debounce for search
        private CancellationTokenSource? _searchDebounceToken;
        private const int SearchDebounceMs = 300;

        private bool _isDbFileSelected;
        public bool IsDbFileSelected
        {
            get => _isDbFileSelected;
            set
            {
                _isDbFileSelected = value;
                OnPropertyChanged();
            }
        }

        private bool _isCsvFileSelected;
        public bool IsCsvFileSelected
        {
            get => _isCsvFileSelected;
            set
            {
                _isCsvFileSelected = value;
                OnPropertyChanged();
            }
        }

        private DataView? _csvDataView;
        public DataView? CsvDataView
        {
            get => _csvDataView;
            set
            {
                _csvDataView = value;
                OnPropertyChanged();
            }
        }

        // Menu states
        private bool _isExplorerMenuOpen;
        public bool IsExplorerMenuOpen
        {
            get => _isExplorerMenuOpen;
            set
            {
                _isExplorerMenuOpen = value;
                OnPropertyChanged();
            }
        }

        private bool _isConfigMenuOpen;
        public bool IsConfigMenuOpen
        {
            get => _isConfigMenuOpen;
            set
            {
                _isConfigMenuOpen = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoggersMenuOpen;
        public bool IsLoggersMenuOpen
        {
            get => _isLoggersMenuOpen;
            set
            {
                _isLoggersMenuOpen = value;
                OnPropertyChanged();
            }
        }

        // Commands
        public ICommand BrowseTableCommand { get; }
        public ICommand RefreshConfigExplorerCommand { get; }
        public ICommand ClearConfigSearchCommand { get; }

        public ConfigExplorerViewModel(MainViewModel parent, LogSessionViewModel sessionVM, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;

            // Initialize collections
            ConfigurationFiles = new ObservableCollection<string>();
            DbTreeNodes = new ObservableCollection<DbTreeNode>();

            // Initialize commands (placeholders for now)
            BrowseTableCommand = new RelayCommand(BrowseTable);
            RefreshConfigExplorerCommand = new RelayCommand(RefreshConfigExplorer);
            ClearConfigSearchCommand = new RelayCommand(o => ConfigSearchText = "");
        }

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
                        dynamic parsedJson = JsonConvert.DeserializeObject(content, new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });
                        ConfigFileContent = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                    }
                    else
                    {
                        ConfigFileContent = content;
                    }
                }
                catch
                {
                    ConfigFileContent = content;
                }
            }
            catch (Exception ex)
            {
                ConfigFileContent = $"Error displaying file content: {ex.Message}";
            }
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
            catch
            {
                // If filter expression fails, clear it
                CsvDataView.RowFilter = "";
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

            DbTreeNode tablesRoot = null;
            string tempDbPath = null;

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
                        DatabaseFileName = SelectedConfigFile // Store DB file name
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
                                DatabaseFileName = SelectedConfigFile // Store DB file name
                            };

                            // Get column info using PRAGMA
                            using (var cmd = new SqliteCommand($"PRAGMA table_info([{EscapeSqlBracketId(tableName)}])", connection))
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

        private void BrowseTable(object obj)
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
                    window.Owner = Application.Current.MainWindow;

                    // Ensure window opens in front
                    window.Loaded += (s, e) =>
                    {
                        window.Activate();
                        window.Focus();
                    };

                    WindowManager.OpenWindow(window);

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

        private void RefreshConfigExplorer(object obj)
        {
            LoadSelectedFileContent();
        }

        /// <summary>
        /// Populates the configuration files list from the current session (config, DB, and terminal files).
        /// </summary>
        public void LoadConfigurationFiles()
        {
            ConfigurationFiles.Clear();

            if (_sessionVM.SelectedSession == null)
                return;

            // Terminal logs mode: show terminal log files instead of config/db
            // Check both TerminalLogFiles (.txt/.log as strings) and TerminalCsvBytes (.csv as raw bytes)
            bool hasTerminalFiles = (_sessionVM.SelectedSession.TerminalLogFiles != null &&
                                     _sessionVM.SelectedSession.TerminalLogFiles.Count > 0) ||
                                    (_sessionVM.SelectedSession.TerminalCsvBytes != null &&
                                     _sessionVM.SelectedSession.TerminalCsvBytes.Count > 0);

            if (_sessionVM.SelectedSession.HasBinaryAppLogs && hasTerminalFiles)
            {
                // Only display .txt and .log files (CSV/ARL are handled by IO Charts)
                if (_sessionVM.SelectedSession.TerminalLogFiles != null)
                {
                    foreach (var fileName in _sessionVM.SelectedSession.TerminalLogFiles.Keys)
                        ConfigurationFiles.Add(fileName);
                }
                return;
            }

            // Add configuration files
            if (_sessionVM.SelectedSession.ConfigurationFiles != null)
            {
                foreach (var fileName in _sessionVM.SelectedSession.ConfigurationFiles.Keys)
                {
                    ConfigurationFiles.Add(fileName);
                }
            }

            // Add database files
            if (_sessionVM.SelectedSession.DatabaseFiles != null)
            {
                foreach (var fileName in _sessionVM.SelectedSession.DatabaseFiles.Keys)
                {
                    ConfigurationFiles.Add(fileName);
                }
            }
        }

        /// <summary>
        /// Clears all configuration file data including DB tree, CSV view, and search state.
        /// </summary>
        public void ClearConfigurationFiles()
        {
            ConfigurationFiles.Clear();
            _configFilesPathMap.Clear();
            DbTreeNodes.Clear();
            _allDbTreeNodes.Clear();
            SelectedConfigFile = null;
            ConfigFileContent = "";
            FilteredConfigContent = "";
            IsDbFileSelected = false;
            IsCsvFileSelected = false;
            CsvDataView = null;
        }

        private DataView ParseCsvToDataView(string csvContent)
        {
            try
            {
                var dt = new DataTable();
                using (var reader = new StringReader(csvContent))
                {
                    // Parse header
                    string headerLine = reader.ReadLine();
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
                    string line;
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
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Escapes a SQL identifier for safe use in bracket-quoted context ([identifier]).
        /// </summary>
        private static string EscapeSqlBracketId(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return identifier.Replace("]", "]]");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchDebounceToken?.Cancel();
                _searchDebounceToken?.Dispose();
                _searchDebounceToken = null;
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
