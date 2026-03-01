using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IndiLogs_3._0.Models.Grep;
using Newtonsoft.Json;

namespace IndiLogs_3._0.Services.Grep
{
    /// <summary>
    /// Saves and loads <see cref="SearchProfile"/> JSON configs.
    /// </summary>
    public class SearchConfigService
    {
        private static readonly string ProfileDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "IndiLogs3.0", "Configs", "SearchProfiles");

        public SearchConfigService()
        {
            Directory.CreateDirectory(ProfileDir);
        }

        public void SaveProfile(SearchProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException("Profile name is required.");

            string path = GetProfilePath(profile.Name);
            File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
            AppLogger.Info($"[Grep] Profile '{profile.Name}' saved to {path}");
        }

        public SearchProfile LoadProfile(string name)
        {
            string path = GetProfilePath(name);
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<SearchProfile>(File.ReadAllText(path));
        }

        public SearchProfile LoadProfileFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            return JsonConvert.DeserializeObject<SearchProfile>(File.ReadAllText(filePath));
        }

        public List<string> ListProfiles()
        {
            if (!Directory.Exists(ProfileDir)) return new List<string>();
            return Directory.GetFiles(ProfileDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
        }

        public bool DeleteProfile(string name)
        {
            string path = GetProfilePath(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public bool RenameProfile(string oldName, string newName)
        {
            string oldPath = GetProfilePath(oldName);
            string newPath = GetProfilePath(newName);
            if (!File.Exists(oldPath) || File.Exists(newPath)) return false;

            var profile = LoadProfile(oldName);
            profile.Name = newName;
            File.Delete(oldPath);
            SaveProfile(profile);
            return true;
        }

        /// <summary>
        /// Exports a profile to an arbitrary file path for sharing.
        /// </summary>
        public void ExportProfile(SearchProfile profile, string filePath)
        {
            File.WriteAllText(filePath, JsonConvert.SerializeObject(profile, Formatting.Indented));
        }

        /// <summary>
        /// Imports a profile from an arbitrary file path.
        /// </summary>
        public SearchProfile ImportProfile(string filePath)
        {
            var profile = JsonConvert.DeserializeObject<SearchProfile>(File.ReadAllText(filePath));
            if (profile != null)
                SaveProfile(profile);
            return profile;
        }

        private string GetProfilePath(string name)
        {
            // Sanitize filename
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return Path.Combine(ProfileDir, name + ".json");
        }
    }
}
