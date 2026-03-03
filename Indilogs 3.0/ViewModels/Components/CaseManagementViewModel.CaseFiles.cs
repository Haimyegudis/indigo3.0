using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
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
        // ── Case Files: save/load .indi-case, apply settings, coloring windows ──

        /// <summary>
        /// Indicates whether a case file is currently being loaded (prevents filter resets during load).
        /// </summary>
        public bool IsLoadingCase => _isLoadingCase;

        private void SaveCase(object parameter)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "IndiLogs Case File (*.indi-case)|*.indi-case",
                    DefaultExt = ".indi-case",
                    FileName = $"Investigation_{DateTime.Now:yyyyMMdd_HHmmss}.indi-case"
                };

                if (dialog.ShowDialog() == true)
                {
                    var caseFile = new CaseFile
                    {
                        Meta = new CaseMetadata
                        {
                            Author = Environment.UserName,
                            CreatedAt = DateTime.Now,
                            Description = "Investigation case file"
                        },
                        ViewState = new CaseViewState
                        {
                            ActiveFilters = _filterVM.MainFilterRoot?.DeepClone(),
                            QuickSearchText = _filterVM.SearchText,
                            SelectedTab = _parent.SelectedTabIndex == 0 ? "MAIN" : "APP",
                            ActiveThreadFilters = _filterVM.ActiveThreadFilters.ToList(),
                            NegativeFilters = _filterVM.NegativeFilters.ToList()
                        },
                        MainColoringRules = MainColoringRules ?? new List<ColoringCondition>(),
                        AppColoringRules = AppColoringRules ?? new List<ColoringCondition>(),
                        Annotations = LogAnnotations.Values.ToList()
                    };

                    // Add resources (log files)
                    if (_sessionVM.SelectedSession != null && !string.IsNullOrEmpty(_sessionVM.SelectedSession.FilePath))
                    {
                        var fileInfo = new FileInfo(_sessionVM.SelectedSession.FilePath);
                        if (fileInfo.Exists)
                        {
                            caseFile.Resources.Add(new CaseResource
                            {
                                FileName = fileInfo.Name,
                                Size = fileInfo.Length,
                                LastModified = fileInfo.LastWriteTime
                            });
                        }
                    }

                    var json = JsonConvert.SerializeObject(caseFile, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);

                    _currentCaseFilePath = dialog.FileName;
                    _currentCase = caseFile;

                    _sessionVM.StatusMessage = $"Case saved: {Path.GetFileName(dialog.FileName)}";
                    _dialogService.ShowInfo($"Case file saved successfully!\n\n" +
                                  $"Filters: {(_filterVM.MainFilterRoot != null ? "✓" : "✗")}\n" +
                                  $"Coloring Rules: {MainColoringRules?.Count ?? 0} (Main) + {AppColoringRules?.Count ?? 0} (App)\n" +
                                  $"Annotations: {caseFile.Annotations.Count}\n" +
                                  $"Search: {(string.IsNullOrEmpty(_filterVM.SearchText) ? "✗" : "✓")}",
                                  "Case Saved");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error saving case: {ex.Message}", "Error");
            }
        }

        /// <summary>
        /// Loads an investigation case from a .indi-case file
        /// </summary>
        private void LoadCase(object parameter)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "IndiLogs Case File (*.indi-case)|*.indi-case",
                    DefaultExt = ".indi-case"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var caseFile = JsonConvert.DeserializeObject<CaseFile>(json, new JsonSerializerSettings { MaxDepth = AppConstants.JsonMaxDepth });

                    if (caseFile == null)
                    {
                        _dialogService.ShowError("Invalid case file format.", "Error");
                        return;
                    }

                    // Check if log files exist and collect paths
                    var logFilesToLoad = new List<string>();
                    bool filesFound = true;
                    var caseDir = Path.GetDirectoryName(dialog.FileName);

                    foreach (var resource in caseFile.Resources)
                    {
                        var logPath = Path.Combine(caseDir, resource.FileName);

                        if (!File.Exists(logPath))
                        {
                            var result = _dialogService.ShowConfirm(
                                $"Log file not found: {resource.FileName}\n\n" +
                                $"Expected location: {caseDir}\n\n" +
                                $"Would you like to locate it manually?",
                                "File Not Found");

                            if (result == MessageBoxResult.Yes)
                            {
                                var fileDialog = new OpenFileDialog
                                {
                                    Filter = "Log Files (*.file;*.log;*.zip)|*.file;*.log;*.zip|All Files (*.*)|*.*",
                                    FileName = resource.FileName,
                                    Title = $"Locate: {resource.FileName}"
                                };

                                if (fileDialog.ShowDialog() == true)
                                {
                                    logPath = fileDialog.FileName;
                                }
                                else
                                {
                                    filesFound = false;
                                    break;
                                }
                            }
                            else
                            {
                                filesFound = false;
                                break;
                            }
                        }

                        logFilesToLoad.Add(logPath);
                    }

                    if (filesFound && logFilesToLoad.Count > 0)
                    {
                        _sessionVM.StatusMessage = "Loading case files...";
                        _isLoadingCase = true;

                        // Clear all existing sessions to start fresh
                        _sessionVM.LoadedSessions.Clear();
                        _sessionVM.SelectedSession = null;

                        // Load the logs with callback
                        _parent.ProcessFiles(logFilesToLoad.ToArray(), session =>
                        {
                            // Callback called after logs are loaded successfully
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _sessionVM.StatusMessage = "Applying case settings...";
                                _ = ApplyCaseSettings(caseFile);
                                _isLoadingCase = false;
                                _sessionVM.StatusMessage = "Case loaded successfully!";
                            });
                        });
                    }
                    else
                    {
                        _dialogService.ShowWarning(
                            "Case cannot be loaded without the log files.\n\n" +
                            "Please ensure the log files are in the same folder as the .indi-case file,\n" +
                            "or select them manually when prompted.",
                            "Missing Log Files");
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error loading case: {ex.Message}", "Error");
                _isLoadingCase = false;
            }
        }

        /// <summary>
        /// Applies case settings after logs are loaded
        /// </summary>
        private async Task ApplyCaseSettings(CaseFile caseFile)
        {
            try
            {
            if (caseFile == null) return;

            _currentCaseFilePath = null;
            _currentCase = caseFile;

            _sessionVM.IsBusy = true;
            _sessionVM.StatusMessage = "Applying case settings...";

            // 1. Restore coloring rules first
            MainColoringRules = caseFile.MainColoringRules ?? new List<ColoringCondition>();
            AppColoringRules = caseFile.AppColoringRules ?? new List<ColoringCondition>();

            // Apply colors to all logs (OPTIMIZATION: Only if custom rules exist)
            await Task.Run(async () =>
            {
                if (_sessionVM.AllLogsCache != null && MainColoringRules.Any())
                {
                    await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllLogsCache, false);
                    await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllLogsCache, MainColoringRules);
                }

                if (_sessionVM.AllAppLogsCache != null && AppColoringRules.Any())
                {
                    await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllAppLogsCache, true);
                    await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllAppLogsCache, AppColoringRules);
                }
            });

            // 2. Restore annotations (re-bind to actual log entries)
            LogAnnotations.Clear();
            int annotationsRestored = 0;

            if (caseFile.Annotations != null && _sessionVM.AllLogsCache != null)
            {
                var allLogs = _sessionVM.AllLogsCache.ToList();
                foreach (var annotation in caseFile.Annotations)
                {
                    var matchingLog = FindLogByTarget(annotation.TargetLog, allLogs);
                    if (matchingLog != null)
                    {
                        LogAnnotations[matchingLog] = annotation;
                        matchingLog.HasAnnotation = true;
                        matchingLog.AnnotationContent = annotation.Content;
                        matchingLog.IsAnnotationExpanded = false; // Start collapsed

                        // Restore custom color if it exists
                        if (!string.IsNullOrEmpty(annotation.Color) && annotation.Color != "#FFFF00")
                        {
                            try
                            {
                                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(annotation.Color);
                                matchingLog.CustomColor = color;
                            }
                            catch (Exception ex) { AppLogger.Error("Color restore failed", ex); }
                        }

                        annotationsRestored++;
                    }
                }
            }

            // 3. Restore view state (filters, search, etc.)
            if (caseFile.ViewState != null)
            {
                _filterVM.MainFilterRoot = caseFile.ViewState.ActiveFilters;
                _filterVM.SearchText = caseFile.ViewState.QuickSearchText ?? "";

                // Restore active thread filters
                _filterVM.ActiveThreadFilters.Clear();
                if (caseFile.ViewState.ActiveThreadFilters != null)
                {
                    foreach (var filter in caseFile.ViewState.ActiveThreadFilters)
                        _filterVM.ActiveThreadFilters.Add(filter);
                }

                // Restore negative filters
                _filterVM.NegativeFilters.Clear();
                if (caseFile.ViewState.NegativeFilters != null)
                {
                    foreach (var filter in caseFile.ViewState.NegativeFilters)
                        _filterVM.NegativeFilters.Add(filter);
                }

                // Set filter active flags
                if (_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children.Count > 0)
                    _filterVM.IsMainFilterActive = true;

                if (_filterVM.NegativeFilters.Any())
                    _filterVM.IsMainFilterOutActive = true;

                // Apply the filters
                if (_filterVM.IsMainFilterActive && _filterVM.MainFilterRoot != null)
                {
                    await Task.Run(() =>
                    {
                        var res = _sessionVM.AllLogsCache.Where(l => _filterVM.EvaluateFilterNode(l, _filterVM.MainFilterRoot)).ToList();
                        _filterVM.LastFilteredCache = res;
                    });
                }

                // Switch to the correct tab
                if (caseFile.ViewState.SelectedTab == "APP")
                    _parent.SelectedTabIndex = 1;
                else
                    _parent.SelectedTabIndex = 0;
            }

            // 4. Refresh view
            Application.Current.Dispatcher.Invoke(() =>
            {
                _filterVM.ApplyMainLogsFilter();
                _filterVM.ApplyAppLogsFilter();

                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));

                _sessionVM.IsBusy = false;
                _sessionVM.StatusMessage = $"Case loaded: {annotationsRestored} annotations restored";

                _dialogService.ShowInfo(
                    $"Case loaded successfully!\n\n" +
                    $"📝 Annotations: {annotationsRestored}/{caseFile.Annotations?.Count ?? 0}\n" +
                    $"🎨 Coloring Rules: {MainColoringRules.Count} (Main) + {AppColoringRules.Count} (App)\n" +
                    $"🔍 Filters: {(_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children.Count > 0 ? "Active" : "None")}\n" +
                    $"🔎 Search: {(string.IsNullOrEmpty(_filterVM.SearchText) ? "None" : $"\"{_filterVM.SearchText}\"")}\n" +
                    $"🧵 Thread Filters: {_filterVM.ActiveThreadFilters.Count}\n" +
                    $"🚫 Filter Out: {_filterVM.NegativeFilters.Count}",
                    "Case Loaded");
            });
            }
            catch (Exception ex) { AppLogger.Error("ApplyCaseSettings failed", ex); }
        }

        private async Task OpenColoringWindow(object obj)
        {
            // ── Different Logs tab (index 12): route to its own coloring logic ──
            if (_parent.SelectedTabIndex == 12)
            {
                await OpenDifferentLogsColoringWindow(obj);
                return;
            }

            try
            {
                var win = new ColoringWindow();
                bool isAppTab = _parent.SelectedTabIndex == 1;
                var currentRulesSource = isAppTab ? AppColoringRules : MainColoringRules;
                var rulesCopy = currentRulesSource.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    _sessionVM.IsBusy = true;
                    _sessionVM.StatusMessage = isAppTab ? "Applying APP Colors..." : "Applying Main Colors...";

                    await Task.Run(async () =>
                    {
                        if (isAppTab)
                        {
                            AppColoringRules = newRules;
                            if (_sessionVM.AllAppLogsCache != null)
                            {
                                await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllAppLogsCache, true);
                                await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllAppLogsCache, AppColoringRules);
                            }
                        }
                        else
                        {
                            MainColoringRules = newRules;
                            if (_sessionVM.AllLogsCache != null)
                            {
                                await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllLogsCache, false);
                                await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllLogsCache, MainColoringRules);
                            }
                        }
                    });

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Notify Active Filters panel so coloring labels appear
                        _parent.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
                        _parent.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
                    });

                    _sessionVM.IsBusy = false;
                    _sessionVM.StatusMessage = "Colors Updated.";
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error: {ex.Message}");
                _sessionVM.IsBusy = false;
            }
        }

        /// <summary>
        /// Opens the coloring window targeting the Different Logs tab (tab 12).
        /// Uses dynamic fields from the loaded plugin columns.
        /// </summary>
        private async Task OpenDifferentLogsColoringWindow(object obj)
        {
            try
            {
                var diffVM = _parent.DifferentLogsVM;
                if (diffVM == null || !diffVM.HasFile) return;

                // Safety: rebuild available fields if empty
                if (diffVM.AvailableFields == null || diffVM.AvailableFields.Count == 0)
                    diffVM.BuildAvailableFields();

                // Create window with dynamic fields from plugin columns
                var win = new ColoringWindow(diffVM.AvailableFields);

                // Load existing rules if any
                var rulesCopy = diffVM.ColoringRules.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    diffVM.ColoringRules = newRules;

                    _sessionVM.IsBusy = true;
                    _sessionVM.StatusMessage = "Applying Colors to Different Logs...";

                    int coloredCount = 0;
                    await Task.Run(async () =>
                    {
                        // Reset colors first, then apply custom rules
                        foreach (var entry in diffVM.AllLogEntries)
                            entry.CustomColor = null;

                        await _coloringService.ApplyCustomColoringAsync(diffVM.AllLogEntries, newRules);

                        coloredCount = diffVM.AllLogEntries.Count(e => e.CustomColor.HasValue);
                    });

                    // Re-create the collection on the UI thread so WPF sees the
                    // new RowBackground values via fresh DataGridRow containers.
                    diffVM.FilteredEntries = new ObservableCollection<LogEntry>(
                        diffVM.IsFilterActive && diffVM.FilterRoot != null
                            ? diffVM.AllLogEntries.Where(l => _filterVM.EvaluateFilterNode(l, diffVM.FilterRoot))
                            : diffVM.AllLogEntries);

                    _sessionVM.IsBusy = false;
                    _sessionVM.StatusMessage = $"Colors Applied — {coloredCount}/{diffVM.AllLogEntries.Count} entries colored";
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error: {ex.Message}");
                _sessionVM.IsBusy = false;
            }
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
