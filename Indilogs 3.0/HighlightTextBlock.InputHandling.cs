using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.Controls
{
    public partial class HighlightTextBlock
    {
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
    }
}
