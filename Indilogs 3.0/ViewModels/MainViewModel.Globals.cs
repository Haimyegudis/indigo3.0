using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // --- GLOBALS TAB ---
        private ObservableCollection<string> _globalsFileNames = new ObservableCollection<string>();
        public ObservableCollection<string> GlobalsFileNames
        {
            get => _globalsFileNames;
            set { _globalsFileNames = value; OnPropertyChanged(); }
        }

        private string _selectedGlobalsFile;
        public string SelectedGlobalsFile
        {
            get => _selectedGlobalsFile;
            set
            {
                if (_selectedGlobalsFile != value)
                {
                    _selectedGlobalsFile = value;
                    OnPropertyChanged();
                    LoadGlobalsFileContent();
                }
            }
        }

        private ObservableCollection<GlobalEntry> _globalsEntries = new ObservableCollection<GlobalEntry>();
        public ObservableCollection<GlobalEntry> GlobalsEntries
        {
            get => _globalsEntries;
            set { _globalsEntries = value; OnPropertyChanged(); }
        }

        // Backing store: plain List (not ObservableCollection — no UI binding)
        private List<GlobalEntry> _allGlobalsEntries = new List<GlobalEntry>();

        // Debounce for globals search
        private CancellationTokenSource _globalsSearchDebounce;

        private string _globalsSearchText = "";
        public string GlobalsSearchText
        {
            get => _globalsSearchText;
            set
            {
                if (_globalsSearchText != value)
                {
                    _globalsSearchText = value;
                    OnPropertyChanged();
                    DebouncedFilterGlobals();
                }
            }
        }

        public ICommand ClearGlobalsSearchCommand { get; private set; }
        public ICommand ToggleGlobalsDiffsCommand { get; private set; }

        private bool _globalsShowDiffsOnly;
        public bool GlobalsShowDiffsOnly
        {
            get => _globalsShowDiffsOnly;
            set
            {
                if (_globalsShowDiffsOnly != value)
                {
                    _globalsShowDiffsOnly = value;
                    OnPropertyChanged();
                    FilterGlobalsEntries();
                }
            }
        }

        private async void DebouncedFilterGlobals()
        {
            _globalsSearchDebounce?.Cancel();
            _globalsSearchDebounce = new CancellationTokenSource();
            var token = _globalsSearchDebounce.Token;
            try
            {
                await Task.Delay(250, token);
                if (!token.IsCancellationRequested)
                    FilterGlobalsEntries();
            }
            catch (TaskCanceledException) { }
        }

        private void LoadGlobalsFileContent()
        {
            _allGlobalsEntries.Clear();
            _globalsSearchText = "";
            OnPropertyChanged(nameof(GlobalsSearchText));
            _globalsShowDiffsOnly = false;
            OnPropertyChanged(nameof(GlobalsShowDiffsOnly));

            if (string.IsNullOrEmpty(SelectedGlobalsFile) || SessionVM?.SelectedSession == null ||
                SessionVM.SelectedSession.GlobalsFiles == null ||
                !SessionVM.SelectedSession.GlobalsFiles.ContainsKey(SelectedGlobalsFile))
            {
                GlobalsEntries = new ObservableCollection<GlobalEntry>();
                return;
            }

            try
            {
                string xmlContent = SessionVM.SelectedSession.GlobalsFiles[SelectedGlobalsFile];
                var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
                var globals = doc.Descendants("Global");
                foreach (var g in globals)
                {
                    string name = g.Element("Name")?.Value ?? "";
                    string value = g.Element("Value")?.Value ?? "";
                    string def = g.Element("Default")?.Value ?? "";
                    var entry = new GlobalEntry
                    {
                        Name = name,
                        Value = value,
                        Default = def,
                        IsRelevant = bool.TryParse(g.Element("IsRelevant")?.Value, out var isRel) && isRel,
                        NameLower = name.ToLowerInvariant(),
                        ValueLower = value.ToLowerInvariant(),
                        DefaultLower = def.ToLowerInvariant()
                    };
                    _allGlobalsEntries.Add(entry);
                }
                // Single batch update — no per-entry CollectionChanged
                GlobalsEntries = new ObservableCollection<GlobalEntry>(_allGlobalsEntries);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadGlobalsFileContent failed", ex);
            }
        }

        private void FilterGlobalsEntries()
        {
            string search = (GlobalsSearchText ?? "").ToLowerInvariant();
            var filtered = new List<GlobalEntry>(_allGlobalsEntries.Count);
            for (int i = 0; i < _allGlobalsEntries.Count; i++)
            {
                var entry = _allGlobalsEntries[i];
                // Diffs-only filter
                if (_globalsShowDiffsOnly &&
                    string.Equals(entry.Value ?? "", entry.Default ?? "", StringComparison.Ordinal))
                    continue;

                // Search filter using pre-cached lowercase
                if (!string.IsNullOrWhiteSpace(search))
                {
                    if ((entry.NameLower == null || !entry.NameLower.Contains(search)) &&
                        (entry.ValueLower == null || !entry.ValueLower.Contains(search)) &&
                        (entry.DefaultLower == null || !entry.DefaultLower.Contains(search)))
                        continue;
                }
                filtered.Add(entry);
            }
            // Single swap instead of Clear + N individual Adds
            GlobalsEntries = new ObservableCollection<GlobalEntry>(filtered);
        }

        public void LoadGlobalsFiles()
        {
            GlobalsFileNames.Clear();
            GlobalsEntries.Clear();
            _allGlobalsEntries.Clear();
            SelectedGlobalsFile = null;

            if (SessionVM?.SelectedSession?.GlobalsFiles != null)
            {
                foreach (var fileName in SessionVM.SelectedSession.GlobalsFiles.Keys)
                {
                    GlobalsFileNames.Add(fileName);
                }
            }
            OnPropertyChanged(nameof(HasGlobalsFiles));
        }
    }
}
