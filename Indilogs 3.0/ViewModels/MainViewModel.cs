using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Views;
using IndiLogs_3._0.ViewModels.Components;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Data.SQLite;
using System.Xml.Linq;

namespace IndiLogs_3._0.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // --- Child ViewModels (Composition Pattern) ---
        public LogSessionViewModel SessionVM { get; private set; }
        public FilterSearchViewModel FilterVM { get; private set; }
        public LiveMonitoringViewModel LiveVM { get; private set; }
        public CaseManagementViewModel CaseVM { get; private set; }
        public ConfigExplorerViewModel ConfigVM { get; private set; }
        public VisualTimelineViewModel VisualTimelineVM { get; set; } = new VisualTimelineViewModel();
        public ChartTabViewModel ChartVM { get; private set; }
        public CprAnalysisViewModel CprVM { get; private set; }
        public DifferentLogsViewModel DifferentLogsVM { get; private set; }
        public StepRecorderViewModel StepRecorderVM { get; private set; }

        private bool _isVisualMode;
        public bool IsVisualMode
        {
            get => _isVisualMode;
            set
            {
                _isVisualMode = value;
                OnPropertyChanged();
                if (value) InitializeVisualMode();
            }
        }

        public ICommand BrowseTableCommand { get; }
        public ICommand CopyTableNameCommand { get; }
        public ICommand ToggleAnnotationCommand { get; }
        public ICommand CloseAnnotationCommand { get; }
        public ICommand ToggleAllAnnotationsCommand { get; }
        public ICommand ToggleVisualModeCommand { get; }

        public ObservableCollection<LogEntry> MarkedAppLogs => CaseVM?.MarkedAppLogs;
        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly ICsvExportService _csvService;
        private readonly DefaultConfigurationService _defaultConfigService = new DefaultConfigurationService();
        public DefaultConfigurationService DefaultConfigService => _defaultConfigService;
        public ILogColoringService ColoringService => _coloringService;

        // Windows Instances
        private StatesWindow _statesWindow;
        private AnalysisReportWindow _analysisWindow;
        private bool _isAnalysisRunning;
        private ExportConfigurationWindow _exportConfigWindow = null;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set { _isAnalysisRunning = value; OnPropertyChanged(); }
        }

        // Caches
        private IList<LogEntry> _allLogsCache;
        private IList<LogEntry> _allAppLogsCache;

        // Coloring
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();

        public List<ColoringCondition> MainColoringRules
        {
            get => CaseVM?.MainColoringRules;
            set { if (CaseVM != null) CaseVM.MainColoringRules = value; }
        }
        public List<ColoringCondition> AppColoringRules
        {
            get => CaseVM?.AppColoringRules;
            set { if (CaseVM != null) CaseVM.AppColoringRules = value; }
        }

        // Case File & Annotations
        public Dictionary<LogEntry, LogAnnotation> LogAnnotations => CaseVM?.LogAnnotations;

        private const int UI_UPDATE_BATCH_SIZE = 500;
        private readonly object _collectionLock = new object();

        // Collections
        public IEnumerable<LogEntry> Logs
        {
            get => SessionVM?.Logs;
            set { if (SessionVM != null) SessionVM.Logs = value; OnPropertyChanged(); }
        }

        public ObservableRangeCollection<LogEntry> FilteredLogs => FilterVM?.FilteredLogs;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered => FilterVM?.AppDevLogsFiltered;
        public ObservableCollection<LoggerNode> LoggerTreeRoot => FilterVM?.LoggerTreeRoot;
        public ObservableCollection<LoggerNode> PlcLoggerTreeRoot => FilterVM?.PlcLoggerTreeRoot;

        public IList<LogEntry> AllLogsCache => SessionVM?.AllLogsCache;
        public IList<LogEntry> AllAppLogsCache => SessionVM?.AllAppLogsCache;
        public ObservableCollection<EventEntry> Events => SessionVM?.Events;

        // Full-column DataView for EVENTS tab (all CSV columns as-is)
        private System.Data.DataView _eventsDataView;
        public System.Data.DataView EventsDataView
        {
            get => _eventsDataView;
            set { _eventsDataView = value; OnPropertyChanged(); }
        }

        public void LoadEventsDataView()
        {
            EventsDataView = null;
            if (SelectedSession?.EventsCsvRawContent == null) return;

            try
            {
                var dt = new System.Data.DataTable();
                var csvContent = SelectedSession.EventsCsvRawContent;
                var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var headers = SplitCsvLineHelper(lines[0]);
                foreach (var header in headers)
                {
                    string colName = header.Trim().Trim('"');
                    if (string.IsNullOrEmpty(colName)) colName = $"Col{dt.Columns.Count}";
                    string uniqueName = colName;
                    int suffix = 2;
                    while (dt.Columns.Contains(uniqueName))
                        uniqueName = $"{colName}_{suffix++}";
                    dt.Columns.Add(uniqueName, typeof(string));
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    var values = SplitCsvLineHelper(lines[i]);
                    var row = dt.NewRow();
                    for (int j = 0; j < dt.Columns.Count && j < values.Count; j++)
                        row[j] = values[j].Trim().Trim('"');
                    dt.Rows.Add(row);
                }

                EventsDataView = dt.DefaultView;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Loading events CSV failed", ex);
            }
        }

        private static List<string> SplitCsvLineHelper(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuotes = !inQuotes;
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start));
            return result;
        }

        public ObservableCollection<BitmapImage> Screenshots => SessionVM?.Screenshots;
        public ObservableCollection<string> LoadedFiles => SessionVM?.LoadedFiles;
        public ObservableCollection<LogSessionData> LoadedSessions => SessionVM?.LoadedSessions;

        public LogSessionData SelectedSession
        {
            get => SessionVM?.SelectedSession;
            set { if (SessionVM != null) SessionVM.SelectedSession = value; }
        }

        public double CurrentProgress
        {
            get => SessionVM?.CurrentProgress ?? 0;
            set { if (SessionVM != null) SessionVM.CurrentProgress = value; }
        }

        public string StatusMessage
        {
            get => SessionVM?.StatusMessage;
            set { if (SessionVM != null) SessionVM.StatusMessage = value; }
        }

        public bool IsBusy
        {
            get => SessionVM?.IsBusy ?? false;
            set { if (SessionVM != null) SessionVM.IsBusy = value; }
        }

        // Search & Filter Properties
        public string SearchText
        {
            get => FilterVM?.SearchText;
            set { if (FilterVM != null) FilterVM.SearchText = value; }
        }

       

        public LoggerNode SelectedTreeItem => FilterVM?.SelectedTreeItem;
        public bool IsMainFilterActive => FilterVM?.IsMainFilterActive ?? false;
        public bool IsAppFilterActive => FilterVM?.IsAppFilterActive ?? false;
        public bool IsMainFilterOutActive => FilterVM?.IsMainFilterOutActive ?? false;
        public bool IsAppFilterOutActive => FilterVM?.IsAppFilterOutActive ?? false;
        public bool IsTimeFocusActive => FilterVM?.IsTimeFocusActive ?? false;
        public bool IsAppTimeFocusActive => FilterVM?.IsAppTimeFocusActive ?? false;

        public FilterNode MainFilterRoot
        {
            get => FilterVM?.MainFilterRoot;
            set { if (FilterVM != null) FilterVM.MainFilterRoot = value; }
        }
        public FilterNode AppFilterRoot
        {
            get => FilterVM?.AppFilterRoot;
            set { if (FilterVM != null) FilterVM.AppFilterRoot = value; }
        }
        public FilterNode SavedFilterRoot
        {
            get => FilterVM?.SavedFilterRoot;
            set { if (FilterVM != null) FilterVM.SavedFilterRoot = value; }
        }
        public List<string> NegativeFilters => FilterVM?.NegativeFilters;
        public List<string> AppNegativeFilters => FilterVM?.AppNegativeFilters;
        public List<string> ActiveThreadFilters => FilterVM?.ActiveThreadFilters;
        public List<LogEntry> LastFilteredCache
        {
            get => FilterVM?.LastFilteredCache;
            set { if (FilterVM != null) FilterVM.LastFilteredCache = value; }
        }
        public List<LogEntry> LastFilteredAppCache
        {
            get => FilterVM?.LastFilteredAppCache;
            set { if (FilterVM != null) FilterVM.LastFilteredAppCache = value; }
        }
        public HashSet<string> TreeHiddenLoggers => FilterVM?.TreeHiddenLoggers;
        public HashSet<string> TreeHiddenPrefixes => FilterVM?.TreeHiddenPrefixes;
        public string TreeShowOnlyLogger
        {
            get => FilterVM?.TreeShowOnlyLogger;
            set { if (FilterVM != null) FilterVM.TreeShowOnlyLogger = value; }
        }
        public string TreeShowOnlyPrefix
        {
            get => FilterVM?.TreeShowOnlyPrefix;
            set { if (FilterVM != null) FilterVM.TreeShowOnlyPrefix = value; }
        }
        public bool IsSearchPanelVisible
        {
            get => FilterVM?.IsSearchPanelVisible ?? false;
            set
            {
                if (FilterVM != null)
                    FilterVM.IsSearchPanelVisible = value;
                OnPropertyChanged();
            }
        }
        // Live Mode
        public bool IsLiveMode
        {
            get => LiveVM?.IsLiveMode ?? false;
            set { if (LiveVM != null) LiveVM.IsLiveMode = value; }
        }
        public bool IsRunning
        {
            get => LiveVM?.IsRunning ?? false;
            set { if (LiveVM != null) LiveVM.IsRunning = value; }
        }
        public bool IsPaused => LiveVM?.IsPaused ?? false;

        // Case Management
        public ObservableCollection<SavedConfiguration> SavedConfigs
        {
            get => CaseVM?.SavedConfigs;
            set { /* Read-only collection */ }
        }
        public ObservableCollection<LogEntry> MarkedLogs
        {
            get => CaseVM?.MarkedLogs;
            set { /* Read-only collection */ }
        }
        public SavedConfiguration SelectedConfig
        {
            get => CaseVM?.SelectedConfig;
            set { if (CaseVM != null) CaseVM.SelectedConfig = value; }
        }
        public bool IsMarkedLogsCombined
        {
            get => CaseVM?.IsMarkedLogsCombined ?? false;
            set { if (CaseVM != null) CaseVM.IsMarkedLogsCombined = value; }
        }
        public bool ShowAllAnnotations
        {
            get => CaseVM?.ShowAllAnnotations ?? false;
            set { if (CaseVM != null) CaseVM.ShowAllAnnotations = value; }
        }

        // Config Explorer
        public ObservableCollection<string> ConfigurationFiles => ConfigVM?.ConfigurationFiles;
        public string SelectedConfigFile
        {
            get => ConfigVM?.SelectedConfigFile;
            set { if (ConfigVM != null) ConfigVM.SelectedConfigFile = value; }
        }
        public string ConfigFileContent
        {
            get => ConfigVM?.ConfigFileContent;
            set { if (ConfigVM != null) ConfigVM.ConfigFileContent = value; }
        }
        public string FilteredConfigContent => ConfigVM?.FilteredConfigContent;
        public string ConfigSearchText
        {
            get => ConfigVM?.ConfigSearchText;
            set { if (ConfigVM != null) ConfigVM.ConfigSearchText = value; }
        }
        public ObservableCollection<DbTreeNode> DbTreeNodes => ConfigVM?.DbTreeNodes;
        public bool IsDbFileSelected
        {
            get => ConfigVM?.IsDbFileSelected ?? false;
            set { if (ConfigVM != null) ConfigVM.IsDbFileSelected = value; }
        }
        public bool IsCsvFileSelected
        {
            get => ConfigVM?.IsCsvFileSelected ?? false;
            set { if (ConfigVM != null) ConfigVM.IsCsvFileSelected = value; }
        }
        public System.Data.DataView CsvDataView => ConfigVM?.CsvDataView;
        public bool IsExplorerMenuOpen
        {
            get => ConfigVM?.IsExplorerMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsExplorerMenuOpen = value; }
        }
        public bool IsConfigMenuOpen
        {
            get => ConfigVM?.IsConfigMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsConfigMenuOpen = value; }
        }
        public bool IsLoggersMenuOpen
        {
            get => ConfigVM?.IsLoggersMenuOpen ?? false;
            set { if (ConfigVM != null) ConfigVM.IsLoggersMenuOpen = value; }
        }

        // Dynamic tab header: "TERMINALS" for binary APP logs, "DB & CONFIG" otherwise
        public string DbConfigTabHeader =>
            SelectedSession?.HasBinaryAppLogs == true ? "TERMINALS" : "DB & CONFIG";

        // Dynamic tab header: "PLC-FW" for S4 (binary APP logs), "PLC LOGS" otherwise
        public string PlcTabHeader =>
            SelectedSession?.HasBinaryAppLogs == true ? "PLC-FW" : "PLC LOGS";

        // Hide SetupInfo tab when APP files are binary
        public bool HasBinaryAppLogs =>
            SelectedSession?.HasBinaryAppLogs == true;

        public bool HasSessionLoaded => SelectedSession != null;

        /// <summary>True when no ZIP session is loaded but an external file is open in Different Logs.</summary>
        public bool HasExternalFileOnly => SelectedSession == null && DifferentLogsVM?.HasFile == true;

        /// <summary>Controls MainTabs visibility: shown when a session is loaded OR an external file is open.</summary>
        public bool ShowMainTabs => HasSessionLoaded || HasExternalFileOnly;

        // Show Globals tab only when loaded from a ZIP that contains globals files
        public bool HasGlobalsFiles =>
            SelectedSession?.GlobalsFiles != null && SelectedSession.GlobalsFiles.Count > 0 &&
            SelectedSession.FilePath != null && SelectedSession.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        // --- GLOBALS TAB ---
        private ObservableCollection<string> _globalsFileNames = new ObservableCollection<string>();
        public ObservableCollection<string> GlobalsFileNames
        {
            get => _globalsFileNames;
            set { _globalsFileNames = value; OnPropertyChanged(); }
        }

        private string _selectedGlobalsFile;
        public string SelectedGlobalsFile
        {
            get => _selectedGlobalsFile;
            set
            {
                if (_selectedGlobalsFile != value)
                {
                    _selectedGlobalsFile = value;
                    OnPropertyChanged();
                    LoadGlobalsFileContent();
                }
            }
        }

        private ObservableCollection<Models.GlobalEntry> _globalsEntries = new ObservableCollection<Models.GlobalEntry>();
        public ObservableCollection<Models.GlobalEntry> GlobalsEntries
        {
            get => _globalsEntries;
            set { _globalsEntries = value; OnPropertyChanged(); }
        }

        // Backing store: plain List (not ObservableCollection — no UI binding)
        private List<Models.GlobalEntry> _allGlobalsEntries = new List<Models.GlobalEntry>();

        // Debounce for globals search
        private CancellationTokenSource _globalsSearchDebounce;

        private string _globalsSearchText = "";
        public string GlobalsSearchText
        {
            get => _globalsSearchText;
            set
            {
                if (_globalsSearchText != value)
                {
                    _globalsSearchText = value;
                    OnPropertyChanged();
                    DebouncedFilterGlobals();
                }
            }
        }

        public ICommand ClearGlobalsSearchCommand { get; private set; }
        public ICommand ToggleGlobalsDiffsCommand { get; private set; }

        private bool _globalsShowDiffsOnly;
        public bool GlobalsShowDiffsOnly
        {
            get => _globalsShowDiffsOnly;
            set
            {
                if (_globalsShowDiffsOnly != value)
                {
                    _globalsShowDiffsOnly = value;
                    OnPropertyChanged();
                    FilterGlobalsEntries();
                }
            }
        }

        private async void DebouncedFilterGlobals()
        {
            _globalsSearchDebounce?.Cancel();
            _globalsSearchDebounce = new CancellationTokenSource();
            var token = _globalsSearchDebounce.Token;
            try
            {
                await Task.Delay(250, token);
                if (!token.IsCancellationRequested)
                    FilterGlobalsEntries();
            }
            catch (TaskCanceledException) { }
        }

        private void LoadGlobalsFileContent()
        {
            _allGlobalsEntries.Clear();
            _globalsSearchText = "";
            OnPropertyChanged(nameof(GlobalsSearchText));
            _globalsShowDiffsOnly = false;
            OnPropertyChanged(nameof(GlobalsShowDiffsOnly));

            if (string.IsNullOrEmpty(SelectedGlobalsFile) || SelectedSession == null ||
                SelectedSession.GlobalsFiles == null ||
                !SelectedSession.GlobalsFiles.ContainsKey(SelectedGlobalsFile))
            {
                GlobalsEntries = new ObservableCollection<Models.GlobalEntry>();
                return;
            }

            try
            {
                string xmlContent = SelectedSession.GlobalsFiles[SelectedGlobalsFile];
                var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
                var globals = doc.Descendants("Global");
                foreach (var g in globals)
                {
                    string name = g.Element("Name")?.Value ?? "";
                    string value = g.Element("Value")?.Value ?? "";
                    string def = g.Element("Default")?.Value ?? "";
                    var entry = new Models.GlobalEntry
                    {
                        Name = name,
                        Value = value,
                        Default = def,
                        IsRelevant = bool.TryParse(g.Element("IsRelevant")?.Value, out var isRel) && isRel,
                        NameLower = name.ToLowerInvariant(),
                        ValueLower = value.ToLowerInvariant(),
                        DefaultLower = def.ToLowerInvariant()
                    };
                    _allGlobalsEntries.Add(entry);
                }
                // Single batch update — no per-entry CollectionChanged
                GlobalsEntries = new ObservableCollection<Models.GlobalEntry>(_allGlobalsEntries);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadGlobalsFileContent failed", ex);
            }
        }

        private void FilterGlobalsEntries()
        {
            string search = (GlobalsSearchText ?? "").ToLowerInvariant();
            var filtered = new List<Models.GlobalEntry>(_allGlobalsEntries.Count);
            for (int i = 0; i < _allGlobalsEntries.Count; i++)
            {
                var entry = _allGlobalsEntries[i];
                // Diffs-only filter
                if (_globalsShowDiffsOnly &&
                    string.Equals(entry.Value ?? "", entry.Default ?? "", StringComparison.Ordinal))
                    continue;

                // Search filter using pre-cached lowercase
                if (!string.IsNullOrWhiteSpace(search))
                {
                    if ((entry.NameLower == null || !entry.NameLower.Contains(search)) &&
                        (entry.ValueLower == null || !entry.ValueLower.Contains(search)) &&
                        (entry.DefaultLower == null || !entry.DefaultLower.Contains(search)))
                        continue;
                }
                filtered.Add(entry);
            }
            // Single swap instead of Clear + N individual Adds
            GlobalsEntries = new ObservableCollection<Models.GlobalEntry>(filtered);
        }

        public void LoadGlobalsFiles()
        {
            GlobalsFileNames.Clear();
            GlobalsEntries.Clear();
            _allGlobalsEntries.Clear();
            SelectedGlobalsFile = null;

            if (SelectedSession?.GlobalsFiles != null)
            {
                foreach (var fileName in SelectedSession.GlobalsFiles.Keys)
                {
                    GlobalsFileNames.Add(fileName);
                }
            }
            OnPropertyChanged(nameof(HasGlobalsFiles));
        }

        // --- SYSTAB TAB ---
        // Show Systab tab only when loaded from a ZIP that contains systab files
        public bool HasSystabFiles =>
            SelectedSession?.SystabFiles != null && SelectedSession.SystabFiles.Count > 0 &&
            SelectedSession.FilePath != null && SelectedSession.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            SelectedSession.HasBinaryAppLogs;

        private ObservableCollection<Models.SystabTopicNode> _systabTree = new ObservableCollection<Models.SystabTopicNode>();
        public ObservableCollection<Models.SystabTopicNode> SystabTree
        {
            get => _systabTree;
            set { _systabTree = value; OnPropertyChanged(); }
        }

        private Models.SystabTopicNode _selectedSystabNode;
        public Models.SystabTopicNode SelectedSystabNode
        {
            get => _selectedSystabNode;
            set
            {
                if (_selectedSystabNode != value)
                {
                    _selectedSystabNode = value;
                    OnPropertyChanged();
                    LoadSystabEntries();
                }
            }
        }

        private ObservableCollection<Models.SystabEntry> _systabEntries = new ObservableCollection<Models.SystabEntry>();
        public ObservableCollection<Models.SystabEntry> SystabEntries
        {
            get => _systabEntries;
            set { _systabEntries = value; OnPropertyChanged(); }
        }

        private ObservableCollection<Models.SystabEntry> _allSystabEntries = new ObservableCollection<Models.SystabEntry>();

        private string _systabSearchText = "";
        public string SystabSearchText
        {
            get => _systabSearchText;
            set
            {
                if (_systabSearchText != value)
                {
                    _systabSearchText = value;
                    OnPropertyChanged();
                    FilterSystabEntries();
                }
            }
        }

        private bool _systabShowDiffsOnly;
        public bool SystabShowDiffsOnly
        {
            get => _systabShowDiffsOnly;
            set
            {
                if (_systabShowDiffsOnly != value)
                {
                    _systabShowDiffsOnly = value;
                    OnPropertyChanged();
                    FilterSystabEntries();
                }
            }
        }

        public ICommand ClearSystabSearchCommand { get; private set; }

        private void LoadSystabEntries()
        {
            SystabEntries.Clear();
            _allSystabEntries.Clear();
            SystabSearchText = "";
            SystabShowDiffsOnly = false;

            if (_selectedSystabNode == null || _selectedSystabNode.Entries == null || _selectedSystabNode.Entries.Count == 0)
            {
                // If a topic node is selected (has children), show all entries from all children
                if (_selectedSystabNode != null && _selectedSystabNode.Children != null && _selectedSystabNode.Children.Count > 0)
                {
                    foreach (var child in _selectedSystabNode.Children)
                    {
                        if (child.Entries != null)
                        {
                            foreach (var entry in child.Entries)
                            {
                                _allSystabEntries.Add(entry);
                                SystabEntries.Add(entry);
                            }
                        }
                    }
                }
                return;
            }

            foreach (var entry in _selectedSystabNode.Entries)
            {
                _allSystabEntries.Add(entry);
                SystabEntries.Add(entry);
            }
        }

        private void FilterSystabEntries()
        {
            SystabEntries.Clear();
            string search = (SystabSearchText ?? "").ToLower();
            foreach (var entry in _allSystabEntries)
            {
                if (_systabShowDiffsOnly && !entry.IsDifferent)
                    continue;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    if (!(entry.Parameter?.ToLower().Contains(search) == true) &&
                        !(entry.Saved?.ToLower().Contains(search) == true) &&
                        !(entry.Default?.ToLower().Contains(search) == true) &&
                        !(entry.Minimum?.ToLower().Contains(search) == true) &&
                        !(entry.Maximum?.ToLower().Contains(search) == true))
                    {
                        continue;
                    }
                }
                SystabEntries.Add(entry);
            }
        }

        public void LoadSystabFiles()
        {
            SystabTree.Clear();
            SystabEntries.Clear();
            _allSystabEntries.Clear();
            SelectedSystabNode = null;

            if (SelectedSession?.SystabFiles != null && SelectedSession.SystabFiles.Count > 0)
            {
                var tree = Services.SystabParserService.BuildSystabTree(SelectedSession.SystabFiles);
                foreach (var node in tree)
                    SystabTree.Add(node);
            }
            OnPropertyChanged(nameof(HasSystabFiles));
        }

        // --- PANEL VISIBILITY ---
        private bool _isLeftPanelVisible = true;
        public bool IsLeftPanelVisible
        {
            get => _isLeftPanelVisible;
            set { _isLeftPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isRightPanelVisible = true;
        public bool IsRightPanelVisible
        {
            get => _isRightPanelVisible;
            set { _isRightPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isBottomPanelVisible = true;
        public bool IsBottomPanelVisible
        {
            get => _isBottomPanelVisible;
            set { _isBottomPanelVisible = value; OnPropertyChanged(); }
        }

        public ICommand ToggleLeftPanelCommand { get; }
        public ICommand ToggleRightPanelCommand { get; }

        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };


        public event Action<LogEntry> RequestScrollToLog;
        public event Action<LogEntry, bool> RequestScrollToLogPreservePosition;
        public event Action<LogEntry> RequestSaveScrollPosition;
        public event Action<string> RequestScrollToBottom;

        /// <summary>
        /// Public method to trigger scroll to log event from child ViewModels
        /// </summary>
        public void ScrollToLog(LogEntry log)
        {
            RequestScrollToLog?.Invoke(log);
        }

        /// <summary>
        /// Scrolls to log while preserving its visual position on screen (used when filter changes)
        /// </summary>
        public void ScrollToLogPreservePosition(LogEntry log)
        {
            RequestScrollToLogPreservePosition?.Invoke(log, true);
        }

        /// <summary>
        /// Saves the current scroll position before filter changes (call BEFORE applying filters)
        /// </summary>
        public void SaveScrollPosition(LogEntry log)
        {
            RequestSaveScrollPosition?.Invoke(log);
        }

        /// <summary>
        /// Scrolls a specific tab grid to its last row. Tab names: "PLC", "FILTERED", "APP"
        /// </summary>
        public void ScrollTabToBottom(string tabName)
        {
            RequestScrollToBottom?.Invoke(tabName);
        }

        // --- SELECTED TAB INDEX ---
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    if (_selectedTabIndex == AppConstants.TAB_APP) // APP Tab
                    {
                        LeftTabIndex = 1; // LOGGERS
                    }
                    else if (_selectedTabIndex == AppConstants.TAB_PLC) // PLC Tab
                    {
                        LeftTabIndex = 0; // EXPLORER
                    }

                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                    OnPropertyChanged(nameof(IsPLCTabSelected));
                    OnPropertyChanged(nameof(IsAppTabSelected));
                    OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                    OnPropertyChanged(nameof(IsExportVisible));
                    OnPropertyChanged(nameof(ActiveLoggerTree));
                    OnPropertyChanged(nameof(LoggerTabTitle));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));

                    // Auto-manage panel visibility per tab type
                    // Tabs 0,1 (PLC, APP) = left + right + bottom panels visible
                    // Tab 9 (CHARTS), 10 (CPR), 11 (STEP RECORDER), 12 (DIFFERENT LOGS) = all panels hidden
                    // Other tabs = left + right hidden, bottom visible
                    if (_selectedTabIndex == AppConstants.TAB_PLC || _selectedTabIndex == AppConstants.TAB_APP)
                    {
                        IsLeftPanelVisible = true;
                        IsRightPanelVisible = true;
                        IsBottomPanelVisible = true;
                    }
                    else if (_selectedTabIndex == AppConstants.TAB_CHARTS  || // CHARTS
                             _selectedTabIndex == AppConstants.TAB_CPR || // CPR
                             _selectedTabIndex == AppConstants.TAB_STEP_RECORDER || // STEP RECORDER
                             _selectedTabIndex == AppConstants.TAB_DIFFERENT_LOGS)   // DIFFERENT LOGS
                    {
                        IsLeftPanelVisible = false;
                        IsRightPanelVisible = false;
                        IsBottomPanelVisible = false;
                    }
                    else // EVENTS, SCREENSHOTS, CONFIG, DB CONFIG, SETUP INFO, GLOBALS, SYSTAB
                    {
                        IsLeftPanelVisible = false;
                        IsRightPanelVisible = false;
                        IsBottomPanelVisible = true;
                    }

                    // Block RequestSyncScroll from firing on the initial ScrollChanged of
                    // the newly-visible tab. WPF re-renders the DataGrid when a tab becomes
                    // visible, which raises ScrollChanged → TriggerTimeSyncScroll even though
                    // no user scroll happened. Without this flag:
                    //   Bug 1: the render-ScrollChanged overwrites _pendingSyncLog so the
                    //          check below finds _pendingSyncTabIndex != _selectedTabIndex
                    //          and the scroll is skipped (user must switch twice).
                    //   Bug 2: every tab switch creates a new spurious pending sync that
                    //          advances the visible time forward.
                    _isTabSwitching = true;

                    // Apply pending sync scroll when user switches to the target tab
                    if (_pendingSyncLog != null && _selectedTabIndex == _pendingSyncTabIndex)
                    {
                        var logToScroll = _pendingSyncLog;
                        _pendingSyncLog = null;
                        _pendingSyncTabIndex = -1;
                        Application.Current?.Dispatcher?.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Loaded,
                            new Action(() =>
                            {
                                RequestScrollToLog?.Invoke(logToScroll);
                                // Tell the view's ApplicationIdle callback NOT to overwrite
                                // this synced position with a scroll-to-bottom.
                                TimeSyncScrollWasApplied = true;
                                // Clear AFTER the scroll so user-initiated scrolls that
                                // happen immediately after the tab lands are not blocked.
                                _isTabSwitching = false;
                            }));
                    }
                    else
                    {
                        // No pending sync — still must clear the flag once layout settles.
                        // Also reset TimeSyncScrollWasApplied so a stale true value from a
                        // previous APP→PLC sync (where PLC is never in _pendingScrollToBottom
                        // and the flag would never be cleared) does not block a future
                        // first-open scroll-to-bottom on any tab.
                        Application.Current?.Dispatcher?.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Loaded,
                            new Action(() =>
                            {
                                TimeSyncScrollWasApplied = false;
                                _isTabSwitching = false;
                            }));
                    }
                }
            }
        }

        public bool IsPLCTabSelected => _selectedTabIndex == AppConstants.TAB_PLC;
        public bool IsAppTabSelected => _selectedTabIndex == AppConstants.TAB_APP;

        /// <summary>Print Analysis visible only on APP tab AND when NOT S4-5 (S6 only).</summary>
        public bool IsPrintAnalysisVisible => IsAppTabSelected && !HasBinaryAppLogs;

        /// <summary>Button text changes: "📊 Statistics" for S4-5, "⚙ Reports" for S6.</summary>
        public string ReportsButtonText => HasBinaryAppLogs ? "📊 Statistics" : "⚙ Reports";

        // Dynamic logger tree: PLC loggers for PLC tabs, APP loggers for APP tab
        public ObservableCollection<LoggerNode> ActiveLoggerTree =>
            IsPLCTabSelected ? PlcLoggerTreeRoot : LoggerTreeRoot;

        public string LoggerTabTitle =>
            IsPLCTabSelected ? "PLC LOGGERS" : "APP LOGGERS";
        public bool IsExportVisible => _selectedTabIndex == AppConstants.TAB_PLC || _selectedTabIndex == AppConstants.TAB_CHARTS;


        private int _leftTabIndex;
        public int LeftTabIndex
        {
            get => _leftTabIndex;
            set { _leftTabIndex = value; OnPropertyChanged(); }
        }

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        private IReadOnlyList<PluginColumnDef> _currentPluginColumns;
        public IReadOnlyList<PluginColumnDef> CurrentPluginColumns
        {
            get => _currentPluginColumns;
            set { _currentPluginColumns = value; OnPropertyChanged(); }
        }

        /// <summary>Exposes the plugin loader so child VMs can query loaded plugins.</summary>
        public Services.Interfaces.IPluginLoader GetPluginLoader()
            => _logService?.GetPluginLoader();

        private string _setupInfo;
        public string SetupInfo
        {
            get => _setupInfo;
            set { _setupInfo = value; OnPropertyChanged(); }
        }

        private string _pressConfig;
        public string PressConfig
        {
            get => _pressConfig;
            set { _pressConfig = value; OnPropertyChanged(); }
        }

        private string _versionsInfo;
        public string VersionsInfo
        {
            get => _versionsInfo;
            set { _versionsInfo = value; OnPropertyChanged(); }
        }

        private LogEntry _selectedLog;
        public LogEntry SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedLog)); }
        }

        public bool HasSelectedLog => _selectedLog != null;

        // Log Details panel pin/auto-hide state — pinned by default so it's always visible
        private bool _isLogDetailsPinned = true;
        public bool IsLogDetailsPinned
        {
            get => _isLogDetailsPinned;
            set { _isLogDetailsPinned = value; OnPropertyChanged(); }
        }

        public ICommand ToggleLogDetailsPinCommand { get; private set; }

        private bool _isSearchSyntaxValid = true;
        public bool IsSearchSyntaxValid
        {
            get => _isSearchSyntaxValid;
            set
            {
                if (_isSearchSyntaxValid != value)
                {
                    _isSearchSyntaxValid = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _searchSyntaxError;
        public string SearchSyntaxError
        {
            get => _searchSyntaxError;
            set
            {
                if (_searchSyntaxError != value)
                {
                    _searchSyntaxError = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ValidateSearchSyntax()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
                return;
            }

            if (QueryParserService.HasBooleanOperators(SearchText))
            {
                var parser = new QueryParserService();
                var result = parser.Parse(SearchText, out string errorMessage);

                if (result == null)
                {
                    IsSearchSyntaxValid = false;
                    SearchSyntaxError = errorMessage;
                }
                else
                {
                    IsSearchSyntaxValid = true;
                    SearchSyntaxError = null;
                }
            }
            else
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
            }
        }

        public bool IsFilterActive
        {
            get
            {
                if (SelectedTabIndex == AppConstants.TAB_DIFFERENT_LOGS) return DifferentLogsVM?.IsFilterActive ?? false;
                return SelectedTabIndex == AppConstants.TAB_APP ? IsAppFilterActive : IsMainFilterActive;
            }
            set
            {
                // Different Logs tab: toggle its own filter
                if (SelectedTabIndex == AppConstants.TAB_DIFFERENT_LOGS)
                {
                    var diffVM = DifferentLogsVM;
                    if (diffVM != null && diffVM.IsFilterActive != value)
                    {
                        if (value && diffVM.FilterRoot == null) return; // No stored filter
                        diffVM.IsFilterActive = value;
                        OnPropertyChanged();

                        if (value && diffVM.FilterRoot != null)
                        {
                            var filtered = diffVM.AllLogEntries
                                .Where(l => FilterVM.EvaluateFilterNode(l, diffVM.FilterRoot))
                                .ToList();
                            diffVM.FilteredEntries = new ObservableCollection<LogEntry>(filtered);
                        }
                        else
                        {
                            diffVM.FilteredEntries = new ObservableCollection<LogEntry>(diffVM.AllLogEntries);
                        }
                    }
                    return;
                }

                // Save the currently selected log and its scroll position BEFORE changing filter state
                var savedSelectedLog = SelectedLog;
                if (savedSelectedLog != null)
                {
                    SaveScrollPosition(savedSelectedLog);
                }

                if (SelectedTabIndex == AppConstants.TAB_APP)
                {
                    if (FilterVM != null && FilterVM.IsAppFilterActive != value)
                    {
                        // Only toggle if there's a stored filter to show/hide
                        // If no stored filter and trying to activate, do nothing
                        if (value && !FilterVM.HasAppStoredFilter)
                            return;

                        FilterVM.IsAppFilterActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
                else
                {
                    if (FilterVM != null && FilterVM.IsMainFilterActive != value)
                    {
                        // Only toggle if there's a stored filter to show/hide
                        // If no stored filter and trying to activate, do nothing
                        if (value && !FilterVM.HasMainStoredFilter)
                            return;

                        FilterVM.IsMainFilterActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(value);

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
            }
        }

        public bool IsFilterOutActive
        {
            get => SelectedTabIndex == AppConstants.TAB_APP ? IsAppFilterOutActive : IsMainFilterOutActive;
            set
            {
                // Save the currently selected log and its scroll position BEFORE changing filter state
                var savedSelectedLog = SelectedLog;
                if (savedSelectedLog != null)
                {
                    SaveScrollPosition(savedSelectedLog);
                }

                if (SelectedTabIndex == AppConstants.TAB_APP)
                {
                    if (FilterVM != null && FilterVM.IsAppFilterOutActive != value)
                    {
                        // Only toggle if there's a stored filter out to show/hide
                        if (value && !FilterVM.HasAppStoredFilterOut)
                            return;

                        FilterVM.IsAppFilterOutActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
                else
                {
                    if (FilterVM != null && FilterVM.IsMainFilterOutActive != value)
                    {
                        // Only toggle if there's a stored filter out to show/hide
                        if (value && !FilterVM.HasMainStoredFilterOut)
                            return;

                        FilterVM.IsMainFilterOutActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(FilterVM.IsMainFilterActive);

                        // Restore the selected log and scroll to it, preserving visual position
                        // Use Dispatcher to ensure UI has fully updated before scrolling
                        if (savedSelectedLog != null)
                        {
                            var logToRestore = savedSelectedLog;
                            Application.Current.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ContextIdle,
                                new Action(() =>
                                {
                                    SelectedLog = logToRestore;
                                    ScrollToLogPreservePosition(logToRestore);
                                }));
                        }
                    }
                }
            }
        }


        private string _selectedFont = "Segoe UI";
        public string SelectedFont
        {
            get => _selectedFont;
            set { if (_selectedFont != value) { _selectedFont = value; OnPropertyChanged(); UpdateContentFont(_selectedFont); } }
        }

        private bool _isBold;
        public bool IsBold
        {
            get => _isBold;
            set { if (_isBold != value) { _isBold = value; OnPropertyChanged(); UpdateContentFontWeight(value); } }
        }

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                _isDarkMode = value;
                ApplyTheme(value);
                OnPropertyChanged();
                Properties.Settings.Default.IsDarkMode = value;
                Properties.Settings.Default.Save();
            }
        }

        private double _gridFontSize = 12;
        public double GridFontSize
        {
            get => _gridFontSize;
            set { _gridFontSize = value; OnPropertyChanged(); }
        }

        private double _screenshotZoom = 400;
        public double ScreenshotZoom
        {
            get => _screenshotZoom;
            set { _screenshotZoom = value; OnPropertyChanged(); }
        }

        private int _contextSeconds = 10;
        public int ContextSeconds
        {
            get => _contextSeconds;
            set { if (_contextSeconds != value) { _contextSeconds = value; OnPropertyChanged(); } }
        }

        private string _selectedTimeUnit = "Seconds";
        public string SelectedTimeUnit
        {
            get => _selectedTimeUnit;
            set { _selectedTimeUnit = value; OnPropertyChanged(); }
        }


        // Time-Sync Scrolling
        private bool _isTimeSyncEnabled;
        public bool IsTimeSyncEnabled
        {
            get => _isTimeSyncEnabled;
            set
            {
                _isTimeSyncEnabled = value;
                OnPropertyChanged();
                StatusMessage = value ? "🔗 Time-Sync ENABLED" : "⛓ Time-Sync DISABLED";
            }
        }

        private bool _isSyncScrolling = false;
        // True while a tab switch is in progress (between SelectedTabIndex change and
        // DispatcherPriority.Loaded firing). Blocks RequestSyncScroll so that the
        // initial ScrollChanged caused by the newly-visible tab's render cycle does not
        // overwrite _pendingSyncLog (Bug 1) or create a spurious sync (Bug 2).
        private bool _isTabSwitching = false;

        // Set to true inside the BeginInvoke(Loaded) that fires the sync scroll.
        // Read and cleared by the view's TabControl_SelectionChanged ApplicationIdle callback.
        // Without this flag, the dispatch order causes:
        //   Loaded(6): sync scroll applied → tab at correct synced time
        //   ApplicationIdle(2): ScrollGridToBottom fires → overwrites synced position with last line
        public bool TimeSyncScrollWasApplied { get; set; } = false;

        // Double (not int) so that sub-second APP/PLC clock differences are preserved.
        // An int would round a 1.7s offset to 1s, causing a visible ~0.7s search error.
        private double _timeSyncOffsetSeconds = 0;
        // Pending sync: stores the log to scroll to when user switches to target tab
        private LogEntry _pendingSyncLog;
        private int _pendingSyncTabIndex = -1;

        public double TimeSyncOffsetSeconds
        {
            get => _timeSyncOffsetSeconds;
            set { _timeSyncOffsetSeconds = value; OnPropertyChanged(); }
        }

        private bool _showSyncedTimeColumn;
        public bool ShowSyncedTimeColumn
        {
            get => _showSyncedTimeColumn;
            set { _showSyncedTimeColumn = value; OnPropertyChanged(); }
        }

        /// <summary>True only when at least one PLC log has SyncedTime populated.
        /// Prevents the user from manually enabling the column when there is no sync data.</summary>
        private bool _hasTimeSyncData;
        public bool HasTimeSyncData
        {
            get => _hasTimeSyncData;
            set
            {
                _hasTimeSyncData = value;
                if (!value) ShowSyncedTimeColumn = false; // auto-hide when data is gone
                OnPropertyChanged();
            }
        }

        // --- Commands ---
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand RemoveSessionCommand { get; }
        public ICommand MarkRowCommand { get; }
        public ICommand NextMarkedCommand { get; }
        public ICommand PrevMarkedCommand { get; }
        public ICommand JumpToLogCommand { get; }
        public ICommand OpenJiraCommand { get; }
        public ICommand OpenKibanaCommand { get; }
        public ICommand OpenOutlookCommand { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand ShowFailuresCommand { get; private set; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand RemoveConfigCommand { get; }
        public ICommand ApplyConfigCommand { get; }
        public ICommand ShowConfigsFolderCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand OpenLoggerFilterCommand { get; }
        public ICommand OpenMethodFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand StartRangeCommand { get; }
        public ICommand EndRangeCommand { get; }
        public ICommand ClearRangeCommand { get; }
        public bool HasRangeStart => FilterVM?.HasRangeStart ?? false;
        public List<Models.ActiveFilterItem> ActiveFilters => FilterVM?.GetActiveFilters() ?? new List<Models.ActiveFilterItem>();
        public bool HasActiveFilters => ActiveFilters.Count > 0;
        public ICommand ClearActiveFilterCommand { get; private set; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ViewLogDetailsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenPluginTesterCommand { get; }
        public ICommand ToggleBoldCommand { get; }
        public ICommand OpenFontsWindowCommand { get; }
        public ICommand OpenMarkedLogsWindowCommand { get; }
        public ICommand ExportParsedDataCommand { get; }
        public ICommand RunAnalysisCommand { get; }
        public ICommand FilterToStateCommand { get; }
        public ICommand OpenStatesWindowCommand { get; }
        public ICommand OpenSnakeGameCommand { get; }
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }
        public ICommand ToggleExplorerMenuCommand { get; }
        public ICommand ToggleConfigMenuCommand { get; }
        public ICommand ToggleLoggersMenuCommand { get; }
        public ICommand TreeShowThisCommand { get; }
        public ICommand TreeHideThisCommand { get; }
        public ICommand TreeShowOnlyThisCommand { get; }
        public ICommand TreeShowWithChildrenCommand { get; }
        public ICommand TreeHideWithChildrenCommand { get; }
        public ICommand TreeShowAllCommand { get; }
        public ICommand OpenIndigoInvadersCommand { get; }
        public ICommand FilterAppErrorsCommand { get; }
        public ICommand OpenVisualAnalysisCommand { get; }
        public ICommand ResetTimeFocusCommand { get; }
        public ICommand ToggleTimeSyncCommand { get; }
        public ICommand OpenTimeRangeFilterCommand { get; }

        public ICommand AddAnnotationCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }
        public ICommand SaveCaseCommand { get; }
        public ICommand LoadCaseCommand { get; }
        public ICommand OpenGlobalGrepCommand { get; }
        public ICommand OpenStripeAnalysisCommand { get; }
        public ICommand OpenComparisonCommand { get; }
        public ICommand SetAsDefaultCommand { get; }
        public ICommand ResetDefaultsCommand { get; }

        public MainViewModel()
            : this(new LogFileService(new PluginLoader()), new LogColoringService(), new CsvExportService())
        {
        }

        public MainViewModel(ILogFileService logService, ILogColoringService coloringService, ICsvExportService csvService)
        {
            _logService = logService;
            _coloringService = coloringService;
            _csvService = csvService;
            _isTimeSyncEnabled = false;

            // Initialize child ViewModels
            SessionVM = new LogSessionViewModel(this, _logService, _coloringService);
            FilterVM = new FilterSearchViewModel(this, SessionVM);
            CaseVM = new CaseManagementViewModel(this, SessionVM, FilterVM);
            LiveVM = new LiveMonitoringViewModel(this, SessionVM, FilterVM, CaseVM, _logService, _coloringService);
            ConfigVM = new ConfigExplorerViewModel(this, SessionVM);
            ChartVM = new ChartTabViewModel(this);
            CprVM = new CprAnalysisViewModel();
            DifferentLogsVM = new DifferentLogsViewModel(_logService.GetPluginLoader());
            DifferentLogsVM.GetCurrentZipPath = () => SelectedSession?.FilePath;
            StepRecorderVM = new StepRecorderViewModel();

            // Set dependencies
            SessionVM.SetDependencies(FilterVM, CaseVM, ConfigVM, LiveVM);

            // Subscriptions
            SessionVM.PropertyChanged += SessionVM_PropertyChanged;
            FilterVM.PropertyChanged += FilterVM_PropertyChanged;
            LiveVM.PropertyChanged += LiveVM_PropertyChanged;
            DifferentLogsVM.PropertyChanged += DifferentLogsVM_PropertyChanged;

            ToggleVisualModeCommand = new RelayCommand(o => IsVisualMode = !IsVisualMode);

            TreeShowThisCommand = FilterVM.TreeShowThisCommand;
            TreeHideThisCommand = FilterVM.TreeHideThisCommand;
            TreeShowOnlyThisCommand = FilterVM.TreeShowOnlyThisCommand;
            TreeShowWithChildrenCommand = FilterVM.TreeShowWithChildrenCommand;
            TreeHideWithChildrenCommand = FilterVM.TreeHideWithChildrenCommand;
            TreeShowAllCommand = FilterVM.TreeShowAllCommand;
            OpenTimeRangeFilterCommand = FilterVM.OpenTimeRangeFilterCommand;
            OpenIndigoInvadersCommand = new RelayCommand(OpenIndigoInvaders);

            _allLogsCache = SessionVM.AllLogsCache;
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            ToggleExplorerMenuCommand = new RelayCommand(o => IsExplorerMenuOpen = !IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => IsConfigMenuOpen = !IsConfigMenuOpen);
            ToggleLoggersMenuCommand = new RelayCommand(o => IsLoggersMenuOpen = !IsLoggersMenuOpen);
            ToggleTimeSyncCommand = new RelayCommand(o => IsTimeSyncEnabled = !IsTimeSyncEnabled);
            ToggleLeftPanelCommand = new RelayCommand(o => IsLeftPanelVisible = !IsLeftPanelVisible);
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelVisible = !IsRightPanelVisible);
            BrowseTableCommand = ConfigVM.BrowseTableCommand;
            CopyTableNameCommand = new RelayCommand(CopyTableName);

            // --- UPDATED ANNOTATION COMMANDS ---
            ToggleAnnotationCommand = new RelayCommand(ToggleAnnotation);
            CloseAnnotationCommand = new RelayCommand(CloseAnnotation);
            ToggleAllAnnotationsCommand = new RelayCommand(ToggleAllAnnotations);

            LoadCommand = SessionVM.LoadCommand;
            ClearCommand = new RelayCommand(o => { SessionVM.ClearCommand.Execute(o); IsExplorerMenuOpen = false; });
            RemoveSessionCommand = SessionVM.RemoveSessionCommand;
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);
            FilterAppErrorsCommand = new RelayCommand(FilterAppErrors);
            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);

            OpenMarkedLogsWindowCommand = new RelayCommand(o => { OpenMarkedLogsWindow(o); IsExplorerMenuOpen = false; });
            OpenStatesWindowCommand = new RelayCommand(o => { OpenStatesWindow(o); IsExplorerMenuOpen = false; });
            ExportParsedDataCommand = new RelayCommand(o => { ExportParsedData(o); IsExplorerMenuOpen = false; });
            RunAnalysisCommand = new RelayCommand(o => { RunAnalysis(o); IsExplorerMenuOpen = false; });
            OpenGlobalGrepCommand = new RelayCommand(o => { OpenGlobalGrepWindow(); IsExplorerMenuOpen = false; });
            OpenStripeAnalysisCommand = new RelayCommand(o => { OpenStripeAnalysisWindow(); IsExplorerMenuOpen = false; });
            OpenComparisonCommand = new RelayCommand(o => { OpenComparisonWindow(); }, o => SessionVM.AllLogsCache?.Count > 0 || SessionVM.AllAppLogsCache?.Count > 0);

            ToggleSearchCommand = FilterVM.ToggleSearchCommand;
            CloseSearchCommand = FilterVM.CloseSearchCommand;
            ShowFailuresCommand = new RelayCommand(_ => ShowFailuresAnalysis());
            OpenFilterWindowCommand = FilterVM.OpenFilterWindowCommand;
            OpenColoringWindowCommand = CaseVM.OpenColoringWindowCommand;

            SaveConfigCommand = new RelayCommand(o => { SaveConfiguration(o); IsConfigMenuOpen = false; });
            LoadConfigCommand = new RelayCommand(o => { LoadConfigurationFromFile(o); IsConfigMenuOpen = false; });
            RemoveConfigCommand = new RelayCommand(o => { RemoveConfiguration(o); IsConfigMenuOpen = false; }, o => SelectedConfig != null);
            ApplyConfigCommand = new RelayCommand(ApplyConfiguration);
            ShowConfigsFolderCommand = CaseVM.ShowConfigsFolderCommand;

            FilterOutCommand = FilterVM.FilterOutCommand;
            FilterOutThreadCommand = FilterVM.FilterOutThreadCommand;
            OpenThreadFilterCommand = FilterVM.OpenThreadFilterCommand;
            OpenLoggerFilterCommand = FilterVM.OpenLoggerFilterCommand;
            OpenMethodFilterCommand = FilterVM.OpenMethodFilterCommand;
            FilterContextCommand = FilterVM.FilterContextCommand;
            UndoFilterOutCommand = FilterVM.UndoFilterOutCommand;
            StartRangeCommand = FilterVM.StartRangeCommand;
            EndRangeCommand = FilterVM.EndRangeCommand;
            ClearRangeCommand = FilterVM.ClearRangeCommand;

            ResetTimeFocusCommand = new RelayCommand(ResetTimeFocus);

            ViewLogDetailsCommand = new RelayCommand(ViewLogDetails);
            ToggleThemeCommand = new RelayCommand(o => IsDarkMode = !IsDarkMode);
            ToggleBoldCommand = new RelayCommand(o => IsBold = !IsBold);
            OpenSettingsCommand = new RelayCommand(OpenSettingsWindow);
            OpenHelpCommand = new RelayCommand(o => WindowManager.OpenWindow(new Views.HelpWindow()));
            OpenPluginTesterCommand = new RelayCommand(o => WindowManager.GetOrCreate<Views.PluginTesterWindow>(() => new Views.PluginTesterWindow()));
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            OpenSnakeGameCommand = new RelayCommand(OpenSnakeGame);

            FilterToStateCommand = new RelayCommand(FilterToState);

            ZoomInCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Min(5000, ScreenshotZoom + 100);
                else GridFontSize = Math.Min(30, GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == AppConstants.TAB_SCREENSHOTS) ScreenshotZoom = Math.Max(100, ScreenshotZoom - 100);
                else GridFontSize = Math.Max(8, GridFontSize - 1);
            });

            LivePlayCommand = LiveVM.LivePlayCommand;
            LivePauseCommand = LiveVM.LivePauseCommand;
            LiveClearCommand = LiveVM.LiveClearCommand;

            ClearGlobalsSearchCommand    = new RelayCommand(o => GlobalsSearchText = "");
            ToggleGlobalsDiffsCommand    = new RelayCommand(o => GlobalsShowDiffsOnly = !GlobalsShowDiffsOnly);
            ClearSystabSearchCommand = new RelayCommand(o => { SystabSearchText = ""; SystabShowDiffsOnly = false; });
            ToggleLogDetailsPinCommand = new RelayCommand(o => IsLogDetailsPinned = !IsLogDetailsPinned);
            ClearActiveFilterCommand = new RelayCommand(o =>
            {
                if (o is string key)
                    FilterVM?.RemoveActiveFilter(key);
            });

            AddAnnotationCommand = new RelayCommand(AddAnnotation);
            DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation);
            SaveCaseCommand = new RelayCommand(SaveCase);
            LoadCaseCommand = new RelayCommand(LoadCase);

            SetAsDefaultCommand = new RelayCommand(SetCurrentAsDefault);
            ResetDefaultsCommand = new RelayCommand(ResetDefaults);

            _isDarkMode = Properties.Settings.Default.IsDarkMode;
            ApplyTheme(_isDarkMode);
            LoadSavedConfigurations();
            LoadUserDefaults();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            // Save the currently selected log and its scroll position BEFORE toggling filter
            var savedSelectedLog = SelectedLog;
            if (savedSelectedLog != null)
            {
                SaveScrollPosition(savedSelectedLog);
            }

            ToggleFilterView(IsFilterActive);

            // Restore the selected log and scroll to it, preserving visual position
            // Use Dispatcher to ensure UI has fully updated before scrolling
            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        SelectedLog = logToRestore;
                        ScrollToLogPreservePosition(logToRestore);
                    }));
            }
        }

        private void InitializeVisualMode()
        {
            // Use filtered logs if time range is active, otherwise use all logs
            var logsToUse = FilterVM.IsGlobalTimeRangeActive ? Logs : (SessionVM.AllLogsCache ?? Logs);
            if (VisualTimelineVM != null)
            {
                // S4-5 (binary APP): skip Events on timeline — user only needs states + errors
                var eventsToShow = HasBinaryAppLogs ? null : Events;
                VisualTimelineVM.LoadData(logsToUse.ToList(), eventsToShow);
            }
        }

        public void ProcessFiles(string[] filePaths, Action<LogSessionData> onLoadComplete = null)
            => SessionVM?.ProcessFiles(filePaths, onLoadComplete);


        // --- NEW ANNOTATION LOGIC ---

        private void ToggleAnnotation(object parameter)
        {
            if (parameter is LogEntry log && log.HasAnnotation)
                log.IsAnnotationExpanded = !log.IsAnnotationExpanded;
        }
        private void ToggleAllAnnotations(object obj)
        {
            IEnumerable<LogEntry> targetList = null;

            if (SelectedTabIndex == AppConstants.TAB_APP) // APP Tab
            {
                targetList = SessionVM?.AllAppLogsCache;
            }
            else // PLC Tab
            {
                targetList = SessionVM?.AllLogsCache;
            }

            if (targetList == null || !targetList.Any()) return;

            // Get only logs with annotations
            var logsWithAnnotations = targetList.Where(l => l.HasAnnotation).ToList();
            if (!logsWithAnnotations.Any()) return;

            // Check if any is expanded to determine direction
            bool anyExpanded = logsWithAnnotations.Any(l => l.IsAnnotationExpanded);
            bool newState = !anyExpanded;

            // Update all annotations
            foreach (var log in logsWithAnnotations)
            {
                log.IsAnnotationExpanded = newState;
            }

            ShowAllAnnotations = newState;
            StatusMessage = newState ? "All annotations expanded" : "All annotations collapsed";
        }

        private void CloseAnnotation(object parameter) => CaseVM?.CloseAnnotationCommand.Execute(parameter);

        private string LoadSqliteContent(byte[] dbBytes)
        {
            var sb = new System.Text.StringBuilder();
            string tempDbPath = null;

            try
            {
                tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, dbBytes);

                using (var connection = new SQLiteConnection($"Data Source={tempDbPath};Read Only=True;"))
                {
                    connection.Open();
                    var tables = new List<string>();
                    using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { tables.Add(reader.GetString(0)); }
                    }

                    sb.AppendLine($"=== SQLite Database: {tables.Count} tables ===");
                    sb.AppendLine();

                    foreach (var tableName in tables)
                    {
                        sb.AppendLine($"━━━ TABLE: {tableName} ━━━");
                        using (var countCmd = new SQLiteCommand($"SELECT COUNT(*) FROM [{EscapeSqlBracketId(tableName)}]", connection))
                        {
                            var count = countCmd.ExecuteScalar();
                            sb.AppendLine($"Rows: {count}");
                        }
                        using (var cmd = new SQLiteCommand($"SELECT * FROM [{EscapeSqlBracketId(tableName)}] LIMIT 100", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            var columns = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++) { columns.Add(reader.GetName(i)); }
                            sb.AppendLine($"Columns: {string.Join(", ", columns)}");
                            sb.AppendLine();

                            int rowNum = 0;
                            while (reader.Read() && rowNum < 100)
                            {
                                sb.AppendLine($"--- Row {++rowNum} ---");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
                                    if (value.Length > 500) value = value.Substring(0, 500) + "...";
                                    sb.AppendLine($"  {columns[i]}: {value}");
                                }
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error reading SQLite database: {ex.Message}");
            }
            finally
            {
                if (tempDbPath != null && File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch (Exception ex) { AppLogger.Error("Temp DB cleanup failed", ex); }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a SQL identifier for safe use in bracket-quoted context ([identifier]).
        /// </summary>
        private static string EscapeSqlBracketId(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return identifier.Replace("]", "]]");
        }

        private List<StateEntry> CalculateStatesInternal(IEnumerable<LogEntry> logs)
        {
            var statesList = new List<StateEntry>();
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();

            var transitionLogs = sortedLogs.Where(l => l.ThreadName != null &&
                                                 l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message != null &&
                                                 l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                                                 l.Message.Contains("->"))
                                     .ToList();

            if (transitionLogs.Count == 0) return statesList;

            var failureEvents = sortedLogs
                .Where(l => l.ThreadName == "Events" &&
                           l.Message != null &&
                           l.Message.Contains("Enqueue event PLC_FAILURE_STATE_CHANGE from Main_PLC"))
                .Select(l => l.Date)
                .ToHashSet();

            DateTime logEndLimit = sortedLogs.Last().Date;

            for (int i = 0; i < transitionLogs.Count; i++)
            {
                var currentLog = transitionLogs[i];
                var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length < 2) continue;

                string fromStateRaw = parts[0].Replace("PlcMngr:", "").Trim();
                string toStateRaw = parts[1].Trim();

                var entry = new StateEntry
                {
                    StateName = toStateRaw,
                    TransitionTitle = $"{fromStateRaw} -> {toStateRaw}",
                    StartTime = currentLog.Date,
                    LogReference = currentLog,
                    Status = "OK",
                    StatusColor = Brushes.LightGreen
                };

                if (i < transitionLogs.Count - 1)
                    entry.EndTime = transitionLogs[i + 1].Date;
                else
                    entry.EndTime = logEndLimit;

                bool hasFailureEvent = failureEvents.Any(eventTime =>
                    eventTime >= entry.StartTime && eventTime <= (entry.EndTime ?? logEndLimit));

                if (hasFailureEvent)
                {
                    entry.Status = "FAILED";
                    entry.StatusColor = Brushes.Red;
                }
                else if (entry.StateName.Equals("GET_READY", StringComparison.OrdinalIgnoreCase))
                {
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLogParts = transitionLogs[i + 1].Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextLogParts.Length >= 2 && !nextLogParts[1].Trim().Equals("DYNAMIC_READY", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = "FAILED";
                            entry.StatusColor = Brushes.Red;
                        }
                    }
                }
                else if (entry.StateName.Equals("MECH_INIT", StringComparison.OrdinalIgnoreCase))
                {
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLogParts = transitionLogs[i + 1].Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextLogParts.Length >= 2 && !nextLogParts[1].Trim().Equals("STANDBY", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = "FAILED";
                            entry.StatusColor = Brushes.Red;
                        }
                    }
                }

                statesList.Add(entry);
            }
            // מיון מהישן לחדש (חדשים למטה)
            return statesList.OrderBy(s => s.StartTime).ToList();
        }

        public void StartBackgroundAnalysis(LogSessionData session)
        {
            IsAnalysisRunning = true;

            Task.Run(() =>
            {
                try
                {
                    session.CachedStates = CalculateStatesInternal(session.Logs);
                    session.CachedAnalysis = new UniversalStateFailureAnalyzer().Analyze(session);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("StartBackgroundAnalysis failed", ex);
                }
                finally
                {
                    IsAnalysisRunning = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SelectedSession == session)
                            StatusMessage = "Background Analysis Complete.";
                    });
                }
            });
        }

        private void LiveClear(object obj)
        {
            IsRunning = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_collectionLock)
                {
                    if (SessionVM.AllLogsCache != null) SessionVM.AllLogsCache.Clear();
                    FilteredLogs?.Clear();
                    SelectedLog = null;
                }
            });

            if (IsLiveMode)
            {
                IsRunning = true;
                StatusMessage = "Cleared. Monitoring continues...";
            }
            else
            {
                StatusMessage = "Logs cleared.";
            }
        }

        private void ClearLogs(object obj)
        {
            try
            {
                SessionVM?.ClearCommand.Execute(null);

                CaseVM?.ClearMarkedLogs();
                CaseVM?.LogAnnotations?.Clear();

                if (FilterVM != null)
                {
                    FilterVM.IsMainFilterActive = false;
                    FilterVM.IsAppFilterActive = false;
                    FilterVM.IsMainFilterOutActive = false;
                    FilterVM.IsAppFilterOutActive = false;
                    FilterVM.IsAppTimeFocusActive = false;
                    FilterVM.IsTimeFocusActive = false;

                    FilterVM.LastFilteredAppCache?.Clear();
                    FilterVM.LastFilteredCache?.Clear();
                    FilterVM.NegativeFilters?.Clear();
                    FilterVM.ActiveThreadFilters?.Clear();
                    FilterVM.ActiveLoggerFilters?.Clear();
                    FilterVM.ActiveMethodFilters?.Clear();

                    if (LoggerTreeRoot != null)
                    {
                        LoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(LoggerTreeRoot));
                    }
                    if (PlcLoggerTreeRoot != null)
                    {
                        PlcLoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(PlcLoggerTreeRoot));
                        OnPropertyChanged(nameof(ActiveLoggerTree));
                    }

                    FilterVM.SearchText = "";
                    FilterVM.IsSearchPanelVisible = false;
                }

                Logs = new List<LogEntry>();
                OnPropertyChanged(nameof(Logs));

                ConfigVM?.ClearConfigurationFiles();
                OnPropertyChanged(nameof(ConfigurationFiles));
                OnPropertyChanged(nameof(DbTreeNodes));
                OnPropertyChanged(nameof(SelectedConfigFile));
                OnPropertyChanged(nameof(ConfigFileContent));
                OnPropertyChanged(nameof(FilteredConfigContent));
                OnPropertyChanged(nameof(ConfigSearchText));
                OnPropertyChanged(nameof(IsDbFileSelected));

                SetupInfo = "";
                OnPropertyChanged(nameof(SetupInfo));
                PressConfig = "";
                OnPropertyChanged(nameof(PressConfig));
                VersionsInfo = "";
                OnPropertyChanged(nameof(VersionsInfo));
                WindowTitle = "IndiLogs 3.0";
                OnPropertyChanged(nameof(WindowTitle));

                CurrentProgress = 0;
                ScreenshotZoom = 400;
                SelectedSession = null;
                SelectedLog = null;
                IsFilterOutActive = false;
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(IsFilterOutActive));

                ResetTreeFilters();

                if (VisualTimelineVM != null)
                    VisualTimelineVM.Clear();
                _isVisualMode = false;
                OnPropertyChanged(nameof(IsVisualMode));

                SelectedTabIndex = AppConstants.TAB_PLC;
                OnPropertyChanged(nameof(SelectedTabIndex));
                LeftTabIndex = 0;
                OnPropertyChanged(nameof(LeftTabIndex));

                StatusMessage = "All data cleared successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Clear failed: {ex.Message}";
            }
        }
        private void OpenStatesWindow(object obj)
        {
            if (IsAnalysisRunning)
            {
                MessageBox.Show("Still analyzing data in background...\nPlease wait until the process finishes.",
                                "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSession == null) { MessageBox.Show("No logs loaded."); return; }

            if (SelectedSession.CachedStates != null && SelectedSession.CachedStates.Count > 0)
            {
                if (_statesWindow != null && _statesWindow.IsVisible) { WindowManager.ActivateWindow(_statesWindow); return; }

                _statesWindow = new StatesWindow(SelectedSession.CachedStates, this);
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                WindowManager.OpenWindow(_statesWindow);
            }
            else
            {
                MessageBox.Show("No states detected in this session.");
            }
        }

        private void ResetTimeFocus(object obj)
        {
            if (VisualTimelineVM != null)
            {
                VisualTimelineVM.ViewScale = 1.0;
                VisualTimelineVM.ViewOffset = 0;
                VisualTimelineVM.SelectedState = null;
            }

            FilterVM.IsTimeFocusActive = false;
            FilterVM.IsAppTimeFocusActive = false;
            FilterVM.LastFilteredCache?.Clear();
            FilterVM.LastFilteredAppCache = null;
            FilterVM.SavedFilterRoot = null;
            SearchText = string.Empty;
            FilterVM.IsMainFilterActive = false;
            FilterVM.IsAppFilterActive = false;
            FilterVM.IsMainFilterOutActive = false;
            FilterVM.IsAppFilterOutActive = false;
            FilterVM.ActiveThreadFilters.Clear();
            FilterVM.NegativeFilters.Clear();
            ResetTreeFilters();

            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterOutActive));

            UpdateMainLogsFilter(false);
            ApplyAppLogsFilter();

            InitializeVisualMode();

            StatusMessage = "Filter reset. Showing all data.";
        }

        private void RunAnalysis(object obj)
        {
            if (SelectedSession == null)
            {
                MessageBox.Show("No logs loaded.");
                return;
            }

            if (IsAnalysisRunning)
            {
                MessageBox.Show("Analysis is still running in the background.\nPlease wait a moment and try again.",
                                "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // S4-5: go directly to Statistics (no Failures analysis available)
            if (HasBinaryAppLogs)
            {
                ShowStatisticsAnalysis();
                return;
            }

            // S6: Show analysis menu with Failures / Statistics choices
            var menuWindow = new Views.AnalysisMenuWindow();
            menuWindow.Owner = Application.Current.MainWindow;

            if (menuWindow.ShowDialog() == true)
            {
                switch (menuWindow.SelectedChoice)
                {
                    case Views.AnalysisMenuWindow.AnalysisChoice.Failures:
                        ShowFailuresAnalysis();
                        break;
                    case Views.AnalysisMenuWindow.AnalysisChoice.Statistics:
                        ShowStatisticsAnalysis();
                        break;
                }
            }
        }

        private void ShowFailuresAnalysis()
        {
            if (_analysisWindow != null && _analysisWindow.IsVisible)
            {
                _analysisWindow.Activate();
                return;
            }

            if (SelectedSession.CachedAnalysis != null && SelectedSession.CachedAnalysis.Any())
            {
                OpenAnalysisWindow(SelectedSession.CachedAnalysis);
            }
            else
            {
                MessageBox.Show("Great news! No critical state failures were detected in this session.",
                                "Analysis Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowStatisticsAnalysis()
        {
            var plcLogs = SessionVM?.AllLogsCache;
            var appLogs = SessionVM?.AllAppLogsCache;

            // בדיקה אם יש בכלל נתונים להציג
            bool hasPlc = plcLogs != null && plcLogs.Any();
            bool hasApp = appLogs != null && appLogs.Any();

            if (!hasPlc && !hasApp)
            {
                MessageBox.Show("No logs available for analysis.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // יצירת החלון עם שני הפרמטרים וקולבק לפילטור
            var statsWindow = new Views.StatsWindow(plcLogs, appLogs, ApplyChartDrillDownFilter, NavigateToLogFromStats, IsDarkMode, HasBinaryAppLogs);
            statsWindow.Title = "Log Statistics Dashboard";
            WindowManager.OpenWindow(statsWindow);
        }

        private void ApplyChartDrillDownFilter(string filterType, string filterValue)
        {
            try
            {
                if (filterType == "Logger")
                {
                    // Filter by Logger field - search for the logger name in the message
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = AppConstants.TAB_PLC;

                    int logCount = Logs?.Count() ?? 0;
                    MessageBox.Show($"Filter applied: Logger = {filterValue}\n\nShowing {logCount} matching logs.",
                        "Logger Filter Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (filterType == "State")
                {
                    // Filter by STATE - search for the state name
                    FilterVM.SearchText = filterValue;
                    FilterVM.IsMainFilterActive = true;
                    FilterVM.ApplyMainLogsFilter();

                    // Switch to PLC tab to show filtered results
                    SelectedTabIndex = AppConstants.TAB_PLC;

                    int logCount = Logs?.Count() ?? 0;
                    MessageBox.Show($"Filter applied: STATE = {filterValue}\n\nShowing {logCount} matching logs.",
                        "State Filter Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying filter: {ex.Message}", "Filter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavigateToLogFromStats(LogEntry log)
        {
            if (log == null) return;
            try
            {
                // Determine which tab to switch to based on log type
                bool isAppLog = !string.IsNullOrEmpty(log.Logger) && !log.Logger.Contains("E1.PLC");
                SelectedTabIndex = isAppLog ? AppConstants.TAB_APP : AppConstants.TAB_PLC;

                SelectedLog = log;
                ScrollToLog(log);
            }
            catch (Exception ex)
            {
                AppLogger.Error("NavigateToLogFromStats failed", ex);
            }
        }

        private void FilterToState(object obj)
        {
            if (obj is StateEntry state)
            {
                IsBusy = true;
                StatusMessage = $"Focusing state: {state.StateName}...";

                Task.Run(() =>
                {
                    DateTime start = state.StartTime;
                    DateTime end = state.EndTime ?? DateTime.MaxValue;

                    if (SessionVM.AllLogsCache != null)
                    {
                        var timeSlice = SessionVM.AllLogsCache.Where(l => l.Date >= start && l.Date <= end).OrderByDescending(l => l.Date).ToList();
                        var smartFiltered = timeSlice.Where(l => IsDefaultLog(l)).ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            FilterVM.LastFilteredCache = timeSlice;
                            FilterVM.SavedFilterRoot = null;
                            FilterVM.IsTimeFocusActive = true;
                            FilterVM.IsMainFilterActive = true;
                            SelectedTabIndex = AppConstants.TAB_PLC;
                            UpdateMainLogsFilter(true);
                            if (FilterVM?.FilteredLogs != null)
                            {
                                FilterVM.FilteredLogs.ReplaceAll(smartFiltered);
                                if (FilterVM.FilteredLogs.Count > 0) SelectedLog = FilterVM.FilteredLogs[0];
                            }
                            OnPropertyChanged(nameof(IsFilterActive));
                            StatusMessage = $"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";

                            if (IsVisualMode && VisualTimelineVM != null)
                            {
                                // Use filtered logs if time range is active
                                var logsForVisual = FilterVM.IsGlobalTimeRangeActive ? Logs : SessionVM.AllLogsCache.ToList();
                                var eventsToShow = HasBinaryAppLogs ? null : Events;
                                VisualTimelineVM.LoadData(logsForVisual, eventsToShow);
                                VisualTimelineVM.FocusOnState(state.StateName);
                            }

                            IsBusy = false;
                        });
                    }
                    else
                    {
                        IsBusy = false;
                    }
                });
            }
        }
        private void FilterAppErrors(object obj)
        {
            if (SessionVM.AllAppLogsCache == null || !SessionVM.AllAppLogsCache.Any()) return;
            IsBusy = true;
            StatusMessage = "Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = SessionVM.AllAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilterVM?.AppDevLogsFiltered?.ReplaceAll(errors);
                    IsBusy = false;
                    StatusMessage = $"Showing {errors.Count} Errors";
                    FilterVM.IsAppErrorFilterActive = true;
                    FilterVM.IsAppFilterActive = true;
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                });
            });
        }
        private void OpenIndigoInvaders(object obj)
        {
            var invadersWindow = new IndiLogs_3._0.Views.IndigoInvadersWindow();
            invadersWindow.Owner = Application.Current.MainWindow;
            invadersWindow.ShowDialog();
        }

        private void BuildLoggerTree(IEnumerable<LogEntry> logs) => FilterVM?.BuildLoggerTree(logs);
                   
        public void ResetTreeFilters()
        {
            FilterVM.TreeHiddenLoggers.Clear();
            FilterVM.TreeHiddenPrefixes.Clear();
            FilterVM.TreeShowOnlyLogger = null;
            FilterVM.TreeShowOnlyPrefix = null;
            foreach (var node in LoggerTreeRoot) ResetVisualHiddenState(node);
        }
        private void ResetVisualHiddenState(LoggerNode node)
        {
            node.IsHidden = false;
            node.IsActive = false;
            foreach (var child in node.Children) ResetVisualHiddenState(child);
        }
        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log)
            {
                WindowManager.OpenWindow(new LogDetailsWindow(log));
            }
        }
        public async Task SortAppLogs(string sortBy, bool ascending)
        {
            try
            {
            if (AppDevLogsFiltered == null || AppDevLogsFiltered.Count == 0) return;
            IsBusy = true;
            StatusMessage = "Sorting...";
            await Task.Run(() =>
            {
                List<LogEntry> sorted = null;
                var source = AppDevLogsFiltered.ToList();
                switch (sortBy)
                {
                    case "Time": sorted = ascending ? source.OrderBy(x => x.Date).ToList() : source.OrderByDescending(x => x.Date).ToList(); break;
                    case "Level": sorted = ascending ? source.OrderBy(x => x.Level).ToList() : source.OrderByDescending(x => x.Level).ToList(); break;
                    case "Logger": sorted = ascending ? source.OrderBy(x => x.Logger).ToList() : source.OrderByDescending(x => x.Logger).ToList(); break;
                    case "Thread": sorted = ascending ? source.OrderBy(x => x.ThreadName).ToList() : source.OrderByDescending(x => x.ThreadName).ToList(); break;
                    default: sorted = source; break;
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppDevLogsFiltered.ReplaceAll(sorted);
                    IsBusy = false;
                    StatusMessage = "Sorted.";
                });
            });
            }
            catch (Exception ex) { AppLogger.Error("SortAppLogs failed", ex); }
        }

        private void ToggleFilterView(bool show) => FilterVM?.ToggleFilterView(show);
        private void UpdateMainLogsFilter(bool show) => FilterVM?.ApplyMainLogsFilter();
        private void ApplyAppLogsFilter() => FilterVM?.ApplyAppLogsFilter();

        private void StartLiveMonitoring(string path) => LiveVM?.StartLiveMonitoring(path);
        private void StopLiveMonitoring() => LiveVM?.StopLiveMonitoring();

        private void LivePlay(object obj) => LiveVM?.LivePlayCommand.Execute(obj);
        private void LivePause(object obj) => LiveVM?.LivePauseCommand.Execute(obj);
        private async void LoadFile(object obj)
        {
            try
            {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log;*.csv|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|CPR Data (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                // Route CSV files to CPR tab instead of log processing
                var csvFiles = dialog.FileNames.Where(f => System.IO.Path.GetExtension(f).ToLower() == ".csv").ToArray();
                var logFiles = dialog.FileNames.Where(f => System.IO.Path.GetExtension(f).ToLower() != ".csv").ToArray();

                if (csvFiles.Length > 0)
                {
                    // Load the first CSV into CPR
                    SelectedTabIndex = AppConstants.TAB_CPR; // CPR tab
                    CprVM?.LoadFileDirect(csvFiles[0]);
                }

                if (logFiles.Length > 0)
                {
                    // For single non-session files, try routing to Different Logs tab
                    if (logFiles.Length == 1 && DifferentLogsVM != null)
                    {
                        var ext = System.IO.Path.GetExtension(logFiles[0]).ToLower();
                        bool isKnownSessionExt = ext == ".zip" || ext == ".log" || ext == ".file";
                        if (!isKnownSessionExt)
                        {
                            bool handled = await DifferentLogsVM.LoadFileAsync(logFiles[0]);
                            if (handled)
                            {
                                SelectedTabIndex = AppConstants.TAB_DIFFERENT_LOGS; // DIFFERENT LOGS tab
                                return;
                            }
                        }
                    }
                    ProcessFiles(logFiles);
                }
            }
            }
            catch (Exception ex) { AppLogger.Error("LoadFile failed", ex); }
        }
        private async void OpenFilterWindow(object obj)
        {
            try
            {
            var win = new FilterWindow();
            bool isAppTab = SelectedTabIndex == AppConstants.TAB_APP;
            var currentRoot = isAppTab ? FilterVM.AppFilterRoot : FilterVM.MainFilterRoot;

            if (currentRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(currentRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                IsBusy = true;
                await Task.Run(() =>
                {
                    if (isAppTab) FilterVM.AppFilterRoot = newRoot;
                    else
                    {
                        FilterVM.MainFilterRoot = newRoot;
                        if (hasAdvanced)
                        {
                            List<LogEntry> cacheCopy;
                            lock (_collectionLock)
                            {
                                cacheCopy = _allLogsCache.ToList();
                            }
                            var res = cacheCopy.Where(l => EvaluateFilterNode(l, FilterVM.MainFilterRoot)).ToList();
                            FilterVM.LastFilteredCache = res;
                        }
                        else FilterVM.LastFilteredCache.Clear();
                    }
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isAppTab) { FilterVM.IsAppFilterActive = hasAdvanced; ApplyAppLogsFilter(); }
                    else { FilterVM.IsMainFilterActive = hasAdvanced || FilterVM.ActiveThreadFilters.Any(); UpdateMainLogsFilter(FilterVM.IsMainFilterActive); }
                    OnPropertyChanged(nameof(IsFilterActive));
                    IsBusy = false;
                });
            }
            }
            catch (Exception ex) { AppLogger.Error("OpenFilterWindow failed", ex); }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node) => FilterVM?.EvaluateFilterNode(log, node) ?? true;
        private async void ExportParsedData(object obj)
        {
            try
            {
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any())
            {
                MessageBox.Show("No logs loaded.", "Info");
                return;
            }

            if (_exportConfigWindow != null && _exportConfigWindow.IsLoaded)
            {
                WindowManager.ActivateWindow(_exportConfigWindow);
                return;
            }

            _exportConfigWindow = new ExportConfigurationWindow();
            var viewModel = new ExportConfigurationViewModel(SelectedSession, _csvService);
            _exportConfigWindow.DataContext = viewModel;
            _exportConfigWindow.Closed += (s, e) => _exportConfigWindow = null;
            WindowManager.OpenWindow(_exportConfigWindow);
            }
            catch (Exception ex) { AppLogger.Error("ExportParsedData failed", ex); }
        }
        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results, log => JumpToLog(log));
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            WindowManager.OpenWindow(_analysisWindow);
        }

        private async Task OnSelectedSessionChangedAsync(LogSessionData session)
        {
            if (session != null && session.HasBinaryAppLogs &&
                !string.IsNullOrEmpty(session.FilePath) && File.Exists(session.FilePath))
                await StepRecorderVM.LoadFromZipAsync(session.FilePath);
            else
                StepRecorderVM.Clear();
        }
        private void OpenSnakeGame(object obj)
        {
            var snakeWindow = new IndiLogs_3._0.Views.SnakeWindow();
            WindowManager.ShowDialog(snakeWindow);
        }
        private void LoadSavedConfigurations()
        {
            // Ensure built-in default config files exist on disk, but don't show them yet.
            // The saved configs list stays empty until a log is loaded.
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs3.0", "Configs");
            CaseVM?.EnsureDefaultConfigsOnDisk(path);
        }

        private void LoadUserDefaults()
        {
            _defaultConfigService.Load();
            var defaults = _defaultConfigService.CurrentDefaults;
            if (defaults == null) return;

            if (defaults.HasCustomMainColoring && defaults.MainDefaultColoringRules != null)
                _coloringService.UserDefaultMainRules = defaults.MainDefaultColoringRules;
            if (defaults.HasCustomAppColoring && defaults.AppDefaultColoringRules != null)
                _coloringService.UserDefaultAppRules = defaults.AppDefaultColoringRules;
            if (defaults.HasCustomPlcFilter && defaults.PlcFilteredDefaultFilter != null)
                FilterVM.DefaultPlcFilter = defaults.PlcFilteredDefaultFilter;
        }

        private void SetCurrentAsDefault(object obj)
        {
            var config = new Models.DefaultConfiguration();

            // Save PLC Filtered default filter from current state
            if (FilterVM.DefaultPlcFilter != null)
            {
                config.PlcFilteredDefaultFilter = FilterVM.DefaultPlcFilter.DeepClone();
                config.HasCustomPlcFilter = true;
            }

            // Save Main coloring rules
            if (CaseVM?.MainColoringRules != null && CaseVM.MainColoringRules.Count > 0)
            {
                config.MainDefaultColoringRules = CaseVM.MainColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomMainColoring = true;
            }

            // Save App coloring rules
            if (CaseVM?.AppColoringRules != null && CaseVM.AppColoringRules.Count > 0)
            {
                config.AppDefaultColoringRules = CaseVM.AppColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomAppColoring = true;
            }

            _defaultConfigService.Save(config);

            // Update live state
            _coloringService.UserDefaultMainRules = config.MainDefaultColoringRules;
            _coloringService.UserDefaultAppRules = config.AppDefaultColoringRules;

            SessionVM.StatusMessage = "Current settings saved as defaults.";
        }

        private void ResetDefaults(object obj)
        {
            _defaultConfigService.Reset();
            _coloringService.UserDefaultMainRules = null;
            _coloringService.UserDefaultAppRules = null;
            FilterVM.DefaultPlcFilter = null;

            SessionVM.StatusMessage = "Defaults reset to factory settings.";
        }

        private void ApplyConfiguration(object parameter) { if (parameter is SavedConfiguration c) CaseVM?.ApplyConfiguration(c); }
        private void RemoveConfiguration(object parameter) => CaseVM?.DeleteConfigCommand.Execute(parameter);
        private void SaveConfiguration(object obj) => CaseVM?.SaveConfigCommand.Execute(obj);
        private void LoadConfigurationFromFile(object obj) => CaseVM?.LoadConfigCommand.Execute(obj);
        private void ApplyTheme(bool isDark)
        {
            var dict = Application.Current.Resources;
            if (isDark)
            {
                // ── Main backgrounds ──
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(10, 18, 30)));       // #0A121E - very deep navy
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(15, 25, 40)));      // #0F1928 - dark navy panel
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(20, 35, 55)));       // #142337 - navy card
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(30, 50, 75)));  // #1E324B - lighter navy hover

                // ── Sidebar ──
                UpdateResource(dict, "SidebarBg", new SolidColorBrush(Color.FromRgb(15, 25, 40)));    // Match BgPanel in dark
                UpdateResource(dict, "SidebarText", new SolidColorBrush(Color.FromRgb(220, 230, 240)));
                UpdateResource(dict, "SidebarBorder", new SolidColorBrush(Color.FromRgb(40, 60, 85)));

                // ── Text & borders ──
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(220, 230, 240)));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(140, 160, 180)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(40, 60, 85)));  // #283C55

                // ── Primary accent (consistent across themes) ──
                UpdateResource(dict, "PrimaryColor", new SolidColorBrush(Color.FromRgb(59, 130, 246)));  // #3B82F6
                UpdateResource(dict, "PrimaryHover", new SolidColorBrush(Color.FromRgb(96, 165, 250)));  // #60A5FA
                UpdateResource(dict, "PrimaryGlow", new SolidColorBrush(Color.FromArgb(0x20, 0x3B, 0x82, 0xF6)));

                // ── Diff / comparison ──
                UpdateResource(dict, "DiffRowDifferent", new SolidColorBrush(Color.FromRgb(42, 21, 21)));  // #2A1515 dark red tint
                UpdateResource(dict, "DiffAddedBg", new SolidColorBrush(Color.FromRgb(144, 238, 144)));
                UpdateResource(dict, "DiffRemovedBg", new SolidColorBrush(Color.FromRgb(240, 128, 128)));

                // ── Gap indicator ──
                UpdateResource(dict, "GapIndicatorBg", new SolidColorBrush(Color.FromRgb(27, 53, 84)));   // #1B3554
                UpdateResource(dict, "GapIndicatorFg", new SolidColorBrush(Color.FromRgb(107, 140, 174))); // #6B8CAE

                // ── Hover overlays ──
                UpdateResource(dict, "RowHoverBg", new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255))); // 10% white
                UpdateResource(dict, "TabHoverBg", new SolidColorBrush(Color.FromArgb(0x10, 0x88, 0x88, 0x88)));

                // ── Animation / loading ──
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 200, 220)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(245, 0, 87)));
                UpdateResource(dict, "AnimColor3", new SolidColorBrush(Color.FromRgb(255, 255, 0)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Colors.White));

                // ── Scrollbar thumb ──
                UpdateResource(dict, "ScrollThumb", new SolidColorBrush(Color.FromRgb(0x68, 0x68, 0x68)));
                UpdateResource(dict, "ScrollThumbHover", new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)));
                UpdateResource(dict, "ScrollThumbDrag", new SolidColorBrush(Color.FromRgb(0xAD, 0xAD, 0xAD)));
                UpdateResource(dict, "ScrollThumbH", new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)));
                UpdateResource(dict, "ScrollThumbHoverH", new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)));
                UpdateResource(dict, "ScrollThumbDragH", new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)));

                // ── Row selection / highlights ──
                UpdateResource(dict, "RowSelectedBg", new SolidColorBrush(Color.FromRgb(0xFF, 0xFA, 0xCD))); // #FFFACD
                UpdateResource(dict, "RowMarkedBg", new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90)));   // #90EE90
                UpdateResource(dict, "RowErrorFg", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));    // #FF6B6B
            }
            else
            {
                // ── Main backgrounds ──
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(240, 242, 245)));    // #F0F2F5 - soft gray
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(243, 244, 246)));    // #F3F4F6
                UpdateResource(dict, "BgCard", new SolidColorBrush(Colors.White));                      // #FFFFFF
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(235, 238, 242))); // #EBEEF2

                // ── Sidebar ──
                UpdateResource(dict, "SidebarBg", new SolidColorBrush(Color.FromRgb(243, 244, 246)));  // #F3F4F6
                UpdateResource(dict, "SidebarText", new SolidColorBrush(Color.FromRgb(31, 41, 55)));   // #1F2937
                UpdateResource(dict, "SidebarBorder", new SolidColorBrush(Color.FromRgb(229, 231, 235))); // #E5E7EB

                // ── Text & borders ──
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(31, 41, 55)));   // #1F2937
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(107, 114, 128))); // #6B7280
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(209, 213, 219))); // #D1D5DB

                // ── Primary accent (slightly darker for light bg readability) ──
                UpdateResource(dict, "PrimaryColor", new SolidColorBrush(Color.FromRgb(37, 99, 235)));   // #2563EB
                UpdateResource(dict, "PrimaryHover", new SolidColorBrush(Color.FromRgb(59, 130, 246)));   // #3B82F6
                UpdateResource(dict, "PrimaryGlow", new SolidColorBrush(Color.FromArgb(0x18, 0x25, 0x63, 0xEB)));

                // ── Diff / comparison ──
                UpdateResource(dict, "DiffRowDifferent", new SolidColorBrush(Color.FromRgb(254, 226, 226))); // #FEE2E2 light pink tint
                UpdateResource(dict, "DiffAddedBg", new SolidColorBrush(Color.FromRgb(187, 247, 208)));     // #BBF7D0
                UpdateResource(dict, "DiffRemovedBg", new SolidColorBrush(Color.FromRgb(254, 202, 202)));   // #FECACA

                // ── Gap indicator ──
                UpdateResource(dict, "GapIndicatorBg", new SolidColorBrush(Color.FromRgb(224, 231, 240))); // #E0E7F0
                UpdateResource(dict, "GapIndicatorFg", new SolidColorBrush(Color.FromRgb(100, 116, 139))); // #64748B

                // ── Hover overlays ──
                UpdateResource(dict, "RowHoverBg", new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)));       // 10% black
                UpdateResource(dict, "TabHoverBg", new SolidColorBrush(Color.FromArgb(0x12, 0, 0, 0)));       // 7% black

                // ── Animation / loading ──
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 120, 215)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(220, 0, 80)));
                UpdateResource(dict, "AnimColor3", new SolidColorBrush(Color.FromRgb(200, 160, 0)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Color.FromRgb(31, 41, 55)));

                // ── Scrollbar thumb ──
                UpdateResource(dict, "ScrollThumb", new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));
                UpdateResource(dict, "ScrollThumbHover", new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)));
                UpdateResource(dict, "ScrollThumbDrag", new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)));
                UpdateResource(dict, "ScrollThumbH", new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)));
                UpdateResource(dict, "ScrollThumbHoverH", new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x98)));
                UpdateResource(dict, "ScrollThumbDragH", new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)));

                // ── Row selection / highlights ──
                UpdateResource(dict, "RowSelectedBg", new SolidColorBrush(Color.FromRgb(0xDB, 0xED, 0xFF))); // #DBEDFF light blue
                UpdateResource(dict, "RowMarkedBg", new SolidColorBrush(Color.FromRgb(0xD4, 0xED, 0xDA)));   // #D4EDDA light green
                UpdateResource(dict, "RowErrorFg", new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)));    // #DC3545 bootstrap red
            }
        }
        private void UpdateResource(ResourceDictionary dict, string key, object value)
        {
            if (dict.Contains(key))
                dict.Remove(key);
            dict.Add(key, value);
        }

        private void MarkRow(object obj) => CaseVM?.MarkLogCommand.Execute(obj);
        private void GoToNextMarked(object obj) => CaseVM?.GoToNextMarkedCommand.Execute(obj);
        private void GoToPrevMarked(object obj) => CaseVM?.GoToPrevMarkedCommand.Execute(obj);
        private void JumpToLog(object obj) { if (obj is LogEntry log) { SelectedLog = log; RequestScrollToLog?.Invoke(log); } }
        private void OpenSettingsWindow(object obj)
        {
            var win = new SettingsWindow { DataContext = this };
            win.WindowStartupLocation = WindowStartupLocation.Manual;

            if (obj is FrameworkElement button)
            {
                // Get DPI scale factor for accurate positioning
                var source = PresentationSource.FromVisual(button);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Position below the button, aligned to its left edge
                Point buttonPosition = button.PointToScreen(new Point(0, 0));
                double buttonHeight = button.ActualHeight * dpiScale;

                // Get screen bounds to ensure window stays on screen
                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)buttonPosition.X, (int)buttonPosition.Y));
                var workingArea = screen.WorkingArea;

                // Position below the button
                double left = buttonPosition.X / dpiScale;
                double top = (buttonPosition.Y + buttonHeight + 5) / dpiScale;

                // Ensure window doesn't go off the right edge
                if (left + win.Width > workingArea.Right / dpiScale)
                {
                    left = workingArea.Right / dpiScale - win.Width - 10;
                }

                // Ensure window doesn't go off the bottom - if so, show above button
                double estimatedHeight = 350;
                if (top + estimatedHeight > workingArea.Bottom / dpiScale)
                {
                    top = buttonPosition.Y / dpiScale - estimatedHeight - 5;
                }

                win.Left = left;
                win.Top = top;

                // Show the window directly instead of using WindowManager
                // to preserve our manual positioning
                win.Show();
                win.Activate();
                win.Focus();
            }
            else
            {
                // Fallback: use WindowManager for centering
                WindowManager.OpenWindow(win);
            }
        }
        private void OpenFontsWindow(object obj) { WindowManager.ShowDialog(new FontsWindow { DataContext = this }); }
        private void UpdateContentFont(string fontName) { if (!string.IsNullOrEmpty(fontName) && Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName)); }
        private void UpdateContentFontWeight(bool isBold)
        {
            if (Application.Current != null)
            {
                UpdateResource(Application.Current.Resources, "ContentFontWeight",
                    isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);
            }
        }

        private void OpenMarkedLogsWindow(object obj) => CaseVM?.OpenMarkedWindowCommand.Execute(obj);

        private void OpenGlobalGrepWindow()
        {
            // יצירת אוסף ריק במידה ולא נטענו סשנים, כדי לאפשר לחלון להיפתח
            var sessions = LoadedSessions ?? new ObservableCollection<LogSessionData>();

            var viewModel = new GlobalGrepViewModel(sessions);

            // אם אין קבצים טעונים, נגדיר את ברירת המחדל לחיפוש חיצוני
            if (!sessions.Any())
            {
                viewModel.SearchMode = GlobalGrepViewModel.SearchModeType.ExternalFiles;
            }

            var window = new GlobalGrepWindow(viewModel, NavigateToGrepResult, LoadMultipleFiles);
            WindowManager.OpenWindow(window);
        }

        private void OpenComparisonWindow()
        {
            var comparisonWindow = WindowManager.GetOrCreate<Views.ComparisonWindow>(
                () => new Views.ComparisonWindow(new LogComparisonViewModel(
                    SessionVM.AllLogsCache,
                    SessionVM.AllAppLogsCache,
                    this
                )),
                Application.Current.MainWindow
            );
        }

        private async void OpenStripeAnalysisWindow()
        {
            try
            {
            var logs = FilterVM?.AppDevLogsFiltered?.ToList();

            if (logs == null || !logs.Any())
            {
                MessageBox.Show(
                    "No APP logs loaded.\n\nPlease load a session with APP logs first, or switch to the APP tab.",
                    "Stripe Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Quick pre-check: do we have any stripe data?
            bool hasStripeData = logs.Any(l =>
                (!string.IsNullOrEmpty(l.Data) && l.Data.Contains("stripeDescriptor")) ||
                (!string.IsNullOrEmpty(l.Message) && l.Message.Contains("stripeDescriptor")));

            if (!hasStripeData)
            {
                MessageBox.Show(
                    "No stripe data found in APP logs.\n\n" +
                    "This feature requires logs containing stripeDescriptor JSON data.",
                    "Stripe Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new StripeAnalysisWindow();
            WindowManager.OpenWindow(window);

            // Load data asynchronously after window is shown
            await Task.Run(() => { }).ContinueWith(_ =>
            {
                window.LoadFromLogs(logs);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex) { AppLogger.Error("OpenStripeAnalysisWindow failed", ex); }
        }

        private void NavigateToGrepResult(GrepResult result)
        {
            if (result == null) return;

            // If we have a direct reference to the log entry (in-memory search)
            if (result.ReferencedLogEntry != null && result.SessionIndex >= 0)
            {
                // Navigate to the loaded session
                if (result.SessionIndex < LoadedSessions.Count)
                {
                    SelectedSession = LoadedSessions[result.SessionIndex];

                    // Switch to the appropriate tab (0 for PLC, 1 for APP)
                    SelectedTabIndex = (result.LogType == "APP") ? 1 : 0;

                    // Wait for UI to update, then scroll to the log entry
                    Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => RequestScrollToLog?.Invoke(result.ReferencedLogEntry)));
                }
                return;
            }

            // If we don't have a direct reference (external file search)
            if (string.IsNullOrEmpty(result.FilePath)) return;

            // Check if the file is already loaded
            var session = LoadedSessions.FirstOrDefault(s => s.FilePath == result.FilePath);

            if (session != null)
            {
                SelectedSession = session;
                JumpByTime(result, session);
            }
            else
            {
                // Load the file if not already loaded
                ProcessFiles(new[] { result.FilePath }, (loadedSession) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SelectedSession = loadedSession;
                        JumpByTime(result, loadedSession);
                    });
                });
            }
        }

        private void JumpByTime(GrepResult result, LogSessionData session)
        {
            // Switch to the appropriate tab (0 for PLC, 1 for APP)
            SelectedTabIndex = (result.LogType == "APP") ? 1 : 0;

            // Get the appropriate log collection
            var logs = (result.LogType == "APP") ? session.AppDevLogs : session.Logs;

            // Find the exact log entry by Timestamp and Message
            var target = logs?.FirstOrDefault(l =>
                l.Date == result.Timestamp &&
                l.Message == result.ReferencedLogEntry?.Message &&
                l.ThreadName == result.ReferencedLogEntry?.ThreadName)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp && l.Message == result.ReferencedLogEntry?.Message)
                ?? logs?.FirstOrDefault(l => l.Date == result.Timestamp);

            if (target != null)
            {
                // Wait for UI to update, then scroll to the log entry
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => RequestScrollToLog?.Invoke(target)));
            }
        }

        private void LoadMultipleFiles(List<(string FilePath, string SessionName)> fileList)
        {
            if (fileList == null || fileList.Count == 0) return;

            // Get list of already loaded files
            var loadedFilePaths = LoadedSessions.Select(s => s.FilePath).ToList();

            // Show file selection window
            var fileSelectionWindow = new Views.FileSelectionWindow(fileList, loadedFilePaths);
            fileSelectionWindow.Owner = Application.Current.MainWindow;

            if (fileSelectionWindow.ShowDialog() == true)
            {
                var filesToLoad = fileSelectionWindow.FilesToLoad;

                if (filesToLoad != null && filesToLoad.Count > 0)
                {
                    // Load all files using ProcessFiles
                    ProcessFiles(filesToLoad.ToArray(), null);

                    MessageBox.Show($"Loaded {filesToLoad.Count} file(s).", "Open All Files", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private bool IsDefaultLog(LogEntry l) => FilterVM?.IsDefaultLog(l) ?? false;
        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        private void OpenKibana(object obj) { }

        private void CopyTableName(object parameter)
        {
            if (parameter is DbTreeNode node && !string.IsNullOrEmpty(node.Name))
            {
                try
                {
                    Clipboard.SetText(node.Name);
                }
                catch (Exception ex) { AppLogger.Error("Clipboard copy failed", ex); }
            }
        }
        public async Task OnFilesDropped(string[] files)
        {
            try
            {
            if (files == null || files.Length == 0) return;

            // Check if any CSV files should be routed to CPR instead of log processing
            if (files.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLower();
                var fileName = System.IO.Path.GetFileName(files[0]).ToLower();

                // Route CSV files to CPR — EXCEPT event CSV files which are log events
                bool isEventCsv = fileName.StartsWith("event-history__from") || fileName.StartsWith("pressevents.");
                if (ext == ".csv" && !isEventCsv)
                {
                    // Switch to CPR tab and load
                    SelectedTabIndex = AppConstants.TAB_CPR; // CPR tab
                    CprVM?.LoadFileDirect(files[0]);
                    return;
                }

                // For single files that are NOT known session types (.zip, .log, .file),
                // try routing to Different Logs tab via plugin detection
                bool isKnownSessionExt = ext == ".zip" || ext == ".log" || ext == ".file";
                if (!isKnownSessionExt && DifferentLogsVM != null)
                {
                    bool handled = await DifferentLogsVM.LoadFileAsync(files[0]);
                    if (handled)
                    {
                        SelectedTabIndex = AppConstants.TAB_DIFFERENT_LOGS; // DIFFERENT LOGS tab
                        return;
                    }
                }
            }

            ProcessFiles(files);
            }
            catch (Exception ex) { AppLogger.Error("OnFilesDropped failed", ex); }
        }

        public LogAnnotation GetAnnotation(LogEntry log) => CaseVM?.GetAnnotation(log);
        private void AddAnnotation(object parameter) => CaseVM?.AddAnnotationCommand.Execute(parameter);
        private void DeleteAnnotation(object parameter) => CaseVM?.DeleteAnnotationCommand.Execute(parameter);
        private void SaveCase(object parameter) => CaseVM?.SaveCaseCommand.Execute(parameter);
        private void LoadCase(object parameter) => CaseVM?.LoadCaseCommand.Execute(parameter);

        // ==================== TIME-SYNC SCROLLING METHODS ====================

        private int LinearSearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0)
                return -1;

            int nearestIndex = 0;
            TimeSpan minDiff = (collection[0].Date - targetTime).Duration();

            for (int i = 1; i < collection.Count; i++)
            {
                TimeSpan currentDiff = (collection[i].Date - targetTime).Duration();
                if (currentDiff < minDiff)
                {
                    minDiff = currentDiff;
                    nearestIndex = i;
                }
                if (minDiff.TotalMilliseconds < 1)
                    break;
            }

            return nearestIndex;
        }

        private int BinarySearchNearest(IList<LogEntry> collection, DateTime targetTime)
        {
            if (collection == null || collection.Count == 0) return -1;

            int left = 0;
            int right = collection.Count - 1;

            if (targetTime <= collection[0].Date) return 0;
            if (targetTime >= collection[right].Date) return right;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                DateTime midTime = collection[mid].Date;

                if (midTime == targetTime) return mid;

                if (midTime < targetTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            if (left >= collection.Count) return right;

            DateTime leftTime = collection[left].Date;
            DateTime rightTime = collection[right].Date;

            TimeSpan leftDiff = (leftTime - targetTime).Duration();
            TimeSpan rightDiff = (targetTime - rightTime).Duration();

            return leftDiff < rightDiff ? left : right;
        }

        public void RequestSyncScroll(DateTime targetTime, string sourceGrid)
        {
            // _isTabSwitching: tab is mid-transition; the ScrollChanged that fired here
            // is from WPF re-rendering the newly-visible DataGrid, not from a real user scroll.
            if (!IsTimeSyncEnabled || _isSyncScrolling || _isTabSwitching) return;

            _isSyncScrolling = true;

            try
            {
                // TimeSyncOffsetSeconds = APP.Date - PLC.Date  (calculated at session load)
                //
                // PLC → APP:  PLC time is raw PLC clock.  We ADD the offset to convert it to
                //              APP clock space before searching AppDevLogsFiltered.
                //
                // APP → PLC:  APP time is in APP clock space.  We SUBTRACT the offset to
                //              convert it back to PLC clock space before searching AllLogsCache.
                //              Using +offset here was the bug that caused >1 min offset in this
                //              direction (it shifted the search time by 2× the actual offset).
                IList<LogEntry> targetCollection = null;
                string targetGrid = null;
                int targetTabIndex = -1;
                DateTime adjustedTime;

                if (sourceGrid == "PLC")
                {
                    // PLC → APP: convert PLC time → APP clock
                    adjustedTime = targetTime.AddSeconds(TimeSyncOffsetSeconds);
                    if (AppDevLogsFiltered != null && AppDevLogsFiltered.Count > 0)
                    {
                        targetCollection = AppDevLogsFiltered;
                        targetGrid = "APP";
                        targetTabIndex = 1;
                    }
                }
                else if (sourceGrid == "APP")
                {
                    // APP → PLC: convert APP time → PLC clock (reverse direction)
                    adjustedTime = targetTime.AddSeconds(-TimeSyncOffsetSeconds);
                    if (AllLogsCache != null && AllLogsCache.Count > 0)
                    {
                        targetCollection = AllLogsCache;
                        targetGrid = "PLC";
                        targetTabIndex = 0;
                    }
                }
                else
                {
                    adjustedTime = targetTime;
                }

                if (targetCollection == null || targetCollection.Count == 0) return;

                int nearestIndex = BinarySearchNearest(targetCollection, adjustedTime);

                if (nearestIndex >= 0)
                {
                    LogEntry nearestLog = targetCollection[nearestIndex];
                    TimeSpan timeDiff = (nearestLog.Date - adjustedTime).Duration();

                    if (timeDiff.TotalSeconds <= 60)
                    {
                        // Store pending sync - will scroll when user switches to target tab
                        _pendingSyncLog = nearestLog;
                        _pendingSyncTabIndex = targetTabIndex;

                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"🔗 Synced to {targetGrid} @ {nearestLog.Date:HH:mm:ss.ffffff} (±{timeDiff.TotalSeconds:F1}s) - switch tab to see";
                        });
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"⚠ No correlated logs within 60s (closest: {timeDiff.TotalSeconds:F0}s)";
                        });
                    }
                }
            }
            finally
            {
                _isSyncScrolling = false;
            }
        }

        /// <summary>
        /// Navigate to a log entry by time (called from Charts when user clicks on a point)
        /// </summary>
        public void NavigateToLogTime(DateTime time)
        {
            if (FilteredLogs == null || FilteredLogs.Count == 0) return;

            // Find the nearest log entry by time
            var nearestLog = FilteredLogs
                .OrderBy(l => Math.Abs((l.Date - time).TotalMilliseconds))
                .FirstOrDefault();

            if (nearestLog != null)
            {
                // Request the UI to scroll to this log
                RequestScrollToLog?.Invoke(nearestLog);
            }
        }

        /// <summary>
        /// Sync chart cursor when a log entry is selected (called from DataGrid selection)
        /// </summary>
        public void OnLogEntrySelected(LogEntry entry)
        {
            if (entry != null)
            {
                // Sync via ChartVM if available
                if (ChartVM?.HasData == true)
                {
                    ChartVM.SyncToLogTime(entry.Date);
                }

                // Also notify the transfer service for In-Memory sync
                ChartDataTransferService.Instance.NotifyLogTimeSelected(entry.Date);
            }
        }

        // ── Child VM PropertyChanged handlers (named, so Dispose can unsubscribe) ──

        private void SessionVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SessionVM.Logs): OnPropertyChanged(nameof(Logs)); break;
                case nameof(SessionVM.AllLogsCache): OnPropertyChanged(nameof(AllLogsCache)); break;
                case nameof(SessionVM.AllAppLogsCache): OnPropertyChanged(nameof(AllAppLogsCache)); break;
                case nameof(SessionVM.Events): OnPropertyChanged(nameof(Events)); break;
                case nameof(SessionVM.Screenshots): OnPropertyChanged(nameof(Screenshots)); break;
                case nameof(SessionVM.LoadedFiles): OnPropertyChanged(nameof(LoadedFiles)); break;
                case nameof(SessionVM.LoadedSessions): OnPropertyChanged(nameof(LoadedSessions)); break;
                case nameof(SessionVM.SelectedSession):
                    OnPropertyChanged(nameof(SelectedSession));
                    OnPropertyChanged(nameof(PlcTabHeader));
                    OnPropertyChanged(nameof(HasBinaryAppLogs));
                    OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                    OnPropertyChanged(nameof(ReportsButtonText));
                    OnPropertyChanged(nameof(ShowMainTabs));
                    OnPropertyChanged(nameof(HasExternalFileOnly));
                    _ = OnSelectedSessionChangedAsync(SessionVM.SelectedSession);
                    break;
                case nameof(SessionVM.CurrentProgress): OnPropertyChanged(nameof(CurrentProgress)); break;
                case nameof(SessionVM.StatusMessage): OnPropertyChanged(nameof(StatusMessage)); break;
                case nameof(SessionVM.IsBusy): OnPropertyChanged(nameof(IsBusy)); break;
            }
        }

        private void FilterVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FilterVM.FilteredLogs): OnPropertyChanged(nameof(FilteredLogs)); break;
                case nameof(FilterVM.AppDevLogsFiltered): OnPropertyChanged(nameof(AppDevLogsFiltered)); break;
                case nameof(FilterVM.SearchText): OnPropertyChanged(nameof(SearchText)); break;
                case nameof(FilterVM.IsSearchPanelVisible): OnPropertyChanged(nameof(IsSearchPanelVisible)); break;
                case nameof(FilterVM.LoggerTreeRoot): OnPropertyChanged(nameof(LoggerTreeRoot)); break;
                case nameof(FilterVM.PlcLoggerTreeRoot): OnPropertyChanged(nameof(PlcLoggerTreeRoot)); break;
                case nameof(FilterVM.SelectedTreeItem): OnPropertyChanged(nameof(SelectedTreeItem)); break;
                case nameof(FilterVM.IsMainFilterActive): OnPropertyChanged(nameof(IsMainFilterActive)); break;
                case nameof(FilterVM.IsAppFilterActive): OnPropertyChanged(nameof(IsAppFilterActive)); break;
            }
        }

        private void LiveVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(LiveVM.IsLiveMode): OnPropertyChanged(nameof(IsLiveMode)); break;
                case nameof(LiveVM.IsRunning): OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(IsPaused)); break;
                case nameof(LiveVM.IsPaused): OnPropertyChanged(nameof(IsPaused)); break;
            }
        }

        private void DifferentLogsVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DifferentLogsVM.HasFile))
            {
                OnPropertyChanged(nameof(ShowMainTabs));
                OnPropertyChanged(nameof(HasExternalFileOnly));
            }
        }

        // ── Dispose ─────────────────────────────────────────────────────

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe child VM events
                if (SessionVM != null) SessionVM.PropertyChanged -= SessionVM_PropertyChanged;
                if (FilterVM != null) FilterVM.PropertyChanged -= FilterVM_PropertyChanged;
                if (LiveVM != null) LiveVM.PropertyChanged -= LiveVM_PropertyChanged;
                if (DifferentLogsVM != null) DifferentLogsVM.PropertyChanged -= DifferentLogsVM_PropertyChanged;
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}