#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
        // Cached SKTypeface instances — avoid re-creating on every render/tooltip
        private static readonly SKTypeface s_segoeNormal = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        private static readonly SKTypeface s_segoeBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        private static readonly SKTypeface s_consolas = SKTypeface.FromFamilyName("Consolas");

        public event Action<int> OnTimeClicked;
        public event Action<int, int> OnViewRangeChanged;
        public event Action<int> OnCursorMoved;

        private List<StateData> _stateDataList = new List<StateData>();
        private int _totalDataLength = 0;
        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _cursorIndex = -1;
        private bool _isLightTheme = false;

        // For X-axis labels
        public Func<int, string> GetXAxisLabel { get; set; }

        // Row height for each CH
        private const float ROW_HEIGHT = 24f;
        private const float LEFT_MARGIN = 100f;  // Wider to show Parent>Name labels
        private const float RIGHT_MARGIN = 55f;  // Match ChartGraphView for alignment
        private const float PADDING = 2f;
        private const float X_AXIS_HEIGHT = 20f;

        // Theme colors
        private SKColor _bgColor;
        private SKColor _borderColor;
        private SKColor _textColor;
        private SKColor _gridColor;
        private static readonly SKColor CursorColor = SKColors.Red;

        // Cached colors used in render path (OnPaintSurface / Draw* helpers)
        private static readonly SKColor s_darkTextColor = SKColor.Parse("#333333");
        private static readonly SKColor s_accentBlue = SKColor.Parse("#42A5F5");
        private static readonly SKColor s_tooltipBgLight = SKColor.Parse("#F0FFFFFF");
        private static readonly SKColor s_tooltipBgDark = SKColor.Parse("#F01B2838");
        private static readonly SKColor s_shadowColor = SKColor.Parse("#60000000");
        private static readonly SKColor s_tooltipBorderLight = SKColor.Parse("#CCCCCC");
        private static readonly SKColor s_tooltipBorderDark = SKColor.Parse("#4A6FA5");

        // State colors for CHSTEP (different from machine state colors) - richer professional palette
        private static readonly SKColor[] CHStepColors = new[]
        {
            SKColor.Parse("#26A69A"), // Teal
            SKColor.Parse("#EF5350"), // Red
            SKColor.Parse("#66BB6A"), // Green
            SKColor.Parse("#FFA726"), // Orange
            SKColor.Parse("#AB47BC"), // Purple
            SKColor.Parse("#42A5F5"), // Blue
            SKColor.Parse("#EC407A"), // Pink
            SKColor.Parse("#8D6E63"), // Brown
            SKColor.Parse("#78909C"), // Blue Grey
            SKColor.Parse("#D4E157"), // Lime
        };

        private SKPaint _borderPaint;
        private SKPaint _textPaint;
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private SKPaint _gridPaint;

        // ── Cached paints for hot render loop (interval drawing) ──
        private readonly SKPaint _rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _gradientPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _glowPaint = new SKPaint { Color = SKColors.White.WithAlpha(40), Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _edgePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        private readonly SKPaint _highlightBorderPaint = new SKPaint { Color = SKColors.White.WithAlpha(200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        private readonly SKPaint _intervalLabelPaint = new SKPaint { TextSize = 9, IsAntialias = true, Typeface = s_segoeBold };

        // ── Cached paints for labels, tooltips, cursors ──
        private readonly SKPaint _labelPaint = new SKPaint { TextSize = 10, IsAntialias = true };
        private readonly SKPaint _axisPaint = new SKPaint { TextSize = 9, IsAntialias = true };
        private readonly SKPaint _cursorGlowPaint = new SKPaint { Color = CursorColor.WithAlpha(40), StrokeWidth = 6, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _eventHighlightPaint = new SKPaint { Color = SKColors.Red.WithAlpha(60), Style = SKPaintStyle.Fill, IsAntialias = true };

        // Tooltip paints (shared between label tooltip, event tooltip, CHStep tooltip)
        private readonly SKPaint _tooltipShadowPaint = new SKPaint { Color = s_shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = s_consolas };
        private readonly SKPaint _tooltipAccentPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        // Label tooltip uses a separate text paint (Segoe Bold, size 11) and a separate border paint (accent blue)
        private readonly SKPaint _labelTooltipTextPaint = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = s_segoeBold };
        private readonly SKPaint _labelTooltipBorderPaint = new SKPaint { Color = s_accentBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };

        // Drag/Pan support
        private bool _isDragging = false;
        private bool _isSyncing = false;
        private Point _lastMousePos;

        // Event marker support
        private List<EventMarker> _chartEventMarkers;
        private SKPaint _eventDotPaint;
        private SKPaint _eventDotBorderPaint;
        private const float EVENT_DOT_RADIUS = 5f;
        private int _hoveredEventIndex = -1;
        private Point _hoverPos;

        // CHSTEP hover support
        private int _hoveredStateRow = -1;
        private StateInterval? _hoveredStateInterval = null;
        private int _hoverDataIndex = -1; // Data index under cursor (for dynamic time tooltip)

        // Label hover support (show full name on hover over left margin)
        private int _hoveredLabelRow = -1;

        // Independent-timeline support (e.g., EM Statistics Gantt)
        public bool HasOwnTimeline { get; set; }
        private float _verticalOffset = 0f;
        private const float MAX_VISIBLE_ROWS_HEIGHT = 450f;

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

            // Dispose previous paints before creating new ones
            _borderPaint?.Dispose();
            _textPaint?.Dispose();
            _gridPaint?.Dispose();
            _eventDotPaint?.Dispose();
            _eventDotBorderPaint?.Dispose();

            _borderPaint = new SKPaint { Color = _borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            _textPaint = new SKPaint { Color = _textColor, TextSize = 10, IsAntialias = true, Typeface = s_segoeNormal };
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
            if (_stateDataList.Count > 0)
            {
                if (HasOwnTimeline)
                {
                    // Height driven by ChartHeight binding — just clamp vertical offset
                    float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                    if (contentHeight > MAX_VISIBLE_ROWS_HEIGHT)
                    {
                        float maxOffset = contentHeight - MAX_VISIBLE_ROWS_HEIGHT;
                        _verticalOffset = Math.Min(_verticalOffset, maxOffset);
                    }
                    else
                        _verticalOffset = 0;
                }
                else
                {
                    GanttContainer.Height = (_stateDataList.Count * ROW_HEIGHT) + X_AXIS_HEIGHT + 10;
                    _verticalOffset = 0;
                }
            }
            else
            {
                GanttContainer.Height = 50;
                _verticalOffset = 0;
            }
        }

        public void SyncViewRange(int start, int end)
        {
            if (_totalDataLength == 0 || _isSyncing) return;
            _isSyncing = true;
            _viewStartIndex = start;
            _viewEndIndex = end;
            SkiaCanvas.InvalidateVisual();
            _isSyncing = false;
        }

        public void SyncCursor(int index)
        {
            if (HasOwnTimeline) return;
            _cursorIndex = index;
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(SkiaCanvas);

            if (HasOwnTimeline)
            {
                // Vertical drag for scrolling (no cursor for independent-timeline charts)
                _isDragging = true;
                _lastMousePos = pos;
                CaptureMouse();
                return;
            }

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
                if (HasOwnTimeline)
                {
                    // Vertical scroll via drag
                    float deltaY = (float)(pos.Y - _lastMousePos.Y);
                    float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                    float canvasH = (float)SkiaCanvas.ActualHeight;
                    float visibleRows = canvasH - X_AXIS_HEIGHT;
                    float maxOffset = Math.Max(0, contentHeight - visibleRows);
                    _verticalOffset = Math.Max(0, Math.Min(_verticalOffset - deltaY, maxOffset));
                    _lastMousePos = pos;
                    SkiaCanvas.InvalidateVisual();
                }
                else
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
                            if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                            SkiaCanvas.InvalidateVisual();
                        }
                    }
                }
            }
            else
            {
                // Pre-compute hover state (lightweight, no rendering)
                int oldRow = _hoveredStateRow;
                var oldInterval = _hoveredStateInterval;
                int oldLabelRow = _hoveredLabelRow;
                bool needsRepaint = false;

                float w2 = (float)SkiaCanvas.ActualWidth;
                float chartWidth2 = w2 - LEFT_MARGIN - RIGHT_MARGIN;

                // Check label hover (left margin area)
                int newLabelRow = -1;
                if (pos.X < LEFT_MARGIN && pos.X >= 0 && _stateDataList.Count > 0)
                {
                    int rowIdx = Math.Max(0, (int)((pos.Y + _verticalOffset - PADDING) / ROW_HEIGHT));
                    if (rowIdx >= 0 && rowIdx < _stateDataList.Count)
                        newLabelRow = rowIdx;
                }
                if (newLabelRow != oldLabelRow)
                {
                    _hoveredLabelRow = newLabelRow;
                    needsRepaint = true;
                }

                if (chartWidth2 > 0 && _totalDataLength > 0 && pos.X >= LEFT_MARGIN)
                {
                    int cnt = _viewEndIndex - _viewStartIndex + 1;
                    double ratio = (pos.X - LEFT_MARGIN) / chartWidth2;
                    int hoverIndex = _viewStartIndex + (int)(ratio * cnt);
                    _hoverDataIndex = Math.Max(0, Math.Min(hoverIndex, _totalDataLength - 1));
                    float rowH = ROW_HEIGHT;
                    int rowIdx = Math.Max(0, (int)((pos.Y + _verticalOffset - PADDING) / rowH));

                    int newRow = -1;
                    StateInterval? newInterval = null;

                    if (rowIdx >= 0 && rowIdx < _stateDataList.Count)
                    {
                        var intervals = _stateDataList[rowIdx].Intervals;
                        // Binary search for the interval containing hoverIndex
                        int lo = 0, hi = intervals.Count - 1;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            if (hoverIndex < intervals[mid].StartIndex)
                                hi = mid - 1;
                            else if (hoverIndex > intervals[mid].EndIndex)
                                lo = mid + 1;
                            else
                            {
                                newRow = rowIdx;
                                newInterval = intervals[mid];
                                break;
                            }
                        }
                    }

                    if (newRow != oldRow || !StateIntervalEquals(newInterval, oldInterval))
                    {
                        _hoveredStateRow = newRow;
                        _hoveredStateInterval = newInterval;
                        needsRepaint = true;
                    }
                    // Repaint when cursor moves along a hovered bar (dynamic time tooltip)
                    else if (newInterval.HasValue)
                    {
                        needsRepaint = true;
                    }
                }
                else if (oldRow >= 0 || oldInterval.HasValue)
                {
                    _hoveredStateRow = -1;
                    _hoveredStateInterval = null;
                    _hoverDataIndex = -1;
                    needsRepaint = true;
                }

                if (needsRepaint)
                    SkiaCanvas.InvalidateVisual();
            }
        }

        private static bool StateIntervalEquals(StateInterval? a, StateInterval? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            return a.Value.StartIndex == b.Value.StartIndex && a.Value.EndIndex == b.Value.EndIndex;
        }

        protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_totalDataLength == 0) return;

            if (HasOwnTimeline)
            {
                // Vertical scroll for independent-timeline charts
                float scrollAmount = e.Delta > 0 ? -40f : 40f;
                float contentHeight = _stateDataList.Count * ROW_HEIGHT + PADDING * 2;
                float canvasH = (float)SkiaCanvas.ActualHeight;
                float visibleRows = canvasH - X_AXIS_HEIGHT;
                float maxOffset = Math.Max(0, contentHeight - visibleRows);
                _verticalOffset = Math.Max(0, Math.Min(_verticalOffset + scrollAmount, maxOffset));
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }

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

            if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
            SkiaCanvas.InvalidateVisual();

            e.Handled = true;
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
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
            // Hover state is pre-computed in OnMouseMove - no need to reset here
            float hoverX = (float)_hoverPos.X;
            float hoverY = (float)_hoverPos.Y;

            // Clip drawing to chart area for state bars
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, 0, chartRight, chartBottom));

            foreach (var stateData in _stateDataList)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT) - _verticalOffset;
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);

                // Skip rows entirely outside the visible area
                if (rowBottom < 0 || rowTop > chartBottom) { rowIndex++; continue; }

                float barTop = rowTop + 2;
                float barBottom = rowBottom - 2;
                float barHeight = barBottom - barTop;

                // Draw alternating row background for readability
                if ((int)rowIndex % 2 == 1)
                {
                    _rowBgPaint.Color = _gridColor.WithAlpha(30);
                    canvas.DrawRect(new SKRect(chartLeft, rowTop, chartRight, rowBottom), _rowBgPaint);
                }

                // Draw state intervals for this CH
                if (stateData.Intervals != null)
                {
                    foreach (var interval in stateData.Intervals)
                    {
                        if (interval.EndIndex < start || interval.StartIndex > end) continue;

                        float x1 = chartLeft + (float)((Math.Max(interval.StartIndex, start) - start) / (double)count * chartWidth);
                        float x2 = chartLeft + (float)((Math.Min(interval.EndIndex, end) - start + 1) / (double)count * chartWidth);
                        if (x2 - x1 < 0.5f) x2 = x1 + 0.5f; // minimum width

                        // Get color based on state ID
                        SKColor baseColor = CHStepColors[Math.Abs(interval.StateId) % CHStepColors.Length];

                        // Use pre-computed hover state from OnMouseMove
                        bool isHovered = _hoveredStateRow == (int)rowIndex &&
                                         _hoveredStateInterval.HasValue &&
                                         _hoveredStateInterval.Value.StartIndex == interval.StartIndex;

                        var barRect = new SKRect(x1, barTop, x2, barBottom);
                        var barRoundRect = new SKRoundRect(barRect, 3, 3);

                        // Main fill with subtle top-to-bottom gradient
                        SKColor topColor = isHovered ? baseColor : LightenColor(baseColor, 0.15f);
                        SKColor bottomColor = isHovered ? DarkenColor(baseColor, 0.1f) : DarkenColor(baseColor, 0.2f);

                        _gradientPaint.Shader?.Dispose();
                        _gradientPaint.Shader = SKShader.CreateLinearGradient(
                            new SKPoint(0, barTop), new SKPoint(0, barBottom),
                            new[] { topColor, bottomColor }, null, SKShaderTileMode.Clamp);
                        canvas.DrawRoundRect(barRoundRect, _gradientPaint);

                        // Subtle inner highlight (top edge glow)
                        if (barHeight > 8)
                        {
                            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x1 + 1, barTop + 1, x2 - 1, barTop + barHeight * 0.4f), 2, 2), _glowPaint);
                        }

                        // Border: thin dark edge for definition
                        _edgePaint.Color = DarkenColor(baseColor, 0.35f);
                        canvas.DrawRoundRect(barRoundRect, _edgePaint);

                        // Hovered bar: bright white border highlight
                        if (isHovered)
                        {
                            canvas.DrawRoundRect(barRoundRect, _highlightBorderPaint);
                        }

                        // Draw state label if there's room
                        float barW = x2 - x1;
                        string stateLabel = !string.IsNullOrEmpty(interval.StateName)
                            ? interval.StateName : interval.StateId.ToString();
                        float textWidth = _intervalLabelPaint.MeasureText(stateLabel);
                        if (textWidth < barW - 6 && barHeight > 10)
                        {
                            // Contrast text: white on dark colors, dark on light colors
                            float brightness = (baseColor.Red * 0.299f + baseColor.Green * 0.587f + baseColor.Blue * 0.114f) / 255f;
                            _intervalLabelPaint.Color = brightness > 0.55f ? s_darkTextColor : SKColors.White;
                            float textX = x1 + (barW - textWidth) / 2;
                            float textY = barTop + barHeight / 2 + 3.5f;
                            canvas.DrawText(stateLabel, textX, textY, _intervalLabelPaint);
                        }
                    }
                }

                rowIndex++;
            }

            canvas.Restore(); // Remove chart clip

            // Draw row labels and grid lines (clipped vertically so they don't overlap X-axis)
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, w, chartBottom));
            rowIndex = 0;
            _labelPaint.Color = _textColor;
            _labelPaint.Typeface = s_segoeNormal;
            foreach (var stateData in _stateDataList)
            {
                float rowTop = PADDING + (rowIndex * ROW_HEIGHT) - _verticalOffset;
                float rowBottom = Math.Min(rowTop + ROW_HEIGHT - PADDING, chartBottom);

                // Skip rows outside visible area
                if (rowBottom < 0 || rowTop > chartBottom) { rowIndex++; continue; }

                bool isLabelHovered = _hoveredLabelRow == (int)rowIndex;

                string label = !string.IsNullOrEmpty(stateData.Category)
                    ? $"{stateData.Category}>{stateData.Name}" : stateData.Name;
                if (label.Length > 14) label = label.Substring(0, 14) + "..";

                // Highlight label on hover
                if (isLabelHovered)
                {
                    _labelPaint.Color = s_accentBlue;
                    _labelPaint.Typeface = s_segoeBold;
                }
                else
                {
                    _labelPaint.Color = _textColor;
                    _labelPaint.Typeface = s_segoeNormal;
                }

                canvas.DrawText(label, 5, rowTop + ROW_HEIGHT / 2 + 4, _labelPaint);

                // Draw subtle horizontal grid line
                canvas.DrawLine(chartLeft, rowBottom + PADDING / 2, chartRight, rowBottom + PADDING / 2, _gridPaint);

                rowIndex++;
            }
            canvas.Restore(); // Remove rows clip

            // Draw full name tooltip when hovering over left label area
            if (_hoveredLabelRow >= 0 && _hoveredLabelRow < _stateDataList.Count)
            {
                var hoveredData = _stateDataList[_hoveredLabelRow];
                string fullName = hoveredData.Name;
                if (!string.IsNullOrEmpty(hoveredData.Category))
                    fullName = $"{hoveredData.Category} > {hoveredData.Name}";

                _tooltipBgPaint.Color = _isLightTheme ? s_tooltipBgLight : s_tooltipBgDark;
                _labelTooltipTextPaint.Color = _isLightTheme ? s_darkTextColor : SKColors.White;

                float textW = _labelTooltipTextPaint.MeasureText(fullName);
                float ttW = textW + 16;
                float ttH = 22;
                float ttX = (float)_hoverPos.X + 10;
                float ttY = (float)_hoverPos.Y - ttH - 5;
                if (ttY < 2) ttY = (float)_hoverPos.Y + 15;
                if (ttX + ttW > w) ttX = w - ttW - 5;

                canvas.DrawRoundRect(new SKRoundRect(new SKRect(ttX, ttY, ttX + ttW, ttY + ttH), 4), _tooltipBgPaint);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(ttX, ttY, ttX + ttW, ttY + ttH), 4), _labelTooltipBorderPaint);
                canvas.DrawText(fullName, ttX + 8, ttY + 15, _labelTooltipTextPaint);
            }

            // Draw CHSTEP hover tooltip
            if (_hoveredStateInterval.HasValue && _hoveredStateRow >= 0 && _hoveredStateRow < _stateDataList.Count)
            {
                var hoveredData = _stateDataList[_hoveredStateRow];
                var hInterval = _hoveredStateInterval.Value;

                var tooltipSb = new StringBuilder();

                // Line 1: Component name (fixed for this bar)
                string compName = !string.IsNullOrEmpty(hoveredData.Category)
                    ? $"{hoveredData.Category} > {hoveredData.Name}" : hoveredData.Name;
                tooltipSb.AppendLine(compName);

                // Line 2: Duration info (fixed for this bar)
                if (!string.IsNullOrEmpty(hInterval.TooltipText))
                {
                    tooltipSb.AppendLine(hInterval.TooltipText);
                }
                else
                {
                    tooltipSb.AppendLine($"State: {hInterval.StateId}");
                    if (!string.IsNullOrEmpty(hInterval.StateName))
                        tooltipSb.AppendLine($"Step: {hInterval.StateName}");

                    string startTime = GetXAxisLabel?.Invoke(hInterval.StartIndex);
                    string endTime = GetXAxisLabel?.Invoke(hInterval.EndIndex);
                    if (!string.IsNullOrEmpty(startTime) && !string.IsNullOrEmpty(endTime))
                        tooltipSb.AppendLine($"{startTime} - {endTime}");
                }

                // Line 3: Dynamic time at cursor position (changes as mouse moves along the bar)
                if (_hoverDataIndex >= 0 && GetXAxisLabel != null)
                {
                    string cursorTime = GetXAxisLabel(_hoverDataIndex);
                    if (!string.IsNullOrEmpty(cursorTime))
                        tooltipSb.AppendLine($"Time: {cursorTime}");
                }

                DrawCHStepTooltip(canvas, tooltipSb.ToString(), hoverX + 15, hoverY - 20, w, h);
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

            // Draw cursor line with subtle glow (not for independent-timeline charts)
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

        // Helper: lighten a color
        private static SKColor LightenColor(SKColor c, float amount)
        {
            int r = Math.Min(255, (int)(c.Red + (255 - c.Red) * amount));
            int g = Math.Min(255, (int)(c.Green + (255 - c.Green) * amount));
            int b = Math.Min(255, (int)(c.Blue + (255 - c.Blue) * amount));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
        }

        // Helper: darken a color
        private static SKColor DarkenColor(SKColor c, float amount)
        {
            int r = Math.Max(0, (int)(c.Red * (1 - amount)));
            int g = Math.Max(0, (int)(c.Green * (1 - amount)));
            int b = Math.Max(0, (int)(c.Blue * (1 - amount)));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
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
                maxWidth = Math.Max(maxWidth, _tooltipTextPaint.MeasureText(line));

            float tooltipW = maxWidth + 16;
            float tooltipH = lines.Length * 16 + 12;

            // Shadow
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6), _tooltipShadowPaint);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 6), _tooltipBgPaint);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + tooltipW, y + tooltipH), 6), _tooltipBorderPaint);

            float ty = y + 16;
            foreach (var line in lines)
            {
                canvas.DrawText(line, x + 8, ty, _tooltipTextPaint);
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
                maxWidth = Math.Max(maxWidth, _tooltipTextPaint.MeasureText(line));

            float tooltipW = maxWidth + 20;
            float tooltipH = lines.Length * 16 + 14;
            float accentW = 4;

            // Clamp tooltip position to stay within canvas bounds
            if (x + tooltipW > canvasWidth - 5)
                x = x - tooltipW - 30;
            if (y + tooltipH > canvasHeight - 5)
                y = canvasHeight - tooltipH - 5;
            if (y < 5) y = 5;

            var rect = new SKRect(x, y, x + tooltipW, y + tooltipH);

            // Shadow
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6), _tooltipShadowPaint);
            // Background
            canvas.DrawRoundRect(new SKRoundRect(rect, 6), _tooltipBgPaint);
            // Left accent bar (color of hovered state)
            if (_hoveredStateInterval.HasValue)
            {
                SKColor barColor = CHStepColors[Math.Abs(_hoveredStateInterval.Value.StateId) % CHStepColors.Length];
                _tooltipAccentPaint.Color = barColor;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + accentW, y + tooltipH), 6, 0), _tooltipAccentPaint);
                canvas.DrawRect(new SKRect(x + 3, y, x + accentW, y + tooltipH), _tooltipAccentPaint); // fill the rounded corner gap
            }
            // Border
            canvas.DrawRoundRect(new SKRoundRect(rect, 6), _tooltipBorderPaint);

            float ty = y + 16;
            foreach (var line in lines)
            {
                canvas.DrawText(line, x + 10, ty, _tooltipTextPaint);
                ty += 16;
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

            _axisPaint.Color = _textColor;
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
                    float textWidth = _axisPaint.MeasureText(label);
                    float textX = x - textWidth / 2;
                    textX = Math.Max(chartLeft, Math.Min(textX, chartRight - textWidth));
                    canvas.DrawText(label, textX, chartBottom + 14, _axisPaint);
                }
            }
        }
    }
}
