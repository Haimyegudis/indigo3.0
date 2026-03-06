using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel : ViewModelBase
    {
        private readonly LogSessionData _sessionData;
        private readonly ICsvExportService _csvService;
        private readonly IDialogService _dialogService;
        private readonly IDispatcher _dispatcher;

        // S4-5 mode: binary APP — hides AXIS, CHStep, Thread columns (show only IO)
        public bool IsBinaryApp { get; private set; }

        // IoTerminal data (S4-5 with Io-*.csv files in TerminalLogs)
        private bool _hasIoTerminalData;
        private List<IoDeviceData> _ioDevices = new();

        public ObservableCollection<SelectableItem> IOComponents { get; set; }
        public ObservableCollection<SelectableItem> AxisComponents { get; set; }
        public ObservableCollection<SelectableItem> CHStepComponents { get; set; }
        public ObservableCollection<SelectableItem> ThreadItems { get; set; }


        private bool _includeUnixTime = true;
        public bool IncludeUnixTime
        {
            get => _includeUnixTime;
            set { _includeUnixTime = value; OnPropertyChanged(nameof(IncludeUnixTime)); }
        }

        private bool _includeEvents = true;
        public bool IncludeEvents
        {
            get => _includeEvents;
            set { _includeEvents = value; OnPropertyChanged(nameof(IncludeEvents)); }
        }

        private bool _includeMachineState = true;
        public bool IncludeMachineState
        {
            get => _includeMachineState;
            set { _includeMachineState = value; OnPropertyChanged(nameof(IncludeMachineState)); }
        }

        private bool _includeLogStats = false;
        public bool IncludeLogStats
        {
            get => _includeLogStats;
            set
            {
                _includeLogStats = value;
                OnPropertyChanged(nameof(IncludeLogStats));
                // CommandManager will automatically refresh - no manual trigger needed
            }
        }

        // EM Statistics Gantt — only visible when ZIP contained EM_Statistics CSV
        public bool HasEmStatisticsData => !string.IsNullOrEmpty(_sessionData?.EmStatisticsCsvContent);

        private bool _includeEmStatistics = true;
        public bool IncludeEmStatistics
        {
            get => _includeEmStatistics;
            set
            {
                _includeEmStatistics = value;
                OnPropertyChanged(nameof(IncludeEmStatistics));
                CommandManager.InvalidateRequerySuggested();
            }
        }


        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(IsProgressVisible));
            }
        }

        private string _loadingMessage = "";
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                _loadingMessage = value;
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }

        private double _loadingProgress;
        public double LoadingProgress
        {
            get => _loadingProgress;
            set { _loadingProgress = value; OnPropertyChanged(nameof(LoadingProgress)); }
        }

        public bool IsProgressVisible => IsLoading && LoadingProgress > 0;

        // ── Per-signal progress tracking ──
        // Uses a plain List replaced wholesale via PropertyChanged (NOT ObservableCollection).
        // This avoids dispatcher flooding from hundreds of rapid CollectionChanged events
        // fired by Parallel.ForEach → Progress<T> → BeginInvoke during signal parsing.
        private List<SignalProgressItem> _signalProgressItems = new List<SignalProgressItem>();
        public IReadOnlyList<SignalProgressItem> SignalProgressItems => _signalProgressItems;
        public bool HasSignalProgress => _signalProgressItems.Count > 0;


        public ICommand ExportCommand { get; }
        public ICommand OpenInChartsTabCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand LoadPresetCommand { get; }
        public ICommand SelectAllIOCommand { get; }
        public ICommand SelectAllAxisCommand { get; }
        public ICommand SelectAllCHStepsCommand { get; }
        public ICommand SelectAllThreadsCommand { get; }
        public ICommand DeselectAllIOCommand { get; }
        public ICommand DeselectAllAxisCommand { get; }
        public ICommand DeselectAllCHStepsCommand { get; }
        public ICommand DeselectAllThreadsCommand { get; }

        public ExportConfigurationViewModel(LogSessionData sessionData, ICsvExportService csvService, IDialogService dialogService, IDispatcher dispatcher)
        {
            _sessionData = sessionData;
            _csvService = csvService;
            _dialogService = dialogService;
            _dispatcher = dispatcher;

            // S4-5: binary APP — only IO column visible in export window
            IsBinaryApp = sessionData?.HasBinaryAppLogs == true;

            IOComponents = new ObservableCollection<SelectableItem>();
            AxisComponents = new ObservableCollection<SelectableItem>();
            CHStepComponents = new ObservableCollection<SelectableItem>();
            ThreadItems = new ObservableCollection<SelectableItem>();

            // Initialize debounce timer for search
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SEARCH_DEBOUNCE_MS)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            ExportCommand = new RelayCommand(async _ => await ExecuteExport(), _ => CanExport());
            OpenInChartsTabCommand = new RelayCommand(async _ => await OpenInChartsTabAsync(), _ => CanExport());
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            LoadPresetCommand = new RelayCommand(_ => LoadPreset());
            SelectAllIOCommand = new RelayCommand(_ => SelectAll(IOComponents));
            SelectAllAxisCommand = new RelayCommand(_ => SelectAll(AxisComponents));
            SelectAllCHStepsCommand = new RelayCommand(_ => SelectAll(CHStepComponents));
            SelectAllThreadsCommand = new RelayCommand(_ => SelectAll(ThreadItems));
            DeselectAllIOCommand = new RelayCommand(_ => DeselectAll(IOComponents));
            DeselectAllAxisCommand = new RelayCommand(_ => DeselectAll(AxisComponents));
            DeselectAllCHStepsCommand = new RelayCommand(_ => DeselectAll(CHStepComponents));
            DeselectAllThreadsCommand = new RelayCommand(_ => DeselectAll(ThreadItems));

            _ = LoadComponentsAndThreads();
        }

        // LoadComponentsAndThreads is in ExportConfigurationViewModel.ComponentScan.cs
        // CanExport, ExecuteExport, SavePreset, LoadPreset, ApplyPreset,
        //   SelectAll, DeselectAll are in ExportConfigurationViewModel.Export.cs
        // OpenInChartsTabAsync and chart data preparation are in ExportConfigurationViewModel.ChartData.cs
        // Search text properties, filter caching, debounce logic are in ExportConfigurationViewModel.Filtering.cs

        /// <summary>
        /// Action to close the window (set by the view)
        /// </summary>
        public Action? CloseWindow { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                    _searchDebounceTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// IProgress implementation that calls handler directly on the calling thread.
        /// </summary>
        private class DirectProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public DirectProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }
    }

    /// <summary>
    /// Represents a single signal's parsing status in the per-signal progress list.
    /// </summary>
    public class SignalProgressItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";

        private string _status = "pending";
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusIcon)));
            }
        }

        public string StatusIcon
        {
            get
            {
                switch (_status)
                {
                    case "done": return "\u2714";      // ✔
                    case "parsing": return "\u23F3";    // ⏳
                    default: return "\u2022";           // •
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
