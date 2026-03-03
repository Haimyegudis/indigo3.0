using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Models
{
    public class LogSessionData
    {

        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public List<LogEntry> AppDevLogs { get; set; } = new List<LogEntry>();

        /// <summary>
        /// Custom column layout supplied by the plugin that produced this session's entries.
        /// Null when the session was loaded by built-in parsers (default columns are used).
        /// </summary>
        public IReadOnlyList<PluginColumnDef> PluginColumns { get; set; }

        // --- Optimization: pre-built lists for fast analysis ---
        public List<LogEntry> StateTransitions { get; set; } = new List<LogEntry>();
        public List<LogEntry> CriticalFailureEvents { get; set; } = new List<LogEntry>();
        // ----------------------------------------------------

        // >>> Fix: added the missing dictionary <<<
        public Dictionary<string, string> ConfigurationFiles { get; set; } = new Dictionary<string, string>();

        // Binary DB files (SQLite) - stored as byte arrays
        public Dictionary<string, byte[]> DatabaseFiles { get; set; } = new Dictionary<string, byte[]>();

        // Terminal log files (from \TerminalLogs\ path in binary APP ZIPs)
        // Text files (.txt, .log) stored as strings for display
        public Dictionary<string, string> TerminalLogFiles { get; set; } = new Dictionary<string, string>();

        // Terminal CSV files (.csv) stored as raw bytes for deferred parsing
        // Avoids expensive byte[]→string conversion during ZIP loading; decoded on demand by IoTerminalDataService
        public Dictionary<string, byte[]> TerminalCsvBytes { get; set; } = new Dictionary<string, byte[]>();

        // Globals XML files (from \DataManagement\eCommon\Globals\ path in ZIPs)
        public Dictionary<string, string> GlobalsFiles { get; set; } = new Dictionary<string, string>();

        // Systab files (from \DiagnosticsLogs\systab_*.txt in binary APP ZIPs)
        // Keys: "saved", "default", "minimum", "maximum" -> file contents
        public Dictionary<string, string> SystabFiles { get; set; } = new Dictionary<string, string>();

        // Flag: true when this session has binary APP logs (numeric .file pattern)
        public bool HasBinaryAppLogs { get; set; }

        // Configuration type detected during parsing: "S4-5" or "S6"
        // S6: PLC logs contain ThreadName "Manager" (no binary APP logs)
        // S4-5: No "Manager" thread found in PLC logs (no binary APP logs)
        // null: Detection not applicable (binary APP logs exist)
        public string ConfigurationType { get; set; }

        public List<EventEntry> Events { get; set; } = new List<EventEntry>();

        // Raw events CSV content for full-column display
        public string EventsCsvRawContent { get; set; }
        public List<BitmapImage> Screenshots { get; set; } = new List<BitmapImage>();
        public ObservableCollection<LogEntry> MarkedLogs { get; set; } = new ObservableCollection<LogEntry>();

        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string SetupInfo { get; set; }
        public string PressConfiguration { get; set; }
        public string VersionsInfo { get; set; }

        public List<StateEntry> CachedStates { get; set; }
        public List<AnalysisResult> CachedAnalysis { get; set; }

        // --- Tab selection configs (preserved for add-back feature) ---
        public TabSelectionConfig LoadTabSelection { get; set; }
        public TabSelectionConfig PreScanConfig { get; set; }

        // --- EM_Statistics CSV content (extracted from ZIP for Gantt chart) ---
        public string EmStatisticsCsvContent { get; set; }

        // --- Per-session filter state (preserved across session switches) ---
        public SessionFilterState SavedFilterState { get; set; }

        // --- Per-session chart state (preserved across session switches) ---
        public SessionChartState SavedChartState { get; set; }
    }

    /// <summary>
    /// Stores the filter state for a session so it persists when switching between sessions.
    /// </summary>
    public class SessionFilterState
    {
        public FilterNode MainFilterRoot { get; set; }
        public FilterNode AppFilterRoot { get; set; }
        public bool IsMainFilterActive { get; set; }
        public bool IsAppFilterActive { get; set; }
        public bool IsMainFilterOutActive { get; set; }
        public bool IsAppFilterOutActive { get; set; }
        public bool IsTimeFocusActive { get; set; }
        public bool IsAppTimeFocusActive { get; set; }
        public List<string> NegativeFilters { get; set; }
        public List<string> ActiveThreadFilters { get; set; }
        public string SearchText { get; set; }
        public List<LogEntry> LastFilteredCache { get; set; }
        public List<LogEntry> LastFilteredAppCache { get; set; }
    }

    /// <summary>
    /// Stores the complete chart state for a session so it persists when switching between sessions.
    /// All large data (signal arrays, data packages) are stored as references — NOT deep-copied.
    /// </summary>
    public class SessionChartState
    {
        // Data state (references to existing objects)
        public ChartDataPackage DataPackage { get; set; }
        public string[] TimeData { get; set; }
        public List<StateInterval> GlobalStates { get; set; }
        public List<ThreadMessageData> ThreadMessages { get; set; }
        public List<StateData> ChStepStates { get; set; }
        public List<EventMarkerData> EventMarkers { get; set; }
        public List<GapRegion> TimeGapRegions { get; set; }
        public int TotalDataLength { get; set; }
        public bool InMemoryDataLoaded { get; set; }

        // Service references (CSV engine, time mapping)
        public ChartDataService DataService { get; set; }
        public ChartSyncService SyncService { get; set; }

        // Chart panels (stored by reference)
        public List<ChartViewModel> Charts { get; set; }

        // View/navigation state
        public int ViewStartIndex { get; set; }
        public int ViewEndIndex { get; set; }
        public int CursorIndex { get; set; }

        // UI preferences
        public bool ShowStates { get; set; }
        public bool IsGridLayout { get; set; }
        public bool IsSignalPanelVisible { get; set; }
        public int SmoothWindowSize { get; set; }
        public int ColorIndex { get; set; }
        public int SelectedChartIndex { get; set; } = -1;
    }
}