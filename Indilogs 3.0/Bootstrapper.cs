using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;

namespace IndiLogs_3._0
{
    /// <summary>
    /// Manual composition root — wires up services and ViewModels.
    /// No external DI container required.
    /// </summary>
    public static class Bootstrapper
    {
        private static readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
        private static bool _configured;

        /// <summary>
        /// Creates all singletons and wires up dependencies.
        /// Call once during application startup.
        /// </summary>
        public static void Configure()
        {
            if (_configured) return;

            // --- Services ---
            var pluginLoader = new PluginLoader();
            Register<IPluginLoader>(pluginLoader);

            var dialogService = new DialogService();
            Register<IDialogService>(dialogService);

            var logFileService = new LogFileService(pluginLoader, dialogService);
            Register<ILogFileService>(logFileService);

            var coloringService = new LogColoringService();
            Register<ILogColoringService>(coloringService);

            var dispatcher = new WpfDispatcher();
            Register<IDispatcher>(dispatcher);

            var csvService = new CsvExportService(dialogService, dispatcher);
            Register<ICsvExportService>(csvService);

            var defaultConfigService = new DefaultConfigurationService();
            Register<IDefaultConfigurationService>(defaultConfigService);

            var windowManager = new WindowManagerAdapter();
            Register<IWindowManager>(windowManager);

            var viewFactory = new ViewFactory();
            Register<IViewFactory>(viewFactory);

            var windowOwner = new WpfWindowOwnerProvider();
            Register<IWindowOwnerProvider>(windowOwner);

            var globalGrepService = new GlobalGrepService();
            Register<IGlobalGrepService>(globalGrepService);

            var searchLocationService = new SearchLocationService();
            Register<ISearchLocationService>(searchLocationService);

            var searchConfigService = new SearchConfigService();
            Register<ISearchConfigService>(searchConfigService);

            var emailNotificationService = new EmailNotificationService();
            Register<IEmailNotificationService>(emailNotificationService);

            var searchSchedulerService = new SearchSchedulerService(globalGrepService, searchLocationService, emailNotificationService);
            Register<ISearchSchedulerService>(searchSchedulerService);

            var windowsTaskSchedulerService = new WindowsTaskSchedulerService();
            Register<IWindowsTaskSchedulerService>(windowsTaskSchedulerService);

            // --- ViewModels ---
            var mainVM = new MainViewModel(logFileService, coloringService, csvService, defaultConfigService, dialogService, viewFactory, dispatcher, windowOwner, windowManager);
            Register<MainViewModel>(mainVM);

            _configured = true;
        }

        /// <summary>
        /// Resolves a registered singleton by type.
        /// </summary>
        public static T Resolve<T>() where T : class
        {
            if (!_configured)
                throw new InvalidOperationException("Bootstrapper has not been initialized. Call Configure() first.");

            if (_singletons.TryGetValue(typeof(T), out var instance))
                return (T)instance;

            throw new InvalidOperationException($"Type {typeof(T).Name} is not registered in the Bootstrapper.");
        }

        /// <summary>
        /// Cleans up all registered singletons on application shutdown.
        /// </summary>
        public static void Shutdown()
        {
            foreach (var instance in _singletons.Values)
            {
                (instance as IDisposable)?.Dispose();
            }
            _singletons.Clear();
            _configured = false;
        }

        private static void Register<T>(object instance)
        {
            _singletons[typeof(T)] = instance;
        }
    }
}
