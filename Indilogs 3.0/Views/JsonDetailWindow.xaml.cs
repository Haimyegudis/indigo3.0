using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace IndiLogs_3._0.Views
{
    public partial class JsonDetailWindow : Window
    {
        private readonly string _jsonContent;
        private readonly string _title;
        private List<KeyValuePair<string, string>> _allItems;
        private List<KeyValuePair<string, string>> _filteredItems;

        public JsonDetailWindow(string jsonContent, string title)
        {
            InitializeComponent();
            _jsonContent = jsonContent;
            _title = title;
            TitleText.Text = title;
            LoadJsonData();
        }

        private void LoadJsonData()
        {
            _allItems = new List<KeyValuePair<string, string>>();

            try
            {
                var obj = JObject.Parse(_jsonContent);

                // Get all leaf values with their full paths
                foreach (var token in obj.Descendants().OfType<JValue>())
                {
                    string path = token.Path;
                    string value = token.Value?.ToString() ?? "(null)";
                    _allItems.Add(new KeyValuePair<string, string>(path, value));
                }

                // Sort by path
                _allItems = _allItems.OrderBy(x => x.Key).ToList();
            }
            catch (Exception ex)
            {
                _allItems.Add(new KeyValuePair<string, string>("Error", ex.Message));
            }

            _filteredItems = _allItems;
            UpdateGrid();
        }

        private void UpdateGrid()
        {
            JsonDataGrid.ItemsSource = _filteredItems;
            CountText.Text = $"{_filteredItems.Count} / {_allItems.Count} properties";
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = FilterTextBox.Text?.Trim().ToLower() ?? "";

            if (string.IsNullOrEmpty(filter))
            {
                _filteredItems = _allItems;
            }
            else
            {
                _filteredItems = _allItems
                    .Where(x => x.Key.ToLower().Contains(filter) || x.Value.ToLower().Contains(filter))
                    .ToList();
            }

            UpdateGrid();
        }

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lines = _filteredItems.Select(x => $"{x.Key}\t{x.Value}");
                var text = string.Join(Environment.NewLine, lines);
                Clipboard.SetText(text);
                MessageBox.Show($"Copied {_filteredItems.Count} properties to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
