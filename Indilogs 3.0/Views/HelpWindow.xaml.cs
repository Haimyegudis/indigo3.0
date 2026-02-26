using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class HelpWindow : Window
    {
        private readonly FrameworkElement[] _sections;
        private bool _isNavigating;

        public HelpWindow()
        {
            InitializeComponent();

            // Build section anchors array matching TOC indices
            _sections = new FrameworkElement[13];
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Map section anchors after the visual tree is ready
            _sections[0] = Section0;
            _sections[1] = Section1;
            _sections[2] = Section2;
            _sections[3] = Section3;
            _sections[4] = Section4;
            _sections[5] = Section5;
            _sections[6] = Section6;
            _sections[7] = Section7;
            _sections[8] = Section8;
            _sections[9] = Section9;
            _sections[10] = Section10;
            _sections[11] = Section11;
            _sections[12] = Section12;

            // Select first TOC item
            TocList.SelectedIndex = 0;
        }

        private void TocList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TocList.SelectedItem is ListBoxItem item && item.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int index) && index >= 0 && index < _sections.Length && _sections[index] != null)
                {
                    _isNavigating = true;
                    _sections[index].BringIntoView();
                    _isNavigating = false;
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HelpSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = HelpSearchBox.Text?.Trim() ?? "";

            // Toggle placeholder visibility
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

            if (query.Length < 2)
            {
                // Clear highlights
                ClearHighlights(ContentPanel);
                SearchResultsText.Text = "";
                return;
            }

            // Highlight matching text and count results
            int matchCount = HighlightMatches(ContentPanel, query);
            SearchResultsText.Text = matchCount > 0
                ? $"Found {matchCount} match{(matchCount == 1 ? "" : "es")}"
                : "No matches found";

            // Scroll to first match
            if (matchCount > 0)
                ScrollToFirstHighlight(ContentPanel);
        }

        /// <summary>
        /// Recursively highlight matching text in all TextBlocks within a panel.
        /// Returns the total number of matches found.
        /// </summary>
        private int HighlightMatches(Panel panel, string query)
        {
            int totalMatches = 0;
            ClearHighlights(panel);

            foreach (var child in GetAllDescendants(panel))
            {
                if (child is TextBlock tb && tb.Name != "SearchPlaceholder" && tb.Name != "SearchResultsText")
                {
                    totalMatches += HighlightTextBlock(tb, query);
                }
            }

            return totalMatches;
        }

        /// <summary>
        /// Highlight matching text within a single TextBlock.
        /// For simple Text-only TextBlocks, we rebuild inlines.
        /// For complex TextBlocks with Runs/LineBreaks, we search each Run.
        /// </summary>
        private int HighlightTextBlock(TextBlock tb, string query)
        {
            int matches = 0;
            string fullText = GetTextBlockFullText(tb);

            if (string.IsNullOrEmpty(fullText)) return 0;
            if (fullText.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) return 0;

            // Count matches in the full text
            int searchIdx = 0;
            while ((searchIdx = fullText.IndexOf(query, searchIdx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                matches++;
                searchIdx += query.Length;
            }

            // For TextBlocks with simple Text property (no inlines), rebuild with highlights
            if (tb.Inlines.Count == 0 && !string.IsNullOrEmpty(tb.Text))
            {
                string text = tb.Text;
                tb.Text = null;
                tb.Inlines.Clear();
                BuildHighlightedInlines(tb.Inlines, text, query, tb.Foreground);
            }
            else if (tb.Inlines.Count > 0)
            {
                // Complex TextBlock with Runs/LineBreaks — rebuild inline by inline
                var inlinesCopy = tb.Inlines.ToList();
                tb.Inlines.Clear();

                foreach (var inline in inlinesCopy)
                {
                    if (inline is Run run)
                    {
                        if (run.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            BuildHighlightedInlines(tb.Inlines, run.Text, query, run.Foreground, run.FontWeight);
                        }
                        else
                        {
                            tb.Inlines.Add(new Run(run.Text)
                            {
                                Foreground = run.Foreground,
                                FontWeight = run.FontWeight,
                                FontStyle = run.FontStyle,
                                TextDecorations = run.TextDecorations
                            });
                        }
                    }
                    else if (inline is LineBreak)
                    {
                        tb.Inlines.Add(new LineBreak());
                    }
                    else
                    {
                        // Other inlines — just add back
                        // Can't re-parent, so skip complex inlines
                    }
                }
            }

            return matches;
        }

        /// <summary>
        /// Build highlighted inlines by splitting text at query matches.
        /// </summary>
        private void BuildHighlightedInlines(InlineCollection inlines, string text, string query,
            Brush defaultForeground, FontWeight? fontWeight = null)
        {
            int pos = 0;
            while (pos < text.Length)
            {
                int matchIdx = text.IndexOf(query, pos, StringComparison.OrdinalIgnoreCase);
                if (matchIdx < 0)
                {
                    // Remainder - no more matches
                    var run = new Run(text.Substring(pos)) { Foreground = defaultForeground };
                    if (fontWeight.HasValue) run.FontWeight = fontWeight.Value;
                    inlines.Add(run);
                    break;
                }

                // Text before match
                if (matchIdx > pos)
                {
                    var beforeRun = new Run(text.Substring(pos, matchIdx - pos)) { Foreground = defaultForeground };
                    if (fontWeight.HasValue) beforeRun.FontWeight = fontWeight.Value;
                    inlines.Add(beforeRun);
                }

                // Highlighted match
                var highlightRun = new Run(text.Substring(matchIdx, query.Length))
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)), // WarningColor amber
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                };
                inlines.Add(highlightRun);

                pos = matchIdx + query.Length;
            }
        }

        private string GetTextBlockFullText(TextBlock tb)
        {
            if (tb.Inlines.Count == 0)
                return tb.Text ?? "";

            var parts = new List<string>();
            foreach (var inline in tb.Inlines)
            {
                if (inline is Run run)
                    parts.Add(run.Text ?? "");
            }
            return string.Join("", parts);
        }

        /// <summary>
        /// Clear all highlights by restoring TextBlocks to their original state.
        /// This is a simplified approach - we rely on the XAML being re-read for full reset.
        /// For now, we just remove highlight backgrounds from Runs.
        /// </summary>
        private void ClearHighlights(Panel panel)
        {
            // Simple approach: clear highlight background from all Runs
            foreach (var child in GetAllDescendants(panel))
            {
                if (child is TextBlock tb)
                {
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run && run.Background is SolidColorBrush scb)
                        {
                            var amber = Color.FromRgb(245, 158, 11);
                            if (scb.Color == amber)
                            {
                                run.Background = Brushes.Transparent;
                                run.Foreground = (Brush)FindResource("TextPrimary");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Scroll to the first TextBlock that contains a highlighted Run.
        /// </summary>
        private void ScrollToFirstHighlight(Panel panel)
        {
            foreach (var child in GetAllDescendants(panel))
            {
                if (child is TextBlock tb)
                {
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run && run.Background is SolidColorBrush scb)
                        {
                            var amber = Color.FromRgb(245, 158, 11);
                            if (scb.Color == amber)
                            {
                                tb.BringIntoView();
                                return;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get all visual descendants of a DependencyObject.
        /// </summary>
        private IEnumerable<DependencyObject> GetAllDescendants(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var grandChild in GetAllDescendants(child))
                    yield return grandChild;
            }
        }
    }
}
