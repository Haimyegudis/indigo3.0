using Indigo.Infra.ICL.Core.Logging;
using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public partial class LogFileService
    {
        private void ProcessNestedZipEntries(
            ZipArchive archive,
            List<string> innerZipEntryNames,
            List<ZipEntryData> filesToProcess,
            LogSessionData session,
            TabSelectionConfig sel,
            bool outerHasMainLog,
            bool outerHasAppLogs,
            ref bool hasBinaryAppLogs,
            ref string detectedSwVersion,
            ref string detectedPlcVersion,
            ConcurrentBag<BitmapImage> screenshotsBag,
            ConcurrentBag<BitmapImage> nonInfoScreenshotsBag)
        {
            foreach (var innerZipName in innerZipEntryNames)
            {
                try
                {
                    var outerEntry = archive.GetEntry(innerZipName);
                    if (outerEntry == null) continue;
                    using (var innerStream = CopyToMemory(outerEntry))
                    using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false))
                    {
                        foreach (var innerEntry in innerArchive.Entries)
                        {
                            if (innerEntry.Length == 0) continue;
                            string innerLower = innerEntry.FullName.ToLower();

                            // Skip backup/temp/archive folders
                            if (innerLower.Contains("/backup/") || innerLower.Contains("\\backup\\") ||
                                innerLower.Contains("/old/") || innerLower.Contains("\\old\\") ||
                                innerLower.Contains("/temp/") || innerLower.Contains("\\temp\\") ||
                                innerLower.Contains("/archive/") || innerLower.Contains("\\archive\\"))
                                continue;

                            // Prefix with inner ZIP name for collision avoidance
                            string prefixedName = $"{Path.GetFileNameWithoutExtension(innerZipName)}/{innerEntry.Name}";

                            // EM_Statistics CSV (Gantt chart)
                            if (ZipClassificationHelpers.IsEmStatisticsFile(innerEntry.FullName))
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(session.EmStatisticsCsvContent))
                                    {
                                        using (var emStream = innerEntry.Open())
                                        using (var emReader = new StreamReader(emStream))
                                            session.EmStatisticsCsvContent = emReader.ReadToEnd();
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP EM_Statistics CSV failed", ex); }
                                continue;
                            }

                            // Globals XML
                            if (IsGlobalsXmlFile(innerLower))
                            {
                                if (!sel.LoadGlobals) continue;
                                try
                                {
                                    string content = ReadTextFromEntry(innerEntry);
                                    if (!session.GlobalsFiles.ContainsKey(prefixedName))
                                        session.GlobalsFiles.Add(prefixedName, content);
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP globals file failed", ex); }
                                continue;
                            }

                            // Systab files
                            if (IsSystabFile(innerLower))
                            {
                                if (!sel.LoadSystab) continue;
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(innerEntry.Name).ToLower();
                                    string systabKey = null;
                                    if (fileNameOnly.Contains("saved")) systabKey = "saved";
                                    else if (fileNameOnly.Contains("default")) systabKey = "default";
                                    else if (fileNameOnly.Contains("minimum")) systabKey = "minimum";
                                    else if (fileNameOnly.Contains("maximum")) systabKey = "maximum";

                                    if (systabKey != null && !session.SystabFiles.ContainsKey(systabKey))
                                    {
                                        // ReadTextFromEntry auto-detects UTF-16 LE BOM (.reg format)
                                        string content = ReadTextFromEntry(innerEntry);
                                        session.SystabFiles.Add(systabKey, content);
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP systab file failed", ex); }
                                continue;
                            }

                            // Configuration files
                            bool isConfig = innerLower.Contains("/configuration/") || innerLower.Contains("\\configuration\\") ||
                                            innerLower.StartsWith("configuration/") || innerLower.StartsWith("configuration\\");
                            if (isConfig)
                            {
                                if (!sel.LoadConfiguration) continue;
                                try
                                {
                                    string fName = Path.GetFileName(innerEntry.Name);
                                    if (fName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.DatabaseFiles.ContainsKey(fName))
                                            session.DatabaseFiles.Add(fName, ReadBytesFromEntry(innerEntry));
                                    }
                                    else
                                    {
                                        if (!session.ConfigurationFiles.ContainsKey(prefixedName))
                                            session.ConfigurationFiles.Add(prefixedName, ReadTextFromEntry(innerEntry));
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP configuration file failed", ex); }
                                continue;
                            }

                            // Terminal logs (TerminalLogs + LRS folders)
                            bool isTerminal = innerLower.Contains("/terminallogs/") || innerLower.Contains("\\terminallogs\\") ||
                                              innerLower.StartsWith("terminallogs/") || innerLower.StartsWith("terminallogs\\");

                            // Skip terminal logs if user unchecked them
                            if (isTerminal && !sel.LoadTerminalLogs) continue;

                            // LRS folder — treat as terminal, exclude engineGroupA and nested ZIPs
                            bool innerIsLrs = innerLower.Contains("/lrs/") || innerLower.Contains("\\lrs\\") ||
                                              innerLower.StartsWith("lrs/") || innerLower.StartsWith("lrs\\");

                            // Skip LRS files if user unchecked them
                            if (innerIsLrs && !sel.LoadLrs) continue;

                            if (innerIsLrs &&
                                innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) < 0 &&
                                !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                isTerminal = true;
                            }

                            if (isTerminal)
                            {
                                try
                                {
                                    string fName = Path.GetFileName(innerEntry.Name);
                                    if (!string.IsNullOrEmpty(fName))
                                    {
                                        string ext = Path.GetExtension(fName);
                                        string key = prefixedName;
                                        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                        {
                                            // CSV: store as raw bytes (deferred string conversion)
                                            if (!session.TerminalCsvBytes.ContainsKey(key))
                                                session.TerminalCsvBytes.Add(key, ReadBytesFromEntry(innerEntry));
                                        }
                                        else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                                 ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                        {
                                            // Text/log: store as string for display
                                            if (!session.TerminalLogFiles.ContainsKey(key))
                                                session.TerminalLogFiles.Add(key, ReadTextFromEntry(innerEntry));
                                        }
                                        // Skip .arl and other formats
                                    }
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP terminal log failed", ex); }
                                continue;
                            }

                            // Main PLC logs — SKIP if outer ZIP already has PLC logs (avoid date mixing)
                            var innerData = new ZipEntryData { Name = innerEntry.Name };
                            bool innerShouldProcess = false;

                            if (innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!sel.LoadPlc) continue;
                                if (outerHasMainLog)
                                {
                                    AppLogger.Info($"[Load] Skipping inner ZIP PLC log: {innerEntry.Name} (outer ZIP already has PLC logs)");
                                    continue;
                                }
                                innerData.Type = FileType.MainLog;
                                innerShouldProcess = true;
                            }
                            // APP dev logs — SKIP if outer ZIP already has APP logs
                            else if ((innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                     (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                            {
                                if (!sel.LoadApp) continue;
                                if (outerHasAppLogs)
                                {
                                    continue; // Skip — outer ZIP's APP logs take priority
                                }
                                innerData.Type = FileType.AppDevLog;
                                innerShouldProcess = true;
                            }
                            // APP binary logs — SKIP if outer ZIP already has APP logs
                            else if (IsNumericAppFile(innerEntry.Name))
                            {
                                if (!sel.LoadApp) continue;
                                if (outerHasAppLogs)
                                {
                                    continue; // Skip — outer ZIP's APP logs take priority
                                }
                                innerData.Type = FileType.AppBinaryLog;
                                innerShouldProcess = true;
                                hasBinaryAppLogs = true;
                            }
                            // Events CSV or XML
                            else if (IsEventsFile(innerEntry.Name, out var innerEvtType2))
                            {
                                if (!sel.LoadEvents) continue;
                                innerData.Type = innerEvtType2;
                                innerShouldProcess = true;
                            }
                            // DB files (skip if user unchecked Configuration)
                            else if (innerEntry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!sel.LoadConfiguration) continue;
                                try
                                {
                                    string fName = Path.GetFileName(innerEntry.Name);
                                    if (!session.DatabaseFiles.ContainsKey(fName))
                                        session.DatabaseFiles.Add(fName, ReadBytesFromEntry(innerEntry));
                                }
                                catch (Exception ex) { AppLogger.Error("Reading inner ZIP database file failed", ex); }
                                continue;
                            }
                            // Screenshots
                            else if (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                     innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!sel.LoadScreenshots) continue;
                                var bmp = LoadBitmapFromZip(innerEntry);
                                if (bmp != null)
                                {
                                    bool isInfoPath = innerLower.Contains("/info/") || innerLower.Contains("\\info\\") ||
                                                      innerLower.StartsWith("info/") || innerLower.StartsWith("info\\");
                                    if (isInfoPath)
                                        screenshotsBag.Add(bmp);
                                    else
                                        nonInfoScreenshotsBag.Add(bmp);
                                }
                                continue;
                            }
                            // Readme — always loaded (press config + versions)
                            else if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrEmpty(session.PressConfiguration))
                                {
                                    session.PressConfiguration = ReadTextFromEntry(innerEntry);
                                    var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                                    if (sw != "Unknown") detectedSwVersion = sw;
                                    if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                                }
                                continue;
                            }

                            if (innerShouldProcess)
                            {
                                innerData.Stream = CopyToMemory(innerEntry);
                                filesToProcess.Add(innerData);
                            }
                        }
                    }

                    AppLogger.Info($"Processed inner ZIP: {innerZipName}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Error processing nested ZIP {innerZipName}", ex);
                }
            }
        }
    }
}
