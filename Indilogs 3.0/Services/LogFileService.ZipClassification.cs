using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        /// <summary>
        /// Fast scan of a ZIP file to detect which component types it contains.
        /// No extraction or parsing — just reads entry names.
        /// </summary>
        public TabSelectionConfig PreScanZip(string zipPath)
        {
            var config = new TabSelectionConfig();

            try
            {
                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    // Track first/last log entries for time range detection
                    string? firstPlcEntry = null, lastPlcEntry = null;
                    string? firstAppBinEntry = null, lastAppBinEntry = null;
                    string? firstAppTextEntry = null, lastAppTextEntry = null;

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length == 0) continue;
                        string lowerName = entry.FullName.ToLower();

                        if (ZipClassificationHelpers.ShouldSkipEntry(lowerName)) continue;

                        // Globals
                        if (ZipClassificationHelpers.IsGlobalsXmlFile(lowerName))
                        { config.HasGlobals = true; continue; }

                        // Systab
                        if (ZipClassificationHelpers.IsSystabFile(lowerName))
                        { config.HasSystab = true; continue; }

                        // Configuration
                        if (ZipClassificationHelpers.IsConfigurationPath(lowerName))
                        { config.HasConfiguration = true; continue; }

                        // Terminal logs
                        if (ZipClassificationHelpers.IsTerminalLogsPath(lowerName))
                        { config.HasTerminalLogs = true; continue; }

                        // LRS
                        if (ZipClassificationHelpers.IsLrsPath(lowerName))
                        { config.HasLrs = true; continue; }

                        // PLC logs
                        if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            config.HasPlc = true;
                            if (firstPlcEntry == null) firstPlcEntry = entry.FullName;
                            lastPlcEntry = entry.FullName;
                            continue;
                        }

                        // APP text logs (S6)
                        if ((entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                        {
                            config.HasApp = true; config.IsS6 = true;
                            if (firstAppTextEntry == null) firstAppTextEntry = entry.FullName;
                            lastAppTextEntry = entry.FullName;
                            continue;
                        }

                        // APP binary logs (S4-5)
                        if (IsNumericAppFile(entry.Name))
                        {
                            config.HasApp = true;
                            if (firstAppBinEntry == null) firstAppBinEntry = entry.FullName;
                            lastAppBinEntry = entry.FullName;
                            continue;
                        }

                        // Events CSV or XML
                        if (IsEventsFile(entry.Name, out _))
                        { config.HasEvents = true; continue; }

                        // Screenshots
                        if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                        { config.HasScreenshots = true; continue; }

                        // Setup Info (Readme.txt, _setupInfo.json)
                        if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                        { config.HasSetupInfo = true; continue; }

                        // Nested ZIP — scan it too for components
                        if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var innerStream = CopyToMemory(entry))
                                using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false))
                                {
                                    foreach (var innerEntry in innerArchive.Entries)
                                    {
                                        if (innerEntry.Length == 0) continue;
                                        string innerLower = innerEntry.FullName.ToLower();
                                        if (ZipClassificationHelpers.ShouldSkipEntry(innerLower)) continue;

                                        if (ZipClassificationHelpers.IsGlobalsXmlFile(innerLower)) config.HasGlobals = true;
                                        else if (ZipClassificationHelpers.IsSystabFile(innerLower)) config.HasSystab = true;
                                        else if (ZipClassificationHelpers.IsConfigurationPath(innerLower)) config.HasConfiguration = true;
                                        else if (ZipClassificationHelpers.IsTerminalLogsPath(innerLower)) config.HasTerminalLogs = true;
                                        else if (ZipClassificationHelpers.IsLrsPath(innerLower)) config.HasLrs = true;
                                        else if (innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                 !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                            config.HasPlc = true;
                                        else if ((innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                                 (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                                        { config.HasApp = true; config.IsS6 = true; }
                                        else if (IsNumericAppFile(innerEntry.Name))
                                            config.HasApp = true;
                                        else if (IsEventsFile(innerEntry.Name, out _))
                                            config.HasEvents = true;
                                        else if (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                 innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                            config.HasScreenshots = true;
                                        else if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase) ||
                                                 innerEntry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                            config.HasSetupInfo = true;
                                    }
                                }
                            }
                            catch (Exception ex) { AppLogger.Warn($"Nested ZIP scan failed for '{entry.Name}': {ex.Message}"); }
                        }
                    }

                    // Scan time bounds from first/last log entries
                    ScanTimeBounds(archive, config, firstPlcEntry, lastPlcEntry,
                        firstAppBinEntry, lastAppBinEntry, firstAppTextEntry, lastAppTextEntry);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("PreScanZip failed", ex);
            }

            return config;
        }

        /// <summary>
        /// Reads timestamps from the first and last log entries to determine the available time range.
        /// Non-fatal — if anything fails, the time range properties stay null and the UI hides the section.
        /// </summary>
        private void ScanTimeBounds(ZipArchive archive, TabSelectionConfig config,
            string? firstPlcEntry, string? lastPlcEntry,
            string? firstAppBinEntry, string? lastAppBinEntry,
            string? firstAppTextEntry, string? lastAppTextEntry)
        {
            try
            {
                DateTime? earliest = null;
                DateTime? latest = null;

                // Helper to update min/max
                void Track(DateTime dt)
                {
                    if (dt == DateTime.MinValue) return;
                    if (!earliest.HasValue || dt < earliest.Value) earliest = dt;
                    if (!latest.HasValue || dt > latest.Value) latest = dt;
                }

                // --- Binary logs (PLC and APP S4-5): use IndigoLogsReader ---
                void ScanBinaryEntry(string? entryName, bool first)
                {
                    if (entryName == null) return;
                    var zipEntry = archive.GetEntry(entryName);
                    if (zipEntry == null) return;
                    using (var ms = CopyToMemory(zipEntry))
                    {
                        var reader = new IndigoLogsReader(ms);
                        if (first)
                        {
                            // Just read the first entry
                            if (reader.MoveToNext() && reader.Current != null)
                                Track(reader.Current.Time);
                        }
                        else
                        {
                            // Iterate to find the last entry
                            DateTime lastTime = DateTime.MinValue;
                            while (reader.MoveToNext())
                            {
                                if (reader.Current != null)
                                    lastTime = reader.Current.Time;
                            }
                            Track(lastTime);
                        }
                    }
                }

                ScanBinaryEntry(firstPlcEntry, true);
                ScanBinaryEntry(lastPlcEntry, false);
                ScanBinaryEntry(firstAppBinEntry, true);
                ScanBinaryEntry(lastAppBinEntry, false);

                // --- Text APP logs (S6): read first/last lines with IsDateStart ---
                void ScanTextEntry(string? entryName, bool first)
                {
                    if (entryName == null) return;
                    var zipEntry = archive.GetEntry(entryName);
                    if (zipEntry == null) return;
                    using (var ms = CopyToMemory(zipEntry))
                    using (var sr = new StreamReader(ms, Encoding.UTF8, true, 65536))
                    {
                        if (first)
                        {
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (IsDateStart(line))
                                {
                                    Track(ParseTimestampFast(line));
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Iterate all lines to find the last valid timestamp
                            DateTime lastTime = DateTime.MinValue;
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (IsDateStart(line))
                                    lastTime = ParseTimestampFast(line);
                            }
                            Track(lastTime);
                        }
                    }
                }

                ScanTextEntry(firstAppTextEntry, true);
                ScanTextEntry(lastAppTextEntry, false);

                if (earliest.HasValue && latest.HasValue && earliest.Value < latest.Value)
                {
                    config.AvailableStartTime = earliest.Value;
                    config.AvailableEndTime = latest.Value;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ScanTimeBounds failed (non-fatal)", ex);
            }
        }

        // Helper method to identify special terminal files (files located outside the TerminalLogs folder but belonging to terminals)
        private bool IsCustomTerminalLog(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = Path.GetFileName(fileName);

            // Terminal files by specific name
            return name.StartsWith("whel3", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("ecm", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("COM1", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("0001", StringComparison.OrdinalIgnoreCase) ||
                   // IO sensor CSV files (Io-BIM, Io-ECM, Io-PDC, Io-WHEL)
                   name.StartsWith("Io-", StringComparison.OrdinalIgnoreCase) ||
                   // Stability CSV files
                   name.StartsWith("Stab-", StringComparison.OrdinalIgnoreCase) ||
                   // PRE/POST analysis files
                   name.StartsWith("PRE_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("POST_", StringComparison.OrdinalIgnoreCase);
        }

        // Helper method to identify Systab files in the DiagnosticsLogs path
        private static bool IsSystabFile(string lowerFullName)
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
        /// Avoids expensive CopyToMemory on binary files (.dll, .exe, .dat, .arl, etc.)
        /// that no plugin will ever parse.
        /// </summary>
        private static bool IsPluginCandidateExtension(string fileName)
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

        // Helper method to identify Globals XML files in the DataManagement\eCommon\Globals path
        private static bool IsGlobalsXmlFile(string lowerFullName)
        {
            if (string.IsNullOrEmpty(lowerFullName)) return false;
            bool inGlobalsPath = lowerFullName.Contains("/datamanagement/ecommon/globals/") ||
                                 lowerFullName.Contains("\\datamanagement\\ecommon\\globals\\") ||
                                 lowerFullName.StartsWith("datamanagement/ecommon/globals/") ||
                                 lowerFullName.StartsWith("datamanagement\\ecommon\\globals\\");
            return inGlobalsPath && lowerFullName.EndsWith(".xml");
        }

        /// <summary>
        /// Detects numeric APP log files (e.g. "50300001.file", "50300001.file.log.8865")
        /// that use binary format but should go to the APP tab.
        /// Excludes engineGroup files which are PLC logs.
        /// </summary>
        private static bool IsNumericAppFile(string fileName)
        {
            string name = Path.GetFileName(fileName).ToLower();

            // Must not be an engineGroup file (those are PLC)
            if (name.Contains("enginegroup")) return false;

            // Match patterns: "50300001.file", "50300001.file.log.8865"
            int dotFileIdx = name.IndexOf(".file");
            if (dotFileIdx <= 0) return false;

            // Check that the part before ".file" ends with digits
            string prefix = name.Substring(0, dotFileIdx);
            return prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]);
        }
    }
}
