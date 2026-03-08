using System;
using SkiaSharp;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGanttView
    {
        private void DrawEventTooltip(SKCanvas canvas, string text, float x, float y)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight : s_tooltipBgDark;
            _tooltipBorderPaint.Color = SKColors.Red;
            _tooltipBorderPaint.StrokeWidth = 1.5f;
            _tooltipTextPaint.Color = _isLightTheme ? s_darkTextColor : SKColors.White;

            float maxWidth = 0;
            foreach (var line in lines)
                maxWidth = Math.Max(maxWidth, _tooltipTextFont.MeasureText(line));

            float tooltipW = maxWidth + 16;
            float tooltipH = lines.Length * 16 + 12;

            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6), _tooltipShadowPaint);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 6), _tooltipBgPaint);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 6), _tooltipBorderPaint);

            float ty = y + 16;
            foreach (var line in lines)
            {
                canvas.DrawText(line, x + 8, ty, _tooltipTextFont, _tooltipTextPaint);
                ty += 16;
            }
        }

        private void DrawCHStepTooltip(SKCanvas canvas, string text, float x, float y, float canvasWidth, float canvasHeight)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight : s_tooltipBgDark;
            _tooltipBorderPaint.Color = _isLightTheme ? s_tooltipBorderLight : s_tooltipBorderDark;
            _tooltipBorderPaint.StrokeWidth = 1f;
            _tooltipTextPaint.Color = _isLightTheme ? s_darkTextColor : SKColors.White;

            float maxWidth = 0;
            foreach (var line in lines)
                maxWidth = Math.Max(maxWidth, _tooltipTextFont.MeasureText(line));

            float tooltipW = maxWidth + 20;
            float tooltipH = lines.Length * 16 + 14;
            float accentW = 4;

            if (x + tooltipW > canvasWidth - 5)
                x = x - tooltipW - 30;
            if (y + tooltipH > canvasHeight - 5)
                y = canvasHeight - tooltipH - 5;
            if (y < 5) y = 5;

            var rect = new SKRect(x, y, x + tooltipW, y + tooltipH);

            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6), _tooltipShadowPaint);
            canvas.DrawRoundRect(new SKRoundRect(rect, 6), _tooltipBgPaint);
            if (_hoveredStateInterval.HasValue)
            {
                SKColor barColor = CHStepColors[Math.Abs(_hoveredStateInterval.Value.StateId) % CHStepColors.Length];
                _tooltipAccentPaint.Color = barColor;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + accentW, y + tooltipH), 6, 0), _tooltipAccentPaint);
                canvas.DrawRect(new SKRect(x + 3, y, x + accentW, y + tooltipH), _tooltipAccentPaint);
            }
            canvas.DrawRoundRect(new SKRoundRect(rect, 6), _tooltipBorderPaint);

            float ty = y + 16;
            foreach (var line in lines)
            {
                canvas.DrawText(line, x + 10, ty, _tooltipTextFont, _tooltipTextPaint);
                ty += 16;
            }
        }

        private void DrawXAxis(SKCanvas canvas, float chartLeft, float chartRight, float chartBottom, float totalHeight, int start, int end, int count)
        {
            float chartWidth = chartRight - chartLeft;

            canvas.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, _borderPaint);

            int labelCount = 5;
            int step = Math.Max(1, count / labelCount);

            _axisPaint.Color = _textColor;
            for (int i = 0; i <= labelCount; i++)
            {
                int index = start + (int)((double)i / labelCount * count);
                if (index >= start && index <= end)
                {
                    float x = chartLeft + (float)((index - start) / (double)count * chartWidth);

                    canvas.DrawLine(x, chartBottom, x, chartBottom + 4, _borderPaint);

                    string label = GetXAxisLabel?.Invoke(index) ?? index.ToString();
                    float textWidth = _axisFont.MeasureText(label);
                    float textX = x - textWidth / 2;
                    textX = Math.Max(chartLeft, Math.Min(textX, chartRight - textWidth));
                    canvas.DrawText(label, textX, chartBottom + 14, _axisFont, _axisPaint);
                }
            }
        }
    }
}
