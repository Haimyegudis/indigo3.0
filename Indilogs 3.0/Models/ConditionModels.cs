#nullable disable
﻿using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    // This is the only place where this class should be defined!
    public class ColoringCondition
    {
        public string Field { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }

        // Uses WPF Color (System.Windows.Media)
        public Color Color { get; set; }

        /// <summary>
        /// When false, this rule is temporarily disabled (cleared) but not deleted.
        /// Reloading a config will re-enable it.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsEnabled { get; set; } = true;

        // Deep copy function
        public ColoringCondition Clone()
        {
            return new ColoringCondition
            {
                Field = this.Field,
                Operator = this.Operator,
                Value = this.Value,
                Color = this.Color
            };
        }
    }

    public class FilterCondition
    {
        public string Field { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
        public bool IsActive { get; set; } = true;
    }
}