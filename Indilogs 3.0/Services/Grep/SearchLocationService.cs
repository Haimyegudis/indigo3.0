using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using IndiLogs_3._0.Models.Grep;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// CRUD and connectivity for search locations. Persisted to disk as JSON.
    /// </summary>
    public class SearchLocationService
    {
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "IndiLogs3.0", "Configs");

        private static readonly string LocationsFile =
            Path.Combine(ConfigDir, "search_locations.json");

        private List<SearchLocation> _locations = new List<SearchLocation>();

        public IReadOnlyList<SearchLocation> Locations => _locations;

        public SearchLocationService()
        {
            Load();
        }

        public void Add(SearchLocation location)
        {
            _locations.Add(location);
            Save();
        }

        public void Remove(Guid id)
        {
            _locations.RemoveAll(l => l.Id == id);
            Save();
        }

        public void Update(SearchLocation location)
        {
            var idx = _locations.FindIndex(l => l.Id == location.Id);
            if (idx >= 0) _locations[idx] = location;
            Save();
        }

        /// <summary>
        /// Pings the host and checks if the base path directory is reachable.
        /// </summary>
        public async Task<ConnectionStatus> TestConnectivityAsync(SearchLocation location)
        {
            location.ConnectionStatus = ConnectionStatus.Testing;
            try
            {
                // 1. Ping the address if it looks like an IP/hostname
                if (!string.IsNullOrWhiteSpace(location.Address))
                {
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(location.Address, 3000).ConfigureAwait(false);
                        if (reply.Status != IPStatus.Success)
                        {
                            location.ConnectionStatus = ConnectionStatus.Disconnected;
                            return ConnectionStatus.Disconnected;
                        }
                    }
                }

                // 2. Check if the path exists
                if (!string.IsNullOrWhiteSpace(location.BasePath))
                {
                    bool exists = await Task.Run(() => Directory.Exists(location.BasePath)).ConfigureAwait(false);
                    location.ConnectionStatus = exists ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
                    if (exists) location.LastAccessed = DateTime.Now;
                    return location.ConnectionStatus;
                }

                location.ConnectionStatus = ConnectionStatus.Connected;
                return ConnectionStatus.Connected;
            }
            catch
            {
                location.ConnectionStatus = ConnectionStatus.Disconnected;
                return ConnectionStatus.Disconnected;
            }
        }

        /// <summary>
        /// Builds a UNC path from an IP address and a share name.
        /// </summary>
        public static string? BuildUncPath(string? address, string? shareName)
        {
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(shareName))
                return null;
            return $@"\\{address.Trim()}\{shareName.TrimStart('\\')}";
        }

        private void Load()
        {
            try
            {
                if (File.Exists(LocationsFile))
                {
                    var json = File.ReadAllText(LocationsFile);
                    _locations = JsonConvert.DeserializeObject<List<SearchLocation>>(json) ?? new List<SearchLocation>();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load search locations: {ex.Message}");
                _locations = new List<SearchLocation>();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(LocationsFile,
                    JsonConvert.SerializeObject(_locations, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save search locations: {ex.Message}");
            }
        }
    }
}
