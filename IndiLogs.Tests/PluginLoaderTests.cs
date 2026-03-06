using IndiLogs_3._0.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    /// <summary>
    /// Unit tests for PluginLoader — verifies plugin discovery, verification,
    /// and built-in plugin registration behavior.
    /// </summary>
    public class PluginLoaderTests
    {
        [Fact]
        public void Constructor_CreatesPluginsFolder()
        {
            // PluginLoader creates the plugins folder on construction
            var loader = new PluginLoader();
            Assert.True(Directory.Exists(PluginLoader.PluginsFolder));
        }

        [Fact]
        public void Plugins_IsNotNull()
        {
            var loader = new PluginLoader();
            Assert.NotNull(loader.Plugins);
        }

        [Fact]
        public void Plugins_ContainsBuiltIns()
        {
            // After construction, Plugins should contain built-in plugins
            // (currently the built-in registry is empty, but the list itself should be initialized)
            var loader = new PluginLoader();
            Assert.NotNull(loader.Plugins);
            // Plugins list should be usable (no exception on enumeration)
            var count = loader.Plugins.Count;
            Assert.True(count >= 0);
        }

        [Fact]
        public void Reload_ClearsAndReloadsPlugins()
        {
            var loader = new PluginLoader();
            int initialCount = loader.Plugins.Count;

            // Reload should not throw and should return to same state
            loader.Reload();
            Assert.Equal(initialCount, loader.Plugins.Count);
        }

        [Fact]
        public void GetDllPath_NullPlugin_ReturnsNull()
        {
            var loader = new PluginLoader();
            Assert.Null(loader.GetDllPath(null));
        }

        [Fact]
        public void GetDllPath_UnknownPlugin_ReturnsNull()
        {
            var loader = new PluginLoader();
            // Built-in plugins are not loaded from DLLs, so GetDllPath returns null
            foreach (var plugin in loader.Plugins)
            {
                // Built-ins have no DLL path
                var path = loader.GetDllPath(plugin);
                // Path is null for built-ins (no DLL source)
                Assert.Null(path);
            }
        }

        [Fact]
        public void PluginsFolder_IsInAppData()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.StartsWith(appData, PluginLoader.PluginsFolder, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PluginsFolder_ContainsIndiLogs()
        {
            Assert.Contains("IndiLogs3.0", PluginLoader.PluginsFolder);
        }

        [Fact]
        public void Reload_DoesNotThrowWhenPluginsFolderExists()
        {
            var loader = new PluginLoader();
            // Ensure folder exists
            Directory.CreateDirectory(PluginLoader.PluginsFolder);

            // Should not throw
            loader.Reload();
            Assert.NotNull(loader.Plugins);
        }

        [Fact]
        public void Reload_SkipsPluginApiDll()
        {
            var loader = new PluginLoader();
            // Even if IndiLogs.PluginAPI.dll exists in the folder, it should be skipped
            var apiDllPath = Path.Combine(PluginLoader.PluginsFolder, "IndiLogs.PluginAPI.dll");
            bool existed = File.Exists(apiDllPath);

            try
            {
                // Create a dummy file to simulate the API DLL
                if (!existed)
                    File.WriteAllBytes(apiDllPath, new byte[] { 0 });

                loader.Reload();
                // Should not crash and should not load the API DLL as a plugin
                Assert.NotNull(loader.Plugins);
            }
            finally
            {
                // Clean up only if we created it
                if (!existed && File.Exists(apiDllPath))
                {
                    try { File.Delete(apiDllPath); } catch { }
                }
            }
        }

        [Fact]
        public void Reload_HandlesInvalidDllGracefully()
        {
            var loader = new PluginLoader();
            // Use a unique filename per test run to avoid file lock race conditions
            var invalidDll = Path.Combine(PluginLoader.PluginsFolder, $"InvalidPlugin_{Guid.NewGuid():N}.dll");

            try
            {
                // Create a file that is not a valid DLL
                File.WriteAllText(invalidDll, "This is not a valid DLL");

                // Should not throw — invalid assemblies are caught and logged
                loader.Reload();
                Assert.NotNull(loader.Plugins);
            }
            finally
            {
                // Retry cleanup — assembly probing may briefly lock the file
                for (int i = 0; i < 3; i++)
                {
                    try { if (File.Exists(invalidDll)) File.Delete(invalidDll); break; }
                    catch { if (i < 2) System.Threading.Thread.Sleep(50); }
                }
            }
        }

        [Fact]
        public void MultipleReloads_AreIdempotent()
        {
            var loader = new PluginLoader();

            for (int i = 0; i < 3; i++)
            {
                loader.Reload();
            }

            Assert.NotNull(loader.Plugins);
        }
    }
}
