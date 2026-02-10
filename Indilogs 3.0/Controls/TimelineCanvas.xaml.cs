using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public partial class TimelineCanvas : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty StatesProperty = DependencyProperty.Register(
            "States", typeof(IEnumerable<TimelineState>), typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, OnStatesOrMarkersChanged));

        public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
            "Markers", typeof(IEnumerable<TimelineMarker>), typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, OnStatesOrMarkersChanged));

        public static readonly DependencyProperty ViewScaleProperty = DependencyProperty.Register(
            "ViewScale", typeof(double), typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));

        public static readonly DependencyProperty ViewOffsetProperty = DependencyProperty.Register(
            "ViewOffset", typeof(double), typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));

        public IEnumerable<TimelineState> States { get => (IEnumerable<TimelineState>)GetValue(StatesProperty); set => SetValue(StatesProperty, value); }
        public IEnumerable<TimelineMarker> Markers { get => (IEnumerable<TimelineMarker>)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
        public double ViewScale { get => (double)GetValue(ViewScaleProperty); set => SetValue(ViewScaleProperty, value); }
        public double ViewOffset { get => (double)GetValue(ViewOffsetProperty); set => SetValue(ViewOffsetProperty, value); }

        private static void OnStatesOrMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineCanvas tc)
            {
                // Unsubscribe from old collection
                if (e.OldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= tc.OnCollectionChanged;

                // Subscribe to new collection for live updates (ObservableCollection)
                if (e.NewValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += tc.OnCollectionChanged;

                tc.CacheTimeRange();
                tc.SkiaCanvas?.InvalidateVisual();
            }
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // On Reset (Clear()), immediately redraw — don't debounce
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _collectionUpdateTimer?.Stop();
                CacheTimeRange();
                SkiaCanvas?.InvalidateVisual();
                return;
            }

            // For bulk adds, use a timer to batch updates (debounce 50ms)
            if (_collectionUpdateTimer == null)
            {
                _collectionUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _collectionUpdateTimer.Tick += (s, args) =>
                {
                    _collectionUpdateTimer.Stop();
                    CacheTimeRange();
                    SkiaCanvas?.InvalidateVisual();
                };
            }
            _collectionUpdateTimer.Stop();
            _collectionUpdateTimer.Start();
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineCanvas tc && tc.SkiaCanvas != null)
                tc.SkiaCanvas.InvalidateVisual();
        }

        #endregion

        #region Events

        public event EventHandler<TimelineState> StateClicked;
        public event EventHandler<TimelineMarker> MarkerClicked;

        #endregion

        #region Fields

        // Interaction
        private bool _isDragging = false;
        private bool _isZooming = false;
        private Point _dragStart;
        private double _dragStartOffset;
        private Point _zoomStartPoint;
        private Point _currentMousePos;

        // Tooltip
        private DispatcherTimer _hoverTimer;
        private object _currentHoverObject;
        private bool _showTooltip;

        // Collection change debounce timer
        private DispatcherTimer _collectionUpdateTimer;

        // Cached time range (avoid recomputing Min/Max every frame)
        private DateTime _cachedMinTime;
        private DateTime _cachedMaxTime;
        private double _cachedTotalSeconds;
        private List<TimelineState> _cachedStates;
        private List<TimelineMarker> _cachedMarkers;

        // DPI scale factor
        private float _dpiScale = 1f;

        // Layout constants (in DPI-independent units, will be scaled)
        private const float TIMELINE_Y = 50f;
        private const float BAR_HEIGHT = 40f;
        private const float MARKER_AREA = 20f;
        private const float TIME_AXIS_HEIGHT = 30f;

        // Theme
        private bool _isLightTheme = false;
        private SKColor _bgColor;
        private SKColor _borderColor;
        private SKColor _textColor;
        private SKColor _gridColor;

        // Reusable paints (avoid allocations per frame)
        private SKPaint _gradientPaint;
        private SKPaint _glowPaint;
        private SKPaint _edgePaint;
        private SKPaint _highlightBorderPaint;
        private SKPaint _labelPaint;
        private SKPaint _axisPaint;
        private SKPaint _axisTextPaint;

        // Material Design color palette for PLC states
        private static readonly SKColor StateColorReady = SKColor.Parse("#26A69A");
        private static readonly SKColor StateColorError = SKColor.Parse("#EF5350");
        private static readonly SKColor StateColorInit = SKColor.Parse("#FFA726");
        private static readonly SKColor StateColorPrint = SKColor.Parse("#42A5F5");
        private static readonly SKColor StateColorDynamic = SKColor.Parse("#66BB6A");
        private static readonly SKColor StateColorStandby = SKColor.Parse("#AB47BC");
        private static readonly SKColor StateColorDefault = SKColor.Parse("#5C6BC0");

        #endregion

        #region Constructor

        public TimelineCanvas()
        {
            InitializeComponent();
            ClipToBounds = true;
            UpdateThemeColors();
            InitializePaints();

            SkiaCanvas.MouseDown += OnMouseDown;
            SkiaCanvas.MouseMove += OnMouseMove;
            SkiaCanvas.MouseUp += OnMouseUp;
            SkiaCanvas.MouseWheel += OnMouseWheel;
            SkiaCanvas.MouseLeave += OnMouseLeave;

            _hoverTimer = new DispatcherTimer();
            _hoverTimer.Interval = TimeSpan.FromSeconds(1.5);
            _hoverTimer.Tick += OnHoverTimerTick;

            this.Loaded += (s, e) => UpdateDpiScale();
        }

        private void UpdateDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                _dpiScale = (float)source.CompositionTarget.TransformToDevice.M11;
            else
                _dpiScale = 1f;
        }

        private void InitializePaints()
        {
            _gradientPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            _glowPaint = new SKPaint { Color = SKColors.White.WithAlpha(40), Style = SKPaintStyle.Fill, IsAntialias = true };
            _edgePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
            _highlightBorderPaint = new SKPaint { Color = SKColors.White.WithAlpha(200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            _labelPaint = new SKPaint { TextSize = 12, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
            _axisPaint = new SKPaint { StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            _axisTextPaint = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        }

        #endregion

        #region Cache

        private void CacheTimeRange()
        {
            _cachedStates = States?.ToList();
            _cachedMarkers = Markers?.ToList();

            if (_cachedStates != null && _cachedStates.Count > 0)
            {
                _cachedMinTime = _cachedStates[0].StartTime;
                _cachedMaxTime = _cachedStates[0].EndTime;
                for (int i = 1; i < _cachedStates.Count; i++)
                {
                    if (_cachedStates[i].StartTime < _cachedMinTime) _cachedMinTime = _cachedStates[i].StartTime;
                    if (_cachedStates[i].EndTime > _cachedMaxTime) _cachedMaxTime = _cachedStates[i].EndTime;
                }
                _cachedTotalSeconds = (_cachedMaxTime - _cachedMinTime).TotalSeconds;
                if (_cachedTotalSeconds <= 0) _cachedTotalSeconds = 1;
            }
            else
            {
                _cachedTotalSeconds = 0;
            }
        }

        #endregion

        #region Theme

        public bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                _isLightTheme = value;
                UpdateThemeColors();
                SkiaCanvas?.InvalidateVisual();
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
                _bgColor = SKColor.Parse("#0A121E");
                _borderColor = SKColor.Parse("#2D4A6F");
                _textColor = SKColors.White;
                _gridColor = SKColor.Parse("#1B3A5C");
            }
        }

        #endregion

        #region Color Helpers

        internal static SKColor GetMaterialColorForState(string name)
        {
            if (string.IsNullOrEmpty(name)) return StateColorDefault;
            string upper = name.ToUpperInvariant();
            if (upper.Contains("ERROR") || upper.Contains("OFF") || upper.Contains("FAIL")) return StateColorError;
            if (upper.Contains("DYNAMIC")) return StateColorDynamic;
            if (upper.Contains("READY")) return StateColorReady;
            if (upper.Contains("STANDBY")) return StateColorStandby;
            if (upper.Contains("INIT")) return StateColorInit;
            if (upper.Contains("PRINT")) return StateColorPrint;
            return StateColorDefault;
        }

        private static SKColor LightenColor(SKColor c, float amount)
        {
            int r = Math.Min(255, (int)(c.Red + (255 - c.Red) * amount));
            int g = Math.Min(255, (int)(c.Green + (255 - c.Green) * amount));
            int b = Math.Min(255, (int)(c.Blue + (255 - c.Blue) * amount));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
        }

        private static SKColor DarkenColor(SKColor c, float amount)
        {
            int r = Math.Max(0, (int)(c.Red * (1 - amount)));
            int g = Math.Max(0, (int)(c.Green * (1 - amount)));
            int b = Math.Max(0, (int)(c.Blue * (1 - amount)));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
        }

        #endregion

        #region Tooltip

        private void OnHoverTimerTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();
            if (_currentHoverObject != null)
            {
                _showTooltip = true;
                SkiaCanvas.InvalidateVisual();
            }
        }

        private void HideTooltip()
        {
            _showTooltip = false;
            _currentHoverObject = null;
        }

        #endregion

        #region Paint

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
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
            object hoveredObj = null;
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

                if (isCriticalFailure) baseColor = SKColor.Parse("#B71C1C");

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
                    _edgePaint.Color = SKColor.Parse("#4CAF50");
                    _edgePaint.StrokeWidth = 2;
                    canvas.DrawRoundRect(barRoundRect, _edgePaint);
                    _edgePaint.StrokeWidth = 0.8f;
                }

                // Text label with auto-contrast
                if (barW > 30)
                {
                    string displayText = state.Name;
                    if (isCriticalFailure) displayText += " (FAILED!)";

                    float textWidth = _labelPaint.MeasureText(displayText);
                    if (textWidth > barW - 10)
                    {
                        while (displayText.Length > 3 && _labelPaint.MeasureText(displayText + "..") > barW - 10)
                            displayText = displayText.Substring(0, displayText.Length - 1);
                        displayText += "..";
                        textWidth = _labelPaint.MeasureText(displayText);
                    }

                    float brightness = (baseColor.Red * 0.299f + baseColor.Green * 0.587f + baseColor.Blue * 0.114f) / 255f;
                    _labelPaint.Color = brightness > 0.55f ? SKColor.Parse("#333333") : SKColors.White;

                    float textX = x1 + (barW - textWidth) / 2;
                    float textY = TIMELINE_Y + BAR_HEIGHT / 2 + 4.5f;
                    canvas.DrawText(displayText, textX, textY, _labelPaint);
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
                        DrawEventMarker(canvas, mxf, my, SKColor.Parse("#00BCD4"));
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
                using (var zoomBorderPaint = new SKPaint { Color = SKColor.Parse("#64B5F6"), StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true })
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
                _hoverTimer.Stop();
                _showTooltip = false;
                _currentHoverObject = hoveredObj;
                if (hoveredObj != null) _hoverTimer.Start();
            }
        }

        #endregion

        #region Drawing Helpers

        private void DrawHazardPattern(SKCanvas canvas, float x, float y, float bw, float bh, SKRoundRect clipRect)
        {
            canvas.Save();
            canvas.ClipRoundRect(clipRect);

            using (var stripePaint = new SKPaint { Color = new SKColor(255, 255, 255, 60), StrokeWidth = 4, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                float step = 15;
                for (float i = -bh; i < bw; i += step)
                    canvas.DrawLine(x + i, y, x + i + bh, y + bh, stripePaint);
            }

            canvas.Restore();

            float cx = x + bw / 2;
            float cy = y + bh / 2;
            float s = 6;
            using (var xPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 3, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round })
            {
                canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, xPaint);
                canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, xPaint);
            }
        }

        private void DrawErrorMarker(SKCanvas canvas, float x, float y)
        {
            using (var glowPaint = new SKPaint { Color = SKColors.Red.WithAlpha(60), Style = SKPaintStyle.Fill, IsAntialias = true })
                canvas.DrawCircle(x, y + 6, 12, glowPaint);

            using (var circlePaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                circlePaint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint(x, y + 4), 8,
                    new[] { SKColor.Parse("#FF5252"), SKColor.Parse("#B71C1C") },
                    null, SKShaderTileMode.Clamp);
                canvas.DrawCircle(x, y + 6, 8, circlePaint);
            }

            using (var borderPaint = new SKPaint { Color = SKColors.White.WithAlpha(180), StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true })
                canvas.DrawCircle(x, y + 6, 8, borderPaint);

            using (var xPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round })
            {
                canvas.DrawLine(x - 3, y + 3, x + 3, y + 9, xPaint);
                canvas.DrawLine(x + 3, y + 3, x - 3, y + 9, xPaint);
            }
        }

        private void DrawEventMarker(SKCanvas canvas, float x, float y, SKColor color)
        {
            using (var glowPaint = new SKPaint { Color = color.WithAlpha(50), Style = SKPaintStyle.Fill, IsAntialias = true })
                canvas.DrawCircle(x, y, 10, glowPaint);

            using (var path = new SKPath())
            {
                path.MoveTo(x, y - 6);
                path.LineTo(x + 6, y);
                path.LineTo(x, y + 6);
                path.LineTo(x - 6, y);
                path.Close();

                using (var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
                {
                    fillPaint.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(x, y - 6), new SKPoint(x, y + 6),
                        new[] { LightenColor(color, 0.2f), DarkenColor(color, 0.2f) },
                        null, SKShaderTileMode.Clamp);
                    canvas.DrawPath(path, fillPaint);
                }

                using (var borderPaint = new SKPaint { Color = SKColors.White.WithAlpha(180), StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true })
                    canvas.DrawPath(path, borderPaint);
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
                float tw = _axisTextPaint.MeasureText(label);
                canvas.DrawText(label, x - tw / 2, y + 20, _axisTextPaint);
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
                accentColor = m.Type == TimelineMarkerType.Error ? StateColorError : SKColor.Parse("#00BCD4");
                var lineList = new List<string>();
                lineList.Add(m.Type == TimelineMarkerType.Error ? "ERROR" : "EVENT");
                if (!string.IsNullOrEmpty(m.Message)) lineList.Add(m.Message);
                lineList.Add($"Time: {m.Time:HH:mm:ss.fff}");
                if (!string.IsNullOrEmpty(m.Severity)) lineList.Add($"Severity: {m.Severity}");
                lines = lineList.ToArray();
            }
            else return;

            using (var textPaint = new SKPaint { Color = _isLightTheme ? SKColor.Parse("#333333") : SKColors.White, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Consolas") })
            {
                float maxWidth = 0;
                foreach (var line in lines)
                    maxWidth = Math.Max(maxWidth, textPaint.MeasureText(line));

                float accentBarWidth = 4;
                float tooltipW = maxWidth + 20 + accentBarWidth;
                float tooltipH = lines.Length * 16 + 14;

                if (x + tooltipW > canvasW - 10) x = canvasW - tooltipW - 10;
                if (y + tooltipH > canvasH - 10) y = canvasH - tooltipH - 10;
                if (x < 5) x = 5;
                if (y < 5) y = 5;

                var tooltipRect = new SKRect(x, y, x + tooltipW, y + tooltipH);
                var tooltipRoundRect = new SKRoundRect(tooltipRect, 6, 6);

                using (var shadowPaint = new SKPaint { Color = SKColor.Parse("#60000000"), Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2, y + 2, x + tooltipW + 2, y + tooltipH + 2), 6, 6), shadowPaint);

                using (var bgPaint = new SKPaint { Color = _isLightTheme ? SKColor.Parse("#F0FFFFFF") : SKColor.Parse("#F01B2838"), Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawRoundRect(tooltipRoundRect, bgPaint);

                using (var borderPaint = new SKPaint { Color = accentColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true })
                    canvas.DrawRoundRect(tooltipRoundRect, borderPaint);

                canvas.Save();
                canvas.ClipRoundRect(tooltipRoundRect);
                using (var accentPaint = new SKPaint { Color = accentColor, Style = SKPaintStyle.Fill })
                    canvas.DrawRect(new SKRect(x, y, x + accentBarWidth, y + tooltipH), accentPaint);
                canvas.Restore();

                float ty = y + 16;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, x + accentBarWidth + 8, ty, textPaint);
                    ty += 16;
                }
            }
        }

        #endregion

        #region Coordinate Transforms

        private double TimeToX(double sec, double w, double total) =>
            ((sec - ViewOffset) / total) * w * ViewScale;

        private double XToSeconds(double x, double w, double total) =>
            (x / (w * ViewScale)) * total + ViewOffset;

        #endregion

        #region Mouse Interaction

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _isZooming = true;
                    _zoomStartPoint = e.GetPosition(SkiaCanvas);
                    SkiaCanvas.CaptureMouse();
                }
                else
                {
                    _isDragging = true;
                    _dragStart = e.GetPosition(SkiaCanvas);
                    _dragStartOffset = ViewOffset;
                    SkiaCanvas.CaptureMouse();
                    CheckObjectClick(_dragStart);
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _currentMousePos = e.GetPosition(SkiaCanvas);

            if (_isZooming)
            {
                SkiaCanvas.InvalidateVisual();
            }
            else if (_isDragging && _cachedStates != null && _cachedStates.Count > 0)
            {
                double dx = _currentMousePos.X - _dragStart.X;
                double pixelsPerSecond = (SkiaCanvas.ActualWidth * ViewScale) / _cachedTotalSeconds;
                if (pixelsPerSecond > 0)
                    ViewOffset = _dragStartOffset - (dx / pixelsPerSecond);
                SkiaCanvas.InvalidateVisual();
            }
            else
            {
                SkiaCanvas.InvalidateVisual();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isZooming && _cachedStates != null && _cachedStates.Count > 0)
            {
                double x1 = Math.Min(_zoomStartPoint.X, _currentMousePos.X);
                double x2 = Math.Max(_zoomStartPoint.X, _currentMousePos.X);
                double width = x2 - x1;

                if (width > 10)
                {
                    double t1 = XToSeconds(x1, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
                    double t2 = XToSeconds(x2, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
                    double newRange = t2 - t1;
                    if (newRange > 0)
                    {
                        ViewScale = _cachedTotalSeconds / newRange;
                        ViewOffset = t1;
                    }
                }
                _isZooming = false;
                SkiaCanvas.InvalidateVisual();
            }
            _isDragging = false;
            SkiaCanvas.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_cachedStates == null || _cachedStates.Count == 0) return;

            Point mousePos = e.GetPosition(SkiaCanvas);
            if (_cachedTotalSeconds <= 0) return;

            double mouseTimeBefore = XToSeconds(mousePos.X, SkiaCanvas.ActualWidth, _cachedTotalSeconds);
            double zoomFactor = e.Delta > 0 ? 1.2 : 0.8;
            ViewScale *= zoomFactor;
            if (ViewScale < 0.1) ViewScale = 0.1;

            double mouseTimeAfter = (mousePos.X / (SkiaCanvas.ActualWidth * ViewScale)) * _cachedTotalSeconds;
            ViewOffset = mouseTimeBefore - mouseTimeAfter;

            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            _isZooming = false;
            HideTooltip();
            _hoverTimer.Stop();
            SkiaCanvas.InvalidateVisual();
        }

        private void CheckObjectClick(Point p)
        {
            var hit = GetHitObject(p);
            if (hit is TimelineState s) StateClicked?.Invoke(this, s);
            else if (hit is TimelineMarker m) MarkerClicked?.Invoke(this, m);
        }

        private object GetHitObject(Point p)
        {
            if (_cachedStates == null || _cachedStates.Count == 0) return null;

            double w = SkiaCanvas.ActualWidth;

            if (_cachedMarkers != null)
            {
                foreach (var m in _cachedMarkers)
                {
                    double mx = TimeToX((m.Time - _cachedMinTime).TotalSeconds, w, _cachedTotalSeconds);
                    double my = m.Type == TimelineMarkerType.Error ? TIMELINE_Y - MARKER_AREA + 6 : TIMELINE_Y + BAR_HEIGHT + 8;
                    if (Math.Abs(p.X - mx) < 10 && Math.Abs(p.Y - my) < 10) return m;
                }
            }

            if (p.Y >= TIMELINE_Y && p.Y <= TIMELINE_Y + BAR_HEIGHT)
            {
                double timeClicked = XToSeconds(p.X, w, _cachedTotalSeconds);
                for (int i = 0; i < _cachedStates.Count; i++)
                {
                    var s = _cachedStates[i];
                    double sStart = (s.StartTime - _cachedMinTime).TotalSeconds;
                    double sEnd = (s.EndTime - _cachedMinTime).TotalSeconds;
                    if (timeClicked >= sStart && timeClicked <= sEnd) return s;
                }
            }
            return null;
        }

        #endregion
    }
}
