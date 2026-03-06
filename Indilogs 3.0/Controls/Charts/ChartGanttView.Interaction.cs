using System;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGanttView
    {
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);

            if (HasOwnTimeline)
            {
                // Vertical drag for scrolling (no cursor for independent-timeline charts)
                _isDragging = true;
                _lastMousePos = pos;
                CaptureMouse();
                return;
            }

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
            CaptureMouse();

            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _isDragging = false;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(SkiaCanvas);
            _hoverPos = pos;

            // Handle drag/pan
            if (_isDragging && _totalDataLength > 0)
            {
                if (HasOwnTimeline)
                {
                    // Vertical scroll via drag
                    float deltaY = (float)(pos.Y - _lastMousePos.Y);
                    float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                    float canvasH = (float)SkiaCanvas.ActualHeight;
                    float visibleRows = canvasH - X_AXIS_HEIGHT;
                    float maxOffset = Math.Max(0, contentHeight - visibleRows);
                    _verticalOffset = Math.Max(0, Math.Min(_verticalOffset - deltaY, maxOffset));
                    _lastMousePos = pos;
                    SkiaCanvas.InvalidateVisual();
                }
                else
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
                }
            }
            else
            {
                // Pre-compute hover state (lightweight, no rendering)
                int oldRow = _hoveredStateRow;
                var oldInterval = _hoveredStateInterval;
                int oldLabelRow = _hoveredLabelRow;
                bool needsRepaint = false;

                float w2 = (float)SkiaCanvas.ActualWidth;
                float chartWidth2 = w2 - LEFT_MARGIN - RIGHT_MARGIN;

                // Check label hover (left margin area)
                int newLabelRow = -1;
                if (pos.X < LEFT_MARGIN && pos.X >= 0 && _stateDataList.Count > 0)
                {
                    int rowIdx = Math.Max(0, (int)((pos.Y + _verticalOffset - PADDING) / ROW_HEIGHT));
                    if (rowIdx >= 0 && rowIdx < _stateDataList.Count)
                        newLabelRow = rowIdx;
                }
                if (newLabelRow != oldLabelRow)
                {
                    _hoveredLabelRow = newLabelRow;
                    needsRepaint = true;
                }

                if (chartWidth2 > 0 && _totalDataLength > 0 && pos.X >= LEFT_MARGIN)
                {
                    int cnt = _viewEndIndex - _viewStartIndex + 1;
                    double ratio = (pos.X - LEFT_MARGIN) / chartWidth2;
                    int hoverIndex = _viewStartIndex + (int)(ratio * cnt);
                    _hoverDataIndex = Math.Max(0, Math.Min(hoverIndex, _totalDataLength - 1));
                    float rowH = ROW_HEIGHT;
                    int rowIdx = Math.Max(0, (int)((pos.Y + _verticalOffset - PADDING) / rowH));

                    int newRow = -1;
                    StateInterval? newInterval = null;

                    if (rowIdx >= 0 && rowIdx < _stateDataList.Count)
                    {
                        var intervals = _stateDataList[rowIdx].Intervals;
                        // Binary search for the interval containing hoverIndex
                        int lo = 0, hi = intervals.Count - 1;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            if (hoverIndex < intervals[mid].StartIndex)
                                hi = mid - 1;
                            else if (hoverIndex > intervals[mid].EndIndex)
                                lo = mid + 1;
                            else
                            {
                                newRow = rowIdx;
                                newInterval = intervals[mid];
                                break;
                            }
                        }
                    }

                    if (newRow != oldRow || !StateIntervalEquals(newInterval, oldInterval))
                    {
                        _hoveredStateRow = newRow;
                        _hoveredStateInterval = newInterval;
                        needsRepaint = true;
                    }
                    // Repaint when cursor moves along a hovered bar (dynamic time tooltip)
                    else if (newInterval.HasValue)
                    {
                        needsRepaint = true;
                    }
                }
                else if (oldRow >= 0 || oldInterval.HasValue)
                {
                    _hoveredStateRow = -1;
                    _hoveredStateInterval = null;
                    _hoverDataIndex = -1;
                    needsRepaint = true;
                }

                if (needsRepaint)
                    SkiaCanvas.InvalidateVisual();
            }
        }

        private static bool StateIntervalEquals(StateInterval? a, StateInterval? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            return a.Value.StartIndex == b.Value.StartIndex && a.Value.EndIndex == b.Value.EndIndex;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_totalDataLength == 0) return;

            if (HasOwnTimeline)
            {
                // Vertical scroll for independent-timeline charts
                float scrollAmount = e.Delta > 0 ? -40f : 40f;
                float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                float canvasH = (float)SkiaCanvas.ActualHeight;
                float visibleRows = canvasH - X_AXIS_HEIGHT;
                float maxOffset = Math.Max(0, contentHeight - visibleRows);
                _verticalOffset = Math.Max(0, Math.Min(_verticalOffset + scrollAmount, maxOffset));
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            // Check if click is in chart area (past label)
            if (pos.X < LEFT_MARGIN) return;

            // Calculate zoom center based on mouse position
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
