using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels.Components;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// Main ViewModel for the Log Comparison window.
    /// Manages two comparison panes, diff engine, and synchronization.
    /// </summary>
    public class LogComparisonViewModel : INotifyPropertyChanged
    {
        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raised when a log should be scrolled to in the main window.
        /// </summary>
        public event Action<LogEntry> RequestScrollToMainWindow;

        #endregion

        #region Fields

        private readonly MainViewModel _mainViewModel;
        private bool _showDiffs = true;
        private string _ignoreMaskPattern;
        private bool _isSyncLocked = true;
        private bool _isMaskValid = true;

        // Delta calculation threshold constants (in milliseconds)
        private const double DeltaSyncThresholdMs = 100;
        private const double DeltaSmallThresholdMs = 1000;

        #endregion

        #region Constructor

        public LogComparisonViewModel(IList<LogEntry> allPlcLogs, IList<LogEntry> allAppLogs, MainViewModel mainViewModel = null)
        {
            _mainViewModel = mainViewModel;

            // Initialize child ViewModels
            LeftPane = new ComparisonPaneViewModel(allPlcLogs, allAppLogs);
            RightPane = new ComparisonPaneViewModel(allPlcLogs, allAppLogs);

            // Set default: Left = PLC, Right = APP
            LeftPane.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllPLC;
            RightPane.SelectedSourceType = ComparisonPaneViewModel.SourceType.AllAPP;

            // Initialize diff engine
            DiffEngine = new DiffEngine();

            // Subscribe to selection changes for delta calculation
            LeftPane.PropertyChanged += OnPanePropertyChanged;
            RightPane.PropertyChanged += OnPanePropertyChanged;

            // Initialize commands
            ToggleDiffCommand = new RelayCommand(_ => ShowDiffs = !ShowDiffs);
            ToggleSyncCommand = new RelayCommand(_ => IsSyncLocked = !IsSyncLocked);
            GoToSourceCommand = new RelayCommand(ExecuteGoToSource, CanExecuteGoToSource);

            // Wire up navigation to main window
            if (_mainViewModel != null)
            {
                RequestScrollToMainWindow += log =>
                {
                    _mainViewModel.SelectedLog = log;
                    _mainViewModel.ScrollToLog(log);
                };
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// The left comparison pane ViewModel.
        /// </summary>
        public ComparisonPaneViewModel LeftPane { get; }

        /// <summary>
        /// The right comparison pane ViewModel.
        /// </summary>
        public ComparisonPaneViewModel RightPane { get; }

        /// <summary>
        /// The diff engine for comparing text.
        /// </summary>
        public DiffEngine DiffEngine { get; }

        /// <summary>
        /// Whether to show diff highlighting.
        /// </summary>
        public bool ShowDiffs
        {
            get => _showDiffs;
            set
            {
                if (_showDiffs != value)
                {
                    _showDiffs = value;
                    ComparisonDebugLogger.Log("VIEWMODEL", $"ShowDiffs changed to {value}");
                    OnPropertyChanged();
                    // Trigger refresh of diff display
                    DiffVersion++;
                    ComparisonDebugLogger.Log("VIEWMODEL", $"DiffVersion incremented to {DiffVersion}");
                }
            }
        }

        /// <summary>
        /// Regex pattern for content to ignore during comparison.
        /// </summary>
        public string IgnoreMaskPattern
        {
            get => _ignoreMaskPattern;
            set
            {
                if (_ignoreMaskPattern != value)
                {
                    ComparisonDebugLogger.LogSeparator("PATTERN CHANGE");
                    ComparisonDebugLogger.Log("VIEWMODEL", $"IgnoreMaskPattern changing from \"{_ignoreMaskPattern ?? "(null)"}\" to \"{value ?? "(null)"}\"");

                    _ignoreMaskPattern = value;
                    DiffEngine.IgnoreMaskPattern = value;
                    IsMaskValid = DiffEngine.IsMaskPatternValid;

                    ComparisonDebugLogger.Log("VIEWMODEL", $"IsMaskValid = {IsMaskValid}");

                    // Test the pattern on sample text if valid
                    if (IsMaskValid && !string.IsNullOrEmpty(value))
                    {
                        var testText = "Test123 Thread-456 [INFO] 2024-01-15 12:34:56.789 GUID=550e8400-e29b-41d4-a716-446655440000";
                        var masked = DiffEngine.ApplyMask(testText);
                        ComparisonDebugLogger.Log("VIEWMODEL", $"Pattern test:");
                        ComparisonDebugLogger.Log("VIEWMODEL", $"  Original: {testText}");
                        ComparisonDebugLogger.Log("VIEWMODEL", $"  Masked:   {masked}");
                    }

                    OnPropertyChanged();
                    // Trigger refresh of diff display by incrementing version
                    DiffVersion++;
                    ComparisonDebugLogger.Log("VIEWMODEL", $"DiffVersion incremented to {DiffVersion}");
                }
            }
        }

        private int _diffVersion;
        /// <summary>
        /// Version counter that increments when diffs need to be recalculated.
        /// Used to trigger binding updates in the UI.
        /// </summary>
        public int DiffVersion
        {
            get => _diffVersion;
            private set
            {
                _diffVersion = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether the current mask pattern is valid.
        /// </summary>
        public bool IsMaskValid
        {
            get => _isMaskValid;
            private set
            {
                if (_isMaskValid != value)
                {
                    _isMaskValid = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether scroll synchronization is enabled.
        /// </summary>
        public bool IsSyncLocked
        {
            get => _isSyncLocked;
            set
            {
                if (_isSyncLocked != value)
                {
                    _isSyncLocked = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Display string for the time delta between selected logs.
        /// </summary>
        public string DeltaDisplay
        {
            get
            {
                if (LeftPane?.SelectedLog == null || RightPane?.SelectedLog == null)
                    return "—";

                var deltaMs = (RightPane.SelectedLog.Date - LeftPane.SelectedLog.Date).TotalMilliseconds;

                if (Math.Abs(deltaMs) < 1)
                    return "0ms";

                if (Math.Abs(deltaMs) < 1000)
                    return deltaMs > 0 ? $"+{deltaMs:F0}ms" : $"{deltaMs:F0}ms";

                var deltaSec = deltaMs / 1000;
                return deltaSec > 0 ? $"+{deltaSec:F1}s" : $"{deltaSec:F1}s";
            }
        }

        /// <summary>
        /// Color for the delta indicator.
        /// </summary>
        public Brush DeltaColor
        {
            get
            {
                if (LeftPane?.SelectedLog == null || RightPane?.SelectedLog == null)
                    return Brushes.Gray;

                var absMs = Math.Abs((RightPane.SelectedLog.Date - LeftPane.SelectedLog.Date).TotalMilliseconds);

                if (absMs < DeltaSyncThresholdMs)
                    return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green

                if (absMs < DeltaSmallThresholdMs)
                    return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Yellow

                return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
        }

        #endregion

        #region Commands

        /// <summary>
        /// Command to toggle diff highlighting.
        /// </summary>
        public ICommand ToggleDiffCommand { get; }

        /// <summary>
        /// Command to toggle scroll synchronization.
        /// </summary>
        public ICommand ToggleSyncCommand { get; }

        /// <summary>
        /// Command to navigate to a log in the main window.
        /// </summary>
        public ICommand GoToSourceCommand { get; }

        private bool CanExecuteGoToSource(object param)
        {
            return param is LogEntry;
        }

        private void ExecuteGoToSource(object param)
        {
            if (param is LogEntry log)
            {
                RequestScrollToMainWindow?.Invoke(log);
                WindowManager.ActivateMainWindow();
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the diff result for comparing two log entries.
        /// </summary>
        public DiffResult GetDiffForRow(LogEntry left, LogEntry right)
        {
            if (left == null || right == null || !ShowDiffs)
                return null;

            return DiffEngine.Compare(left.Message, right.Message);
        }

        /// <summary>
        /// Handles scroll change from one pane to sync the other.
        /// </summary>
        public void OnPaneScrollChanged(ComparisonPaneViewModel initiator, DateTime topTime)
        {
            if (!IsSyncLocked)
                return;

            var target = initiator == LeftPane ? RightPane : LeftPane;
            int nearestIndex = target.BinarySearchNearest(topTime);

            if (nearestIndex >= 0)
            {
                target.TopVisibleIndex = nearestIndex;
            }
        }

        /// <summary>
        /// Gets the log at the corresponding index in the other pane based on time.
        /// </summary>
        public LogEntry GetCorrespondingLog(ComparisonPaneViewModel fromPane, LogEntry log)
        {
            if (log == null)
                return null;

            var targetPane = fromPane == LeftPane ? RightPane : LeftPane;
            int nearestIndex = targetPane.BinarySearchNearest(log.Date);

            return targetPane.GetLogAtIndex(nearestIndex);
        }

        #endregion

        #region Event Handlers

        private void OnPanePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ComparisonPaneViewModel.SelectedLog))
            {
                OnPropertyChanged(nameof(DeltaDisplay));
                OnPropertyChanged(nameof(DeltaColor));
            }
        }

        #endregion

        #region INotifyPropertyChanged

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

}
