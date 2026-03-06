using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class LiveMonitoringViewModel
    {
        private bool ShouldShowInFilteredView(LogEntry log)
        {
            // 1. Check Negative Filters (Filter Out) - always active if defined
            if (_filterVM.IsMainFilterOutActive && _filterVM.NegativeFilters.Any())
            {
                foreach (var f in _filterVM.NegativeFilters)
                {
                    if (f.StartsWith("THREAD:"))
                    {
                        if (log.ThreadName != null && log.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                    else
                    {
                        if (log.Message != null && log.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                    }
                }
            }

            // 2. Are there active filters? (search, trees, Threads)
            bool hasSearch = !string.IsNullOrWhiteSpace(_filterVM.SearchText);
            bool hasActiveFilter = _filterVM.IsMainFilterActive || hasSearch || _filterVM.ActiveThreadFilters.Any();

            // 3. If no filters active -> use default PLC filter (same as regular file loading)
            if (!hasActiveFilter)
            {
                return _filterVM.IsDefaultLog(log);
            }

            // 4. Check active filters

            // Thread Filter
            if (_filterVM.ActiveThreadFilters.Any())
            {
                if (log.ThreadName == null || !_filterVM.ActiveThreadFilters.Contains(log.ThreadName)) return false;
            }

            // Search Text
            if (hasSearch)
            {
                if (log.Message == null || _filterVM.SearchText == null || log.Message.IndexOf(_filterVM.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Advanced Tree / Condition Filter
            if (_filterVM.MainFilterRoot != null && _filterVM.MainFilterRoot.Children != null && _filterVM.MainFilterRoot.Children.Count > 0)
            {
                if (!_filterVM.EvaluateFilterNode(log, _filterVM.MainFilterRoot)) return false;
            }

            return true;
        }

        /// <summary>
        /// Incrementally reads new bytes from the live file, parses new log entries, and updates the UI.
        /// </summary>
        public async Task RefreshLogsOptimized()
        {
            if (_isRefreshActive) return;
            _isRefreshActive = true;

            try
            {
                long currentFileSize;
                try
                {
                    var fileInfo = new FileInfo(_liveFilePath!);
                    currentFileSize = fileInfo.Length;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Cannot access file", ex);
                    return;
                }

                bool isFirstRun = _cachedStream == null;

                // Skip if file hasn't grown since last check
                if (!isFirstRun && currentFileSize <= _lastFileSize)
                {
                    return;
                }

                long deltaBytes = isFirstRun ? currentFileSize : (currentFileSize - _lastFileSize);
                var sw = Stopwatch.StartNew();

                // ── Build/update local cache ──────────────────────────────────
                // IndigoLogsReader binary format REQUIRES reading from position 0.
                // Re-reading the entire file (85 MB+) from a network share every 5 s is too slow.
                // Instead we keep a local MemoryStream and only fetch the NEW bytes each poll.
                bool cacheUpdated = false;
                await Task.Run(() =>
                {
                    try
                    {
                        using (var fs = new FileStream(_liveFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144))
                        {
                            if (isFirstRun)
                            {
                                // First run: read the entire file into a local MemoryStream
                                _cachedStream = new MemoryStream((int)fs.Length + 1024 * 1024);
                                fs.CopyTo(_cachedStream);
                                cacheUpdated = true;
                            }
                            else
                            {
                                // Incremental: read only the new bytes and append to cache
                                long oldSize = _lastFileSize;
                                if (fs.Length > oldSize)
                                {
                                    fs.Seek(oldSize, SeekOrigin.Begin);
                                    _cachedStream!.Seek(0, SeekOrigin.End);
                                    fs.CopyTo(_cachedStream);
                                    cacheUpdated = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Cache update error", ex);
                    }
                });

                if (!cacheUpdated)
                {
                    AppLogger.Info("Cache not updated (network error or no new data)");
                    return;
                }

                _lastFileSize = currentFileSize;
                long cacheMs = sw.ElapsedMilliseconds;

                // ── Parse the cached stream ───────────────────────────────────
                if (_cachedStream == null || _cachedStream.Length == 0) return;

                List<LogEntry>? newLogs = null;
                int totalParsed = 0;

                await Task.Run(() =>
                {
                    try
                    {
                        _cachedStream.Position = 0;

                        if (isFirstRun)
                        {
                            // First run: parse everything (no entries to skip)
                            var allLogs = _logService.ParseLogStreamPartial(_cachedStream);
                            totalParsed = allLogs.Count;
                            newLogs = allLogs;
                            _lastParsedLogCount = totalParsed;
                        }
                        else
                        {
                            // Incremental: skip already-seen entries, only create LogEntry for new ones
                            var result = _logService.ParseLogStreamSkipExisting(_cachedStream, _lastParsedLogCount);
                            totalParsed = result.TotalCount;
                            newLogs = result.NewEntries;
                            _lastParsedLogCount = totalParsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Parse error", ex);
                        newLogs = new List<LogEntry>();
                    }
                });

                long parseMs = sw.ElapsedMilliseconds - cacheMs;
                AppLogger.Info($"Cache: {cacheMs}ms ({deltaBytes:N0} bytes) | Parse: {parseMs}ms (total={totalParsed:N0}, new={newLogs?.Count ?? 0})");

                // ── Update UI ─────────────────────────────────────────────────
                if (newLogs != null && newLogs.Count > 0)
                {
                    // Apply coloring in background (fire-and-forget)
                    var logsForColoring = newLogs;
                    _ = ApplyColoringInBackgroundAsync(logsForColoring);

                    var logsToAdd = newLogs;
                    var wasFirstRun = isFirstRun;

                    _dispatcher.Post(() =>
                    {
                        lock (_collectionLock)
                        {
                            try
                            {
                                _liveLogsCollection?.AddRange(logsToAdd);

                                // Also update filtered view
                                var filteredToAdd = logsToAdd.Where(l => ShouldShowInFilteredView(l)).ToList();
                                if (filteredToAdd.Count > 0 && _filterVM.FilteredLogs != null)
                                {
                                    _filterVM.FilteredLogs.AddRange(filteredToAdd);
                                }

                                if (wasFirstRun)
                                    _sessionVM.StatusMessage = $"Live: Loaded {_liveLogsCollection?.Count ?? 0:N0} logs (watching for new data...)";
                                else
                                    _sessionVM.StatusMessage = $"Live: +{logsToAdd.Count:N0} new (Total: {_liveLogsCollection?.Count ?? 0:N0})";

                                // Auto-scroll to bottom so user sees new entries
                                _parent.ScrollTabToBottom("PLC");
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Error("UI update error", ex);
                            }
                        }
                    }, DispatchPriority.DataBind);
                }
                else if (isFirstRun)
                {
                    _dispatcher.Post(() =>
                    {
                        _sessionVM.StatusMessage = "Live: File loaded but 0 logs parsed. Watching for new data...";
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("RefreshLogsOptimized error", ex);
            }
            finally
            {
                _isRefreshActive = false;
            }
        }

        private async Task ApplyColoringInBackgroundAsync(List<LogEntry> logs)
        {
            try
            {
                await _coloringService.ApplyDefaultColorsAsync(logs, false).ConfigureAwait(false);
                if (_caseVM.MainColoringRules != null && _caseVM.MainColoringRules.Any())
                    await _coloringService.ApplyCustomColoringAsync(logs, _caseVM.MainColoringRules).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Error("Coloring failed", ex); }
        }

        /// <summary>
        /// Main polling loop that periodically calls RefreshLogsOptimized until cancelled.
        /// </summary>
        public async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsRunning && !string.IsNullOrEmpty(_liveFilePath))
                    {
                        await RefreshLogsOptimized();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Polling error", ex);
                }

                try
                {
                    await Task.Delay(POLLING_INTERVAL_MS, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Starts auto-refresh polling for a statically-loaded .file.
        /// Called after ProcessFiles completes the initial static load.
        /// Pre-fills local cache from the existing session data (no extra network read).
        /// </summary>
        public void StartFileWatcher(string filePath, LogSessionData session)
        {
            // Don't start if already in live mode
            if (IsLiveMode) return;

            // Set IsLiveMode FIRST to prevent filter operations from overwriting Logs
            IsLiveMode = true;

            _liveFilePath = filePath;
            _liveSession = session;

            // Create an observable collection that wraps the existing session logs
            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            if (session.Logs != null && session.Logs.Count > 0)
            {
                _liveLogsCollection.AddRange(session.Logs);
            }

            // Point UI to the live collection
            _sessionVM.AllLogsCache = _liveLogsCollection;
            _sessionVM.Logs = _liveLogsCollection;

            _lastParsedLogCount = _liveLogsCollection.Count;

            IsRunning = true;
            _parent.WindowTitle = $"IndiLogs 3.0 - AUTO-REFRESH: {Path.GetFileName(filePath)}";

            // Pre-fill local cache in background to avoid blocking the UI thread.
            // The initial file read from network can take 20-30 seconds for large files.
            _liveCts = new CancellationTokenSource();
            _ = PreFillCacheAndStartPollingAsync(filePath, _liveCts.Token);

            _sessionVM.StatusMessage = $"Auto-refresh active ({_liveLogsCollection.Count:N0} logs loaded, watching for new data...)";
        }

        /// <summary>
        /// Stops live monitoring and releases all resources.
        /// </summary>
        public void Cleanup()
        {
            StopLiveMonitoring();
        }

        private async Task PreFillCacheAndStartPollingAsync(string filePath, CancellationToken token)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144))
                {
                    _cachedStream = new MemoryStream((int)fs.Length + 1024 * 1024);
                    await fs.CopyToAsync(_cachedStream, token).ConfigureAwait(false);
                    _lastFileSize = fs.Length;
                }
                AppLogger.Info($"Cache pre-filled: {_lastFileSize:N0} bytes in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cache pre-fill error", ex);
                _lastFileSize = 0;
                // Cache will be built on first poll cycle instead
            }

            // Start polling after cache is ready
            await PollingLoop(token).ConfigureAwait(false);
        }
    }
}
