using IndiLogs_3._0.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    public class DefaultConfigurationService
    {
        private static readonly string DefaultsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs", "Configs", "_defaults.json");

        private static FilterNode _cachedFactoryPlcFilter;

        public DefaultConfiguration CurrentDefaults { get; private set; }

        public void Load()
        {
            try
            {
                if (File.Exists(DefaultsFilePath))
                {
                    var json = File.ReadAllText(DefaultsFilePath);
                    CurrentDefaults = JsonConvert.DeserializeObject<DefaultConfiguration>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEFAULT CONFIG] Failed to load defaults: {ex.Message}");
                CurrentDefaults = null;
            }
        }

        public void Save(DefaultConfiguration config)
        {
            try
            {
                var dir = Path.GetDirectoryName(DefaultsFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(DefaultsFilePath, json);
                CurrentDefaults = config;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEFAULT CONFIG] Failed to save defaults: {ex.Message}");
            }
        }

        public void Reset()
        {
            try
            {
                if (File.Exists(DefaultsFilePath))
                    File.Delete(DefaultsFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEFAULT CONFIG] Failed to delete defaults file: {ex.Message}");
            }
            CurrentDefaults = null;
        }

        /// <summary>
        /// Returns the factory-default PLC Filtered filter, equivalent to the original hardcoded IsDefaultLog:
        /// Level=Error OR Message starts with "PlcMngr:" OR Thread=Events OR Logger contains "Manager" OR Thread=Manager
        /// </summary>
        public static FilterNode GetFactoryPlcFilter()
        {
            if (_cachedFactoryPlcFilter != null)
                return _cachedFactoryPlcFilter;

            var root = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Level", Operator = "Equals", Value = "Error" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Begins With", Value = "PlcMngr:" },
                    new FilterNode { Type = NodeType.Condition, Field = "ThreadName", Operator = "Equals", Value = "Events" },
                    new FilterNode { Type = NodeType.Condition, Field = "Logger", Operator = "Contains", Value = "Manager" },
                    new FilterNode { Type = NodeType.Condition, Field = "ThreadName", Operator = "Equals", Value = "Manager" }
                }
            };

            _cachedFactoryPlcFilter = root;
            return root;
        }

        /// <summary>
        /// Returns factory-default coloring rules for PLC/Main logs.
        /// </summary>
        public static List<ColoringCondition> GetFactoryMainColoringRules()
        {
            return new List<ColoringCondition>
            {
                // Thread = Events -> Red text (IsErrorOrEvents handled separately, but as a coloring rule: red foreground)
                new ColoringCondition { Field = "ThreadName", Operator = "Equals", Value = "Events", Color = Color.FromRgb(255, 0, 0) },
                // Thread = Manager AND Message begins with "PlcMngr:" AND Message contains "->" -> Light Blue
                // Note: This compound rule is simplified to a single condition here.
                // The full logic requires Message starts with PlcMngr: AND contains -> AND Thread=Manager.
                // As a single ColoringCondition can't express AND, we keep this as the primary indicator.
                new ColoringCondition { Field = "ThreadName", Operator = "Equals", Value = "Manager", Color = Color.FromRgb(173, 216, 230) }
            };
        }

        /// <summary>
        /// Returns factory-default coloring rules for APP logs.
        /// </summary>
        public static List<ColoringCondition> GetFactoryAppColoringRules()
        {
            return new List<ColoringCondition>
            {
                // PipelineCancellationProvider errors -> Strong Orange
                new ColoringCondition { Field = "Logger", Operator = "Contains", Value = "Press.BL.Printing.Pipeline.PipelineCancellationProvider", Color = Color.FromRgb(255, 140, 0) },
                // PressStateManager + FallToPressStateAsync -> Orange
                new ColoringCondition { Field = "Logger", Operator = "Contains", Value = "PressStateManager", Color = Color.FromRgb(255, 165, 0) }
            };
        }
    }
}
