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

        public IEnumerable<TimelineState>? States { get => (IEnumerable<TimelineState>?)GetValue(StatesProperty); set => SetValue(StatesProperty, value); }
        public IEnumerable<TimelineMarker>? Markers { get => (IEnumerable<TimelineMarker>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
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

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        public event EventHandler<TimelineState>? StateClicked;
        public event EventHandler<TimelineMarker>? MarkerClicked;

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
        private DispatcherTimer? _hoverTimer;
        private object? _currentHoverObject;
        private bool _showTooltip;

        // Collection change debounce timer
        private DispatcherTimer? _collectionUpdateTimer;

        // Cached time range (avoid recomputing Min/Max every frame)
        private DateTime _cachedMinTime;
        private DateTime _cachedMaxTime;
        private double _cachedTotalSeconds;
        private List<TimelineState>? _cachedStates;
        private List<TimelineMarker>? _cachedMarkers;

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
        private SKPaint _gradientPaint = null!;
        private SKPaint _glowPaint = null!;
        private SKPaint _edgePaint = null!;
        private SKPaint _highlightBorderPaint = null!;
        private SKPaint _labelPaint = null!;
        private SKFont _labelFont = null!;
        private SKPaint _axisPaint = null!;
        private SKPaint _axisTextPaint = null!;
        private SKFont _axisTextFont = null!;

        // Cached paints for draw helpers (DrawHazardPattern, DrawErrorMarker, DrawEventMarker, DrawTooltip)
        private readonly SKPaint _hazardStripePaint = new SKPaint { Color = new SKColor(255, 255, 255, 60), StrokeWidth = 4, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _hazardXPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 3, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _markerGlowPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _markerFillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _markerBorderPaint = new SKPaint { Color = SKColors.White.WithAlpha(180), StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _markerXPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _tooltipTextFont = new SKFont { Size = 11 };
        private readonly SKPaint _tooltipShadowPaint = new SKPaint { Color = SKColor.Parse("#60000000"), Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        private readonly SKPaint _tooltipAccentPaint = new SKPaint { Style = SKPaintStyle.Fill };

        // Material Design color palette for PLC states
        private static readonly SKColor StateColorReady = SKColor.Parse("#26A69A");
        private static readonly SKColor StateColorError = SKColor.Parse("#EF5350");
        private static readonly SKColor StateColorInit = SKColor.Parse("#FFA726");
        private static readonly SKColor StateColorPrint = SKColor.Parse("#42A5F5");
        private static readonly SKColor StateColorDynamic = SKColor.Parse("#66BB6A");
        private static readonly SKColor StateColorStandby = SKColor.Parse("#AB47BC");
        private static readonly SKColor StateColorDefault = SKColor.Parse("#5C6BC0");

        // Cached colors used in render path (OnPaintSurface / Draw* helpers)
        private static readonly SKColor s_criticalFailureColor = SKColor.Parse("#B71C1C");
        private static readonly SKColor s_successBorderColor = SKColor.Parse("#4CAF50");
        private static readonly SKColor s_darkTextColor = SKColor.Parse("#333333");
        private static readonly SKColor s_eventMarkerCyan = SKColor.Parse("#00BCD4");
        private static readonly SKColor s_zoomBorderColor = SKColor.Parse("#64B5F6");
        private static readonly SKColor s_errorGradientTop = SKColor.Parse("#FF5252");
        private static readonly SKColor s_shadowColor = SKColor.Parse("#60000000");
        private static readonly SKColor s_tooltipBgLight = SKColor.Parse("#F0FFFFFF");
        private static readonly SKColor s_tooltipBgDark = SKColor.Parse("#F01B2838");

        // Cached typefaces for rendering
        private static readonly SKTypeface s_consolas = SKTypeface.FromFamilyName("Consolas");
        private static readonly SKTypeface s_segoeUIBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        private static readonly SKTypeface s_segoeUI = SKTypeface.FromFamilyName("Segoe UI");

        // Cached theme colors (avoid SKColor.Parse per theme switch)
        private static readonly SKColor s_lightBg = SKColor.Parse("#FFFFFF");
        private static readonly SKColor s_lightBorder = SKColor.Parse("#DDDDDD");
        private static readonly SKColor s_lightText = SKColor.Parse("#333333");
        private static readonly SKColor s_lightGrid = SKColor.Parse("#E0E0E0");
        private static readonly SKColor s_darkBg = SKColor.Parse("#0A121E");
        private static readonly SKColor s_darkBorder = SKColor.Parse("#2D4A6F");
        private static readonly SKColor s_darkGrid = SKColor.Parse("#1B3A5C");

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
            this.Unloaded += (s, e) =>
            {
                _hoverTimer?.Stop();
                _collectionUpdateTimer?.Stop();
            };
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
            _labelPaint = new SKPaint { IsAntialias = true };
            _labelFont = new SKFont(s_segoeUIBold, 12);
            _axisPaint = new SKPaint { StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            _axisTextPaint = new SKPaint { IsAntialias = true };
            _axisTextFont = new SKFont(s_segoeUI, 11);
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
                _bgColor = s_lightBg;
                _borderColor = s_lightBorder;
                _textColor = s_lightText;
                _gridColor = s_lightGrid;
            }
            else
            {
                _bgColor = s_darkBg;
                _borderColor = s_darkBorder;
                _textColor = SKColors.White;
                _gridColor = s_darkGrid;
            }
        }

        #endregion

        #region Color Helpers

        internal static SKColor GetMaterialColorForState(string? name)
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

        private static SKColor LightenColor(SKColor c, float amount) => SkiaColorHelpers.LightenColor(c, amount);
        private static SKColor DarkenColor(SKColor c, float amount) => SkiaColorHelpers.DarkenColor(c, amount);

        #endregion

        #region Tooltip

        private void OnHoverTimerTick(object? sender, EventArgs e)
        {
            _hoverTimer!.Stop();
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

    }
}
