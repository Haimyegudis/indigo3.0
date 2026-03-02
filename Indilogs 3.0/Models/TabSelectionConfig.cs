using System;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Configuration for which components to load from a ZIP file.
    /// Unchecked components are skipped entirely (no extraction, no parsing).
    /// Tab-only items (Charts, CPR, etc.) control visibility without affecting parsing.
    /// </summary>
    public class TabSelectionConfig
    {
        // --- Data components (affect both parsing AND tab visibility) ---
        public bool LoadApp { get; set; } = true;
        public bool LoadPlc { get; set; } = true;
        public bool LoadTerminalLogs { get; set; } = true;
        public bool LoadConfiguration { get; set; } = true;
        public bool LoadSystab { get; set; } = true;
        public bool LoadGlobals { get; set; } = true;
        public bool LoadLrs { get; set; } = true;
        public bool LoadEvents { get; set; } = true;
        public bool LoadScreenshots { get; set; } = true;
        public bool LoadSetupInfo { get; set; } = true;

        // S6-only
        public bool LoadManagerThread { get; set; } = true;

        // --- Tab-only visibility (no parsing impact, just show/hide the tab) ---
        public bool ShowCharts { get; set; } = true;
        public bool ShowCpr { get; set; } = true;
        public bool ShowStepRecorder { get; set; } = true;
        public bool ShowDifferentLogs { get; set; } = true;

        // --- Tracks which components actually exist in the ZIP (set by PreScanZip) ---
        public bool HasApp { get; set; }
        public bool HasPlc { get; set; }
        public bool HasTerminalLogs { get; set; }
        public bool HasConfiguration { get; set; }
        public bool HasSystab { get; set; }
        public bool HasGlobals { get; set; }
        public bool HasLrs { get; set; }
        public bool HasEvents { get; set; }
        public bool HasScreenshots { get; set; }
        public bool HasSetupInfo { get; set; }
        public bool HasManagerThread { get; set; }

        /// <summary>
        /// Whether this is an S6 configuration (non-binary APP).
        /// </summary>
        public bool IsS6 { get; set; }

        // --- Time range (detected by PreScanZip, user-filtered in TabSelectionWindow) ---
        public DateTime? AvailableStartTime { get; set; }
        public DateTime? AvailableEndTime { get; set; }
        public DateTime? FilterStartTime { get; set; }
        public DateTime? FilterEndTime { get; set; }
        public bool UseTimeFilter { get; set; }

        /// <summary>
        /// Creates a default config for the detected configuration type.
        /// All applicable components default to true (load everything).
        /// </summary>
        public static TabSelectionConfig CreateForConfiguration(bool isS6)
        {
            return new TabSelectionConfig { IsS6 = isS6 };
        }
    }
}
