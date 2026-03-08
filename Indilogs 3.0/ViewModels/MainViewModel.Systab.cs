using IndiLogs_3._0.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // --- SYSTAB TAB ---
        // Show Systab tab only when loaded from a ZIP that contains systab files
        public bool HasSystabFiles =>
            SessionVM?.SelectedSession?.SystabFiles != null && SessionVM.SelectedSession.SystabFiles.Count > 0 &&
            SessionVM.SelectedSession.FilePath != null && SessionVM.SelectedSession.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            SessionVM.SelectedSession.HasBinaryAppLogs;

        private ObservableCollection<SystabTopicNode> _systabTree = new ObservableCollection<SystabTopicNode>();
        public ObservableCollection<SystabTopicNode> SystabTree
        {
            get => _systabTree;
            set { _systabTree = value; OnPropertyChanged(); }
        }

        private SystabTopicNode? _selectedSystabNode;
        public SystabTopicNode? SelectedSystabNode
        {
            get => _selectedSystabNode;
            set
            {
                if (_selectedSystabNode != value)
                {
                    _selectedSystabNode = value;
                    OnPropertyChanged();
                    LoadSystabEntries();
                }
            }
        }

        private ObservableCollection<SystabEntry> _systabEntries = new ObservableCollection<SystabEntry>();
        public ObservableCollection<SystabEntry> SystabEntries
        {
            get => _systabEntries;
            set { _systabEntries = value; OnPropertyChanged(); }
        }

        private ObservableCollection<SystabEntry> _allSystabEntries = new ObservableCollection<SystabEntry>();

        private string _systabSearchText = "";
        public string SystabSearchText
        {
            get => _systabSearchText;
            set
            {
                if (_systabSearchText != value)
                {
                    _systabSearchText = value;
                    OnPropertyChanged();
                    FilterSystabEntries();
                }
            }
        }

        private bool _systabShowDiffsOnly;
        public bool SystabShowDiffsOnly
        {
            get => _systabShowDiffsOnly;
            set
            {
                if (_systabShowDiffsOnly != value)
                {
                    _systabShowDiffsOnly = value;
                    OnPropertyChanged();
                    FilterSystabEntries();
                }
            }
        }

        public ICommand ClearSystabSearchCommand { get; private set; }

        private void LoadSystabEntries()
        {
            SystabEntries.Clear();
            _allSystabEntries.Clear();
            SystabSearchText = "";
            SystabShowDiffsOnly = false;

            if (_selectedSystabNode == null || _selectedSystabNode.Entries == null || _selectedSystabNode.Entries.Count == 0)
            {
                // If a topic node is selected (has children), show all entries from all children
                if (_selectedSystabNode != null && _selectedSystabNode.Children != null && _selectedSystabNode.Children.Count > 0)
                {
                    foreach (var child in _selectedSystabNode.Children)
                    {
                        if (child.Entries != null)
                        {
                            foreach (var entry in child.Entries)
                            {
                                _allSystabEntries.Add(entry);
                                SystabEntries.Add(entry);
                            }
                        }
                    }
                }
                return;
            }

            foreach (var entry in _selectedSystabNode.Entries)
            {
                _allSystabEntries.Add(entry);
                SystabEntries.Add(entry);
            }
        }

        private void FilterSystabEntries()
        {
            SystabEntries.Clear();
            string search = (SystabSearchText ?? "").Trim();
            foreach (var entry in _allSystabEntries)
            {
                if (_systabShowDiffsOnly && !entry.IsDifferent)
                    continue;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    if (!(entry.Parameter?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
                        !(entry.Saved?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
                        !(entry.Default?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
                        !(entry.Minimum?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
                        !(entry.Maximum?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        continue;
                    }
                }
                SystabEntries.Add(entry);
            }
        }

        public void LoadSystabFiles()
        {
            SystabTree.Clear();
            SystabEntries.Clear();
            _allSystabEntries.Clear();
            SelectedSystabNode = null;

            if (SessionVM?.SelectedSession?.SystabFiles != null && SessionVM.SelectedSession.SystabFiles.Count > 0)
            {
                var tree = Services.SystabParserService.BuildSystabTree(SessionVM.SelectedSession.SystabFiles);
                foreach (var node in tree)
                    SystabTree.Add(node);
            }
            OnPropertyChanged(nameof(HasSystabFiles));
        }
    }
}
