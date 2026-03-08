using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Services;
using System.Windows.Controls;
using IndiLogs_3._0.ViewModels;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Controls
{
    public partial class DifferentLogsTabControl
    {
        // ── Column persistence ──────────────────────────────────────

        private string? DerivePluginKey(DifferentLogsViewModel? vm)
        {
            // Use the plugin name from StatusText (e.g., "30 entries  —  plugin: JSON Test Log Plugin")
            if (!string.IsNullOrEmpty(vm?.StatusText))
            {
                int idx = vm.StatusText.IndexOf("plugin:", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string pluginName = vm.StatusText.Substring(idx + 7).Trim();
                    if (!string.IsNullOrEmpty(pluginName))
                        return pluginName;
                }
            }

            // Fallback: use file extension
            if (!string.IsNullOrEmpty(vm?.CurrentFilePath))
                return Path.GetExtension(vm.CurrentFilePath).ToLowerInvariant();

            return null;
        }

        private void SaveColumnSettings()
        {
            if (string.IsNullOrEmpty(_currentPluginKey)) return;

            try
            {
                var allSettings = LoadAllSettings();

                var settings = new DiffColumnSettings
                {
                    ColumnWidths = new Dictionary<string, double>(),
                    ColumnOrders = new Dictionary<string, int>(),
                    ColumnVisibility = new Dictionary<string, bool>()
                };

                foreach (var col in LogGrid.Columns)
                {
                    string header = GetColumnHeader(col);
                    if (string.IsNullOrEmpty(header)) continue;

                    settings.ColumnWidths[header] = col.ActualWidth;
                    settings.ColumnOrders[header] = col.DisplayIndex;
                    settings.ColumnVisibility[header] = col.Visibility == Visibility.Visible;
                }

                allSettings[_currentPluginKey] = settings;

                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(allSettings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Saving column settings failed", ex);
            }
        }

        private void RestoreColumnSettings()
        {
            if (string.IsNullOrEmpty(_currentPluginKey)) return;

            try
            {
                var allSettings = LoadAllSettings();
                if (!allSettings.TryGetValue(_currentPluginKey, out var settings))
                    return; // No saved settings for this plugin

                // Apply visibility
                if (settings.ColumnVisibility != null)
                {
                    foreach (var col in LogGrid.Columns)
                    {
                        string header = GetColumnHeader(col);
                        if (settings.ColumnVisibility.TryGetValue(header, out bool visible))
                            col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                // Apply widths
                if (settings.ColumnWidths != null)
                {
                    foreach (var col in LogGrid.Columns)
                    {
                        string header = GetColumnHeader(col);
                        if (settings.ColumnWidths.TryGetValue(header, out double width) && width > 0)
                            col.Width = new DataGridLength(width);
                    }
                }

                // Apply display order
                if (settings.ColumnOrders != null)
                {
                    foreach (var col in LogGrid.Columns)
                    {
                        string header = GetColumnHeader(col);
                        if (settings.ColumnOrders.TryGetValue(header, out int order))
                        {
                            if (order >= 0 && order < LogGrid.Columns.Count)
                                col.DisplayIndex = order;
                        }
                    }
                }

                // Rebuild context menu to reflect restored visibility states
                _allDefinedColumns = LogGrid.Columns.Select(c => (GetColumnHeader(c), c)).ToList();
                BuildColumnHeaderContextMenu();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Restoring column settings failed", ex);
            }
        }

        private Dictionary<string, DiffColumnSettings> LoadAllSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    return JsonConvert.DeserializeObject<Dictionary<string, DiffColumnSettings>>(json, AppConstants.SafeJsonSettings)
                           ?? new Dictionary<string, DiffColumnSettings>();
                }
            }
            catch (Exception ex) { AppLogger.Error("Loading all column settings failed", ex); }
            return new Dictionary<string, DiffColumnSettings>();
        }

        private static string GetColumnHeader(DataGridColumn col)
        {
            if (col.Header is string s) return s;
            return col.Header?.ToString() ?? "";
        }

        // ── Settings model ──────────────────────────────────────────
        private class DiffColumnSettings
        {
            public Dictionary<string, double>? ColumnWidths { get; set; }
            public Dictionary<string, int>? ColumnOrders { get; set; }
            public Dictionary<string, bool>? ColumnVisibility { get; set; }
        }
    }
}
