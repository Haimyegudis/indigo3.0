using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class UpdateService
    {
        // Server paths — read from appsettings.json (sits next to the exe)
        private static readonly string VersionFileUrl;
        private static readonly string InstallerFolder;
        private const string ExePattern = "IndiLogs3.0_*.exe";

        static UpdateService()
        {
            try
            {
                // Environment.ProcessPath points to the actual exe even in single-file mode
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                string settingsPath = Path.Combine(exeDir, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    VersionFileUrl = doc.RootElement.TryGetProperty("UpdateVersionFile", out var vf) ? vf.GetString() : "";
                    InstallerFolder = doc.RootElement.TryGetProperty("UpdateInstallerFolder", out var inf) ? inf.GetString() : "";
                }
                else
                {
                    VersionFileUrl = "";
                    InstallerFolder = "";
                }
            }
            catch
            {
                VersionFileUrl = "";
                InstallerFolder = "";
            }
        }

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

                    // Establish connection to the network share (needed for hidden admin shares)
                    EnsureShareConnection(InstallerFolder);

                    // Read version file directly — File.Exists() returns false on hidden
                    // admin shares (softwareqa$) even when the file is accessible.
                    string serverVersionText;
                    try
                    {
                        serverVersionText = File.ReadAllText(VersionFileUrl).Trim();
                    }
                    catch (Exception readEx)
                    {
                        UpdateLogger.Log($"[ERROR] Cannot read version file: {readEx.Message}");
                        return;
                    }
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
                                DownloadAndInstallUpdate(serverVersion, serverVersionText);
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

        private void DownloadAndInstallUpdate(Version serverVersion, string versionText)
        {
            try
            {
                UpdateLogger.Log("[AUTO-UPDATE] Locating new exe on network share...");

                string serverExePath = FindExeOnServer(versionText);
                if (string.IsNullOrEmpty(serverExePath))
                {
                    UpdateLogger.Log("[ERROR] Could not find update exe on server");
                    MessageBox.Show(
                        "Could not find the update file on the server.\n\n" +
                        $"Please open this folder and copy the exe manually:\n{InstallerFolder}",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Current exe path
                string currentExePath = Environment.ProcessPath;
                string currentDir = Path.GetDirectoryName(currentExePath);
                string currentExeName = Path.GetFileName(currentExePath);

                // Copy new exe to temp location next to current exe
                string tempExePath = Path.Combine(currentDir, $"IndiLogs3.0_update_{versionText}.tmp");
                UpdateLogger.Log($"[AUTO-UPDATE] Copying from server: {serverExePath}");
                File.Copy(serverExePath, tempExePath, true);
                UpdateLogger.Log($"[AUTO-UPDATE] Copied to: {tempExePath}");

                // Write a small .cmd script that waits, replaces, and relaunches
                string cmdPath = Path.Combine(currentDir, "update.cmd");
                string cmdContent =
                    "@echo off\r\n" +
                    "echo Updating IndiLogs 3.0...\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"move /y \"{tempExePath}\" \"{currentExePath}\"\r\n" +
                    $"start \"\" \"{currentExePath}\"\r\n" +
                    $"del \"%~f0\"\r\n";
                File.WriteAllText(cmdPath, cmdContent);

                UpdateLogger.Log("[AUTO-UPDATE] Launching update script, closing application...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = cmdPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[AUTO-UPDATE ERROR]", ex);
                MessageBox.Show(
                    $"Failed to apply update:\n{ex.Message}\n\n" +
                    $"Please open this folder and copy the exe manually:\n{InstallerFolder}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string FindExeOnServer(string versionText)
        {
            // Try the known filename: IndiLogs3.0_{version}.exe
            string expectedPath = Path.Combine(InstallerFolder, $"IndiLogs3.0_{versionText}.exe");
            if (File.Exists(expectedPath))
            {
                UpdateLogger.Log($"[AUTO-UPDATE] Found exe by version: {expectedPath}");
                return expectedPath;
            }

            // Fallback: enumerate directory for IndiLogs3.0_*.exe
            try
            {
                var exeFiles = Directory.GetFiles(InstallerFolder, ExePattern)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                UpdateLogger.Log($"[AUTO-UPDATE] Found {exeFiles.Count} update file(s):");
                foreach (var file in exeFiles)
                {
                    var fi = new FileInfo(file);
                    UpdateLogger.Log($"  - {fi.Name} ({fi.Length} bytes, {fi.LastWriteTime})");
                }

                if (exeFiles.Count > 0)
                    return exeFiles.First();
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[AUTO-UPDATE] Cannot enumerate directory...", ex);
            }

            return null;
        }


        /// <summary>
        /// Gets the current application version
        /// </summary>
        public static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        // ── Network share connection via Windows API ──

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        private const int RESOURCETYPE_DISK = 1;
        private const int CONNECT_TEMPORARY = 0x00000004;

        /// <summary>
        /// Establishes a connection to a UNC share.
        /// Needed for hidden admin shares (e.g., softwareqa$) where File.Exists()
        /// and File.ReadAllText() fail without an explicit connection.
        /// </summary>
        private void EnsureShareConnection(string uncFolder)
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                UpdateLogger.Log($"Running as: {identity.Name}");

                var nr = new NETRESOURCE
                {
                    dwType = RESOURCETYPE_DISK,
                    lpRemoteName = uncFolder
                };

                // Try current Windows credentials first
                int result = WNetAddConnection2(ref nr, null, null, CONNECT_TEMPORARY);

                if (result == 0 || result == 1219) // 0 = success, 1219 = already connected
                {
                    UpdateLogger.Log($"[NETWORK] Connected to {uncFolder} (code {result})");
                    return;
                }

                UpdateLogger.Log($"[NETWORK] Default credentials failed (code {result}), using service account...");

                // Connect with the shared service account
                result = WNetAddConnection2(ref nr, "hpindigo2010", @"inr\automation", CONNECT_TEMPORARY);

                if (result == 0 || result == 1219)
                    UpdateLogger.Log($"[NETWORK] Connected to {uncFolder} via service account");
                else
                    UpdateLogger.Log($"[NETWORK] Service account connection failed (code {result})");
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"[NETWORK] Connection attempt failed: {ex.Message}");
            }
        }
    }
}
