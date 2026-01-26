using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace IndiLogs_3._0.Views
{
    public partial class RowDetailWindow : Window
    {
        private readonly string _jsonContent;
        private List<ColumnValueItem> _allItems;
        private ObservableCollection<ColumnValueItem> _filteredItems;

        public RowDetailWindow(string jsonContent, string title)
        {
            InitializeComponent();
            _jsonContent = jsonContent;
            TitleText.Text = title;
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                // Parse JSON
                var jObject = JObject.Parse(_jsonContent);

                if (jObject == null || !jObject.HasValues)
                {
                    ColumnCountText.Text = "No data";
                    return;
                }

                // Flatten nested JSON to path.value pairs
                _allItems = new List<ColumnValueItem>();
                FlattenJson(jObject, "", _allItems);

                if (_allItems.Count == 0)
                {
                    ColumnCountText.Text = "No data";
                    return;
                }

                // Sort by column name
                _allItems = _allItems.OrderBy(x => x.ColumnName).ToList();

                // Bind to grid
                _filteredItems = new ObservableCollection<ColumnValueItem>(_allItems);
                DataGrid.ItemsSource = _filteredItems;

                ColumnCountText.Text = $"{_allItems.Count} fields";
                CountText.Text = "";
            }
            catch (Exception ex)
            {
                ColumnCountText.Text = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Recursively flattens a JSON object into path.value pairs.
        /// Example: {"Conductivity": {"CondFactor": 1.0}} becomes "Conductivity.CondFactor" = "1.0"
        /// </summary>
        private void FlattenJson(JToken token, string prefix, List<ColumnValueItem> result)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        string newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                        FlattenJson(prop.Value, newPrefix, result);
                    }
                    break;

                case JTokenType.Array:
                    var arr = (JArray)token;
                    if (arr.Count == 0)
                    {
                        result.Add(new ColumnValueItem { ColumnName = prefix, Value = "[]" });
                    }
                    else if (arr.All(a => a.Type != JTokenType.Object && a.Type != JTokenType.Array))
                    {
                        // Simple array - show values inline
                        result.Add(new ColumnValueItem
                        {
                            ColumnName = prefix,
                            Value = "[" + string.Join(", ", arr.Select(a => a.ToString())) + "]"
                        });
                    }
                    else
                    {
                        // Complex array - flatten each element
                        for (int i = 0; i < arr.Count; i++)
                        {
                            FlattenJson(arr[i], $"{prefix}[{i}]", result);
                        }
                    }
                    break;

                default:
                    // Primitive value (string, number, bool, null)
                    result.Add(new ColumnValueItem
                    {
                        ColumnName = prefix,
                        Value = token.Type == JTokenType.Null ? "(null)" : token.ToString()
                    });
                    break;
            }
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allItems == null) return;

            string filter = FilterTextBox.Text?.Trim().ToLower() ?? "";

            _filteredItems.Clear();

            if (string.IsNullOrEmpty(filter))
            {
                foreach (var item in _allItems)
                {
                    _filteredItems.Add(item);
                }
                CountText.Text = "";
            }
            else
            {
                var filtered = _allItems.Where(x =>
                    x.ColumnName.ToLower().Contains(filter) ||
                    x.Value.ToLower().Contains(filter)).ToList();

                foreach (var item in filtered)
                {
                    _filteredItems.Add(item);
                }
                CountText.Text = $"Showing {filtered.Count} / {_allItems.Count} fields";
            }
        }

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_filteredItems == null || _filteredItems.Count == 0)
                {
                    MessageBox.Show("No data to copy.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("Column\tValue");

                foreach (var item in _filteredItems)
                {
                    sb.AppendLine($"{item.ColumnName}\t{item.Value}");
                }

                Clipboard.SetText(sb.ToString());
                MessageBox.Show($"Copied {_filteredItems.Count} fields to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
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

    /// <summary>
    /// Represents a column name and its value for display in the grid
    /// </summary>
    public class ColumnValueItem : INotifyPropertyChanged
    {
        private string _columnName;
        private string _value;

        public string ColumnName
        {
            get => _columnName;
            set { _columnName = value; OnPropertyChanged(nameof(ColumnName)); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
