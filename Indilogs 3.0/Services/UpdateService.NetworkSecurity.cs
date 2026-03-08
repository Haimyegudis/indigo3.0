using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace IndiLogs_3._0.Services
{
    public partial class UpdateService
    {
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

        // ── Authenticode signature verification ──

        private static bool VerifyAuthenticode(string filePath) =>
            AuthenticodeVerifier.VerifySignature(filePath, msg => UpdateLogger.Log($"[AUTO-UPDATE] {msg}"));

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
