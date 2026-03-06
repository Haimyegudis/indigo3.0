using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Service for loading, saving, and resetting user default configuration settings.
    /// </summary>
    public interface IDefaultConfigurationService
    {
        /// <summary>Gets the currently loaded default configuration.</summary>
        DefaultConfiguration? CurrentDefaults { get; }

        /// <summary>Loads the default configuration from disk.</summary>
        void Load();

        /// <summary>Saves the given configuration as the new defaults.</summary>
        void Save(DefaultConfiguration config);

        /// <summary>Resets defaults to factory settings by deleting the saved file.</summary>
        void Reset();
    }
}
