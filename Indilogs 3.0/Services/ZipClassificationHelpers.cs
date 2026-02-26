using System;
using System.IO;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Static helpers for classifying files inside ZIP archives.
    /// Extracted from LogFileService to reduce file size and improve maintainability.
    /// </summary>
    internal static class ZipClassificationHelpers
    {
        /// <summary>
        /// Returns true if the file is a custom terminal log (outside TerminalLogs folder).
        /// Matches: whel3*, ecm*, COM1*, 0001*, Io-*, Stab-*, PRE_*, POST_*
        /// </summary>
        public static bool IsCustomTerminalLog(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string name = Path.GetFileName(fileName);

            return name.StartsWith("whel3", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("ecm", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("COM1", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("0001", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Io-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Stab-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("PRE_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("POST_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the file is a Systab file in DiagnosticsLogs path.
        /// </summary>
        public static bool IsSystabFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inDiagPath = lowerFullName.Contains("/diagnosticslogs/") ||
                              lowerFullName.Contains("\\diagnosticslogs\\") ||
                              lowerFullName.StartsWith("diagnosticslogs/") ||
                              lowerFullName.StartsWith("diagnosticslogs\\");
            string fileName = Path.GetFileName(lowerFullName);
            return inDiagPath && fileName.StartsWith("systab_") && fileName.EndsWith(".txt");
        }

        /// <summary>
        /// Returns true if the file extension is a text/log format that plugins might handle.
        /// Avoids expensive I/O on binary files (.dll, .exe, .dat, .arl, etc.)
        /// </summary>
        public static bool IsPluginCandidateExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) return false;
            switch (ext.ToLowerInvariant())
            {
                case ".log": case ".txt": case ".csv": case ".file":
                case ".json": case ".xml": case ".cfg": case ".ini":
                case ".config": case ".tsv":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if the file is a Globals XML in DataManagement\eCommon\Globals path.
        /// </summary>
        public static bool IsGlobalsXmlFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inGlobalsPath = lowerFullName.Contains("/datamanagement/ecommon/globals/") ||
                                 lowerFullName.Contains("\\datamanagement\\ecommon\\globals\\") ||
                                 lowerFullName.StartsWith("datamanagement/ecommon/globals/") ||
                                 lowerFullName.StartsWith("datamanagement\\ecommon\\globals\\");
            return inGlobalsPath && lowerFullName.EndsWith(".xml");
        }

        /// <summary>
        /// Returns true if the entry is in a TerminalLogs folder path.
        /// </summary>
        public static bool IsTerminalLogsPath(string lowerFullName)
        {
            return lowerFullName.Contains("/terminallogs/") ||
                   lowerFullName.Contains("\\terminallogs\\") ||
                   lowerFullName.Contains("\\terminallogs/") ||
                   lowerFullName.Contains("/terminallogs\\") ||
                   lowerFullName.StartsWith("terminallogs/") ||
                   lowerFullName.StartsWith("terminallogs\\");
        }

        /// <summary>
        /// Returns true if the entry is in an LRS folder path.
        /// </summary>
        public static bool IsLrsPath(string lowerFullName)
        {
            return lowerFullName.Contains("/lrs/") ||
                   lowerFullName.Contains("\\lrs\\") ||
                   lowerFullName.Contains("\\lrs/") ||
                   lowerFullName.Contains("/lrs\\") ||
                   lowerFullName.StartsWith("lrs/") ||
                   lowerFullName.StartsWith("lrs\\");
        }

        /// <summary>
        /// Returns true if the entry is in a Configuration folder path.
        /// </summary>
        public static bool IsConfigurationPath(string lowerFullName)
        {
            return lowerFullName.Contains("/configuration/") ||
                   lowerFullName.Contains("\\configuration\\") ||
                   lowerFullName.Contains("\\configuration/") ||
                   lowerFullName.Contains("/configuration\\") ||
                   lowerFullName.StartsWith("configuration/") ||
                   lowerFullName.StartsWith("configuration\\");
        }

        /// <summary>
        /// Returns true if file should be skipped (backup, old, temp, archive folders).
        /// </summary>
        public static bool ShouldSkipEntry(string lowerFullName)
        {
            return lowerFullName.Contains("/backup/") || lowerFullName.Contains("\\backup\\") ||
                   lowerFullName.Contains("/old/") || lowerFullName.Contains("\\old\\") ||
                   lowerFullName.Contains("/temp/") || lowerFullName.Contains("\\temp\\") ||
                   lowerFullName.Contains("/archive/") || lowerFullName.Contains("\\archive\\");
        }
    }
}
