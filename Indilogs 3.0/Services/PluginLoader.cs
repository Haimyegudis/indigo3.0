using IndiLogs.PluginAPI;
using IndiLogs_3._0.Services.BuiltInPlugins;
using IndiLogs_3._0.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Scans <see cref="PluginsFolder"/> for DLLs that contain classes implementing
    /// <see cref="ILogFilePlugin"/> and exposes them via <see cref="IPluginLoader"/>.
    ///
    /// <para>Plugins are loaded with <see cref="Assembly.LoadFrom"/> into the default
    /// AppDomain, so they share the same CLR process as IndiLogs.
    /// Assemblies are never unloaded until the application exits.</para>
    /// </summary>
    public class PluginLoader : IPluginLoader
    {
        // ---------------------------------------------------------------
        // Plugins folder: %AppData%\IndiLogs3.0\Plugins\
        // ---------------------------------------------------------------
        /// <summary>
        /// The folder scanned for plugin DLLs.
        /// Plugins copy their DLLs here to be picked up automatically.
        /// </summary>
        public static readonly string PluginsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndiLogs3.0",
            "Plugins");

        private readonly List<ILogFilePlugin> _plugins  = new List<ILogFilePlugin>();
        // Maps each plugin instance to the DLL file it was loaded from
        private readonly Dictionary<ILogFilePlugin, string> _dllPaths = new Dictionary<ILogFilePlugin, string>();

        // ---------------------------------------------------------------
        // IPluginLoader
        // ---------------------------------------------------------------

        /// <inheritdoc/>
        public IReadOnlyList<ILogFilePlugin> Plugins => _plugins.AsReadOnly();

        /// <inheritdoc/>
        public string GetDllPath(ILogFilePlugin plugin)
        {
            if (plugin == null) return null;
            _dllPaths.TryGetValue(plugin, out string path);
            return path;
        }

        // ---------------------------------------------------------------
        // Construction — triggers initial scan
        // ---------------------------------------------------------------

        /// <summary>
        /// Creates a new <see cref="PluginLoader"/> and immediately scans
        /// <see cref="PluginsFolder"/> for plugins.
        /// </summary>
        public PluginLoader()
        {
            // When a plugin DLL is loaded, it may try to load its own copy of
            // IndiLogs.PluginAPI.dll from its own folder.  That would create a
            // second, distinct assembly identity and make IsAssignableFrom return
            // false for every plugin type.  This handler redirects such requests
            // to the copy already loaded by the host application.
            AppDomain.CurrentDomain.AssemblyResolve += RedirectAlreadyLoadedAssembly;
            Reload();
        }

        private static Assembly RedirectAlreadyLoadedAssembly(object sender, ResolveEventArgs args)
        {
            string shortName = new AssemblyName(args.Name).Name;
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == shortName);
        }

        // ---------------------------------------------------------------
        // Reload
        // ---------------------------------------------------------------

        /// <inheritdoc/>
        public void Reload()
        {
            _plugins.Clear();
            _dllPaths.Clear();

            // ── Step 1: Load external DLL plugins from the Plugins folder ──────────
            // External plugins are added FIRST so they take priority over built-ins
            // (FindPlugin uses FirstOrDefault — first match wins).
            if (!Directory.Exists(PluginsFolder))
            {
                try
                {
                    Directory.CreateDirectory(PluginsFolder);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginLoader] Creating plugins folder failed: {ex.Message}");
                }
            }

            if (!Directory.Exists(PluginsFolder))
            {
            }
            else
            {
                string[] dlls = Directory.GetFiles(PluginsFolder, "*.dll", SearchOption.TopDirectoryOnly);

                foreach (string dll in dlls)
                {
                    // Skip the API contract assembly — it is not a plugin
                    if (Path.GetFileName(dll).Equals("IndiLogs.PluginAPI.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        LoadPluginsFromAssembly(dll);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PluginLoader] Loading plugins from assembly '{System.IO.Path.GetFileName(dll)}' failed: {ex.Message}");
                    }
                }
            }

            // ── Step 2: Append built-in parsers AFTER external plugins ──────────────
            // Built-ins are the fallback: they only match if no external plugin claimed the file.
            foreach (var builtIn in BuiltInPluginRegistry.All)
            {
                _plugins.Add(builtIn);
            }
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private void LoadPluginsFromAssembly(string dllPath)
        {
            Assembly asm = Assembly.LoadFrom(dllPath);
            Type pluginInterface = typeof(ILogFilePlugin);

            // Find all public, concrete classes that implement ILogFilePlugin
            IEnumerable<Type> pluginTypes = asm
                .GetExportedTypes()
                .Where(t => t.IsClass && !t.IsAbstract && pluginInterface.IsAssignableFrom(t));

            foreach (Type type in pluginTypes)
            {
                try
                {
                    var plugin = (ILogFilePlugin)Activator.CreateInstance(type);
                    _plugins.Add(plugin);
                    _dllPaths[plugin] = dllPath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginLoader] Instantiating plugin type '{type.FullName}' failed: {ex.Message}");
                }
            }
        }
    }
}
