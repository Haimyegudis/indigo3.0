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
    public partial class DifferentLogsViewModel : ViewModelBase
    {
        // ── Built-in field names that bind directly on LogEntry ───
        public static readonly System.Collections.Frozen.FrozenSet<string> BuiltInFields = AppConstants.BuiltInFields;

        private readonly IPluginLoader _pluginLoader;
        private readonly IDialogService _dialogService;
        private readonly IViewFactory _viewFactory;
        private readonly IWindowOwnerProvider _windowOwner;
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
        public Func<string?>? GetCurrentZipPath { get; set; }

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
        private FilterNode? _filterRoot;
        public FilterNode? FilterRoot
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

        private IReadOnlyList<PluginColumnDef>? _columns;
        /// <summary>Raised via PropertyChanged when a new plugin is selected -- code-behind rebuilds DataGrid columns.</summary>
        public IReadOnlyList<PluginColumnDef>? Columns
        {
            get => _columns;
            private set { _columns = value; OnPropertyChanged(); }
        }

        private string? _currentFilePath;
        public string? CurrentFilePath
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
        public DifferentLogsViewModel(IPluginLoader pluginLoader, IDialogService dialogService, IViewFactory viewFactory, IWindowOwnerProvider windowOwner)
        {
            _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
            _dialogService = dialogService;
            _viewFactory = viewFactory;
            _windowOwner = windowOwner;

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
