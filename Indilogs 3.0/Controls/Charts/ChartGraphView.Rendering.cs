using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView
    {
        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_totalDataLength == 0) return;

            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            int start = Math.Max(0, _viewStartIndex);
            int end = Math.Min(_totalDataLength - 1, _viewEndIndex);
            int count = end - start + 1;
            if (count <= 1 || chartW <= 0) return;

            // Background States
            if (_showStates && _states != null)
            {
                foreach (var st in _states)
                {
                    if (st.EndIndex < start || st.StartIndex > end) continue;
                    float x1 = (float)((Math.Max(st.StartIndex, start) - start) / (double)count * chartW);
                    float x2 = (float)((Math.Min(st.EndIndex, end) - start) / (double)count * chartW);
                    _stateFillPaint.Color = ChartStateConfig.GetColor(st.StateId);
                    canvas.DrawRect(new SKRect(chartLeft + x1, chartTop, chartLeft + x2, chartBottom), _stateFillPaint);

                    string nm = ChartStateConfig.GetName(st.StateId);
                    float tw = _stateTextFont.MeasureText(nm);
                    if (tw < (x2 - x1) - 4)
                    {
                        canvas.DrawText(nm, (float)Math.Round(chartLeft + x1 + (x2 - x1) / 2 - tw / 2), (float)Math.Round(chartTop + 14), _stateTextFont, _stateTextPaint);
                    }
                }
            }

            // Time Gap Regions (semi-transparent red overlay) — uses cached paints
            if (_timeGaps != null && _timeGaps.Count > 0)
            {
                foreach (var gap in _timeGaps)
                {
                    if (gap.EndIndex < start || gap.StartIndex > end) continue;

                    float gx1 = chartLeft + (float)((Math.Max(gap.StartIndex, start) - start) / (double)count * chartW);
                    float gx2 = chartLeft + (float)((Math.Min(gap.EndIndex, end) - start) / (double)count * chartW);

                    // Ensure minimum visible width
                    if (gx2 - gx1 < 3) gx2 = gx1 + 3;

                    var gapRect = new SKRect(gx1, chartTop, gx2, chartBottom);
                    canvas.DrawRect(gapRect, _gapFillPaint);
                    canvas.DrawRect(gapRect, _gapBorderPaint);

                    // Draw gap label at top
                    if (!string.IsNullOrEmpty(gap.Duration))
                    {
                        string label = $"GAP {gap.Duration}";
                        float tw = _gapTextFont.MeasureText(label);
                        float labelX = gx1 + (gx2 - gx1) / 2 - tw / 2;
                        if (tw < (gx2 - gx1) - 4)
                        {
                            canvas.DrawText(label, labelX, chartTop + 12, _gapTextFont, _gapTextPaint);
                        }
                        else
                        {
                            canvas.DrawText("GAP", gx1 + 2, chartTop + 12, _gapTextFont, _gapTextPaint);
                        }
                    }
                }
            }

            // Dual Scale Logic
            double lMin = double.MaxValue, lMax = double.MinValue;
            double rMin = double.MaxValue, rMax = double.MinValue;
            bool hasLeft = false, hasRight = false;
            int step = Math.Max(1, count / 1000);

            foreach (var s in _seriesList)
            {
                if (s.Data == null || !s.IsVisible) continue;
                var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;

                if (s.YAxisType == AxisType.Right)
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                        {
                            if (dataToDraw[i] < rMin) rMin = dataToDraw[i];
                            if (dataToDraw[i] > rMax) rMax = dataToDraw[i];
                            hasRight = true;
                        }
                    }
                }
                else
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                        {
                            if (dataToDraw[i] < lMin) lMin = dataToDraw[i];
                            if (dataToDraw[i] > lMax) lMax = dataToDraw[i];
                            hasLeft = true;
                        }
                    }
                }
            }

            if (!hasLeft) { lMin = 0; lMax = 10; }
            if (!hasRight) { rMin = 0; rMax = 10; }
            if (Math.Abs(lMax - lMin) < 0.0001) { lMax += 1; lMin -= 1; }
            if (Math.Abs(rMax - rMin) < 0.0001) { rMax += 1; rMin -= 1; }

            double lPadding = (lMax - lMin) * 0.1;
            double lDisplayMin = lMin - lPadding;
            double lRange = (lMax - lMin) + (2 * lPadding);

            double rPadding = (rMax - rMin) * 0.1;
            double rDisplayMin = rMin - rPadding;
            double rRange = (rMax - rMin) + (2 * rPadding);

            // Grid Y
            int ySteps = 4;
            for (int i = 0; i <= ySteps; i++)
            {
                double ratio = i / (double)ySteps;
                float yPos = SnapToPixel(chartBottom - (float)(ratio * chartH));
                canvas.DrawLine(chartLeft, yPos, chartRight, yPos, _gridLinePaint);

                if (hasLeft || !hasRight)
                {
                    string lbl = (lDisplayMin + (ratio * lRange)).ToString("0.##");
                    float lblW = _textFontLeft.MeasureText(lbl);
                    canvas.DrawText(lbl, chartLeft - lblW - 6, yPos + 4, _textFontLeft, _textPaintLeft);
                }
                if (hasRight)
                {
                    string lbl = (rDisplayMin + (ratio * rRange)).ToString("0.##");
                    canvas.DrawText(lbl, chartRight + 6, yPos + 4, _textFontRight, _textPaintRight);
                }
            }

            // Grid X
            float stepPixels = 120;
            int xSteps = (int)(chartW / stepPixels);
            float lastTextRight = -1000;

            for (int i = 0; i <= xSteps; i++)
            {
                float xPos = SnapToPixel(chartLeft + (i * stepPixels));
                if (xPos > chartRight) break;

                double ratio = (xPos - chartLeft) / chartW;
                int idx = start + (int)(count * ratio);
                canvas.DrawLine(xPos, chartTop, xPos, chartBottom, _gridLinePaint);

                if (GetXAxisLabel != null)
                {
                    string t = GetXAxisLabel(idx);
                    if (!string.IsNullOrEmpty(t))
                    {
                        float txtW = _textFontLeft.MeasureText(t);
                        float tl = (float)Math.Round(xPos - txtW / 2);
                        if (tl > lastTextRight + 20)
                        {
                            canvas.DrawText(t, tl, chartBottom + 16, _textFontLeft, _textPaintLeft);
                            lastTextRight = tl + txtW;
                        }
                    }
                }
            }

            // Reference Lines — uses cached paints (_refLinePaint, _refLineTextPaint)
            if (_referenceLines != null)
            {
                foreach (var line in _referenceLines)
                {
                    _refLinePaint.Color = line.Color;
                    _refLinePaint.StrokeWidth = line.Thickness;
                    _refLinePaint.PathEffect = line.IsDashed ? _refDashEffect : null;

                    if (line.Type == ReferenceLineType.Vertical)
                    {
                        int idx = (int)line.Value;
                        if (idx >= start && idx <= end)
                        {
                            float x = chartLeft + (float)((idx - start) / (double)count * chartW);
                            canvas.DrawLine(x, chartTop, x, chartBottom, _refLinePaint);

                            if (!string.IsNullOrEmpty(line.Name))
                            {
                                _refLineTextPaint.Color = line.Color;
                                canvas.DrawText(line.Name, x + 4, chartTop + 12, _refLineTextFont, _refLineTextPaint);
                            }
                        }
                    }
                    else
                    {
                        double range = (line.YAxis == AxisType.Left) ? lRange : rRange;
                        double dMin = (line.YAxis == AxisType.Left) ? lDisplayMin : rDisplayMin;

                        if (line.Value >= dMin && line.Value <= (dMin + range))
                        {
                            float y = chartBottom - (float)((line.Value - dMin) / range * chartH);
                            canvas.DrawLine(chartLeft, y, chartRight, y, _refLinePaint);

                            if (!string.IsNullOrEmpty(line.Name))
                            {
                                _refLineTextPaint.Color = line.Color;
                                canvas.DrawText(line.Name, chartLeft + 4, y - 4, _refLineTextFont, _refLineTextPaint);
                            }
                        }
                    }
                }
            }

            // Thread Message Markers (vertical dashed lines with triangles at top)
            if (_threadMessages != null && _threadMessages.Count > 0)
            {
                _threadLinePaint.PathEffect = _threadDashEffect;

                foreach (var msg in _threadMessages)
                {
                    if (msg.TimeIndex < start || msg.TimeIndex > end) continue;

                    // Use pre-computed color map (built in SetThreadMessages)
                    if (!_threadColorMap.TryGetValue(msg.ThreadName, out SKColor markerColor))
                        markerColor = ThreadMarkerColors[0];

                    float x = chartLeft + (float)((msg.TimeIndex - start) / (double)count * chartW);

                    // Draw dashed vertical line (reuse cached paint)
                    _threadLinePaint.Color = markerColor;
                    canvas.DrawLine(x, chartTop, x, chartBottom, _threadLinePaint);

                    // Draw triangle marker at top (reuse cached paint)
                    _threadTrianglePaint.Color = markerColor;
                    using (var trianglePath = new SKPath())
                    {
                        trianglePath.MoveTo(x, chartTop);
                        trianglePath.LineTo(x - 5, chartTop - 8);
                        trianglePath.LineTo(x + 5, chartTop - 8);
                        trianglePath.Close();
                        canvas.DrawPath(trianglePath, _threadTrianglePaint);
                    }
                }
            }

            // Signal Lines — uses cached _signalLinePaint
            using (var path = new SKPath())
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

                int drawLimit = end;
                if (_isProgressiveMode && _globalCursorIndex != -1)
                    drawLimit = Math.Min(end, _globalCursorIndex);

                foreach (var s in _seriesList)
                {
                    if (!s.IsVisible || s.Data == null) continue;
                    var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                    _signalLinePaint.Color = s.Color;
                    path.Reset();
                    bool first = true;

                    double currentMin = (s.YAxisType == AxisType.Right) ? rDisplayMin : lDisplayMin;
                    double currentRange = (s.YAxisType == AxisType.Right) ? rRange : lRange;
                    int drawStep = Math.Max(1, count / (int)chartW);

                    if (drawStep > 2)
                    {
                        // Min/Max decimation: for each pixel bucket, find min and max to preserve spikes
                        for (int bucket = start; bucket <= drawLimit; bucket += drawStep)
                        {
                            double minVal = double.MaxValue, maxVal = double.MinValue;
                            int minIdx = bucket, maxIdx = bucket;
                            int bucketEnd = Math.Min(bucket + drawStep, drawLimit + 1);
                            for (int j = bucket; j < bucketEnd && j < dataToDraw.Length; j++)
                            {
                                double v = dataToDraw[j];
                                if (double.IsNaN(v)) continue;
                                if (v < minVal) { minVal = v; minIdx = j; }
                                if (v > maxVal) { maxVal = v; maxIdx = j; }
                            }
                            if (minVal == double.MaxValue) { first = true; continue; }

                            float x = chartLeft + (float)((bucket - start) / (double)count * chartW);
                            float yMin = chartBottom - (float)((minVal - currentMin) / currentRange * chartH);
                            float yMax = chartBottom - (float)((maxVal - currentMin) / currentRange * chartH);

                            // Draw min first, then max (or vice versa) to preserve waveform shape
                            if (minIdx <= maxIdx)
                            {
                                if (first) { path.MoveTo(x, yMin); first = false; } else path.LineTo(x, yMin);
                                if (yMin != yMax) path.LineTo(x, yMax);
                            }
                            else
                            {
                                if (first) { path.MoveTo(x, yMax); first = false; } else path.LineTo(x, yMax);
                                if (yMin != yMax) path.LineTo(x, yMin);
                            }
                        }
                    }
                    else
                    {
                        // Zoomed in: draw every point
                        for (int i = start; i <= drawLimit; i += drawStep)
                        {
                            if (i >= dataToDraw.Length) break;
                            double val = dataToDraw[i];
                            if (double.IsNaN(val)) { first = true; continue; }

                            float x = chartLeft + (float)((i - start) / (double)count * chartW);
                            float y = chartBottom - (float)((val - currentMin) / currentRange * chartH);

                            if (first) { path.MoveTo(x, y); first = false; }
                            else path.LineTo(x, y);
                        }
                    }

                    if (!first) canvas.DrawPath(path, _signalLinePaint);
                }
                canvas.Restore();
            }

            // Event Markers (Red Dots on X-axis timeline)
            _hoveredEventIndex = -1;
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                float eventY = chartBottom - 8; // Position dots near the bottom of the chart area

                foreach (var evt in _chartEventMarkers)
                {
                    if (evt.Index < start || evt.Index > end) continue;

                    float ex = chartLeft + (float)((evt.Index - start) / (double)count * chartW);

                    // Draw red dot
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint);
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint);

                    // Check if mouse is hovering near this event dot
                    {
                        float dx = (float)_hoverPos.X - ex;
                        float dy = (float)_hoverPos.Y - eventY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (dist < EVENT_DOT_RADIUS * 4)
                        {
                            _hoveredEventIndex = evt.Index;
                        }
                    }
                }

                // Draw tooltip for hovered event
                if (_hoveredEventIndex >= 0)
                {
                    var hoveredEvent = _chartEventMarkers.FirstOrDefault(e => e.Index == _hoveredEventIndex);
                    if (hoveredEvent != null)
                    {
                        float hx = chartLeft + (float)((hoveredEvent.Index - start) / (double)count * chartW);

                        // Draw a larger highlight circle — uses cached _eventHighlightPaint
                        canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, _eventHighlightPaint);

                        // Build tooltip text
                        var sb = new StringBuilder();
                        sb.AppendLine("=== EVENT ===");
                        if (!string.IsNullOrEmpty(hoveredEvent.Time))
                            sb.AppendLine($"Time: {hoveredEvent.Time}");
                        if (!string.IsNullOrEmpty(hoveredEvent.Name))
                            sb.AppendLine($"Name: {hoveredEvent.Name}");
                        if (!string.IsNullOrEmpty(hoveredEvent.Message))
                            sb.AppendLine($"Message: {hoveredEvent.Message}");
                        if (!string.IsNullOrEmpty(hoveredEvent.Severity))
                            sb.AppendLine($"Severity: {hoveredEvent.Severity}");
                        if (!string.IsNullOrEmpty(hoveredEvent.Description))
                            sb.AppendLine($"Source: {hoveredEvent.Description}");

                        float tooltipX = hx + 15;
                        float tooltipY = eventY - 40;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
            }

            // Border
            float L = SnapToPixel(chartLeft), R = SnapToPixel(chartRight), B = SnapToPixel(chartBottom), T = SnapToPixel(chartTop);
            canvas.DrawLine(L, T, L, B, _axisLinePaint);
            if (hasRight) canvas.DrawLine(R, T, R, B, _axisLinePaint);
            canvas.DrawLine(L, B, R, B, _axisLinePaint);

            // Target Line (Blue)
            if (_targetLineIndex >= start && _targetLineIndex <= end)
            {
                float tx = SnapToPixel(chartLeft + (float)((_targetLineIndex - start) / (double)count * chartW));
                canvas.DrawLine(tx, T, tx, B, _targetLinePaint);
            }

            // Cursor Line (Red)
            if (_globalCursorIndex >= start && _globalCursorIndex <= end)
            {
                float cx = SnapToPixel(chartLeft + (float)((_globalCursorIndex - start) / (double)count * chartW));
                canvas.DrawLine(cx, T, cx, B, _cursorLinePaint);
            }

            // Measure Box (Shift+Drag)
            if (_measureStartIndex != -1 && _measureCurrentIndex != -1)
            {
                int mS = Math.Max(Math.Min(_measureStartIndex, _measureCurrentIndex), start);
                int mE = Math.Min(Math.Max(_measureStartIndex, _measureCurrentIndex), end);

                if (mE > mS)
                {
                    float x1 = chartLeft + (float)((mS - start) / (double)count * chartW);
                    float x2 = chartLeft + (float)((mE - start) / (double)count * chartW);
                    var rect = new SKRect(x1, chartTop, x2, chartBottom);
                    canvas.DrawRect(rect, _measureFillPaint);
                    canvas.DrawRect(rect, _measureBorderPaint);

                    if (!_isMeasuring)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== AREA MEASUREMENT ===");
                        sb.AppendLine($"Index Range: {mS} -> {mE}");
                        sb.AppendLine($"Points: {mE - mS + 1}");

                        if (GetXAxisLabel != null)
                        {
                            string t1 = GetXAxisLabel(mS);
                            string t2 = GetXAxisLabel(mE);
                            if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                            {
                                sb.AppendLine($"Time: {t1} -> {t2}");
                            }
                        }

                        sb.AppendLine("-------------------");

                        foreach (var s in _seriesList)
                        {
                            if (!s.IsVisible || s.Data == null) continue;
                            var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                            double sum = 0, mn = double.MaxValue, mx = double.MinValue;
                            int c = 0;

                            for (int i = mS; i <= mE; i++)
                            {
                                if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                                {
                                    double v = dataToDraw[i];
                                    sum += v;
                                    if (v < mn) mn = v;
                                    if (v > mx) mx = v;
                                    c++;
                                }
                            }

                            if (c > 0)
                            {
                                double avg = sum / c;
                                sb.AppendLine($"{s.Name}:");
                                sb.AppendLine($"  Avg: {avg:F3}");
                                sb.AppendLine($"  Min: {mn:F3}");
                                sb.AppendLine($"  Max: {mx:F3}");
                                sb.AppendLine($"  Delta: {(mx - mn):F3}");
                            }
                        }

                        float tooltipX = (_measureCurrentIndex > _measureStartIndex) ? x2 + 15 : x1 - 170;
                        float tooltipY = chartTop + 10;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
            }

            // Ctrl+Click 2-Point Measurement
            if (_ctrlPoint1 != -1 && _ctrlPoint1 >= start && _ctrlPoint1 <= end)
            {
                float x1 = chartLeft + (float)((_ctrlPoint1 - start) / (double)count * chartW);
                float y1 = (float)_ctrlPoint1Pos.Y;

                // Clamp y1 to chart area
                y1 = Math.Max(chartTop, Math.Min(chartBottom, y1));

                canvas.DrawLine(x1, chartTop, x1, chartBottom, _ctrlMeasurePaint);
                canvas.DrawLine(chartLeft, y1, chartRight, y1, _ctrlMeasurePaint);
                canvas.DrawCircle(x1, y1, 5, _ctrlMeasurePaint);

                if (_ctrlPoint2 != -1 && _ctrlPoint2 >= start && _ctrlPoint2 <= end)
                {
                    float x2 = chartLeft + (float)((_ctrlPoint2 - start) / (double)count * chartW);
                    float y2 = (float)_ctrlPoint2Pos.Y;

                    // Clamp y2 to chart area
                    y2 = Math.Max(chartTop, Math.Min(chartBottom, y2));

                    canvas.DrawLine(x2, chartTop, x2, chartBottom, _ctrlMeasurePaint);
                    canvas.DrawLine(chartLeft, y2, chartRight, y2, _ctrlMeasurePaint);
                    canvas.DrawCircle(x2, y2, 5, _ctrlMeasurePaint);

                    canvas.DrawLine(x1, y1, x2, y2, _ctrlMeasureDashPaint);

                    if (!_isCtrlMeasuring)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== 2-POINT MEASUREMENT ===");
                        sb.AppendLine($"Point 1 Index: {_ctrlPoint1}");
                        sb.AppendLine($"Point 2 Index: {_ctrlPoint2}");
                        sb.AppendLine($"X Distance: {Math.Abs(_ctrlPoint2 - _ctrlPoint1)} points");

                        if (GetXAxisLabel != null)
                        {
                            string t1 = GetXAxisLabel(_ctrlPoint1);
                            string t2 = GetXAxisLabel(_ctrlPoint2);
                            if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                            {
                                sb.AppendLine($"Time 1: {t1}");
                                sb.AppendLine($"Time 2: {t2}");
                            }
                        }

                        sb.AppendLine("-------------------");

                        foreach (var s in _seriesList)
                        {
                            if (!s.IsVisible || s.Data == null) continue;
                            var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;

                            if (_ctrlPoint1 < dataToDraw.Length && _ctrlPoint2 < dataToDraw.Length)
                            {
                                double v1 = dataToDraw[_ctrlPoint1];
                                double v2 = dataToDraw[_ctrlPoint2];

                                if (!double.IsNaN(v1) && !double.IsNaN(v2))
                                {
                                    sb.AppendLine($"{s.Name}:");
                                    sb.AppendLine($"  P1: {v1:F3}");
                                    sb.AppendLine($"  P2: {v2:F3}");
                                    sb.AppendLine($"  Delta: {(v2 - v1):F3}");
                                }
                            }
                        }

                        float tooltipX = (_ctrlPoint2 > _ctrlPoint1) ? x2 + 15 : x2 - 170;
                        float tooltipY = y2;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
            }

            // State hover tooltip (shows rich CHStep data or basic state info)
            if (_hoveredState.HasValue)
            {
                string stateTooltipText;
                if (!string.IsNullOrEmpty(_hoveredState.Value.TooltipText))
                {
                    stateTooltipText = _hoveredState.Value.TooltipText;
                }
                else
                {
                    var hs = _hoveredState.Value;
                    string stateName = ChartStateConfig.GetName(hs.StateId);
                    stateTooltipText = $"State: {hs.StateId} ({stateName})";
                    if (!string.IsNullOrEmpty(hs.StateName))
                        stateTooltipText += $"\n{hs.StateName}";
                }

                float stateTooltipX = (float)_hoverPos.X + 15;
                float stateTooltipY = (float)_hoverPos.Y - 20;
                DrawTooltip(canvas, stateTooltipText, stateTooltipX, stateTooltipY);
            }

            // Hover Tooltip (Alt key)
            if (_showHoverTooltip && _hoverPos.X >= chartLeft && _hoverPos.X <= chartRight)
            {
                int hoverIdx = PixelToIndex(_hoverPos.X);
                if (hoverIdx >= start && hoverIdx <= end)
                {
                    StringBuilder tooltipText = new StringBuilder();
                    tooltipText.AppendLine($"Index: {hoverIdx}");

                    if (GetXAxisLabel != null)
                    {
                        string timeLabel = GetXAxisLabel(hoverIdx);
                        if (!string.IsNullOrEmpty(timeLabel))
                            tooltipText.AppendLine($"Time: {timeLabel}");
                    }

                    foreach (var s in _seriesList)
                    {
                        if (!s.IsVisible || s.Data == null) continue;
                        var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                        if (hoverIdx >= dataToDraw.Length) continue;
                        double val = dataToDraw[hoverIdx];
                        string valStr = double.IsNaN(val) ? "NaN" : val.ToString("F3");
                        string suffix = s.IsSmoothed ? " [S]" : "";
                        tooltipText.AppendLine($"{s.Name}{suffix}: {valStr}");
                    }

                    float tooltipX = (float)_hoverPos.X + 15;
                    float tooltipY = (float)_hoverPos.Y + 15;
                    DrawTooltip(canvas, tooltipText.ToString(), tooltipX, tooltipY);
                }
            }
        }

        private void DrawTooltip(SKCanvas c, string t, float x, float y)
        {
            var ls = t.Split('\n');
            float maxWidth = 0;

            foreach (var line in ls)
            {
                float w = _tooltipMeasureFont.MeasureText(line);
                if (w > maxWidth) maxWidth = w;
            }

            float boxW = Math.Max(150, maxWidth + 15);
            float h = ls.Length * 16 + 10;

            if (x + boxW > c.LocalClipBounds.Width) x -= (boxW + 20);
            if (y + h > c.LocalClipBounds.Height) y = c.LocalClipBounds.Height - h - 10;

            // Theme-aware tooltip — uses cached colors and paints
            _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight.WithAlpha(245) : s_tooltipBgDark.WithAlpha(245);
            _tooltipBorderPaint.Color = _accentColor;

            c.DrawRect(new SKRect(x + 2, y + 2, x + boxW + 2, y + h + 2), _tooltipShadowPaint);
            c.DrawRect(new SKRect(x, y, x + boxW, y + h), _tooltipBgPaint);
            c.DrawRect(new SKRect(x, y, x + boxW, y + h), _tooltipBorderPaint);

            _tooltipTextPaint.Color = _isLightTheme ? s_tooltipTextLight : SKColors.White;

            float ty = y + 14;
            foreach (var l in ls)
            {
                c.DrawText(l, x + 5, ty, _tooltipTextFont, _tooltipTextPaint);
                ty += 16;
            }
        }
    }
}
