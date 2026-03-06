using System;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;
using SkiaSharp;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprChartView
    {
        #region Histogram

        private void DrawHistogram(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            var hist = _graphResult!.HistogramData!;
            if (hist.BinEdges == null || hist.BinCounts == null) return;

            double xMin = hist.BinEdges.First();
            double xMax = hist.BinEdges.Last();
            double yMin = 0;
            double yMax = Math.Max(hist.BinCounts.Max(), hist.NormalY != null && hist.NormalY.Length > 0 ? hist.NormalY.Max() : 0);
            yMax *= 1.1;

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            DrawTitle(canvas, w, _graphResult.Title);
            DrawGrid(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xMax, yMin, yMax);
            DrawAxisLabels(canvas, chartLeft, chartTop, chartRight, chartBottom, w, h, _graphResult.XLabel, _graphResult.YLabel);

            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            // Draw bars
            _histBarPaint.Color = s_histBarColor;
            for (int i = 0; i < hist.BinCounts.Length; i++)
            {
                float x1 = chartLeft + (float)((hist.BinEdges[i] - xMin) / xRange * chartW);
                float x2 = chartLeft + (float)((hist.BinEdges[i + 1] - xMin) / xRange * chartW);
                float yTop = chartBottom - (float)(hist.BinCounts[i] / yRange * chartH);

                canvas.DrawRect(new SKRect(x1, yTop, x2, chartBottom), _histBarPaint);
            }

            // Draw normal curve
            if (hist.NormalX != null && hist.NormalY != null)
            {
                using (var path = new SKPath())
                {
                    bool first = true;
                    for (int i = 0; i < hist.NormalX.Length; i++)
                    {
                        float x = chartLeft + (float)((hist.NormalX[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)(hist.NormalY[i] / yRange * chartH);
                        if (first) { path.MoveTo(x, y); first = false; }
                        else path.LineTo(x, y);
                    }
                    canvas.DrawPath(path, _histCurvePaint);
                }
            }

            canvas.Restore();
        }

        #endregion

        #region Subplots (Skew)

        private void DrawSubplots(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            int rows = _graphResult!.SubplotRows;
            int cols = _graphResult.SubplotCols;

            float cellW = w / cols;
            float cellH = h / rows;

            // Calculate shared Y range if needed
            double sharedYMin = double.MaxValue, sharedYMax = double.MinValue;
            if (_graphResult.SharedYAxis)
            {
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var sp = _graphResult.Subplots![r, c];
                    if (sp == null) continue;
                    foreach (var scatter in sp.ScatterSeries)
                    {
                        if (scatter.YValues == null) continue;
                        foreach (var v in scatter.YValues)
                        {
                            if (double.IsNaN(v)) continue;
                            if (v < sharedYMin) sharedYMin = v;
                            if (v > sharedYMax) sharedYMax = v;
                        }
                    }
                }
                double pad = (sharedYMax - sharedYMin) * 0.1;
                sharedYMin -= pad;
                sharedYMax += pad;
            }

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var sp = _graphResult.Subplots![r, c];
                if (sp == null) continue;

                float ox = c * cellW;
                float oy = r * cellH;

                canvas.Save();
                canvas.Translate(ox, oy);

                DrawSingleSubplot(canvas, cellW, cellH, sp,
                    _graphResult.SharedYAxis ? sharedYMin : double.NaN,
                    _graphResult.SharedYAxis ? sharedYMax : double.NaN);

                canvas.Restore();
            }
        }

        private void DrawSingleSubplot(SKCanvas canvas, float w, float h, CprSubplot subplot,
            double forceYMin, double forceYMax)
        {
            float chartLeft = 45;
            float chartRight = w - 10;
            float chartTop = 22;
            float chartBottom = h - 20;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            // Calculate ranges from scatter data
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var scatter in subplot.ScatterSeries)
            {
                if (scatter.XValues == null || scatter.YValues == null) continue;
                for (int i = 0; i < scatter.XValues.Length; i++)
                {
                    if (double.IsNaN(scatter.XValues[i]) || double.IsNaN(scatter.YValues[i])) continue;
                    if (scatter.XValues[i] < xMin) xMin = scatter.XValues[i];
                    if (scatter.XValues[i] > xMax) xMax = scatter.XValues[i];
                    if (scatter.YValues[i] < yMin) yMin = scatter.YValues[i];
                    if (scatter.YValues[i] > yMax) yMax = scatter.YValues[i];
                }
            }

            if (xMin >= xMax) return;

            if (!double.IsNaN(forceYMin))
            {
                yMin = forceYMin;
                yMax = forceYMax;
            }
            else
            {
                double yPad = (yMax - yMin) * 0.1;
                yMin -= yPad;
                yMax += yPad;
            }

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            // Subplot border (reuse cached paint)
            _subplotBorderPaint.Color = _gridColor;
            canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), _subplotBorderPaint);

            // Subplot title (reuse cached paint)
            _subplotTitlePaint.Color = _textColor;
            _subplotTitleFont.Size = 11;
            _subplotTitleFont.Typeface = s_segoeNormal;
            float titleW = _subplotTitleFont.MeasureText(subplot.Title);
            canvas.DrawText(subplot.Title, (chartLeft + chartRight - titleW) / 2, chartTop - 6, _subplotTitleFont, _subplotTitlePaint);

            // Y-axis labels (3 ticks)
            DrawSubplotYAxis(canvas, chartLeft, chartTop, chartBottom, yMin, yMax);

            // Draw scatter points
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            foreach (var scatter in subplot.ScatterSeries)
            {
                if (scatter.XValues == null || scatter.YValues == null) continue;
                _subplotDotPaint.Color = scatter.Color;

                for (int i = 0; i < scatter.XValues.Length; i++)
                {
                    if (double.IsNaN(scatter.YValues[i])) continue;
                    float x = chartLeft + (float)((scatter.XValues[i] - xMin) / xRange * chartW);
                    float y = chartBottom - (float)((scatter.YValues[i] - yMin) / yRange * chartH);
                    canvas.DrawCircle(x, y, 2.5f, _subplotDotPaint);
                }
            }

            // Draw line series (regression + polynomial)
            using (var path = new SKPath())
            {
                foreach (var line in subplot.LineSeries)
                {
                    if (line.XValues == null || line.YValues == null || line.XValues.Length == 0) continue;
                    _subplotLinePaint.Color = line.Color;
                    _subplotLinePaint.StrokeWidth = line.StrokeWidth;
                    _subplotLinePaint.PathEffect = line.IsDashed ? SKPathEffect.CreateDash(new float[] { 5, 3 }, 0) : null;

                    path.Reset();
                    bool first = true;
                    for (int i = 0; i < line.XValues.Length; i++)
                    {
                        if (double.IsNaN(line.YValues[i])) { first = true; continue; }
                        float x = chartLeft + (float)((line.XValues[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)((line.YValues[i] - yMin) / yRange * chartH);
                        if (first) { path.MoveTo(x, y); first = false; }
                        else path.LineTo(x, y);
                    }
                    if (!first) canvas.DrawPath(path, _subplotLinePaint);
                    _subplotLinePaint.PathEffect = null;
                }
            }

            canvas.Restore();
        }

        private void DrawSubplotYAxis(SKCanvas canvas, float chartLeft, float chartTop, float chartBottom, double yMin, double yMax)
        {
            _subplotAxisTextPaint.Color = _textColor;
            _subplotAxisTextFont.Size = 9;
            _subplotAxisTextFont.Typeface = s_segoeNormal;

            int nTicks = 3;
            for (int i = 0; i <= nTicks; i++)
            {
                double val = yMin + (yMax - yMin) * i / nTicks;
                float y = chartBottom - (chartBottom - chartTop) * i / nTicks;
                string label = FormatTickLabel(val);
                float tw = _subplotAxisTextFont.MeasureText(label);
                canvas.DrawText(label, chartLeft - tw - 3, y + 3, _subplotAxisTextFont, _subplotAxisTextPaint);
            }
        }

        #endregion
    }
}
