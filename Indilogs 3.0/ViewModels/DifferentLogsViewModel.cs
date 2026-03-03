#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.BuiltInPlugins;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs.PluginAPI;
using IndiLogs_3._0.Views;
using Microsoft.Win32;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the Different Logs tab -- loads and displays non-standard log files via plugins.
    /// </summary>
    public class DifferentLogsViewModel : ViewModelBase
    {
        // ── Built-in field names that bind directly on LogEntry ───
        public static readonly HashSet<string> BuiltInFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Date", "Level", "Message", "ThreadName", "Logger",
            "ProcessName", "Source", "LineNumber"
        };

        private readonly IPluginLoader _pluginLoader;
        private readonly IDialogService _dialogService;
        /// <summary>
        /// Combined plugin list for manual file opening: external DLL plugins (highest priority)
        /// followed by AllForManualOpen (which includes IndigoAppLogPlugin).
        /// This is intentionally broader than the automatic ZIP-scanning list.
        /// </summary>
        private readonly IReadOnlyList<ILogFilePlugin> _allPlugins;

        /// <summary>
        /// Returns the FilePath of the currently selected session (the loaded ZIP path),
        /// or null if no session is loaded. Set by MainViewModel after construction.
        /// </summary>
        public Func<string> GetCurrentZipPath { get; set; }

        // ── Observable data ─────────────────────────────────────────
        public ObservableCollection<LogEntryDto> Entries { get; } = new ObservableCollection<LogEntryDto>();

        /// <summary>All loaded entries converted to LogEntry (unfiltered master copy).</summary>
        private List<LogEntry> _allLogEntries = new List<LogEntry>();
        public List<LogEntry> AllLogEntries => _allLogEntries;

        /// <summary>Filtered view — DataGrid binds to this.</summary>
        private ObservableCollection<LogEntry> _filteredEntries = new ObservableCollection<LogEntry>();
        public ObservableCollection<LogEntry> FilteredEntries
        {
            get => _filteredEntries;
            set { _filteredEntries = value; OnPropertyChanged(); }
        }

        // ── Filter state ─────────────────────────────────────────────
        private FilterNode _filterRoot;
        public FilterNode FilterRoot
        {
            get => _filterRoot;
            set { _filterRoot = value; OnPropertyChanged(); }
        }

        private bool _isFilterActive;
        public bool IsFilterActive
        {
            get => _isFilterActive;
            set { _isFilterActive = value; OnPropertyChanged(); }
        }

        // ── Coloring state ───────────────────────────────────────────
        private List<ColoringCondition> _coloringRules = new List<ColoringCondition>();
        public List<ColoringCondition> ColoringRules
        {
            get => _coloringRules;
            set { _coloringRules = value; OnPropertyChanged(); }
        }

        // ── Dynamic field names (standard + plugin columns) ──────────
        private List<string> _availableFields = new List<string>();
        public List<string> AvailableFields
        {
            get => _availableFields;
            set { _availableFields = value; OnPropertyChanged(); }
        }

        private IReadOnlyList<PluginColumnDef> _columns;
        /// <summary>Raised via PropertyChanged when a new plugin is selected — code-behind rebuilds DataGrid columns.</summary>
        public IReadOnlyList<PluginColumnDef> Columns
        {
            get => _columns;
            private set { _columns = value; OnPropertyChanged(); }
        }

        private string _currentFilePath;
        public string CurrentFilePath
        {
            get => _currentFilePath;
            private set { _currentFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentFileName)); OnPropertyChanged(nameof(HasFile)); }
        }

        public string CurrentFileName => string.IsNullOrEmpty(CurrentFilePath)
            ? string.Empty
            : Path.GetFileName(CurrentFilePath);

        public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath);

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _statusText = "Open a log file to begin.";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        // ── Commands ────────────────────────────────────────────────
        public ICommand BrowseCommand   { get; }
        public ICommand CloseFileCommand { get; }

        // ── Constructor ─────────────────────────────────────────────
        public DifferentLogsViewModel(IPluginLoader pluginLoader, IDialogService dialogService)
        {
            _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
            _dialogService = dialogService;

            // Build combined plugin list: external DLL plugins first (highest priority),
            // then AllForManualOpen which includes IndigoAppLogPlugin.
            var combined = new List<ILogFilePlugin>();
            foreach (var p in _pluginLoader.Plugins)
            {
                if (_pluginLoader.GetDllPath(p) != null)
                    combined.Add(p);
            }
            foreach (var p in BuiltInPluginRegistry.AllForManualOpen)
                combined.Add(p);
            _allPlugins = combined.AsReadOnly();

            BrowseCommand    = new RelayCommand(_ => _ = BrowseAsync());
            CloseFileCommand = new RelayCommand(_ => CloseFile(), _ => HasFile);
        }

        // ── Browse ──────────────────────────────────────────────────

        private async Task BrowseAsync()
        {
            // If a ZIP session is loaded, show the fast ZIP browser (reads entries instantly, no full extraction)
            string zipPath = GetCurrentZipPath?.Invoke();
            if (!string.IsNullOrEmpty(zipPath) &&
                zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(zipPath))
            {
                var picker = new ZipBrowserWindow(zipPath) { Owner = Application.Current.MainWindow };
                var result = picker.ShowDialog();

                if (picker.BrowseExternalRequested)
                {
                    // User clicked "Browse Files…" — fall through to standard dialog below
                }
                else if (result == true && !string.IsNullOrEmpty(picker.SelectedEntryName))
                {
                    // Extract only the selected entry to a temp file (instant for a single file)
                    string tempFile = await Task.Run(() => ExtractSingleZipEntry(zipPath, picker.SelectedEntryName));
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
        private static string ExtractSingleZipEntry(string zipPath, string entryFullName)
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
            catch (Exception)
            {
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
            IReadOnlyList<PluginColumnDef> pluginCols;
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
            catch (Exception)
            {
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

        /// <summary>Builds the dynamic field list from standard fields + plugin columns.</summary>
        public void BuildAvailableFields()
        {
            var fields = new List<string>
            {
                "Message", "Level", "ThreadName", "Logger", "ProcessName",
                "Method", "Data", "Exception"
            };

            // Add plugin-defined extra columns (use Header for display when Field is a built-in)
            if (Columns != null)
            {
                foreach (var col in Columns)
                {
                    string field = col.Field ?? col.Header;
                    if (string.IsNullOrEmpty(field)) continue;
                    if (fields.Contains(field, StringComparer.OrdinalIgnoreCase)) continue;
                    if (BuiltInFields.Contains(field)) continue;
                    fields.Add(field);
                }
            }

            // Also scan ExtraFields from loaded entries for any keys not yet listed
            foreach (var entry in _allLogEntries)
            {
                if (entry.ExtraFields == null) continue;
                foreach (var key in entry.ExtraFields.Keys)
                {
                    if (!fields.Contains(key, StringComparer.OrdinalIgnoreCase))
                        fields.Add(key);
                }
            }

            AvailableFields = fields;
        }

        // ── Close ───────────────────────────────────────────────────
        private void CloseFile()
        {
            CurrentFilePath = null;
            Entries.Clear();
            _allLogEntries.Clear();
            FilteredEntries = new ObservableCollection<LogEntry>();
            Columns    = null;
            FilterRoot = null;
            IsFilterActive = false;
            ColoringRules = new List<ColoringCondition>();
            AvailableFields = new List<string>();
            StatusText = "Open a log file to begin.";
        }

        // ── Helpers ─────────────────────────────────────────────────
        private static string[] ReadSampleLines(string filePath, int maxLines)
        {
            var lines = new List<string>(maxLines);
            using var reader = new StreamReader(filePath);
            for (int i = 0; i < maxLines && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (line != null) lines.Add(line);
            }
            return lines.ToArray();
        }

        private string BuildOpenDialogFilter()
        {
            var allExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts   = new List<string>();

            foreach (var plugin in _allPlugins)
            {
                if (plugin.SupportedExtensions == null || plugin.SupportedExtensions.Length == 0)
                    continue;
                var exts = plugin.SupportedExtensions;
                // Normalize: plugin extensions may be "*.ext" or ".ext" — ensure we get "*.ext"
                string NormExt(string e) => e.StartsWith("*") ? e : $"*{e}";
                parts.Add($"{plugin.Name}|{string.Join(";", exts.Select(NormExt))}");
                foreach (var e in exts) allExts.Add(NormExt(e));
            }

            var combined = allExts.Count > 0
                ? $"All Supported|{string.Join(";", allExts)}|"
                : string.Empty;

            return combined + string.Join("|", parts) + "|All Files|*.*";
        }

        private static IReadOnlyList<PluginColumnDef> BuildDefaultColumns()
        {
            return new List<PluginColumnDef>
            {
                new PluginColumnDef { Header = "Date",    Field = "Date",    Width = 190, StringFormat = "yyyy-MM-dd HH:mm:ss.ffffff" },
                new PluginColumnDef { Header = "Level",   Field = "Level",   Width = 70  },
                new PluginColumnDef { Header = "Message", Field = "Message", Width = -1  }
            };
        }

    }
}
