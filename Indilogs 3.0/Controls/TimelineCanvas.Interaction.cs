using System;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public partial class TimelineCanvas
    {
        #region Mouse Interaction

        private void OnMouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _isZooming = true;
                    _zoomStartPoint = e.GetPosition(SkiaCanvas);
                    SkiaCanvas.CaptureMouse();
                }
                else
                {
                    _isDragging = true;
                    _dragStart = e.GetPosition(SkiaCanvas);
                    _dragStartOffset = ViewOffset;
                    SkiaCanvas.CaptureMouse();
                    CheckObjectClick(_dragStart);
                }
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            _currentMousePos = e.GetPosition(SkiaCanvas);

            if (_isZooming)
            {
                SkiaCanvas.InvalidateVisual();
            }
            else if (_isDragging && _cachedStates != null && _cachedStates.Count > 0)
            {
                double dx = _currentMousePos.X - _dragStart.X;
                double pixelsPerSecond = (SkiaCanvas.ActualWidth * ViewScale) / _cachedTotalSeconds;
                if (pixelsPerSecond > 0)
                    ViewOffset = _dragStartOffset - (dx / pixelsPerSecond);
                SkiaCanvas.InvalidateVisual();
            }
            else
            {
                SkiaCanvas.InvalidateVisual();
            }
        }

        private void OnMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (_isZooming && _cachedStates != null && _cachedStates.Count > 0)
            {
                double x1 = Math.Min(_zoomStartPoint.X, _currentMousePos.X);
                double x2 = Math.Max(_zoomStartPoint.X, _currentMousePos.X);
                double width = x2 - x1;

                if (width > 10)
                {
                    double t1 = XToSeconds(x1, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
                    double t2 = XToSeconds(x2, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
                    double newRange = t2 - t1;
                    if (newRange > 0)
                    {
                        ViewScale = _cachedTotalSeconds / newRange;
                        ViewOffset = t1;
                    }
                }
                _isZooming = false;
                SkiaCanvas.InvalidateVisual();
            }
            _isDragging = false;
            SkiaCanvas.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (_cachedStates == null || _cachedStates.Count == 0) return;

            Point mousePos = e.GetPosition(SkiaCanvas);
            if (_cachedTotalSeconds <= 0) return;

            double mouseTimeBefore = XToSeconds(mousePos.X, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
            double zoomFactor = e.Delta > 0 ? 1.2 : 0.8;
            ViewScale *= zoomFactor;
            if (ViewScale < 0.1) ViewScale = 0.1;

            double mouseTimeAfter = (mousePos.X / (SkiaCanvas.ActualWidth * ViewScale)) * _cachedTotalSeconds;
            ViewOffset = mouseTimeBefore - mouseTimeAfter;

            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeave(object? sender, MouseEventArgs e)
        {
            _isDragging = false;
            _isZooming = false;
            HideTooltip();
            _hoverTimer!.Stop();
            SkiaCanvas.InvalidateVisual();
        }

        private void CheckObjectClick(Point p)
        {
            var hit = GetHitObject(p);
            if (hit is TimelineState s) StateClicked?.Invoke(this, s);
            else if (hit is TimelineMarker m) MarkerClicked?.Invoke(this, m);
        }

        private object? GetHitObject(Point p)
        {
            if (_cachedStates == null || _cachedStates.Count == 0) return null;

            double w = SkiaCanvas.ActualWidth;

            if (_cachedMarkers != null)
            {
                foreach (var m in _cachedMarkers)
                {
                    double mx = TimeToX((m.Time - _cachedMinTime).TotalSeconds, w, _cachedTotalSeconds);
                    double my = m.Type == TimelineMarkerType.Error ? TIMELINE_Y - MARKER_AREA + 6 : TIMELINE_Y + BAR_HEIGHT + 8;
                    if (Math.Abs(p.X - mx) < 10 && Math.Abs(p.Y - my) < 10) return m;
                }
            }

            if (p.Y >= TIMELINE_Y && p.Y <= TIMELINE_Y + BAR_HEIGHT)
            {
                double timeClicked = XToSeconds(p.X, w, _cachedTotalSeconds);
                for (int i = 0; i < _cachedStates.Count; i++)
                {
                    var s = _cachedStates[i];
                    double sStart = (s.StartTime - _cachedMinTime).TotalSeconds;
                    double sEnd = (s.EndTime - _cachedMinTime).TotalSeconds;
                    if (timeClicked >= sStart && timeClicked <= sEnd) return s;
                }
            }
            return null;
        }

        #endregion
    }
}
