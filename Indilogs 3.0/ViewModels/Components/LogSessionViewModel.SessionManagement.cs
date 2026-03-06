using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
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
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class LogSessionViewModel
    {
        private void ClearLogs(object? obj)
        {
            // Clear logs
            if (_allLogsCache != null) _allLogsCache.Clear();
            if (_allAppLogsCache != null) _allAppLogsCache.Clear();

            Logs = new List<LogEntry>();
            OnPropertyChanged(nameof(Logs));

            // Clear events
            if (_events != null)
            {
                _events.Clear();
                OnPropertyChanged(nameof(Events));
            }

            // Clear screenshots
            if (_screenshots != null)
            {
                _screenshots.Clear();
                OnPropertyChanged(nameof(Screenshots));
            }

            // Clear files
            if (_loadedFiles != null)
            {
                _loadedFiles.Clear();
                OnPropertyChanged(nameof(LoadedFiles));
            }

            // Clear sessions
            if (_loadedSessions != null)
            {
                _loadedSessions.Clear();
                OnPropertyChanged(nameof(LoadedSessions));
            }

            SelectedSession = null;
            CurrentProgress = 0;

            // Clear chart state
            _parent.ChartVM?.RestoreChartState(null);

            // Clear text info properties in parent
            _parent.SetupInfo = "";
            _parent.PressConfig = "";
            _parent.VersionsInfo = "";
            _parent.WindowTitle = "IndiLogs 3.0";

            // Clear FilterVM collections directly
            if (_filterVM != null)
            {
                // Clear ALL filters (thread, logger, method, time focus, search, negative, etc.)
                _filterVM.ClearFilters();

                if (_filterVM.FilteredLogs != null)
                {
                    _filterVM.FilteredLogs.Clear();
                }

                if (_filterVM.AppDevLogsFiltered != null)
                {
                    _filterVM.AppDevLogsFiltered.Clear();
                }

                if (_filterVM.LoggerTreeRoot != null)
                {
                    _filterVM.LoggerTreeRoot.Clear();
                }
                if (_filterVM.PlcLoggerTreeRoot != null)
                {
                    _filterVM.PlcLoggerTreeRoot.Clear();
                    _parent.NotifyPropertyChanged(nameof(_parent.ActiveLoggerTree));
                }

                // Notify ACTIVE FILTERS panel to update
                _parent.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
                _parent.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
            }

            // Clear ConfigVM collections directly
            if (_configVM != null)
            {
                _configVM.ClearConfigurationFiles();
            }

            StatusMessage = "Logs cleared";
        }

        private void RemoveSession(object? obj)
        {
            if (obj is LogSessionData session && _loadedSessions != null && _loadedSessions.Contains(session))
            {
                bool wasSelected = (session == _selectedSession);
                _loadedSessions.Remove(session);
                OnPropertyChanged(nameof(LoadedSessions));

                if (wasSelected)
                {
                    // If removed session was selected, switch to the first remaining session or clear
                    if (_loadedSessions.Count > 0)
                    {
                        SelectedSession = _loadedSessions[0];
                    }
                    else
                    {
                        SelectedSession = null;
                        // Clear everything since no sessions remain
                        ClearLogs(null);
                    }
                }
            }
        }

        private void SwitchToSession(LogSessionData session)
        {
            if (session == null) return;
            IsBusy = true;

            AllLogsCache = session.Logs;
            Logs = session.Logs;
            _parent.CurrentPluginColumns = session.PluginColumns;

            // Load configuration and database files through ConfigVM
            _configVM.LoadConfigurationFiles();
            _parent.NotifyPropertyChanged(nameof(_parent.DbConfigTabHeader));
            _parent.NotifyPropertyChanged(nameof(_parent.HasBinaryAppLogs));
            _parent.LoadGlobalsFiles();

            // Restore tab visibility from the session's saved selection
            _parent.ApplyTabSelection(session.LoadTabSelection, session.PreScanConfig);
            _parent.UpdateTabVisibilityAfterLoad();

            // Update Events and Screenshots — single collection replacement instead of per-item Add
            Events = new ObservableCollection<EventEntry>(session.Events);
            _parent.LoadEventsDataView();

            Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots);

            _caseVM.MarkedLogs = session.MarkedLogs;
            _parent.SetupInfo = session.SetupInfo;
            _parent.PressConfig = session.PressConfiguration;

            if (!string.IsNullOrEmpty(session.VersionsInfo))
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                _parent.WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            AllAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();
            // Note: Parsing already done in LogFileService when case logs were loaded or when saving case

            _filterVM.BuildLoggerTree(AllAppLogsCache);
            _filterVM.BuildPlcLoggerTree(AllLogsCache);

            // Don't reset search/filters when loading a case - will be restored by ApplyCaseSettings
            if (!_caseVM.IsLoadingCase)
            {
                // Reload saved configs filtered by session type (show only the matching default)
                _caseVM.LoadSavedConfigs(session.HasBinaryAppLogs);

                // Try to restore previously saved filter state for this session
                if (!RestoreFilterState(session))
                {
                    // First time opening this session — start with no filters
                    _filterVM.SearchText = "";
                    _filterVM.IsTimeFocusActive = false;
                    _filterVM.IsAppTimeFocusActive = false;

                    _filterVM.NegativeFilters.Clear();
                    _filterVM.ActiveThreadFilters.Clear();

                    _filterVM.MainFilterRoot = null;
                    _filterVM.AppFilterRoot = null;
                    _filterVM.LastFilteredAppCache = null;
                    _filterVM.LastFilteredCache = null;

                    _parent.ResetTreeFilters();

                    _filterVM.IsMainFilterActive = false;
                    _filterVM.IsAppFilterActive = false;
                    _filterVM.IsMainFilterOutActive = false;
                    _filterVM.IsAppFilterOutActive = false;

                    _filterVM.ApplyAppLogsFilter();
                }
            }
            else
            {
                _filterVM.ApplyAppLogsFilter();
            }

            // Restore chart state for this session (or clear if no saved state)
            _parent.ChartVM?.RestoreChartState(session.SavedChartState);

            // Restore per-session time sync offset
            if (session.TimeSyncOffset.HasValue)
            {
                _parent.TimeSyncOffsetSeconds = session.TimeSyncOffset.Value.TotalSeconds;
                _parent.HasTimeSyncData = session.HasTimeSyncData;
                _parent.ShowSyncedTimeColumn = session.HasTimeSyncData;
            }
            else
            {
                _parent.TimeSyncOffsetSeconds = 0;
                _parent.HasTimeSyncData = false;
                _parent.ShowSyncedTimeColumn = false;
            }

            IsBusy = false;
        }

        /// <summary>
        /// Saves the current filter state into the session so it persists across switches.
        /// </summary>
        private void SaveFilterState(LogSessionData? session)
        {
            if (session == null) return;

            session.SavedFilterState = new SessionFilterState
            {
                MainFilterRoot = _filterVM.MainFilterRoot?.DeepClone(),
                AppFilterRoot = _filterVM.AppFilterRoot?.DeepClone(),
                IsMainFilterActive = _filterVM.IsMainFilterActive,
                IsAppFilterActive = _filterVM.IsAppFilterActive,
                IsMainFilterOutActive = _filterVM.IsMainFilterOutActive,
                IsAppFilterOutActive = _filterVM.IsAppFilterOutActive,
                IsTimeFocusActive = _filterVM.IsTimeFocusActive,
                IsAppTimeFocusActive = _filterVM.IsAppTimeFocusActive,
                NegativeFilters = _filterVM.NegativeFilters.ToList(),
                ActiveThreadFilters = _filterVM.ActiveThreadFilters.ToList(),
                SearchText = _filterVM.SearchText,
                LastFilteredCache = _filterVM.LastFilteredCache,
                LastFilteredAppCache = _filterVM.LastFilteredAppCache
            };

            // Save chart state for this session
            session.SavedChartState = _parent.ChartVM?.SaveChartState();
        }

        /// <summary>
        /// Restores a previously saved filter state for the session.
        /// Returns true if state was restored, false if no saved state exists.
        /// </summary>
        private bool RestoreFilterState(LogSessionData session)
        {
            var state = session?.SavedFilterState;
            if (state == null) return false;

            _filterVM.SearchText = state.SearchText ?? "";
            _filterVM.IsTimeFocusActive = state.IsTimeFocusActive;
            _filterVM.IsAppTimeFocusActive = state.IsAppTimeFocusActive;

            _filterVM.NegativeFilters.Clear();
            foreach (var nf in state.NegativeFilters) _filterVM.NegativeFilters.Add(nf);

            _filterVM.ActiveThreadFilters.Clear();
            foreach (var tf in state.ActiveThreadFilters) _filterVM.ActiveThreadFilters.Add(tf);

            _filterVM.MainFilterRoot = state.MainFilterRoot?.DeepClone();
            _filterVM.AppFilterRoot = state.AppFilterRoot?.DeepClone();
            _filterVM.LastFilteredCache = state.LastFilteredCache;
            _filterVM.LastFilteredAppCache = state.LastFilteredAppCache;

            _filterVM.IsMainFilterActive = state.IsMainFilterActive;
            _filterVM.IsAppFilterActive = state.IsAppFilterActive;
            _filterVM.IsMainFilterOutActive = state.IsMainFilterOutActive;
            _filterVM.IsAppFilterOutActive = state.IsAppFilterOutActive;

            _parent.NotifyPropertyChanged(nameof(_parent.IsFilterActive));
            _parent.NotifyPropertyChanged(nameof(_parent.IsFilterOutActive));

            _filterVM.ApplyMainLogsFilter();
            _filterVM.ApplyAppLogsFilter();

            return true;
        }
    }
}
