using System;
using System.IO;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0
{
    /// <summary>
    /// Application-wide constants to avoid magic numbers and repeated values.
    /// </summary>
    internal static class AppConstants
    {
        /// <summary>
        /// Default timeout for user-supplied regex patterns to prevent ReDoS attacks.
        /// </summary>
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Maximum depth for JSON deserialization to prevent stack overflow attacks.
        /// </summary>
        public const int JsonMaxDepth = 64;

        /// <summary>
        /// Shared Newtonsoft.Json settings with depth limit to prevent stack overflow attacks.
        /// </summary>
        public static readonly Newtonsoft.Json.JsonSerializerSettings SafeJsonSettings =
            new Newtonsoft.Json.JsonSerializerSettings { MaxDepth = JsonMaxDepth };

        /// <summary>
        /// Shared System.Text.Json options with depth limit for JsonDocument.Parse.
        /// </summary>
        public static readonly System.Text.Json.JsonDocumentOptions SafeJsonDocumentOptions =
            new System.Text.Json.JsonDocumentOptions { MaxDepth = JsonMaxDepth };

        /// <summary>
        /// ZIP file extension (case-insensitive comparisons should use OrdinalIgnoreCase).
        /// </summary>
        public const string ZipExtension = ".zip";

        /// <summary>
        /// CSV file extension.
        /// </summary>
        public const string CsvExtension = ".csv";

        /// <summary>
        /// Systab file name prefix used in ZIP classification.
        /// </summary>
        public const string SystabPrefix = "systab_";

        /// <summary>
        /// Systab file extension.
        /// </summary>
        public const string SystabExtension = ".txt";

        /// <summary>
        /// Batch size for UI update operations (e.g. ObservableRangeCollection batching).
        /// </summary>
        public const int UiUpdateBatchSize = 500;

        // ── Tab Indices ─────────────────────────────────────────────
        // Must match the order of TabItems in MainWindow.xaml
        public const int TAB_PLC = 0;
        public const int TAB_APP = 1;
        public const int TAB_EVENTS = 2;
        public const int TAB_SCREENSHOTS = 3;
        public const int TAB_CONFIG = 4;
        public const int TAB_TERMINALS = 5;
        public const int TAB_SETUP_INFO = 6;
        public const int TAB_GLOBALS = 7;
        public const int TAB_SYSTAB = 8;
        public const int TAB_CHARTS = 9;
        public const int TAB_CPR = 10;
        public const int TAB_STEP_RECORDER = 11;
        public const int TAB_DIFFERENT_LOGS = 12;

        // ── Shared Regex Patterns ─────────────────────────────────────
        /// <summary>
        /// S4-5 binary PLC state transitions: "STATE_STANDBY - Enter ======" / "STATE_SERVICE - Exit ======"
        /// </summary>
        public static readonly Regex S4StateRegex = new Regex(
            @"STATE_(\w+)\s*-\s*(Enter|Exit)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

        /// <summary>
        /// Matches timestamps like 2024-01-15T14-30-00 or 2024-01-15_14-30-00 embedded in file names.
        /// Groups: 1=year, 2=month, 3=day, 4=hour, 5=minute, 6=second.
        /// </summary>
        public static readonly Regex FileTimestampRegex = new Regex(
            @"(\d{4})-(\d{2})-(\d{2})[T_](\d{2})-(\d{2})-(\d{2})",
            RegexOptions.Compiled, RegexTimeout);

        // ── SQL Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Escapes a SQL identifier for safe use in bracket-quoted context ([identifier]).
        /// </summary>
        public static string EscapeSqlBracketId(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return identifier.Replace("]", "]]");
        }

        /// <summary>
        /// Escapes a SQL identifier for safe use in double-quoted context ("identifier").
        /// </summary>
        public static string EscapeSqlIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return identifier.Replace("\"", "\"\"");
        }
    }

    /// <summary>
    /// Centralized application data paths. Everything lives in a single
    /// flat folder: %AppData%\IndiLogs3.0\
    /// Prefixes prevent name collisions between dynamic file types.
    /// </summary>
    internal static class AppPaths
    {
        // ── Single folder for everything ─────────────────────────────
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3.0");

        // ── Fixed config files ───────────────────────────────────────
        public static readonly string SearchLocations = Path.Combine(Root, "search_locations.json");
        public static readonly string SearchSchedules = Path.Combine(Root, "search_schedules.json");
        public static readonly string DefaultConfig = Path.Combine(Root, "_defaults.json");
        public static readonly string GridColumnSettings = Path.Combine(Root, "GridColumnSettings.json");
        public static readonly string DifferentLogsColumnSettings = Path.Combine(Root, "DifferentLogsColumnSettings.json");
        public static readonly string StripeColumnOrder = Path.Combine(Root, "stripe_column_order.json");
        public static readonly string SnakeScores = Path.Combine(Root, "snake_scores.json");
        public static readonly string UpdateLog = Path.Combine(Root, "update_debug.log");
        public static readonly string UpdateCredentials = Path.Combine(Root, "update-credentials.dat");

        // ── Prefixed dynamic files (all in Root) ─────────────────────
        // Saved configs:     config_{name}.json
        // Search profiles:   searchprofile_{name}.json
        // DB column layouts:  dbcol_{name}.json
        // Plugins:           *.dll
        // App logs:          IndiLogs_{date}.log
        public const string ConfigPrefix = "config_";
        public const string SearchProfilePrefix = "searchprofile_";
        public const string DbColumnPrefix = "dbcol_";
    }
}
