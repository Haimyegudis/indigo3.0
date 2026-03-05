using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Services.Charts
{
    /// <summary>
    /// Package containing all chart data for In-Memory transfer
    /// </summary>
    public class ChartDataPackage
    {
        public string SessionName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<DateTime> TimeStamps { get; set; } = new();
        public List<SignalData> Signals { get; set; } = new();
        public List<StateData> States { get; set; } = new();
        public List<ThreadMessageData> ThreadMessages { get; set; } = new();
        public List<EventMarkerData> Events { get; set; } = new();
        /// <summary>When true, ChartTabControl skips gap detection (used for IO terminal data).</summary>
        public bool SuppressGapDetection { get; set; }
        /// <summary>EM_Statistics CSV content for Gantt chart display (optional, from ZIP extraction).</summary>
        public string? EmStatisticsCsvContent { get; set; }
    }

    /// <summary>
    /// Event marker for overlay display on charts (red markers)
    /// </summary>
    public class EventMarkerData
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Description { get; set; } = "";
        public string Parameters { get; set; } = "";
        public DateTime TimeStamp { get; set; }
        public int TimeIndex { get; set; }
    }

    /// <summary>
    /// Signal data for charting
    /// </summary>
    public class SignalData
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public SignalType SignalType { get; set; }

        /// <summary>
        /// Total number of data points (= unique timestamp count).
        /// For signals with Data set directly, falls back to Data.Length.
        /// Accessing this property never triggers lazy materialization.
        /// </summary>
        private int _dataLength;
        public int DataLength
        {
            get { return _dataLength > 0 ? _dataLength : (_data?.Length ?? 0); }
            set { _dataLength = value; }
        }

        /// <summary>Sparse data points collected during parsing (index → value).</summary>
        internal List<KeyValuePair<int, double>>? SparsePoints { get; set; }

        private double[]? _data;

        /// <summary>
        /// Dense data array. Lazy-materialized from SparsePoints on first access
        /// (avoids ~13GB upfront allocation when only a few signals are charted).
        /// </summary>
        public double[]? Data
        {
            get
            {
                if (_data == null && SparsePoints != null)
                    MaterializeData();
                return _data;
            }
            set { _data = value; }
        }

        /// <summary>Builds the dense double[] from sparse points + forward-fill.</summary>
        internal void MaterializeData()
        {
            int len = _dataLength;
            var data = new double[len];
            // Fill with NaN
            for (int i = 0; i < len; i++)
                data[i] = double.NaN;
            // Apply sparse values
            var sparse = SparsePoints;
            if (sparse != null)
            {
                for (int i = 0; i < sparse.Count; i++)
                {
                    var kv = sparse[i];
                    data[kv.Key] = kv.Value;
                }
            }
            // Forward-fill
            double lastValue = double.NaN;
            for (int i = 0; i < len; i++)
            {
                if (double.IsNaN(data[i]))
                    data[i] = lastValue;
                else
                    lastValue = data[i];
            }
            _data = data;
            SparsePoints = null; // Release sparse data for GC
        }
    }

    /// <summary>
    /// State data for Gantt visualization
    /// </summary>
    public class StateData
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public List<StateInterval> Intervals { get; set; } = new();
    }

    /// <summary>
    /// Thread message for overlay markers
    /// </summary>
    public class ThreadMessageData
    {
        public int TimeIndex { get; set; }
        public string ThreadName { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime TimeStamp { get; set; }
    }

    public enum SignalType
    {
        Analog,
        Digital,
        State
    }
}
