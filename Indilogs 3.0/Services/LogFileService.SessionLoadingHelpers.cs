using Indigo.Infra.ICL.Core.Logging;
using IndiLogs.PluginAPI;
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
        private void ClassifyZipEntries(
            ZipArchive archive,
            LogSessionData session,
            TabSelectionConfig sel,
            List<ZipEntryData> filesToProcess,
            List<string> innerZipEntryNames,
            ConcurrentBag<BitmapImage> screenshotsBag,
            ConcurrentBag<BitmapImage> nonInfoScreenshotsBag,
            ref bool hasBinaryAppLogs,
            ref string detectedSwVersion,
            ref string detectedPlcVersion)
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0) continue;

                string lowerName = entry.FullName.ToLower();

                // Aggressive filtering
                if (lowerName.Contains("/backup/") || lowerName.Contains("\\backup\\") ||
                    lowerName.Contains("/old/") || lowerName.Contains("\\old\\") ||
                    lowerName.Contains("/temp/") || lowerName.Contains("\\temp\\") ||
                    lowerName.Contains("/archive/") || lowerName.Contains("\\archive\\"))
                {
                    continue;
                }

                bool shouldProcess = false;
                var entryData = new ZipEntryData { Name = entry.Name };

                // 0-em. EM_Statistics CSV (Gantt chart)
                if (ZipClassificationHelpers.IsEmStatisticsFile(entry.FullName))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(session.EmStatisticsCsvContent))
                        {
                            using (var emStream = entry.Open())
                            using (var emReader = new StreamReader(emStream))
                                session.EmStatisticsCsvContent = emReader.ReadToEnd();
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Reading EM_Statistics CSV failed", ex); }
                    continue;
                }

                // 0. Check for Globals XML files in DataManagement\eCommon\Globals folder (before Configuration)
                if (IsGlobalsXmlFile(lowerName))
                {
                    if (!sel.LoadGlobals) continue;
                    try
                    {
                        string fileNameOnly = Path.GetFileName(entry.Name);
                        string content = ReadTextFromEntry(entry);
                        if (!session.GlobalsFiles.ContainsKey(fileNameOnly))
                            session.GlobalsFiles.Add(fileNameOnly, content);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Reading globals XML file failed", ex);
                    }
                    continue;
                }

                // 0b. Systab files in DiagnosticsLogs folder (systab_saved.txt, systab_default.txt, etc.)
                if (IsSystabFile(lowerName))
                {
                    if (!sel.LoadSystab) continue;
                    try
                    {
                        string fileNameOnly = Path.GetFileName(entry.Name).ToLower();
                        string? systabKey = null;
                        if (fileNameOnly.Contains("saved")) systabKey = "saved";
                        else if (fileNameOnly.Contains("default")) systabKey = "default";
                        else if (fileNameOnly.Contains("minimum")) systabKey = "minimum";
                        else if (fileNameOnly.Contains("maximum")) systabKey = "maximum";

                        if (systabKey != null && !session.SystabFiles.ContainsKey(systabKey))
                        {
                            // ReadTextFromEntry auto-detects UTF-16 LE BOM (.reg format)
                            string content = ReadTextFromEntry(entry);
                            session.SystabFiles.Add(systabKey, content);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Systab extraction failed", ex);
                    }
                    continue;
                }

                // 1. Identify Configuration files
                bool isConfigFile = lowerName.Contains("/configuration/") ||
                                    lowerName.Contains("\\configuration\\") ||
                                    lowerName.Contains("\\configuration/") ||
                                    lowerName.Contains("/configuration\\") ||
                                    lowerName.StartsWith("configuration/") ||
                                    lowerName.StartsWith("configuration\\");

                if (isConfigFile)
                {
                    if (!sel.LoadConfiguration) continue;
                    try
                    {
                        string fileNameOnly = Path.GetFileName(entry.Name);

                        if (fileNameOnly.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                        {
                            byte[] dbBytes = ReadBytesFromEntry(entry);
                            if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                                session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                        }
                        else
                        {
                            string content = ReadTextFromEntry(entry);
                            if (!session.ConfigurationFiles.ContainsKey(fileNameOnly))
                                session.ConfigurationFiles.Add(fileNameOnly, content);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Reading configuration file failed", ex);
                    }
                    continue;
                }

                // 1b. Terminal log files (files in TerminalLogs and LRS folders)
                bool isTerminalLog = lowerName.Contains("/terminallogs/") ||
                                     lowerName.Contains("\\terminallogs\\") ||
                                     lowerName.Contains("\\terminallogs/") ||
                                     lowerName.Contains("/terminallogs\\") ||
                                     lowerName.StartsWith("terminallogs/") ||
                                     lowerName.StartsWith("terminallogs\\");

                // Skip terminal logs if user unchecked them
                if (isTerminalLog && !sel.LoadTerminalLogs) continue;

                // LRS folder — treat as terminal path, but exclude engineGroupA logs and nested ZIPs
                bool isLrsPath = lowerName.Contains("/lrs/") ||
                                 lowerName.Contains("\\lrs\\") ||
                                 lowerName.Contains("\\lrs/") ||
                                 lowerName.Contains("/lrs\\") ||
                                 lowerName.StartsWith("lrs/") ||
                                 lowerName.StartsWith("lrs\\");

                // Skip LRS files if user unchecked them
                if (isLrsPath && !sel.LoadLrs) continue;

                if (isLrsPath &&
                    entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    isTerminalLog = true;
                }

                if (isTerminalLog)
                {
                    try
                    {
                        string fileNameOnly = Path.GetFileName(entry.Name);
                        if (!string.IsNullOrEmpty(fileNameOnly))
                        {
                            string ext = Path.GetExtension(fileNameOnly);
                            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                            {
                                // CSV files: store as raw bytes (deferred string conversion)
                                if (!session.TerminalCsvBytes.ContainsKey(fileNameOnly))
                                    session.TerminalCsvBytes.Add(fileNameOnly, ReadBytesFromEntry(entry));
                            }
                            else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                     ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                            {
                                // Text/log files: store as string for TERMINALS tab display
                                if (!session.TerminalLogFiles.ContainsKey(fileNameOnly))
                                    session.TerminalLogFiles.Add(fileNameOnly, ReadTextFromEntry(entry));
                            }
                            // Skip .arl and other non-essential formats entirely
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Reading terminal log file failed", ex);
                    }
                    continue;
                }

                // 2. Main logs (exclude .zip — those are nested archives, not log files)
                if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sel.LoadPlc) continue;
                    entryData.Type = FileType.MainLog;
                    shouldProcess = true;
                }
                // 3. Application logs
                else if ((entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                         (lowerName.Contains("indigologs/logger files") || lowerName.Contains("indigologs\\logger files")))
                {
                    if (!sel.LoadApp) continue;
                    entryData.Type = FileType.AppDevLog;
                    shouldProcess = true;
                }
                // 3b. APP binary logs
                else if (IsNumericAppFile(entry.Name))
                {
                    if (!sel.LoadApp) continue;
                    entryData.Type = FileType.AppBinaryLog;
                    shouldProcess = true;
                    hasBinaryAppLogs = true;
                }
                // 4. Events CSV or XML
                else if (IsEventsFile(entry.Name, out var outerEvtType2))
                {
                    if (!sel.LoadEvents) continue;
                    entryData.Type = outerEvtType2;
                    shouldProcess = true;
                }
                // 5. .db files (skip if user unchecked Configuration)
                else if (entry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sel.LoadConfiguration) continue;
                    try
                    {
                        string fileNameOnly = Path.GetFileName(entry.Name);
                        byte[] dbBytes = ReadBytesFromEntry(entry);
                        if (!session.DatabaseFiles.ContainsKey(fileNameOnly))
                            session.DatabaseFiles.Add(fileNameOnly, dbBytes);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Reading database file from ZIP failed", ex);
                    }
                    continue;
                }
                // 6. Screenshots - split into Info vs non-Info for S4-5 filtering
                else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                         entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sel.LoadScreenshots) continue;
                    var bmp = LoadBitmapFromZip(entry);
                    if (bmp != null)
                    {
                        bool isInfoPath = lowerName.Contains("/info/") || lowerName.Contains("\\info\\") ||
                                          lowerName.StartsWith("info/") || lowerName.StartsWith("info\\");
                        if (isInfoPath)
                            screenshotsBag.Add(bmp);
                        else
                            nonInfoScreenshotsBag.Add(bmp);
                    }
                    continue;
                }
                // 7. Info files — Readme.txt always loaded (press config + versions)
                else if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                {
                    session.PressConfiguration = ReadTextFromEntry(entry);
                    var (sw, plc) = ParseReadmeVersions(session.PressConfiguration);
                    if (sw != "Unknown") detectedSwVersion = sw;
                    if (plc != "Unknown" && detectedPlcVersion == "Unknown") detectedPlcVersion = plc;
                    continue;
                }
                else if (entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sel.LoadSetupInfo) continue;
                    session.SetupInfo = ReadTextFromEntry(entry);
                    string? plcVer = ExtractPlcVersionFromSetupInfo(session.SetupInfo);
                    if (!string.IsNullOrEmpty(plcVer)) detectedPlcVersion = plcVer;
                    continue;
                }
                // 8b. Nested ZIP — record name for deferred extraction (saves 400MB+ RAM)
                else if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    innerZipEntryNames.Add(entry.FullName);
                    continue;
                }
                else
                {
                    // 8. Plugin fallback — offer unrecognised ZIP entries to registered plugins
                    // OPTIMIZATION: only attempt plugin fallback for text-like extensions
                    // to avoid expensive CopyToMemory on large binary files (.dll, .exe, .dat, .arl, etc.)
                    if (_pluginLoader != null && _pluginLoader.Plugins.Count > 0 &&
                        IsPluginCandidateExtension(entry.Name))
                    {
                        var ms = CopyToMemory(entry);
                        string[] sample = ReadSampleLines(ms, 20);
                        ILogFilePlugin? plugin = FindPlugin(entry.Name, sample);
                        if (plugin != null)
                        {
                            entryData.Type    = FileType.Plugin;
                            entryData.Plugin  = plugin;
                            entryData.Stream  = ms;  // ownership transferred
                            entryData.Context = new ParseContext
                            {
                                FileName     = entry.Name,
                                IsInsideZip  = true,
                                ZipEntryPath = entry.FullName
                            };
                            filesToProcess.Add(entryData);
                            if (session.PluginColumns == null)
                            {
                                try { session.PluginColumns = plugin.GetColumns(); }
                                catch (Exception ex) { AppLogger.Error("Plugin GetColumns failed", ex); }
                            }
                        }
                        else
                        {
                            ms.Dispose();  // no plugin claimed it — free the buffer
                        }
                    }
                    continue;  // do NOT fall through to the shouldProcess block below
                }

                if (shouldProcess)
                {
                    // Defer extraction — pipeline will CopyToMemory while parsing runs
                    entryData.EntryFullName = entry.FullName;
                    filesToProcess.Add(entryData);
                }
            }
        }
    }
}
