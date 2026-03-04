using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using IndiLogs_3._0;
using IndiLogs_3._0.Converters;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.Views;
using IndiLogs.PluginAPI;
using Newtonsoft.Json;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls
{
    public partial class DifferentLogsTabControl : UserControl
    {
        /// <summary>Shared converter instance for safe ExtraFields dictionary access.</summary>
        private static readonly ExtraFieldConverter _extraFieldConverter = new ExtraFieldConverter();

        // Built-in field names that bind directly on LogEntryDto
        private static readonly HashSet<string> _builtInFields = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "Date", "Level", "Message", "ThreadName", "Logger",
            "ProcessName", "Source", "LineNumber"
        };

        /// <summary>Persistence key = plugin name or file extension → column settings.</summary>
        private string? _currentPluginKey;

        private static readonly string SettingsDir = AppPaths.Root;
        private static readonly string SettingsFile = AppPaths.DifferentLogsColumnSettings;

        public DifferentLogsTabControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            LogGrid.MouseDoubleClick += LogGrid_MouseDoubleClick;
        }

        // ── Double-click → open details window ──
        private void LogGrid_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            if (LogGrid.SelectedItem is LogEntry log)
            {
                var win = new LogDetailsWindow(log)
                {
                    Owner = Window.GetWindow(this)
                };
                win.Show();
            }
        }

        private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DifferentLogsViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is DifferentLogsViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                // Apply columns from initial state if already loaded
                RebuildColumns(newVm.Columns);
            }
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not DifferentLogsViewModel vm) return;

            if (e.PropertyName == nameof(DifferentLogsViewModel.Columns))
            {
                // Derive plugin key from status text (contains plugin name) or file extension
                _currentPluginKey = DerivePluginKey(vm);
                RebuildColumns(vm.Columns);
            }
            else if (e.PropertyName == nameof(DifferentLogsViewModel.StatusText))
            {
                // StatusText changes after loading completes ("30 entries — plugin: XYZ").
                // Re-derive plugin key and restore saved column settings if we now have a key.
                var newKey = DerivePluginKey(vm);
                if (!string.IsNullOrEmpty(newKey) && newKey != _currentPluginKey)
                {
                    _currentPluginKey = newKey;
                    RestoreColumnSettings();
                }
            }
            else if (e.PropertyName == nameof(DifferentLogsViewModel.FilteredEntries))
            {
                // FilteredEntries was replaced with a new ObservableCollection.
                // The XAML binding {Binding FilteredEntries} automatically updates the DataGrid's
                // ItemsSource, creating fresh DataGridRow containers with correct RowBackground.
            }
        }

        // ── Column rebuild ──────────────────────────────────────────

        private void RebuildColumns(IReadOnlyList<PluginColumnDef>? columns)
        {
            LogGrid.Columns.Clear();
            LogGrid.ColumnReordered -= LogGrid_ColumnReordered;

            if (columns == null || columns.Count == 0)
            {
                AddDefaultColumns();
                AttachColumnEvents();
                return;
            }

            foreach (var def in columns)
            {
                bool isBuiltIn = _builtInFields.Contains(def.Field);

                Binding binding;
                if (isBuiltIn)
                {
                    binding = new Binding(def.Field);
                }
                else
                {
                    // Use converter to safely access ExtraFields dictionary
                    // (avoids KeyNotFoundException when a LogEntry doesn't have this field)
                    binding = new Binding("ExtraFields")
                    {
                        Converter = _extraFieldConverter,
                        ConverterParameter = def.Field
                    };
                }

                if (!string.IsNullOrEmpty(def.StringFormat))
                    binding.StringFormat = def.StringFormat;

                var col = new DataGridTextColumn
                {
                    Header  = def.Header,
                    Binding = binding,
                    Width   = def.Width < 0
                                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                                : new DataGridLength(def.Width)
                };

                LogGrid.Columns.Add(col);
            }

            // Track all columns for context menu (always build, even without persistence key)
            _allDefinedColumns = LogGrid.Columns.Select(c => (GetColumnHeader(c), c)).ToList();
            BuildColumnHeaderContextMenu();

            // Restore saved column settings (visibility, order, widths) if key is available
            if (!string.IsNullOrEmpty(_currentPluginKey))
                RestoreColumnSettings();

            AttachColumnEvents();
        }

        private void AddDefaultColumns()
        {
            LogGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Date",
                Binding = new Binding("Date") { StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                Width   = new DataGridLength(160)
            });
            LogGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Level",
                Binding = new Binding("Level"),
                Width   = new DataGridLength(70)
            });
            LogGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Message",
                Binding = new Binding("Message"),
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        // ── Column events (reorder, resize) ─────────────────────────

        private void AttachColumnEvents()
        {
            LogGrid.ColumnReordered += LogGrid_ColumnReordered;

            // Attach resize handler to each column via Thumb.DragCompleted
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var col in LogGrid.Columns)
                {
                    // Find the Thumb in the column header for resize
                    // We use the simpler approach: save on any layout change after a delay
                }
            }));
        }

        private void LogGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
        {
            SaveColumnSettings();
        }

        // ── Right-click context menu for column visibility ──────────

        /// <summary>All columns defined by the plugin — used to show/hide columns.</summary>
        private List<(string Header, DataGridColumn Column)> _allDefinedColumns = new List<(string, DataGridColumn)>();

        /// <summary>Builds and shows the column visibility context menu on header right-click.</summary>
        private void BuildColumnHeaderContextMenu()
        {
            var menu = new ContextMenu();

            foreach (var (header, col) in _allDefinedColumns)
            {
                var item = new MenuItem
                {
                    Header = header,
                    IsCheckable = true,
                    IsChecked = col.Visibility == Visibility.Visible
                };

                var capturedCol = col;
                var capturedHeader = header;
                item.Click += (s, e) =>
                {
                    capturedCol.Visibility = ((MenuItem)s!).IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    SaveColumnSettings();
                };

                menu.Items.Add(item);
            }

            LogGrid.ColumnHeaderStyle = CreateColumnHeaderStyleWithMenu(menu);
        }

        private Style CreateColumnHeaderStyleWithMenu(ContextMenu menu)
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, FindResource("BgCard")));
            style.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, FindResource("TextPrimary")));
            style.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 4, 8, 4)));
            style.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, FindResource("BorderColor")));
            style.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(DataGridColumnHeader.ContextMenuProperty, menu));

            // Save widths when column header thumb drag completes (resize)
            var trigger = new EventSetter(Thumb.DragCompletedEvent, new DragCompletedEventHandler((s, e) => SaveColumnSettings()));
            style.Setters.Add(trigger);

            return style;
        }

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
