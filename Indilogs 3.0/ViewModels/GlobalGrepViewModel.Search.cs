#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Search Execution

        /// <summary>
        /// Flushes queued results from background threads to the UI ObservableCollection.
        /// Called by a timer every 150ms during search.
        /// </summary>
        private void FlushResultsToUI(object state)
        {
            if (_resultQueue.IsEmpty) return;

            var batch = new List<GrepResult>();
            while (_resultQueue.TryDequeue(out var result))
                batch.Add(result);

            if (batch.Count == 0) return;

            try
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    Results.AddRange(batch);
                    OnPropertyChanged(nameof(ResultCount));
                    StatusMessage = $"Searching... {Results.Count:N0} result(s) found so far";
                });
            }
            catch (TaskCanceledException) { }
        }

        private async Task ExecuteSearchAsync()
        {
            if (IsSearching) return;
            IsSearching = true;
            Results.Clear();
            OnPropertyChanged(nameof(ResultCount));
            StatusMessage = "Preparing search...";
            SearchDuration = "";

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Start flush timer — pushes queued results to UI every 150ms
            _flushTimer = new System.Threading.Timer(FlushResultsToUI, null, 150, 150);

            try
            {
                var progress = new Progress<(int current, int total, string status)>(p =>
                {
                    ProgressCurrent = p.current;
                    ProgressTotal = p.total;
                    // Don't overwrite "X results found so far" with location progress
                });

                var activeLocations = Locations.Where(l => l.IsActive).ToList();

                bool hasSearchText = !string.IsNullOrWhiteSpace(SearchQuery);
                bool hasConditions = ConditionGroups.Any(g => g.Conditions.Any(c => !string.IsNullOrWhiteSpace(c.Value)));
                bool hasLocations = activeLocations.Any();
                bool hasLoadedSessions = LoadedSessions?.Any() == true;

                string searchPattern = hasSearchText ? SearchQuery : "(no quick-search text)";
                string conditionsSummary = hasConditions ? BuildCriteriaSummary() : "(no structured conditions)";

                AppLogger.Info($"[Grep] ════════════════════════════════════════════════════");
                AppLogger.Info($"[Grep] SEARCH STARTED at {DateTime.Now:HH:mm:ss.fff}");
                AppLogger.Info($"[Grep] Pattern: \"{searchPattern}\"");
                AppLogger.Info($"[Grep] Field: {SelectedQuickSearchField}, Regex: {UseRegex}, PLC: {SearchPLC}, APP: {SearchAPP}");
                if (hasConditions)
                    AppLogger.Info($"[Grep] Conditions: {conditionsSummary}");
                AppLogger.Info($"[Grep] Locations configured: {activeLocations.Count}, Loaded sessions: {(LoadedSessions as System.Collections.ICollection)?.Count ?? 0}");

                if (!hasSearchText && !hasConditions)
                {
                    StatusMessage = "Please enter a search query or add search conditions.";
                    AppLogger.Warn("[Grep] SEARCH ABORTED — no search text and no conditions provided");
                    return;
                }

                SearchCriteria criteria;
                if (hasConditions)
                    criteria = BuildCriteria();
                else
                    criteria = BuildQuickSearchCriteria();

                // Streaming callback: enqueue results for batched UI flush
                Action<GrepResult> onResult = result =>
                {
                    _resultQueue.Enqueue(result);
                };

                if (hasLocations)
                {
                    AppLogger.Info($"[Grep] MODE: Multi-location search across {activeLocations.Count} location(s)");
                    foreach (var loc in activeLocations)
                        AppLogger.Info($"[Grep]   Location: \"{loc.Name}\" — {loc.BasePath}");
                    await _grepService.SearchMultiLocationAsync(
                        criteria, activeLocations, progress, _cancellationTokenSource.Token, onResult);
                }
                else if (hasLoadedSessions)
                {
                    var sessionsList = LoadedSessions.ToList();
                    AppLogger.Info($"[Grep] MODE: In-memory loaded sessions ({sessionsList.Count} session(s))");
                    foreach (var s in sessionsList)
                        AppLogger.Info($"[Grep]   Session: \"{s.FileName}\" — PLC:{s.Logs?.Count ?? 0} entries, APP:{s.AppDevLogs?.Count ?? 0} entries");
                    await _grepService.SearchLoadedSessionsWithCriteriaAsync(
                        LoadedSessions, criteria, progress, _cancellationTokenSource.Token, onResult);
                }
                else
                {
                    StatusMessage = "No search target available. Add a search location or load a log file in the main window first.";
                    AppLogger.Warn("[Grep] SEARCH ABORTED — no locations configured and no loaded sessions available");
                    return;
                }

                // Final flush — push any remaining queued results
                _flushTimer?.Dispose();
                _flushTimer = null;
                FlushResultsToUI(null);

                sw.Stop();
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    OnPropertyChanged(nameof(ResultCount));
                    SearchDuration = $"({sw.ElapsedMilliseconds:N0}ms)";

                    if (Results.Count > 0)
                    {
                        StatusMessage = $"Search complete. Found {Results.Count:N0} result(s).";
                        AppLogger.Info($"[Grep] SEARCH COMPLETE — {Results.Count:N0} result(s) found in {sw.ElapsedMilliseconds:N0}ms");
                    }
                    else
                    {
                        StatusMessage = $"Search complete. No results found for \"{searchPattern}\".";
                        AppLogger.Warn($"[Grep] SEARCH COMPLETE — 0 results for \"{searchPattern}\" in {sw.ElapsedMilliseconds:N0}ms");
                    }

                    var locationNames = hasLocations
                        ? activeLocations.Select(l => l.Name).ToList()
                        : LoadedSessions?.Select(s => System.IO.Path.GetFileName(s.FileName) ?? "Loaded session").ToList()
                          ?? new List<string>();
                    _lastSearchParams = new SearchReportParams
                    {
                        LocationNames = locationNames,
                        QueryText = SearchQuery,
                        CriteriaSummary = BuildCriteriaSummary(),
                        SearchDuration = $"{sw.ElapsedMilliseconds:N0}ms",
                        LogTypes = (SearchPLC && SearchAPP) ? "PLC + APP" : SearchPLC ? "PLC" : SearchAPP ? "APP" : "None",
                        FileTimeRange = FormatTimeRange(FileTimeFrom, FileTimeTo),
                        ResultTimeRange = FormatTimeRange(ResultTimeFrom, ResultTimeTo)
                    };
                });
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
                AppLogger.Info($"[Grep] SEARCH CANCELLED by user after {sw.ElapsedMilliseconds:N0}ms");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
                AppLogger.Error($"[Grep] SEARCH FAILED after {sw.ElapsedMilliseconds:N0}ms", ex);
            }
            finally
            {
                // Ensure timer is stopped
                _flushTimer?.Dispose();
                _flushTimer = null;
                // Final flush in case of cancellation/error
                FlushResultsToUI(null);
                IsSearching = false;
                AppLogger.Info($"[Grep] ════════════════════════════════════════════════════");
            }
        }

        private SearchCriteria BuildCriteria()
        {
            var criteria = new SearchCriteria
            {
                SearchPLC = SearchPLC,
                SearchAPP = SearchAPP,
                GroupOperator = SelectedGroupOperator,
                Groups = ConditionGroups.Select(g => new SearchConditionGroup
                {
                    Operator = g.Operator,
                    Conditions = g.Conditions
                        .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                        .Select(c => new SearchCondition
                        {
                            Field = c.Field,
                            Operator = c.Operator,
                            Value = c.Value,
                            Negate = c.Negate
                        }).ToList()
                }).Where(g => g.Conditions.Count > 0).ToList()
            };

            if (FileTimeFrom.HasValue || FileTimeTo.HasValue)
                criteria.FileTimeFilter = new TimeRangeFilter { From = FileTimeFrom, To = FileTimeTo };

            if (ResultTimeFrom.HasValue || ResultTimeTo.HasValue)
                criteria.ResultTimeFilter = new TimeRangeFilter { From = ResultTimeFrom, To = ResultTimeTo };

            return criteria;
        }

        private SearchCriteria BuildQuickSearchCriteria()
        {
            var criteria = new SearchCriteria
            {
                SearchPLC = SearchPLC,
                SearchAPP = SearchAPP,
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Operator = ConditionOperator.Or,
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition
                            {
                                Field = SelectedQuickSearchField,
                                Operator = UseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                                Value = SearchQuery
                            }
                        }
                    }
                }
            };

            if (FileTimeFrom.HasValue || FileTimeTo.HasValue)
                criteria.FileTimeFilter = new TimeRangeFilter { From = FileTimeFrom, To = FileTimeTo };
            if (ResultTimeFrom.HasValue || ResultTimeTo.HasValue)
                criteria.ResultTimeFilter = new TimeRangeFilter { From = ResultTimeFrom, To = ResultTimeTo };

            return criteria;
        }

        private void CancelSearch()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling search...";
        }

        private void ClearResults()
        {
            Results.Clear();
            OnPropertyChanged(nameof(ResultCount));
            StatusMessage = "Results cleared.";
            SearchDuration = "";
            SelectedResult = null;
        }

        private void FindFirstOccurrence()
        {
            var first = Results.Where(r => r.Timestamp.HasValue).OrderBy(r => r.Timestamp.Value).FirstOrDefault();
            if (first != null)
            {
                SelectedResult = first;
                StatusMessage = $"First occurrence: {first.TimestampDisplay} in {first.SessionName}";
            }
        }

        public List<(string FilePath, string SessionName)> GetUniqueFiles()
        {
            return Results
                .Where(r => !string.IsNullOrWhiteSpace(r.FilePath) && !string.IsNullOrWhiteSpace(r.SessionName))
                .Select(r => (r.FilePath, r.SessionName))
                .Distinct()
                .OrderBy(f => f.SessionName)
                .ToList();
        }

        #endregion
    }
}
