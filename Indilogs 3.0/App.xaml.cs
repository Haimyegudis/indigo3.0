using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.Views;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── HEADLESS MODE: --run-schedule {guid} ──
            // Launched by Windows Task Scheduler when the app is closed.
            if (e.Args.Length >= 2
                && e.Args[0].Equals("--run-schedule", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(e.Args[1], out Guid scheduleId))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                await RunHeadlessScheduleAsync(scheduleId);
                Shutdown(0);
                return;
            }

            // ── NORMAL UI MODE ──
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                AppLogger.Error("Unhandled application exception", ex);
                MessageBox.Show($"An unexpected error occurred. Please check the application log.\n\n{ex?.Message}", "Error");
            };

            // Prevent WPF from shutting down when the splash window closes
            // (default OnLastWindowClose would exit the app before MainWindow is created).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Show splash and let it animate fully before creating MainWindow.
            // new MainWindow() blocks the UI thread, so DispatcherTimer callbacks
            // (which drive the splash animations) cannot fire while it runs.
            // Solution: wait for the splash to close itself, THEN create MainWindow.
            var splash = new SplashWindow();
            var splashDone = new TaskCompletionSource<bool>();
            splash.Closed += (_, __) => splashDone.TrySetResult(true);
            splash.Show();
            await splashDone.Task;

            // Initialize DI container before creating MainWindow
            Bootstrapper.Configure();

            // Ensure singletons are disposed on shutdown
            this.Exit += (_, __) => Bootstrapper.Shutdown();

            // Splash is gone – now create and show the main window
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // Check for updates in the background
            try
            {
                var updateService = Bootstrapper.Resolve<Services.Interfaces.IUpdateService>();
                await updateService.CheckForUpdatesSimpleAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Update check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs a single scheduled search without any UI.
        /// Creates only the services needed for scanning, executes, saves output, exits.
        /// </summary>
        private async Task RunHeadlessScheduleAsync(Guid scheduleId)
        {
            AppLogger.Info($"[Headless] Starting headless mode for schedule {scheduleId}");

            try
            {
                IGlobalGrepService grepService = new GlobalGrepService();
                ISearchLocationService locationService = new SearchLocationService();
                IEmailNotificationService emailService = new EmailNotificationService();
                using (ISearchSchedulerService schedulerService = new SearchSchedulerService(grepService, locationService, emailService))
                {
                    var schedule = schedulerService.Schedules.FirstOrDefault(s => s.Id == scheduleId);
                    if (schedule == null)
                    {
                        AppLogger.Warn($"[Headless] Schedule {scheduleId} not found. Exiting.");
                        return;
                    }

                    if (!schedule.IsEnabled)
                    {
                        AppLogger.Info($"[Headless] Schedule \"{schedule.Name}\" is disabled. Exiting.");
                        return;
                    }

                    AppLogger.Info($"[Headless] Executing schedule \"{schedule.Name}\" (Mode: {schedule.ScanMode})");
                    var htmlPath = await schedulerService.RunNowAsync(schedule, CancellationToken.None);
                    AppLogger.Info($"[Headless] Schedule \"{schedule.Name}\" complete. Report: {htmlPath ?? "none"}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Headless] Fatal error running schedule {scheduleId}", ex);
            }
        }
    }
}