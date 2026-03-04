using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class CaseManagementViewModel
    {
        // ── Saved Configurations: CRUD, apply, defaults ──

        private void SaveConfig(object obj)
        {
            var existingNames = SavedConfigs.Select(c => c.Name).ToList();
            var dlg = _viewFactory.Create<Views.SaveConfigWindow>(existingNames);
            if (dlg.ShowDialog() == true)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs3.0", "Configs");
                Directory.CreateDirectory(dir);
                var cfg = new SavedConfiguration
                {
                    Name = dlg.ConfigName,
                    CreatedDate = DateTime.Now,
                    FilePath = Path.Combine(dir, dlg.ConfigName + ".json"),
                    MainColoringRules = MainColoringRules ?? new List<ColoringCondition>(),
                    MainFilterRoot = _filterVM.MainFilterRoot,
                    AppColoringRules = AppColoringRules ?? new List<ColoringCondition>(),
                    AppFilterRoot = _filterVM.AppFilterRoot
                };
                File.WriteAllText(cfg.FilePath, JsonConvert.SerializeObject(cfg));
                SavedConfigs.Add(cfg);
                _sessionVM.StatusMessage = $"Configuration '{cfg.Name}' saved";
            }
        }

        private void LoadConfig(object obj)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Config files|*.json;*.txt|JSON config|*.json|Text filter|*.txt",
                Title  = "Load Configuration or Text Filter"
            };
            if (dlg.ShowDialog() != true) return;

            var configSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                SavedConfiguration c;

                if (dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    // ── Convert DevExpress text filter → FilterNode JSON ──────────────
                    string filterText = File.ReadAllText(dlg.FileName).Trim();
                    var    filterRoot = TextFilterParser.Parse(filterText);
                    string baseName  = Path.GetFileNameWithoutExtension(dlg.FileName);

                    string dir      = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs3.0", "Configs");
                    Directory.CreateDirectory(dir);
                    string jsonPath = Path.Combine(dir, baseName + ".json");

                    c = new SavedConfiguration
                    {
                        Name              = baseName,
                        CreatedDate       = DateTime.Now,
                        FilePath          = jsonPath,
                        MainColoringRules = new List<ColoringCondition>(),
                        MainFilterRoot    = filterRoot,
                        AppColoringRules  = new List<ColoringCondition>(),
                    };
                    File.WriteAllText(jsonPath, JsonConvert.SerializeObject(c, Formatting.Indented));
                    _sessionVM.StatusMessage = $"Text filter converted → '{baseName}.json'";
                }
                else
                {
                    // ── Normal JSON load ──────────────────────────────────────────────
                    c          = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(dlg.FileName), new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });
                    c.FilePath = dlg.FileName;
                    _sessionVM.StatusMessage = $"Configuration '{c.Name}' loaded";
                }

                // Replace existing entry with the same name (avoid duplicates)
                var existing = SavedConfigs.FirstOrDefault(s =>
                    string.Equals(s.Name, c.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) SavedConfigs.Remove(existing);

                SavedConfigs.Add(c);
                AppLogger.Info($"[Config] Configuration loaded from {Path.GetFileName(dlg.FileName)} — {configSw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error loading configuration: {ex.Message}", "Error");
            }
        }

        private void DeleteConfig(object obj)
        {
            var configToDelete = SelectedConfig;
            if (configToDelete != null && _dialogService.ShowConfirm($"Delete '{configToDelete.Name}'?", "Confirm") == DialogResult.Yes)
            {
                if (File.Exists(configToDelete.FilePath)) File.Delete(configToDelete.FilePath);
                SavedConfigs.Remove(configToDelete);
                _sessionVM.StatusMessage = $"Configuration '{configToDelete.Name}' deleted";
            }
        }

        /// <summary>
        /// Applies a saved configuration (filters and coloring rules) to the current session.
        /// </summary>
        public async Task ApplyConfiguration(SavedConfiguration c)
        {
            var configSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
            if (c == null) return;

            _sessionVM.IsBusy = true;
            _sessionVM.StatusMessage = $"Loading config: {c.Name} (Overriding current state)...";

            await _dispatcher.InvokeAsync(() =>
            {
                _filterVM.SearchText = "";
                _filterVM.IsSearchPanelVisible = false;
                _filterVM.NegativeFilters.Clear();
                _filterVM.ActiveThreadFilters.Clear();
                _filterVM.IsTimeFocusActive = false;
                _filterVM.IsAppTimeFocusActive = false;
                _filterVM.ResetTreeFilters();
                _filterVM.LastFilteredCache = null;
                _filterVM.LastFilteredAppCache = null;
                _filterVM.IsMainFilterActive = false;
                _filterVM.IsAppFilterActive = false;
                _filterVM.IsMainFilterOutActive = false;
                _filterVM.IsAppFilterOutActive = false;
                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));
            });

            // Deep clone coloring rules so clearing doesn't affect the saved config
            MainColoringRules = c.MainColoringRules?.Select(r => r.Clone()).ToList() ?? new List<ColoringCondition>();
            if (_sessionVM.AllLogsCache != null && MainColoringRules.Any())
            {
                await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllLogsCache, false).ConfigureAwait(false);
                await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllLogsCache, MainColoringRules).ConfigureAwait(false);
            }

            // Deep clone coloring rules so clearing doesn't affect the saved config
            AppColoringRules = c.AppColoringRules?.Select(r => r.Clone()).ToList() ?? new List<ColoringCondition>();
            if (_sessionVM.AllAppLogsCache != null && AppColoringRules.Any())
            {
                await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllAppLogsCache, true).ConfigureAwait(false);
                await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllAppLogsCache, AppColoringRules).ConfigureAwait(false);
            }

            // Deep clone filter tree so clearing doesn't affect the saved config
            _filterVM.MainFilterRoot = c.MainFilterRoot?.DeepClone();
            if (_filterVM.MainFilterRoot != null && _sessionVM.AllLogsCache != null)
            {
                var res = await Task.Run(() => _sessionVM.AllLogsCache.Where(l => _filterVM.EvaluateFilterNode(l, _filterVM.MainFilterRoot)).ToList());
                _filterVM.LastFilteredCache = res;
            }

            // Deep clone filter tree so clearing doesn't affect the saved config
            _filterVM.AppFilterRoot = c.AppFilterRoot?.DeepClone();

            _dispatcher.Post(() =>
            {
                if (_filterVM.AppFilterRoot != null && _filterVM.AppFilterRoot.Children.Count > 0)
                    _filterVM.IsAppFilterActive = true;

                if (_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children.Count > 0)
                    _filterVM.IsMainFilterActive = true;

                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();

                // Colors already applied by ColoringService - no manual refresh needed
                // WPF DataGrid virtualization will query RowBackground when rendering visible rows

                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));
            });

            _sessionVM.IsBusy = false;
            _sessionVM.StatusMessage = "Configuration loaded successfully.";
            AppLogger.Info($"[Config] Configuration '{c.Name}' applied (filters, coloring) — {configSw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) { AppLogger.Error("ApplyConfiguration failed", ex); }
        }

        // The two built-in default config file stems (one for S4-5, one for S6)
        // Both display as "PLC_FILTERED" in the UI; distinguished by file name on disk.
        private static readonly string ConfigFileS45 = "PLC_FILTERED_S45";
        private static readonly string ConfigFileS6  = "PLC_FILTERED_S6";

        /// <summary>
        /// Ensures built-in default config files exist on disk (without loading them into the list).
        /// Called once at startup.
        /// </summary>
        public void EnsureDefaultConfigsOnDisk(string configDir)
        {
            EnsureDefaultConfigs(configDir);
        }

        /// <summary>
        /// Loads saved configurations from disk.
        /// Only the matching default config is shown:
        ///   true  (S4-5) → show PLC_FILTERED_S45, hide PLC_FILTERED_S6
        ///   false (S6)   → show PLC_FILTERED_S6, hide PLC_FILTERED_S45
        /// User-created configs are always shown.
        /// </summary>
        public void LoadSavedConfigs(bool hasBinaryAppLogs)
        {
            var configSw = System.Diagnostics.Stopwatch.StartNew();
            SavedConfigs.Clear();
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs3.0", "Configs");

            // Hide the non-matching built-in default (by file name stem)
            string hideFileStem = hasBinaryAppLogs ? ConfigFileS6 : ConfigFileS45;

            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path, "*.json"))
                {
                    // Skip the defaults configuration file (managed by DefaultConfigurationService)
                    if (Path.GetFileName(f).StartsWith("_")) continue;

                    try
                    {
                        var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(f), new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });
                        c.FilePath = f;

                        // Hide the non-matching built-in default by file stem
                        string fileStem = Path.GetFileNameWithoutExtension(f);
                        if (string.Equals(fileStem, hideFileStem, StringComparison.OrdinalIgnoreCase))
                            continue;

                        SavedConfigs.Add(c);
                    }
                    catch
                    {
                        // Ignore corrupted config files
                    }
                }
            }
            AppLogger.Info($"[Config] {SavedConfigs.Count} saved configs loaded from disk — {configSw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Creates the built-in default saved configurations if they don't exist:
        /// - PLC_FILTERED_S45: default for S4-5 (binary APP) sessions
        /// - PLC_FILTERED_S6: default for S6 (non-binary APP) sessions
        /// Both display as "PLC_FILTERED" in the UI.
        /// Also removes legacy config files from previous versions.
        /// </summary>
        private void EnsureDefaultConfigs(string configDir)
        {
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            // Remove legacy config files from previous versions
            try
            {
                string legacyBinary = Path.Combine(configDir, "APP_BINARY.json");
                if (File.Exists(legacyBinary)) File.Delete(legacyBinary);

                string legacyNotBinary = Path.Combine(configDir, "app_not_binary.json");
                if (File.Exists(legacyNotBinary)) File.Delete(legacyNotBinary);
            }
            catch (Exception ex) { AppLogger.Error("Legacy config cleanup failed", ex); }

            // S4-5: PLC_FILTERED config
            string s45Path = Path.Combine(configDir, ConfigFileS45 + ".json");
            if (!File.Exists(s45Path))
            {
                var config = new SavedConfiguration
                {
                    Name = "PLC_FILTERED",
                    FilePath = s45Path,
                    CreatedDate = DateTime.Now,
                    MainColoringRules = new List<ColoringCondition>(),
                    AppColoringRules = new List<ColoringCondition>(),
                    MainFilterRoot = new FilterNode
                    {
                        Type = NodeType.Group,
                        LogicalOperator = "OR",
                        Field = "Message",
                        Operator = "Contains",
                        Value = "",
                        Children = new ObservableCollection<FilterNode>
                        {
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "Level", Operator = "Equals", Value = "error" },
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "Message", Operator = "Contains", Value = "=== state" }
                        }
                    },
                    AppFilterRoot = null,
                    PlcFilteredRoot = null
                };
                File.WriteAllText(s45Path, JsonConvert.SerializeObject(config, Formatting.Indented));
            }

            // S6: PLC_FILTERED config
            string s6Path = Path.Combine(configDir, ConfigFileS6 + ".json");
            if (!File.Exists(s6Path))
            {
                var config = new SavedConfiguration
                {
                    Name = "PLC_FILTERED",
                    FilePath = s6Path,
                    CreatedDate = DateTime.Now,
                    MainColoringRules = new List<ColoringCondition>(),
                    AppColoringRules = new List<ColoringCondition>(),
                    MainFilterRoot = new FilterNode
                    {
                        Type = NodeType.Group,
                        LogicalOperator = "OR",
                        Field = "Message",
                        Operator = "Contains",
                        Value = "",
                        Children = new ObservableCollection<FilterNode>
                        {
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "Message", Operator = "Begins With", Value = "plcmngr:" },
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "ThreadName", Operator = "Begins With", Value = "Manager" },
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "Level", Operator = "Equals", Value = "error" },
                            new FilterNode { Type = NodeType.Condition, LogicalOperator = "AND", Field = "ThreadName", Operator = "Equals", Value = "Events" }
                        }
                    },
                    AppFilterRoot = null,
                    PlcFilteredRoot = null
                };
                File.WriteAllText(s6Path, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }
    }
}
