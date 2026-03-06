using IndiLogs_3._0.Services.Interfaces;
using System;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService : ILogFileService
    {
        // -----------------------------------------------------------------------
        // Plugin loader — injected via DI; null-safe (no plugins = graceful skip)
        // -----------------------------------------------------------------------
        private readonly IPluginLoader _pluginLoader;
        private readonly Interfaces.IDialogService? _dialogService;

        public LogFileService(IPluginLoader pluginLoader, Interfaces.IDialogService? dialogService = null)
        {
            _pluginLoader = pluginLoader;
            _dialogService = dialogService;
        }

        /// <summary>Exposes the plugin loader for external callers (e.g. dialog filter building).</summary>
        public IPluginLoader GetPluginLoader() => _pluginLoader;

        // --- Optimization: StringPool class for string interning (Thread-Safe) ---
        public class StringPool
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cache
                = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

            public string Intern(string value)
            {
                // If the value is empty or null, nothing to store in Cache
                if (string.IsNullOrEmpty(value)) return value;

                // ConcurrentDictionary.GetOrAdd is thread-safe
                return _cache.GetOrAdd(value, value);
            }

            public void Clear()
            {
                _cache.Clear();
            }
        }
        // ------------------------------------------------------

    }
}
