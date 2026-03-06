using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class PluginTesterWindow : Window
    {
        // ── Dynamic DataGrid columns ──────────────────────────────────

        private void ApplyResultColumns(ILogFilePlugin plugin)
        {
            ResultsGrid.Columns.Clear();
            ColumnHintLabel.Text = "";

            var cols = GetPluginColumns(plugin);
            if (cols == null || cols.Count == 0)
            {
                AddDefaultColumns();
                return;
            }

            var extraFields = new List<string>();
            foreach (var def in cols)
            {
                bool   isBuiltIn    = _builtInFields.Contains(def.Field);
                string bindingPath  = isBuiltIn ? def.Field : $"ExtraFields[{def.Field}]";

                var binding = new Binding(bindingPath);
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

                ResultsGrid.Columns.Add(col);
                if (!isBuiltIn) extraFields.Add(def.Field);
            }

            if (extraFields.Count > 0)
                ColumnHintLabel.Text = $"Custom fields in ExtraFields: {string.Join(", ", extraFields)}";
        }

        private void AddDefaultColumns()
        {
            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Date",
                Binding = new Binding("Date") { StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                Width   = 160
            });
            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Level",
                Binding = new Binding("Level"),
                Width   = 70
            });
            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Message",
                Binding = new Binding("Message"),
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        private static IReadOnlyList<PluginColumnDef>? GetPluginColumns(ILogFilePlugin plugin)
        {
            try   { return plugin?.GetColumns(); }
            catch (Exception ex) { AppLogger.Error("GetPluginColumns failed", ex); return null; }
        }

        // ── Plugin card builder ───────────────────────────────────────

        private UIElement BuildPluginCard(ILogFilePlugin plugin)
        {
            bool   isTemp  = _tempPlugins.Contains(plugin);
            string? dllPath = _pluginLoader.GetDllPath(plugin);
            if (dllPath == null) _tempDllPaths.TryGetValue(plugin, out dllPath);
            string dllName = dllPath != null ? Path.GetFileName(dllPath) : "(temp / unknown)";

            string exts = (plugin.SupportedExtensions != null && plugin.SupportedExtensions.Length > 0)
                ? string.Join("  ", plugin.SupportedExtensions)
                : "(any extension)";

            // Card border
            var card = new Border
            {
                Background      = TryFindResource("BgCard")     as Brush,
                BorderBrush     = TryFindResource("BorderColor") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(0, 0, 0, 6)
            };

            var inner = new StackPanel();

            // ── Header: icon + name + TEMP badge ─────────────────────
            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 3)
            };
            headerRow.Children.Add(new TextBlock
            {
                Text              = "📦",
                FontSize          = 13,
                Margin            = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerRow.Children.Add(new TextBlock
            {
                Text              = plugin.Name,
                FontSize          = 12,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = TryFindResource("TextPrimary") as Brush,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (isTemp)
            {
                headerRow.Children.Add(new Border
                {
                    Background    = new SolidColorBrush(Color.FromRgb(0x3A, 0x72, 0xC4)),
                    CornerRadius  = new CornerRadius(3),
                    Padding       = new Thickness(4, 1, 4, 1),
                    Margin        = new Thickness(6, 0, 0, 0),
                    Child         = new TextBlock
                    {
                        Text       = "TEMP",
                        FontSize   = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    }
                });
            }
            inner.Children.Add(headerRow);

            // ── Version + DLL filename ────────────────────────────────
            inner.Children.Add(new TextBlock
            {
                Text         = $"v{plugin.Version}  •  {dllName}",
                FontSize     = 10,
                Foreground   = TryFindResource("TextSecondary") as Brush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 0, 0, 1),
                ToolTip      = dllPath ?? "(unknown)"
            });

            // ── Supported extensions ──────────────────────────────────
            inner.Children.Add(new TextBlock
            {
                Text         = exts,
                FontSize     = 10,
                Foreground   = TryFindResource("TextSecondary") as Brush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 0, 0, 6)
            });

            // ── Action buttons ────────────────────────────────────────
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

            if (isTemp)
            {
                btnRow.Children.Add(MakeSmallBtn("Unload", plugin, UnloadPlugin, false));
            }

            if (dllPath != null && File.Exists(dllPath))
            {
                var deleteBtn = MakeSmallBtn("Delete DLL", plugin, DeleteDll, true);
                deleteBtn.Margin = new Thickness(isTemp ? 4 : 0, 0, 0, 0);
                btnRow.Children.Add(deleteBtn);
            }

            inner.Children.Add(btnRow);
            card.Child = inner;
            return card;
        }

        private Button MakeSmallBtn(string label, ILogFilePlugin plugin,
                                    Action<ILogFilePlugin> onClick, bool danger)
        {
            Brush? fg = danger
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36))
                : TryFindResource("TextSecondary") as Brush;

            var btn = new Button
            {
                Height  = 22,
                FontSize = 10,
                Style   = TryFindResource("ModernBtn") as Style,
                Content = new TextBlock { Text = label, Foreground = fg }
            };
            btn.Click += (s, e) => onClick(plugin);
            return btn;
        }

        // ── Refresh plugin list + combobox ────────────────────────────

        private void RefreshPluginList()
        {
            PluginListPanel.Children.Clear();
            PluginComboBox.Items.Clear();

            var all = AllPlugins().ToList();

            PluginCountBadge.Text = all.Count > 0
                ? $"({all.Count} loaded)"
                : "(no plugins)";

            if (all.Count == 0)
            {
                PluginListPanel.Children.Add(new TextBlock
                {
                    Text        = "No plugins loaded.\nCopy DLLs to the Plugins folder\nor use '＋ Load DLL...' to test.",
                    FontSize    = 11,
                    Foreground  = TryFindResource("TextSecondary") as Brush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin      = new Thickness(2, 4, 2, 0)
                });
            }
            else
            {
                foreach (var p in all)
                {
                    PluginListPanel.Children.Add(BuildPluginCard(p));
                    PluginComboBox.Items.Add(new PluginComboItem(p));
                }
            }

            if (PluginComboBox.Items.Count > 0)
                PluginComboBox.SelectedIndex = 0;
        }

        // ── Reset stats / results ─────────────────────────────────────

        private void ResetResults()
        {
            CanHandleLabel.Text       = "—";
            CanHandleLabel.Foreground = TryFindResource("TextSecondary") as Brush;
            EntriesLabel.Text         = "—";
            TimeLabel.Text            = "—";
            ColsLabel.Text            = "—";
            ColumnHintLabel.Text      = "";
            ResultsGrid.ItemsSource   = null;
            ResultsGrid.Columns.Clear();
            ErrorsBox.Text            = "(none)";
        }

        // ── Low-level helpers ─────────────────────────────────────────

        private IEnumerable<ILogFilePlugin> AllPlugins()
            => _pluginLoader.Plugins.Concat(_tempPlugins);

        private static string[] ReadSampleLines(string filePath, int count)
        {
            try
            {
                var lines = new List<string>();
                using (var sr = new StreamReader(filePath, Encoding.UTF8, true))
                {
                    string? line;
                    while (lines.Count < count && (line = sr.ReadLine()) != null)
                        lines.Add(line);
                }
                return lines.ToArray();
            }
            catch (Exception ex) { AppLogger.Error("ReadSampleLines failed", ex); return Array.Empty<string>(); }
        }

        private static bool SafeCanHandle(ILogFilePlugin plugin, string fileName, string[] sampleLines)
        {
            try   { return plugin.CanHandle(fileName, sampleLines); }
            catch (Exception ex) { AppLogger.Error("SafeCanHandle failed", ex); return false; }
        }

        // ── ComboBox item wrapper ─────────────────────────────────────

        private class PluginComboItem
        {
            public ILogFilePlugin Plugin { get; }
            public PluginComboItem(ILogFilePlugin p) { Plugin = p; }
            public override string ToString() => $"{Plugin.Name}  v{Plugin.Version}";
        }
    }
}
