using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Manages Windows Scheduled Task registration for IndiLogs background scans.
    /// Uses schtasks.exe — no third-party dependencies.
    /// Task names follow the pattern: IndiLogs_Scan_{guid}
    /// </summary>
    public class WindowsTaskSchedulerService
    {
        private const string TaskPrefix = "IndiLogs_Scan_";

        private static string ExePath =>
            Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>
        /// Creates or updates a Windows Scheduled Task for the given schedule.
        /// If the schedule is disabled, unregisters the task instead.
        /// </summary>
        public bool RegisterSchedule(ScheduledSearch schedule)
        {
            if (!schedule.IsEnabled)
                return UnregisterSchedule(schedule.Id);

            string taskName = GetTaskName(schedule.Id);
            string exePath = ExePath;

            // Build the /TR argument with proper quoting (exe path has a space)
            string trValue = $"\\\"{exePath}\\\" --run-schedule {schedule.Id}";

            string args = $"/Create /F /TN \"{taskName}\" /TR \"{trValue}\"";

            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once:
                    var runDate = schedule.RunDate ?? DateTime.Today;
                    args += $" /SC ONCE /SD {runDate:MM/dd/yyyy} /ST {schedule.RunTime:hh\\:mm}";
                    break;

                case ScheduleType.Daily:
                    args += $" /SC DAILY /ST {schedule.RunTime:hh\\:mm}";
                    break;

                case ScheduleType.Weekly:
                    var days = string.Join(",", schedule.RunDays.Select(DayToSchtasksName));
                    if (string.IsNullOrEmpty(days)) days = "MON";
                    args += $" /SC WEEKLY /D {days} /ST {schedule.RunTime:hh\\:mm}";
                    break;

                case ScheduleType.Interval:
                    int intervalVal = Math.Max(1, schedule.RepeatIntervalValue);
                    switch (schedule.IntervalUnit)
                    {
                        case IntervalUnit.Hours:
                            args += $" /SC HOURLY /MO {intervalVal}";
                            break;
                        case IntervalUnit.Days:
                            args += $" /SC DAILY /MO {intervalVal}";
                            break;
                        default: // Minutes
                            args += $" /SC MINUTE /MO {intervalVal}";
                            break;
                    }
                    if (schedule.RunTime.TotalSeconds > 0)
                        args += $" /ST {schedule.RunTime:hh\\:mm}";
                    break;
            }

            var (exitCode, _, stderr) = RunSchtasks(args);
            if (exitCode == 0)
            {
                AppLogger.Info($"[TaskScheduler] Registered task \"{taskName}\" for schedule \"{schedule.Name}\"");
                return true;
            }

            AppLogger.Warn($"[TaskScheduler] Failed to register \"{taskName}\": {stderr}");
            return false;
        }

        /// <summary>
        /// Deletes the Windows Scheduled Task for the given schedule ID.
        /// </summary>
        public bool UnregisterSchedule(Guid scheduleId)
        {
            string taskName = GetTaskName(scheduleId);
            var (exitCode, _, stderr) = RunSchtasks($"/Delete /TN \"{taskName}\" /F");

            if (exitCode == 0 || (stderr != null && stderr.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                AppLogger.Info($"[TaskScheduler] Unregistered task \"{taskName}\"");
                return true;
            }

            AppLogger.Warn($"[TaskScheduler] Failed to unregister \"{taskName}\": {stderr}");
            return false;
        }

        /// <summary>
        /// Syncs all enabled schedules with Windows Task Scheduler.
        /// Registers missing tasks and removes orphaned ones.
        /// </summary>
        public void SyncAll(IReadOnlyList<ScheduledSearch> schedules)
        {
            // Register all enabled schedules
            foreach (var s in schedules)
            {
                if (s.IsEnabled)
                    RegisterSchedule(s);
                else
                    UnregisterSchedule(s.Id);
            }

            // Query existing IndiLogs tasks to remove orphans
            var (exitCode, stdout, _) = RunSchtasks("/Query /FO CSV /NH");
            if (exitCode != 0 || string.IsNullOrEmpty(stdout)) return;

            var validTaskNames = new HashSet<string>(
                schedules.Where(s => s.IsEnabled).Select(s => GetTaskName(s.Id)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // CSV format: "\\TaskPath","Status","Next Run Time",...
                var parts = line.Split(',');
                if (parts.Length == 0) continue;

                string name = parts[0].Trim('"', ' ');
                // Extract just the task name (remove folder path like \)
                int lastSlash = name.LastIndexOf('\\');
                if (lastSlash >= 0) name = name.Substring(lastSlash + 1);

                if (name.StartsWith(TaskPrefix, StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(name, @"^IndiLogs_Scan_[0-9a-fA-F]{32}$")
                    && !validTaskNames.Contains(name))
                {
                    RunSchtasks($"/Delete /TN \"{name}\" /F");
                    AppLogger.Info($"[TaskScheduler] Removed orphaned task \"{name}\"");
                }
            }
        }

        private string GetTaskName(Guid id) => TaskPrefix + id.ToString("N");

        private (int exitCode, string stdout, string stderr) RunSchtasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10_000);
                    return (proc.ExitCode, stdout, stderr);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[TaskScheduler] schtasks.exe failed: {ex.Message}");
                return (-1, "", ex.Message);
            }
        }

        private static string DayToSchtasksName(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Sunday:    return "SUN";
                case DayOfWeek.Monday:    return "MON";
                case DayOfWeek.Tuesday:   return "TUE";
                case DayOfWeek.Wednesday: return "WED";
                case DayOfWeek.Thursday:  return "THU";
                case DayOfWeek.Friday:    return "FRI";
                case DayOfWeek.Saturday:  return "SAT";
                default:                  return "MON";
            }
        }
    }
}
