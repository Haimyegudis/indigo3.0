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
    }
}
