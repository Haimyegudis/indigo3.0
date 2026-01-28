using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IndiLogs_3._0.Views
{
    public partial class RowDetailWindow : Window
    {
        private readonly string _formattedJson;

        // Colors for JSON syntax highlighting (VS Code dark theme style)
        private static readonly SolidColorBrush KeyColor = new SolidColorBrush(Color.FromRgb(156, 220, 254));      // Light blue for keys
        private static readonly SolidColorBrush StringColor = new SolidColorBrush(Color.FromRgb(206, 145, 120));   // Orange for strings
        private static readonly SolidColorBrush NumberColor = new SolidColorBrush(Color.FromRgb(181, 206, 168));   // Light green for numbers
        private static readonly SolidColorBrush BoolNullColor = new SolidColorBrush(Color.FromRgb(86, 156, 214));  // Blue for bool/null
        private static readonly SolidColorBrush BracketColor = new SolidColorBrush(Color.FromRgb(212, 212, 212));  // Gray for brackets
        private static readonly SolidColorBrush DefaultColor = new SolidColorBrush(Color.FromRgb(212, 212, 212));  // Default gray

        public RowDetailWindow(string jsonString, string title)
        {
            InitializeComponent();
            TitleText.Text = title;

            // Format JSON with indentation
            _formattedJson = FormatJson(jsonString);

            // Apply syntax highlighting
            ApplySyntaxHighlighting(_formattedJson);
        }

        private string FormatJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "(empty)";

            try
            {
                var obj = JToken.Parse(json);
                return obj.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                return $"Error parsing JSON: {ex.Message}\n\nRaw data:\n{json}";
            }
        }

        private void ApplySyntaxHighlighting(string json)
        {
            var document = new FlowDocument();
            document.PageWidth = 2000; // Prevent word wrap issues

            // Split by lines to handle multi-line JSON properly
            var lines = json.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var paragraph = new Paragraph();
                paragraph.Margin = new Thickness(0);
                paragraph.LineHeight = 1; // Minimal line height, actual height from font

                if (string.IsNullOrEmpty(line))
                {
                    // Empty line - add a space to preserve it
                    paragraph.Inlines.Add(new Run(" ") { Foreground = DefaultColor });
                }
                else
                {
                    ApplyLineHighlighting(line, paragraph);
                }

                document.Blocks.Add(paragraph);
            }

            JsonRichTextBox.Document = document;
        }

        private void ApplyLineHighlighting(string line, Paragraph paragraph)
        {
            // Regex patterns for JSON elements
            var keyPattern = @"""([^""\\]|\\.)*""\s*:";
            var stringPattern = @":\s*""([^""\\]|\\.)*""";
            var numberPattern = @":\s*(-?\d+\.?\d*([eE][+-]?\d+)?)";
            var boolNullPattern = @":\s*(true|false|null)";
            var arrayStringPattern = @"(?<=[\[,]\s*)""([^""\\]|\\.)*""";
            var arrayNumberPattern = @"(?<=[\[,]\s*)(-?\d+\.?\d*([eE][+-]?\d+)?)(?=\s*[,\]])";
            var bracketPattern = @"[\{\}\[\]]";

            var matches = new System.Collections.Generic.List<(int Index, int Length, SolidColorBrush Color)>();

            // Find all keys
            foreach (Match match in Regex.Matches(line, keyPattern))
            {
                var keyText = match.Value.TrimEnd(':', ' ');
                matches.Add((match.Index, keyText.Length, KeyColor));
            }

            // Find string values (after colon)
            foreach (Match match in Regex.Matches(line, stringPattern))
            {
                var colonIndex = match.Value.IndexOf(':');
                var valueStart = match.Index + colonIndex + 1;
                var valueText = match.Value.Substring(colonIndex + 1).Trim();
                var actualStart = valueStart + (match.Value.Length - colonIndex - 1 - valueText.Length);
                if (actualStart >= 0 && actualStart + valueText.Length <= line.Length)
                    matches.Add((actualStart, valueText.Length, StringColor));
            }

            // Find number values
            foreach (Match match in Regex.Matches(line, numberPattern))
            {
                var colonIndex = match.Value.IndexOf(':');
                var valueStart = match.Index + colonIndex + 1;
                var valueText = match.Value.Substring(colonIndex + 1).Trim();
                var actualStart = valueStart + (match.Value.Length - colonIndex - 1 - valueText.Length);
                if (actualStart >= 0 && actualStart + valueText.Length <= line.Length)
                    matches.Add((actualStart, valueText.Length, NumberColor));
            }

            // Find bool/null values
            foreach (Match match in Regex.Matches(line, boolNullPattern))
            {
                var colonIndex = match.Value.IndexOf(':');
                var valueStart = match.Index + colonIndex + 1;
                var valueText = match.Value.Substring(colonIndex + 1).Trim();
                var actualStart = valueStart + (match.Value.Length - colonIndex - 1 - valueText.Length);
                if (actualStart >= 0 && actualStart + valueText.Length <= line.Length)
                    matches.Add((actualStart, valueText.Length, BoolNullColor));
            }

            // Find array string values
            foreach (Match match in Regex.Matches(line, arrayStringPattern))
            {
                matches.Add((match.Index, match.Length, StringColor));
            }

            // Find array number values
            foreach (Match match in Regex.Matches(line, arrayNumberPattern))
            {
                matches.Add((match.Index, match.Length, NumberColor));
            }

            // Find brackets and braces
            foreach (Match match in Regex.Matches(line, bracketPattern))
            {
                matches.Add((match.Index, match.Length, BracketColor));
            }

            // Sort matches by index and remove overlaps
            matches.Sort((a, b) => a.Index.CompareTo(b.Index));

            // Build the line with colored runs
            int lastIndex = 0;
            foreach (var match in matches)
            {
                if (match.Index < lastIndex) continue; // Skip overlapping matches

                // Add text before this match
                if (match.Index > lastIndex)
                {
                    var beforeText = line.Substring(lastIndex, match.Index - lastIndex);
                    paragraph.Inlines.Add(new Run(beforeText) { Foreground = DefaultColor });
                }

                // Add the colored match
                if (match.Index + match.Length <= line.Length)
                {
                    var matchText = line.Substring(match.Index, match.Length);
                    paragraph.Inlines.Add(new Run(matchText) { Foreground = match.Color });
                    lastIndex = match.Index + match.Length;
                }
            }

            // Add remaining text
            if (lastIndex < line.Length)
            {
                paragraph.Inlines.Add(new Run(line.Substring(lastIndex)) { Foreground = DefaultColor });
            }
        }

        private void OpenInNotepadPlusPlus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create temp file with .json extension
                var tempFile = Path.Combine(Path.GetTempPath(), $"indilogs_json_{Guid.NewGuid():N}.json");
                File.WriteAllText(tempFile, _formattedJson);

                // Try to find Notepad++
                var notepadPlusPlusPath = FindNotepadPlusPlus();

                if (!string.IsNullOrEmpty(notepadPlusPlusPath))
                {
                    Process.Start(notepadPlusPlusPath, $"\"{tempFile}\"");
                }
                else
                {
                    // Fallback to default application for .json files
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempFile,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open in external editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FindNotepadPlusPlus()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files\Notepad++\notepad++.exe",
                @"C:\Program Files (x86)\Notepad++\notepad++.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Notepad++\notepad++.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_formattedJson);
                MessageBox.Show("Copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
