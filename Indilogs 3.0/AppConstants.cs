using System;

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

        // ── Tab Indices ─────────────────────────────────────────────
        // Must match the order of TabItems in MainWindow.xaml
        public const int TAB_PLC = 0;
        public const int TAB_APP = 1;
        public const int TAB_PLC_FILTERED = 2;
        public const int TAB_SCREENSHOTS = 3;
        public const int TAB_GLOBALS = 4;
        public const int TAB_SYSTAB = 5;
        public const int TAB_EVENTS = 6;
        public const int TAB_TERMINALS = 7;
        public const int TAB_TIMELINE = 8;
        public const int TAB_CHARTS = 9;
        public const int TAB_CPR = 10;
        public const int TAB_STEP_RECORDER = 11;
        public const int TAB_DIFFERENT_LOGS = 12;
    }
}
