using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls
{
    /// <summary>
    /// A TextBlock that highlights text differences using colored segments.
    /// Used in the comparison window to show character-level differences.
    /// </summary>
    public class DiffHighlightTextBlock : TextBlock
    {
        #region Dependency Properties

        public static readonly DependencyProperty DiffSegmentsProperty =
            DependencyProperty.Register(
                nameof(DiffSegments),
                typeof(IList<DiffSegment>),
                typeof(DiffHighlightTextBlock),
                new PropertyMetadata(null, OnDiffSegmentsChanged));

        public static readonly DependencyProperty IsDifferentProperty =
            DependencyProperty.Register(
                nameof(IsDifferent),
                typeof(bool),
                typeof(DiffHighlightTextBlock),
                new PropertyMetadata(false, OnIsDifferentChanged));

        public static readonly DependencyProperty PlainTextProperty =
            DependencyProperty.Register(
                nameof(PlainText),
                typeof(string),
                typeof(DiffHighlightTextBlock),
                new PropertyMetadata(string.Empty, OnPlainTextChanged));

        public static readonly DependencyProperty ShowDiffsProperty =
            DependencyProperty.Register(
                nameof(ShowDiffs),
                typeof(bool),
                typeof(DiffHighlightTextBlock),
                new PropertyMetadata(true, OnShowDiffsChanged));

        #endregion

        #region Properties

        /// <summary>
        /// The diff segments to display with highlighting.
        /// </summary>
        public IList<DiffSegment> DiffSegments
        {
            get => (IList<DiffSegment>)GetValue(DiffSegmentsProperty);
            set => SetValue(DiffSegmentsProperty, value);
        }

        /// <summary>
        /// Whether this text block contains differences.
        /// </summary>
        public bool IsDifferent
        {
            get => (bool)GetValue(IsDifferentProperty);
            set => SetValue(IsDifferentProperty, value);
        }

        /// <summary>
        /// The plain text to display when no segments are available.
        /// </summary>
        public string PlainText
        {
            get => (string)GetValue(PlainTextProperty);
            set => SetValue(PlainTextProperty, value);
        }

        /// <summary>
        /// Whether to show diff highlighting. When false, displays plain text.
        /// </summary>
        public bool ShowDiffs
        {
            get => (bool)GetValue(ShowDiffsProperty);
            set => SetValue(ShowDiffsProperty, value);
        }

        #endregion

        #region Colors

        // Colors for different segment types
        private static readonly SolidColorBrush AddedBackground = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // LightGreen
        private static readonly SolidColorBrush RemovedBackground = new SolidColorBrush(Color.FromRgb(240, 128, 128)); // LightCoral
        private static readonly SolidColorBrush AddedForeground = new SolidColorBrush(Color.FromRgb(0, 100, 0)); // DarkGreen
        private static readonly SolidColorBrush RemovedForeground = new SolidColorBrush(Color.FromRgb(139, 0, 0)); // DarkRed

        static DiffHighlightTextBlock()
        {
            // Freeze brushes for performance
            AddedBackground.Freeze();
            RemovedBackground.Freeze();
            AddedForeground.Freeze();
            RemovedForeground.Freeze();
        }

        #endregion

        #region Property Changed Handlers

        private static void OnDiffSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DiffHighlightTextBlock)d).UpdateDisplay();
        }

        private static void OnIsDifferentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DiffHighlightTextBlock)d).UpdateDisplay();
        }

        private static void OnPlainTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DiffHighlightTextBlock)d).UpdateDisplay();
        }

        private static void OnShowDiffsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DiffHighlightTextBlock)d).UpdateDisplay();
        }

        #endregion

        #region Display Update

        /// <summary>
        /// Updates the display based on current properties.
        /// </summary>
        private void UpdateDisplay()
        {
            Inlines.Clear();

            // If diffs are disabled or no segments, show plain text
            if (!ShowDiffs || DiffSegments == null || DiffSegments.Count == 0)
            {
                Inlines.Add(new Run(PlainText ?? string.Empty));
                return;
            }

            // Build inlines from diff segments
            foreach (var segment in DiffSegments)
            {
                if (string.IsNullOrEmpty(segment.Text))
                    continue;

                var run = new Run(segment.Text);

                switch (segment.Type)
                {
                    case DiffType.Added:
                        run.Background = AddedBackground;
                        run.Foreground = AddedForeground;
                        run.FontWeight = FontWeights.SemiBold;
                        break;

                    case DiffType.Removed:
                        run.Background = RemovedBackground;
                        run.Foreground = RemovedForeground;
                        run.FontWeight = FontWeights.SemiBold;
                        run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                        break;

                    case DiffType.Unchanged:
                    default:
                        // Use default styling
                        break;
                }

                Inlines.Add(run);
            }

            // If no inlines were added, add placeholder
            if (Inlines.Count == 0)
            {
                Inlines.Add(new Run(PlainText ?? string.Empty));
            }
        }

        #endregion
    }
}
