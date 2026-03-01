using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Data.SQLite;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel : ViewModelBase
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

        private readonly ILogFileService _logService;
        private readonly ILogColoringService _coloringService;
        private readonly ICsvExportService _csvService;
        private readonly IDefaultConfigurationService _defaultConfigService;
        public IDefaultConfigurationService DefaultConfigService => _defaultConfigService;
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

        // Coloring
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();
        // Case Management — bind XAML directly to CaseVM.* properties

        private const int UI_UPDATE_BATCH_SIZE = AppConstants.UiUpdateBatchSize;
        private readonly object _collectionLock = new object();

        // Collections — bind XAML directly to SessionVM.* / FilterVM.* properties

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
            if (SessionVM?.SelectedSession?.EventsCsvRawContent == null) return;

            try
            {
                var dt = new System.Data.DataTable();
                var csvContent = SessionVM.SelectedSession.EventsCsvRawContent;
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

        // Session/Live — bind XAML directly to SessionVM.* / LiveVM.* properties

        // Search & Filter — bind XAML directly to FilterVM.* properties
        // Live Mode — bind XAML directly to LiveVM.* properties

        // Case Management — bind XAML directly to CaseVM.* properties

        // Config Explorer — bind XAML directly to ConfigVM.* properties

        // Dynamic tab header: "TERMINALS" for binary APP logs, "DB & CONFIG" otherwise
        public string DbConfigTabHeader =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs == true ? "TERMINALS" : "DB & CONFIG";

        // Dynamic tab header: "PLC-FW" for S4 (binary APP logs), "PLC LOGS" otherwise
        public string PlcTabHeader =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs == true ? "PLC-FW" : "PLC LOGS";

        // Hide SetupInfo tab when APP files are binary
        public bool HasBinaryAppLogs =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs == true;

        public bool HasSessionLoaded => SessionVM?.SelectedSession != null;

        /// <summary>True when no ZIP session is loaded but an external file is open in Different Logs.</summary>
        public bool HasExternalFileOnly => SessionVM?.SelectedSession == null && DifferentLogsVM?.HasFile == true;

        /// <summary>Controls MainTabs visibility: shown when a session is loaded OR an external file is open.</summary>
        public bool ShowMainTabs => HasSessionLoaded || HasExternalFileOnly;

        // Show Globals tab only when loaded from a ZIP that contains globals files
        public bool HasGlobalsFiles =>
            SessionVM?.SelectedSession?.GlobalsFiles != null && SessionVM.SelectedSession.GlobalsFiles.Count > 0 &&
            SessionVM.SelectedSession.FilePath != null && SessionVM.SelectedSession.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

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
            IsPLCTabSelected ? FilterVM?.PlcLoggerTreeRoot : FilterVM?.LoggerTreeRoot;

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
            if (string.IsNullOrWhiteSpace(FilterVM?.SearchText))
            {
                IsSearchSyntaxValid = true;
                SearchSyntaxError = null;
                return;
            }

            if (QueryParserService.HasBooleanOperators(FilterVM.SearchText))
            {
                var parser = new QueryParserService();
                var result = parser.Parse(FilterVM.SearchText, out string errorMessage);

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
                return SelectedTabIndex == AppConstants.TAB_APP ? (FilterVM?.IsAppFilterActive ?? false) : (FilterVM?.IsMainFilterActive ?? false);
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
            get => SelectedTabIndex == AppConstants.TAB_APP ? (FilterVM?.IsAppFilterOutActive ?? false) : (FilterVM?.IsMainFilterOutActive ?? false);
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

        public MainViewModel(ILogFileService logService, ILogColoringService coloringService, ICsvExportService csvService, IDefaultConfigurationService defaultConfigService)
        {
            _logService = logService;
            _coloringService = coloringService;
            _csvService = csvService;
            _defaultConfigService = defaultConfigService;
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
            DifferentLogsVM.GetCurrentZipPath = () => SessionVM?.SelectedSession?.FilePath;
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
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            ToggleExplorerMenuCommand = new RelayCommand(o => ConfigVM.IsExplorerMenuOpen = !ConfigVM.IsExplorerMenuOpen);
            ToggleConfigMenuCommand = new RelayCommand(o => ConfigVM.IsConfigMenuOpen = !ConfigVM.IsConfigMenuOpen);
            ToggleLoggersMenuCommand = new RelayCommand(o => ConfigVM.IsLoggersMenuOpen = !ConfigVM.IsLoggersMenuOpen);
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
            ClearCommand = new RelayCommand(o => { SessionVM.ClearCommand.Execute(o); ConfigVM.IsExplorerMenuOpen = false; });
            RemoveSessionCommand = SessionVM.RemoveSessionCommand;
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);
            FilterAppErrorsCommand = new RelayCommand(FilterAppErrors);
            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);

            OpenMarkedLogsWindowCommand = new RelayCommand(o => { OpenMarkedLogsWindow(o); ConfigVM.IsExplorerMenuOpen = false; });
            OpenStatesWindowCommand = new RelayCommand(o => { OpenStatesWindow(o); ConfigVM.IsExplorerMenuOpen = false; });
            ExportParsedDataCommand = new RelayCommand(o => { _ = ExportParsedData(o); ConfigVM.IsExplorerMenuOpen = false; });
            RunAnalysisCommand = new RelayCommand(o => { RunAnalysis(o); ConfigVM.IsExplorerMenuOpen = false; });
            OpenGlobalGrepCommand = new RelayCommand(o => { OpenGlobalGrepWindow(); ConfigVM.IsExplorerMenuOpen = false; });
            OpenStripeAnalysisCommand = new RelayCommand(o => { _ = OpenStripeAnalysisWindow(); ConfigVM.IsExplorerMenuOpen = false; });
            OpenComparisonCommand = new RelayCommand(o => { OpenComparisonWindow(); }, o => SessionVM.AllLogsCache?.Count > 0 || SessionVM.AllAppLogsCache?.Count > 0);

            ToggleSearchCommand = FilterVM.ToggleSearchCommand;
            CloseSearchCommand = FilterVM.CloseSearchCommand;
            ShowFailuresCommand = new RelayCommand(_ => ShowFailuresAnalysis());
            OpenFilterWindowCommand = FilterVM.OpenFilterWindowCommand;
            OpenColoringWindowCommand = CaseVM.OpenColoringWindowCommand;

            SaveConfigCommand = new RelayCommand(o => { SaveConfiguration(o); ConfigVM.IsConfigMenuOpen = false; });
            LoadConfigCommand = new RelayCommand(o => { LoadConfigurationFromFile(o); ConfigVM.IsConfigMenuOpen = false; });
            RemoveConfigCommand = new RelayCommand(o => { RemoveConfiguration(o); ConfigVM.IsConfigMenuOpen = false; }, o => CaseVM?.SelectedConfig != null);
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
            var logsToUse = FilterVM.IsGlobalTimeRangeActive ? SessionVM.Logs : (SessionVM.AllLogsCache ?? SessionVM.Logs);
            if (VisualTimelineVM != null)
            {
                // S4-5 (binary APP): skip Events on timeline — user only needs states + errors
                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
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

            if (CaseVM != null) CaseVM.ShowAllAnnotations = newState;
            SessionVM.StatusMessage =newState ? "All annotations expanded" : "All annotations collapsed";
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

        private void LiveClear(object obj)
        {
            LiveVM.IsRunning = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_collectionLock)
                {
                    if (SessionVM.AllLogsCache != null) SessionVM.AllLogsCache.Clear();
                    FilterVM?.FilteredLogs?.Clear();
                    SelectedLog = null;
                }
            });

            if (LiveVM.IsLiveMode)
            {
                LiveVM.IsRunning = true;
                SessionVM.StatusMessage ="Cleared. Monitoring continues...";
            }
            else
            {
                SessionVM.StatusMessage ="Logs cleared.";
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

                    if (FilterVM?.LoggerTreeRoot != null)
                    {
                        FilterVM.LoggerTreeRoot.Clear();
                    }
                    if (FilterVM?.PlcLoggerTreeRoot != null)
                    {
                        FilterVM.PlcLoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(ActiveLoggerTree));
                    }

                    FilterVM.SearchText = "";
                    FilterVM.IsSearchPanelVisible = false;
                }

                SessionVM.Logs = new List<LogEntry>();

                ConfigVM?.ClearConfigurationFiles();
                // ConfigVM properties refresh automatically via their own PropertyChanged

                SetupInfo = "";
                OnPropertyChanged(nameof(SetupInfo));
                PressConfig = "";
                OnPropertyChanged(nameof(PressConfig));
                VersionsInfo = "";
                OnPropertyChanged(nameof(VersionsInfo));
                WindowTitle = "IndiLogs 3.0";
                OnPropertyChanged(nameof(WindowTitle));

                SessionVM.CurrentProgress = 0;
                ScreenshotZoom = 400;
                SessionVM.SelectedSession = null;
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

                SessionVM.StatusMessage ="All data cleared successfully";
            }
            catch (Exception ex)
            {
                SessionVM.StatusMessage =$"Clear failed: {ex.Message}";
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

            if (SessionVM.SelectedSession == null) { MessageBox.Show("No logs loaded."); return; }

            if (SessionVM.SelectedSession.CachedStates != null && SessionVM.SelectedSession.CachedStates.Count > 0)
            {
                if (_statesWindow != null && _statesWindow.IsVisible) { WindowManager.ActivateWindow(_statesWindow); return; }

                _statesWindow = new StatesWindow(SessionVM.SelectedSession.CachedStates, this);
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
            FilterVM.SearchText = string.Empty;
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

            SessionVM.StatusMessage ="Filter reset. Showing all data.";
        }

        private void RunAnalysis(object obj)
        {
            if (SessionVM.SelectedSession == null)
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

            if (SessionVM.SelectedSession.CachedAnalysis != null && SessionVM.SelectedSession.CachedAnalysis.Any())
            {
                OpenAnalysisWindow(SessionVM.SelectedSession.CachedAnalysis);
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

                    int logCount = SessionVM?.Logs?.Count() ?? 0;
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

                    int logCount = SessionVM?.Logs?.Count() ?? 0;
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
                SessionVM.IsBusy = true;
                SessionVM.StatusMessage =$"Focusing state: {state.StateName}...";

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
                            SessionVM.StatusMessage =$"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";

                            if (IsVisualMode && VisualTimelineVM != null)
                            {
                                // Use filtered logs if time range is active
                                var logsForVisual = FilterVM.IsGlobalTimeRangeActive ? SessionVM.Logs : SessionVM.AllLogsCache.ToList();
                                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
                                VisualTimelineVM.LoadData(logsForVisual, eventsToShow);
                                VisualTimelineVM.FocusOnState(state.StateName);
                            }

                            SessionVM.IsBusy =false;
                        });
                    }
                    else
                    {
                        SessionVM.IsBusy =false;
                    }
                });
            }
        }
        private void FilterAppErrors(object obj)
        {
            if (SessionVM.AllAppLogsCache == null || !SessionVM.AllAppLogsCache.Any()) return;
            SessionVM.IsBusy = true;
            SessionVM.StatusMessage ="Filtering App Errors...";
            Task.Run(() =>
            {
                var errors = SessionVM.AllAppLogsCache.Where(l => l.Level != null && l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Date).ToList();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilterVM?.AppDevLogsFiltered?.ReplaceAll(errors);
                    SessionVM.IsBusy =false;
                    SessionVM.StatusMessage =$"Showing {errors.Count} Errors";
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
            if (FilterVM?.LoggerTreeRoot != null)
                foreach (var node in FilterVM.LoggerTreeRoot) ResetVisualHiddenState(node);
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
            if (FilterVM?.AppDevLogsFiltered == null || FilterVM.AppDevLogsFiltered.Count == 0) return;
            SessionVM.IsBusy = true;
            SessionVM.StatusMessage ="Sorting...";
            await Task.Run(() =>
            {
                List<LogEntry> sorted = null;
                var source = FilterVM.AppDevLogsFiltered.ToList();
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
                    FilterVM.AppDevLogsFiltered.ReplaceAll(sorted);
                    SessionVM.IsBusy =false;
                    SessionVM.StatusMessage ="Sorted.";
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
        private async Task LoadFile(object obj)
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
        private async Task OpenFilterWindow(object obj)
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
                SessionVM.IsBusy = true;
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
                    SessionVM.IsBusy =false;
                });
            }
            }
            catch (Exception ex) { AppLogger.Error("OpenFilterWindow failed", ex); }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node) => FilterVM?.EvaluateFilterNode(log, node) ?? true;
        private async Task ExportParsedData(object obj)
        {
            try
            {
            if (SessionVM.SelectedSession == null)
            {
                MessageBox.Show("No logs loaded.", "Info");
                return;
            }

            var selectedSession = SessionVM.SelectedSession;
            // S4-5 (binary APP): allow export even without parsed PLC logs — IO data comes from CSV
            bool hasLogs = selectedSession.Logs != null && selectedSession.Logs.Any();
            bool hasIoCsv = selectedSession.HasBinaryAppLogs &&
                            ((selectedSession.TerminalCsvBytes != null && selectedSession.TerminalCsvBytes.Keys.Any(k => k.StartsWith("Io-", StringComparison.OrdinalIgnoreCase))) ||
                             (selectedSession.TerminalLogFiles != null && selectedSession.TerminalLogFiles.Keys.Any(k => k.StartsWith("Io-", StringComparison.OrdinalIgnoreCase))));

            if (!hasLogs && !hasIoCsv)
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
            var viewModel = new ExportConfigurationViewModel(selectedSession, _csvService);
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

        private void OpenMarkedLogsWindow(object obj) => CaseVM?.OpenMarkedWindowCommand.Execute(obj);

        private void OpenGlobalGrepWindow()
        {
            // יצירת אוסף ריק במידה ולא נטענו סשנים, כדי לאפשר לחלון להיפתח
            var sessions = SessionVM?.LoadedSessions ?? new ObservableCollection<LogSessionData>();

            var viewModel = new GlobalGrepViewModel(sessions);

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

        private async Task OpenStripeAnalysisWindow()
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
                _ = window.LoadFromLogs(logs);
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
                if (result.SessionIndex < SessionVM.LoadedSessions.Count)
                {
                    SessionVM.SelectedSession = SessionVM.LoadedSessions[result.SessionIndex];

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
            var session = SessionVM.LoadedSessions.FirstOrDefault(s => s.FilePath == result.FilePath);

            if (session != null)
            {
                SessionVM.SelectedSession = session;
                JumpByTime(result, session);
            }
            else
            {
                // Load the file if not already loaded
                ProcessFiles(new[] { result.FilePath }, (loadedSession) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SessionVM.SelectedSession = loadedSession;
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
            var loadedFilePaths = SessionVM.LoadedSessions.Select(s => s.FilePath).ToList();

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
        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { AppLogger.Error($"OpenUrl failed for '{url}'", ex); } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        /// <summary>
        /// Opens Kibana in the default browser, pre-filling the machine name from the loaded ZIP.
        /// </summary>
        private void OpenKibana(object obj)
        {
            string zipPath = SessionVM?.SelectedSession?.FilePath ?? string.Empty;
            string machineName = string.Empty;

            if (!string.IsNullOrEmpty(zipPath))
            {
                string fileName = Path.GetFileNameWithoutExtension(zipPath);
                int underscoreIdx = fileName.IndexOf('_');
                machineName = underscoreIdx > 0 ? fileName.Substring(0, underscoreIdx) : fileName;
            }

            if (string.IsNullOrEmpty(machineName))
            {
                OpenUrl("http://localhost/");
                return;
            }

            string escapedMachineName = Uri.EscapeDataString(machineName);
            string nextParam = Uri.EscapeDataString($"/{escapedMachineName}/app/home");
            string url = $"http://localhost/{escapedMachineName}/login?next={nextParam}";
            OpenUrl(url);
        }

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

        // ── Child VM PropertyChanged handlers (named, so Dispose can unsubscribe) ──

        private void SessionVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SessionVM.SelectedSession):
                    OnPropertyChanged(nameof(PlcTabHeader));
                    OnPropertyChanged(nameof(HasBinaryAppLogs));
                    OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                    OnPropertyChanged(nameof(ReportsButtonText));
                    OnPropertyChanged(nameof(ShowMainTabs));
                    OnPropertyChanged(nameof(HasExternalFileOnly));
                    _ = OnSelectedSessionChangedAsync(SessionVM.SelectedSession);
                    break;
            }
        }

        private void FilterVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FilterVM.LoggerTreeRoot):
                case nameof(FilterVM.PlcLoggerTreeRoot):
                    OnPropertyChanged(nameof(ActiveLoggerTree));
                    break;
                case nameof(FilterVM.IsMainFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsAppFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsMainFilterOutActive):
                case nameof(FilterVM.IsAppFilterOutActive):
                    OnPropertyChanged(nameof(IsFilterOutActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.HasRangeStart):
                    OnPropertyChanged(nameof(HasRangeStart));
                    break;
            }
        }

        private void LiveVM_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            // LiveVM properties (IsLiveMode, IsRunning, IsPaused) bind directly via LiveVM.* in XAML
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