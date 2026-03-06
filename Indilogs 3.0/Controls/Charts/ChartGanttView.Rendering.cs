using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGanttView
    {
        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_totalDataLength == 0 || _stateDataList.Count == 0) return;

            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartWidth = chartRight - chartLeft;
            float chartBottom = h - X_AXIS_HEIGHT;

            int start = Math.Max(0, _viewStartIndex);
            int end = Math.Min(_totalDataLength - 1, _viewEndIndex);
            int count = end - start + 1;
            if (count <= 1 || chartWidth <= 0) return;

            float rowIndex = 0;
            float hoverX = (float)_hoverPos.X;
            float hoverY = (float)_hoverPos.Y;

            // Clip drawing to chart area for state bars
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, 0, chartRight, chartBottom));

            foreach (var stateData in _stateDataList)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT) - _verticalOffset;
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);

                if (rowBottom < 0 || rowTop > chartBottom) { rowIndex++; continue; }

                float barTop = rowTop + 2;
                float barBottom = rowBottom - 2;
                float barHeight = barBottom - barTop;

                if ((int)rowIndex % 2 == 1)
                {
                    _rowBgPaint.Color = _gridColor.WithAlpha(30);
                    canvas.DrawRect(new SKRect(chartLeft, rowTop, chartRight, rowBottom), _rowBgPaint);
                }

                if (stateData.Intervals != null)
                {
                    foreach (var interval in stateData.Intervals)
                    {
                        if (interval.EndIndex < start || interval.StartIndex > end) continue;

                        float x1 = chartLeft + (float)((Math.Max(interval.StartIndex, start) - start) / (double)count * chartWidth);
                        float x2 = chartLeft + (float)((Math.Min(interval.EndIndex, end) - start + 1) / (double)count * chartWidth);
                        if (x2 - x1 < 0.5f) x2 = x1 + 0.5f;

                        SKColor baseColor = CHStepColors[Math.Abs(interval.StateId) % CHStepColors.Length];

                        bool isHovered = _hoveredStateRow == (int)rowIndex &&
                                         _hoveredStateInterval.HasValue &&
                                         _hoveredStateInterval.Value.StartIndex == interval.StartIndex;

                        var barRect = new SKRect(x1, barTop, x2, barBottom);
                        var barRoundRect = new SKRoundRect(barRect, 3, 3);

                        SKColor topColor = isHovered ? baseColor : SkiaColorHelpers.LightenColor(baseColor, 0.15f);
                        SKColor bottomColor = isHovered ? SkiaColorHelpers.DarkenColor(baseColor, 0.1f) : SkiaColorHelpers.DarkenColor(baseColor, 0.2f);

                        _gradientPaint.Shader?.Dispose();
                        _gradientPaint.Shader = SKShader.CreateLinearGradient(
                            new SKPoint(0, barTop), new SKPoint(0, barBottom),
                            new[] { topColor, bottomColor }, null, SKShaderTileMode.Clamp);
                        canvas.DrawRoundRect(barRoundRect, _gradientPaint);

                        if (barHeight > 8)
                        {
                            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x1 + 1, barTop + 1, x2 - 1, barTop + barHeight * 0.4f), 2, 2), _glowPaint);
                        }

                        _edgePaint.Color = SkiaColorHelpers.DarkenColor(baseColor, 0.35f);
                        canvas.DrawRoundRect(barRoundRect, _edgePaint);

                        if (isHovered)
                        {
                            canvas.DrawRoundRect(barRoundRect, _highlightBorderPaint);
                        }

                        float barW = x2 - x1;
                        string stateLabel = !string.IsNullOrEmpty(interval.StateName)
                            ? interval.StateName : interval.StateId.ToString();
                        float textWidth = _intervalLabelFont.MeasureText(stateLabel);
                        if (textWidth < barW - 6 && barHeight > 10)
                        {
                            float brightness = (baseColor.Red * 0.299f + baseColor.Green * 0.587f + baseColor.Blue * 0.114f) / 255f;
                            _intervalLabelPaint.Color = brightness > 0.55f ? s_darkTextColor : SKColors.White;
                            float textX = x1 + (barW - textWidth) / 2;
                            float textY = barTop + barHeight / 2 + 3.5f;
                            canvas.DrawText(stateLabel, textX, textY, _intervalLabelFont, _intervalLabelPaint);
                        }
                    }
                }

                rowIndex++;
            }

            canvas.Restore();

            // Draw row labels and grid lines
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, w, chartBottom));
            rowIndex = 0;
            _labelPaint.Color = _textColor;
            _labelFont.Typeface = s_segoeNormal;
            foreach (var stateData in _stateDataList)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT) - _verticalOffset;
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);

                if (rowBottom < 0 || rowTop > chartBottom) { rowIndex++; continue; }

                bool isLabelHovered = _hoveredLabelRow == (int)rowIndex;

                string label = !string.IsNullOrEmpty(stateData.Category)
                    ? $"{stateData.Category}>{stateData.Name}" : stateData.Name;
                if (label.Length > 14) label = label.Substring(0, 14) + "..";

                if (isLabelHovered)
                {
                    _labelPaint.Color = s_accentBlue;
                    _labelFont.Typeface = s_segoeBold;
                }
                else
                {
                    _labelPaint.Color = _textColor;
                    _labelFont.Typeface = s_segoeNormal;
                }

                canvas.DrawText(label, 5, rowTop + ROW_HEIGHT / 2 + 4, _labelFont, _labelPaint);
                canvas.DrawLine(chartLeft, rowBottom + PADDING / 2, chartRight, rowBottom + PADDING / 2, _gridPaint);

                rowIndex++;
            }
            canvas.Restore();

            // Draw full name tooltip when hovering over left label area
            if (_hoveredLabelRow >= 0 && _hoveredLabelRow < _stateDataList.Count)
            {
                var hoveredData = _stateDataList[_hoveredLabelRow];
                string fullName = hoveredData.Name;
                if (!string.IsNullOrEmpty(hoveredData.Category))
                    fullName = $"{hoveredData.Category} > {hoveredData.Name}";

                _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight : s_tooltipBgDark;
                _labelTooltipTextPaint.Color = _isLightTheme ? s_darkTextColor : SKColors.White;

                float textW = _labelTooltipTextFont.MeasureText(fullName);
                float ttW = textW + 16;
                float ttH = 22;
                float ttX = (float)_hoverPos.X + 10;
                float ttY = (float)_hoverPos.Y - ttH - 5;
                if (ttY < 2) ttY = (float)_hoverPos.Y + 15;
                if (ttX + ttW > w) ttX = w - ttW - 5;

                canvas.DrawRoundRect(new SKRoundRect(new SKRect(ttX, ttY, ttX + ttW, ttY + ttH), 4), _tooltipBgPaint);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(ttX, ttY, ttX + ttW, ttY + ttH), 4), _labelTooltipBorderPaint);
                canvas.DrawText(fullName, ttX + 8, ttY + 15, _labelTooltipTextFont, _labelTooltipTextPaint);
            }

            // Draw CHSTEP hover tooltip
            if (_hoveredStateInterval.HasValue && _hoveredStateRow >= 0 && _hoveredStateRow < _stateDataList.Count)
            {
                var hoveredData = _stateDataList[_hoveredStateRow];
                var hInterval = _hoveredStateInterval.Value;

                var tooltipSb = new StringBuilder();

                string compName = !string.IsNullOrEmpty(hoveredData.Category)
                    ? $"{hoveredData.Category} > {hoveredData.Name}" : hoveredData.Name;
                tooltipSb.AppendLine(compName);

                if (!string.IsNullOrEmpty(hInterval.TooltipText))
                {
                    tooltipSb.AppendLine(hInterval.TooltipText);
                }
                else
                {
                    tooltipSb.AppendLine($"State: {hInterval.StateId}");
                    if (!string.IsNullOrEmpty(hInterval.StateName))
                        tooltipSb.AppendLine($"Step: {hInterval.StateName}");

                    string? startTime = GetXAxisLabel?.Invoke(hInterval.StartIndex);
                    string? endTime = GetXAxisLabel?.Invoke(hInterval.EndIndex);
                    if (!string.IsNullOrEmpty(startTime) && !string.IsNullOrEmpty(endTime))
                        tooltipSb.AppendLine($"{startTime} - {endTime}");
                }

                if (_hoverDataIndex >= 0 && GetXAxisLabel != null)
                {
                    string cursorTime = GetXAxisLabel(_hoverDataIndex);
                    if (!string.IsNullOrEmpty(cursorTime))
                        tooltipSb.AppendLine($"Time: {cursorTime}");
                }

                DrawCHStepTooltip(canvas, tooltipSb.ToString(), hoverX + 15, hoverY - 20, w, h);
            }

            // Event Markers
            _hoveredEventIndex = -1;
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                float eventY = chartBottom - 8;

                foreach (var evt in _chartEventMarkers)
                {
                    if (evt.Index < start || evt.Index > end) continue;

                    float ex = chartLeft + (float)((evt.Index - start) / (double)count * chartWidth);

                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint);
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint);

                    {
                        float dx = (float)_hoverPos.X - ex;
                        float dy = (float)_hoverPos.Y - eventY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist < EVENT_DOT_RADIUS * 4)
                            _hoveredEventIndex = evt.Index;
                    }
                }

                if (_hoveredEventIndex >= 0)
                {
                    var hoveredEvent = _chartEventMarkers.FirstOrDefault(ev => ev.Index == _hoveredEventIndex);
                    if (hoveredEvent != null)
                    {
                        float hx = chartLeft + (float)((hoveredEvent.Index - start) / (double)count * chartWidth);

                        canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, _eventHighlightPaint);

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

                        DrawEventTooltip(canvas, sb.ToString(), hx + 15, eventY - 40);
                    }
                }
            }

            // Draw X-axis with time labels
            DrawXAxis(canvas, chartLeft, chartRight, chartBottom, h, start, end, count);

            // Draw cursor line with subtle glow
            if (!HasOwnTimeline && _cursorIndex >= start && _cursorIndex <= end)
            {
                float cursorX = chartLeft + (float)((_cursorIndex - start) / (double)count * chartWidth);
                canvas.DrawLine(cursorX, 0, cursorX, chartBottom, _cursorGlowPaint);
                canvas.DrawLine(cursorX, 0, cursorX, chartBottom, _cursorPaint);
            }

            // Draw border
            canvas.DrawRect(new SKRect(chartLeft, 0, chartRight, chartBottom), _borderPaint);

            // Draw scroll indicator for scrollable independent-timeline charts
            if (HasOwnTimeline)
            {
                float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                if (contentHeight > chartBottom)
                {
                    float trackX = w - 6;
                    float trackH = chartBottom - 4;
                    float thumbRatio = chartBottom / contentHeight;
                    float thumbH = Math.Max(20, trackH * thumbRatio);
                    float maxOffset = contentHeight - chartBottom;
                    float scrollRatio = maxOffset > 0 ? _verticalOffset / maxOffset : 0;
                    float thumbY = 2 + scrollRatio * (trackH - thumbH);

                    using (var trackPaint = new SKPaint { Color = _gridColor.WithAlpha(40), Style = SKPaintStyle.Fill })
                        canvas.DrawRoundRect(new SKRoundRect(new SKRect(trackX, 2, trackX + 4, 2 + trackH), 2), trackPaint);
                    using (var thumbPaint = new SKPaint { Color = _textColor.WithAlpha(100), Style = SKPaintStyle.Fill })
                        canvas.DrawRoundRect(new SKRoundRect(new SKRect(trackX, thumbY, trackX + 4, thumbY + thumbH), 2), thumbPaint);
                }
            }
        }

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
