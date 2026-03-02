using System;
using System.Collections.Generic;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.Models.Grep
{
    /// <summary>
    /// Holds all computed statistics for a log scan.
    /// </summary>
    public class LogStatisticsResult
    {
        // Summary
        public int TotalPlcLogs { get; set; }
        public int TotalAppLogs { get; set; }
        public int TotalPlcErrors { get; set; }
        public int TotalAppErrors { get; set; }
        public DateTime? EarliestTimestamp { get; set; }
        public DateTime? LatestTimestamp { get; set; }

        // PLC Statistics
        public List<ErrorStat> PlcTopErrors { get; set; } = new List<ErrorStat>();
        public List<LoadStat> PlcThreadLoad { get; set; } = new List<LoadStat>();
        public List<GapInfo> PlcGaps { get; set; } = new List<GapInfo>();

        // APP Statistics
        public List<ErrorStat> AppLoggerErrors { get; set; } = new List<ErrorStat>();
        public List<LoadStat> AppLoggerLoad { get; set; } = new List<LoadStat>();
        public List<ErrorStat> AppMethodErrors { get; set; } = new List<ErrorStat>();
        public List<LoadStat> AppMethodLoad { get; set; } = new List<LoadStat>();
        public List<GapInfo> AppGaps { get; set; } = new List<GapInfo>();

        // Advanced Analytics
        public List<StatCount> ErrorsBySource { get; set; } = new List<StatCount>();
        public List<StatCount> ErrorsByState { get; set; } = new List<StatCount>();
        public List<StateEntry> StateEntries { get; set; } = new List<StateEntry>();

        /// <summary>
        /// Whether APP logs are binary (S4-5) — if true, method statistics are not available.
        /// </summary>
        public bool HasBinaryAppLogs { get; set; }
    }

    /// <summary>
    /// Simple name + count pair for statistics (JSON-serializable, unlike tuples).
    /// </summary>
    public class StatCount
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }
}
