#nullable disable
using IndiLogs.PluginAPI;
using System.Collections.Generic;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Discovers and manages <see cref="ILogFilePlugin"/> instances
    /// loaded from the user's Plugins folder.
    /// </summary>
    public interface IPluginLoader
    {
        /// <summary>All currently loaded and instantiated plugins, in discovery order.</summary>
        IReadOnlyList<ILogFilePlugin> Plugins { get; }

        /// <summary>
        /// Returns the full path of the DLL from which <paramref name="plugin"/>
        /// was loaded, or <c>null</c> if the origin is unknown (e.g. manually loaded).
        /// </summary>
        string GetDllPath(ILogFilePlugin plugin);

        /// <summary>
        /// (Re-)scans the Plugins folder and reloads all plugin assemblies.
        /// Existing plugin instances are discarded.
        /// Safe to call multiple times.
        /// </summary>
        void Reload();
    }
}
