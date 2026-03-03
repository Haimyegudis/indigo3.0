#nullable disable
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class PluginTesterWindow : Window
    {
        private readonly IPluginLoader _pluginLoader;
        private readonly List<ILogFilePlugin>              _tempPlugins  = new List<ILogFilePlugin>();
        private readonly Dictionary<ILogFilePlugin, string> _tempDllPaths = new Dictionary<ILogFilePlugin, string>();

        // Built-in field names that bind directly on LogEntryDto
        private static readonly HashSet<string> _builtInFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Date", "Level", "Message", "ThreadName", "Logger",
            "ProcessName", "Source", "LineNumber"
        };

        // ── Construction ──────────────────────────────────────────────
        public PluginTesterWindow()
        {
            InitializeComponent();
            _pluginLoader = new PluginLoader();
            RefreshPluginList();
        }

        // ── Title bar ─────────────────────────────────────────────────
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        // ── Open Plugins Folder in Explorer ───────────────────────────
        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PluginLoader.PluginsFolder);
                Process.Start("explorer.exe", PluginLoader.PluginsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot open folder:\n{ex.Message}", "Plugin Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Reload all folder plugins ─────────────────────────────────
        private void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _pluginLoader.Reload();
                RefreshPluginList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Reload failed:\n{ex.Message}", "Plugin Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Load a DLL manually (for testing without installing) ──────
        private void LoadDllBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Load Plugin DLL",
                Filter      = "DLL files (*.dll)|*.dll",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var hostIfaceType = typeof(ILogFilePlugin);
                var asm = Assembly.LoadFrom(dlg.FileName);

                Type[] types;
                try
                {
                    types = asm.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    types = rtle.Types?.Where(t => t != null).ToArray() ?? new Type[0];
                }

                var pluginTypes = types
                    .Where(t => hostIfaceType.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

                int added = 0;
                foreach (var type in pluginTypes)
                {
                    try
                    {
                        var plugin = (ILogFilePlugin)Activator.CreateInstance(type);
                        if (_tempPlugins.Any(p => p.Name == plugin.Name && p.Version == plugin.Version))
                            continue;
                        _tempPlugins.Add(plugin);
                        _tempDllPaths[plugin] = dlg.FileName;
                        added++;
                    }
                    catch (Exception ex2)
                    {
                        MessageBox.Show($"Could not instantiate {type.FullName}:\n{ex2.Message}",
                            "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                if (added == 0)
                {
                    MessageBox.Show("No ILogFilePlugin implementations found in the selected DLL.",
                        "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                RefreshPluginList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load DLL:\n{ex.GetType().Name}: {ex.Message}",
                    "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Unload (temp plugins only) ────────────────────────────────
        private void UnloadPlugin(ILogFilePlugin plugin)
        {
            if (_tempPlugins.Contains(plugin))
            {
                _tempPlugins.Remove(plugin);
                _tempDllPaths.Remove(plugin);
                RefreshPluginList();
            }
            else
            {
                MessageBox.Show(
                    "Folder plugins can only be removed by deleting the DLL.\n" +
                    "Use 'Delete DLL', or manually remove the file from the Plugins folder and click 'Reload All'.",
                    "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── Delete DLL from disk ──────────────────────────────────────
        private void DeleteDll(ILogFilePlugin plugin)
        {
            string path = _pluginLoader.GetDllPath(plugin);
            if (path == null) _tempDllPaths.TryGetValue(plugin, out path);

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("DLL path is not known or the file no longer exists.",
                    "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fileName = Path.GetFileName(path);
            var answer = MessageBox.Show(
                $"Permanently delete  '{fileName}'  from disk?\n\nThis action cannot be undone.",
                "Delete Plugin DLL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(path);
                _tempPlugins.Remove(plugin);
                _tempDllPaths.Remove(plugin);
                _pluginLoader.Reload();
                RefreshPluginList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete file:\n{ex.Message}",
                    "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Browse for test file ──────────────────────────────────────
        private void BrowseFileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select Test Log File",
                Filter = BuildBrowseFilter()
            };
            if (dlg.ShowDialog() == true)
                FilePathBox.Text = dlg.FileName;
        }

        private string BuildBrowseFilter()
        {
            var exts = AllPlugins()
                .SelectMany(p => p.SupportedExtensions ?? new string[0])
                .Distinct()
                .ToList();

            if (exts.Count == 0)
                return "All files (*.*)|*.*";

            string pluginPart = string.Join(";", exts);
            return $"Supported Log Files ({pluginPart})|{pluginPart}|All files (*.*)|*.*";
        }

        // ── Run Test ──────────────────────────────────────────────────
        private async void RunTestBtn_Click(object sender, RoutedEventArgs e)
        {
            string filePath = FilePathBox.Text?.Trim();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid test file.", "Plugin Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RunTestBtn.IsEnabled = false;
            ResetResults();

            try
            {
                string   fileName    = Path.GetFileName(filePath);
                string[] sampleLines = ReadSampleLines(filePath, 20);

                // Resolve which plugin to use
                ILogFilePlugin plugin = null;
                if (AutoDetectRadio.IsChecked == true)
                {
                    plugin = AllPlugins().FirstOrDefault(p => SafeCanHandle(p, fileName, sampleLines));
                }
                else
                {
                    plugin = (PluginComboBox.SelectedItem as PluginComboItem)?.Plugin;
                    if (plugin == null)
                    {
                        MessageBox.Show("Please select a plugin from the list.", "Plugin Manager",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // CanHandle
                bool canHandle = plugin != null && SafeCanHandle(plugin, fileName, sampleLines);
                CanHandleLabel.Text = canHandle
                    ? "✅ true"
                    : (plugin == null ? "✗ no plugin" : "✗ false");
                CanHandleLabel.Foreground = canHandle
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

                if (!canHandle)
                {
                    ErrorsBox.Text = plugin == null
                        ? "No plugin accepted this file. Load or reload plugins."
                        : $"Plugin '{plugin.Name}' returned false from CanHandle.";
                    return;
                }

                // Apply column layout from plugin (before binding data)
                ApplyResultColumns(plugin);

                // Parse in background
                var sw     = Stopwatch.StartNew();
                var errors = new StringBuilder();
                List<LogEntryDto> entries = null;

                try
                {
                    var capturedPlugin = plugin;
                    entries = await Task.Run(() =>
                    {
                        var list    = new List<LogEntryDto>();
                        var context = new ParseContext
                        {
                            FileName     = fileName,
                            FilePath     = filePath,
                            IsInsideZip  = false,
                            ZipEntryPath = null
                        };

                        using (var stream = File.OpenRead(filePath))
                        {
                            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            foreach (var entry in capturedPlugin.Parse(stream, context, null, cts.Token))
                            {
                                list.Add(entry);
                                if (list.Count >= 500) break; // cap at 500 rows
                            }
                        }
                        return list;
                    });
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"Parse error: {ex.Message}");
                }

                sw.Stop();

                int count = entries?.Count ?? 0;
                EntriesLabel.Text = count.ToString();
                TimeLabel.Text    = $"{sw.ElapsedMilliseconds} ms";

                // Column count stat
                var cols = GetPluginColumns(plugin);
                ColsLabel.Text = cols?.Count.ToString() ?? "—";

                if (entries != null && entries.Count > 0)
                    ResultsGrid.ItemsSource = entries;

                ErrorsBox.Text = errors.Length > 0 ? errors.ToString().Trim() : "(none)";
            }
            catch (Exception ex)
            {
                ErrorsBox.Text = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                RunTestBtn.IsEnabled = true;
            }
        }

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

        private static IReadOnlyList<PluginColumnDef> GetPluginColumns(ILogFilePlugin plugin)
        {
            try   { return plugin?.GetColumns(); }
            catch (Exception ex) { AppLogger.Error("GetPluginColumns failed", ex); return null; }
        }

        // ── Plugin card builder ───────────────────────────────────────

        private UIElement BuildPluginCard(ILogFilePlugin plugin)
        {
            bool   isTemp  = _tempPlugins.Contains(plugin);
            string dllPath = _pluginLoader.GetDllPath(plugin);
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
            Brush fg = danger
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
                    string line;
                    while (lines.Count < count && (line = sr.ReadLine()) != null)
                        lines.Add(line);
                }
                return lines.ToArray();
            }
            catch (Exception ex) { AppLogger.Error("ReadSampleLines failed", ex); return new string[0]; }
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
