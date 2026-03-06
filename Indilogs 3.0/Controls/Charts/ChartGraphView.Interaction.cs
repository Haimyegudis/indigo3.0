#pragma warning disable CS0618 // SKPaint text APIs are obsolete in favor of SKFont — suppress until SkiaSharp migration
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView
    {
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus(); // Enable keyboard events
            OnChartClicked?.Invoke();
        }

        private void UserControl_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            // Handle zoom with mouse wheel - this is PreviewMouseWheel so it fires before ScrollViewer
            if (_totalDataLength == 0) return;
            int totalPoints = _viewEndIndex - _viewStartIndex;
            if (totalPoints < 10) return;

            // Symmetric zoom factor - same amount of zoom in/out
            // Use 1.25 for zoom out, 1/1.25 = 0.8 for zoom in
            const double ZOOM_RATIO = 1.25;
            double zoomFactor = e.Delta > 0 ? (1.0 / ZOOM_RATIO) : ZOOM_RATIO;

            double chartWidth = ActualWidth - LEFT_MARGIN - RIGHT_MARGIN;
            double mouseX = e.GetPosition(this).X - LEFT_MARGIN;
            double mouseRatio = Math.Max(0, Math.Min(mouseX / chartWidth, 1));

            int mouseIndex = _viewStartIndex + (int)(totalPoints * mouseRatio);
            int newSpan = Math.Max(10, (int)Math.Round(totalPoints * zoomFactor));

            int newStart = mouseIndex - (int)Math.Round(newSpan * mouseRatio);
            int newEnd = newStart + newSpan;

            if (newStart < 0) { newStart = 0; newEnd = Math.Min(newSpan, _totalDataLength - 1); }
            if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = Math.Max(0, newEnd - newSpan); }

            if (newEnd > newStart && newEnd - newStart >= 10 && (newStart != _viewStartIndex || newEnd != _viewEndIndex))
            {
                _viewStartIndex = newStart;
                _viewEndIndex = newEnd;
                SkiaCanvas.InvalidateVisual();
                if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
            }

            e.Handled = true; // Mark as handled so ScrollViewer doesn't scroll
        }

        // Convert WPF coordinates to Skia coordinates (account for DPI)
        private Point WpfToSkia(Point wpfPoint)
        {
            return new Point(wpfPoint.X * _dpiScaleX, wpfPoint.Y * _dpiScaleY);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var wpfPos = e.GetPosition(this);
            var pos = WpfToSkia(wpfPos);

            // Ctrl+Click for 2-point measurement
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (!_isCtrlMeasuring)
                {
                    _ctrlPoint1 = PixelToIndex(pos.X);
                    _ctrlPoint1Pos = pos;
                    _ctrlPoint2 = -1;
                    _isCtrlMeasuring = true;
                }
                else
                {
                    _ctrlPoint2 = PixelToIndex(pos.X);
                    _ctrlPoint2Pos = pos;
                    _isCtrlMeasuring = false;
                }
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Shift+Click/Drag for area measurement
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                _isMeasuring = true;
                _measureStartIndex = PixelToIndex(pos.X);
                _measureCurrentIndex = _measureStartIndex;
                CaptureMouse();
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Regular click - trigger time sync
            int clickedIndex = PixelToIndex(pos.X);
            OnTimeClicked?.Invoke(clickedIndex);

            _isDragging = true;
            _lastMousePos = pos;
            CaptureMouse();
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _isDragging = false;

            if (_isMeasuring && Math.Abs(_measureStartIndex - _measureCurrentIndex) < 5)
            {
                _measureStartIndex = -1;
                _measureCurrentIndex = -1;
            }
            _isMeasuring = false;

            ReleaseMouseCapture();
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_totalDataLength == 0) return;

            // During playback mode, don't move cursor with mouse at all
            // Only allow measurement if actively measuring
            if (_isProgressiveMode)
            {
                if (_isMeasuring)
                {
                    var wpfPos = e.GetPosition(this);
                    var currentPos = WpfToSkia(wpfPos);
                    int cursorIdx = PixelToIndex(currentPos.X);
                    _measureCurrentIndex = cursorIdx;
                    SkiaCanvas.InvalidateVisual();
                }
                return;
            }

            var pos = e.GetPosition(this);
            var scaledPos = WpfToSkia(pos);

            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartRight = (ActualWidth - RIGHT_MARGIN) * _dpiScaleX;

            _showHoverTooltip = Keyboard.Modifiers == ModifierKeys.Alt &&
                               scaledPos.X >= chartLeft &&
                               scaledPos.X <= chartRight;
            _hoverPos = scaledPos;

            int cursorIndex = PixelToIndex(scaledPos.X);

            // Detect state hover for CHStep tooltip (binary search for performance)
            _hoveredState = null;
            if (_showStates && _states != null && _states.Count > 0)
            {
                int lo = 0, hi = _states.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (cursorIndex < _states[mid].StartIndex)
                        hi = mid - 1;
                    else if (cursorIndex > _states[mid].EndIndex)
                        lo = mid + 1;
                    else
                    {
                        _hoveredState = _states[mid];
                        break;
                    }
                }
            }

            // Handle measurement
            if (_isMeasuring)
            {
                _measureCurrentIndex = cursorIndex;
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Update cursor position (only when not in playback mode)
            if (cursorIndex != _globalCursorIndex)
            {
                _globalCursorIndex = cursorIndex;
                OnCursorMoved?.Invoke(cursorIndex);
                UpdateLegendValues(cursorIndex);
                SkiaCanvas.InvalidateVisual();
            }

            if (_isDragging)
            {
                double deltaX = scaledPos.X - _lastMousePos.X;
                double chartWidth = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
                int visiblePoints = _viewEndIndex - _viewStartIndex;
                int shift = (int)((deltaX / chartWidth) * visiblePoints);
                int newStart = _viewStartIndex - shift;
                int newEnd = _viewEndIndex - shift;

                if (newStart < 0) { newStart = 0; newEnd = visiblePoints; }
                if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - visiblePoints; }

                if (newStart != _viewStartIndex)
                {
                    _viewStartIndex = newStart;
                    _viewEndIndex = newEnd;
                    _lastMousePos = scaledPos;
                    SkiaCanvas.InvalidateVisual();
                    if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                }
            }
            else if (_showHoverTooltip || _hoveredState.HasValue)
            {
                SkiaCanvas.InvalidateVisual();
            }
            else if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                // Only repaint if hovered event index changed (not every mouse move)
                int oldHovered = _hoveredEventIndex;
                int newHovered = FindHoveredEventIndex(cursorIndex);
                if (newHovered != oldHovered)
                {
                    SkiaCanvas.InvalidateVisual();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                ClearAllMeasurements();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredState.HasValue)
            {
                _hoveredState = null;
                _showHoverTooltip = false;
                SkiaCanvas.InvalidateVisual();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            ClearAllMeasurements();
        }

        private void ClearAllMeasurements()
        {
            _ctrlPoint1 = -1;
            _ctrlPoint2 = -1;
            _isCtrlMeasuring = false;
            _measureStartIndex = -1;
            _measureCurrentIndex = -1;
            _isMeasuring = false;
            SkiaCanvas.InvalidateVisual();
        }

        private int FindHoveredEventIndex(int cursorIndex)
        {
            if (_chartEventMarkers == null || _chartEventMarkers.Count == 0) return -1;
            int start = _viewStartIndex;
            int end = _viewEndIndex;
            int count = end - start;
            if (count <= 0) return -1;
            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartW = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
            double chartBottom = (ActualHeight - BOTTOM_MARGIN) * _dpiScaleY;
            float eventY = (float)chartBottom - 8;

            foreach (var evt in _chartEventMarkers)
            {
                if (evt.Index < start || evt.Index > end) continue;
                float ex = (float)(chartLeft + (evt.Index - start) / (double)count * chartW);
                float dx = (float)_hoverPos.X - ex;
                float dy = (float)_hoverPos.Y - eventY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist < EVENT_DOT_RADIUS * 4) return evt.Index;
            }
            return -1;
        }

        private int PixelToIndex(double x)
        {
            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartWidth = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
            if (chartWidth <= 0 || _totalDataLength == 0) return 0;
            double relX = x - chartLeft;
            int count = _viewEndIndex - _viewStartIndex;
            int offset = (int)((relX / chartWidth) * count);
            return Math.Max(0, Math.Min(_viewStartIndex + offset, _totalDataLength - 1));
        }

        public double GetCurrentCursorValue()
        {
            if (_globalCursorIndex < 0 || _seriesList.Count == 0) return 0;
            var firstVisible = _seriesList.FirstOrDefault(s => s.IsVisible && s.Data != null);
            if (firstVisible == null || _globalCursorIndex >= firstVisible.Data.Length) return 0;
            return firstVisible.Data[_globalCursorIndex];
        }

        public int GetCurrentCursorIndex() => _globalCursorIndex;
    }
}
