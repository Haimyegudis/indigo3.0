using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.Views;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
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

            // Splash is gone – now create and show the main window
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // בדיקת עדכונים ברקע
            try
            {
                var updateService = new UpdateService();
                await updateService.CheckForUpdatesSimpleAsync();
            }
            catch
            {
                // התעלמות משגיאות אם אין אינטרנט או שרת העדכונים למטה
            }
        }

    }
}