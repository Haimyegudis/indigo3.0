using IndiLogs_3._0.Services.BuiltInPlugins;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class BuiltInPluginRegistryTests
    {
        [Fact]
        public void All_IsEmpty()
        {
            // Auto-scan pipeline uses no built-in plugins
            Assert.Empty(BuiltInPluginRegistry.All);
        }

        [Fact]
        public void AllForManualOpen_HasPlugins()
        {
            Assert.True(BuiltInPluginRegistry.AllForManualOpen.Count > 0);
        }

        [Fact]
        public void AllForManualOpen_ContainsIndigoAppLogPlugin()
        {
            Assert.Contains(BuiltInPluginRegistry.AllForManualOpen,
                p => p.GetType().Name == "IndigoAppLogPlugin");
        }

        [Fact]
        public void AllForManualOpen_ContainsGenericTextLogPlugin()
        {
            Assert.Contains(BuiltInPluginRegistry.AllForManualOpen,
                p => p.GetType().Name == "GenericTextLogPlugin");
        }

        [Fact]
        public void AllForManualOpen_ContainsCsvAutoPlugin()
        {
            Assert.Contains(BuiltInPluginRegistry.AllForManualOpen,
                p => p.GetType().Name == "CsvAutoPlugin");
        }

        [Fact]
        public void AllForManualOpen_ContainsJsonLogPlugin()
        {
            Assert.Contains(BuiltInPluginRegistry.AllForManualOpen,
                p => p.GetType().Name == "JsonLogPlugin");
        }

        [Fact]
        public void AllForManualOpen_ContainsLog4NetPlugin()
        {
            Assert.Contains(BuiltInPluginRegistry.AllForManualOpen,
                p => p.GetType().Name == "Log4NetPlugin");
        }

        [Fact]
        public void AllForManualOpen_AllHaveNames()
        {
            foreach (var plugin in BuiltInPluginRegistry.AllForManualOpen)
            {
                Assert.False(string.IsNullOrEmpty(plugin.Name),
                    $"Plugin {plugin.GetType().Name} has no name");
            }
        }

        [Fact]
        public void AllForManualOpen_AllHaveCanHandle()
        {
            // Each plugin should be able to respond to CanHandle without crashing
            foreach (var plugin in BuiltInPluginRegistry.AllForManualOpen)
            {
                bool result = plugin.CanHandle("test.txt", System.Array.Empty<string>());
                // We don't assert the result, just that it doesn't throw
            }
        }
    }
}
