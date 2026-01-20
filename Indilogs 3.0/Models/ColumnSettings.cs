using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Stores column display settings including width, order, and visibility
    /// </summary>
    [Serializable]
    public class ColumnSettings
    {
        public Dictionary<string, double> ColumnWidths { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, int> ColumnOrders { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, bool> ColumnVisibility { get; set; } = new Dictionary<string, bool>();
    }

    /// <summary>
    /// Container for all grid settings
    /// </summary>
    [Serializable]
    public class GridSettings
    {
        public ColumnSettings PlcColumns { get; set; } = new ColumnSettings();
        public ColumnSettings AppColumns { get; set; } = new ColumnSettings();
    }
}
