using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels.Components
{
    /// <summary>
    /// Manages log filtering, searching, and logger tree operations for PLC and APP log views.
    /// </summary>
    public partial class FilterSearchViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;
        private readonly LogSessionViewModel _sessionVM;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IDispatcher _dispatcher;
        private readonly IWindowOwnerProvider _windowOwner;

        /// <summary>
        /// User-configurable default PLC filter applied when no explicit filters are active.
        /// </summary>
        private FilterNode? _defaultPlcFilter;
        public FilterNode? DefaultPlcFilter
        {
            get => _defaultPlcFilter;
            set { _defaultPlcFilter = value; OnPropertyChanged(); }
        }

        // --- Search ---
        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    OnSearchTextChanged();
                }
            }
        }

        private bool _isSearchPanelVisible;
        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            set
            {
                if (_isSearchPanelVisible != value)
                {
                    _isSearchPanelVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Filtered PLC/main log entries displayed in the FILTERED tab.
        /// </summary>
        private ObservableRangeCollection<LogEntry> _filteredLogs;
        public ObservableRangeCollection<LogEntry> FilteredLogs
        {
            get => _filteredLogs;
            set
            {
                _filteredLogs = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Filtered APP developer log entries displayed in the APP tab.
        /// </summary>
        private ObservableRangeCollection<LogEntry> _appDevLogsFiltered;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered
        {
            get => _appDevLogsFiltered;
            set
            {
                _appDevLogsFiltered = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Hierarchical tree of APP logger names for tree-based filtering.
        /// </summary>
        private ObservableCollection<LoggerNode> _loggerTreeRoot;
        public ObservableCollection<LoggerNode> LoggerTreeRoot
        {
            get => _loggerTreeRoot;
            set
            {
                _loggerTreeRoot = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Hierarchical tree of PLC logger names for tree-based filtering.
        /// </summary>
        private ObservableCollection<LoggerNode> _plcLoggerTreeRoot = new ObservableCollection<LoggerNode>();
        public ObservableCollection<LoggerNode> PlcLoggerTreeRoot
        {
            get => _plcLoggerTreeRoot;
            set
            {
                _plcLoggerTreeRoot = value;
                OnPropertyChanged();
            }
        }

        private LoggerNode? _selectedTreeItem;
        public LoggerNode? SelectedTreeItem
        {
            get => _selectedTreeItem;
            set
            {
                _selectedTreeItem = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Root node of the advanced filter tree for PLC/main logs.
        /// </summary>
        private FilterNode? _mainFilterRoot;
        public FilterNode? MainFilterRoot
        {
            get => _mainFilterRoot;
            set
            {
                _mainFilterRoot = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Root node of the advanced filter tree for APP logs.
        /// </summary>
        private FilterNode? _appFilterRoot;
        public FilterNode? AppFilterRoot
        {
            get => _appFilterRoot;
            set { _appFilterRoot = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Root node of a saved/persisted filter tree for restoring filter state.
        /// </summary>
        private FilterNode? _savedFilterRoot;
        public FilterNode? SavedFilterRoot
        {
            get => _savedFilterRoot;
            set { _savedFilterRoot = value; OnPropertyChanged(); }
        }

        private DispatcherTimer _searchDebounceTimer;

        // --- Commands ---
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand OpenLoggerFilterCommand { get; }
        public ICommand OpenMethodFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand TreeShowThisCommand { get; }
        public ICommand TreeHideThisCommand { get; }
        public ICommand TreeShowOnlyThisCommand { get; }
        public ICommand TreeShowWithChildrenCommand { get; }
        public ICommand TreeHideWithChildrenCommand { get; }
        public ICommand TreeShowAllCommand { get; }
        public ICommand OpenTimeRangeFilterCommand { get; }
        public ICommand StartRangeCommand { get; }
        public ICommand EndRangeCommand { get; }
        public ICommand ClearRangeCommand { get; }

        // Range selection state
        private LogEntry? _rangeStartLog = null;
        private bool _hasRangeStart = false;
        public bool HasRangeStart
        {
            get => _hasRangeStart;
            set { _hasRangeStart = value; OnPropertyChanged(); }
        }

        public FilterSearchViewModel(MainViewModel parent, LogSessionViewModel sessionVM, IDialogService dialogService, IViewFactory viewFactory, IDispatcher dispatcher, IWindowOwnerProvider windowOwner)
        {
            _parent = parent;
            _sessionVM = sessionVM;
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _dispatcher = dispatcher;
            _windowOwner = windowOwner;

            _filteredLogs = new ObservableRangeCollection<LogEntry>();
            _appDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            _loggerTreeRoot = new ObservableCollection<LoggerNode>();
            _plcLoggerTreeRoot = new ObservableCollection<LoggerNode>();

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            ToggleSearchCommand = new RelayCommand(o =>
            {
                // Force refresh by toggling if already true
                if (IsSearchPanelVisible)
                {
                    IsSearchPanelVisible = false;
                }
                IsSearchPanelVisible = true;
            });
            CloseSearchCommand = new RelayCommand(o =>
            {
                // Save the currently selected log and its scroll position BEFORE clearing search
                var savedSelectedLog = _parent.SelectedLog;
                if (savedSelectedLog != null)
                {
                    _parent.SaveScrollPosition(savedSelectedLog);
                }

                SearchText = "";  // Clear the search text
                IsSearchPanelVisible = false;
                // Refresh the logs to show all (without search filter)
                ApplyMainLogsFilter();
                ApplyAppLogsFilter();

                // Restore the selected log and scroll to it, preserving visual position
                // Use Dispatcher to ensure UI has fully updated before scrolling
                if (savedSelectedLog != null)
                {
                    var logToRestore = savedSelectedLog;
                    _dispatcher.Post(() =>
                    {
                        _parent.SelectedLog = logToRestore;
                        _parent.ScrollToLogPreservePosition(logToRestore);
                    }, DispatchPriority.ContextIdle);
                }
            });
            OpenFilterWindowCommand = new RelayCommand(async o => await OpenFilterWindow(o));
            FilterOutCommand = new RelayCommand(FilterOut);
            FilterOutThreadCommand = new RelayCommand(FilterOutThread);

            // Fixed commands calling specific logic
            OpenThreadFilterCommand = new RelayCommand(OpenThreadFilter);
            OpenLoggerFilterCommand = new RelayCommand(OpenLoggerFilter);
            OpenMethodFilterCommand = new RelayCommand(OpenMethodFilter);

            FilterContextCommand = new RelayCommand(FilterContext);
            UndoFilterOutCommand = new RelayCommand(UndoFilterOut);
            TreeShowThisCommand = new RelayCommand(ExecuteTreeShowThis);
            TreeHideThisCommand = new RelayCommand(ExecuteTreeHideThis);
            TreeShowOnlyThisCommand = new RelayCommand(ExecuteTreeShowOnlyThis);
            TreeShowWithChildrenCommand = new RelayCommand(ExecuteTreeShowWithChildren);
            TreeHideWithChildrenCommand = new RelayCommand(ExecuteTreeHideWithChildren);
            TreeShowAllCommand = new RelayCommand(ExecuteTreeShowAll);
            OpenTimeRangeFilterCommand = new RelayCommand(OpenTimeRangeFilter);
            StartRangeCommand = new RelayCommand(StartRange);
            EndRangeCommand = new RelayCommand(EndRange, o => HasRangeStart);
            ClearRangeCommand = new RelayCommand(ClearRange);
        }

        private void OnSearchTextChanged()
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OnSearchTimerTick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            // Save the currently selected log and its scroll position BEFORE applying search filter
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            ApplyMainLogsFilter();
            ApplyAppLogsFilter();

            // Restore the selected log and scroll to it, preserving visual position
            if (savedSelectedLog != null)
            {
                var logToRestore = savedSelectedLog;
                _dispatcher.Post(() =>
                {
                    _parent.SelectedLog = logToRestore;
                    _parent.ScrollToLogPreservePosition(logToRestore);
                }, DispatchPriority.ContextIdle);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= OnSearchTimerTick;
                }
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
