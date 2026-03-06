using System;
using System.Collections.Generic;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public partial class TimelineCanvas
    {
        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_cachedStates == null || _cachedStates.Count == 0 || _cachedTotalSeconds <= 0) return;

            // Use WPF ActualWidth/Height for coordinate calculations (matches mouse coords)
            float w = (float)SkiaCanvas.ActualWidth;
            float h = (float)SkiaCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Scale canvas to match WPF coordinates
            float scaleX = info.Width / w;
            float scaleY = info.Height / h;
            canvas.Scale(scaleX, scaleY);

            float chartBottom = h - TIME_AXIS_HEIGHT;

            // Track hovered state/marker for tooltip
            object? hoveredObj = null;
            float hoverX = (float)_currentMousePos.X;
            float hoverY = (float)_currentMousePos.Y;

            // ─── Draw state bars ───
            for (int i = 0; i < _cachedStates.Count; i++)
            {
                var state = _cachedStates[i];
                float x1 = (float)TimeToX((state.StartTime - _cachedMinTime).TotalSeconds, w, _cachedTotalSeconds);
                float x2 = (float)TimeToX((state.EndTime - _cachedMinTime).TotalSeconds, w, _cachedTotalSeconds);
                float barW = Math.Max(2, x2 - x1);
                x2 = x1 + barW;

                if (x2 < 0 || x1 > w) continue;

                SKColor baseColor = GetMaterialColorForState(state.Name);
                bool isCriticalFailure = state.Status == "FAILED";

                // Hover detection
                bool isHovered = hoverX >= x1 && hoverX <= x2 && hoverY >= TIMELINE_Y && hoverY <= TIMELINE_Y + BAR_HEIGHT;
                if (isHovered) hoveredObj = state;

                if (isCriticalFailure) baseColor = s_criticalFailureColor;

                var barRect = new SKRect(x1, TIMELINE_Y, x2, TIMELINE_Y + BAR_HEIGHT);
                var barRoundRect = new SKRoundRect(barRect, 3, 3);

                // Gradient fill
                SKColor topColor = isHovered ? baseColor : LightenColor(baseColor, 0.15f);
                SKColor bottomColor = isHovered ? DarkenColor(baseColor, 0.1f) : DarkenColor(baseColor, 0.2f);

                _gradientPaint.Shader?.Dispose();
                _gradientPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, TIMELINE_Y), new SKPoint(0, TIMELINE_Y + BAR_HEIGHT),
                    new[] { topColor, bottomColor }, null, SKShaderTileMode.Clamp);
                canvas.DrawRoundRect(barRoundRect, _gradientPaint);

                // Inner highlight glow (top 40% of bar)
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x1 + 1, TIMELINE_Y + 1, x2 - 1, TIMELINE_Y + BAR_HEIGHT * 0.4f), 2, 2), _glowPaint);

                // Thin dark border
                _edgePaint.Color = DarkenColor(baseColor, 0.35f);
                canvas.DrawRoundRect(barRoundRect, _edgePaint);

                // Hovered bar highlight
                if (isHovered)
                    canvas.DrawRoundRect(barRoundRect, _highlightBorderPaint);

                // Hazard pattern for FAILED states
                if (isCriticalFailure)
                    DrawHazardPattern(canvas, x1, TIMELINE_Y, barW, BAR_HEIGHT, barRoundRect);

                // Status indicator border
                if (state.Status == "SUCCESS")
                {
                    _edgePaint.Color = s_successBorderColor;
                    _edgePaint.StrokeWidth = 2;
                    canvas.DrawRoundRect(barRoundRect, _edgePaint);
                    _edgePaint.StrokeWidth = 0.8f;
                }

                // Text label with auto-contrast
                if (barW > 30)
                {
                    string displayText = state.Name;
                    if (isCriticalFailure) displayText += " (FAILED!)";

                    float textWidth = _labelFont.MeasureText(displayText);
                    if (textWidth > barW - 10)
                    {
                        while (displayText.Length > 3 && _labelFont.MeasureText(displayText + "..") > barW - 10)
                            displayText = displayText.Substring(0, displayText.Length - 1);
                        displayText += "..";
                        textWidth = _labelFont.MeasureText(displayText);
                    }

                    float brightness = (baseColor.Red * 0.299f + baseColor.Green * 0.587f + baseColor.Blue * 0.114f) / 255f;
                    _labelPaint.Color = brightness > 0.55f ? s_darkTextColor : SKColors.White;

                    float textX = x1 + (barW - textWidth) / 2;
                    float textY = TIMELINE_Y + BAR_HEIGHT / 2 + 4.5f;
                    canvas.DrawText(displayText, textX, textY, _labelFont, _labelPaint);
                }
            }

            // ─── Draw markers ───
            if (_cachedMarkers != null)
            {
                for (int i = 0; i < _cachedMarkers.Count; i++)
                {
                    var marker = _cachedMarkers[i];
                    float mxf = (float)TimeToX((marker.Time - _cachedMinTime).TotalSeconds, w, _cachedTotalSeconds);
                    if (mxf < 0 || mxf > w) continue;

                    if (marker.Type == TimelineMarkerType.Error)
                    {
                        float my = TIMELINE_Y - MARKER_AREA;
                        DrawErrorMarker(canvas, mxf, my);
                        if (Math.Abs(hoverX - mxf) < 10 && Math.Abs(hoverY - (my + 6)) < 10)
                            hoveredObj = marker;
                    }
                    else
                    {
                        float my = TIMELINE_Y + BAR_HEIGHT + 8;
                        DrawEventMarker(canvas, mxf, my, s_eventMarkerCyan);
                        if (Math.Abs(hoverX - mxf) < 8 && Math.Abs(hoverY - my) < 8)
                            hoveredObj = marker;
                    }
                }
            }

            // ─── Draw time axis ───
            DrawTimeAxis(canvas, chartBottom, w, _cachedTotalSeconds, _cachedMinTime);

            // ─── Draw zoom selection rectangle ───
            if (_isZooming)
            {
                float zx = (float)Math.Min(_zoomStartPoint.X, _currentMousePos.X);
                float zw = (float)Math.Abs(_zoomStartPoint.X - _currentMousePos.X);

                using (var zoomFillPaint = new SKPaint { Color = new SKColor(0, 120, 215, 50), Style = SKPaintStyle.Fill })
                using (var zoomBorderPaint = new SKPaint { Color = s_zoomBorderColor, StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true })
                {
                    canvas.DrawRect(new SKRect(zx, 0, zx + zw, h), zoomFillPaint);
                    canvas.DrawRect(new SKRect(zx, 0, zx + zw, h), zoomBorderPaint);
                }
            }

            // ─── Draw tooltip ───
            if (_showTooltip && _currentHoverObject != null)
            {
                DrawTooltip(canvas, _currentHoverObject, hoverX + 15, hoverY - 10, w, h);
            }

            // Update hover tracking
            if (hoveredObj != _currentHoverObject)
            {
                _hoverTimer!.Stop();
                _showTooltip = false;
                _currentHoverObject = hoveredObj;
                if (hoveredObj != null) _hoverTimer.Start();
            }
        }

        private void DrawHazardPattern(SKCanvas canvas, float x, float y, float bw, float bh, SKRoundRect clipRect)
        {
            canvas.Save();
            canvas.ClipRoundRect(clipRect);

            float step = 15;
            for (float i = -bh; i < bw; i += step)
                canvas.DrawLine(x + i, y, x + i + bh, y + bh, _hazardStripePaint);

            canvas.Restore();

            float cx = x + bw / 2;
            float cy = y + bh / 2;
            float s = 6;
            canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, _hazardXPaint);
            canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, _hazardXPaint);
        }

        private void DrawErrorMarker(SKCanvas canvas, float x, float y)
        {
            _markerGlowPaint.Color = SKColors.Red.WithAlpha(60);
            canvas.DrawCircle(x, y + 6, 12, _markerGlowPaint);

            var shader = SKShader.CreateRadialGradient(
                new SKPoint(x, y + 4), 8,
                new[] { s_errorGradientTop, s_criticalFailureColor },
                null, SKShaderTileMode.Clamp);
            _markerFillPaint.Shader = shader;
            canvas.DrawCircle(x, y + 6, 8, _markerFillPaint);
            _markerFillPaint.Shader = null;
            shader.Dispose();

            canvas.DrawCircle(x, y + 6, 8, _markerBorderPaint);

            canvas.DrawLine(x - 3, y + 3, x + 3, y + 9, _markerXPaint);
            canvas.DrawLine(x + 3, y + 3, x - 3, y + 9, _markerXPaint);
        }

        private void DrawEventMarker(SKCanvas canvas, float x, float y, SKColor color)
        {
            _markerGlowPaint.Color = color.WithAlpha(50);
            canvas.DrawCircle(x, y, 10, _markerGlowPaint);

            using (var path = new SKPath())
            {
                path.MoveTo(x, y - 6);
                path.LineTo(x + 6, y);
                path.LineTo(x, y + 6);
                path.LineTo(x - 6, y);
                path.Close();

                var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y - 6), new SKPoint(x, y + 6),
                    new[] { LightenColor(color, 0.2f), DarkenColor(color, 0.2f) },
                    null, SKShaderTileMode.Clamp);
                _markerFillPaint.Shader = shader;
                canvas.DrawPath(path, _markerFillPaint);
                _markerFillPaint.Shader = null;
                shader.Dispose();

                _markerBorderPaint.StrokeWidth = 1;
                canvas.DrawPath(path, _markerBorderPaint);
                _markerBorderPaint.StrokeWidth = 1.2f; // restore default
            }
        }

        private void DrawTimeAxis(SKCanvas canvas, float y, float w, double totalSeconds, DateTime startTime)
        {
            _axisPaint.Color = _gridColor;
            _axisTextPaint.Color = _textColor;

            canvas.DrawLine(0, y, w, y, _axisPaint);

            double pixelPerSecond = (w * ViewScale) / totalSeconds;
            double step = 100 / pixelPerSecond;
            if (step < 1) step = 1;
            if (step > 60) step = 60;
            else if (step > 30) step = 30;
            else if (step > 10) step = 10;
            else if (step > 5) step = 5;

            double startSec = XToSeconds(0, w, totalSeconds);
            double endSec = XToSeconds(w, w, totalSeconds);

            for (double t = Math.Floor(startSec / step) * step; t < endSec; t += step)
            {
                float x = (float)TimeToX(t, w, totalSeconds);
                canvas.DrawLine(x, y, x, y + 5, _axisPaint);

                DateTime absoluteTime = startTime.AddSeconds(t);
                string label = absoluteTime.ToString("HH:mm:ss");
                float tw = _axisTextFont.MeasureText(label);
                canvas.DrawText(label, x - tw / 2, y + 20, _axisTextFont, _axisTextPaint);
            }
        }

        private void DrawTooltip(SKCanvas canvas, object obj, float x, float y, float canvasW, float canvasH)
        {
            string[] lines;
            SKColor accentColor;

            if (obj is TimelineState s)
            {
                accentColor = GetMaterialColorForState(s.Name);
                lines = new[]
                {
                    s.Name,
                    $"Duration: {s.Duration.TotalSeconds:F2}s",
                    $"Errors: {s.ErrorCount}",
                    $"Status: {s.Status}"
                };
            }
            else if (obj is TimelineMarker m)
            {
                accentColor = m.Type == TimelineMarkerType.Error ? StateColorError : s_eventMarkerCyan;
                var lineList = new List<string>();
                lineList.Add(m.Type == TimelineMarkerType.Error ? "ERROR" : "EVENT");
                if (!string.IsNullOrEmpty(m.Message)) lineList.Add(m.Message);
                lineList.Add($"Time: {m.Time:HH:mm:ss.ffffff}");
                if (!string.IsNullOrEmpty(m.Severity)) lineList.Add($"Severity: {m.Severity}");
                lines = lineList.ToArray();
            }
            else return;

            _tooltipTextPaint.Color = _isLightTheme ? s_darkTextColor : SKColors.White;
            _tooltipTextFont.Typeface = s_consolas;
            {
                var textFont = _tooltipTextFont;
                float maxWidth = 0;
                foreach (var line in lines)
                    maxWidth = Math.Max(maxWidth, textFont.MeasureText(line));

                float accentBarWidth = 4;
                float tooltipW = maxWidth + 20 + accentBarWidth;
                float tooltipH = lines.Length * 16 + 14;

                if (x + tooltipW > canvasW - 10) x = canvasW - tooltipW - 10;
                if (y + tooltipH > canvasH - 10) y = canvasH - tooltipH - 10;
                if (x < 5) x = 5;
                if (y < 5) y = 5;

                var tooltipRect = new SKRect(x, y, x + tooltipW, y + tooltipH);
                var tooltipRoundRect = new SKRoundRect(tooltipRect, 6, 6);

                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6, 6), _tooltipShadowPaint);

                _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight : s_tooltipBgDark;
                canvas.DrawRoundRect(tooltipRoundRect, _tooltipBgPaint);

                _tooltipBorderPaint.Color = accentColor;
                canvas.DrawRoundRect(tooltipRoundRect, _tooltipBorderPaint);

                canvas.Save();
                canvas.ClipRoundRect(tooltipRoundRect);
                _tooltipAccentPaint.Color = accentColor;
                canvas.DrawRect(new SKRect(x, y, x + accentBarWidth, y + tooltipH), _tooltipAccentPaint);
                canvas.Restore();

                float ty = y + 16;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, x + accentBarWidth + 8, ty, textFont, _tooltipTextPaint);
                    ty += 16;
                }
            }
        }

        private double TimeToX(double sec, double w, double total) =>
            ((sec - ViewOffset) / total) * w * ViewScale;

        private double XToSeconds(double x, double w, double total) =>
            (x / (w * ViewScale)) * total + ViewOffset;
    }
}
