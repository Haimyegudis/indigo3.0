using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models
{
    public class SavedConfiguration
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedDate { get; set; }

        // --- הגדרות עבור ה-LOGS הראשיים (PLC) ---
        public List<ColoringCondition> MainColoringRules { get; set; }
        public FilterNode MainFilterRoot { get; set; }

        // --- הגדרות עבור ה-APP ---
        public List<ColoringCondition> AppColoringRules { get; set; }
        public FilterNode AppFilterRoot { get; set; }

        // --- הגדרות עבור ה-PLC Filtered ---
        public FilterNode PlcFilteredRoot { get; set; }

        // --- Flags to indicate which tabs were configured ---
        // If null/false, the tab should keep its current state when loading
        public bool? HasMainConfig { get; set; }
        public bool? HasAppConfig { get; set; }
        public bool? HasPlcFilteredConfig { get; set; }
    }
}