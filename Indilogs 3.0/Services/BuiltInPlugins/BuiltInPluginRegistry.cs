#nullable disable
using IndiLogs.PluginAPI;
using System.Collections.Generic;

namespace IndiLogs_3._0.Services.BuiltInPlugins
{
    /// <summary>
    /// Provides the list of parsers that are built directly into IndiLogs.
    /// These are always available without installing any plugin DLL.
    /// External DLL plugins override built-ins when both claim the same file.
    /// </summary>
    internal static class BuiltInPluginRegistry
    {
        /// <summary>
        /// Built-in parsers used in the AUTOMATIC ZIP/file loading pipeline.
        /// This list is intentionally EMPTY — all known file types (PLC logs, APP logs,
        /// CSV terminal logs, etc.) are handled by the internal LogFileService parser.
        /// Running generic built-in plugins on ZIP entries would cause redundant parsing
        /// of large files and dramatically slow down log loading.
        /// Only user-installed external DLL plugins (from the Plugins folder) will appear
        /// in the PluginLoader's list via the normal DLL-scan path.
        /// </summary>
        public static IReadOnlyList<ILogFilePlugin> All { get; } = new List<ILogFilePlugin>();

        /// <summary>
        /// Extended list that also includes <see cref="IndigoAppLogPlugin"/> for use
        /// in the "Different Logs" tab where the user manually selects a file.
        /// This must NOT be used by the automatic ZIP scanning pipeline.
        /// </summary>
        public static IReadOnlyList<ILogFilePlugin> AllForManualOpen { get; } = new List<ILogFilePlugin>
        {
            new IndigoAppLogPlugin(),     // INDIGO APP text logs — only for manual/Different-Logs tab
            new GenericTextLogPlugin(),
            new CsvAutoPlugin(),
            new JsonLogPlugin(),
            new Log4NetPlugin(),
        };
    }
}
