using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System.Text;
using System.Collections.ObjectModel;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView : UserControl
    {
        // Events for synchronization
        public event Action<int, int> OnViewRangeChanged;
        public event Action<int> OnCursorMoved;
        public event Action OnChartClicked;
        public event Action<int> OnTimeClicked; // For log synchronization

        public Func<int, string> GetXAxisLabel { get; set; }

        private bool _isSyncing = false;
        private bool _showStates = true;
        private bool _isProgressiveMode = false;
        private bool _isLightTheme = false;

        public bool IsProgressiveMode
        {
            get => _isProgressiveMode;
            set { _isProgressiveMode = value; SkiaCanvas.InvalidateVisual(); }
        }

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

        // Theme colors
        private SKColor _bgColor;
        private SKColor _gridColor;
        private SKColor _textColor;
        private SKColor _accentColor;

        // Paints
        private SKPaint _gridLinePaint;
        private SKPaint _axisLinePaint;
        private SKPaint _textPaintLeft;
        private SKPaint _textPaintRight;
        private SKPaint _stateTextPaint;
        private SKPaint _stateFillPaint;
        private SKPaint _targetLinePaint;
        private SKPaint _cursorLinePaint;
        private SKPaint _measureFillPaint;
        private SKPaint _measureBorderPaint;

        private List<SignalSeries> _seriesList = new List<SignalSeries>();
        private ObservableCollection<ReferenceLine> _referenceLines;
        private List<StateInterval> _states;

        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _totalDataLength = 0;
        private int _globalCursorIndex = -1;
        private int _targetLineIndex = -1;

        private const float LEFT_MARGIN = 60;
        private const float RIGHT_MARGIN = 55;
        private const float TOP_MARGIN = 20;
        private const float BOTTOM_MARGIN = 20;

        private bool _isDragging = false;
        private bool _isMeasuring = false;
        private Point _lastMousePos;
        private int _measureStartIndex = -1;
        private int _measureCurrentIndex = -1;

        private bool _isCtrlMeasuring = false;
        private int _ctrlPoint1 = -1;
        private int _ctrlPoint2 = -1;
        private Point _ctrlPoint1Pos;
        private Point _ctrlPoint2Pos;

        private bool _showHoverTooltip = false;
        private Point _hoverPos;

        // Store DPI scale for coordinate conversion
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        public ChartGraphView()
        {
            InitializeComponent();
            UpdateThemeColors();

            Loaded += (s, e) =>
            {
                var source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            };
        }

        private void UpdateThemeColors()
        {
            if (_isLightTheme)
            {
                _bgColor = SKColor.Parse("#FFFFFF");
                _gridColor = SKColor.Parse("#DDDDDD");
                _textColor = SKColor.Parse("#333333");
                _accentColor = SKColor.Parse("#3B82F6");
            }
            else
            {
                _bgColor = SKColor.Parse("#1B2838");
                _gridColor = SKColor.Parse("#2D4A6F");
                _textColor = SKColors.White;
                _accentColor = SKColor.Parse("#3B82F6");
            }

            _gridLinePaint = new SKPaint { Color = _gridColor.WithAlpha(80), IsAntialias = false, StrokeWidth = 1 };
            _axisLinePaint = new SKPaint { Color = _gridColor, IsAntialias = false, StrokeWidth = 1 };
            _textPaintLeft = new SKPaint { Color = _textColor, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) };
            _textPaintRight = new SKPaint { Color = _accentColor, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
            _stateTextPaint = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
            _stateFillPaint = new SKPaint { Style = SKPaintStyle.Fill };
            _targetLinePaint = new SKPaint { Color = _accentColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = false };
            _cursorLinePaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = false };
            _measureFillPaint = new SKPaint { Color = _accentColor.WithAlpha(40), Style = SKPaintStyle.Fill };
            _measureBorderPaint = new SKPaint { Color = _accentColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0) };
        }

        private float SnapToPixel(float coord) => (float)Math.Floor(coord) + 0.5f;

        public void SetViewModel(ChartViewModel vm)
        {
            if (vm == null) return;
            _seriesList = vm.Series.ToList();
            _referenceLines = vm.ReferenceLines;
            _states = vm.States;
            _totalDataLength = _seriesList.Any() ? _seriesList.Max(s => s.Data != null ? s.Data.Length : 0) : 0;
            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }
            SkiaCanvas.InvalidateVisual();
        }

        public void SetShowStates(bool show) { _showStates = show; SkiaCanvas.InvalidateVisual(); }
        public void SetTargetLine(int index) { _targetLineIndex = index; SkiaCanvas.InvalidateVisual(); }

        public void SyncViewRange(int start, int end)
        {
            if (_totalDataLength == 0 || _isSyncing) return;
            _isSyncing = true;
            _viewStartIndex = Math.Max(0, Math.Min(start, _totalDataLength - 1));
            _viewEndIndex = Math.Max(0, Math.Min(end, _totalDataLength - 1));
            SkiaCanvas.InvalidateVisual();
            _isSyncing = false;
        }

        public void SyncCursor(int index)
        {
            _globalCursorIndex = index;
            UpdateLegendValues(index);
            SkiaCanvas.InvalidateVisual();
        }

        private void UpdateLegendValues(int index)
        {
            if (index < 0 || index >= _totalDataLength) return;
            foreach (var s in _seriesList)
            {
                if (s.Data != null && index < s.Data.Length)
                {
                    double val = s.Data[index];
                    s.CurrentValueDisplay = double.IsNaN(val) ? "NaN" : val.ToString("F2");
                }
                else
                {
                    s.CurrentValueDisplay = "-";
                }
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus(); // Enable keyboard events
            OnChartClicked?.Invoke();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (_totalDataLength == 0) return;
            int totalPoints = _viewEndIndex - _viewStartIndex;
            if (totalPoints < 10) return;

            double zoomFactor = e.Delta > 0 ? 0.85 : 1.15;

            double chartWidth = ActualWidth - LEFT_MARGIN - RIGHT_MARGIN;
            double mouseX = e.GetPosition(this).X - LEFT_MARGIN;
            double mouseRatio = Math.Max(0, Math.Min(mouseX / chartWidth, 1));

            int mouseIndex = _viewStartIndex + (int)(totalPoints * mouseRatio);
            int newSpan = Math.Max(10, (int)(totalPoints * zoomFactor));

            int newStart = mouseIndex - (int)(newSpan * mouseRatio);
            int newEnd = newStart + newSpan;

            if (newStart < 0) { newStart = 0; newEnd = Math.Min(newSpan, _totalDataLength - 1); }
            if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = Math.Max(0, newEnd - newSpan); }

            if (newEnd > newStart && newEnd - newStart >= 10 && (newStart != _viewStartIndex || newEnd != _viewEndIndex))
            {
                _viewStartIndex = newStart;
                _viewEndIndex = newEnd;
                SkiaCanvas.InvalidateVisual();
                if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
            }
        }

        // Convert WPF coordinates to Skia coordinates (account for DPI)
        private Point WpfToSkia(Point wpfPoint)
        {
            return new Point(wpfPoint.X * _dpiScaleX, wpfPoint.Y * _dpiScaleY);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var wpfPos = e.GetPosition(this);
            var pos = WpfToSkia(wpfPos);

            // Ctrl+Click for 2-point measurement
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (!_isCtrlMeasuring)
                {
                    _ctrlPoint1 = PixelToIndex(pos.X);
                    _ctrlPoint1Pos = pos;
                    _ctrlPoint2 = -1;
                    _isCtrlMeasuring = true;
                }
                else
                {
                    _ctrlPoint2 = PixelToIndex(pos.X);
                    _ctrlPoint2Pos = pos;
                    _isCtrlMeasuring = false;
                }
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Shift+Click/Drag for area measurement
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                _isMeasuring = true;
                _measureStartIndex = PixelToIndex(pos.X);
                _measureCurrentIndex = _measureStartIndex;
                CaptureMouse();
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Regular click - trigger time sync
            int clickedIndex = PixelToIndex(pos.X);
            OnTimeClicked?.Invoke(clickedIndex);

            _isDragging = true;
            _lastMousePos = pos;
            CaptureMouse();
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _isDragging = false;

            if (_isMeasuring && Math.Abs(_measureStartIndex - _measureCurrentIndex) < 5)
            {
                _measureStartIndex = -1;
                _measureCurrentIndex = -1;
            }
            _isMeasuring = false;

            ReleaseMouseCapture();
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_totalDataLength == 0) return;

            var wpfPos = e.GetPosition(this);
            var currentPos = WpfToSkia(wpfPos);

            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartRight = (ActualWidth - RIGHT_MARGIN) * _dpiScaleX;

            _showHoverTooltip = Keyboard.Modifiers == ModifierKeys.Alt &&
                               currentPos.X >= chartLeft &&
                               currentPos.X <= chartRight;
            _hoverPos = currentPos;

            if (_isProgressiveMode) return;

            int cursorIdx = PixelToIndex(currentPos.X);

            if (!_isProgressiveMode && cursorIdx != _globalCursorIndex)
            {
                _globalCursorIndex = cursorIdx;
                OnCursorMoved?.Invoke(cursorIdx);
                UpdateLegendValues(cursorIdx);
                SkiaCanvas.InvalidateVisual();
            }

            if (_isMeasuring)
            {
                _measureCurrentIndex = cursorIdx;
                SkiaCanvas.InvalidateVisual();
            }
            else if (_isDragging)
            {
                double deltaX = currentPos.X - _lastMousePos.X;
                double chartWidth = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
                int visiblePoints = _viewEndIndex - _viewStartIndex;
                int shift = (int)((deltaX / chartWidth) * visiblePoints);
                int newStart = _viewStartIndex - shift;
                int newEnd = _viewEndIndex - shift;

                if (newStart < 0) { newStart = 0; newEnd = visiblePoints; }
                if (newEnd >= _totalDataLength) { newEnd = _totalDataLength - 1; newStart = newEnd - visiblePoints; }

                if (newStart != _viewStartIndex)
                {
                    _viewStartIndex = newStart;
                    _viewEndIndex = newEnd;
                    _lastMousePos = currentPos;
                    SkiaCanvas.InvalidateVisual();
                    if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                }
            }
            else if (_showHoverTooltip)
            {
                SkiaCanvas.InvalidateVisual();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                ClearAllMeasurements();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            ClearAllMeasurements();
        }

        private void ClearAllMeasurements()
        {
            _ctrlPoint1 = -1;
            _ctrlPoint2 = -1;
            _isCtrlMeasuring = false;
            _measureStartIndex = -1;
            _measureCurrentIndex = -1;
            _isMeasuring = false;
            SkiaCanvas.InvalidateVisual();
        }

        private int PixelToIndex(double x)
        {
            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartWidth = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
            if (chartWidth <= 0 || _totalDataLength == 0) return 0;
            double relX = x - chartLeft;
            int count = _viewEndIndex - _viewStartIndex;
            int offset = (int)((relX / chartWidth) * count);
            return Math.Max(0, Math.Min(_viewStartIndex + offset, _totalDataLength - 1));
        }

        public double GetCurrentCursorValue()
        {
            if (_globalCursorIndex < 0 || _seriesList.Count == 0) return 0;
            var firstVisible = _seriesList.FirstOrDefault(s => s.IsVisible && s.Data != null);
            if (firstVisible == null || _globalCursorIndex >= firstVisible.Data.Length) return 0;
            return firstVisible.Data[_globalCursorIndex];
        }

        public int GetCurrentCursorIndex() => _globalCursorIndex;

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_totalDataLength == 0) return;

            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            int start = Math.Max(0, _viewStartIndex);
            int end = Math.Min(_totalDataLength - 1, _viewEndIndex);
            int count = end - start + 1;
            if (count <= 1 || chartW <= 0) return;

            // Background States
            if (_showStates && _states != null)
            {
                foreach (var st in _states)
                {
                    if (st.EndIndex < start || st.StartIndex > end) continue;
                    float x1 = (float)((Math.Max(st.StartIndex, start) - start) / (double)count * chartW);
                    float x2 = (float)((Math.Min(st.EndIndex, end) - start) / (double)count * chartW);
                    canvas.DrawRect(new SKRect(chartLeft + x1, chartTop, chartLeft + x2, chartBottom),
                        new SKPaint { Style = SKPaintStyle.Fill, Color = ChartStateConfig.GetColor(st.StateId) });

                    string nm = ChartStateConfig.GetName(st.StateId);
                    float tw = _stateTextPaint.MeasureText(nm);
                    if (tw < (x2 - x1) - 4)
                    {
                        canvas.DrawText(nm, (float)Math.Round(chartLeft + x1 + (x2 - x1) / 2 - tw / 2), (float)Math.Round(chartTop + 14), _stateTextPaint);
                    }
                }
            }

            // Dual Scale Logic
            double lMin = double.MaxValue, lMax = double.MinValue;
            double rMin = double.MaxValue, rMax = double.MinValue;
            bool hasLeft = false, hasRight = false;
            int step = Math.Max(1, count / 1000);

            foreach (var s in _seriesList)
            {
                if (s.Data == null || !s.IsVisible) continue;

                if (s.YAxisType == AxisType.Right)
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < s.Data.Length && !double.IsNaN(s.Data[i]))
                        {
                            if (s.Data[i] < rMin) rMin = s.Data[i];
                            if (s.Data[i] > rMax) rMax = s.Data[i];
                            hasRight = true;
                        }
                    }
                }
                else
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < s.Data.Length && !double.IsNaN(s.Data[i]))
                        {
                            if (s.Data[i] < lMin) lMin = s.Data[i];
                            if (s.Data[i] > lMax) lMax = s.Data[i];
                            hasLeft = true;
                        }
                    }
                }
            }

            if (!hasLeft) { lMin = 0; lMax = 10; }
            if (!hasRight) { rMin = 0; rMax = 10; }
            if (Math.Abs(lMax - lMin) < 0.0001) { lMax += 1; lMin -= 1; }
            if (Math.Abs(rMax - rMin) < 0.0001) { rMax += 1; rMin -= 1; }

            double lPadding = (lMax - lMin) * 0.1;
            double lDisplayMin = lMin - lPadding;
            double lRange = (lMax - lMin) + (2 * lPadding);

            double rPadding = (rMax - rMin) * 0.1;
            double rDisplayMin = rMin - rPadding;
            double rRange = (rMax - rMin) + (2 * rPadding);

            // Grid Y
            int ySteps = 4;
            for (int i = 0; i <= ySteps; i++)
            {
                double ratio = i / (double)ySteps;
                float yPos = SnapToPixel(chartBottom - (float)(ratio * chartH));
                canvas.DrawLine(chartLeft, yPos, chartRight, yPos, _gridLinePaint);

                if (hasLeft || !hasRight)
                {
                    string lbl = (lDisplayMin + (ratio * lRange)).ToString("0.##");
                    float lblW = _textPaintLeft.MeasureText(lbl);
                    canvas.DrawText(lbl, chartLeft - lblW - 6, yPos + 4, _textPaintLeft);
                }
                if (hasRight)
                {
                    string lbl = (rDisplayMin + (ratio * rRange)).ToString("0.##");
                    canvas.DrawText(lbl, chartRight + 6, yPos + 4, _textPaintRight);
                }
            }

            // Grid X
            float stepPixels = 120;
            int xSteps = (int)(chartW / stepPixels);
            float lastTextRight = -1000;

            for (int i = 0; i <= xSteps; i++)
            {
                float xPos = SnapToPixel(chartLeft + (i * stepPixels));
                if (xPos > chartRight) break;

                double ratio = (xPos - chartLeft) / chartW;
                int idx = start + (int)(count * ratio);
                canvas.DrawLine(xPos, chartTop, xPos, chartBottom, _gridLinePaint);

                if (GetXAxisLabel != null)
                {
                    string t = GetXAxisLabel(idx);
                    if (!string.IsNullOrEmpty(t))
                    {
                        float txtW = _textPaintLeft.MeasureText(t);
                        float tl = (float)Math.Round(xPos - txtW / 2);
                        if (tl > lastTextRight + 20)
                        {
                            canvas.DrawText(t, tl, chartBottom + 16, _textPaintLeft);
                            lastTextRight = tl + txtW;
                        }
                    }
                }
            }

            // Reference Lines
            if (_referenceLines != null)
            {
                foreach (var line in _referenceLines)
                {
                    using (var paint = new SKPaint
                    {
                        Color = line.Color,
                        StrokeWidth = line.Thickness,
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true,
                        PathEffect = line.IsDashed ? SKPathEffect.CreateDash(new float[] { 10, 5 }, 0) : null
                    })
                    {
                        if (line.Type == ReferenceLineType.Vertical)
                        {
                            int idx = (int)line.Value;
                            if (idx >= start && idx <= end)
                            {
                                float x = chartLeft + (float)((idx - start) / (double)count * chartW);
                                canvas.DrawLine(x, chartTop, x, chartBottom, paint);

                                if (!string.IsNullOrEmpty(line.Name))
                                {
                                    using (var tp = new SKPaint { TextSize = 11, Color = line.Color, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
                                    {
                                        canvas.DrawText(line.Name, x + 4, chartTop + 12, tp);
                                    }
                                }
                            }
                        }
                        else
                        {
                            double range = (line.YAxis == AxisType.Left) ? lRange : rRange;
                            double dMin = (line.YAxis == AxisType.Left) ? lDisplayMin : rDisplayMin;

                            if (line.Value >= dMin && line.Value <= (dMin + range))
                            {
                                float y = chartBottom - (float)((line.Value - dMin) / range * chartH);
                                canvas.DrawLine(chartLeft, y, chartRight, y, paint);

                                if (!string.IsNullOrEmpty(line.Name))
                                {
                                    using (var tp = new SKPaint { TextSize = 11, Color = line.Color, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
                                    {
                                        canvas.DrawText(line.Name, chartLeft + 4, y - 4, tp);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Signal Lines
            using (var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true })
            using (var path = new SKPath())
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

                int drawLimit = end;
                if (_isProgressiveMode && _globalCursorIndex != -1)
                    drawLimit = Math.Min(end, _globalCursorIndex);

                foreach (var s in _seriesList)
                {
                    if (!s.IsVisible || s.Data == null) continue;
                    paint.Color = s.Color;
                    path.Reset();
                    bool first = true;

                    double currentMin = (s.YAxisType == AxisType.Right) ? rDisplayMin : lDisplayMin;
                    double currentRange = (s.YAxisType == AxisType.Right) ? rRange : lRange;
                    int drawStep = Math.Max(1, count / (int)chartW);

                    for (int i = start; i <= drawLimit; i += drawStep)
                    {
                        if (i >= s.Data.Length) break;
                        double val = s.Data[i];
                        if (double.IsNaN(val)) { first = true; continue; }

                        float x = chartLeft + (float)((i - start) / (double)count * chartW);
                        float y = chartBottom - (float)((val - currentMin) / currentRange * chartH);

                        if (first) { path.MoveTo(x, y); first = false; }
                        else path.LineTo(x, y);
                    }

                    if (!first) canvas.DrawPath(path, paint);
                }
                canvas.Restore();
            }

            // Border
            float L = SnapToPixel(chartLeft), R = SnapToPixel(chartRight), B = SnapToPixel(chartBottom), T = SnapToPixel(chartTop);
            canvas.DrawLine(L, T, L, B, _axisLinePaint);
            if (hasRight) canvas.DrawLine(R, T, R, B, _axisLinePaint);
            canvas.DrawLine(L, B, R, B, _axisLinePaint);

            // Target Line (Blue)
            if (_targetLineIndex >= start && _targetLineIndex <= end)
            {
                float tx = SnapToPixel(chartLeft + (float)((_targetLineIndex - start) / (double)count * chartW));
                canvas.DrawLine(tx, T, tx, B, _targetLinePaint);
            }

            // Cursor Line (Red)
            if (_globalCursorIndex >= start && _globalCursorIndex <= end)
            {
                float cx = SnapToPixel(chartLeft + (float)((_globalCursorIndex - start) / (double)count * chartW));
                canvas.DrawLine(cx, T, cx, B, _cursorLinePaint);
            }

            // Measure Box (Shift+Drag)
            if (_measureStartIndex != -1 && _measureCurrentIndex != -1)
            {
                int mS = Math.Max(Math.Min(_measureStartIndex, _measureCurrentIndex), start);
                int mE = Math.Min(Math.Max(_measureStartIndex, _measureCurrentIndex), end);

                if (mE > mS)
                {
                    float x1 = chartLeft + (float)((mS - start) / (double)count * chartW);
                    float x2 = chartLeft + (float)((mE - start) / (double)count * chartW);
                    var rect = new SKRect(x1, chartTop, x2, chartBottom);
                    canvas.DrawRect(rect, _measureFillPaint);
                    canvas.DrawRect(rect, _measureBorderPaint);

                    if (!_isMeasuring)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== AREA MEASUREMENT ===");
                        sb.AppendLine($"Index Range: {mS} -> {mE}");
                        sb.AppendLine($"Points: {mE - mS + 1}");

                        if (GetXAxisLabel != null)
                        {
                            string t1 = GetXAxisLabel(mS);
                            string t2 = GetXAxisLabel(mE);
                            if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                            {
                                sb.AppendLine($"Time: {t1} -> {t2}");
                            }
                        }

                        sb.AppendLine("-------------------");

                        foreach (var s in _seriesList)
                        {
                            if (!s.IsVisible || s.Data == null) continue;
                            double sum = 0, mn = double.MaxValue, mx = double.MinValue;
                            int c = 0;

                            for (int i = mS; i <= mE; i++)
                            {
                                if (i < s.Data.Length && !double.IsNaN(s.Data[i]))
                                {
                                    double v = s.Data[i];
                                    sum += v;
                                    if (v < mn) mn = v;
                                    if (v > mx) mx = v;
                                    c++;
                                }
                            }

                            if (c > 0)
                            {
                                double avg = sum / c;
                                sb.AppendLine($"{s.Name}:");
                                sb.AppendLine($"  Avg: {avg:F3}");
                                sb.AppendLine($"  Min: {mn:F3}");
                                sb.AppendLine($"  Max: {mx:F3}");
                                sb.AppendLine($"  Delta: {(mx - mn):F3}");
                            }
                        }

                        float tooltipX = (_measureCurrentIndex > _measureStartIndex) ? x2 + 15 : x1 - 170;
                        float tooltipY = chartTop + 10;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
            }

            // Ctrl+Click 2-Point Measurement
            if (_ctrlPoint1 != -1 && _ctrlPoint1 >= start && _ctrlPoint1 <= end)
            {
                float x1 = chartLeft + (float)((_ctrlPoint1 - start) / (double)count * chartW);
                float y1 = (float)_ctrlPoint1Pos.Y;

                // Clamp y1 to chart area
                y1 = Math.Max(chartTop, Math.Min(chartBottom, y1));

                using (var paint = new SKPaint { Color = SKColors.LimeGreen, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
                {
                    canvas.DrawLine(x1, chartTop, x1, chartBottom, paint);
                    canvas.DrawLine(chartLeft, y1, chartRight, y1, paint);
                    canvas.DrawCircle(x1, y1, 5, paint);
                }

                if (_ctrlPoint2 != -1 && _ctrlPoint2 >= start && _ctrlPoint2 <= end)
                {
                    float x2 = chartLeft + (float)((_ctrlPoint2 - start) / (double)count * chartW);
                    float y2 = (float)_ctrlPoint2Pos.Y;

                    // Clamp y2 to chart area
                    y2 = Math.Max(chartTop, Math.Min(chartBottom, y2));

                    using (var paint = new SKPaint { Color = SKColors.LimeGreen, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
                    {
                        canvas.DrawLine(x2, chartTop, x2, chartBottom, paint);
                        canvas.DrawLine(chartLeft, y2, chartRight, y2, paint);
                        canvas.DrawCircle(x2, y2, 5, paint);
                    }

                    using (var paint = new SKPaint { Color = SKColors.LimeGreen, StrokeWidth = 2, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0) })
                    {
                        canvas.DrawLine(x1, y1, x2, y2, paint);
                    }

                    if (!_isCtrlMeasuring)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== 2-POINT MEASUREMENT ===");
                        sb.AppendLine($"Point 1 Index: {_ctrlPoint1}");
                        sb.AppendLine($"Point 2 Index: {_ctrlPoint2}");
                        sb.AppendLine($"X Distance: {Math.Abs(_ctrlPoint2 - _ctrlPoint1)} points");

                        if (GetXAxisLabel != null)
                        {
                            string t1 = GetXAxisLabel(_ctrlPoint1);
                            string t2 = GetXAxisLabel(_ctrlPoint2);
                            if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                            {
                                sb.AppendLine($"Time 1: {t1}");
                                sb.AppendLine($"Time 2: {t2}");
                            }
                        }

                        sb.AppendLine("-------------------");

                        foreach (var s in _seriesList)
                        {
                            if (!s.IsVisible || s.Data == null) continue;

                            if (_ctrlPoint1 < s.Data.Length && _ctrlPoint2 < s.Data.Length)
                            {
                                double v1 = s.Data[_ctrlPoint1];
                                double v2 = s.Data[_ctrlPoint2];

                                if (!double.IsNaN(v1) && !double.IsNaN(v2))
                                {
                                    sb.AppendLine($"{s.Name}:");
                                    sb.AppendLine($"  P1: {v1:F3}");
                                    sb.AppendLine($"  P2: {v2:F3}");
                                    sb.AppendLine($"  Delta: {(v2 - v1):F3}");
                                }
                            }
                        }

                        float tooltipX = (_ctrlPoint2 > _ctrlPoint1) ? x2 + 15 : x2 - 170;
                        float tooltipY = y2;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
            }

            // Hover Tooltip (Alt key)
            if (_showHoverTooltip && _hoverPos.X >= chartLeft && _hoverPos.X <= chartRight)
            {
                int hoverIdx = PixelToIndex(_hoverPos.X);
                if (hoverIdx >= start && hoverIdx <= end)
                {
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
                        if (!s.IsVisible || s.Data == null || hoverIdx >= s.Data.Length) continue;
                        double val = s.Data[hoverIdx];
                        string valStr = double.IsNaN(val) ? "NaN" : val.ToString("F3");
                        tooltipText.AppendLine($"{s.Name}: {valStr}");
                    }

                    float tooltipX = (float)_hoverPos.X + 15;
                    float tooltipY = (float)_hoverPos.Y + 15;
                    DrawTooltip(canvas, tooltipText.ToString(), tooltipX, tooltipY);
                }
            }
        }

        private void DrawTooltip(SKCanvas c, string t, float x, float y)
        {
            var ls = t.Split('\n');
            float maxWidth = 0;

            using (var measurePaint = new SKPaint { TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) })
            {
                foreach (var line in ls)
                {
                    float w = measurePaint.MeasureText(line);
                    if (w > maxWidth) maxWidth = w;
                }
            }

            float boxW = Math.Max(150, maxWidth + 15);
            float h = ls.Length * 16 + 10;

            if (x + boxW > c.LocalClipBounds.Width) x -= (boxW + 20);
            if (y + h > c.LocalClipBounds.Height) y = c.LocalClipBounds.Height - h - 10;

            // Theme-aware tooltip
            SKColor tooltipBg = _isLightTheme ? SKColor.Parse("#FFFFFF").WithAlpha(245) : SKColor.Parse("#1E3A5F").WithAlpha(245);
            SKColor tooltipText = _isLightTheme ? SKColor.Parse("#333333") : SKColors.White;

            using (var p = new SKPaint { Color = tooltipBg, Style = SKPaintStyle.Fill })
            using (var b = new SKPaint { Color = _accentColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f })
            using (var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(80), Style = SKPaintStyle.Fill })
            {
                c.DrawRect(new SKRect(x + 2, y + 2, x + boxW + 2, y + h + 2), shadow);
                c.DrawRect(new SKRect(x, y, x + boxW, y + h), p);
                c.DrawRect(new SKRect(x, y, x + boxW, y + h), b);
            }

            float ty = y + 14;
            using (var textPaint = new SKPaint { Color = tooltipText, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) })
            {
                foreach (var l in ls)
                {
                    c.DrawText(l, x + 5, ty, textPaint);
                    ty += 16;
                }
            }
        }
    }
}
