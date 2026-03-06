using System;
using System.Text;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView
    {
        private void DrawStateHoverTooltip(SKCanvas canvas)
        {
            if (!_hoveredState.HasValue) return;

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

        private void DrawHoverTooltip(SKCanvas canvas, in ChartBounds b)
        {
            if (!_showHoverTooltip || _hoverPos.X < b.Left || _hoverPos.X > b.Right) return;

            int hoverIdx = PixelToIndex(_hoverPos.X);
            if (hoverIdx < b.Start || hoverIdx > b.End) return;

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
