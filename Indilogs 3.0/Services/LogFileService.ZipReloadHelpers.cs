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
        private void ProcessInnerZipEntries(
            ZipArchive archive,
            List<string> innerZipEntryNames,
            TabSelectionConfig sel,
            LogSessionData session,
            List<ZipEntryData> filesToProcess,
            List<BitmapImage> screenshotsList,
            bool hasBinaryAppLogs)
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
                            if (innerLower.Contains("/backup/") || innerLower.Contains("\\backup\\") ||
                                innerLower.Contains("/old/") || innerLower.Contains("\\old\\"))
                                continue;

                            string prefixedName = $"{Path.GetFileNameWithoutExtension(innerZipName)}/{innerEntry.Name}";

                            // Globals
                            if (sel.LoadGlobals && IsGlobalsXmlFile(innerLower))
                            {
                                try
                                {
                                    string content = ReadTextFromEntry(innerEntry);
                                    if (!session.GlobalsFiles.ContainsKey(prefixedName))
                                        session.GlobalsFiles[prefixedName] = content;
                                }
                                catch (Exception ex) { AppLogger.Warn($"Failed to read globals file '{innerEntry.Name}': {ex.Message}"); }
                                continue;
                            }

                            // Systab
                            if (sel.LoadSystab && IsSystabFile(innerLower))
                            {
                                try
                                {
                                    string fileNameOnly = Path.GetFileName(innerEntry.Name).ToLower();
                                    string? systabKey = null;
                                    if (fileNameOnly.Contains("saved")) systabKey = "saved";
                                    else if (fileNameOnly.Contains("default")) systabKey = "default";
                                    else if (fileNameOnly.Contains("minimum")) systabKey = "minimum";
                                    else if (fileNameOnly.Contains("maximum")) systabKey = "maximum";
                                    if (systabKey != null && !session.SystabFiles.ContainsKey(systabKey))
                                        session.SystabFiles[systabKey] = ReadTextFromEntry(innerEntry);
                                }
                                catch (Exception ex) { AppLogger.Warn($"Failed to read systab file '{innerEntry.Name}': {ex.Message}"); }
                                continue;
                            }

                            // Configuration
                            bool innerIsConfig = innerLower.Contains("/configuration/") || innerLower.Contains("\\configuration\\") ||
                                                 innerLower.StartsWith("configuration/") || innerLower.StartsWith("configuration\\");
                            if (sel.LoadConfiguration && innerIsConfig)
                            {
                                try
                                {
                                    string fName = Path.GetFileName(innerEntry.Name);
                                    if (fName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.DatabaseFiles.ContainsKey(fName))
                                            session.DatabaseFiles[fName] = ReadBytesFromEntry(innerEntry);
                                    }
                                    else
                                    {
                                        if (!session.ConfigurationFiles.ContainsKey(prefixedName))
                                            session.ConfigurationFiles[prefixedName] = ReadTextFromEntry(innerEntry);
                                    }
                                }
                                catch (Exception ex) { AppLogger.Warn($"Failed to read config file '{innerEntry.Name}': {ex.Message}"); }
                                continue;
                            }

                            // Terminal logs
                            bool innerIsTerminal = innerLower.Contains("/terminallogs/") || innerLower.Contains("\\terminallogs\\") ||
                                                   innerLower.StartsWith("terminallogs/") || innerLower.StartsWith("terminallogs\\");
                            if (sel.LoadTerminalLogs && innerIsTerminal)
                            {
                                try
                                {
                                    string fName = Path.GetFileName(innerEntry.Name);
                                    string ext = Path.GetExtension(fName);
                                    string key = prefixedName;
                                    if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.TerminalCsvBytes.ContainsKey(key))
                                            session.TerminalCsvBytes[key] = ReadBytesFromEntry(innerEntry);
                                    }
                                    else if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!session.TerminalLogFiles.ContainsKey(key))
                                            session.TerminalLogFiles[key] = ReadTextFromEntry(innerEntry);
                                    }
                                }
                                catch (Exception ex) { AppLogger.Warn($"Failed to read terminal log '{innerEntry.Name}': {ex.Message}"); }
                                continue;
                            }

                            // PLC logs from inner ZIP
                            if (sel.LoadPlc && innerEntry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                !innerEntry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                var ms = CopyToMemory(innerEntry);
                                filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.MainLog });
                                continue;
                            }

                            // APP text logs from inner ZIP
                            if (sel.LoadApp && (innerEntry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                innerEntry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                (innerLower.Contains("indigologs/logger files") || innerLower.Contains("indigologs\\logger files")))
                            {
                                var ms = CopyToMemory(innerEntry);
                                filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.AppDevLog });
                                continue;
                            }

                            // APP binary logs from inner ZIP
                            if (sel.LoadApp && IsNumericAppFile(innerEntry.Name))
                            {
                                var ms = CopyToMemory(innerEntry);
                                filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = FileType.AppBinaryLog });
                                continue;
                            }

                            // Events CSV or XML from inner ZIP
                            if (sel.LoadEvents && IsEventsFile(innerEntry.Name, out var innerEvtType))
                            {
                                var ms = CopyToMemory(innerEntry);
                                filesToProcess.Add(new ZipEntryData { Name = innerEntry.Name, Stream = ms, Type = innerEvtType });
                                continue;
                            }

                            // Screenshots from inner ZIP
                            if (sel.LoadScreenshots && (innerEntry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || innerEntry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                            {
                                var bmp = LoadBitmapFromZip(innerEntry);
                                if (bmp != null)
                                {
                                    bool isInfoPath = innerLower.Contains("/info/") || innerLower.Contains("\\info\\");
                                    if (!hasBinaryAppLogs || isInfoPath)
                                        screenshotsList.Add(bmp);
                                }
                                continue;
                            }

                            // Readme from inner ZIP — always loaded
                            if (innerEntry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrEmpty(session.PressConfiguration))
                                    session.PressConfiguration = ReadTextFromEntry(innerEntry);
                                continue;
                            }
                        }
                    }
                }
                catch (Exception ex) { AppLogger.Error($"Reload: nested ZIP {innerZipName} failed", ex); }
            }
        }

        private static void MergeReloadResults(
            LogSessionData session,
            TabSelectionConfig sel,
            string componentName,
            ConcurrentBag<List<LogEntry>> logsBag,
            ConcurrentBag<List<LogEntry>> appLogsBag,
            ConcurrentBag<List<EventEntry>> eventsBag,
            List<BitmapImage> screenshotsList)
        {
            if (sel.LoadPlc || sel.LoadManagerThread)
            {
                var newLogs = new List<LogEntry>();
                foreach (var l in logsBag) newLogs.AddRange(l);
                if (newLogs.Count > 0)
                {
                    SortLogEntriesCacheFriendly(newLogs);
                    var merged = new List<LogEntry>(session.Logs.Count + newLogs.Count);
                    merged.AddRange(session.Logs);
                    merged.AddRange(newLogs);
                    SortLogEntriesCacheFriendly(merged);
                    session.Logs = merged;
                }
            }

            if (sel.LoadApp)
            {
                var newApps = new List<LogEntry>();
                foreach (var l in appLogsBag) newApps.AddRange(l);
                if (newApps.Count > 0)
                {
                    SortLogEntriesCacheFriendly(newApps);
                    var merged = new List<LogEntry>(session.AppDevLogs.Count + newApps.Count);
                    merged.AddRange(session.AppDevLogs);
                    merged.AddRange(newApps);
                    SortLogEntriesCacheFriendly(merged);
                    session.AppDevLogs = merged;
                }
            }

            if (sel.LoadEvents)
            {
                var newEvents = new List<EventEntry>();
                foreach (var l in eventsBag) newEvents.AddRange(l);
                if (newEvents.Count > 0)
                {
                    newEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
                    var merged = new List<EventEntry>(session.Events.Count + newEvents.Count);
                    merged.AddRange(session.Events);
                    merged.AddRange(newEvents);
                    merged.Sort((a, b) => a.Time.CompareTo(b.Time));
                    session.Events = merged;
                }
            }

            if (sel.LoadScreenshots && screenshotsList.Count > 0)
            {
                session.Screenshots.AddRange(screenshotsList);
            }

            if (session.LoadTabSelection != null)
            {
                switch (componentName)
                {
                    case "App": session.LoadTabSelection.LoadApp = true; break;
                    case "Plc": session.LoadTabSelection.LoadPlc = true; break;
                    case "Events": session.LoadTabSelection.LoadEvents = true; break;
                    case "Screenshots": session.LoadTabSelection.LoadScreenshots = true; break;
                    case "TerminalLogs": session.LoadTabSelection.LoadTerminalLogs = true; break;
                    case "Configuration": session.LoadTabSelection.LoadConfiguration = true; break;
                    case "Systab": session.LoadTabSelection.LoadSystab = true; break;
                    case "Globals": session.LoadTabSelection.LoadGlobals = true; break;
                    case "Lrs": session.LoadTabSelection.LoadLrs = true; break;
                    case "SetupInfo": session.LoadTabSelection.LoadSetupInfo = true; break;
                    case "ManagerThread": session.LoadTabSelection.LoadManagerThread = true; break;
                }
            }
        }
    }
}
