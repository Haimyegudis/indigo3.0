using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class UpdateService
    {
        // Server paths - change these if server location changes
        private const string VersionFileUrl = @"\\iihome.inr.rd.hpicorp.net\softwareqa$\QA-Utils\Indilogs3.0\version.txt";
        private const string InstallerFolder = @"\\iihome.inr.rd.hpicorp.net\softwareqa$\QA-Utils\Indilogs3.0";
        private const string InstallerPattern = "IndiLogs*.exe"; // Pattern to find installer

        public async Task CheckForUpdatesSimpleAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    UpdateLogger.Log("========== UPDATE CHECK STARTED ==========");
                    UpdateLogger.Log($"Checking path: {VersionFileUrl}");

                    // Get current version from assembly
                    Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    UpdateLogger.Log($"Current version: {currentVersion}");

                    // Check directory accessibility first
                    string directory = Path.GetDirectoryName(VersionFileUrl);
                    UpdateLogger.Log($"Checking directory: {directory}");

                    if (!Directory.Exists(directory))
                    {
                        UpdateLogger.Log($"[ERROR] Directory not accessible: {directory}");
                        UpdateLogger.Log("Possible causes: Network not connected, VPN required, no permissions");
                        return;
                    }
                    UpdateLogger.Log($"Directory exists: YES");

                    // List files in directory for debugging
                    try
                    {
                        var files = Directory.GetFiles(directory);
                        UpdateLogger.Log($"Files in directory ({files.Length}):");
                        foreach (var file in files)
                        {
                            UpdateLogger.Log($"  - {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception dirEx)
                    {
                        UpdateLogger.Log($"[ERROR] Cannot list directory: {dirEx.Message}");
                    }

                    // Check if server is accessible
                    if (!File.Exists(VersionFileUrl))
                    {
                        UpdateLogger.Log($"[ERROR] Version file not found at: {VersionFileUrl}");
                        return;
                    }
                    UpdateLogger.Log("Version file exists: YES");

                    // Read version from server
                    string serverVersionText = File.ReadAllText(VersionFileUrl).Trim();
                    UpdateLogger.Log($"Server version text (raw): '{serverVersionText}'");
                    UpdateLogger.Log($"Server version text length: {serverVersionText.Length}");

                    if (!Version.TryParse(serverVersionText, out Version serverVersion))
                    {
                        UpdateLogger.Log($"[ERROR] Failed to parse server version: '{serverVersionText}'");
                        UpdateLogger.Log("Expected format: X.X.X.X (e.g., 1.0.0.2)");
                        return;
                    }

                    UpdateLogger.Log($"Server version (parsed): {serverVersion}");
                    UpdateLogger.Log($"Comparison: Server ({serverVersion}) > Current ({currentVersion}) = {serverVersion > currentVersion}");

                    // Compare versions
                    if (serverVersion > currentVersion)
                    {
                        UpdateLogger.Log($"[UPDATE AVAILABLE] New version: {serverVersion}");

                        // Show dialog on UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = MessageBox.Show(
                                $"A new version is available!\n\n" +
                                $"Current version: {currentVersion}\n" +
                                $"New version: {serverVersion}\n\n" +
                                "Do you want to download and install the update now?",
                                "IndiLogs Update Available",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (result == MessageBoxResult.Yes)
                            {
                                DownloadAndInstallUpdate(serverVersion);
                            }
                        });
                    }
                    else
                    {
                        UpdateLogger.Log($"[UP TO DATE] Already at latest version ({currentVersion})");
                    }
                }
                catch (Exception ex)
                {
                    UpdateLogger.Log("[EXCEPTION] Update check failed", ex);
                }
                finally
                {
                    UpdateLogger.Log("========== UPDATE CHECK COMPLETED ==========\n");
                }
            });
        }

        private void DownloadAndInstallUpdate(Version serverVersion)
        {
            try
            {
                UpdateLogger.Log("[AUTO-UPDATE] Locating installer on network share...");

                // Find the installer on the server
                string installerPath = FindInstallerOnServer();
                if (string.IsNullOrEmpty(installerPath))
                {
                    UpdateLogger.Log("[ERROR] Could not find installer file on server");
                    MessageBox.Show(
                        "Could not find the installer file on the server.\n\n" +
                        $"Please open this folder and run the installer manually:\n{InstallerFolder}",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                UpdateLogger.Log($"[AUTO-UPDATE] Found installer: {installerPath}");

                // Run directly from the network share — no copy to %TEMP%.
                // Copying to temp and running from there triggers CrowdStrike/EDR false positives
                // because that is exactly the pattern malware uses.
                // Running directly from a trusted corporate network share is safe and clean.
                UpdateLogger.Log("[AUTO-UPDATE] Launching installer directly from network share...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true   // opens with UAC prompt, no shell injection
                };

                Process.Start(startInfo);
                UpdateLogger.Log("[AUTO-UPDATE] Installer launched, closing application...");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[AUTO-UPDATE ERROR]", ex);
                MessageBox.Show(
                    $"Failed to launch update installer:\n{ex.Message}\n\n" +
                    $"Please open this folder and run the installer manually:\n{InstallerFolder}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string FindInstallerOnServer()
        {
            try
            {
                // Look for installer files matching the pattern
                var installerFiles = Directory.GetFiles(InstallerFolder, InstallerPattern)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                UpdateLogger.Log($"[AUTO-UPDATE] Found {installerFiles.Count} installer file(s):");
                foreach (var file in installerFiles)
                {
                    var fi = new FileInfo(file);
                    UpdateLogger.Log($"  - {fi.Name} ({fi.Length} bytes, {fi.LastWriteTime})");
                }

                // Return the most recent one
                return installerFiles.FirstOrDefault();
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[AUTO-UPDATE] Error finding installer", ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the current application version
        /// </summary>
        public static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }
    }
}
