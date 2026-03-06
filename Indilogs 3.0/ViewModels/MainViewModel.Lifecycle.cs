using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using IndiLogs_3._0.ViewModels.Components;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        // ── Visual mode ──

        private void InitializeVisualMode()
        {
            // Use filtered logs if time range is active, otherwise use all logs
            var logsToUse = FilterVM.IsGlobalTimeRangeActive ? SessionVM.Logs : (SessionVM.AllLogsCache ?? SessionVM.Logs);
            if (VisualTimelineVM != null)
            {
                // S4-5 (binary APP): skip Events on timeline
                var eventsToShow = HasBinaryAppLogs ? null : SessionVM?.Events;
                VisualTimelineVM.LoadData(logsToUse.ToList(), eventsToShow);
            }
        }

        // ── Annotation logic ──

        private void ToggleAnnotation(object? parameter)
        {
            if (parameter is LogEntry log && log.HasAnnotation)
                log.IsAnnotationExpanded = !log.IsAnnotationExpanded;
        }

        private void ToggleAllAnnotations(object? obj)
        {
            IEnumerable<LogEntry>? targetList = null;

            if (SelectedTabIndex == AppConstants.TAB_APP)
                targetList = SessionVM?.AllAppLogsCache;
            else
                targetList = SessionVM?.AllLogsCache;

            if (targetList == null || !targetList.Any()) return;

            var logsWithAnnotations = targetList.Where(l => l.HasAnnotation).ToList();
            if (!logsWithAnnotations.Any()) return;

            bool anyExpanded = logsWithAnnotations.Any(l => l.IsAnnotationExpanded);
            bool newState = !anyExpanded;

            foreach (var log in logsWithAnnotations)
                log.IsAnnotationExpanded = newState;

            if (CaseVM != null) CaseVM.ShowAllAnnotations = newState;
            if (SessionVM != null)
                SessionVM.StatusMessage = newState ? "All annotations expanded" : "All annotations collapsed";
        }

        private void CloseAnnotation(object? parameter) => CaseVM?.CloseAnnotationCommand.Execute(parameter);

        // ── Live monitoring ──

        private void LiveClear(object? obj)
        {
            LiveVM.IsRunning = false;

            _dispatcher.Post(() =>
            {
                lock (_collectionLock)
                {
                    if (SessionVM.AllLogsCache != null) SessionVM.AllLogsCache.Clear();
                    FilterVM?.FilteredLogs?.Clear();
                    SelectedLog = null;
                }
            });

            if (LiveVM.IsLiveMode)
            {
                LiveVM.IsRunning = true;
                SessionVM.StatusMessage ="Cleared. Monitoring continues...";
            }
            else
            {
                SessionVM.StatusMessage ="Logs cleared.";
            }
        }

        private void ClearLogs(object? obj)
        {
            try
            {
                SessionVM?.ClearCommand.Execute(null);

                CaseVM?.ClearMarkedLogs();
                CaseVM?.LogAnnotations?.Clear();

                if (FilterVM != null)
                {
                    FilterVM.IsMainFilterActive = false;
                    FilterVM.IsAppFilterActive = false;
                    FilterVM.IsMainFilterOutActive = false;
                    FilterVM.IsAppFilterOutActive = false;
                    FilterVM.IsAppTimeFocusActive = false;
                    FilterVM.IsTimeFocusActive = false;

                    FilterVM.LastFilteredAppCache?.Clear();
                    FilterVM.LastFilteredCache?.Clear();
                    FilterVM.NegativeFilters?.Clear();
                    FilterVM.ActiveThreadFilters?.Clear();
                    FilterVM.ActiveLoggerFilters?.Clear();
                    FilterVM.ActiveMethodFilters?.Clear();

                    if (FilterVM.LoggerTreeRoot != null)
                    {
                        FilterVM.LoggerTreeRoot.Clear();
                    }
                    if (FilterVM.PlcLoggerTreeRoot != null)
                    {
                        FilterVM.PlcLoggerTreeRoot.Clear();
                        OnPropertyChanged(nameof(ActiveLoggerTree));
                    }

                    FilterVM.SearchText = "";
                    FilterVM.IsSearchPanelVisible = false;
                }

                if (SessionVM != null)
                    SessionVM.Logs = new List<LogEntry>();

                ConfigVM?.ClearConfigurationFiles();

                SetupInfo = "";
                OnPropertyChanged(nameof(SetupInfo));
                PressConfig = "";
                OnPropertyChanged(nameof(PressConfig));
                VersionsInfo = "";
                OnPropertyChanged(nameof(VersionsInfo));
                WindowTitle = "IndiLogs 3.0";
                OnPropertyChanged(nameof(WindowTitle));

                if (SessionVM != null)
                {
                    SessionVM.CurrentProgress = 0;
                    SessionVM.SelectedSession = null;
                }
                ScreenshotZoom = 400;
                SelectedLog = null;
                IsFilterOutActive = false;
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(IsFilterOutActive));

                ResetTreeFilters();

                if (VisualTimelineVM != null)
                    VisualTimelineVM.Clear();
                _isVisualMode = false;
                OnPropertyChanged(nameof(IsVisualMode));

                SelectedTabIndex = AppConstants.TAB_PLC;
                OnPropertyChanged(nameof(SelectedTabIndex));
                LeftTabIndex = 0;
                OnPropertyChanged(nameof(LeftTabIndex));

                if (SessionVM != null) SessionVM.StatusMessage ="All data cleared successfully";
            }
            catch (Exception ex)
            {
                SessionVM.StatusMessage =$"Clear failed: {ex.Message}";
            }
        }

        private void StartLiveMonitoring(string path) => LiveVM?.StartLiveMonitoring(path);
        private void StopLiveMonitoring() => LiveVM?.StopLiveMonitoring();
        private void LivePlay(object? obj) => LiveVM?.LivePlayCommand.Execute(obj);
        private void LivePause(object? obj) => LiveVM?.LivePauseCommand.Execute(obj);

        // ── Configuration management ──

        private void LoadSavedConfigurations()
        {
            string path = AppPaths.Root;
            CaseVM?.EnsureDefaultConfigsOnDisk(path);
        }

        private void LoadUserDefaults()
        {
            _defaultConfigService.Load();
            var defaults = _defaultConfigService.CurrentDefaults;
            if (defaults == null) return;

            if (defaults.HasCustomMainColoring && defaults.MainDefaultColoringRules != null)
                _coloringService.UserDefaultMainRules = defaults.MainDefaultColoringRules;
            if (defaults.HasCustomAppColoring && defaults.AppDefaultColoringRules != null)
                _coloringService.UserDefaultAppRules = defaults.AppDefaultColoringRules;
            if (defaults.HasCustomPlcFilter && defaults.PlcFilteredDefaultFilter != null)
                FilterVM.DefaultPlcFilter = defaults.PlcFilteredDefaultFilter;
        }

        private void SetCurrentAsDefault(object? obj)
        {
            var config = new Models.DefaultConfiguration();

            if (FilterVM.DefaultPlcFilter != null)
            {
                config.PlcFilteredDefaultFilter = FilterVM.DefaultPlcFilter.DeepClone();
                config.HasCustomPlcFilter = true;
            }

            if (CaseVM?.MainColoringRules != null && CaseVM.MainColoringRules.Count > 0)
            {
                config.MainDefaultColoringRules = CaseVM.MainColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomMainColoring = true;
            }

            if (CaseVM?.AppColoringRules != null && CaseVM.AppColoringRules.Count > 0)
            {
                config.AppDefaultColoringRules = CaseVM.AppColoringRules.Select(r => r.Clone()).ToList();
                config.HasCustomAppColoring = true;
            }

            _defaultConfigService.Save(config);

            _coloringService.UserDefaultMainRules = config.MainDefaultColoringRules;
            _coloringService.UserDefaultAppRules = config.AppDefaultColoringRules;

            SessionVM.StatusMessage = "Current settings saved as defaults.";
        }

        private void ResetDefaults(object? obj)
        {
            _defaultConfigService.Reset();
            _coloringService.UserDefaultMainRules = null;
            _coloringService.UserDefaultAppRules = null;
            FilterVM.DefaultPlcFilter = null;

            SessionVM.StatusMessage = "Defaults reset to factory settings.";
        }

        private void ApplyConfiguration(object? parameter) { if (parameter is SavedConfiguration c) CaseVM?.ApplyConfiguration(c); }
        private void RemoveConfiguration(object? parameter) => CaseVM?.DeleteConfigCommand.Execute(parameter);
        private void SaveConfiguration(object? obj) => CaseVM?.SaveConfigCommand.Execute(obj);
        private void LoadConfigurationFromFile(object? obj) => CaseVM?.LoadConfigCommand.Execute(obj);

        // ── Case management delegates ──

        private void MarkRow(object? obj) => CaseVM?.MarkLogCommand.Execute(obj);
        private void GoToNextMarked(object? obj) => CaseVM?.GoToNextMarkedCommand.Execute(obj);
        private void GoToPrevMarked(object? obj) => CaseVM?.GoToPrevMarkedCommand.Execute(obj);
        private void JumpToLog(object? obj) { if (obj is LogEntry log) { SelectedLog = log; RequestScrollToLog?.Invoke(log); } }

        public LogAnnotation? GetAnnotation(LogEntry log) => CaseVM?.GetAnnotation(log);
        private void AddAnnotation(object? parameter) => CaseVM?.AddAnnotationCommand.Execute(parameter);
        private void DeleteAnnotation(object? parameter) => CaseVM?.DeleteAnnotationCommand.Execute(parameter);
        private void SaveCase(object? parameter) => CaseVM?.SaveCaseCommand.Execute(parameter);
        private void LoadCase(object? parameter) => CaseVM?.LoadCaseCommand.Execute(parameter);

        // ── Clipboard ──

        private void CopyTableName(object? parameter)
        {
            if (parameter is DbTreeNode node && !string.IsNullOrEmpty(node.Name))
            {
                try
                {
                    System.Windows.Clipboard.SetText(node.Name);
                }
                catch (Exception ex) { AppLogger.Error("Clipboard copy failed", ex); }
            }
        }

        // ── Session changes ──

        private async Task OnSelectedSessionChangedAsync(LogSessionData? session)
        {
            if (session != null && session.HasBinaryAppLogs &&
                !string.IsNullOrEmpty(session.FilePath) && File.Exists(session.FilePath))
                await StepRecorderVM.LoadFromZipAsync(session.FilePath);
            else
                StepRecorderVM.Clear();
        }

        // ── Child VM PropertyChanged handlers ──

        private void SessionVM_PropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SessionVM.SelectedSession):
                    OnPropertyChanged(nameof(PlcTabHeader));
                    OnPropertyChanged(nameof(HasBinaryAppLogs));
                    OnPropertyChanged(nameof(IsPrintAnalysisVisible));
                    OnPropertyChanged(nameof(ReportsButtonText));
                    OnPropertyChanged(nameof(ShowMainTabs));
                    OnPropertyChanged(nameof(HasExternalFileOnly));
                    _ = OnSelectedSessionChangedAsync(SessionVM.SelectedSession);
                    break;
            }
        }

        private void FilterVM_PropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FilterVM.LoggerTreeRoot):
                case nameof(FilterVM.PlcLoggerTreeRoot):
                    OnPropertyChanged(nameof(ActiveLoggerTree));
                    break;
                case nameof(FilterVM.IsMainFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsAppFilterActive):
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.IsMainFilterOutActive):
                case nameof(FilterVM.IsAppFilterOutActive):
                    OnPropertyChanged(nameof(IsFilterOutActive));
                    OnPropertyChanged(nameof(ActiveFilters));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    break;
                case nameof(FilterVM.HasRangeStart):
                    OnPropertyChanged(nameof(HasRangeStart));
                    break;
            }
        }

        private void LiveVM_PropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            // LiveVM properties (IsLiveMode, IsRunning, IsPaused) bind directly via LiveVM.* in XAML
        }

        private void DifferentLogsVM_PropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DifferentLogsVM.HasFile))
            {
                OnPropertyChanged(nameof(ShowMainTabs));
                OnPropertyChanged(nameof(HasExternalFileOnly));
            }
        }

        // ── Dispose ──

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (SessionVM != null) SessionVM.PropertyChanged -= SessionVM_PropertyChanged;
                if (FilterVM != null) FilterVM.PropertyChanged -= FilterVM_PropertyChanged;
                if (LiveVM != null) LiveVM.PropertyChanged -= LiveVM_PropertyChanged;
                if (DifferentLogsVM != null) DifferentLogsVM.PropertyChanged -= DifferentLogsVM_PropertyChanged;
            }
            base.Dispose(disposing);
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}
