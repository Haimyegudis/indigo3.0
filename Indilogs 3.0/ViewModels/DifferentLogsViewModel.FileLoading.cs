using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using IndiLogs.PluginAPI;
using Microsoft.Win32;

namespace IndiLogs_3._0.ViewModels
{
    public partial class DifferentLogsViewModel
    {
        // ── Browse ──────────────────────────────────────────────────

        private async Task BrowseAsync()
        {
            // If a ZIP session is loaded, show the fast ZIP browser (reads entries instantly, no full extraction)
            string? zipPath = GetCurrentZipPath?.Invoke();
            if (!string.IsNullOrEmpty(zipPath) &&
                zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(zipPath))
            {
                var picker = _viewFactory.Create<ZipBrowserWindow>(zipPath);
                picker.Owner = _windowOwner.GetOwner();
                var result = picker.ShowDialog();

                if (picker.BrowseExternalRequested)
                {
                    // User clicked "Browse Files…" — fall through to standard dialog below
                }
                else if (result == true && !string.IsNullOrEmpty(picker.SelectedEntryName))
                {
                    // Extract only the selected entry to a temp file (instant for a single file)
                    string? tempFile = await Task.Run(() => ExtractSingleZipEntry(zipPath!, picker.SelectedEntryName));
                    if (tempFile == null)
                    {
                        _dialogService.ShowWarning("Could not extract the selected file from the ZIP.",
                            "Extraction Error");
                        return;
                    }

                    var success = await LoadFileAsync(tempFile);
                    if (!success)
                    {
                        _dialogService.ShowWarning(
                            $"No plugin could handle the file:\n{Path.GetFileName(tempFile)}\n\nLoad additional plugins via the Plugin Tester.",
                            "Unsupported File");
                    }
                    return;
                }
                else
                {
                    return; // user cancelled
                }
            }

            // Standard file dialog (no ZIP loaded, or user clicked "Browse Files…")
            var filter = BuildOpenDialogFilter();
            var dlg = new OpenFileDialog
            {
                Title  = "Open Log File",
                Filter = filter
            };

            if (dlg.ShowDialog() != true) return;

            var success2 = await LoadFileAsync(dlg.FileName);
            if (!success2)
            {
                _dialogService.ShowWarning(
                    $"No plugin could handle the file:\n{Path.GetFileName(dlg.FileName)}\n\nLoad additional plugins via the Plugin Tester.",
                    "Unsupported File");
            }
        }

        /// <summary>
        /// Extracts a single entry from a ZIP to a temp file. Returns the temp file path, or null on failure.
        /// </summary>
        private static string? ExtractSingleZipEntry(string zipPath, string entryFullName)
        {
            try
            {
                // Strip any display suffix like " [nested ZIP]"
                string cleanName = entryFullName;
                int bracketIdx = cleanName.IndexOf(" [", StringComparison.Ordinal);
                if (bracketIdx > 0) cleanName = cleanName.Substring(0, bracketIdx);

                string tempDir = Path.Combine(Path.GetTempPath(), "IndiLogs_ZipBrowse", "single");
                Directory.CreateDirectory(tempDir);

                string fileName = Path.GetFileName(cleanName);
                string destPath = Path.Combine(tempDir, fileName);

                // Prevent ZIP Slip: verify resolved path is inside tempDir
                string resolvedPath = Path.GetFullPath(destPath);
                string resolvedDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);
                if (!resolvedPath.StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warn($"Path traversal detected: {cleanName}");
                    return null;
                }

                // Remove old file if it exists
                if (File.Exists(destPath))
                {
                    try { File.SetAttributes(destPath, FileAttributes.Normal); File.Delete(destPath); }
                    catch (Exception ex) { AppLogger.Error("File delete failed", ex); destPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + "_" + fileName); }
                }

                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(cleanName);
                if (entry == null) return null;

                entry.ExtractToFile(destPath, true);
                return destPath;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Single-entry extraction failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Loads a file into the Different Logs tab using plugin auto-detection.
        /// Returns true if a plugin handled the file, false if no plugin matched.
        /// Called from BrowseAsync (after dialog) and from MainViewModel for drag-drop / open routing.
        /// </summary>
        public async Task<bool> LoadFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            string fileName = Path.GetFileName(filePath);

            // Read sample lines to find a matching plugin
            string[] sampleLines;
            try
            {
                sampleLines = ReadSampleLines(filePath, 30);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read sample lines from '{filePath}': {ex.Message}");
                return false;
            }

            var plugin = _allPlugins.FirstOrDefault(p =>
            {
                try { return p.CanHandle(fileName, sampleLines); }
                catch (Exception ex) { AppLogger.Error("CanHandle failed", ex); return false; }
            });

            if (plugin == null)
                return false;

            // Reset state
            CloseFile();
            IsLoading  = true;
            StatusText = $"Loading with plugin \"{plugin.Name}\"…";

            // Apply column definitions before data arrives so DataGrid rebuilds
            IReadOnlyList<PluginColumnDef>? pluginCols;
            try { pluginCols = plugin.GetColumns(); } catch (Exception ex) { AppLogger.Error("GetColumns failed", ex); pluginCols = null; }
            Columns = (pluginCols != null && pluginCols.Count > 0) ? pluginCols : BuildDefaultColumns();

            // Parse in background
            var parsed = new List<LogEntryDto>();
            var context = new ParseContext
            {
                FileName     = fileName,
                FilePath     = filePath,
                IsInsideZip  = false,
                ZipEntryPath = null
            };

            try
            {
                await Task.Run(() =>
                {
                    using var fs = File.OpenRead(filePath);
                    foreach (var dto in plugin.Parse(fs, context, null, CancellationToken.None))
                        parsed.Add(dto);
                });

                // Convert DTOs to LogEntry and build both collections
                _allLogEntries.Clear();
                foreach (var dto in parsed)
                {
                    Entries.Add(dto);
                    _allLogEntries.Add(LogFileService.MapDtoToLogEntry(dto));
                }

                // Mark Error-level entries for red text display
                foreach (var entry in _allLogEntries)
                {
                    if (string.Equals(entry.Level, "Error", StringComparison.OrdinalIgnoreCase))
                        entry.IsErrorOrEvents = true;
                }

                FilteredEntries = new ObservableCollection<LogEntry>(_allLogEntries);

                // Build available fields: standard fields + plugin ExtraField columns
                BuildAvailableFields();

                CurrentFilePath = filePath;
                StatusText = $"{_allLogEntries.Count:N0} entries  —  plugin: {plugin.Name}";
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"File load failed: {ex.Message}");
                StatusText = "Error loading file.";
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Checks whether any loaded plugin can handle the given file, without actually loading it.
        /// Used by MainViewModel to decide whether to route a file to Different Logs.
        /// </summary>
        public bool CanHandleFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            string fileName = Path.GetFileName(filePath);
            string[] sampleLines;
            try { sampleLines = ReadSampleLines(filePath, 30); }
            catch (Exception ex) { AppLogger.Error("ReadSampleLines failed", ex); return false; }

            return _allPlugins.Any(p =>
            {
                try { return p.CanHandle(fileName, sampleLines); }
                catch (Exception ex) { AppLogger.Error("CanHandle failed", ex); return false; }
            });
        }
    }
}
