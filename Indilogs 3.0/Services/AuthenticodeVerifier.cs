using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IndiLogs_3._0.Services
{
    internal static class AuthenticodeVerifier
    {
        // Trusted organization names parsed from the DN O= field.
        private static readonly string[] TrustedOrgs = { "HP", "HP Inc", "HP Inc.", "Hewlett-Packard", "Hewlett Packard", "HP Indigo" };

        public static bool IsTrustedSubject(string subject)
        {
            // Parse the O= (Organization) field from the X.500 distinguished name
            // instead of doing a loose substring match on the entire subject string.
            string? org = ParseDnField(subject, "O");
            if (org != null)
            {
                foreach (var trusted in TrustedOrgs)
                {
                    if (org.Equals(trusted, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        internal static string? ParseDnField(string dn, string fieldName)
        {
            // Handles: O=HP Inc, O="HP Inc", O = HP Inc
            if (string.IsNullOrEmpty(dn)) return null;
            string prefix = fieldName + "=";
            int idx = dn.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // Ensure it's at the start or preceded by comma/space
            if (idx > 0 && dn[idx - 1] != ',' && dn[idx - 1] != ' ')
                return null;

            int valueStart = idx + prefix.Length;
            while (valueStart < dn.Length && dn[valueStart] == ' ') valueStart++;
            if (valueStart >= dn.Length) return null;

            string value;
            if (dn[valueStart] == '"')
            {
                int end = dn.IndexOf('"', valueStart + 1);
                value = end > 0 ? dn.Substring(valueStart + 1, end - valueStart - 1) : dn.Substring(valueStart + 1);
            }
            else
            {
                int end = dn.IndexOf(',', valueStart);
                value = end > 0 ? dn.Substring(valueStart, end - valueStart) : dn.Substring(valueStart);
            }
            return value.Trim();
        }

        public static bool VerifySignature(string filePath, Action<string> log)
        {
            try
            {
                var cert = X509CertificateLoader.LoadCertificateFromFile(filePath);
                if (cert != null)
                {
                    using var chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                    if (chain.Build(cert))
                    {
                        if (!IsTrustedSubject(cert.Subject))
                        {
                            log($"Binary signed by untrusted publisher: {cert.Subject}");
                            return false;
                        }
                        log($"Binary signed by (chain valid): {cert.Subject}");
                        return true;
                    }
                    log($"Certificate chain invalid: {cert.Subject}");
                }
            }
            catch (CryptographicException)
            {
                log("Binary has no Authenticode signature.");
            }
            catch (Exception ex)
            {
                log($"Signature verification error: {ex.Message}");
            }
            return false;
        }
    }
}
