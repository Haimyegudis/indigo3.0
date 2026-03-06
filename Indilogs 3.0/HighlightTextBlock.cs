using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.Controls
{
    /// <summary>
    /// A read-only RichTextBox that supports search term highlighting (yellow background)
    /// and text selection via click-and-drag, while forwarding keyboard events to the DataGrid.
    /// Single click selects the DataGrid row; click-and-drag selects text for copying.
    /// </summary>
    public partial class HighlightTextBlock : RichTextBox
    {
        // Mouse click-vs-drag state
        private bool _isDragging;
        private Point _mouseDownPosition;
        private bool _mouseIsDown;

        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.Register("HighlightText", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightTextChanged));

        public string HighlightText
        {
            get { return (string)GetValue(HighlightTextProperty); }
            set { SetValue(HighlightTextProperty, value); }
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnTextChanged));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty TextTrimmingProperty =
            DependencyProperty.Register("TextTrimming", typeof(TextTrimming), typeof(HighlightTextBlock),
                new PropertyMetadata(TextTrimming.None));

        public TextTrimming TextTrimming
        {
            get { return (TextTrimming)GetValue(TextTrimmingProperty); }
            set { SetValue(TextTrimmingProperty, value); }
        }

        public static readonly DependencyProperty TextWrappingProperty =
            DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(HighlightTextBlock),
                new PropertyMetadata(TextWrapping.NoWrap));

        public TextWrapping TextWrapping
        {
            get { return (TextWrapping)GetValue(TextWrappingProperty); }
            set { SetValue(TextWrappingProperty, value); }
        }

        public HighlightTextBlock()
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            Padding = new Thickness(0);
            Margin = new Thickness(0);
            CaretBrush = Brushes.Transparent;
            IsDocumentEnabled = true;
            VerticalContentAlignment = VerticalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;

            // Disable scrollbars - single line display
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

            // Remove default context menu
            ContextMenu = null;

            // Don't accept tab focus - let DataGrid handle tab navigation
            IsTabStop = false;

            // Initialize empty document
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = 1
            };
        }

        // When the Foreground property changes (e.g. via a style trigger setting black text
        // on a coloured row), rebuild the FlowDocument so the Run elements pick up the
        // new colour.  Without this, Runs created with { Foreground = Foreground } keep
        // the stale captured value and the text stays white in dark-mode on coloured rows.
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == TextElement.ForegroundProperty)
            {
                UpdateHighlighting();
            }
        }

        #region Highlighting

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
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                LineHeight = double.NaN,
                TextAlignment = TextAlignment.Left
            };

            string text = Text;
            string highlight = HighlightText;

            if (string.IsNullOrEmpty(text))
            {
                Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
                return;
            }

            if (string.IsNullOrEmpty(highlight) || highlight.Length < 2)
            {
                // No explicit Foreground – inherits from HighlightTextBlock.Foreground so that
                // style triggers (e.g. black text on coloured rows) propagate automatically.
                paragraph.Inlines.Add(new Run(text));
                Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
                return;
            }

            // Use fast string.IndexOf instead of Regex for performance
            int pos = 0;
            int highlightLen = highlight.Length;

            while (pos < text.Length)
            {
                int matchIdx = text.IndexOf(highlight, pos, StringComparison.OrdinalIgnoreCase);
                if (matchIdx < 0)
                {
                    // No more matches - add remaining text (no explicit Foreground – inherits)
                    if (pos < text.Length)
                        paragraph.Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }

                // Add text before match (no explicit Foreground – inherits)
                if (matchIdx > pos)
                    paragraph.Inlines.Add(new Run(text.Substring(pos, matchIdx - pos)));

                // Add highlighted match
                paragraph.Inlines.Add(new Run(text.Substring(matchIdx, highlightLen))
                {
                    Background = Brushes.Yellow,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                });

                pos = matchIdx + highlightLen;
            }

            // If nothing was added, add empty run (no explicit Foreground – inherits)
            if (!paragraph.Inlines.FirstInline?.ContentStart.HasValidLayout == true || paragraph.Inlines.Count == 0)
            {
                if (paragraph.Inlines.Count == 0)
                    paragraph.Inlines.Add(new Run(text));
            }

            Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        }

        #endregion

        #region Helpers

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? parent = child is Visual || child is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found)
                    return found;
                parent = parent is Visual || parent is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(parent)
                    : LogicalTreeHelper.GetParent(parent);
            }
            return null;
        }

        #endregion
    }
}
