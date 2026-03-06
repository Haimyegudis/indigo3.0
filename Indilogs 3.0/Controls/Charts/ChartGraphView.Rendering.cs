using System;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using IndiLogs_3._0.Models.Charts;

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

            var bounds = new ChartBounds(chartLeft, chartRight, chartTop, chartBottom, chartW, chartH, start, end, count);

            DrawStates(canvas, in bounds);
            DrawTimeGaps(canvas, in bounds);

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

            var scale = new ScaleInfo(lDisplayMin, lRange, rDisplayMin, rRange, hasLeft, hasRight);

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

            DrawReferenceLines(canvas, in bounds, in scale);
            DrawThreadMarkers(canvas, in bounds);

            // Signal Lines
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

            DrawEventMarkers(canvas, in bounds);

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

            DrawMeasureBox(canvas, in bounds, in scale);
            DrawCtrlMeasurement(canvas, in bounds);
            DrawStateHoverTooltip(canvas);
            DrawHoverTooltip(canvas, in bounds);
        }
    }
}
