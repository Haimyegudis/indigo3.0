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
    public class HighlightTextBlock : RichTextBox
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

        #region Keyboard Handling

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Allow Ctrl+C for copy
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            // Allow Ctrl+A for select all
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            // Space key: directly execute the DataGrid's MarkRowCommand
            if (e.Key == Key.Space)
            {
                var dataGrid = FindParent<DataGrid>(this);
                if (dataGrid != null)
                {
                    foreach (InputBinding binding in dataGrid.InputBindings)
                    {
                        if (binding is KeyBinding kb && kb.Key == Key.Space && kb.Modifiers == ModifierKeys.None)
                        {
                            if (kb.Command != null && kb.Command.CanExecute(kb.CommandParameter))
                            {
                                kb.Command.Execute(kb.CommandParameter);
                            }
                            break;
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            // Navigation keys: raise KeyDown (bubbling) on the DataGrid
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.PageUp || e.Key == Key.PageDown
                || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Enter || e.Key == Key.Escape
                || e.Key == Key.Tab)
            {
                var dataGrid = FindParent<DataGrid>(this);
                if (dataGrid != null)
                {
                    var args = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };
                    dataGrid.RaiseEvent(args);
                }
                e.Handled = true;
                return;
            }

            // Block all other keys
            e.Handled = true;
        }

        #endregion

        #region Mouse Click-vs-Drag Handling

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            // Double-click handling
            if (e.ClickCount == 2)
            {
                _mouseIsDown = false;
                var clickPos = e.GetPosition(this);

                // Check if click is within 50px margin of actual text
                bool isOnText = false;
                var startPointer = Document.ContentStart;
                var endPointer = Document.ContentEnd;
                var startRect = startPointer.GetCharacterRect(LogicalDirection.Forward);
                var endRect = endPointer.GetCharacterRect(LogicalDirection.Backward);
                // Allow 35px margin before first char and after last char
                if (endRect.Right > 0 &&
                    clickPos.X >= startRect.Left - 35 && clickPos.X <= endRect.Right + 35)
                {
                    isOnText = true;
                }

                // If click is on or near text, let RichTextBox select the word
                if (isOnText)
                {
                    Focus();
                    base.OnPreviewMouseLeftButtonDown(e);
                    return;
                }

                // If click is on empty area, execute ViewLogDetailsCommand
                var dg = FindParent<DataGrid>(this);
                if (dg != null)
                {
                    foreach (InputBinding binding in dg.InputBindings)
                    {
                        if (binding is MouseBinding mb && mb.MouseAction == MouseAction.LeftDoubleClick)
                        {
                            if (mb.Command != null && mb.Command.CanExecute(mb.CommandParameter))
                            {
                                mb.Command.Execute(mb.CommandParameter);
                            }
                            break;
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            // Record position for drag detection
            _mouseDownPosition = e.GetPosition(this);
            _mouseIsDown = true;
            _isDragging = false;

            // Select the DataGrid row immediately (click behavior)
            var dataGridRow = FindParent<DataGridRow>(this);
            var dataGrid = FindParent<DataGrid>(this);
            if (dataGrid != null && dataGridRow != null)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    dataGridRow.IsSelected = !dataGridRow.IsSelected;
                }
                else if (Keyboard.Modifiers != ModifierKeys.Shift)
                {
                    dataGrid.SelectedItem = dataGridRow.DataContext;
                }

                // Ensure DataGrid has focus for subsequent keyboard events
                dataGrid.Focus();
            }

            // Clear any existing text selection
            Selection.Select(Document.ContentStart, Document.ContentStart);

            // Suppress base RichTextBox mouse handling (prevents immediate text selection)
            e.Handled = true;

            // Capture mouse so we get move/up events
            CaptureMouse();
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (_mouseIsDown && !_isDragging)
            {
                var currentPos = e.GetPosition(this);
                double dx = Math.Abs(currentPos.X - _mouseDownPosition.X);
                double dy = Math.Abs(currentPos.Y - _mouseDownPosition.Y);

                if (dx > SystemParameters.MinimumHorizontalDragDistance ||
                    dy > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Drag threshold exceeded: switch to text selection mode
                    _isDragging = true;

                    // Release our capture so RichTextBox can work normally
                    ReleaseMouseCapture();

                    // Get text position at the original mouse-down point
                    TextPointer start = GetPositionFromPoint(_mouseDownPosition, true);
                    if (start != null)
                    {
                        // Focus the RichTextBox for text selection
                        Focus();
                        Selection.Select(start, start);

                        // Re-invoke base mouse-down to start native text selection tracking
                        base.OnPreviewMouseLeftButtonDown(new MouseButtonEventArgs(
                            e.MouseDevice, e.Timestamp, MouseButton.Left)
                        {
                            RoutedEvent = PreviewMouseLeftButtonDownEvent
                        });
                    }
                }
            }

            if (_isDragging)
            {
                base.OnPreviewMouseMove(e);
            }
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_mouseIsDown)
            {
                _mouseIsDown = false;

                if (!_isDragging)
                {
                    // Simple click - release capture, no text selection
                    if (IsMouseCaptured)
                        ReleaseMouseCapture();
                    e.Handled = true;
                }
                else
                {
                    // End of drag-select: let RichTextBox finalize selection
                    _isDragging = false;
                    base.OnPreviewMouseLeftButtonUp(e);
                }
            }
        }

        #endregion

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

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found)
                    return found;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        #endregion
    }
}
