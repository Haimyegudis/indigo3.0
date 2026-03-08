using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    /// <summary>
    /// Signal item for display in the list
    /// </summary>
    public class SignalListItem
    {
        public string FullName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string TypeIcon { get; set; } = "";
        public Brush? TypeColor { get; set; }
        public SignalItemCategory Category { get; set; }

        // For CHSTEP - store state data
        public StateData? StateData { get; set; }

        // For THREAD - store thread name
        public string? ThreadName { get; set; }
        public List<ThreadMessageData>? ThreadMessages { get; set; }
    }

    public enum SignalItemCategory
    {
        All,     // All signals
        Axis,    // Axis/Motion signals
        IO,      // IO signals
        CHStep,  // CHSTEP Gantt
        Thread,  // Thread messages
        Events,  // Event markers
        EmStats  // EM Statistics Gantt
    }

    public partial class ChartSignalList : UserControl
    {
        public event Action<SignalListItem>? OnItemDoubleClicked;
        public event Action<string>? OnSignalDoubleClicked; // Legacy event for signal names

        private List<SignalListItem> _allItems = new List<SignalListItem>();
        private List<SignalListItem> _filteredItems = new List<SignalListItem>();

        // Debounce timer for search
        private DispatcherTimer _searchDebounceTimer;
        private string _pendingSearchText = "";

        // Color mapping for types
        private static readonly Brush AxisColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));    // Green
        private static readonly Brush IOColor = new SolidColorBrush(Color.FromRgb(33, 150, 243));     // Blue
        private static readonly Brush CHStepColor = new SolidColorBrush(Color.FromRgb(255, 152, 0));  // Orange
        private static readonly Brush ThreadColor = new SolidColorBrush(Color.FromRgb(156, 39, 176)); // Purple
        private static readonly Brush EventsColor = new SolidColorBrush(Color.FromRgb(244, 67, 54));   // Red
        private static readonly Brush EmStatsColor = new SolidColorBrush(Color.FromRgb(0, 150, 136)); // Teal
        private static readonly Brush DefaultColor = new SolidColorBrush(Color.FromRgb(96, 125, 139)); // Gray

        public ChartSignalList()
        {
            InitializeComponent();

            // Setup debounce timer for search (150ms delay)
            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            // Update placeholder visibility
            SearchBox.TextChanged += (s, e) =>
            {
                SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
                ClearSearchBtn.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Collapsed : Visibility.Visible;
            };
        }

        public SignalListItem? SelectedItem => SignalListBox.SelectedItem as SignalListItem;
        public string? SelectedSignal => SelectedItem?.FullName;

        private void ApplyFilters()
        {
            string searchText = _pendingSearchText;

            _filteredItems = _allItems.Where(item =>
            {
                // Category filter
                bool categoryMatch = true;
                if (AxisBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.Axis;
                else if (IOBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.IO;
                else if (CHStepBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.CHStep;
                else if (ThreadBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.Thread;
                else if (EventsBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.Events;
                else if (EmStatsBtn?.IsChecked == true)
                    categoryMatch = item.Category == SignalItemCategory.EmStats;
                // AllBtn shows everything

                if (!categoryMatch)
                    return false;

                // Search filter
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!item.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
                        !item.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();

            if (SignalListBox != null)
            {
                SignalListBox.ItemsSource = _filteredItems;
                ItemCountText.Text = $"{_filteredItems.Count} items";
            }
        }

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _pendingSearchText = SearchBox.Text ?? "";

            // Restart debounce timer
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ApplyFilters();
        }

        private void CategoryButton_Checked(object? sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void ClearSearchBtn_Click(object? sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            _pendingSearchText = "";
            ApplyFilters();
        }

        private void SignalListBox_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SignalListBox.SelectedItem is SignalListItem item)
            {
                OnItemDoubleClicked?.Invoke(item);
                OnSignalDoubleClicked?.Invoke(item.FullName);
            }
        }
    }
}
