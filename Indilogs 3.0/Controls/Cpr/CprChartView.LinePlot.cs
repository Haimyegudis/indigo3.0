using System;
using SkiaSharp;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprChartView
    {
        #region Line Plot

        private void DrawLinePlot(SKCanvas canvas, SKImageInfo info)
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

            var series = _graphResult!.Series;
            if (series == null || series.Count == 0)
            {
                DrawNoData(canvas, w, h);
                return;
            }

            // Calculate full data ranges
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var s in series)
            {
                if (s.XValues == null || s.YValues == null) continue;
                for (int i = 0; i < s.XValues.Length; i++)
                {
                    if (double.IsNaN(s.XValues[i]) || double.IsNaN(s.YValues[i])) continue;
                    if (s.XValues[i] < xMin) xMin = s.XValues[i];
                    if (s.XValues[i] > xMax) xMax = s.XValues[i];
                    if (s.YValues[i] < yMin) yMin = s.YValues[i];
                    if (s.YValues[i] > yMax) yMax = s.YValues[i];
                }
            }

            if (xMin >= xMax || yMin >= yMax)
            {
                DrawNoData(canvas, w, h);
                return;
            }

            // Apply manual Y-axis if specified
            if (!_graphResult.AutoYAxis)
            {
                yMin = _graphResult.YAxisFrom;
                yMax = _graphResult.YAxisTo;
            }
            else
            {
                // Add 5% padding
                double yPad = (yMax - yMin) * 0.05;
                yMin -= yPad;
                yMax += yPad;
            }

            // Store full data ranges for zoom
            _dataXMin = xMin;
            _dataXMax = xMax;
            _dataYMin = yMin;
            _dataYMax = yMax;

            // Apply zoom if active
            if (_zoomXMin.HasValue)
            {
                xMin = _zoomXMin.Value;
                xMax = _zoomXMax!.Value;
                yMin = _zoomYMin!.Value;
                yMax = _zoomYMax!.Value;
            }

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            // Draw title
            DrawTitle(canvas, w, _graphResult.Title);

            // Draw grid
            DrawGrid(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xMax, yMin, yMax);

            // Draw axis labels
            DrawAxisLabels(canvas, chartLeft, chartTop, chartRight, chartBottom, w, h, _graphResult.XLabel, _graphResult.YLabel);

            // Draw DFT markers if present
            if (_graphResult.DftMarkers != null)
            {
                DrawDftMarkers(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xRange);
            }

            // Draw vertical reference lines if present (Colors graph)
            if (_graphResult.VerticalRefLines != null)
            {
                DrawVerticalRefLines(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xRange, chartW);
            }

            // Draw series with clipping
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            _subplotLinePaint.IsAntialias = true;
            using (var path = new SKPath())
            {
                foreach (var s in series)
                {
                    if (s.XValues == null || s.YValues == null || s.XValues.Length == 0) continue;

                    _subplotLinePaint.Color = s.Color;
                    _subplotLinePaint.StrokeWidth = s.StrokeWidth;
                    _subplotLinePaint.PathEffect = s.IsDashed ? SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) : null;

                    path.Reset();
                    bool first = true;

                    for (int i = 0; i < s.XValues.Length; i++)
                    {
                        if (double.IsNaN(s.YValues[i]))
                        {
                            first = true;
                            continue;
                        }

                        float x = chartLeft + (float)((s.XValues[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)((s.YValues[i] - yMin) / yRange * chartH);

                        if (first)
                        {
                            path.MoveTo(x, y);
                            first = false;
                        }
                        else
                        {
                            path.LineTo(x, y);
                        }
                    }

                    if (!first) canvas.DrawPath(path, _subplotLinePaint);
                    _subplotLinePaint.PathEffect = null;
                }
            }

            canvas.Restore();

            // Draw legend
            DrawLegend(canvas, chartRight, chartTop, series);

            // Draw zoom indicator
            if (_zoomXMin.HasValue)
            {
                _dftLabelPaint.Color = _textColor.WithAlpha(100);
                _dftLabelFont.Size = 9;
                canvas.DrawText("Right-click to reset zoom", chartLeft + 4, h - 3, _dftLabelFont, _dftLabelPaint);
            }

            // Draw hover tooltip
            if (_showHover)
            {
                DrawHoverTooltip(canvas, chartLeft, chartTop, chartRight, chartBottom,
                    xMin, xRange, yMin, yRange, series);
            }
        }

        #endregion

        #region Formatting Utilities

        private static string FormatTickLabel(double val)
        {
            double abs = Math.Abs(val);
            if (abs >= 10000 || (abs > 0 && abs < 0.01))
                return val.ToString("E1");
            if (abs >= 100)
                return val.ToString("F0");
            if (abs >= 1)
                return val.ToString("F1");
            return val.ToString("F3");
        }

        private static float Snap(float coord) => (float)Math.Floor(coord) + 0.5f;

        #endregion
    }
}
