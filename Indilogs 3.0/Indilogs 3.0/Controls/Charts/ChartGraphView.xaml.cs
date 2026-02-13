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
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

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
        private List<ThreadMessageData> _threadMessages = new List<ThreadMessageData>();
        private List<EventMarkerData> _eventMarkers = new List<EventMarkerData>();
        private List<EventMarker> _chartEventMarkers;
        private List<GapRegion> _timeGaps = new List<GapRegion>();

        // Event marker paints and rendering
        private SKPaint _eventDotPaint;
        private SKPaint _eventDotBorderPaint;
        private const float EVENT_DOT_RADIUS = 5f;
        private int _hoveredEventIndex = -1;

        // Event marker colors
        private static readonly SKColor EventMarkerColor = SKColors.Red;

        // Thread message marker colors (different color per thread)
        private static readonly SKColor[] ThreadMarkerColors = new[]
        {
            SKColor.Parse("#FF6B6B"), // Red
            SKColor.Parse("#4ECDC4"), // Teal
            SKColor.Parse("#FFE66D"), // Yellow
            SKColor.Parse("#95E1D3"), // Mint
            SKColor.Parse("#F38181"), // Coral
            SKColor.Parse("#AA96DA"), // Lavender
        };

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
        private StateInterval? _hoveredState = null;

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
            _eventDotPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
            _eventDotBorderPaint = new SKPaint { Color = SKColors.DarkRed, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        }

        private float SnapToPixel(float coord) => (float)Math.Floor(coord) + 0.5f;

        /// <summary>
        /// Sets thread messages to display as vertical markers on the chart
        /// </summary>
        public void SetThreadMessages(List<ThreadMessageData> messages)
        {
            _threadMessages = messages ?? new List<ThreadMessageData>();
            SkiaCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Sets time gap regions to display as semi-transparent overlays on the chart
        /// </summary>
        public void SetTimeGaps(List<GapRegion> gaps)
        {
            _timeGaps = gaps ?? new List<GapRegion>();
            SkiaCanvas.InvalidateVisual();
        }

        public void SetViewModel(ChartViewModel vm)
        {
            if (vm == null) return;

            // Unsubscribe from previous series
            foreach (var s in _seriesList)
            {
                s.PropertyChanged -= Series_PropertyChanged;
            }

            _seriesList = vm.Series.ToList();
            _referenceLines = vm.ReferenceLines;
            _states = vm.States;
            _chartEventMarkers = vm.EventMarkers;
            _totalDataLength = _seriesList.Any() ? _seriesList.Max(s => s.Data != null ? s.Data.Length : 0) : 0;
            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }

            // Subscribe to property changes on each series (for IsVisible, etc.)
            foreach (var s in _seriesList)
            {
                s.PropertyChanged += Series_PropertyChanged;
            }

            SkiaCanvas.InvalidateVisual();
        }

        private void Series_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Redraw when series properties change (like IsVisible, IsSmoothed)
            if (e.PropertyName == nameof(SignalSeries.IsVisible) ||
                e.PropertyName == nameof(SignalSeries.YAxisType) ||
                e.PropertyName == nameof(SignalSeries.IsSmoothed))
            {
                SkiaCanvas.InvalidateVisual();
            }
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
                var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                if (dataToDraw != null && index < dataToDraw.Length)
                {
                    double val = dataToDraw[index];
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

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Handle zoom with mouse wheel - this is PreviewMouseWheel so it fires before ScrollViewer
            if (_totalDataLength == 0) return;
            int totalPoints = _viewEndIndex - _viewStartIndex;
            if (totalPoints < 10) return;

            // Symmetric zoom factor - same amount of zoom in/out
            // Use 1.25 for zoom out, 1/1.25 = 0.8 for zoom in
            const double ZOOM_RATIO = 1.25;
            double zoomFactor = e.Delta > 0 ? (1.0 / ZOOM_RATIO) : ZOOM_RATIO;

            double chartWidth = ActualWidth - LEFT_MARGIN - RIGHT_MARGIN;
            double mouseX = e.GetPosition(this).X - LEFT_MARGIN;
            double mouseRatio = Math.Max(0, Math.Min(mouseX / chartWidth, 1));

            int mouseIndex = _viewStartIndex + (int)(totalPoints * mouseRatio);
            int newSpan = Math.Max(10, (int)Math.Round(totalPoints * zoomFactor));

            int newStart = mouseIndex - (int)Math.Round(newSpan * mouseRatio);
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

            e.Handled = true; // Mark as handled so ScrollViewer doesn't scroll
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

            // During playback mode, don't move cursor with mouse at all
            // Only allow measurement if actively measuring
            if (_isProgressiveMode)
            {
                if (_isMeasuring)
                {
                    var wpfPos = e.GetPosition(this);
                    var currentPos = WpfToSkia(wpfPos);
                    int cursorIdx = PixelToIndex(currentPos.X);
                    _measureCurrentIndex = cursorIdx;
                    SkiaCanvas.InvalidateVisual();
                }
                return;
            }

            var pos = e.GetPosition(this);
            var scaledPos = WpfToSkia(pos);

            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartRight = (ActualWidth - RIGHT_MARGIN) * _dpiScaleX;

            _showHoverTooltip = Keyboard.Modifiers == ModifierKeys.Alt &&
                               scaledPos.X >= chartLeft &&
                               scaledPos.X <= chartRight;
            _hoverPos = scaledPos;

            int cursorIndex = PixelToIndex(scaledPos.X);

            // Detect state hover for CHStep tooltip (binary search for performance)
            _hoveredState = null;
            if (_showStates && _states != null && _states.Count > 0)
            {
                int lo = 0, hi = _states.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (cursorIndex < _states[mid].StartIndex)
                        hi = mid - 1;
                    else if (cursorIndex > _states[mid].EndIndex)
                        lo = mid + 1;
                    else
                    {
                        _hoveredState = _states[mid];
                        break;
                    }
                }
            }

            // Handle measurement
            if (_isMeasuring)
            {
                _measureCurrentIndex = cursorIndex;
                SkiaCanvas.InvalidateVisual();
                return;
            }

            // Update cursor position (only when not in playback mode)
            if (cursorIndex != _globalCursorIndex)
            {
                _globalCursorIndex = cursorIndex;
                OnCursorMoved?.Invoke(cursorIndex);
                UpdateLegendValues(cursorIndex);
                SkiaCanvas.InvalidateVisual();
            }

            if (_isDragging)
            {
                double deltaX = scaledPos.X - _lastMousePos.X;
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
                    _lastMousePos = scaledPos;
                    SkiaCanvas.InvalidateVisual();
                    if (!_isSyncing) OnViewRangeChanged?.Invoke(_viewStartIndex, _viewEndIndex);
                }
            }
            else if (_showHoverTooltip || _hoveredState.HasValue)
            {
                SkiaCanvas.InvalidateVisual();
            }
            else if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                // Only repaint if hovered event index changed (not every mouse move)
                int oldHovered = _hoveredEventIndex;
                int newHovered = FindHoveredEventIndex(cursorIndex);
                if (newHovered != oldHovered)
                {
                    SkiaCanvas.InvalidateVisual();
                }
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

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredState.HasValue)
            {
                _hoveredState = null;
                _showHoverTooltip = false;
                SkiaCanvas.InvalidateVisual();
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

        /// <summary>
        /// Lightweight hover detection for event markers without triggering a full repaint.
        /// Returns the Index of the hovered event, or -1 if none.
        /// </summary>
        private int FindHoveredEventIndex(int cursorIndex)
        {
            if (_chartEventMarkers == null || _chartEventMarkers.Count == 0) return -1;
            int start = _viewStartIndex;
            int end = _viewEndIndex;
            int count = end - start;
            if (count <= 0) return -1;
            double chartLeft = LEFT_MARGIN * _dpiScaleX;
            double chartW = (ActualWidth - LEFT_MARGIN - RIGHT_MARGIN) * _dpiScaleX;
            double chartBottom = (ActualHeight - BOTTOM_MARGIN) * _dpiScaleY;
            float eventY = (float)chartBottom - 8;

            foreach (var evt in _chartEventMarkers)
            {
                if (evt.Index < start || evt.Index > end) continue;
                float ex = (float)(chartLeft + (evt.Index - start) / (double)count * chartW);
                float dx = (float)_hoverPos.X - ex;
                float dy = (float)_hoverPos.Y - eventY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist < EVENT_DOT_RADIUS * 4) return evt.Index;
            }
            return -1;
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

            // Time Gap Regions (semi-transparent red overlay)
            if (_timeGaps != null && _timeGaps.Count > 0)
            {
                using (var gapFillPaint = new SKPaint
                {
                    Color = SKColor.Parse("#30FF4444"),
                    Style = SKPaintStyle.Fill
                })
                using (var gapBorderPaint = new SKPaint
                {
                    Color = SKColor.Parse("#80FF4444"),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
                    IsAntialias = true
                })
                using (var gapTextPaint = new SKPaint
                {
                    Color = SKColor.Parse("#FF6B6B"),
                    TextSize = 10,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                })
                {
                    foreach (var gap in _timeGaps)
                    {
                        if (gap.EndIndex < start || gap.StartIndex > end) continue;

                        float gx1 = chartLeft + (float)((Math.Max(gap.StartIndex, start) - start) / (double)count * chartW);
                        float gx2 = chartLeft + (float)((Math.Min(gap.EndIndex, end) - start) / (double)count * chartW);

                        // Ensure minimum visible width
                        if (gx2 - gx1 < 3) gx2 = gx1 + 3;

                        var gapRect = new SKRect(gx1, chartTop, gx2, chartBottom);
                        canvas.DrawRect(gapRect, gapFillPaint);
                        canvas.DrawRect(gapRect, gapBorderPaint);

                        // Draw gap label at top
                        if (!string.IsNullOrEmpty(gap.Duration))
                        {
                            string label = $"GAP {gap.Duration}";
                            float tw = gapTextPaint.MeasureText(label);
                            float labelX = gx1 + (gx2 - gx1) / 2 - tw / 2;
                            if (tw < (gx2 - gx1) - 4)
                            {
                                canvas.DrawText(label, labelX, chartTop + 12, gapTextPaint);
                            }
                            else
                            {
                                canvas.DrawText("GAP", gx1 + 2, chartTop + 12, gapTextPaint);
                            }
                        }
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
                var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;

                if (s.YAxisType == AxisType.Right)
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                        {
                            if (dataToDraw[i] < rMin) rMin = dataToDraw[i];
                            if (dataToDraw[i] > rMax) rMax = dataToDraw[i];
                            hasRight = true;
                        }
                    }
                }
                else
                {
                    for (int i = start; i <= end; i += step)
                    {
                        if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                        {
                            if (dataToDraw[i] < lMin) lMin = dataToDraw[i];
                            if (dataToDraw[i] > lMax) lMax = dataToDraw[i];
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

            // Thread Message Markers (vertical dashed lines with triangles at top)
            if (_threadMessages != null && _threadMessages.Count > 0)
            {
                // Group messages by thread to assign consistent colors
                var threadColorMap = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase);
                int colorIdx = 0;

                foreach (var msg in _threadMessages)
                {
                    if (msg.TimeIndex < start || msg.TimeIndex > end) continue;

                    // Get or assign color for this thread
                    if (!threadColorMap.TryGetValue(msg.ThreadName, out SKColor markerColor))
                    {
                        markerColor = ThreadMarkerColors[colorIdx % ThreadMarkerColors.Length];
                        threadColorMap[msg.ThreadName] = markerColor;
                        colorIdx++;
                    }

                    float x = chartLeft + (float)((msg.TimeIndex - start) / (double)count * chartW);

                    // Draw dashed vertical line
                    using (var linePaint = new SKPaint
                    {
                        Color = markerColor,
                        StrokeWidth = 1.5f,
                        Style = SKPaintStyle.Stroke,
                        PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
                        IsAntialias = true
                    })
                    {
                        canvas.DrawLine(x, chartTop, x, chartBottom, linePaint);
                    }

                    // Draw triangle marker at top
                    using (var trianglePaint = new SKPaint { Color = markerColor, Style = SKPaintStyle.Fill, IsAntialias = true })
                    {
                        var trianglePath = new SKPath();
                        trianglePath.MoveTo(x, chartTop);
                        trianglePath.LineTo(x - 5, chartTop - 8);
                        trianglePath.LineTo(x + 5, chartTop - 8);
                        trianglePath.Close();
                        canvas.DrawPath(trianglePath, trianglePaint);
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
                    var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                    paint.Color = s.Color;
                    path.Reset();
                    bool first = true;

                    double currentMin = (s.YAxisType == AxisType.Right) ? rDisplayMin : lDisplayMin;
                    double currentRange = (s.YAxisType == AxisType.Right) ? rRange : lRange;
                    int drawStep = Math.Max(1, count / (int)chartW);

                    if (drawStep > 2)
                    {
                        // Min/Max decimation: for each pixel bucket, find min and max to preserve spikes
                        for (int bucket = start; bucket <= drawLimit; bucket += drawStep)
                        {
                            double minVal = double.MaxValue, maxVal = double.MinValue;
                            int minIdx = bucket, maxIdx = bucket;
                            int bucketEnd = Math.Min(bucket + drawStep, drawLimit + 1);
                            for (int j = bucket; j < bucketEnd && j < dataToDraw.Length; j++)
                            {
                                double v = dataToDraw[j];
                                if (double.IsNaN(v)) continue;
                                if (v < minVal) { minVal = v; minIdx = j; }
                                if (v > maxVal) { maxVal = v; maxIdx = j; }
                            }
                            if (minVal == double.MaxValue) { first = true; continue; }

                            float x = chartLeft + (float)((bucket - start) / (double)count * chartW);
                            float yMin = chartBottom - (float)((minVal - currentMin) / currentRange * chartH);
                            float yMax = chartBottom - (float)((maxVal - currentMin) / currentRange * chartH);

                            // Draw min first, then max (or vice versa) to preserve waveform shape
                            if (minIdx <= maxIdx)
                            {
                                if (first) { path.MoveTo(x, yMin); first = false; } else path.LineTo(x, yMin);
                                if (yMin != yMax) path.LineTo(x, yMax);
                            }
                            else
                            {
                                if (first) { path.MoveTo(x, yMax); first = false; } else path.LineTo(x, yMax);
                                if (yMin != yMax) path.LineTo(x, yMin);
                            }
                        }
                    }
                    else
                    {
                        // Zoomed in: draw every point
                        for (int i = start; i <= drawLimit; i += drawStep)
                        {
                            if (i >= dataToDraw.Length) break;
                            double val = dataToDraw[i];
                            if (double.IsNaN(val)) { first = true; continue; }

                            float x = chartLeft + (float)((i - start) / (double)count * chartW);
                            float y = chartBottom - (float)((val - currentMin) / currentRange * chartH);

                            if (first) { path.MoveTo(x, y); first = false; }
                            else path.LineTo(x, y);
                        }
                    }

                    if (!first) canvas.DrawPath(path, paint);
                }
                canvas.Restore();
            }

            // Event Markers (Red Dots on X-axis timeline)
            _hoveredEventIndex = -1;
            if (_chartEventMarkers != null && _chartEventMarkers.Count > 0)
            {
                float eventY = chartBottom - 8; // Position dots near the bottom of the chart area

                foreach (var evt in _chartEventMarkers)
                {
                    if (evt.Index < start || evt.Index > end) continue;

                    float ex = chartLeft + (float)((evt.Index - start) / (double)count * chartW);

                    // Draw red dot
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint);
                    canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint);

                    // Check if mouse is hovering near this event dot
                    {
                        float dx = (float)_hoverPos.X - ex;
                        float dy = (float)_hoverPos.Y - eventY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (dist < EVENT_DOT_RADIUS * 4)
                        {
                            _hoveredEventIndex = evt.Index;
                        }
                    }
                }

                // Draw tooltip for hovered event
                if (_hoveredEventIndex >= 0)
                {
                    var hoveredEvent = _chartEventMarkers.FirstOrDefault(e => e.Index == _hoveredEventIndex);
                    if (hoveredEvent != null)
                    {
                        float hx = chartLeft + (float)((hoveredEvent.Index - start) / (double)count * chartW);

                        // Draw a larger highlight circle
                        using (var highlightPaint = new SKPaint
                        {
                            Color = SKColors.Red.WithAlpha(60),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, highlightPaint);
                        }

                        // Build tooltip text
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
                        if (!string.IsNullOrEmpty(hoveredEvent.Description))
                            sb.AppendLine($"Source: {hoveredEvent.Description}");

                        float tooltipX = hx + 15;
                        float tooltipY = eventY - 40;
                        DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                    }
                }
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
                            var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                            double sum = 0, mn = double.MaxValue, mx = double.MinValue;
                            int c = 0;

                            for (int i = mS; i <= mE; i++)
                            {
                                if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                                {
                                    double v = dataToDraw[i];
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
                            var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;

                            if (_ctrlPoint1 < dataToDraw.Length && _ctrlPoint2 < dataToDraw.Length)
                            {
                                double v1 = dataToDraw[_ctrlPoint1];
                                double v2 = dataToDraw[_ctrlPoint2];

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

            // State hover tooltip (shows rich CHStep data or basic state info)
            if (_hoveredState.HasValue)
            {
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
