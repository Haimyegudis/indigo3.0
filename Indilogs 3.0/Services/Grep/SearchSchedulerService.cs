using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
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
        private readonly System.Timers.Timer _timer;
        private bool _isRunning;

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
            return await ExecuteScheduledSearchAsync(schedule, ct);
        }

        private async void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;

            try
            {
                var now = DateTime.Now;
                foreach (var schedule in Schedules.Where(s => s.IsEnabled))
                {
                    if (ShouldRun(schedule, now))
                    {
                        await ExecuteScheduledSearchAsync(schedule, CancellationToken.None);
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

        private bool ShouldRun(ScheduledSearch schedule, DateTime now)
        {
            if (!schedule.IsEnabled) return false;

            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once:
                    if (schedule.LastRunTime != null) return false;
                    if (schedule.RunDate.HasValue)
                        return now >= schedule.RunDate.Value.Date.Add(schedule.RunTime);
                    return now.TimeOfDay >= schedule.RunTime;

                case ScheduleType.Daily:
                    // Run if RunTime has passed today and hasn't run today
                    return now.TimeOfDay >= schedule.RunTime &&
                           (schedule.LastRunTime == null || schedule.LastRunTime.Value.Date < now.Date);

                case ScheduleType.Weekly:
                    return schedule.RunDays.Contains(now.DayOfWeek) &&
                           now.TimeOfDay >= schedule.RunTime &&
                           (schedule.LastRunTime == null || schedule.LastRunTime.Value.Date < now.Date);

                case ScheduleType.Interval:
                    return schedule.LastRunTime == null ||
                           (now - schedule.LastRunTime.Value).TotalMinutes >= schedule.RepeatIntervalMinutes;

                default:
                    return false;
            }
        }

        private async Task<string> ExecuteScheduledSearchAsync(ScheduledSearch schedule, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SearchStarted?.Invoke(schedule);

            // --- Detailed start logging ---
            var criteria = schedule.Criteria;
            string logTypes = (criteria.SearchPLC && criteria.SearchAPP) ? "PLC + APP"
                : criteria.SearchPLC ? "PLC only"
                : criteria.SearchAPP ? "APP only"
                : "None (!)";
            var activeLocations = _locationService.Locations.Where(l => l.IsActive).ToList();
            if (criteria.LocationIds != null && criteria.LocationIds.Count > 0)
                activeLocations = activeLocations.Where(l => criteria.LocationIds.Contains(l.Id)).ToList();

            AppLogger.Info($"[Scheduler] ════════════════════════════════════════════════════");
            AppLogger.Info($"[Scheduler] SCHEDULED SEARCH STARTED: \"{schedule.Name}\"");
            AppLogger.Info($"[Scheduler] Type: {schedule.ScheduleType}, Log types: {logTypes}");
            AppLogger.Info($"[Scheduler] Locations: {activeLocations.Count} active");
            foreach (var loc in activeLocations)
                AppLogger.Info($"[Scheduler]   → \"{loc.Name}\" — {loc.BasePath}");

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
                AppLogger.Warn($"[Scheduler] WARNING: No search conditions found in criteria! Schedule may have been created without a search query.");
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
                var results = await _grepService.SearchMultiLocationAsync(
                    criteria,
                    activeLocations,
                    null, // No UI progress for scheduled runs
                    ct);

                schedule.LastRunTime = DateTime.Now;
                schedule.LastRunStatus = $"OK — {results.Count:N0} results in {sw.ElapsedMilliseconds}ms";
                SaveSchedules();

                // Save results to output directory
                if (!string.IsNullOrWhiteSpace(schedule.OutputDirectory))
                {
                    Directory.CreateDirectory(schedule.OutputDirectory);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string baseName = $"{schedule.Name}_{timestamp}";

                    // JSON output
                    string jsonPath = Path.Combine(schedule.OutputDirectory, baseName + ".json");
                    File.WriteAllText(jsonPath, JsonConvert.SerializeObject(results, Formatting.Indented));

                    // CSV output
                    string csvPath = Path.Combine(schedule.OutputDirectory, baseName + ".csv");
                    WriteCsv(csvPath, results);

                    // HTML report
                    htmlReportPath = Path.Combine(schedule.OutputDirectory, baseName + ".html");
                    var reportParams = new SearchReportParams
                    {
                        LocationNames = _locationService.Locations.Where(l => l.IsActive).Select(l => l.Name).ToList(),
                        QueryText = null,
                        CriteriaSummary = $"Scheduled search: {schedule.Name}",
                        SearchDuration = $"{sw.ElapsedMilliseconds:N0}ms",
                        LogTypes = (schedule.Criteria.SearchPLC && schedule.Criteria.SearchAPP) ? "PLC + APP"
                            : schedule.Criteria.SearchPLC ? "PLC" : schedule.Criteria.SearchAPP ? "APP" : "None"
                    };
                    SearchReportService.GenerateHtmlReport(htmlReportPath, reportParams, results);

                    AppLogger.Info($"[Scheduler] Results saved: JSON={jsonPath}, CSV={csvPath}, HTML={htmlReportPath}");
                }

                AppLogger.Info($"[Scheduler] SCHEDULED SEARCH COMPLETE: \"{schedule.Name}\" — {results.Count:N0} result(s) in {sw.ElapsedMilliseconds:N0}ms");
                AppLogger.Info($"[Scheduler] ════════════════════════════════════════════════════");
                SearchCompleted?.Invoke(schedule, results.Count, null);
            }
            catch (Exception ex)
            {
                schedule.LastRunTime = DateTime.Now;
                schedule.LastRunStatus = $"FAILED — {ex.Message}";
                SaveSchedules();
                AppLogger.Error($"[Scheduler] SCHEDULED SEARCH FAILED: \"{schedule.Name}\" — {ex.Message}");
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
        }
    }
}
