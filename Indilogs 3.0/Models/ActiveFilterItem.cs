using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public class ActiveFilterItem
    {
        public string Category { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        /// <summary>
        /// Unique key identifying this filter for removal. Format: "Category:SubKey"
        /// </summary>
        public string Key { get; set; }
        /// <summary>
        /// Optional color brush for COLORING filter items (shown as a swatch)
        /// </summary>
        public SolidColorBrush ColorBrush { get; set; }
    }
}
