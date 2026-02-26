using IndiLogs_3._0;
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
            "IndiLogs3.0", "Configs", "_defaults.json");

        private static FilterNode _cachedFactoryPlcFilter;

        public DefaultConfiguration CurrentDefaults { get; private set; }

        public void Load()
        {
            try
            {
                if (File.Exists(DefaultsFilePath))
                {
                    var json = File.ReadAllText(DefaultsFilePath);
                    CurrentDefaults = JsonConvert.DeserializeObject<DefaultConfiguration>(json, new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load default configuration", ex);
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
                System.Diagnostics.Debug.WriteLine($"[DefaultConfigurationService] Saving defaults failed: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[DefaultConfigurationService] Resetting defaults file failed: {ex.Message}");
            }
            CurrentDefaults = null;
        }

        /// <summary>
        /// Returns the factory-default PLC Filtered filter for standard (non-binary) sessions:
        /// Message starts with "PlcMngr:" OR ThreadName starts with "Manager" OR Level=error OR ThreadName=Events
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
                    new FilterNode { Type = NodeType.Condition, Field = "Message",    Operator = "Begins With", Value = "PlcMngr:" },
                    new FilterNode { Type = NodeType.Condition, Field = "ThreadName", Operator = "Begins With", Value = "Manager" },
                    new FilterNode { Type = NodeType.Condition, Field = "Level",      Operator = "Equals",      Value = "error" },
                    new FilterNode { Type = NodeType.Condition, Field = "ThreadName", Operator = "Equals",      Value = "Events" }
                }
            };

            _cachedFactoryPlcFilter = root;
            return root;
        }

        /// <summary>
        /// Returns factory-default coloring rules for PLC/Main logs.
        /// Empty — state transitions are shown only in the heat map strip, not as row backgrounds.
        /// </summary>
        public static List<ColoringCondition> GetFactoryMainColoringRules()
        {
            return new List<ColoringCondition>();
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

        /// <summary>
        /// Returns the factory-default PLC filter for binary APP sessions:
        /// Level=error OR Message Contains "=== state"
        /// </summary>
        public static FilterNode GetFactoryBinaryAppPlcFilter()
        {
            return new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR",
                Children = new ObservableCollection<FilterNode>
                {
                    new FilterNode { Type = NodeType.Condition, Field = "Level",   Operator = "Equals",   Value = "error" },
                    new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains", Value = "=== state" }
                }
            };
        }

        /// <summary>
        /// Returns factory-default coloring rules for binary APP sessions.
        /// Empty — state transitions are shown only in the heat map strip, not as row backgrounds.
        /// </summary>
        public static List<ColoringCondition> GetFactoryBinaryAppColoringRules()
        {
            return new List<ColoringCondition>();
        }
    }
}
