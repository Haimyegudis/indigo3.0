using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class PatternChartWindow : Window
    {
        public string ExtractionPattern { get; private set; } = "";
        public string ChartName { get; private set; } = "";
        public bool IncludeMachineState { get; private set; } = true;
        public PatternSearchField SearchField { get; private set; } = PatternSearchField.Message;

        private readonly string _originalMessage;

        public PatternChartWindow(string message)
        {
            InitializeComponent();
            _originalMessage = message ?? "";
            MessageTextBox.Text = _originalMessage;
            ChartNameTextBox.Text = "Pattern Chart";
        }

        private void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            string msg = _originalMessage;

            // Priority 1: Find the LAST "key = value" or "key=value" pattern
            // This handles messages like "Engage Rear DiffPosition =-37" where the
            // interesting value is the last one after '='
            var kvMatches = Regex.Matches(msg, @"=\s*(-?\d+\.?\d*)");
            if (kvMatches.Count > 0)
            {
                var lastKv = kvMatches[kvMatches.Count - 1];
                // Use everything up to and including the '=' as the fixed prefix
                int eqIdx = msg.LastIndexOf('=', lastKv.Index + lastKv.Length - 1, lastKv.Length);
                if (eqIdx < 0) eqIdx = lastKv.Index;
                string prefix = Regex.Escape(msg.Substring(0, eqIdx + 1));
                // Allow optional whitespace between '=' and the number
                PatternTextBox.Text = prefix + @"\s*(-?\d+\.?\d*)";
                return;
            }

            // Priority 2: Any standalone numeric value
            var match = Regex.Match(msg, @"-?\d+\.?\d*");
            if (match.Success)
            {
                string before = Regex.Escape(msg.Substring(0, match.Index));
                string after = Regex.Escape(msg.Substring(match.Index + match.Length));
                PatternTextBox.Text = before + @"(-?\d+\.?\d*)" + after;
                return;
            }

            // Priority 3: Boolean
            var boolMatch = Regex.Match(msg, @"\b(True|False)\b", RegexOptions.IgnoreCase);
            if (boolMatch.Success)
            {
                string before = Regex.Escape(msg.Substring(0, boolMatch.Index));
                string after = Regex.Escape(msg.Substring(boolMatch.Index + boolMatch.Length));
                PatternTextBox.Text = before + @"(True|False)" + after;
            }
        }

        private void PatternTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (PreviewLabel == null) return;
            string pattern = PatternTextBox.Text;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                PreviewLabel.Text = "";
                return;
            }

            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                var match = regex.Match(_originalMessage);
                if (match.Success && match.Groups.Count > 1)
                {
                    string captured = match.Groups[1].Value;
                    bool isNumeric = double.TryParse(captured, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double val);
                    PreviewLabel.Text = isNumeric
                        ? $"Preview: captured \"{captured}\" (numeric: {val})"
                        : $"Preview: captured \"{captured}\" (boolean/text)";
                }
                else
                {
                    PreviewLabel.Text = "No match on original message. Ensure pattern has a capture group (...)";
                }
            }
            catch (ArgumentException ex)
            {
                PreviewLabel.Text = $"Invalid regex: {ex.Message}";
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            string pattern = PatternTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.Show("Please enter an extraction pattern.", "Missing Pattern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"Invalid regex pattern:\n{ex.Message}", "Invalid Pattern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExtractionPattern = pattern;
            ChartName = string.IsNullOrWhiteSpace(ChartNameTextBox.Text) ? "Pattern Chart" : ChartNameTextBox.Text.Trim();
            IncludeMachineState = IncludeStatesCheckBox.IsChecked == true;
            SearchField = SearchFieldCombo.SelectedIndex switch
            {
                1 => PatternSearchField.Logger,
                2 => PatternSearchField.ThreadName,
                _ => PatternSearchField.Message
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public enum PatternSearchField
    {
        Message,
        Logger,
        ThreadName
    }
}
