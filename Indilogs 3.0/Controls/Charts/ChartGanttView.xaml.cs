using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGanttView : UserControl
    {
        public event Action<int> OnTimeClicked;
        public event Action<int, int> OnStateClicked;
        public event Action<int, int> OnViewRangeChanged;
        public event Action<int> OnCursorMoved;

        private List<StateData> _stateDataList = new List<StateData>();
        private int _totalDataLength = 0;
        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _cursorIndex = -1;
        private bool _isExpanded = true;
        private bool _isLightTheme = false;

        // For X-axis labels
        public Func<int, string> GetXAxisLabel { get; set; }

        // Row height for each CH
        private const float ROW_HEIGHT = 24f;
        private const float LEFT_MARGIN = 60f;   // Match ChartGraphView for alignment
        private const float RIGHT_MARGIN = 55f;  // Match ChartGraphView for alignment
        private const float PADDING = 2f;
        private const float X_AXIS_HEIGHT = 20f;

        // Theme colors
        private SKColor _bgColor;
        private SKColor _borderColor;
        private SKColor _textColor;
        private SKColor _gridColor;
        private static readonly SKColor CursorColor = SKColors.Red;

        // State colors for CHSTEP (different from machine state colors)
        private static readonly SKColor[] CHStepColors = new[]
        {
            SKColor.Parse("#4ECDC4"), // Teal
            SKColor.Parse("#FF6B6B"), // Red
            SKColor.Parse("#95E1D3"), // Mint
            SKColor.Parse("#FFE66D"), // Yellow
            SKColor.Parse("#AA96DA"), // Lavender
            SKColor.Parse("#F38181"), // Coral
            SKColor.Parse("#6C5CE7"), // Purple
            SKColor.Parse("#00B894"), // Green
            SKColor.Parse("#FDCB6E"), // Gold
            SKColor.Parse("#E17055"), // Orange
        };

        private SKPaint _borderPaint;
        private SKPaint _textPaint;
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private SKPaint _gridPaint;

        // Drag/Pan support
        private bool _isDragging = false;
        private Point _lastMousePos;

        // Event marker support
        private List<EventMarker> _chartEventMarkers;
        private SKPaint _eventDotPaint;
        private SKPaint _eventDotBorderPaint;
        private const float EVENT_DOT_RADIUS = 5f;
        private int _hoveredEventIndex = -1;
        private Point _hoverPos;

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

        public ChartGanttView()
        {
            InitializeComponent();
            UpdateThemeColors();
        }

        public void SetStates(List<StateData> stateDataList, int totalDataLength)
        {
            _stateDataList = stateDataList ?? new List<StateData>();
            _totalDataLength = totalDataLength;

            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }

            // Update height based on number of CH rows
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
            if (_isExpanded && _stateDataList.Count > 0)
            {
                GanttContainer.Height = (_stateDataList.Count * ROW_HEIGHT) + X_AXIS_HEIGHT + 10;
            }
            else
            {
                GanttContainer.Height = _isExpanded ? 50 : 0;
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

        private void ExpandCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                _isExpanded = btn.IsChecked == true;
                btn.Content = _isExpanded ? "▼" : "▶";
                UpdateHeight();
            }
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

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
            CaptureMouse();

            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _isDragging = false;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(SkiaCanvas);
            _hoverPos = pos;

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
            }
            else if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                SkiaCanvas.InvalidateVisual();
            }
        }

        protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);
            float w = (float)SkiaCanvas.ActualWidth;

            // Check if click is in chart area (past label)
            if (pos.X < LEFT_MARGIN) return;

            // Calculate zoom center based on mouse position
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

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_totalDataLength == 0 || _stateDataList.Count == 0 || !_isExpanded) return;

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

            foreach (var stateData in _stateDataList)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT);
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);

                // Draw row label (CH name) - truncate to fit LEFT_MARGIN
                string label = stateData.Name;
                if (label.Length > 7) label = label.Substring(0, 7) + "..";
                canvas.DrawText(label, 5, rowTop + ROW_HEIGHT / 2 + 4, _textPaint);

                // Draw horizontal grid line
                canvas.DrawLine(chartLeft, rowBottom + PADDING / 2, chartRight, rowBottom + PADDING / 2, _gridPaint);

                // Draw state intervals for this CH
                if (stateData.Intervals != null)
                {
                    foreach (var interval in stateData.Intervals)
                    {
                        if (interval.EndIndex < start || interval.StartIndex > end) continue;

                        float x1 = chartLeft + (float)((Math.Max(interval.StartIndex, start) - start) / (double)count * chartWidth);
                        float x2 = chartLeft + (float)((Math.Min(interval.EndIndex, end) - start + 1) / (double)count * chartWidth);

                        // Get color based on state ID
                        SKColor color = CHStepColors[Math.Abs(interval.StateId) % CHStepColors.Length];

                        using (var fillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill })
                        {
                            canvas.DrawRect(new SKRect(x1, rowTop, x2, rowBottom), fillPaint);
                        }

                        // Draw state label if there's room
                        string stateLabel = interval.StateId.ToString();
                        float textWidth = _textPaint.MeasureText(stateLabel);
                        if (textWidth < (x2 - x1) - 4)
                        {
                            using (var tp = new SKPaint { Color = SKColors.Black, TextSize = 9, IsAntialias = true })
                            {
                                float textX = x1 + ((x2 - x1) - textWidth) / 2;
                                canvas.DrawText(stateLabel, textX, rowTop + ROW_HEIGHT / 2 + 3, tp);
                            }
                        }
                    }
                }

                rowIndex++;
            }

            // Event Markers (Red Dots at bottom of Gantt area)
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

                    // Hover detection
                    {
                        float dx = (float)_hoverPos.X - ex;
                        float dy = (float)_hoverPos.Y - eventY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist < EVENT_DOT_RADIUS * 4)
                            _hoveredEventIndex = evt.Index;
                    }
                }

                // Draw tooltip for hovered event
                if (_hoveredEventIndex >= 0)
                {
                    var hoveredEvent = _chartEventMarkers.FirstOrDefault(ev => ev.Index == _hoveredEventIndex);
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
            int step = Math.Max(1, count / labelCount);

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
    }
}
