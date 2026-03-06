using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Text.RegularExpressions;

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

        // Regex for parsing application logs - old format with \x1e as separator
        private readonly Regex _appDevRegex = new Regex(
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\x1e" +
            @"(?<Thread>[^\x1e]*)\x1e" +
            @"(?<RootIFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowName>[^\x1e]*)\x1e" +
            @"(?<Pattern>[^\x1e]*)\x1e" +
            @"(?<Context>[^\x1e]*)\x1e" +
            @"(?<Level>\w+)\s(?<Logger>[^\x1e]*)\x1e" +
            @"(?<Location>[^\x1e]*)\x1e" +
            @"(?<Message>.*?)\x1e" +
            @"(?<Exception>.*?)\x1e" +
            @"(?<Data>.*?)(\x1e|$)",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        // Regex for parsing application logs - new format with | as separator
        // Format: 2026-01-29 10:32:38,073 |Thread| |RootIFlowId| |IFlowId| |IFlowName| |Pattern| |Context| LEVEL  Logger
        // Next line: |Method|
        // Next lines: --> or <-- or message text, followed by optional data/JSON, ending with ||
        private readonly Regex _appDevRegexPipe = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7})\s*\|(?<Thread>[^|]*)\|\s*\|(?<RootIFlowId>[^|]*)\|\s*\|(?<IFlowId>[^|]*)\|\s*\|(?<IFlowName>[^|]*)\|\s*\|(?<Pattern>[^|]*)\|\s*\|(?<Context>[^|]*)\|\s*(?<Level>\w+)\s+(?<Logger>[^\r\n]*)[\r\n]+\|(?<Location>[^|]*)\|[\r\n]+(?<Message>.*?)\s*\|\|",
            RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3,7}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    }
}
