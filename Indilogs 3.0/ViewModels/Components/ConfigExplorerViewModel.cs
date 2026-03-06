using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages configuration files and database browser functionality
    /// </summary>
    public partial class ConfigExplorerViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private readonly IWindowOwnerProvider _windowOwner;
        private readonly IWindowManager _windowManager;

        // Configuration file management
        public ObservableCollection<string> ConfigurationFiles { get; set; }

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

        public ConfigExplorerViewModel(MainViewModel parent, LogSessionViewModel sessionVM, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher, IWindowOwnerProvider windowOwner, IWindowManager windowManager)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowOwner = windowOwner;
            _windowManager = windowManager;

            // Initialize collections
            ConfigurationFiles = new ObservableCollection<string>();
            DbTreeNodes = new ObservableCollection<DbTreeNode>();

            // Initialize commands (placeholders for now)
            BrowseTableCommand = new RelayCommand(BrowseTable);
            RefreshConfigExplorerCommand = new RelayCommand(RefreshConfigExplorer);
            ClearConfigSearchCommand = new RelayCommand(o => ConfigSearchText = "");
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
            DbTreeNodes.Clear();
            _allDbTreeNodes.Clear();
            SelectedConfigFile = null;
            ConfigFileContent = "";
            FilteredConfigContent = "";
            IsDbFileSelected = false;
            IsCsvFileSelected = false;
            CsvDataView = null;
        }

        private void RefreshConfigExplorer(object? obj)
        {
            LoadSelectedFileContent();
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
    }
}
