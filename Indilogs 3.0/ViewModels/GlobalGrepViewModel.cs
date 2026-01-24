using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the Global Grep window.
    /// Manages search across loaded sessions or external files.
    /// </summary>
    public class GlobalGrepViewModel : INotifyPropertyChanged
    {
        private readonly GlobalGrepService _grepService;
        private CancellationTokenSource _cancellationTokenSource;

        #region Constructor

        public GlobalGrepViewModel(IEnumerable<LogSessionData> loadedSessions)
        {
            _grepService = new GlobalGrepService();
            LoadedSessions = loadedSessions;
            Results = new ObservableRangeCollection<GrepResult>();

            // Default values
            SearchMode = SearchModeType.LoadedSessions;
            UseRegex = false;
            SearchMessage = true;
            SearchException = true;
            SearchMethod = true;
            SearchData = true;
            SearchPLC = true;
            SearchAPP = true;

            // Commands
            SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(), _ => CanExecuteSearch());
            CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);
            FindFirstOccurrenceCommand = new RelayCommand(_ => FindFirstOccurrence(), _ => Results.Any());
            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => Results.Any());
        }

        #endregion

        #region Properties

        private IEnumerable<LogSessionData> LoadedSessions { get; }

        private ObservableRangeCollection<GrepResult> _results;
        public ObservableRangeCollection<GrepResult> Results
        {
            get => _results;
            set
            {
                _results = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ResultCount));
            }
        }

        public int ResultCount => Results?.Count ?? 0;

        private string _searchQuery;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                }
            }
        }

        private SearchModeType _searchMode;
        public SearchModeType SearchMode
        {
            get => _searchMode;
            set
            {
                if (_searchMode != value)
                {
                    _searchMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLoadedSessionsMode));
                    OnPropertyChanged(nameof(IsExternalFilesMode));
                }
            }
        }

        public bool IsLoadedSessionsMode
        {
            get => SearchMode == SearchModeType.LoadedSessions;
            set { if (value) SearchMode = SearchModeType.LoadedSessions; }
        }

        public bool IsExternalFilesMode
        {
            get => SearchMode == SearchModeType.ExternalFiles;
            set { if (value) SearchMode = SearchModeType.ExternalFiles; }
        }

        private string _externalPath;
        public string ExternalPath
        {
            get => _externalPath;
            set
            {
                if (_externalPath != value)
                {
                    _externalPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _useRegex;
        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                if (_useRegex != value)
                {
                    _useRegex = value;
                    OnPropertyChanged();
                }
            }
        }

        // Field filters (for in-memory search)
        private bool _searchMessage;
        public bool SearchMessage
        {
            get => _searchMessage;
            set
            {
                if (_searchMessage != value)
                {
                    _searchMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _searchException;
        public bool SearchException
        {
            get => _searchException;
            set
            {
                if (_searchException != value)
                {
                    _searchException = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _searchMethod;
        public bool SearchMethod
        {
            get => _searchMethod;
            set
            {
                if (_searchMethod != value)
                {
                    _searchMethod = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _searchData;
        public bool SearchData
        {
            get => _searchData;
            set
            {
                if (_searchData != value)
                {
                    _searchData = value;
                    OnPropertyChanged();
                }
            }
        }

        // Log type filters
        private bool _searchPLC;
        public bool SearchPLC
        {
            get => _searchPLC;
            set
            {
                if (_searchPLC != value)
                {
                    _searchPLC = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _searchAPP;
        public bool SearchAPP
        {
            get => _searchAPP;
            set
            {
                if (_searchAPP != value)
                {
                    _searchAPP = value;
                    OnPropertyChanged();
                }
            }
        }

        // Search status
        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                if (_isSearching != value)
                {
                    _isSearching = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotSearching));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsNotSearching => !IsSearching;

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _progressCurrent;
        public int ProgressCurrent
        {
            get => _progressCurrent;
            set
            {
                if (_progressCurrent != value)
                {
                    _progressCurrent = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _progressTotal;
        public int ProgressTotal
        {
            get => _progressTotal;
            set
            {
                if (_progressTotal != value)
                {
                    _progressTotal = value;
                    OnPropertyChanged();
                }
            }
        }

        private GrepResult _selectedResult;
        public GrepResult SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (_selectedResult != value)
                {
                    _selectedResult = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand SearchCommand { get; }
        public ICommand CancelSearchCommand { get; }
        public ICommand FindFirstOccurrenceCommand { get; }
        public ICommand ClearResultsCommand { get; }

        private bool CanExecuteSearch()
        {
            if (IsSearching || string.IsNullOrWhiteSpace(SearchQuery))
                return false;

            if (SearchMode == SearchModeType.LoadedSessions)
            {
                return LoadedSessions?.Any() == true;
            }
            else
            {
                return !string.IsNullOrWhiteSpace(ExternalPath);
            }
        }

        private async Task ExecuteSearchAsync()
        {
            if (IsSearching)
                return;

            IsSearching = true;
            Results.Clear();
            ProgressCurrent = 0;
            ProgressTotal = 0;
            StatusMessage = "Preparing search...";

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var progress = new Progress<(int current, int total, string status)>(p =>
                {
                    ProgressCurrent = p.current;
                    ProgressTotal = p.total;
                    StatusMessage = p.status;
                });

                List<GrepResult> results;

                if (SearchMode == SearchModeType.LoadedSessions)
                {
                    results = await _grepService.SearchLoadedSessionsAsync(
                        LoadedSessions,
                        SearchQuery,
                        UseRegex,
                        SearchMessage,
                        SearchException,
                        SearchMethod,
                        SearchData,
                        progress,
                        _cancellationTokenSource.Token);
                }
                else
                {
                    results = await _grepService.SearchExternalFilesAsync(
                        ExternalPath,
                        SearchQuery,
                        UseRegex,
                        SearchPLC,
                        SearchAPP,
                        progress,
                        _cancellationTokenSource.Token);
                }

                // Update UI on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Results.ReplaceAll(results);
                    OnPropertyChanged(nameof(ResultCount));
                    StatusMessage = $"Search complete. Found {results.Count} result(s).";
                });
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
                MessageBox.Show($"Search error: {ex.Message}", "Global Grep Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void CancelSearch()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling search...";
        }

        private void FindFirstOccurrence()
        {
            if (!Results.Any())
                return;

            var firstResult = Results
                .Where(r => r.Timestamp.HasValue)
                .OrderBy(r => r.Timestamp.Value)
                .FirstOrDefault();

            if (firstResult != null)
            {
                SelectedResult = firstResult;
                StatusMessage = $"First occurrence: {firstResult.TimestampDisplay} in {firstResult.SessionName}";
            }
            else
            {
                StatusMessage = "No results with valid timestamps found.";
            }
        }

        private void ClearResults()
        {
            Results.Clear();
            OnPropertyChanged(nameof(ResultCount));
            StatusMessage = "Results cleared.";
            SelectedResult = null;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Enums

        public enum SearchModeType
        {
            LoadedSessions,
            ExternalFiles
        }

        #endregion
    }
}
