using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// In-app scheduler that checks every 60 seconds for scheduled searches and executes them.
    /// Results are saved to the configured output directory as JSON and CSV.
    /// </summary>
    public class SearchSchedulerService : IDisposable
    {
        private static readonly string ScheduleFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "IndiLogs3.0", "Configs", "search_schedules.json");

        private readonly GlobalGrepService _grepService;
        private readonly SearchLocationService _locationService;
        private readonly EmailNotificationService _emailService;
        private readonly System.Timers.Timer _timer;
        private volatile bool _isRunning;

        public List<ScheduledSearch> Schedules { get; private set; } = new List<ScheduledSearch>();

        /// <summary>
        /// Raised when a scheduled search starts.
        /// </summary>
        public event Action<ScheduledSearch> SearchStarted;

        /// <summary>
        /// Raised when a scheduled search completes (or fails).
        /// </summary>
        public event Action<ScheduledSearch, int, string> SearchCompleted;

        public SearchSchedulerService(GlobalGrepService grepService, SearchLocationService locationService)
        {
            _grepService = grepService;
            _locationService = locationService;
            _emailService = new EmailNotificationService();
            LoadSchedules();

            _timer = new System.Timers.Timer(60_000); // Check every 60 seconds
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            _timer.Start();
            AppLogger.Info("[Scheduler] Search scheduler started");
        }

        public void Stop()
        {
            _timer.Stop();
            AppLogger.Info("[Scheduler] Search scheduler stopped");
        }

        public void AddSchedule(ScheduledSearch schedule)
        {
            Schedules.Add(schedule);
            SaveSchedules();
        }

        public void RemoveSchedule(Guid id)
        {
            Schedules.RemoveAll(s => s.Id == id);
            SaveSchedules();
        }

        public void UpdateSchedule(ScheduledSearch schedule)
        {
            var idx = Schedules.FindIndex(s => s.Id == schedule.Id);
            if (idx >= 0) Schedules[idx] = schedule;
            SaveSchedules();
        }

        /// <summary>
        /// Manually run a scheduled search now.
        /// Returns the HTML report path if generated, null otherwise.
        /// </summary>
        public async Task<string> RunNowAsync(ScheduledSearch schedule, CancellationToken ct = default)
        {
            return await ExecuteScheduledSearchAsync(schedule, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Immediately checks all enabled schedules and runs any that are due.
        /// Called after Add/Edit to avoid waiting for the 60-second timer.
        /// </summary>
        public async Task TriggerCheckAsync()
        {
            if (_isRunning)
            {
                AppLogger.Info("[Scheduler] TriggerCheck skipped — a scan is already running");
                return;
            }
            _isRunning = true;
            try
            {
                var now = DateTime.Now;
                AppLogger.Info($"[Scheduler] TriggerCheck: evaluating {Schedules.Count(s => s.IsEnabled)} enabled schedule(s)");
                foreach (var schedule in Schedules.Where(s => s.IsEnabled))
                {
                    if (ShouldRun(schedule, now))
                    {
                        await ExecuteScheduledSearchAsync(schedule, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[Scheduler] TriggerCheck error", ex);
            }
            finally
            {
                _isRunning = false;
            }
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e) => _ = OnTimerElapsedAsync();

        private async Task OnTimerElapsedAsync()
        {
            if (_isRunning)
            {
                AppLogger.Info("[Scheduler] Timer tick — skipped (scan already running)");
                return;
            }
            _isRunning = true;

            try
            {
                var now = DateTime.Now;
                var enabled = Schedules.Where(s => s.IsEnabled).ToList();
                if (enabled.Count > 0)
                    AppLogger.Info($"[Scheduler] Timer tick — checking {enabled.Count} schedule(s) at {now:HH:mm:ss}");

                foreach (var schedule in enabled)
                {
                    if (ShouldRun(schedule, now))
                    {
                        await ExecuteScheduledSearchAsync(schedule, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[Scheduler] Timer tick error", ex);
            }
            finally
            {
                _isRunning = false;
            }
        }

        internal bool ShouldRun(ScheduledSearch schedule, DateTime now)
        {
            if (!schedule.IsEnabled) return false;

            bool result;
            string reason;

            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once:
                    if (schedule.LastRunTime != null) { reason = "already ran"; result = false; break; }
                    if (schedule.RunDate.HasValue)
                    {
                        var target = schedule.RunDate.Value.Date.Add(schedule.RunTime);
                        result = now >= target;
                        reason = result ? "target time reached" : $"waiting for {target:yyyy-MM-dd HH:mm}";
                    }
                    else
                    {
                        result = now.TimeOfDay >= schedule.RunTime;
                        reason = result ? "run time reached" : $"waiting for {schedule.RunTime:hh\\:mm}";
                    }
                    break;

                case ScheduleType.Daily:
                    if (now.TimeOfDay < schedule.RunTime)
                    {
                        reason = $"waiting for {schedule.RunTime:hh\\:mm}";
                        result = false;
                    }
                    else if (schedule.LastRunTime != null && schedule.LastRunTime.Value.Date >= now.Date)
                    {
                        reason = "already ran today";
                        result = false;
                    }
                    else
                    {
                        reason = "daily run time reached";
                        result = true;
                    }
                    break;

                case ScheduleType.Weekly:
                    if (!schedule.RunDays.Contains(now.DayOfWeek))
                    {
                        reason = $"today ({now.DayOfWeek}) not in scheduled days";
                        result = false;
                    }
                    else if (now.TimeOfDay < schedule.RunTime)
                    {
                        reason = $"waiting for {schedule.RunTime:hh\\:mm}";
                        result = false;
                    }
                    else if (schedule.LastRunTime != null && schedule.LastRunTime.Value.Date >= now.Date)
                    {
                        reason = "already ran today";
                        result = false;
                    }
                    else
                    {
                        reason = "weekly run time reached";
                        result = true;
                    }
                    break;

                case ScheduleType.Interval:
                    if (schedule.LastRunTime == null)
                    {
                        // First run: respect start time if set
                        if (schedule.RunTime.TotalSeconds > 0 && now.TimeOfDay < schedule.RunTime)
                        {
                            reason = $"waiting for start time {schedule.RunTime:hh\\:mm}";
                            result = false;
                        }
                        else
                        {
                            reason = "first run (no previous execution)";
                            result = true;
                        }
                    }
                    else
                    {
                        double elapsed = (now - schedule.LastRunTime.Value).TotalMinutes;
                        int interval = schedule.RepeatIntervalMinutes;
                        result = elapsed >= interval;
                        reason = result ? $"interval elapsed ({elapsed:F0}/{interval} min)"
                            : $"waiting ({elapsed:F0}/{interval} min)";
                    }
                    break;

                default:
                    reason = "unknown schedule type";
                    result = false;
                    break;
            }

            if (result)
                AppLogger.Info($"[Scheduler] Schedule \"{schedule.Name}\" ({schedule.ScheduleType}) → WILL RUN ({reason})");
            else
                AppLogger.Info($"[Scheduler] Schedule \"{schedule.Name}\" ({schedule.ScheduleType}) → SKIPPED ({reason})");

            return result;
        }

        private async Task<string> ExecuteScheduledSearchAsync(ScheduledSearch schedule, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SearchStarted?.Invoke(schedule);

            // --- Resolve relative time ranges to absolute dates ---
            var criteria = schedule.Criteria;
            if (criteria.FileTimeFilter != null)
                criteria.FileTimeFilter = criteria.FileTimeFilter.Resolve();
            if (criteria.ResultTimeFilter != null)
                criteria.ResultTimeFilter = criteria.ResultTimeFilter.Resolve();

            // --- Detailed start logging ---
            string logTypes = (criteria.SearchPLC && criteria.SearchAPP) ? "PLC + APP"
                : criteria.SearchPLC ? "PLC only"
                : criteria.SearchAPP ? "APP only"
                : "None (!)";
            var activeLocations = _locationService.Locations.Where(l => l.IsActive).ToList();
            if (criteria.LocationIds != null && criteria.LocationIds.Count > 0)
                activeLocations = activeLocations.Where(l => criteria.LocationIds.Contains(l.Id)).ToList();

            AppLogger.Info($"[Scheduler] ════════════════════════════════════════════════════");
            AppLogger.Info($"[Scheduler] SCHEDULED SCAN STARTED: \"{schedule.Name}\" (Mode: {schedule.ScanMode})");
            AppLogger.Info($"[Scheduler] Type: {schedule.ScheduleType}, Log types: {logTypes}");
            // Fix locations with empty BasePath but Address containing a path
            foreach (var loc in activeLocations)
            {
                if (string.IsNullOrWhiteSpace(loc.BasePath) && !string.IsNullOrWhiteSpace(loc.Address)
                    && (loc.Address.StartsWith(@"\\") || Directory.Exists(loc.Address)))
                {
                    AppLogger.Warn($"[Scheduler] Location \"{loc.Name}\": BasePath is empty but Address contains a path — using Address as BasePath");
                    loc.BasePath = loc.Address;
                    _locationService.Update(loc);
                }
            }

            AppLogger.Info($"[Scheduler] ── CONFIGURATION ──");
            AppLogger.Info($"[Scheduler]   Scan Mode:     {schedule.ScanMode}");
            AppLogger.Info($"[Scheduler]   Schedule:      {schedule.ScheduleType} at {schedule.RunTime:hh\\:mm}");
            AppLogger.Info($"[Scheduler]   Log Types:     {logTypes}");
            AppLogger.Info($"[Scheduler]   Locations:     {activeLocations.Count} of {_locationService.Locations.Count} total");
            foreach (var loc in activeLocations)
                AppLogger.Info($"[Scheduler]     → \"{loc.Name}\" — {loc.BasePath}");
            if (activeLocations.Count == 0)
                AppLogger.Warn($"[Scheduler] WARNING: No locations matched! Check that locations are active and have a path set.");

            if (schedule.ScanMode != ScanMode.StatisticsOnly)
            {
                if (criteria.Groups != null && criteria.Groups.Count > 0)
                {
                    AppLogger.Info($"[Scheduler] Criteria: {criteria.Groups.Count} group(s), operator: {criteria.GroupOperator}");
                    foreach (var group in criteria.Groups)
                    {
                        if (group.Conditions == null) continue;
                        foreach (var cond in group.Conditions)
                            AppLogger.Info($"[Scheduler]   Search: {cond.Field} {cond.Operator} \"{cond.Value}\"{(cond.Negate ? " (EXCLUDE)" : "")}");
                    }
                }
                else
                {
                    AppLogger.Warn($"[Scheduler] WARNING: No search conditions found in criteria!");
                }
            }
            if (criteria.FileTimeFilter != null)
                AppLogger.Info($"[Scheduler] File time filter: {criteria.FileTimeFilter.From?.ToString("yyyy-MM-dd HH:mm") ?? "any"} → {criteria.FileTimeFilter.To?.ToString("yyyy-MM-dd HH:mm") ?? "any"}");
            if (criteria.ResultTimeFilter != null)
                AppLogger.Info($"[Scheduler] Result time filter: {criteria.ResultTimeFilter.From?.ToString("yyyy-MM-dd HH:mm") ?? "any"} → {criteria.ResultTimeFilter.To?.ToString("yyyy-MM-dd HH:mm") ?? "any"}");

            string htmlReportPath = null;

            // Mark as run NOW to prevent the timer from triggering a duplicate run
            schedule.LastRunTime = DateTime.Now;
            SaveSchedules();

            try
            {
                List<GrepResult> results = null;
                LogStatisticsResult stats = null;
                bool doSearch = schedule.ScanMode == ScanMode.SearchOnly || schedule.ScanMode == ScanMode.SearchAndStatistics;
                bool doStats = schedule.ScanMode == ScanMode.StatisticsOnly || schedule.ScanMode == ScanMode.SearchAndStatistics;

                // --- Search ---
                if (doSearch)
                {
                    results = await _grepService.SearchMultiLocationAsync(
                        criteria, activeLocations, null, ct).ConfigureAwait(false);
                    AppLogger.Info($"[Scheduler] Search phase done: {results.Count:N0} result(s)");
                }

                // --- Statistics ---
                if (doStats)
                {
                    AppLogger.Info($"[Scheduler] Starting statistics computation...");
                    var (plcLogs, appLogs, hasBinary) = await Task.Run(() =>
                        LogStatisticsService.ParseLogsFromLocations(activeLocations, criteria, null, ct)).ConfigureAwait(false);

                    stats = LogStatisticsService.ComputeStatistics(plcLogs, appLogs, hasBinary);
                    AppLogger.Info($"[Scheduler] Statistics computed: {stats.TotalPlcLogs:N0} PLC + {stats.TotalAppLogs:N0} APP logs, {stats.TotalPlcErrors + stats.TotalAppErrors:N0} errors");
                }

                // --- Status ---
                schedule.LastRunTime = DateTime.Now;
                int resultCount = results?.Count ?? 0;
                string statusParts = "";
                if (doSearch) statusParts += $"{resultCount:N0} results";
                if (doStats) statusParts += (statusParts.Length > 0 ? ", " : "") + $"{(stats?.TotalPlcLogs ?? 0) + (stats?.TotalAppLogs ?? 0):N0} logs analyzed";
                schedule.LastRunStatus = $"OK — {statusParts} in {sw.ElapsedMilliseconds}ms";
                SaveSchedules();

                // --- Save outputs ---
                if (!string.IsNullOrWhiteSpace(schedule.OutputDirectory))
                {
                    Directory.CreateDirectory(schedule.OutputDirectory);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string baseName = $"{schedule.Name}_{timestamp}";

                    // Search outputs (JSON + CSV)
                    if (doSearch && results != null && results.Count > 0)
                    {
                        string jsonPath = Path.Combine(schedule.OutputDirectory, baseName + ".json");
                        File.WriteAllText(jsonPath, JsonConvert.SerializeObject(results, Formatting.Indented));

                        string csvPath = Path.Combine(schedule.OutputDirectory, baseName + ".csv");
                        WriteCsv(csvPath, results);

                        AppLogger.Info($"[Scheduler] Search results saved: JSON={jsonPath}, CSV={csvPath}");
                    }

                    // Statistics JSON
                    if (doStats && stats != null)
                    {
                        string statsJsonPath = Path.Combine(schedule.OutputDirectory, baseName + "_stats.json");
                        File.WriteAllText(statsJsonPath, JsonConvert.SerializeObject(stats, Formatting.Indented));
                        AppLogger.Info($"[Scheduler] Statistics saved: {statsJsonPath}");
                    }

                    // HTML report (combined or statistics-only)
                    htmlReportPath = Path.Combine(schedule.OutputDirectory, baseName + ".html");
                    var reportParams = new SearchReportParams
                    {
                        LocationNames = activeLocations.Select(l => l.Name).ToList(),
                        QueryText = null,
                        CriteriaSummary = $"Scheduled scan: {schedule.Name} ({schedule.ScanMode})",
                        SearchDuration = $"{sw.ElapsedMilliseconds:N0}ms",
                        LogTypes = (criteria.SearchPLC && criteria.SearchAPP) ? "PLC + APP"
                            : criteria.SearchPLC ? "PLC" : criteria.SearchAPP ? "APP" : "None"
                    };

                    if (doStats && !doSearch)
                        SearchReportService.GenerateStatisticsReport(htmlReportPath, reportParams, stats);
                    else if (doStats)
                        SearchReportService.GenerateHtmlReport(htmlReportPath, reportParams, results ?? new List<GrepResult>(), stats);
                    else
                        SearchReportService.GenerateHtmlReport(htmlReportPath, reportParams, results ?? new List<GrepResult>());

                    AppLogger.Info($"[Scheduler] HTML report saved: {htmlReportPath}");
                }

                AppLogger.Info($"[Scheduler] SCHEDULED SCAN COMPLETE: \"{schedule.Name}\" — {statusParts} — {sw.ElapsedMilliseconds:N0}ms");
                AppLogger.Info($"[Scheduler] ════════════════════════════════════════════════════");

                // ── Email Notification ──
                try
                {
                    bool headless = System.Windows.Application.Current?.MainWindow == null;
                    _emailService?.ProcessScanResult(schedule, results, stats, htmlReportPath, forceImmediate: headless);
                }
                catch (Exception emailEx)
                {
                    AppLogger.Error($"[Scheduler] Email notification failed for \"{schedule.Name}\": {emailEx.Message}");
                }

                SearchCompleted?.Invoke(schedule, resultCount, null);
            }
            catch (Exception ex)
            {
                schedule.LastRunTime = DateTime.Now;
                schedule.LastRunStatus = $"FAILED — {ex.Message}";
                SaveSchedules();
                AppLogger.Error($"[Scheduler] SCHEDULED SCAN FAILED: \"{schedule.Name}\" — {ex.Message}");
                AppLogger.Info($"[Scheduler] ════════════════════════════════════════════════════");
                SearchCompleted?.Invoke(schedule, 0, ex.Message);
            }

            return htmlReportPath;
        }

        private void WriteCsv(string path, List<Models.GrepResult> results)
        {
            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("Timestamp,LocationName,LocationAddress,FilePath,LineNumber,LogType,MatchedField,PreviewText");
                foreach (var r in results)
                {
                    writer.Write(Escape(r.TimestampDisplay)); writer.Write(',');
                    writer.Write(Escape(r.LocationName)); writer.Write(',');
                    writer.Write(Escape(r.LocationAddress)); writer.Write(',');
                    writer.Write(Escape(r.FilePath)); writer.Write(',');
                    writer.Write(r.LineNumber); writer.Write(',');
                    writer.Write(Escape(r.LogType)); writer.Write(',');
                    writer.Write(Escape(r.MatchedField)); writer.Write(',');
                    writer.WriteLine(Escape(r.PreviewText));
                }
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void LoadSchedules()
        {
            try
            {
                if (File.Exists(ScheduleFile))
                    Schedules = JsonConvert.DeserializeObject<List<ScheduledSearch>>(File.ReadAllText(ScheduleFile))
                                ?? new List<ScheduledSearch>();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Scheduler] Failed to load schedules: {ex.Message}");
                Schedules = new List<ScheduledSearch>();
            }
        }

        public void SaveSchedules()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScheduleFile));
                File.WriteAllText(ScheduleFile, JsonConvert.SerializeObject(Schedules, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Scheduler] Failed to save schedules: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _emailService?.Dispose();
        }
    }
}
