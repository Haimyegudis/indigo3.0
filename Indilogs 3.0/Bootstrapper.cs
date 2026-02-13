using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace IndiLogs_3._0
{
    /// <summary>
    /// Configures the DI container for the application.
    /// Centralizes all service and ViewModel registrations.
    /// </summary>
    public static class Bootstrapper
    {
        private static IServiceProvider _serviceProvider;

        /// <summary>
        /// Gets the application-wide service provider.
        /// </summary>
        public static IServiceProvider ServiceProvider => _serviceProvider
            ?? throw new InvalidOperationException("Bootstrapper has not been initialized. Call Configure() first.");

        /// <summary>
        /// Configures the DI container with all service and ViewModel registrations.
        /// Should be called once during application startup.
        /// </summary>
        public static void Configure()
        {
            var services = new ServiceCollection();

            // --- Singleton Services with Interfaces ---
            services.AddSingleton<ILogFileService, LogFileService>();
            services.AddSingleton<ILogColoringService, LogColoringService>();
            services.AddSingleton<ICsvExportService, CsvExportService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IChartDataTransferService, ChartDataTransferService>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<ITabTearOffManager, TabTearOffManager>();

            // Also register concrete types for backward-compat DI constructor injection
            services.AddSingleton(sp => (LogFileService)sp.GetRequiredService<ILogFileService>());
            services.AddSingleton(sp => (LogColoringService)sp.GetRequiredService<ILogColoringService>());
            services.AddSingleton(sp => (CsvExportService)sp.GetRequiredService<ICsvExportService>());
            services.AddSingleton(sp => (UpdateService)sp.GetRequiredService<IUpdateService>());

            // --- Transient Services ---
            services.AddTransient<QueryParserService>();
            services.AddTransient<IGlobalGrepService, GlobalGrepService>();

            // --- ViewModels ---
            services.AddSingleton<MainViewModel>();

            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>
        /// Resolves a service from the DI container.
        /// </summary>
        public static T Resolve<T>() where T : class
        {
            return ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Cleans up the DI container on application shutdown.
        /// </summary>
        public static void Shutdown()
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _serviceProvider = null;
        }
    }
}
