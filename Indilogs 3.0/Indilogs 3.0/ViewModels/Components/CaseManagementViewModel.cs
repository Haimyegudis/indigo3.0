using DocumentFormat.OpenXml.Spreadsheet;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages case files, annotations, and marked logs
    /// </summary>
    public class CaseManagementViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly FilterSearchViewModel _filterVM;
        private readonly LogColoringService _coloringService;

        // Case management
        private CaseFile _currentCase = null;
        private string _currentCaseFilePath = null;
        private bool _isLoadingCase = false;

        // Coloring rules
        public List<ColoringCondition> MainColoringRules { get; set; } = new List<ColoringCondition>();
        public List<ColoringCondition> AppColoringRules { get; set; } = new List<ColoringCondition>();

        // Annotations
        private Dictionary<LogEntry, LogAnnotation> _logAnnotations = new Dictionary<LogEntry, LogAnnotation>();
        public Dictionary<LogEntry, LogAnnotation> LogAnnotations => _logAnnotations;

        // Marked logs management
        public ObservableCollection<LogEntry> MarkedLogs { get; set; }
        public ObservableCollection<LogEntry> MarkedAppLogs { get; set; }
        public ObservableCollection<SavedConfiguration> SavedConfigs { get; set; }

        // Marked log windows
        private MarkedLogsWindow _markedMainLogsWindow;
        private MarkedLogsWindow _markedAppLogsWindow;
        private MarkedLogsWindow _combinedMarkedWindow;

        private SavedConfiguration _selectedConfig;
        public SavedConfiguration SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                _selectedConfig = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.SelectedConfig));
            }
        }

        private bool _isMarkedLogsCombined;
        public bool IsMarkedLogsCombined
        {
            get => _isMarkedLogsCombined;
            set
            {
                if (_isMarkedLogsCombined != value)
                {
                    _isMarkedLogsCombined = value;
                    OnPropertyChanged();
                    _parent?.NotifyPropertyChanged(nameof(_parent.IsMarkedLogsCombined));
                    CloseAllMarkedWindows();
                }
            }
        }

        // Annotation management
        private bool _showAllAnnotations = false;
        public bool ShowAllAnnotations
        {
            get => _showAllAnnotations;
            set
            {
                _showAllAnnotations = value;
                OnPropertyChanged();
                _parent?.NotifyPropertyChanged(nameof(_parent.ShowAllAnnotations));
                UpdateAllAnnotationsVisibility();
            }
        }

        // Commands
        public ICommand ToggleAnnotationCommand { get; }
        public ICommand CloseAnnotationCommand { get; }
        public ICommand AddAnnotationCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }  // ✅ DELETE ANNOTATION COMMAND
        public ICommand MarkLogCommand { get; }
        public ICommand UnmarkLogCommand { get; }
        public ICommand OpenMarkedWindowCommand { get; }
        public ICommand GoToNextMarkedCommand { get; }
        public ICommand GoToPrevMarkedCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand DeleteConfigCommand { get; }
        public ICommand SaveCaseCommand { get; }
        public ICommand LoadCaseCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }

        public CaseManagementViewModel(MainViewModel parent, LogSessionViewModel sessionVM, FilterSearchViewModel filterVM)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _filterVM = filterVM;
            _coloringService = new LogColoringService();

            // Initialize collections
            MarkedLogs = new ObservableCollection<LogEntry>();
            MarkedAppLogs = new ObservableCollection<LogEntry>();
            SavedConfigs = new ObservableCollection<SavedConfiguration>();

            // Initialize commands
            ToggleAnnotationCommand = new RelayCommand(ToggleAnnotation);
            CloseAnnotationCommand = new RelayCommand(CloseAnnotation);
            AddAnnotationCommand = new RelayCommand(obj => { if (obj is LogEntry log) AddAnnotation(log); });
            DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation);  // ✅ No CanExecute - just check in the method itself
            MarkLogCommand = new RelayCommand(MarkRow);
            UnmarkLogCommand = new RelayCommand(UnmarkLog);
            OpenMarkedWindowCommand = new RelayCommand(OpenMarkedLogsWindow);
            GoToNextMarkedCommand = new RelayCommand(GoToNextMarked);
            GoToPrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            LoadConfigCommand = new RelayCommand(LoadConfig);
            DeleteConfigCommand = new RelayCommand(DeleteConfig);
            SaveCaseCommand = new RelayCommand(SaveCase);
            LoadCaseCommand = new RelayCommand(LoadCase);
            OpenColoringWindowCommand = new RelayCommand(OpenColoringWindow);
        }

        private void CloseAnnotation(object obj)
        {
            if (obj is LogEntry log)
            {
                log.IsAnnotationExpanded = false;
            }
        }

        /// <summary>
        /// Gets annotation for a specific log entry, or null if none exists
        /// </summary>
        public LogAnnotation GetAnnotation(LogEntry log)
        {
            if (log == null) return null;
            return _logAnnotations.TryGetValue(log, out var annotation) ? annotation : null;
        }

        /// <summary>
        /// Toggle annotation expansion for a log entry
        /// </summary>
        private void ToggleAnnotation(object obj)
        {
            if (obj is LogEntry log && log.HasAnnotation)
            {
                log.IsAnnotationExpanded = !log.IsAnnotationExpanded;
            }
        }

        /// <summary>
        /// Adds or updates annotation for a log entry
        /// </summary>
        public void AddAnnotation(LogEntry log)
        {
            if (log == null) return;

            // Prompt user for comment
            var window = new Views.AnnotationWindow(GetAnnotation(log)?.Content ?? "");
            if (window.ShowDialog() == true)
            {
                // Save custom color if exists, otherwise use default yellow
                string color = "#FFFF00";
                if (log.CustomColor.HasValue)
                {
                    color = log.CustomColor.Value.ToString();
                }

                var annotation = new LogAnnotation
                {
                    TargetLog = CreateLogTarget(log),
                    Content = window.AnnotationText,
                    Color = color,
                    Author = Environment.UserName,
                    CreatedAt = DateTime.Now
                };

                _logAnnotations[log] = annotation;

                // Mark log as having annotation for visual indicator
                log.HasAnnotation = true;
                log.AnnotationContent = window.AnnotationText;
                log.IsAnnotationExpanded = true;

                _sessionVM.StatusMessage = "Annotation added";
            }
        }

        // ✅ DELETE ANNOTATION METHODS
        private bool SelectedLogHasAnnotation()
        {
            var selectedLog = _parent?.SelectedLog;
            return selectedLog != null && LogAnnotations.ContainsKey(selectedLog);
        }

        private void DeleteAnnotation(object parameter)
        {
            LogEntry log = parameter as LogEntry ?? _parent?.SelectedLog;

            System.Diagnostics.Debug.WriteLine($"[DELETE] Parameter type: {parameter?.GetType().Name}, Log: {log?.Date:HH:mm:ss.fff}");
            System.Diagnostics.Debug.WriteLine($"[DELETE] HasAnnotation: {log?.HasAnnotation}, ContainsKey: {(log != null ? LogAnnotations.ContainsKey(log) : false)}");

            if (log == null)
            {
                System.Diagnostics.Debug.WriteLine("[DELETE] Log is null!");
                return;
            }

            if (!log.HasAnnotation)
            {
                System.Diagnostics.Debug.WriteLine("[DELETE] Log has no annotation!");
                return;
            }

            var result = MessageBox.Show(
                $"Delete annotation for this log entry?\n\n{log.Message}",
                "Delete Annotation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (LogAnnotations.ContainsKey(log))
                {
                    LogAnnotations.Remove(log);
                }

                log.HasAnnotation = false;
                log.IsAnnotationExpanded = false;
                log.AnnotationContent = null;

                _sessionVM.StatusMessage = "Annotation deleted";
                System.Diagnostics.Debug.WriteLine($"[ANNOTATION] Deleted annotation for log at {log.Date:HH:mm:ss.fff}");
            }
        }

        /// <summary>
        /// Creates a soft link target for a log entry
        /// </summary>
        private LogTarget CreateLogTarget(LogEntry log)
        {
            return new LogTarget
            {
                Timestamp = log.Date,
                Logger = log.Logger,
                Thread = log.ThreadName,
                Level = log.Level,
                Snippet = log.Message?.Length > 100 ? log.Message.Substring(0, 100) : log.Message
            };
        }

        /// <summary>
        /// Finds a log entry matching the soft link target
        /// </summary>
        public LogEntry FindLogByTarget(LogTarget target, IEnumerable<LogEntry> logs)
        {
            if (target == null || logs == null) return null;

            // Try exact match first
            var exactMatch = logs.FirstOrDefault(l =>
                l.Date == target.Timestamp &&
                l.Logger == target.Logger &&
                l.ThreadName == target.Thread);

            if (exactMatch != null) return exactMatch;

            // Fallback: find closest by timestamp with same logger/thread
            var timeTolerance = TimeSpan.FromMilliseconds(100);
            return logs.FirstOrDefault(l =>
                Math.Abs((l.Date - target.Timestamp).TotalMilliseconds) < timeTolerance.TotalMilliseconds &&
                l.Logger == target.Logger &&
                l.ThreadName == target.Thread &&
                (!string.IsNullOrEmpty(target.Snippet) && !string.IsNullOrEmpty(l.Message) &&
                 l.Message.StartsWith(target.Snippet.Substring(0, Math.Min(50, target.Snippet.Length)))));
        }

        /// <summary>
        /// Update all annotations visibility based on ShowAllAnnotations setting
        /// </summary>
        public void UpdateAllAnnotationsVisibility()
        {
            if (_sessionVM?.AllLogsCache != null)
            {
                foreach (var log in _sessionVM.AllLogsCache.Where(l => l.HasAnnotation))
                {
                    if (!ShowAllAnnotations)
                        log.IsAnnotationExpanded = false;
                }
            }

            if (_sessionVM?.AllAppLogsCache != null)
            {
                foreach (var log in _sessionVM.AllAppLogsCache.Where(l => l.HasAnnotation))
                {
                    if (!ShowAllAnnotations)
                        log.IsAnnotationExpanded = false;
                }
            }
        }

        /// <summary>
        /// Clear all annotations
        /// </summary>
        public void ClearAnnotations()
        {
            _logAnnotations.Clear();
        }

        /// <summary>
        /// Marks or unmarks the currently selected log entry
        /// </summary>
        private void MarkRow(object obj)
        {
            if (_parent.SelectedLog != null)
            {
                var currentLog = _parent.SelectedLog;
                currentLog.IsMarked = !currentLog.IsMarked;

                // Force UI refresh by re-notifying RowBackground
                currentLog.OnPropertyChanged(nameof(currentLog.RowBackground));

                bool isAppTab = _parent.SelectedTabIndex == 2;
                var targetList = isAppTab ? MarkedAppLogs : MarkedLogs;

                if (currentLog.IsMarked)
                {
                    targetList.Add(currentLog);
                    var sorted = targetList.OrderByDescending(x => x.Date).ToList();
                    targetList.Clear();
                    foreach (var l in sorted) targetList.Add(l);
                }
                else
                {
                    targetList.Remove(currentLog);
                }
            }
        }

        private void UnmarkLog(object obj)
        {
            // Placeholder for future unmark functionality
        }

        /// <summary>
        /// Opens marked logs window - combined or separate based on IsMarkedLogsCombined setting
        /// </summary>
        private void OpenMarkedLogsWindow(object obj)
        {
            if (IsMarkedLogsCombined)
            {
                // Check if combined window already exists
                if (_combinedMarkedWindow != null && _combinedMarkedWindow.IsVisible)
                {
                    _combinedMarkedWindow.Activate();
                    return;
                }

                // Combine main and app marked logs
                var combinedList = new List<LogEntry>();
                if (MarkedLogs != null) combinedList.AddRange(MarkedLogs);
                if (MarkedAppLogs != null) combinedList.AddRange(MarkedAppLogs);

                var sortedList = combinedList.OrderByDescending(x => x.Date).ToList();
                var collectionToShow = new ObservableCollection<LogEntry>(sortedList);

                _combinedMarkedWindow = new MarkedLogsWindow(collectionToShow, "Marked Lines (Combined - Main & App)");
                _combinedMarkedWindow.DataContext = _parent;
                _combinedMarkedWindow.Closed += (s, e) => _combinedMarkedWindow = null;
                WindowManager.OpenWindow(_combinedMarkedWindow);
            }
            else
            {
                bool isAppTab = _parent.SelectedTabIndex == 2;

                if (isAppTab)
                {
                    // Show App logs marked window
                    if (_markedAppLogsWindow != null && _markedAppLogsWindow.IsVisible)
                    {
                        WindowManager.ActivateWindow(_markedAppLogsWindow);
                        return;
                    }
                    _markedAppLogsWindow = new MarkedLogsWindow(MarkedAppLogs, "Marked Lines (APP)");
                    _markedAppLogsWindow.DataContext = _parent;
                    _markedAppLogsWindow.Closed += (s, e) => _markedAppLogsWindow = null;
                    WindowManager.OpenWindow(_markedAppLogsWindow);
                }
                else
                {
                    // Show Main logs marked window
                    if (_markedMainLogsWindow != null && _markedMainLogsWindow.IsVisible)
                    {
                        WindowManager.ActivateWindow(_markedMainLogsWindow);
                        return;
                    }
                    _markedMainLogsWindow = new MarkedLogsWindow(MarkedLogs, "Marked Lines (LOGS)");
                    _markedMainLogsWindow.DataContext = _parent;
                    _markedMainLogsWindow.Closed += (s, e) => _markedMainLogsWindow = null;
                    WindowManager.OpenWindow(_markedMainLogsWindow);
                }
            }
        }

        private void SaveConfig(object obj)
        {
            var existingNames = SavedConfigs.Select(c => c.Name).ToList();
            var dlg = new Views.SaveConfigWindow(existingNames);
            if (dlg.ShowDialog() == true)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
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
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(dlg.FileName));
                    c.FilePath = dlg.FileName;
                    SavedConfigs.Add(c);
                    _sessionVM.StatusMessage = $"Configuration '{c.Name}' loaded";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteConfig(object obj)
        {
            var configToDelete = SelectedConfig;
            if (configToDelete != null && MessageBox.Show($"Delete '{configToDelete.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (File.Exists(configToDelete.FilePath)) File.Delete(configToDelete.FilePath);
                SavedConfigs.Remove(configToDelete);
                _sessionVM.StatusMessage = $"Configuration '{configToDelete.Name}' deleted";
            }
        }

        public async void ApplyConfiguration(SavedConfiguration c)
        {
            if (c == null) return;

            _sessionVM.IsBusy = true;
            _sessionVM.StatusMessage = $"Loading config: {c.Name} (Overriding current state)...";

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _parent.SearchText = "";
                _parent.IsSearchPanelVisible = false;
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

            await Task.Run(async () =>
            {
                MainColoringRules = c.MainColoringRules ?? new List<ColoringCondition>();
                if (_sessionVM.AllLogsCache != null)
                {
                    // OPTIMIZATION: Only reapply default colors if there are custom rules
                    // (otherwise default colors were already applied during initial load)
                    if (MainColoringRules.Any())
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllLogsCache, false);
                        await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllLogsCache, MainColoringRules);
                    }
                }

                AppColoringRules = c.AppColoringRules ?? new List<ColoringCondition>();
                if (_sessionVM.AllAppLogsCache != null)
                {
                    // OPTIMIZATION: Only reapply default colors if there are custom rules
                    if (AppColoringRules.Any())
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllAppLogsCache, true);
                        await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllAppLogsCache, AppColoringRules);
                    }
                }
            });

            _filterVM.MainFilterRoot = c.MainFilterRoot;
            if (_filterVM.MainFilterRoot != null && _sessionVM.AllLogsCache != null)
            {
                var res = await Task.Run(() => _sessionVM.AllLogsCache.Where(l => _filterVM.EvaluateFilterNode(l, _filterVM.MainFilterRoot)).ToList());
                _filterVM.LastFilteredCache = res;
            }

            _filterVM.AppFilterRoot = c.AppFilterRoot;

            Application.Current.Dispatcher.Invoke(() =>
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
        }

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
                            QuickSearchText = _parent.SearchText,
                            SelectedTab = _parent.SelectedTabIndex == 0 ? "MAIN" : _parent.SelectedTabIndex == 1 ? "FILTERED" : "APP",
                            ActiveThreadFilters = _filterVM.ActiveThreadFilters.ToList(),
                            NegativeFilters = _filterVM.NegativeFilters.ToList()
                        },
                        MainColoringRules = MainColoringRules ?? new List<ColoringCondition>(),
                        AppColoringRules = AppColoringRules ?? new List<ColoringCondition>(),
                        Annotations = LogAnnotations.Values.ToList()
                    };

                    // Add resources (log files)
                    if (_parent.SelectedSession != null && !string.IsNullOrEmpty(_parent.SelectedSession.FilePath))
                    {
                        var fileInfo = new FileInfo(_parent.SelectedSession.FilePath);
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
                    MessageBox.Show($"Case file saved successfully!\n\n" +
                                  $"Filters: {(_filterVM.MainFilterRoot != null ? "✓" : "✗")}\n" +
                                  $"Coloring Rules: {MainColoringRules?.Count ?? 0} (Main) + {AppColoringRules?.Count ?? 0} (App)\n" +
                                  $"Annotations: {caseFile.Annotations.Count}\n" +
                                  $"Search: {(string.IsNullOrEmpty(_parent.SearchText) ? "✗" : "✓")}",
                                  "Case Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving case: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var caseFile = JsonConvert.DeserializeObject<CaseFile>(json);

                    if (caseFile == null)
                    {
                        MessageBox.Show("Invalid case file format.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                            var result = MessageBox.Show(
                                $"Log file not found: {resource.FileName}\n\n" +
                                $"Expected location: {caseDir}\n\n" +
                                $"Would you like to locate it manually?",
                                "File Not Found",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

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
                        _parent.LoadedSessions.Clear();
                        _parent.SelectedSession = null;

                        // Load the logs with callback
                        _parent.ProcessFiles(logFilesToLoad.ToArray(), session =>
                        {
                            // Callback called after logs are loaded successfully
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _sessionVM.StatusMessage = "Applying case settings...";
                                ApplyCaseSettings(caseFile);
                                _isLoadingCase = false;
                                _sessionVM.StatusMessage = "Case loaded successfully!";
                            });
                        });
                    }
                    else
                    {
                        MessageBox.Show(
                            "Case cannot be loaded without the log files.\n\n" +
                            "Please ensure the log files are in the same folder as the .indi-case file,\n" +
                            "or select them manually when prompted.",
                            "Missing Log Files",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading case: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _isLoadingCase = false;
            }
        }

        /// <summary>
        /// Applies case settings after logs are loaded
        /// </summary>
        private async void ApplyCaseSettings(CaseFile caseFile)
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
                            catch { }
                        }

                        annotationsRestored++;
                    }
                }
            }

            // 3. Restore view state (filters, search, etc.)
            if (caseFile.ViewState != null)
            {
                _filterVM.MainFilterRoot = caseFile.ViewState.ActiveFilters;
                _parent.SearchText = caseFile.ViewState.QuickSearchText ?? "";

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
                    _parent.SelectedTabIndex = 2;
                else if (caseFile.ViewState.SelectedTab == "FILTERED")
                    _parent.SelectedTabIndex = 1;
                else
                    _parent.SelectedTabIndex = 0;
            }

            // 4. Refresh view
            Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] Starting refresh view...");

                // Update the UI with filters first
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] Calling ApplyMainLogsFilter...");
                _filterVM.ApplyMainLogsFilter();
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] ApplyMainLogsFilter complete");

                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] Calling ApplyAppLogsFilter...");
                _filterVM.ApplyAppLogsFilter();
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] ApplyAppLogsFilter complete");

                // Check collection sizes AFTER filtering
                var logsCount = _parent.Logs?.Count() ?? 0;
                var appLogsCount = _parent.AppDevLogsFiltered?.Count ?? 0;
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] After filters - PLC Logs: {logsCount}, APP Logs: {appLogsCount}");

                // CRITICAL: Skip RowBackground refresh entirely - let WPF DataGrid virtualization handle it
                // The DataGrid will query RowBackground property when it renders visible rows
                // No need to trigger property change on potentially 167K+ logs
                System.Diagnostics.Debug.WriteLine($"[CASE LOAD] Skipping manual RowBackground refresh - WPF will handle via virtualization");

                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
                _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));

                _sessionVM.IsBusy = false;
                _sessionVM.StatusMessage = $"Case loaded: {annotationsRestored} annotations restored";

                MessageBox.Show(
                    $"Case loaded successfully!\n\n" +
                    $"📝 Annotations: {annotationsRestored}/{caseFile.Annotations?.Count ?? 0}\n" +
                    $"🎨 Coloring Rules: {MainColoringRules.Count} (Main) + {AppColoringRules.Count} (App)\n" +
                    $"🔍 Filters: {(_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children.Count > 0 ? "Active" : "None")}\n" +
                    $"🔎 Search: {(string.IsNullOrEmpty(_parent.SearchText) ? "None" : $"\"{_parent.SearchText}\"")}\n" +
                    $"🧵 Thread Filters: {_filterVM.ActiveThreadFilters.Count}\n" +
                    $"🚫 Filter Out: {_filterVM.NegativeFilters.Count}",
                    "Case Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        /// <summary>
        /// Closes all marked log windows (combined, main, and app)
        /// </summary>
        private void CloseAllMarkedWindows()
        {
            if (_combinedMarkedWindow != null) { _combinedMarkedWindow.Close(); _combinedMarkedWindow = null; }
            if (_markedMainLogsWindow != null) { _markedMainLogsWindow.Close(); _markedMainLogsWindow = null; }
            if (_markedAppLogsWindow != null) { _markedAppLogsWindow.Close(); _markedAppLogsWindow = null; }
        }

        // Track the currently highlighted marked log
        private LogEntry _currentMarkedLog = null;

        private void ClearCurrentMarked()
        {
            if (_currentMarkedLog != null)
            {
                _currentMarkedLog.IsCurrentMarked = false;
                _currentMarkedLog = null;
            }
        }

        /// <summary>
        /// Navigate to the next marked log entry
        /// </summary>
        private void GoToNextMarked(object obj)
        {
            if (!_parent.Logs.Any()) return;

            var list = _parent.Logs.ToList();
            int current = _parent.SelectedLog != null ? list.IndexOf(_parent.SelectedLog) : -1;
            var next = list.Skip(current + 1).FirstOrDefault(l => l.IsMarked) ?? list.FirstOrDefault(l => l.IsMarked);

            if (next != null)
            {
                ClearCurrentMarked();
                next.IsCurrentMarked = true;
                _currentMarkedLog = next;
                _parent.SelectedLog = next;
                _parent.ScrollToLog(next);
            }
        }

        /// <summary>
        /// Navigate to the previous marked log entry
        /// </summary>
        private void GoToPrevMarked(object obj)
        {
            if (!_parent.Logs.Any()) return;

            var list = _parent.Logs.ToList();
            int current = _parent.SelectedLog != null ? list.IndexOf(_parent.SelectedLog) : list.Count;
            var prev = list.Take(current).LastOrDefault(l => l.IsMarked) ?? list.LastOrDefault(l => l.IsMarked);

            if (prev != null)
            {
                ClearCurrentMarked();
                prev.IsCurrentMarked = true;
                _currentMarkedLog = prev;
                _parent.SelectedLog = prev;
                _parent.ScrollToLog(prev);
            }
        }

        /// <summary>
        /// Clears all marked logs collections
        /// </summary>
        public void ClearMarkedLogs()
        {
            MarkedLogs.Clear();
            MarkedAppLogs.Clear();
        }

        public void LoadSavedConfigs()
        {
            SavedConfigs.Clear();
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path, "*.json"))
                {
                    try
                    {
                        var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(f));
                        c.FilePath = f;
                        SavedConfigs.Add(c);
                    }
                    catch
                    {
                        // Ignore corrupted config files
                    }
                }
            }
        }

        public bool IsLoadingCase => _isLoadingCase;

        private async void OpenColoringWindow(object obj)
        {
            try
            {
                var win = new ColoringWindow();
                bool isAppTab = _parent.SelectedTabIndex == 2;
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
                        // Colors already applied by ColoringService which sets CustomColor property
                        // CustomColor setter already triggers OnPropertyChanged(nameof(RowBackground))
                        // No additional manual refresh needed - WPF handles the rest
                    });

                    _sessionVM.IsBusy = false;
                    _sessionVM.StatusMessage = "Colors Updated.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                _sessionVM.IsBusy = false;
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}