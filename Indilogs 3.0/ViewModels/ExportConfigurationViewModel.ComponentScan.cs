using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel
    {
        private async Task LoadComponentsAndThreads()
        {
            try
            {
            if (_sessionData == null) return;

            // ── S4-5 with Io-*.csv: load IO components from TerminalLogs ────
            // Keys may be prefixed for nested ZIPs (e.g. "InnerZip/TerminalLogs/Io-BIM[0].csv")
            bool hasIoCsv = (_sessionData.TerminalCsvBytes != null &&
                             _sessionData.TerminalCsvBytes.Keys.Any(
                                 k => System.IO.Path.GetFileName(k).StartsWith("Io-", StringComparison.OrdinalIgnoreCase))) ||
                            (_sessionData.TerminalLogFiles != null &&
                             _sessionData.TerminalLogFiles.Keys.Any(
                                 k => System.IO.Path.GetFileName(k).StartsWith("Io-", StringComparison.OrdinalIgnoreCase)));

            if (_sessionData.HasBinaryAppLogs && hasIoCsv)
            {
                _hasIoTerminalData = true;

                IsLoading = true;
                LoadingMessage = "Loading IO components from TerminalLogs...";

                await Task.Run(() =>
                {
                    var svc = new IoTerminalDataService();
                    _ioDevices = svc.ParseIoFiles(_sessionData.TerminalLogFiles ?? new Dictionary<string, string>(), _sessionData.TerminalCsvBytes);
                    var items = svc.GetAllComponents(_ioDevices);

                    _dispatcher.Post(() =>
                    {
                        IOComponents.Clear();
                        foreach (var item in items)
                            IOComponents.Add(item);

                        IsLoading = false;
                        LoadingMessage = $"Found {IOComponents.Count} IO components";
                    });
                });
                return; // S4-5 only needs IO — skip log scanning
            }

            // ── S6 (and S4-5 without terminal CSVs): scan from session logs ──
            if (_sessionData.Logs == null) return;

            // Show loading indicator
            IsLoading = true;
            LoadingProgress = 0;
            LoadingMessage = "Scanning logs for components...";

            await Task.Run(() =>
            {
                AppLogger.Info($"[ComponentScan] Scanning PLC logs: {_sessionData.Logs.Count:N0}");

                // Use ConcurrentDictionary for thread-safe parallel processing
                var ioComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var axisComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var chStepComponents = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                var threads = new ConcurrentDictionary<string, byte>();

                int processedLogs = 0;
                int totalLogs = _sessionData.Logs.Count;

                // Process logs in parallel for better performance
                Parallel.ForEach(_sessionData.Logs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, log =>
                {
                    if (string.IsNullOrEmpty(log.Message)) return;

                    string msg = log.Message;

                    // Update progress every 10000 logs
                    int current = System.Threading.Interlocked.Increment(ref processedLogs);
                    if (current % 10000 == 0)
                    {
                        double pct = (double)current / totalLogs * 100;
                        _dispatcher.Post(() =>
                        {
                            LoadingProgress = pct;
                            LoadingMessage = $"Scanning logs... {pct:F1}% ({current:N0} / {totalLogs:N0})";
                        });
                    }

                    // Early filtering - skip lines that are definitely not relevant
                    char firstChar = msg.Length > 0 ? msg[0] : ' ';
                    if (firstChar != 'I' && firstChar != 'i' &&
                        firstChar != 'A' && firstChar != 'a' &&
                        firstChar != 'C' && firstChar != 'c')
                    {
                        // Still check threads
                        if (!string.IsNullOrEmpty(log.ThreadName))
                            threads.TryAdd(log.ThreadName, 0);
                        return;
                    }

                    // IO Components - current IO_Mon pattern
                    if (msg.Length > 7 && (msg[0] == 'I' || msg[0] == 'i') &&
                        msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 2)
                            {
                                string subsystem = parts[0].Trim();

                                for (int i = 1; i < parts.Length; i++)
                                {
                                    int eqIndex = parts[i].IndexOf('=');
                                    if (eqIndex > 0)
                                    {
                                        string fullSymbolName = parts[i].Substring(0, eqIndex).Trim();

                                        // Strip subsystem prefix from symbol name if present
                                        string cleanSymbol = fullSymbolName;
                                        if (cleanSymbol.StartsWith(subsystem, StringComparison.OrdinalIgnoreCase))
                                            cleanSymbol = cleanSymbol.Substring(subsystem.Length).TrimStart('_', ' ');

                                        string componentName;
                                        if (cleanSymbol.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                            componentName = cleanSymbol.Substring(0, cleanSymbol.Length - 8);
                                        else if (cleanSymbol.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                            componentName = cleanSymbol.Substring(0, cleanSymbol.Length - 8);
                                        else
                                            componentName = cleanSymbol;

                                        ioComponents.TryAdd($"{subsystem}|{componentName}", 0);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing IO_Mon component failed", ex);
                        }
                    }
                    // IO Components - optimized IO: pattern (20.01.2026)
                    else if (msg.Length > 3 && (msg[0] == 'I' || msg[0] == 'i') &&
                             msg.StartsWith("IO:", StringComparison.OrdinalIgnoreCase) &&
                             !msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 2)
                            {
                                string subsystem = parts[0].Trim();
                                string pair = parts[1].Trim();
                                int eqIndex = pair.IndexOf('=');
                                if (eqIndex > 0)
                                {
                                    string fullSymbolName = pair.Substring(0, eqIndex).Trim();

                                    // Strip subsystem prefix from symbol name if present
                                    string cleanSymbol = fullSymbolName;
                                    if (cleanSymbol.StartsWith(subsystem, StringComparison.OrdinalIgnoreCase))
                                        cleanSymbol = cleanSymbol.Substring(subsystem.Length).TrimStart('_', ' ');

                                    string componentName;
                                    if (cleanSymbol.EndsWith("_MotTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = cleanSymbol.Substring(0, cleanSymbol.Length - 8);
                                    else if (cleanSymbol.EndsWith("_DrvTemp", StringComparison.OrdinalIgnoreCase))
                                        componentName = cleanSymbol.Substring(0, cleanSymbol.Length - 8);
                                    else
                                        componentName = cleanSymbol;

                                    ioComponents.TryAdd($"{subsystem}|{componentName}", 0);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing IO component failed", ex);
                        }
                    }
                    // Axis Components - current AxisMon pattern
                    else if (msg.Length > 8 && (msg[0] == 'A' || msg[0] == 'a') &&
                             msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 3)
                            {
                                string subsystem = parts[0].Trim();
                                string motor = parts[1].Trim();
                                axisComponents.TryAdd($"{subsystem}|{motor}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing AxisMon component failed", ex);
                        }
                    }
                    // Axis Components - optimized AxM: pattern (20.01.2026)
                    else if (msg.Length > 4 && (msg[0] == 'A' || msg[0] == 'a') &&
                             msg.StartsWith("AxM:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int colonIndex = msg.IndexOf(':');
                            if (colonIndex < 0) return;

                            string content = msg.Substring(colonIndex + 1);
                            var parts = content.Split(',');

                            if (parts.Length >= 3)
                            {
                                string subsystem = parts[0].Trim();
                                string motor = parts[1].Trim();
                                axisComponents.TryAdd($"{subsystem}|{motor}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing AxM component failed", ex);
                        }
                    }
                    // CHStep Components - optimized with faster string parsing
                    else if (msg.Length > 7 && (msg[0] == 'C' || msg[0] == 'c') &&
                             msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Fast path: use IndexOf instead of regex
                            int firstComma = msg.IndexOf(',', 7);
                            if (firstComma < 0) return;

                            int statePos = msg.IndexOf("State ", firstComma, StringComparison.OrdinalIgnoreCase);
                            if (statePos < 0) return;

                            int openBracket = msg.IndexOf('<', statePos);
                            if (openBracket < 0) return;

                            // Extract CHName (between "CHStep:" and first comma)
                            string chName = msg.Substring(7, firstComma - 7).Trim();

                            // Extract CHParentName (first item after '<')
                            int nextComma = msg.IndexOf(',', openBracket);
                            if (nextComma < 0) return;

                            string chParentName = msg.Substring(openBracket + 1, nextComma - openBracket - 1).Trim();

                            if (!chName.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase))
                            {
                                chStepComponents.TryAdd($"{chParentName}|{chName}", 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Parsing CHStep component failed", ex);
                        }
                    }

                    // Threads
                    if (!string.IsNullOrEmpty(log.ThreadName))
                    {
                        threads.TryAdd(log.ThreadName, 0);
                    }
                });

                AppLogger.Info($"[ComponentScan] Found: {ioComponents.Count} IO, {axisComponents.Count} Axis, {chStepComponents.Count} CHStep, {threads.Count} Threads");
                if (ioComponents.Count > 0)
                    AppLogger.Info($"[ComponentScan] IO samples: {string.Join(", ", ioComponents.Keys.Take(5))}");
                if (axisComponents.Count > 0)
                    AppLogger.Info($"[ComponentScan] Axis samples: {string.Join(", ", axisComponents.Keys.Take(5))}");

                // Build lists (not yet added to ObservableCollection)
                _dispatcher.Post(() =>
                {
                    LoadingMessage = "Building component lists...";
                });

                var ioList = ioComponents.Keys.OrderBy(x => x).Select(io =>
                {
                    var parts = io.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : io,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var axisList = axisComponents.Keys.OrderBy(x => x).Select(axis =>
                {
                    var parts = axis.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : axis,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var chStepList = chStepComponents.Keys.OrderBy(x => x).Select(ch =>
                {
                    var parts = ch.Split('|');
                    return new SelectableItem
                    {
                        Name = parts.Length > 1 ? parts[1] : ch,
                        Category = parts.Length > 1 ? parts[0] : "Unknown",
                        IsSelected = false  // DEFAULT = FALSE
                    };
                }).ToList();

                var threadList = threads.Keys.OrderBy(x => x).Select(thread =>
                    new SelectableItem
                    {
                        Name = thread,
                        Category = "Thread",
                        IsSelected = false  // DEFAULT = FALSE
                    }).ToList();

                // Add to UI on UI thread - NON-BLOCKING
                _dispatcher.Post(() =>
                {
                    LoadingMessage = "Populating UI...";

                    // Clear and add all at once (much faster than individual adds)
                    IOComponents.Clear();
                    foreach (var item in ioList)
                        IOComponents.Add(item);

                    AxisComponents.Clear();
                    foreach (var item in axisList)
                        AxisComponents.Add(item);

                    CHStepComponents.Clear();
                    foreach (var item in chStepList)
                        CHStepComponents.Add(item);

                    ThreadItems.Clear();
                    foreach (var item in threadList)
                        ThreadItems.Add(item);

                    // Initialize cached lists
                    _cachedIOFiltered = IOComponents.ToList();
                    _cachedAxisFiltered = AxisComponents.ToList();
                    _cachedCHStepFiltered = CHStepComponents.ToList();
                    _cachedThreadFiltered = ThreadItems.ToList();

                    IsLoading = false;
                    LoadingMessage = $"Found {IOComponents.Count} IO, {AxisComponents.Count} Axis, {CHStepComponents.Count} CHSteps, {ThreadItems.Count} Threads";
                });
            });
            }
            catch (Exception ex) { AppLogger.Error("LoadComponentsAndThreads failed", ex); }
        }
    }
}
