using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models.Cpr;
using SkiaSharp;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprChartView
    {
        #region Line Plot Drawing Helpers

        private void DrawNoData(SKCanvas canvas, float w, float h)
        {
            _noDataPaint.Color = _textColor.WithAlpha(128);
            _noDataFont.Size = 14;
            _noDataFont.Typeface = s_segoeNormal;

            string msg = "No data to display";
            float tw = _noDataFont.MeasureText(msg);
            canvas.DrawText(msg, (w - tw) / 2, h / 2, _noDataFont, _noDataPaint);
        }

        private const string Ellipsis = "...";

        private void DrawTitle(SKCanvas canvas, float w, string? title)
        {
            if (string.IsNullOrEmpty(title)) return;
            _subplotTitlePaint.Color = _textColor;
            _subplotTitleFont.Size = 12;
            _subplotTitleFont.Typeface = s_segoeBold;

            float tw = _subplotTitleFont.MeasureText(title);
            // Truncate if too long
            if (tw > w - 20)
            {
                float ellipsisW = _subplotTitleFont.MeasureText(Ellipsis);
                while (title.Length > 10 && _subplotTitleFont.MeasureText(title) + ellipsisW > w - 20)
                    title = title.Substring(0, title.Length - 1);
                title += Ellipsis;
                tw = _subplotTitleFont.MeasureText(title);
            }
            canvas.DrawText(title, (w - tw) / 2, 16, _subplotTitleFont, _subplotTitlePaint);
        }

        private void DrawGrid(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xMax, double yMin, double yMax)
        {
            _gridPaint.Color = _gridColor.WithAlpha(60);
            _axisPaint.Color = _gridColor;
            _axisTextPaint.Color = _textColor;
            _axisTextFont.Typeface = s_segoeNormal;

            // Border
            canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), _axisPaint);

            // Y grid + labels
            int nYTicks = 5;
            for (int i = 0; i <= nYTicks; i++)
            {
                double val = yMin + (yMax - yMin) * i / nYTicks;
                float y = Snap(chartBottom - (chartBottom - chartTop) * i / nYTicks);
                canvas.DrawLine(chartLeft, y, chartRight, y, _gridPaint);

                string label = FormatTickLabel(val);
                float tw = _axisTextFont.MeasureText(label);
                canvas.DrawText(label, chartLeft - tw - 4, y + 4, _axisTextFont, _axisTextPaint);
            }

            // X grid + labels
            int nXTicks = 6;
            for (int i = 0; i <= nXTicks; i++)
            {
                double val = xMin + (xMax - xMin) * i / nXTicks;
                float x = Snap(chartLeft + (chartRight - chartLeft) * i / nXTicks);
                canvas.DrawLine(x, chartTop, x, chartBottom, _gridPaint);

                string label = FormatTickLabel(val);
                float tw = _axisTextFont.MeasureText(label);
                canvas.DrawText(label, x - tw / 2, chartBottom + 14, _axisTextFont, _axisTextPaint);
            }

            // Y=0 reference line (X-axis) — helps see convergence
            double yRange = yMax - yMin;
            if (yRange > 0 && yMin <= 0 && yMax >= 0)
            {
                float yZero = Snap(chartBottom - (float)((0 - yMin) / yRange * (chartBottom - chartTop)));
                _zeroLinePaint.Color = _textColor.WithAlpha(140);
                canvas.DrawLine(chartLeft, yZero, chartRight, yZero, _zeroLinePaint);
            }
        }

        private void DrawAxisLabels(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            float w, float h, string? xLabel, string? yLabel)
        {
            _axisLabelPaint.Color = _textColor.WithAlpha(180);
            _axisLabelFont.Typeface = s_segoeNormal;

            if (!string.IsNullOrEmpty(xLabel))
            {
                float tw = _axisLabelFont.MeasureText(xLabel);
                canvas.DrawText(xLabel, (chartLeft + chartRight - tw) / 2, h - 2, _axisLabelFont, _axisLabelPaint);
            }

            if (!string.IsNullOrEmpty(yLabel))
            {
                canvas.Save();
                canvas.RotateDegrees(-90, 10, (chartTop + chartBottom) / 2);
                float tw = _axisLabelFont.MeasureText(yLabel);
                canvas.DrawText(yLabel, 10 - tw / 2, (chartTop + chartBottom) / 2 + 4, _axisLabelFont, _axisLabelPaint);
                canvas.Restore();
            }
        }

        private void DrawDftMarkers(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange)
        {
            float chartW = chartRight - chartLeft;

            _dftMarkerPaint.StrokeWidth = 1;
            _dftLabelFont.Typeface = s_segoeNormal;

            foreach (var marker in _graphResult!.DftMarkers!)
            {
                float x = chartLeft + (float)((marker.Frequency - xMin) / xRange * chartW);
                if (x < chartLeft || x > chartRight) continue;

                _dftMarkerPaint.Color = marker.Color == SKColors.Black ? _textColor.WithAlpha(180) : marker.Color;
                _dftMarkerPaint.PathEffect = marker.IsDashed ? SKPathEffect.CreateDash(new float[] { 4, 3 }, 0) : null;

                canvas.DrawLine(x, chartTop, x, chartBottom, _dftMarkerPaint);

                _dftLabelPaint.Color = _dftMarkerPaint.Color;
                canvas.DrawText(marker.Label, x + 2, chartTop + 12, _dftLabelFont, _dftLabelPaint);

                _dftMarkerPaint.PathEffect = null;
            }
        }

        private void DrawVerticalRefLines(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange, float chartW)
        {
            _dftMarkerPaint.StrokeWidth = 1.5f;
            _dftLabelFont.Typeface = s_segoeNormal;

            float labelOffset = 0;
            foreach (var refLine in _graphResult!.VerticalRefLines!)
            {
                float x = chartLeft + (float)((refLine.XValue - xMin) / xRange * chartW);
                if (x < chartLeft || x > chartRight) continue;

                // Adapt black color to theme
                _dftMarkerPaint.Color = refLine.Color == SKColors.Black ? _textColor.WithAlpha(180) : refLine.Color;

                // Set line style
                switch (refLine.LineStyle)
                {
                    case RefLineStyle.Solid:
                        _dftMarkerPaint.PathEffect = null;
                        break;
                    case RefLineStyle.Dashed:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0);
                        break;
                    case RefLineStyle.DashDot:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3, 2, 3 }, 0);
                        break;
                    case RefLineStyle.Dotted:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 2, 3 }, 0);
                        break;
                }

                canvas.DrawLine(x, chartTop, x, chartBottom, _dftMarkerPaint);

                _dftLabelPaint.Color = _dftMarkerPaint.Color;
                canvas.DrawText(refLine.Label, x + 2, chartTop + 12 + labelOffset, _dftLabelFont, _dftLabelPaint);
                labelOffset += 10; // Stagger labels so they don't overlap

                _dftMarkerPaint.PathEffect = null;
            }
        }

        private void DrawLegend(SKCanvas canvas, float chartRight, float chartTop, List<CprSeriesData> series)
        {
            if (series == null || series.Count == 0) return;

            float legendX = chartRight + 4;
            float legendY = chartTop + 5;

            _axisTextPaint.Color = _textColor;
            _axisTextFont.Typeface = s_segoeNormal;
            _subplotLinePaint.StrokeWidth = 2;
            _subplotLinePaint.IsAntialias = false;

            foreach (var s in series)
            {
                _subplotLinePaint.Color = s.Color;
                canvas.DrawLine(legendX, legendY + 5, legendX + 14, legendY + 5, _subplotLinePaint);
                canvas.DrawText(s.Name, legendX + 17, legendY + 9, _axisTextFont, _axisTextPaint);
                legendY += LEGEND_LINE_HEIGHT;
            }
            _subplotLinePaint.IsAntialias = true; // restore default
        }

        private void DrawHoverTooltip(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange, double yMin, double yRange, List<CprSeriesData> series)
        {
            float mx = (float)_hoverPos.X;
            float my = (float)_hoverPos.Y;

            if (mx < chartLeft || mx > chartRight || my < chartTop || my > chartBottom) return;

            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            double dataX = xMin + (mx - chartLeft) / chartW * xRange;
            double dataY = yMin + (chartBottom - my) / chartH * yRange;

            // --- Draw crosshair lines ---
            _crosshairPaint.Color = _textColor.WithAlpha(80);
            _crosshairPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
            canvas.DrawLine(mx, chartTop, mx, chartBottom, _crosshairPaint);   // vertical
            canvas.DrawLine(chartLeft, my, chartRight, my, _crosshairPaint);   // horizontal
            _crosshairPaint.PathEffect = null;

            // --- Draw Y value label on the left Y-axis ---
            _dftLabelPaint.Color = _textColor;
            _dftLabelFont.Size = 9;
            _dftLabelFont.Typeface = s_segoeNormal;
            _tooltipBgPaint.Color = _bgColor.WithAlpha(220);
            {
                string yLabel = FormatTickLabel(dataY);
                float tw = _dftLabelFont.MeasureText(yLabel);
                var yRect = new SKRect(chartLeft - tw - 7, my - 7, chartLeft - 1, my + 7);
                canvas.DrawRect(yRect, _tooltipBgPaint);
                canvas.DrawText(yLabel, chartLeft - tw - 4, my + 3, _dftLabelFont, _dftLabelPaint);

                // X value label on the bottom X-axis
                string xLabel = FormatTickLabel(dataX);
                float xw = _dftLabelFont.MeasureText(xLabel);
                var xRect = new SKRect(mx - xw / 2 - 3, chartBottom + 1, mx + xw / 2 + 3, chartBottom + 15);
                canvas.DrawRect(xRect, _tooltipBgPaint);
                canvas.DrawText(xLabel, mx - xw / 2, chartBottom + 12, _dftLabelFont, _dftLabelPaint);
            }

            // --- Build tooltip with per-series Y values at cursor X ---
            var tooltipLines = new List<(string text, SKColor color)>();

            foreach (var s in series)
            {
                if (s.XValues == null || s.YValues == null || s.XValues.Length == 0) continue;

                // Find nearest X index to cursor dataX using binary search
                int bestIdx = -1;
                double bestDist = double.MaxValue;
                int lo = 0, hi = s.XValues.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    double d = Math.Abs(s.XValues[mid] - dataX);
                    if (d < bestDist) { bestDist = d; bestIdx = mid; }
                    if (s.XValues[mid] < dataX) lo = mid + 1;
                    else hi = mid - 1;
                }

                if (bestIdx >= 0 && !double.IsNaN(s.YValues[bestIdx]))
                {
                    string yVal = FormatTickLabel(s.YValues[bestIdx]);
                    tooltipLines.Add(($"{s.Name}: {yVal}", s.Color));
                }
            }

            if (tooltipLines.Count == 0) return;

            // --- Draw tooltip box ---
            _tooltipBgPaint.Color = _bgColor.WithAlpha(230);
            _tooltipBorderPaint.Color = _gridColor;
            _tooltipTextPaint.Color = _textColor;
            _tooltipTextFont.Typeface = s_segoeNormal;
            {
                // Header line
                string header = $"X: {FormatTickLabel(dataX)}";
                float lineHeight = 14;
                float maxW = _tooltipTextFont.MeasureText(header);
                foreach (var line in tooltipLines)
                {
                    float lw = _tooltipTextFont.MeasureText(line.text);
                    if (lw + 18 > maxW) maxW = lw + 18; // 18 = color swatch + gap
                }

                float boxW = maxW + 12;
                float boxH = lineHeight * (tooltipLines.Count + 1) + 10; // +1 for header

                float tipX = mx + 14;
                float tipY = my - boxH / 2;
                // Keep within chart bounds
                if (tipX + boxW > chartRight) tipX = mx - boxW - 14;
                if (tipY < chartTop) tipY = chartTop;
                if (tipY + boxH > chartBottom) tipY = chartBottom - boxH;

                var rect = new SKRect(tipX, tipY, tipX + boxW, tipY + boxH);
                canvas.DrawRoundRect(rect, 4, 4, _tooltipBgPaint);
                canvas.DrawRoundRect(rect, 4, 4, _tooltipBorderPaint);

                // Draw header
                float textX = tipX + 6;
                float textY = tipY + lineHeight;
                canvas.DrawText(header, textX, textY, _tooltipTextFont, _tooltipTextPaint);

                // Separator line
                textY += 3;
                _gridPaint.Color = _gridColor.WithAlpha(100);
                canvas.DrawLine(tipX + 4, textY, tipX + boxW - 4, textY, _gridPaint);

                // Draw series values
                foreach (var line in tooltipLines)
                {
                    textY += lineHeight;
                    // Color swatch
                    _tooltipColorPaint.Color = line.color;
                    canvas.DrawLine(textX, textY - 4, textX + 10, textY - 4, _tooltipColorPaint);
                    // Text
                    canvas.DrawText(line.text, textX + 14, textY, _tooltipTextFont, _tooltipTextPaint);
                }
            }
        }

        #endregion
    }
}
