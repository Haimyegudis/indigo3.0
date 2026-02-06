using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartThreadView : UserControl
    {
        public event Action<int> OnTimeClicked;
        public event Action<int, int> OnViewRangeChanged;
        public event Action<int> OnCursorMoved;

        // Support for multiple threads (like INDICHARTSUIT)
        private Dictionary<string, List<ThreadMessageData>> _threadGroups = new Dictionary<string, List<ThreadMessageData>>();
        private List<string> _threadNames = new List<string>();
        private int _totalDataLength = 0;
        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _cursorIndex = -1;
        private bool _isLightTheme = false;

        // Mouse tracking for tooltip
        private int _hoveredMessageIndex = -1;
        private List<MessageHitArea> _messageHitAreas = new List<MessageHitArea>();

        // Drag/Pan support
        private bool _isDragging = false;
        private Point _lastMousePos;

        // For X-axis labels
        public Func<int, string> GetXAxisLabel { get; set; }

        // Layout constants - match ChartGraphView for perfect alignment
        private const float ROW_HEIGHT = 24f;
        private const float LEFT_MARGIN = 60f;   // Match ChartGraphView
        private const float RIGHT_MARGIN = 55f;  // Match ChartGraphView
        private const float PADDING = 2f;
        private const float X_AXIS_HEIGHT = 20f;

        // Theme colors
        private SKColor _bgColor;
        private SKColor _borderColor;
        private SKColor _textColor;
        private SKColor _gridColor;
        private static readonly SKColor CursorColor = SKColors.Red;

        // Thread colors (different colors for different threads)
        private static readonly SKColor[] ThreadColors = new[]
        {
            SKColor.Parse("#9C27B0"), // Purple
            SKColor.Parse("#2196F3"), // Blue
            SKColor.Parse("#4CAF50"), // Green
            SKColor.Parse("#FF9800"), // Orange
            SKColor.Parse("#E91E63"), // Pink
            SKColor.Parse("#00BCD4"), // Cyan
            SKColor.Parse("#795548"), // Brown
            SKColor.Parse("#607D8B"), // Blue Gray
        };

        private SKPaint _borderPaint;
        private SKPaint _textPaint;
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private SKPaint _gridPaint;

        // Event marker support
        private List<EventMarker> _chartEventMarkers;
        private SKPaint _eventDotPaint;
        private SKPaint _eventDotBorderPaint;
        private const float EVENT_DOT_RADIUS = 5f;
        private int _hoveredEventDotIndex = -1;
        private Point _eventHoverPos;

        public bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                _isLightTheme = value;
                UpdateThemeColors();
                SkiaCanvas.InvalidateVisual();
            }
        }

        private void UpdateThemeColors()
        {
            if (_isLightTheme)
            {
                _bgColor = SKColor.Parse("#FFFFFF");
                _borderColor = SKColor.Parse("#DDDDDD");
                _textColor = SKColor.Parse("#333333");
                _gridColor = SKColor.Parse("#E0E0E0");
            }
            else
            {
                _bgColor = SKColor.Parse("#0D1B2A");
                _borderColor = SKColor.Parse("#2D4A6F");
                _textColor = SKColors.White;
                _gridColor = SKColor.Parse("#1B3A5C");
            }

            _borderPaint = new SKPaint { Color = _borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            _textPaint = new SKPaint { Color = _textColor, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) };
            _gridPaint = new SKPaint { Color = _gridColor, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            _eventDotPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
            _eventDotBorderPaint = new SKPaint { Color = SKColors.DarkRed, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        }

        public ChartThreadView()
        {
            InitializeComponent();
            UpdateThemeColors();
        }

        /// <summary>
        /// Sets data for a single thread (legacy compatibility)
        /// </summary>
        public void SetThreadData(string threadName, List<ThreadMessageData> messages, int totalDataLength)
        {
            _threadGroups.Clear();
            _threadNames.Clear();

            if (!string.IsNullOrEmpty(threadName) && messages != null && messages.Count > 0)
            {
                _threadGroups[threadName] = messages;
                _threadNames.Add(threadName);
            }

            _totalDataLength = totalDataLength;

            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }

            UpdateHeight();
            SkiaCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Sets data for multiple threads (INDICHARTSUIT style - multiple rows)
        /// </summary>
        public void SetMultipleThreadData(Dictionary<string, List<ThreadMessageData>> threadGroups, int totalDataLength)
        {
            _threadGroups = threadGroups ?? new Dictionary<string, List<ThreadMessageData>>();
            _threadNames = _threadGroups.Keys.OrderBy(k => k).ToList();
            _totalDataLength = totalDataLength;

            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }

            UpdateHeight();
            SkiaCanvas.InvalidateVisual();
        }

        public void SetEventMarkers(List<EventMarker> markers)
        {
            _chartEventMarkers = markers;
            SkiaCanvas.InvalidateVisual();
        }

        private void UpdateHeight()
        {
            if (_threadNames.Count > 0)
            {
                // Height = (number of threads * row height) + X-axis height + padding
                this.Height = (_threadNames.Count * ROW_HEIGHT) + X_AXIS_HEIGHT + 10;
            }
            else
            {
                this.Height = 50;
            }
        }

        public void SyncViewRange(int start, int end)
        {
            _viewStartIndex = start;
            _viewEndIndex = end;
            SkiaCanvas.InvalidateVisual();
        }

        public void SyncCursor(int index)
        {
            _cursorIndex = index;
            SkiaCanvas.InvalidateVisual();
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
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
                canvas.DrawText(label, 5, rowCenter + 4, _textPaint);

                // Draw horizontal grid line
                canvas.DrawLine(chartLeft, rowBottom + PADDING / 2, chartRight, rowBottom + PADDING / 2, _gridPaint);

                // Draw horizontal center line for this row
                using (var centerLinePaint = new SKPaint { Color = threadColor.WithAlpha(80), StrokeWidth = 1, Style = SKPaintStyle.Stroke })
                {
                    canvas.DrawLine(chartLeft, rowCenter, chartRight, rowCenter, centerLinePaint);
                }

                // Draw messages for this thread
                if (_threadGroups.TryGetValue(threadName, out var messages))
                {
                    var visibleMessages = messages.Where(m => m.TimeIndex >= start && m.TimeIndex <= end).ToList();

                    using (var markerPaint = new SKPaint { Color = threadColor, Style = SKPaintStyle.Fill })
                    using (var labelPaint = new SKPaint { Color = SKColors.White, TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) })
                    {
                        foreach (var msg in visibleMessages)
                        {
                            float x = chartLeft + (float)((msg.TimeIndex - start) / (double)count * chartWidth);

                            // Draw vertical line marker (thin)
                            using (var linePaint = new SKPaint { Color = threadColor.WithAlpha(150), StrokeWidth = 1, Style = SKPaintStyle.Stroke })
                            {
                                canvas.DrawLine(x, rowTop + 2, x, rowBottom - 2, linePaint);
                            }

                            // Get first character of message for label (like INDICHARTSUIT)
                            string msgLabel = GetMessageLabel(msg.Message);

                            // Draw label background (small rectangle)
                            float labelWidth = labelPaint.MeasureText(msgLabel);
                            float rectWidth = Math.Max(labelWidth + 4, 14);
                            float rectHeight = 12;
                            var labelRect = new SKRect(x - rectWidth / 2, rowCenter - rectHeight / 2, x + rectWidth / 2, rowCenter + rectHeight / 2);

                            canvas.DrawRoundRect(labelRect, 2, 2, markerPaint);

                            // Draw label text centered
                            float textX = x - labelWidth / 2;
                            canvas.DrawText(msgLabel, textX, rowCenter + 3, labelPaint);

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

                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint);
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint);

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

                        using (var highlightPaint = new SKPaint { Color = SKColors.Red.WithAlpha(60), Style = SKPaintStyle.Fill, IsAntialias = true })
                        {
                            canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, highlightPaint);
                        }

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
            canvas.DrawRect(new SKRect(chartLeft, 0, chartRight, chartBottom), _borderPaint);
        }

        /// <summary>
        /// Gets a short label for the message (first character or abbreviated)
        /// </summary>
        private string GetMessageLabel(string message)
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
            using (var bgPaint = new SKPaint { Color = SKColor.Parse("#DD1B2838"), Style = SKPaintStyle.Fill })
            using (var borderPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            using (var textPaint = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Consolas") })
            {
                float maxWidth = 0;
                foreach (var line in lines)
                    maxWidth = Math.Max(maxWidth, textPaint.MeasureText(line));

                float tooltipW = maxWidth + 12;
                float tooltipH = lines.Length * 15 + 8;

                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 4), bgPaint);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 4), borderPaint);

                float ty = y + 14;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, x + 6, ty, textPaint);
                    ty += 15;
                }
            }
        }

        private void DrawXAxis(SKCanvas canvas, float chartLeft, float chartRight, float chartBottom, float totalHeight, int start, int end, int count)
        {
            float chartWidth = chartRight - chartLeft;

            // Draw X-axis line
            canvas.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, _borderPaint);

            // Calculate how many labels to show (about 5-7 labels)
            int labelCount = 5;

            using (var axisPaint = new SKPaint { Color = _textColor, TextSize = 9, IsAntialias = true })
            {
                for (int i = 0; i <= labelCount; i++)
                {
                    int index = start + (int)((double)i / labelCount * count);
                    if (index >= start && index <= end)
                    {
                        float x = chartLeft + (float)((index - start) / (double)count * chartWidth);

                        // Draw tick
                        canvas.DrawLine(x, chartBottom, x, chartBottom + 4, _borderPaint);

                        // Draw label
                        string label = GetXAxisLabel?.Invoke(index) ?? index.ToString();
                        float textWidth = axisPaint.MeasureText(label);
                        float textX = x - textWidth / 2;
                        textX = Math.Max(chartLeft, Math.Min(textX, chartRight - textWidth));
                        canvas.DrawText(label, textX, chartBottom + 14, axisPaint);
                    }
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_threadNames.Count == 0 && !_isDragging) return;

            var pos = e.GetPosition(SkiaCanvas);
            _eventHoverPos = pos;

            // Handle drag/pan
            if (_isDragging && _totalDataLength > 0)
            {
                float w = (float)SkiaCanvas.ActualWidth;
                float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
                if (chartWidth <= 0) return;

                double deltaX = pos.X - _lastMousePos.X;
                int visiblePoints = _viewEndIndex - _viewStartIndex;
                int shift = (int)((deltaX / chartWidth) * visiblePoints);

                if (shift != 0)
                {
                    int newStart = _viewStartIndex - shift;
                    int newEnd = _viewEndIndex - shift;

                    if (newStart < 0) { newStart = 0; newEnd = visiblePoints; }
                    if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - visiblePoints; }

                    if (newStart != _viewStartIndex)
                    {
                        _viewStartIndex = newStart;
                        _viewEndIndex = newEnd;
                        _lastMousePos = pos;
                        OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                        SkiaCanvas.InvalidateVisual();
                    }
                }
                return;
            }

            // Get DPI scale factor for proper coordinate mapping
            var source = PresentationSource.FromVisual(this);
            double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            // Scale mouse coordinates to match SkiaSharp's pixel coordinates
            float mouseX = (float)(pos.X * dpiScale);
            float mouseY = (float)(pos.Y * dpiScale);

            // Find message at mouse position
            int hoveredIndex = -1;
            const float hitRadius = 20f; // Increased hit radius for better detection

            for (int i = 0; i < _messageHitAreas.Count; i++)
            {
                var area = _messageHitAreas[i];
                // Check if mouse is within the label rectangle or close to it
                bool inRect = area.Rect.Contains(mouseX, mouseY);
                bool nearMarker = Math.Abs(mouseX - area.X) < hitRadius && mouseY >= area.Top && mouseY <= area.Bottom;

                if (inRect || nearMarker)
                {
                    hoveredIndex = i;
                    break;
                }
            }

            if (hoveredIndex != _hoveredMessageIndex)
            {
                _hoveredMessageIndex = hoveredIndex;

                if (hoveredIndex >= 0 && hoveredIndex < _messageHitAreas.Count)
                {
                    var msg = _messageHitAreas[hoveredIndex].Message;
                    ShowTooltip(msg);
                }
                else
                {
                    HideTooltip();
                }
            }

            // Always repaint when events exist for event dot hover detection
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                SkiaCanvas.InvalidateVisual();
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _hoveredMessageIndex = -1;
            HideTooltip();
        }

        private void ShowTooltip(ThreadMessageData msg)
        {
            TooltipThread.Text = msg.ThreadName;
            TooltipTime.Text = msg.TimeStamp.ToString("HH:mm:ss.fff");
            TooltipMessage.Text = msg.Message;
            MessageTooltip.IsOpen = true;
        }

        private void HideTooltip()
        {
            MessageTooltip.IsOpen = false;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            // Check if click is in chart area (past label)
            if (pos.X < LEFT_MARGIN) return;

            float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
            double ratio = (pos.X - LEFT_MARGIN) / chartWidth;
            int count = _viewEndIndex - _viewStartIndex + 1;
            int clickedIndex = _viewStartIndex + (int)(ratio * count);
            clickedIndex = Math.Max(0, Math.Min(clickedIndex, _totalDataLength - 1));

            _cursorIndex = clickedIndex;
            OnTimeClicked?.Invoke(clickedIndex);
            OnCursorMoved?.Invoke(clickedIndex);

            // Start dragging for pan
            _isDragging = true;
            _lastMousePos = pos;
            SkiaCanvas.CaptureMouse();

            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            SkiaCanvas.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            if (pos.X < LEFT_MARGIN) return;

            // Calculate zoom center
            float chartWidth = w - LEFT_MARGIN - RIGHT_MARGIN;
            double ratio = (pos.X - LEFT_MARGIN) / chartWidth;
            int count = _viewEndIndex - _viewStartIndex + 1;
            int centerIndex = _viewStartIndex + (int)(ratio * count);

            // Zoom factor
            double zoomFactor = e.Delta > 0 ? 0.8 : 1.25;
            int newCount = (int)(count * zoomFactor);
            newCount = Math.Max(10, Math.Min(newCount, _totalDataLength));

            // Calculate new range centered on mouse position
            int newStart = centerIndex - (int)(ratio * newCount);
            int newEnd = newStart + newCount - 1;

            // Clamp to valid range
            if (newStart < 0) { newStart = 0; newEnd = newCount - 1; }
            if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - newCount + 1; }
            if (newStart < 0) newStart = 0;

            _viewStartIndex = newStart;
            _viewEndIndex = newEnd;

            OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
            SkiaCanvas.InvalidateVisual();

            e.Handled = true;
        }

        private class MessageHitArea
        {
            public ThreadMessageData Message;
            public float X;
            public float Top;
            public float Bottom;
            public SKRect Rect;
        }
    }
}
