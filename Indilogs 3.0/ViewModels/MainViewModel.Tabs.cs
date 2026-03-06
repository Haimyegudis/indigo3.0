using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels.Components;
using System.Collections.Generic;
using System.Linq;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // ── Tab header computed properties ──

        public string DbConfigTabHeader =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs == true ? "TERMINALS" : "DB & CONFIG";

        public string PlcTabHeader =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs == true ? "PLC-FW" : "PLC LOGS";

        public bool HasBinaryAppLogs =>
            SessionVM?.SelectedSession?.HasBinaryAppLogs ?? false;

        public bool HasSessionLoaded => SessionVM?.SelectedSession != null;

        public bool HasExternalFileOnly => SessionVM?.SelectedSession == null && DifferentLogsVM?.HasFile == true;

        public bool ShowMainTabs => HasSessionLoaded || HasExternalFileOnly;

        public bool HasGlobalsFiles =>
            SessionVM?.SelectedSession?.GlobalsFiles != null
            && SessionVM.SelectedSession.GlobalsFiles.Count > 0;

        // ── Per-tab show/hide flags ──

        private bool _showPlcTab = true;
        public bool ShowPlcTab { get => _showPlcTab; set { _showPlcTab = value; OnPropertyChanged(); } }

        private bool _showAppTab = true;
        public bool ShowAppTab { get => _showAppTab; set { _showAppTab = value; OnPropertyChanged(); } }

        private bool _showEventsTab = true;
        public bool ShowEventsTab { get => _showEventsTab; set { _showEventsTab = value; OnPropertyChanged(); } }

        private bool _showScreenshotsTab = true;
        public bool ShowScreenshotsTab { get => _showScreenshotsTab; set { _showScreenshotsTab = value; OnPropertyChanged(); } }

        private bool _showConfigTab = true;
        public bool ShowConfigTab { get => _showConfigTab; set { _showConfigTab = value; OnPropertyChanged(); } }

        private bool _showDbConfigTab = true;
        public bool ShowDbConfigTab { get => _showDbConfigTab; set { _showDbConfigTab = value; OnPropertyChanged(); } }

        private bool _showSetupInfoTab = true;
        public bool ShowSetupInfoTab { get => _showSetupInfoTab; set { _showSetupInfoTab = value; OnPropertyChanged(); } }

        private bool _showGlobalsTab = true;
        public bool ShowGlobalsTab { get => _showGlobalsTab; set { _showGlobalsTab = value; OnPropertyChanged(); } }

        private bool _showSystabTab = true;
        public bool ShowSystabTab { get => _showSystabTab; set { _showSystabTab = value; OnPropertyChanged(); } }

        private bool _showChartsTab = true;
        public bool ShowChartsTab { get => _showChartsTab; set { _showChartsTab = value; OnPropertyChanged(); } }

        private bool _showCprTab = true;
        public bool ShowCprTab { get => _showCprTab; set { _showCprTab = value; OnPropertyChanged(); } }

        private bool _showStepRecorderTab = true;
        public bool ShowStepRecorderTab { get => _showStepRecorderTab; set { _showStepRecorderTab = value; OnPropertyChanged(); } }

        private bool _showDifferentLogsTab = true;
        public bool ShowDifferentLogsTab { get => _showDifferentLogsTab; set { _showDifferentLogsTab = value; OnPropertyChanged(); } }

        // ── Tab selection ──

        private TabSelectionConfig? _lastTabSelection;

        public void ApplyTabSelection(TabSelectionConfig? selection, TabSelectionConfig? preScan)
        {
            _lastTabSelection = selection;

            // Reset all to visible first
            ShowPlcTab = true;
            ShowAppTab = true;
            ShowEventsTab = true;
            ShowScreenshotsTab = true;
            ShowConfigTab = true;
            ShowDbConfigTab = true;
            ShowSetupInfoTab = true;
            ShowGlobalsTab = true;
            ShowSystabTab = true;
            ShowChartsTab = true;
            ShowCprTab = true;
            ShowStepRecorderTab = true;
            ShowDifferentLogsTab = true;

            if (selection == null) return;

            ShowPlcTab = selection.LoadPlc;
            ShowAppTab = selection.LoadApp;
            ShowEventsTab = selection.LoadEvents;
            ShowScreenshotsTab = selection.LoadScreenshots;
            ShowConfigTab = selection.LoadConfiguration;
            // DB & CONFIG / TERMINALS tab is controlled by Configuration + Terminal selection
            ShowDbConfigTab = selection.LoadConfiguration || selection.LoadTerminalLogs || selection.LoadLrs;
            ShowSetupInfoTab = selection.LoadSetupInfo;
            ShowGlobalsTab = selection.LoadGlobals;
            ShowSystabTab = selection.LoadSystab;
            ShowChartsTab = selection.ShowCharts;
            ShowCprTab = selection.ShowCpr;
            ShowStepRecorderTab = selection.ShowStepRecorder;
            ShowDifferentLogsTab = selection.ShowDifferentLogs;
        }

        /// <summary>
        /// Re-evaluates data-driven tab visibility after session data is loaded.
        /// </summary>
        public void UpdateTabVisibilityAfterLoad()
        {
            bool userAllowsGlobals = _lastTabSelection?.LoadGlobals ?? true;
            bool userAllowsSystab = _lastTabSelection?.LoadSystab ?? true;
            bool userAllowsSetupInfo = _lastTabSelection?.LoadSetupInfo ?? true;

            ShowGlobalsTab = userAllowsGlobals && HasGlobalsFiles;
            ShowSystabTab = userAllowsSystab && HasSystabFiles;
            // Setup Info: hide for S4-5 (HasBinaryAppLogs) even if user checked it
            ShowSetupInfoTab = userAllowsSetupInfo && HasSessionLoaded && !HasBinaryAppLogs;

            OnPropertyChanged(nameof(HasGlobalsFiles));
            OnPropertyChanged(nameof(HasBinaryAppLogs));
            OnPropertyChanged(nameof(DbConfigTabHeader));
            OnPropertyChanged(nameof(PlcTabHeader));
            OnPropertyChanged(nameof(ShowMainTabs));
            OnPropertyChanged(nameof(HasExternalFileOnly));
            OnPropertyChanged(nameof(IsPrintAnalysisVisible));
            OnPropertyChanged(nameof(ReportsButtonText));
        }

        // ── Skipped components ──

        public bool HasSkippedComponents
        {
            get
            {
                var skipped = GetSkippedComponents();
                return skipped != null && skipped.Count > 0;
            }
        }

        public List<(string Name, string DisplayName)> GetSkippedComponents()
        {
            var session = SessionVM?.SelectedSession;
            if (session?.LoadTabSelection == null || session?.PreScanConfig == null)
                return new List<(string, string)>();

            var sel = session.LoadTabSelection;
            var pre = session.PreScanConfig;
            var result = new List<(string Name, string DisplayName)>();

            if (!sel.LoadPlc && pre.HasPlc) result.Add(("Plc", "PLC Logs"));
            if (!sel.LoadApp && pre.HasApp) result.Add(("App", "APP Logs"));
            if (!sel.LoadEvents && pre.HasEvents) result.Add(("Events", "Events"));
            if (!sel.LoadScreenshots && pre.HasScreenshots) result.Add(("Screenshots", "Screenshots"));
            if (!sel.LoadConfiguration && pre.HasConfiguration) result.Add(("Configuration", "Configuration"));
            if (!sel.LoadTerminalLogs && pre.HasTerminalLogs) result.Add(("TerminalLogs", "Terminal Logs"));
            if (!sel.LoadLrs && pre.HasLrs) result.Add(("Lrs", "LRS"));
            if (!sel.LoadSetupInfo && pre.HasSetupInfo) result.Add(("SetupInfo", "Setup Info"));
            if (!sel.LoadSystab && pre.HasSystab) result.Add(("Systab", "Systab"));
            if (!sel.LoadGlobals && pre.HasGlobals) result.Add(("Globals", "Globals"));
            if (pre.IsS6 && !sel.LoadManagerThread && pre.HasManagerThread) result.Add(("ManagerThread", "Manager Thread"));

            // Tool tabs
            if (!sel.ShowCharts) result.Add(("Charts", "Charts"));
            if (!sel.ShowCpr) result.Add(("CPR", "CPR"));
            if (!pre.IsS6 && !sel.ShowStepRecorder) result.Add(("Step Recorder", "Step Recorder"));
            if (!sel.ShowDifferentLogs) result.Add(("Different Logs", "Different Logs"));

            return result;
        }

        // ── Selected tab index ──

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex == value) return;
                _selectedTabIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPLCTabSelected));
                OnPropertyChanged(nameof(IsAppTabSelected));
                OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                OnPropertyChanged(nameof(ReportsButtonText));
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(IsFilterOutActive));
                OnPropertyChanged(nameof(ActiveLoggerTree));
                OnPropertyChanged(nameof(LoggerTabTitle));
                OnPropertyChanged(nameof(IsExportVisible));
                OnPropertyChanged(nameof(ActiveFilters));
                OnPropertyChanged(nameof(HasActiveFilters));

                // Auto-load events DataView the first time user clicks the EVENTS tab
                if (_selectedTabIndex == AppConstants.TAB_EVENTS && _eventsDataView == null)
                    LoadEventsDataView();

                // Apply pending time-sync scroll when user switches to the target tab
                if (_pendingSyncLog != null && _selectedTabIndex == _pendingSyncTabIndex)
                {
                    var logToScroll = _pendingSyncLog;
                    _pendingSyncLog = null;
                    _pendingSyncTabIndex = -1;
                    TimeSyncScrollWasApplied = true;
                    SelectedLog = logToScroll;
                    // Defer scroll to Loaded priority — the tab's grid isn't rendered yet
                    _dispatcher.Post(() => RequestScrollToLog?.Invoke(logToScroll),
                        Services.Interfaces.DispatchPriority.Loaded);
                }
            }
        }

        public bool IsPLCTabSelected => _selectedTabIndex == AppConstants.TAB_PLC;
        public bool IsAppTabSelected => _selectedTabIndex == AppConstants.TAB_APP;

        public bool IsPrintAnalysisVisible => IsAppTabSelected && !HasBinaryAppLogs;

        public string ReportsButtonText => HasBinaryAppLogs ? "📊 Statistics" : "⚙ Reports";

        public System.Collections.ObjectModel.ObservableCollection<Models.LoggerNode>? ActiveLoggerTree =>
            _selectedTabIndex == AppConstants.TAB_APP ? FilterVM?.LoggerTreeRoot : FilterVM?.PlcLoggerTreeRoot;

        public string LoggerTabTitle =>
            _selectedTabIndex == AppConstants.TAB_APP ? "App Loggers" : "PLC Loggers";
        public bool IsExportVisible => _selectedTabIndex == AppConstants.TAB_PLC || _selectedTabIndex == AppConstants.TAB_CHARTS;

        // ── Left panel tab ──

        private int _leftTabIndex;
        public int LeftTabIndex
        {
            get => _leftTabIndex;
            set { _leftTabIndex = value; OnPropertyChanged(); }
        }
    }
}
