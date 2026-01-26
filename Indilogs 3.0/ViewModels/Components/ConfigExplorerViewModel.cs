using IndiLogs_3._0.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
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
    public class ConfigExplorerViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;

        // Configuration file management
        public ObservableCollection<string> ConfigurationFiles { get; set; }
        private Dictionary<string, string> _configFilesPathMap = new Dictionary<string, string>();

        private string _selectedConfigFile;
        public string SelectedConfigFile
        {
            get => _selectedConfigFile;
            set
            {
                if (_selectedConfigFile != value)
                {
                    _selectedConfigFile = value;
                    OnPropertyChanged();
                    _parent?.NotifyPropertyChanged(nameof(_parent.SelectedConfigFile));
                    LoadSelectedFileContent();
                }
            }
        }

        private string _configFileContent;
        public string ConfigFileContent
        {
            get => _configFileContent;
            set
            {
                _configFileContent = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.ConfigFileContent));
            }
        }

        private string _filteredConfigContent;
        public string FilteredConfigContent
        {
            get => _filteredConfigContent;
            set
            {
                _filteredConfigContent = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.FilteredConfigContent));
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
                    _parent?.NotifyPropertyChanged(nameof(_parent.ConfigSearchText));

                    // Use debounce for DB tree filtering to avoid lag
                    if (IsDbFileSelected)
                    {
                        DebouncedFilterDbTree();
                    }
                    else
                    {
                        FilterConfigContent();
                    }
                }
            }
        }

        private async void DebouncedFilterDbTree()
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
                        Application.Current.Dispatcher.Invoke(() => FilterDbTreeNodes());
                    }, token);
                }
            }
            catch (TaskCanceledException)
            {
                // Search was cancelled by newer search - this is expected
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

        // Database browser
        private ObservableCollection<DbTreeNode> _dbTreeNodes = new ObservableCollection<DbTreeNode>();
        public ObservableCollection<DbTreeNode> DbTreeNodes
        {
            get => _dbTreeNodes;
            set
            {
                _dbTreeNodes = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.DbTreeNodes));
            }
        }

        private ObservableCollection<DbTreeNode> _allDbTreeNodes = new ObservableCollection<DbTreeNode>();

        // Debounce for search
        private CancellationTokenSource _searchDebounceToken;
        private const int SearchDebounceMs = 300;

        private bool _isDbFileSelected;
        public bool IsDbFileSelected
        {
            get => _isDbFileSelected;
            set
            {
                _isDbFileSelected = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.IsDbFileSelected));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsExplorerMenuOpen));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsConfigMenuOpen));
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
                _parent?.NotifyPropertyChanged(nameof(_parent.IsLoggersMenuOpen));
            }
        }

        // Commands
        public ICommand BrowseTableCommand { get; }
        public ICommand RefreshConfigExplorerCommand { get; }
        public ICommand ClearConfigSearchCommand { get; }

        public ConfigExplorerViewModel(MainViewModel parent, LogSessionViewModel sessionVM)
        {
            _parent = parent;
            _sessionVM = sessionVM;

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
                DbTreeNodes.Clear();
                return;
            }

            try
            {
                // Check if this is a SQLite database file
                if (SelectedConfigFile.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    IsDbFileSelected = true;
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
                        dynamic parsedJson = JsonConvert.DeserializeObject(content);
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
            Application.Current.Dispatcher.Invoke(() =>
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

                    using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Read Only=True;"))
                    {
                        connection.Open();

                        // Get all tables with their CREATE statements
                        var tablesInfo = new List<(string name, string sql)>();
                        using (var cmd = new SQLiteCommand("SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
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
                            using (var cmd = new SQLiteCommand($"PRAGMA table_info([{tableName}])", connection))
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
                        try { File.Delete(tempDbPath); } catch { }
                    }

                    return root;
                });

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DbTreeNodes.Add(tablesRoot);
                    _allDbTreeNodes.Add(tablesRoot);
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
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
                    MessageBox.Show("No database file available.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_sessionVM.SelectedSession.DatabaseFiles.ContainsKey(node.DatabaseFileName))
                {
                    MessageBox.Show($"Database file '{node.DatabaseFileName}' not found in session.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var dbBytes = _sessionVM.SelectedSession.DatabaseFiles[node.DatabaseFileName];
                    var window = new Views.BrowseTableWindow(node.Name, dbBytes);
                    window.Owner = Application.Current.MainWindow;
                    window.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening table browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshConfigExplorer(object obj)
        {
            LoadSelectedFileContent();
        }

        public void LoadConfigurationFiles()
        {
            ConfigurationFiles.Clear();

            if (_sessionVM.SelectedSession == null)
                return;

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
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
