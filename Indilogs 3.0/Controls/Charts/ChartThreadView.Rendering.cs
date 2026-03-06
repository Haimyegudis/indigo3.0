using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartThreadView
    {
        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_totalDataLength == 0 || _threadNames.Count == 0) return;

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

            // Clear hit areas for tooltip detection
            _messageHitAreas.Clear();

            float rowIndex = 0;

            foreach (var threadName in _threadNames)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT);
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);
                float rowCenter = (rowTop + rowBottom) / 2;

                // Get thread color
                SKColor threadColor = ThreadColors[(int)rowIndex % ThreadColors.Length];

                // Draw thread name label - truncate to fit LEFT_MARGIN
                string label = threadName;
                if (label.Length > 7) label = label.Substring(0, 7) + "..";
                canvas.DrawText(label, 5, rowCenter + 4, _textFont!, _textPaint!);

                // Draw horizontal grid line
                canvas.DrawLine(chartLeft, rowBottom + PADDING / 2, chartRight, rowBottom + PADDING / 2, _gridPaint!);

                // Draw horizontal center line for this row
                _centerLinePaint.Color = threadColor.WithAlpha(80);
                canvas.DrawLine(chartLeft, rowCenter, chartRight, rowCenter, _centerLinePaint);

                // Draw messages for this thread
                if (_threadGroups.TryGetValue(threadName, out var messages))
                {
                    var visibleMessages = messages.Where(m => m.TimeIndex >= start && m.TimeIndex <= end).ToList();

                    _markerPaint.Color = threadColor;
                    _linePaint.Color = threadColor.WithAlpha(150);

                    foreach (var msg in visibleMessages)
                    {
                        float x = chartLeft + (float)((msg.TimeIndex - start) / (double)count * chartWidth);

                        // Draw vertical line marker (thin)
                        canvas.DrawLine(x, rowTop + 2, x, rowBottom - 2, _linePaint);

                        // Get first character of message for label (like INDICHARTSUIT)
                        string msgLabel = GetMessageLabel(msg.Message);

                        // Draw label background (small rectangle)
                        float labelWidth = _labelFont.MeasureText(msgLabel);
                        float rectWidth = Math.Max(labelWidth + 4, 14);
                        float rectHeight = 12;
                        var labelRect = new SKRect(x - rectWidth / 2, rowCenter - rectHeight / 2, x + rectWidth / 2, rowCenter + rectHeight / 2);

                        canvas.DrawRoundRect(labelRect, 2, 2, _markerPaint);

                        // Draw label text centered
                        float textX = x - labelWidth / 2;
                        canvas.DrawText(msgLabel, textX, rowCenter + 3, _labelFont, _labelPaint);

                        // Store hit area for tooltip
                        _messageHitAreas.Add(new MessageHitArea
                        {
                            Message = msg,
                            X = x,
                            Top = rowTop,
                            Bottom = rowBottom,
                            Rect = labelRect
                        });
                    }
                }

                rowIndex++;
            }

            // Event Markers (Red Dots at bottom of Thread area)
            _hoveredEventDotIndex = -1;
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                float eventY = chartBottom - 8;

                foreach (var evt in _chartEventMarkers)
                {
                    if (evt.Index < start || evt.Index > end) continue;

                    float ex = chartLeft + (float)((evt.Index - start) / (double)count * chartWidth);

                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint!);
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint!);

                    // Hover detection
                    {
                        float dx = (float)_eventHoverPos.X - ex;
                        float dy = (float)_eventHoverPos.Y - eventY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist < EVENT_DOT_RADIUS * 4)
                            _hoveredEventDotIndex = evt.Index;
                    }
                }

                // Draw tooltip for hovered event
                if (_hoveredEventDotIndex >= 0)
                {
                    var hoveredEvent = _chartEventMarkers.FirstOrDefault(ev => ev.Index == _hoveredEventDotIndex);
                    if (hoveredEvent != null)
                    {
                        float hx = chartLeft + (float)((hoveredEvent.Index - start) / (double)count * chartWidth);

                        canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, _highlightPaint);

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

            // Draw cursor line
            if (_cursorIndex >= start && _cursorIndex <= end)
            {
                float cursorX = chartLeft + (float)((_cursorIndex - start) / (double)count * chartWidth);
                canvas.DrawLine(cursorX, 0, cursorX, chartBottom, _cursorPaint);
            }

            // Draw border
            canvas.DrawRect(new SKRect(chartLeft, 0, chartRight, chartBottom), _borderPaint!);
        }

        private string GetMessageLabel(string? message)
        {
            if (string.IsNullOrEmpty(message)) return "?";

            // Remove common prefixes and get first meaningful character
            message = message.Trim();

            // Look for patterns like "PlcMngr:" or similar
            int colonIdx = message.IndexOf(':');
            if (colonIdx > 0 && colonIdx < 20)
            {
                string afterColon = message.Substring(colonIdx + 1).Trim();
                if (afterColon.Length > 0)
                    return afterColon[0].ToString().ToUpper();
            }

            // Just use first character
            return message[0].ToString().ToUpper();
        }

        private void DrawEventTooltip(SKCanvas canvas, string text, float x, float y)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            float maxWidth = 0;
            foreach (var line in lines)
                maxWidth = Math.Max(maxWidth, _tooltipTextFont.MeasureText(line));

            float tooltipW = maxWidth + 12;
            float tooltipH = lines.Length * 15 + 8;

            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 4), _tooltipBgPaint);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 4), _tooltipBorderPaint);

            float ty = y + 14;
            foreach (var line in lines)
            {
                canvas.DrawText(line, x + 6, ty, _tooltipTextFont, _tooltipTextPaint);
                ty += 15;
            }
        }

        private void DrawXAxis(SKCanvas canvas, float chartLeft, float chartRight, float chartBottom, float totalHeight, int start, int end, int count)
        {
            float chartWidth = chartRight - chartLeft;

            // Draw X-axis line
            canvas.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, _borderPaint!);

            // Calculate how many labels to show (about 5-7 labels)
            int labelCount = 5;

            _axisPaint.Color = _textColor;

            for (int i = 0; i <= labelCount; i++)
            {
                int index = start + (int)((double)i / labelCount * count);
                if (index >= start && index <= end)
                {
                    float x = chartLeft + (float)((index - start) / (double)count * chartWidth);

                    // Draw tick
                    canvas.DrawLine(x, chartBottom, x, chartBottom + 4, _borderPaint!);

                    // Draw label
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
