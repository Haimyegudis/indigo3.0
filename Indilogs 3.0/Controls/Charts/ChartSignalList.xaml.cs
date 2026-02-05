using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartSignalList : UserControl
    {
        public event Action<string> OnSignalDoubleClicked;

        private List<string> _allSignals = new List<string>();
        private List<string> _filteredSignals = new List<string>();

        public ChartSignalList()
        {
            InitializeComponent();
        }

        public void SetSignals(List<string> signals)
        {
            _allSignals = signals ?? new List<string>();
            ApplyFilters();
        }

        public string SelectedSignal => SignalListBox.SelectedItem as string;

        private void ApplyFilters()
        {
            string searchText = SearchBox?.Text?.ToLower() ?? "";
            string category = (CategoryCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Signals";

            _filteredSignals = _allSignals.Where(s =>
            {
                // Search filter
                if (!string.IsNullOrEmpty(searchText) && !s.ToLower().Contains(searchText))
                    return false;

                // Category filter
                if (category != "All Signals")
                {
                    var categoryConfig = SignalCategory.DefaultCategories.FirstOrDefault(c => c.Name == category);
                    if (categoryConfig != null && categoryConfig.Keywords.Length > 0)
                    {
                        string lower = s.ToLower();
                        if (!categoryConfig.Keywords.Any(k => lower.Contains(k)))
                            return false;
                    }
                }

                return true;
            }).ToList();

            if (SignalListBox != null)
                SignalListBox.ItemsSource = _filteredSignals;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void SignalListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SignalListBox.SelectedItem is string signal)
            {
                OnSignalDoubleClicked?.Invoke(signal);
            }
        }
    }
}
