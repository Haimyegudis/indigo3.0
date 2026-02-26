using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace IndiLogs_3._0.Models.Charts
{
    /// <summary>
    /// Configuration for machine states - names, colors, and ID mapping
    /// </summary>
    public static class ChartStateConfig
    {
        // State name mapping
        public static readonly Dictionary<int, string> StateNames = new Dictionary<int, string>
        {
            { 0, "UNDEFINED" }, { 1, "INIT" }, { 2, "POWER_DISABLE" }, { 3, "OFF" },
            { 4, "SERVICE" }, { 5, "MECH_INIT" }, { 6, "STANDBY" }, { 7, "GET_READY" },
            { 8, "READY" }, { 9, "PRE_PRINT" }, { 10, "PRINT" }, { 11, "POST_PRINT" },
            { 12, "PAUSE" }, { 13, "RECOVERY" }, { 14, "GO_TO_OFF" }, { 15, "GO_TO_STANDBY" },
            { 16, "GO_TO_SERVICE" }, { 17, "SML_OFF" }, { 18, "DYNAMIC_READY" }
        };

        // Reverse mapping (name -> ID)
        public static readonly Dictionary<string, int> StateNameToId;

        static ChartStateConfig()
        {
            StateNameToId = StateNames.ToDictionary(x => x.Value, x => x.Key);
        }

        // State colors (semi-transparent for background display)
        public static readonly Dictionary<int, SKColor> StateColors = new Dictionary<int, SKColor>
        {
            { 0, SKColor.Parse("#D3D3D3") },   // UNDEFINED - Light Gray
            { 1, SKColor.Parse("#FFE135") },   // INIT - Yellow
            { 2, SKColor.Parse("#FF6B6B") },   // POWER_DISABLE - Red
            { 3, SKColor.Parse("#808080") },   // OFF - Gray
            { 4, SKColor.Parse("#8B4513") },   // SERVICE - Brown
            { 5, SKColor.Parse("#FFA500") },   // MECH_INIT - Orange
            { 6, SKColor.Parse("#FFFF00") },   // STANDBY - Bright Yellow
            { 7, SKColor.Parse("#FFA500") },   // GET_READY - Orange
            { 8, SKColor.Parse("#90EE90") },   // READY - Light Green
            { 9, SKColor.Parse("#26C6DA") },   // PRE_PRINT - Cyan
            { 10, SKColor.Parse("#228B22") },  // PRINT - Forest Green
            { 11, SKColor.Parse("#4169E1") },  // POST_PRINT - Royal Blue
            { 12, SKColor.Parse("#FFA726") },  // PAUSE - Orange
            { 13, SKColor.Parse("#EC407A") },  // RECOVERY - Pink
            { 14, SKColor.Parse("#A0522D") },  // GO_TO_OFF - Sienna
            { 15, SKColor.Parse("#DAA520") },  // GO_TO_STANDBY - Goldenrod
            { 16, SKColor.Parse("#CD853F") },  // GO_TO_SERVICE - Peru
            { 17, SKColor.Parse("#C62828") },  // SML_OFF - Dark Red
            { 18, SKColor.Parse("#32CD32") }   // DYNAMIC_READY - Lime Green
        };

        /// <summary>
        /// Get color for a state ID with transparency for background display
        /// </summary>
        public static SKColor GetColor(int stateId)
        {
            if (StateColors.TryGetValue(stateId, out SKColor color))
            {
                return color.WithAlpha(60); // Semi-transparent for background
            }
            return SKColors.Transparent;
        }

        /// <summary>
        /// Get solid color for a state ID (for timeline display)
        /// </summary>
        public static SKColor GetSolidColor(int stateId)
        {
            if (StateColors.TryGetValue(stateId, out SKColor color))
            {
                return color;
            }
            return SKColors.Gray;
        }

        /// <summary>
        /// Get the name for a state ID
        /// </summary>
        public static string GetName(int stateId)
        {
            return StateNames.ContainsKey(stateId) ? StateNames[stateId] : stateId.ToString();
        }

        /// <summary>
        /// Smart ID detection from raw value (supports numeric and string formats)
        /// </summary>
        public static int GetId(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return 0;

            // Try direct numeric conversion
            if (int.TryParse(rawValue, out int id)) return id;

            string clean = rawValue.Trim().ToUpper();

            // Try exact name match
            if (StateNameToId.TryGetValue(clean, out int mappedId)) return mappedId;

            // Try partial match (for cases where state name is embedded in a longer string)
            foreach (var kvp in StateNameToId)
            {
                if (clean.Contains(kvp.Key)) return kvp.Value;
            }

            return 0; // Not found - return UNDEFINED
        }
    }
}
