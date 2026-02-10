using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    /// <summary>
    /// Signal item for display in the list
    /// </summary>
    public class SignalListItem
    {
        public string FullName { get; set; }
        public string DisplayName { get; set; }
        public string TypeIcon { get; set; }
        public Brush TypeColor { get; set; }
        public SignalItemCategory Category { get; set; }

        // For CHSTEP - store state data
        public StateData StateData { get; set; }

        // For THREAD - store thread name
        public string ThreadName { get; set; }
        public List<ThreadMessageData> ThreadMessages { get; set; }
    }

    public enum SignalItemCategory
    {
        All,     // All signals
        Axis,    // Axis/Motion signals
        IO,      // IO signals
        CHStep,  // CHSTEP Gantt
        Thread,  // Thread messages
        Events   // Event markers
    }

    public partial class ChartSignalList : UserControl
    {
        public event Action<SignalListItem> OnItemDoubleClicked;
        public event Action<string> OnSignalDoubleClicked; // Legacy event for signal names

        private List<SignalListItem> _allItems = new List<SignalListItem>();
        private List<SignalListItem> _filteredItems = new List<SignalListItem>();
        private SignalItemCategory _currentCategory = SignalItemCategory.All;

        // Debounce timer for search
        private DispatcherTimer _searchDebounceTimer;
        private string _pendingSearchText = "";

        // Color mapping for types
        private static readonly Brush AxisColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));    // Green
        private static readonly Brush IOColor = new SolidColorBrush(Color.FromRgb(33, 150, 243));     // Blue
        private static readonly Brush CHStepColor = new SolidColorBrush(Color.FromRgb(255, 152, 0));  // Orange
        private static readonly Brush ThreadColor = new SolidColorBrush(Color.FromRgb(156, 39, 176)); // Purple
        private static readonly Brush EventsColor = new SolidColorBrush(Color.FromRgb(244, 67, 54));   // Red
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

        /// <summary>
        /// Sets signal names (for CSV mode - legacy compatibility)
        /// </summary>
        public void SetSignals(List<string> signals)
        {
            _allItems.Clear();

            foreach (var signal in signals ?? new List<string>())
            {
                var item = CreateSignalItem(signal);
                _allItems.Add(item);
            }

            ApplyFilters();
        }

        /// <summary>
        /// Sets full data package (for In-Memory mode with CHSTEP and Thread support)
        /// </summary>
        public void SetDataPackage(ChartDataPackage package)
        {
            _allItems.Clear();

            if (package == null)
            {
                ApplyFilters();
                return;
            }

            // Add regular signals (AXIS, IO) - use Category from SignalData
            foreach (var signal in package.Signals)
            {
                var item = CreateSignalItemFromData(signal);
                _allItems.Add(item);
            }

            // Add CHSTEP items (excluding MachineState which goes to timeline)
            foreach (var state in package.States)
            {
                if (state.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase))
                    continue;

                string displayName = !string.IsNullOrEmpty(state.Category)
                    ? $"{state.Category} > {state.Name}" : state.Name;
                string fullName = !string.IsNullOrEmpty(state.Category)
                    ? $"{state.Category}|{state.Name}" : state.Name;

                _allItems.Add(new SignalListItem
                {
                    FullName = fullName,
                    DisplayName = displayName,
                    TypeIcon = "G",
                    TypeColor = CHStepColor,
                    Category = SignalItemCategory.CHStep,
                    StateData = state
                });
            }

            // Add Thread items (group by thread name)
            var threadGroups = package.ThreadMessages
                .GroupBy(m => m.ThreadName)
                .OrderBy(g => g.Key);

            foreach (var group in threadGroups)
            {
                _allItems.Add(new SignalListItem
                {
                    FullName = group.Key,
                    DisplayName = $"{group.Key} ({group.Count()} msgs)",
                    TypeIcon = "T",
                    TypeColor = ThreadColor,
                    Category = SignalItemCategory.Thread,
                    ThreadName = group.Key,
                    ThreadMessages = group.ToList()
                });
            }

            // Add Events item if events exist
            if (package.Events != null && package.Events.Count > 0)
            {
                _allItems.Add(new SignalListItem
                {
                    FullName = "[Events]",
                    DisplayName = $"Events ({package.Events.Count})",
                    TypeIcon = "E",
                    TypeColor = EventsColor,
                    Category = SignalItemCategory.Events
                });
            }

            ApplyFilters();
        }

        /// <summary>
        /// Creates signal item from SignalData (uses Category from data)
        /// </summary>
        private SignalListItem CreateSignalItemFromData(SignalData signalData)
        {
            var item = new SignalListItem
            {
                FullName = signalData.Name,
                DisplayName = signalData.Name
            };

            // Use the Category from the parsed SignalData
            string category = signalData.Category?.Trim() ?? "";

            // Match exact category names from ChartDataTransferService
            if (category.Equals("Axis", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("AxisMon", StringComparison.OrdinalIgnoreCase))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (category.Equals("IO", StringComparison.OrdinalIgnoreCase))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else
            {
                // Default for unknown categories
                item.Category = SignalItemCategory.All;
                item.TypeIcon = "S";
                item.TypeColor = DefaultColor;
            }

            return item;
        }

        /// <summary>
        /// Creates signal item from name only (legacy for CSV mode).
        /// Recognizes exported CSV header patterns to assign proper categories.
        /// </summary>
        private SignalListItem CreateSignalItem(string signalName)
        {
            var item = new SignalListItem
            {
                FullName = signalName,
                DisplayName = signalName
            };

            string lower = signalName.ToLower();

            // Check for exported CSV patterns first (from CsvExportService):
            //   Axis:  "Subsys-Motor-Param [Thread]" with Param = SetP/ActP/SetV/ActV/Trq/LagErr
            //   IO:    "Subsys-Comp-Value [IOs-I]" or [IOs-Q]
            //   CHStep: column name contains § separator
            //   Thread: ends with _Message (but not Events_Message)

            if (lower.Contains("§") || lower.StartsWith("chstep"))
            {
                item.Category = SignalItemCategory.CHStep;
                item.TypeIcon = "G";
                item.TypeColor = CHStepColor;
            }
            else if (lower.Contains("[ios-") || lower.Contains("[io_mon"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else if (lower.Contains("-setp") || lower.Contains("-actp") || lower.Contains("-setv") ||
                     lower.Contains("-actv") || lower.Contains("-trq") || lower.Contains("-lagerr"))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (lower.Contains("-value") || lower.Contains("-mottemp") || lower.Contains("-drvtemp"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else if (lower.Contains("axis") || lower.Contains("motor") || lower.Contains("position") ||
                     lower.Contains("velocity") || lower.Contains("encoder") || lower.Contains("servo"))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (lower.Contains("io_") || lower.Contains("sensor") || lower.Contains("switch") ||
                     lower.Contains("valve") || lower.Contains("relay") ||
                     lower.StartsWith("di_") || lower.StartsWith("do_") ||
                     lower.StartsWith("ai_") || lower.StartsWith("ao_"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else
            {
                item.Category = SignalItemCategory.All;
                item.TypeIcon = "S";
                item.TypeColor = DefaultColor;
            }

            return item;
        }

        public SignalListItem SelectedItem => SignalListBox.SelectedItem as SignalListItem;
        public string SelectedSignal => SelectedItem?.FullName;

        private void ApplyFilters()
        {
            string searchText = _pendingSearchText.ToLower();

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
                // AllBtn shows everything

                if (!categoryMatch)
                    return false;

                // Search filter
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!item.FullName.ToLower().Contains(searchText) &&
                        !item.DisplayName.ToLower().Contains(searchText))
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pendingSearchText = SearchBox.Text ?? "";

            // Restart debounce timer
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ApplyFilters();
        }

        private void CategoryButton_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            _pendingSearchText = "";
            ApplyFilters();
        }

        private void SignalListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SignalListBox.SelectedItem is SignalListItem item)
            {
                OnItemDoubleClicked?.Invoke(item);
                OnSignalDoubleClicked?.Invoke(item.FullName);
            }
        }
    }
}
