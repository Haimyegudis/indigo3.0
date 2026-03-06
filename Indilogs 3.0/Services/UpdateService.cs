using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services
{
    public class UpdateService : Interfaces.IUpdateService
    {
        // Server paths — read from appsettings.json (sits next to the exe)
        private static readonly string VersionFileUrl = "";
        private static readonly string InstallerFolder = "";
        private const string ExePattern = "IndiLogs3.0_*.exe";

        private readonly Interfaces.IDialogService? _dialogService;
        private readonly Interfaces.IDispatcher? _dispatcher;

        public UpdateService(Interfaces.IDialogService? dialogService = null, Interfaces.IDispatcher? dispatcher = null)
        {
            _dialogService = dialogService;
            _dispatcher = dispatcher;
        }

        static UpdateService()
        {
            try
            {
                // Environment.ProcessPath points to the actual exe even in single-file mode
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

                // Load base settings, then overlay with local settings (which are gitignored)
                VersionFileUrl = "";
                InstallerFolder = "";

                string settingsPath = Path.Combine(exeDir, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), AppConstants.SafeJsonDocumentOptions);
                    VersionFileUrl = doc.RootElement.TryGetProperty("UpdateVersionFile", out var vf) ? vf.GetString() ?? "" : "";
                    InstallerFolder = doc.RootElement.TryGetProperty("UpdateInstallerFolder", out var inf) ? inf.GetString() ?? "" : "";
                }

                // Local overrides (not committed to source control)
                string localPath = Path.Combine(exeDir, "appsettings.local.json");
                if (File.Exists(localPath))
                {
                    using var localDoc = JsonDocument.Parse(File.ReadAllText(localPath), AppConstants.SafeJsonDocumentOptions);
                    if (localDoc.RootElement.TryGetProperty("UpdateVersionFile", out var lvf) && !string.IsNullOrEmpty(lvf.GetString()))
                        VersionFileUrl = lvf.GetString() ?? "";
                    if (localDoc.RootElement.TryGetProperty("UpdateInstallerFolder", out var linf) && !string.IsNullOrEmpty(linf.GetString()))
                        InstallerFolder = linf.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateService: Failed to read appsettings: {ex.Message}");
                VersionFileUrl = "";
                InstallerFolder = "";
            }
        }

        public async Task CheckForUpdatesSimpleAsync()
        {
            try
            {
                UpdateLogger.Log("========== UPDATE CHECK STARTED ==========");
                UpdateLogger.Log($"Checking path: {VersionFileUrl}");

                // Get current version from assembly
                Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                UpdateLogger.Log($"Current version: {currentVersion}");

                // Offload network I/O to a thread-pool thread
                var (serverVersionText, serverVersion) = await Task.Run(() =>
                {
                    // Establish connection to the network share (needed for hidden admin shares)
                    EnsureShareConnection(InstallerFolder);

                    // Read version file directly — File.Exists() returns false on hidden
                    // admin shares (softwareqa$) even when the file is accessible.
                    string versionText;
                    try
                    {
                        versionText = File.ReadAllText(VersionFileUrl).Trim();
                    }
                    catch (Exception readEx)
                    {
                        UpdateLogger.Log($"[ERROR] Cannot read version file: {readEx.Message}");
                        return ((string?)null, (Version?)null);
                    }
                    UpdateLogger.Log($"Server version text (raw): '{versionText}'");
                    UpdateLogger.Log($"Server version text length: {versionText.Length}");

                    if (!Version.TryParse(versionText, out Version? parsed))
                    {
                        UpdateLogger.Log($"[ERROR] Failed to parse server version: '{versionText}'");
                        UpdateLogger.Log("Expected format: X.X.X.X (e.g., 1.0.0.2)");
                        return ((string?)null, (Version?)null);
                    }
                    return (versionText, (Version?)parsed);
                }).ConfigureAwait(false);

                if (serverVersionText == null || serverVersion == null) return;

                UpdateLogger.Log($"Server version (parsed): {serverVersion}");
                UpdateLogger.Log($"Comparison: Server ({serverVersion}) > Current ({currentVersion}) = {serverVersion > currentVersion}");

                // Compare versions
                if (serverVersion > currentVersion)
                {
                    UpdateLogger.Log($"[UPDATE AVAILABLE] New version: {serverVersion}");

                    // Show dialog on UI thread
                    await (_dispatcher?.InvokeAsync(() =>
                    {
                        var result = _dialogService != null
                            ? _dialogService.ShowConfirm(
                                $"A new version is available!\n\n" +
                                $"Current version: {currentVersion}\n" +
                                $"New version: {serverVersion}\n\n" +
                                "Do you want to download and install the update now?",
                                "IndiLogs Update Available")
                            : DialogResult.No;

                        if (result == DialogResult.Yes)
                        {
                            DownloadAndInstallUpdate(serverVersion);
                        }
                    }) ?? Task.CompletedTask);
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
        }

        private void DownloadAndInstallUpdate(Version serverVersion)
        {
            try
            {
                UpdateLogger.Log("[AUTO-UPDATE] Locating new exe on network share...");

                string? serverExePath = FindExeOnServer(serverVersion);
                if (string.IsNullOrEmpty(serverExePath))
                {
                    UpdateLogger.Log("[ERROR] Could not find update exe on server");
                    _dialogService?.ShowWarning(
                        "Could not find the update file on the server.\n\n" +
                        $"Please open this folder and copy the exe manually:\n{InstallerFolder}",
                        "Update Error");
                    return;
                }

                // Current exe path
                string? currentExePath = Environment.ProcessPath;
                string? currentDir = Path.GetDirectoryName(currentExePath);
                string? currentExeName = Path.GetFileName(currentExePath);

                // Copy new exe to temp location next to current exe
                string tempExePath = Path.Combine(currentDir!, $"IndiLogs3.0_update_{serverVersion}.tmp");
                UpdateLogger.Log($"[AUTO-UPDATE] Copying from server: {serverExePath}");
                File.Copy(serverExePath, tempExePath, true);
                UpdateLogger.Log($"[AUTO-UPDATE] Copied to: {tempExePath}");

                // Verify Authenticode signature of the downloaded binary — block if invalid
                if (!VerifyAuthenticode(tempExePath))
                {
                    UpdateLogger.Log("[AUTO-UPDATE] BLOCKED: Update binary has no valid Authenticode signature. Aborting update.");
                    try { File.Delete(tempExePath); } catch (Exception ex) { AppLogger.Warn($"Failed to delete temp update file: {ex.Message}"); }
                    return;
                }

                // Write a small .cmd script that waits, replaces, and relaunches
                string cmdPath = Path.Combine(currentDir!, "update.cmd");
                string cmdContent =
                    "@echo off\r\n" +
                    "echo Updating IndiLogs 3.0...\r\n" +
                    "timeout /t 2 /nobreak\r\n" +
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

                _dispatcher?.Post(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("[AUTO-UPDATE ERROR]", ex);
                _dialogService?.ShowError(
                    $"Failed to apply update:\n{ex.Message}\n\n" +
                    $"Please open this folder and copy the exe manually:\n{InstallerFolder}",
                    "Update Error");
            }
        }

        private string? FindExeOnServer(Version version)
        {
            // Try the known filename: IndiLogs3.0_{version}.exe
            string expectedPath = Path.Combine(InstallerFolder, $"IndiLogs3.0_{version}.exe");
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
        public static Version? GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        // ── Network share connection via Windows API ──

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? username, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string? lpLocalName;
            public string? lpRemoteName;
            public string? lpComment;
            public string? lpProvider;
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

                UpdateLogger.Log($"[NETWORK] Default credentials failed (code {result}), trying stored service account...");

                // Try DPAPI-encrypted service account credentials from local config
                var (username, password) = LoadEncryptedCredentials();
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    result = WNetAddConnection2(ref nr, password, username, CONNECT_TEMPORARY);

                    if (result == 0 || result == 1219)
                        UpdateLogger.Log($"[NETWORK] Connected to {uncFolder} via stored service account");
                    else
                        UpdateLogger.Log($"[NETWORK] Stored service account connection failed (code {result})");
                }
                else
                {
                    UpdateLogger.Log("[NETWORK] No stored credentials found. Use SetUpdateCredentials() to configure.");
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"[NETWORK] Connection attempt failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies that a file has a valid Authenticode signature with a trusted certificate chain.
        /// Returns true if the signature is valid, false otherwise.
        /// </summary>
        private static bool VerifyAuthenticode(string filePath)
        {
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(filePath);
                if (cert != null)
                {
                    using (var chain = new System.Security.Cryptography.X509Certificates.X509Chain())
                    {
                        chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
                        chain.ChainPolicy.RevocationFlag = System.Security.Cryptography.X509Certificates.X509RevocationFlag.EntireChain;
                        if (chain.Build(cert))
                        {
                            UpdateLogger.Log($"[AUTO-UPDATE] Binary signed by (chain valid): {cert.Subject}");
                            return true;
                        }
                        UpdateLogger.Log($"[AUTO-UPDATE] Certificate chain invalid: {cert.Subject}");
                    }
                }
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                UpdateLogger.Log("[AUTO-UPDATE] Binary has no Authenticode signature.");
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"[AUTO-UPDATE] Signature verification error: {ex.Message}");
            }
            return false;
        }

        // ── DPAPI-encrypted credential storage for network share access ──

        private static string CredentialFilePath
        {
            get
            {
                Directory.CreateDirectory(AppPaths.Root);
                return AppPaths.UpdateCredentials;
            }
        }

        /// <summary>
        /// Stores network share credentials encrypted with DPAPI (CurrentUser scope).
        /// Call once during initial setup; the encrypted file persists across runs.
        /// </summary>
        public static void SetUpdateCredentials(string username, string password)
        {
            try
            {
                string payload = $"{username}\n{password}";
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(payload), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(CredentialFilePath, encrypted);
                UpdateLogger.Log("[CREDENTIALS] Service account credentials saved (DPAPI-encrypted).");
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"[CREDENTIALS] Failed to save credentials: {ex.Message}");
            }
        }

        private static (string? username, string? password) LoadEncryptedCredentials()
        {
            try
            {
                string path = CredentialFilePath;
                if (!File.Exists(path))
                    return (null, null);

                byte[] encrypted = File.ReadAllBytes(path);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string payload = Encoding.UTF8.GetString(decrypted);
                int sep = payload.IndexOf('\n');
                if (sep < 0) return (null, null);

                return (payload.Substring(0, sep), payload.Substring(sep + 1));
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"[CREDENTIALS] Failed to load credentials: {ex.Message}");
                return (null, null);
            }
        }
    }
}
