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
        private static readonly System.Collections.Frozen.FrozenSet<string> _builtInFields = AppConstants.BuiltInFields;

        // ── Construction ──────────────────────────────────────────────
        public PluginTesterWindow()
        {
            InitializeComponent();
            _pluginLoader = new PluginLoader();
            RefreshPluginList();
        }

        // ── Title bar ─────────────────────────────────────────────────
        private void CloseBtn_Click(object? sender, RoutedEventArgs e) => Close();

        // ── Open Plugins Folder in Explorer ───────────────────────────
        private void OpenFolderBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PluginLoader.PluginsFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", PluginLoader.PluginsFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot open folder:\n{ex.Message}", "Plugin Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Reload all folder plugins ─────────────────────────────────
        private void ReloadBtn_Click(object? sender, RoutedEventArgs e)
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
        private void LoadDllBtn_Click(object? sender, RoutedEventArgs e)
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
                    types = rtle.Types?.Where(t => t != null).Cast<Type>().ToArray() ?? Array.Empty<Type>();
                }

                var pluginTypes = types
                    .Where(t => hostIfaceType.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

                int added = 0;
                foreach (var type in pluginTypes)
                {
                    try
                    {
                        var plugin = Activator.CreateInstance(type) as ILogFilePlugin;
                        if (plugin == null) continue;
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
            string? path = _pluginLoader.GetDllPath(plugin);
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
        private void BrowseFileBtn_Click(object? sender, RoutedEventArgs e)
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
                .SelectMany(p => p.SupportedExtensions ?? Array.Empty<string>())
                .Distinct()
                .ToList();

            if (exts.Count == 0)
                return "All files (*.*)|*.*";

            string pluginPart = string.Join(";", exts);
            return $"Supported Log Files ({pluginPart})|{pluginPart}|All files (*.*)|*.*";
        }

        // ── Run Test ──────────────────────────────────────────────────
        private async void RunTestBtn_Click(object? sender, RoutedEventArgs e)
        {
            string? filePath = FilePathBox.Text?.Trim();
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
                ILogFilePlugin? plugin = null;
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
                ApplyResultColumns(plugin!);

                // Parse in background
                var sw     = Stopwatch.StartNew();
                var errors = new StringBuilder();
                List<LogEntryDto>? entries = null;

                try
                {
                    var capturedPlugin = plugin!;
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
                var cols = GetPluginColumns(plugin!);
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

    }
}
