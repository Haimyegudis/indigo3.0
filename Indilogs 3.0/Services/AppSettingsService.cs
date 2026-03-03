using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Reads application settings from appsettings.json.
    /// Provides centralized access to configurable URLs and paths.
    /// </summary>
    internal static class AppSettingsService
    {
        public static string JiraUrl { get; private set; } = "https://hp-jira.external.hp.com/secure/Dashboard.jspa";
        public static string KibanaBaseUrl { get; private set; } = "";

        static AppSettingsService()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                string settingsPath = Path.Combine(exeDir, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    var root = doc.RootElement;

                    if (root.TryGetProperty("JiraUrl", out var jira) && !string.IsNullOrEmpty(jira.GetString()))
                        JiraUrl = jira.GetString()!;

                    if (root.TryGetProperty("KibanaBaseUrl", out var kibana) && !string.IsNullOrEmpty(kibana.GetString()))
                        KibanaBaseUrl = kibana.GetString()!;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppSettingsService: Failed to read appsettings.json: {ex.Message}");
            }
        }
    }
}
