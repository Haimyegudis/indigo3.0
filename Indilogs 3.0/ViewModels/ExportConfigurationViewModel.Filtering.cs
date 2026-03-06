using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public partial class ExportConfigurationViewModel
    {
        // Cached filtered lists for performance
        private List<SelectableItem>? _cachedIOFiltered;
        private List<SelectableItem>? _cachedAxisFiltered;
        private List<SelectableItem>? _cachedCHStepFiltered;
        private List<SelectableItem>? _cachedThreadFiltered;

        // Debounce timer for search - prevents lag while typing
        private DispatcherTimer? _searchDebounceTimer;
        private const int SEARCH_DEBOUNCE_MS = 300;
        private bool _ioSearchPending = false;
        private bool _axisSearchPending = false;
        private bool _chStepSearchPending = false;
        private bool _threadSearchPending = false;

        private string _ioSearchText = string.Empty;
        public string IOSearchText
        {
            get => _ioSearchText;
            set
            {
                _ioSearchText = value;
                OnPropertyChanged(nameof(IOSearchText));

                // Debounced search - mark as pending and restart timer
                _ioSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _axisSearchText = string.Empty;
        public string AxisSearchText
        {
            get => _axisSearchText;
            set
            {
                _axisSearchText = value;
                OnPropertyChanged(nameof(AxisSearchText));

                _axisSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _chStepSearchText = string.Empty;
        public string CHStepSearchText
        {
            get => _chStepSearchText;
            set
            {
                _chStepSearchText = value;
                OnPropertyChanged(nameof(CHStepSearchText));

                _chStepSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        private string _threadSearchText = string.Empty;
        public string ThreadSearchText
        {
            get => _threadSearchText;
            set
            {
                _threadSearchText = value;
                OnPropertyChanged(nameof(ThreadSearchText));

                _threadSearchPending = true;
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer?.Start();
            }
        }

        public IEnumerable<SelectableItem> FilteredIOComponents =>
            _cachedIOFiltered != null ? (IEnumerable<SelectableItem>)_cachedIOFiltered : IOComponents;
        public IEnumerable<SelectableItem> FilteredAxisComponents =>
            _cachedAxisFiltered != null ? (IEnumerable<SelectableItem>)_cachedAxisFiltered : AxisComponents;
        public IEnumerable<SelectableItem> FilteredCHStepComponents =>
            _cachedCHStepFiltered != null ? (IEnumerable<SelectableItem>)_cachedCHStepFiltered : CHStepComponents;
        public IEnumerable<SelectableItem> FilteredThreadItems =>
            _cachedThreadFiltered != null ? (IEnumerable<SelectableItem>)_cachedThreadFiltered : ThreadItems;

        private void UpdateIOFilter()
        {
            if (string.IsNullOrWhiteSpace(IOSearchText))
            {
                _cachedIOFiltered = null;
            }
            else
            {
                var search = IOSearchText.ToLowerInvariant();
                _cachedIOFiltered = IOComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredIOComponents));
        }

        private void UpdateAxisFilter()
        {
            if (string.IsNullOrWhiteSpace(AxisSearchText))
            {
                _cachedAxisFiltered = null;
            }
            else
            {
                var search = AxisSearchText.ToLowerInvariant();
                _cachedAxisFiltered = AxisComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredAxisComponents));
        }

        private void UpdateCHStepFilter()
        {
            if (string.IsNullOrWhiteSpace(CHStepSearchText))
            {
                _cachedCHStepFiltered = null;
            }
            else
            {
                var search = CHStepSearchText.ToLowerInvariant();
                _cachedCHStepFiltered = CHStepComponents.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredCHStepComponents));
        }

        private void UpdateThreadFilter()
        {
            if (string.IsNullOrWhiteSpace(ThreadSearchText))
            {
                _cachedThreadFiltered = null;
            }
            else
            {
                var search = ThreadSearchText.ToLowerInvariant();
                _cachedThreadFiltered = ThreadItems.Where(item =>
                    item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            OnPropertyChanged(nameof(FilteredThreadItems));
        }

        // Timer tick - execute pending searches
        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();

            // Execute only pending searches
            if (_ioSearchPending)
            {
                _ioSearchPending = false;
                UpdateIOFilter();
            }

            if (_axisSearchPending)
            {
                _axisSearchPending = false;
                UpdateAxisFilter();
            }

            if (_chStepSearchPending)
            {
                _chStepSearchPending = false;
                UpdateCHStepFilter();
            }

            if (_threadSearchPending)
            {
                _threadSearchPending = false;
                UpdateThreadFilter();
            }
        }
    }
}
