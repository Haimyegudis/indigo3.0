using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
        private const string HashFileExtension = ".sha256"; // Expected hash file alongside installer

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

                // Verify installer integrity before execution
                if (!VerifyInstallerIntegrity(installerPath))
                {
                    UpdateLogger.Log("[AUTO-UPDATE] Installer integrity verification FAILED - aborting");
                    MessageBox.Show(
                        "Installer integrity verification failed.\n\n" +
                        "The installer file may have been tampered with or the hash file is missing.\n" +
                        $"Please verify the installer manually at:\n{InstallerFolder}",
                        "Security Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                UpdateLogger.Log("[AUTO-UPDATE] Integrity verified, launching installer...");

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
        /// Verifies installer integrity using SHA-256 hash file and Authenticode signature.
        /// The hash file (e.g., IndiLogs_Setup.exe.sha256) must exist alongside the installer.
        /// </summary>
        private bool VerifyInstallerIntegrity(string installerPath)
        {
            try
            {
                // Step 1: Verify Authenticode digital signature with chain validation
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromSignedFile(installerPath);
                if (cert != null)
                {
                    // Validate the certificate chains to a trusted root
                    using (var chain = new System.Security.Cryptography.X509Certificates.X509Chain())
                    {
                        chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
                        chain.ChainPolicy.RevocationFlag = System.Security.Cryptography.X509Certificates.X509RevocationFlag.EntireChain;
                        bool chainValid = chain.Build(cert);
                        if (chainValid)
                        {
                            UpdateLogger.Log($"[VERIFY] Authenticode signature valid: {cert.Subject}");
                            return true;
                        }
                        else
                        {
                            UpdateLogger.Log($"[VERIFY] Authenticode signature found but chain validation failed: {cert.Subject}");
                            foreach (var status in chain.ChainStatus)
                                UpdateLogger.Log($"[VERIFY]   Chain error: {status.StatusInformation}");
                            // Fall through to hash verification
                        }
                    }
                }
            }
            catch (CryptographicException)
            {
                // No Authenticode signature - fall through to hash verification
                UpdateLogger.Log("[VERIFY] No Authenticode signature found, checking SHA-256 hash...");
            }

            try
            {
                // Step 2: Verify SHA-256 hash file
                string hashFilePath = installerPath + HashFileExtension;
                if (!File.Exists(hashFilePath))
                {
                    UpdateLogger.Log($"[VERIFY] Hash file not found: {hashFilePath}");
                    return false;
                }

                string expectedHash = File.ReadAllText(hashFilePath).Trim().Split(' ')[0].ToUpperInvariant();
                UpdateLogger.Log($"[VERIFY] Expected hash: {expectedHash}");

                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(installerPath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
                    UpdateLogger.Log($"[VERIFY] Actual hash:   {actualHash}");

                    if (string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateLogger.Log("[VERIFY] SHA-256 hash verified successfully");
                        return true;
                    }
                    else
                    {
                        UpdateLogger.Log("[VERIFY] SHA-256 hash MISMATCH - file may be tampered");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[VERIFY] Hash verification error", ex);
                return false;
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
