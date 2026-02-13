using System.Collections.Generic;

namespace IndiLogs_3._0.Models
{
    public class DefaultConfiguration
    {
        // PLC Filtered tab default filter (replaces hardcoded IsDefaultLog)
        public FilterNode PlcFilteredDefaultFilter { get; set; }

        // Default coloring rules for PLC/Main logs
        public List<ColoringCondition> MainDefaultColoringRules { get; set; }

        // Default coloring rules for APP logs
        public List<ColoringCondition> AppDefaultColoringRules { get; set; }

        // Flags to indicate which parts have been customized
        public bool HasCustomPlcFilter { get; set; }
        public bool HasCustomMainColoring { get; set; }
        public bool HasCustomAppColoring { get; set; }
    }
}
