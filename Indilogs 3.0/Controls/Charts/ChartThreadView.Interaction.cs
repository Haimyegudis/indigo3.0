using System;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartThreadView
    {
        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_threadNames.Count == 0 && !_isDragging) return;

            var pos = e.GetPosition(SkiaCanvas);
            _eventHoverPos = pos;

            // Handle drag/pan
            if (_isDragging && _totalDataLength > 0)
            {
                float w = (float)SkiaCanvas.ActualWidth;
                float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
                if (chartWidth <= 0) return;

                double deltaX = pos.X - _lastMousePos.X;
                int visiblePoints = _viewEndIndex - _viewStartIndex;
                int shift = (int)((deltaX / chartWidth) * visiblePoints);

                if (shift != 0)
                {
                    int newStart = _viewStartIndex - shift;
                    int newEnd = _viewEndIndex - shift;

                    if (newStart < 0) { newStart = 0; newEnd = visiblePoints; }
                    if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - visiblePoints; }

                    if (newStart != _viewStartIndex)
                    {
                        _viewStartIndex = newStart;
                        _viewEndIndex = newEnd;
                        _lastMousePos = pos;
                        if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                        SkiaCanvas.InvalidateVisual();
                    }
                }
                return;
            }

            // Get DPI scale factor for proper coordinate mapping
            var source = PresentationSource.FromVisual(this);
            double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            // Scale mouse coordinates to match SkiaSharp's pixel coordinates
            float mouseX = (float)(pos.X * dpiScale);
            float mouseY = (float)(pos.Y * dpiScale);

            // Find message at mouse position
            int hoveredIndex = -1;
            const float hitRadius = 20f; // Increased hit radius for better detection

            for (int i = 0; i < _messageHitAreas.Count; i++)
            {
                var area = _messageHitAreas[i];
                // Check if mouse is within the label rectangle or close to it
                bool inRect = area.Rect.Contains(mouseX, mouseY);
                bool nearMarker = Math.Abs(mouseX - area.X) < hitRadius && mouseY >= area.Top && mouseY <= area.Bottom;

                if (inRect || nearMarker)
                {
                    hoveredIndex = i;
                    break;
                }
            }

            if (hoveredIndex != _hoveredMessageIndex)
            {
                _hoveredMessageIndex = hoveredIndex;

                if (hoveredIndex >= 0 && hoveredIndex < _messageHitAreas.Count)
                {
                    var msg = _messageHitAreas[hoveredIndex].Message;
                    ShowTooltip(msg);
                }
                else
                {
                    HideTooltip();
                }
            }

            // Only repaint for events if hovered event index changed
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                int oldEvtIdx = _hoveredEventDotIndex;
                // Quick scan: find which event the mouse is near
                int newEvtIdx = -1;
                float w2 = (float)SkiaCanvas.ActualWidth;
                float cw = w2 - LEFT_MARGIN - RIGHT_MARGIN;
                if (cw > 0 && _totalDataLength > 0)
                {
                    int cnt = _viewEndIndex - _viewStartIndex + 1;
                    float h2 = (float)SkiaCanvas.ActualHeight;
                    float evtY = h2 - 10;
                    foreach (var evt in _chartEventMarkers)
                    {
                        if (evt.Index < _viewStartIndex || evt.Index > _viewEndIndex) continue;
                        float ex = LEFT_MARGIN + (float)((evt.Index - _viewStartIndex) / (double)cnt * cw);
                        float dx = mouseX - ex;
                        float dy = mouseY - evtY;
                        if (Math.Sqrt(dx * dx + dy * dy) < 20) { newEvtIdx = evt.Index; break; }
                    }
                }
                if (newEvtIdx != oldEvtIdx)
                    SkiaCanvas.InvalidateVisual();
            }
        }

        private void OnMouseLeave(object? sender, MouseEventArgs e)
        {
            _hoveredMessageIndex = -1;
            HideTooltip();
        }

        private void ShowTooltip(Services.Charts.ThreadMessageData msg)
        {
            TooltipThread.Text = msg.ThreadName;
            TooltipTime.Text = msg.TimeStamp.ToString("HH:mm:ss.ffffff");
            TooltipMessage.Text = msg.Message;
            MessageTooltip.IsOpen = true;
        }

        private void HideTooltip()
        {
            MessageTooltip.IsOpen = false;
        }

        private void OnMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            // Check if click is in chart area (past label)
            if (pos.X < LEFT_MARGIN) return;

            float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
            double ratio = (pos.X - LEFT_MARGIN) / chartWidth;
            int count = _viewEndIndex - _viewStartIndex + 1;
            int clickedIndex = _viewStartIndex + (int)(ratio * count);
            clickedIndex = Math.Max(0, Math.Min(clickedIndex, _totalDataLength - 1));

            _cursorIndex = clickedIndex;
            OnTimeClicked?.Invoke(clickedIndex);
            OnCursorMoved?.Invoke(clickedIndex);

            // Start dragging for pan
            _isDragging = true;
            _lastMousePos = pos;
            SkiaCanvas.CaptureMouse();

            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            SkiaCanvas.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            if (pos.X < LEFT_MARGIN) return;

            // Calculate zoom center
            float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
            double ratio = (pos.X - LEFT_MARGIN) / chartWidth;
            int count = _viewEndIndex - _viewStartIndex + 1;
            int centerIndex = _viewStartIndex + (int)(ratio * count);

            // Zoom factor
            double zoomFactor = e.Delta > 0 ? 0.8 : 1.25;
            int newCount = (int)(count * zoomFactor);
            newCount = Math.Max(10, Math.Min(newCount, _totalDataLength));

            // Calculate new range centered on mouse position
            int newStart = centerIndex - (int)(ratio * newCount);
            int newEnd = newStart + newCount - 1;

            // Clamp to valid range
            if (newStart < 0) { newStart = 0; newEnd = newCount - 1; }
            if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - newCount + 1; }
            if (newStart < 0) newStart = 0;

            _viewStartIndex = newStart;
            _viewEndIndex = newEnd;

            if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
            SkiaCanvas.InvalidateVisual();

            e.Handled = true;
        }
    }
}
