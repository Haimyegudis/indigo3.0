using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages case files, annotations, and marked logs
    /// </summary>
    public partial class CaseManagementViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly FilterSearchViewModel _filterVM;
        private readonly ILogColoringService _coloringService;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private readonly IWindowManager _windowManager;

        // Case management
        private CaseFile? _currentCase = null;
        private string? _currentCaseFilePath = null;
        private bool _isLoadingCase = false;

        /// <summary>
        /// Custom coloring rules applied to PLC/main log entries.
        /// </summary>
        public List<ColoringCondition> MainColoringRules { get; set; } = new List<ColoringCondition>();

        /// <summary>
        /// Custom coloring rules applied to APP log entries.
        /// </summary>
        public List<ColoringCondition> AppColoringRules { get; set; } = new List<ColoringCondition>();

        /// <summary>
        /// Maps log entries to their annotations for lookup and persistence.
        /// </summary>
        private Dictionary<LogEntry, LogAnnotation> _logAnnotations = new Dictionary<LogEntry, LogAnnotation>();
        public Dictionary<LogEntry, LogAnnotation> LogAnnotations => _logAnnotations;

        /// <summary>
        /// Collection of marked (bookmarked) PLC/main log entries.
        /// </summary>
        public ObservableCollection<LogEntry> MarkedLogs { get; set; }

        /// <summary>
        /// Collection of marked (bookmarked) APP log entries.
        /// </summary>
        public ObservableCollection<LogEntry> MarkedAppLogs { get; set; }

        /// <summary>
        /// Collection of saved filter/coloring configurations.
        /// </summary>
        public ObservableCollection<SavedConfiguration> SavedConfigs { get; set; }

        private MarkedLogsWindow? _markedMainLogsWindow;
        private MarkedLogsWindow? _markedAppLogsWindow;
        private MarkedLogsWindow? _combinedMarkedWindow;

        private SavedConfiguration? _selectedConfig;
        public SavedConfiguration? SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                _selectedConfig = value;
                OnPropertyChanged();
            }
        }

        private bool _isMarkedLogsCombined;
        public bool IsMarkedLogsCombined
        {
            get => _isMarkedLogsCombined;
            set
            {
                _isMarkedLogsCombined = value;
                OnPropertyChanged();

                // Close all existing marked windows when changing mode
                CloseAllMarkedWindows();
            }
        }

        private bool _showAllAnnotations = false;
        public bool ShowAllAnnotations
        {
            get => _showAllAnnotations;
            set
            {
                _showAllAnnotations = value;
                OnPropertyChanged();
                UpdateAllAnnotationsVisibility();
            }
        }

        public ICommand ToggleAnnotationCommand { get; }
        public ICommand CloseAnnotationCommand { get; }
        public ICommand AddAnnotationCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }
        public ICommand MarkLogCommand { get; }
        public ICommand UnmarkLogCommand { get; }
        public ICommand OpenMarkedWindowCommand { get; }
        public ICommand GoToNextMarkedCommand { get; }
        public ICommand GoToPrevMarkedCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand DeleteConfigCommand { get; }
        public ICommand ShowConfigsFolderCommand { get; }
        public ICommand SaveCaseCommand { get; }
        public ICommand LoadCaseCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }

        public CaseManagementViewModel(MainViewModel parent, LogSessionViewModel sessionVM, FilterSearchViewModel filterVM, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher, IWindowManager windowManager)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _filterVM = filterVM;
            _coloringService = new LogColoringService();
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowManager = windowManager;

            // Initialize collections
            MarkedLogs = new ObservableCollection<LogEntry>();
            MarkedAppLogs = new ObservableCollection<LogEntry>();
            SavedConfigs = new ObservableCollection<SavedConfiguration>();

            // Initialize commands
            ToggleAnnotationCommand = new RelayCommand(ToggleAnnotation);
            CloseAnnotationCommand = new RelayCommand(CloseAnnotation);
            AddAnnotationCommand = new RelayCommand(obj => { if (obj is LogEntry log) AddAnnotation(log); });
            DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation);
            MarkLogCommand = new RelayCommand(MarkRow);
            UnmarkLogCommand = new RelayCommand(UnmarkLog);
            OpenMarkedWindowCommand = new RelayCommand(OpenMarkedLogsWindow);
            GoToNextMarkedCommand = new RelayCommand(GoToNextMarked);
            GoToPrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            LoadConfigCommand = new RelayCommand(LoadConfig);
            DeleteConfigCommand = new RelayCommand(DeleteConfig);
            ShowConfigsFolderCommand = new RelayCommand(_ =>
            {
                try
                {
                    string dir = AppPaths.Root;
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = dir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Cannot open folder:\n{ex.Message}", "Error");
                }
            });
            SaveCaseCommand = new RelayCommand(SaveCase);
            LoadCaseCommand = new RelayCommand(LoadCase);
            OpenColoringWindowCommand = new RelayCommand(async o => await OpenColoringWindow(o));
        }

        // ── Annotation Methods ──

        private void CloseAnnotation(object? obj)
        {
            if (obj is LogEntry log)
            {
                log.IsAnnotationExpanded = false;
            }
        }

        /// <summary>
        /// Gets annotation for a specific log entry, or null if none exists
        /// </summary>
        public LogAnnotation? GetAnnotation(LogEntry? log)
        {
            if (log == null) return null;
            return _logAnnotations.TryGetValue(log, out var annotation) ? annotation : null;
        }

        /// <summary>
        /// Toggle annotation expansion for a log entry
        /// </summary>
        private void ToggleAnnotation(object? obj)
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
            var window = _viewFactory.Create<Views.AnnotationWindow>(GetAnnotation(log)?.Content ?? "");
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

        private bool SelectedLogHasAnnotation()
        {
            var selectedLog = _parent?.SelectedLog;
            return selectedLog != null && LogAnnotations.ContainsKey(selectedLog);
        }

        private void DeleteAnnotation(object? parameter)
        {
            LogEntry? log = parameter as LogEntry ?? _parent?.SelectedLog;

            if (log == null || !log.HasAnnotation) return;

            var result = _dialogService.ShowConfirm(
                $"Delete annotation for this log entry?\n\n{log.Message}",
                "Delete Annotation");

            if (result == DialogResult.Yes)
            {
                if (LogAnnotations.ContainsKey(log))
                    LogAnnotations.Remove(log);

                log.HasAnnotation = false;
                log.IsAnnotationExpanded = false;
                log.AnnotationContent = "";

                _sessionVM.StatusMessage = "Annotation deleted";
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
                Snippet = log.Message?.Length > 100 ? log.Message.Substring(0, 100) : log.Message ?? ""
            };
        }

        /// <summary>
        /// Finds a log entry matching the soft link target
        /// </summary>
        public LogEntry? FindLogByTarget(LogTarget? target, IEnumerable<LogEntry>? logs)
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
    }
}
