using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IndiLogs_3._0.Controls
{
    public class HighlightTextBlock : TextBlock
    {
        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.Register("HighlightText", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightTextChanged));

        public string HighlightText
        {
            get { return (string)GetValue(HighlightTextProperty); }
            set { SetValue(HighlightTextProperty, value); }
        }

        public new string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public new static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnTextChanged));

        private static void OnHighlightTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).UpdateHighlighting();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            Inlines.Clear();

            string text = Text;
            string highlight = HighlightText;

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.IsNullOrEmpty(highlight) || highlight.Length < 2)
            {
                Inlines.Add(new Run(text));
                return;
            }

            // Use fast string.IndexOf instead of Regex.Split for performance
            int pos = 0;
            int highlightLen = highlight.Length;

            while (pos < text.Length)
            {
                int matchIdx = text.IndexOf(highlight, pos, StringComparison.OrdinalIgnoreCase);
                if (matchIdx < 0)
                {
                    // No more matches - add remaining text
                    if (pos < text.Length)
                        Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }

                // Add text before match
                if (matchIdx > pos)
                    Inlines.Add(new Run(text.Substring(pos, matchIdx - pos)));

                // Add highlighted match
                Inlines.Add(new Run(text.Substring(matchIdx, highlightLen))
                {
                    Background = Brushes.Yellow,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                });

                pos = matchIdx + highlightLen;
            }

            // If nothing was added (empty text), add empty run
            if (Inlines.Count == 0)
                Inlines.Add(new Run(text));
        }
    }
}