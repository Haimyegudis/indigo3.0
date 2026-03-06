using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.Collections.ObjectModel;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView : UserControl
    {
        // Cached SKTypeface instances — avoid re-creating on every render/tooltip
        private static readonly SKTypeface s_segoeNormal = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        private static readonly SKTypeface s_segoeBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

        // Events for synchronization
        public event Action<int, int>? OnViewRangeChanged;
        public event Action<int>? OnCursorMoved;
        public event Action? OnChartClicked;
        public event Action<int>? OnTimeClicked; // For log synchronization

        public Func<int, string>? GetXAxisLabel { get; set; }

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
        private SKPaint _gridLinePaint = null!;
        private SKPaint _axisLinePaint = null!;
        private SKPaint _textPaintLeft = null!;
        private readonly SKFont _textFontLeft = new SKFont { Size = 11, Typeface = s_segoeNormal };
        private SKPaint _textPaintRight = null!;
        private readonly SKFont _textFontRight = new SKFont { Size = 11, Typeface = s_segoeBold };
        private SKPaint _stateTextPaint = null!;
        private readonly SKFont _stateTextFont = new SKFont { Size = 12, Typeface = s_segoeBold };
        private SKPaint _stateFillPaint = null!;
        private SKPaint _targetLinePaint = null!;
        private SKPaint _cursorLinePaint = null!;
        private SKPaint _measureFillPaint = null!;
        private SKPaint _measureBorderPaint = null!;

        private List<SignalSeries> _seriesList = new List<SignalSeries>();
        private ObservableCollection<ReferenceLine>? _referenceLines;
        private List<StateInterval>? _states;
        private List<ThreadMessageData> _threadMessages = new List<ThreadMessageData>();
        private Dictionary<string, SKColor> _threadColorMap = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase);
        private readonly SKPaint _threadLinePaint = new SKPaint { StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _threadTrianglePaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private SKPathEffect? _threadDashEffect;
        private List<EventMarkerData> _eventMarkers = new List<EventMarkerData>();
        private List<EventMarker>? _chartEventMarkers;
        private List<GapRegion> _timeGaps = new List<GapRegion>();

        // Event marker paints and rendering
        private SKPaint _eventDotPaint = null!;
        private SKPaint _eventDotBorderPaint = null!;
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

        // Cached SKColor.Parse values used in render path
        private static readonly SKColor s_gapFillColor = SKColor.Parse("#30FF4444");
        private static readonly SKColor s_gapBorderColor = SKColor.Parse("#80FF4444");
        private static readonly SKColor s_gapTextColor = SKColor.Parse("#FF6B6B");
        private static readonly SKColor s_tooltipBgDark = SKColor.Parse("#1E3A5F");
        private static readonly SKColor s_tooltipBgLight = SKColor.Parse("#FFFFFF");
        private static readonly SKColor s_tooltipTextLight = SKColor.Parse("#333333");

        // Cached paints for time gap rendering (reused per-frame)
        private readonly SKPaint _gapFillPaint = new SKPaint { Color = s_gapFillColor, Style = SKPaintStyle.Fill };
        private readonly SKPaint _gapBorderPaint = new SKPaint
        {
            Color = s_gapBorderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        private readonly SKPaint _gapTextPaint;
        private readonly SKFont _gapTextFont = new SKFont { Size = 10, Typeface = s_segoeBold };

        // Cached path effect for gap borders
        private readonly SKPathEffect _gapDashEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0);

        // Cached paints for reference lines (color/pathEffect updated per-line)
        private readonly SKPaint _refLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _refLineTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _refLineTextFont = new SKFont { Size = 11, Typeface = s_segoeBold };

        // Cached path effect for dashed reference lines
        private readonly SKPathEffect _refDashEffect = SKPathEffect.CreateDash(new float[] { 10, 5 }, 0);

        // Cached paint for signal lines (color updated per-series)
        private readonly SKPaint _signalLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };

        // Cached paint for event highlight circle
        private readonly SKPaint _eventHighlightPaint = new SKPaint
        {
            Color = SKColors.Red.WithAlpha(60),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        // Cached paints for Ctrl+Click measurement
        private readonly SKPaint _ctrlMeasurePaint = new SKPaint { Color = SKColors.LimeGreen, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _ctrlMeasureDashPaint = new SKPaint { Color = SKColors.LimeGreen, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private readonly SKPathEffect _ctrlDashEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0);

        // Cached paints for DrawTooltip (colors updated per-call)
        private readonly SKPaint _tooltipMeasurePaint = new SKPaint();
        private readonly SKFont _tooltipMeasureFont = new SKFont { Size = 11, Typeface = s_segoeNormal };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        private readonly SKPaint _tooltipShadowPaint = new SKPaint { Color = SKColors.Black.WithAlpha(80), Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _tooltipTextFont = new SKFont { Size = 11, Typeface = s_segoeNormal };

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

            // Initialize paints that need constructor-time setup
            _gapTextPaint = new SKPaint
            {
                Color = s_gapTextColor,
                IsAntialias = true
            };
            _gapBorderPaint.PathEffect = _gapDashEffect;
            _ctrlMeasureDashPaint.PathEffect = _ctrlDashEffect;

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

            // Dispose previous paints before creating new ones (avoid memory leaks)
            _gridLinePaint?.Dispose();
            _axisLinePaint?.Dispose();
            _textPaintLeft?.Dispose();
            _textPaintRight?.Dispose();
            _stateTextPaint?.Dispose();
            _stateFillPaint?.Dispose();
            _targetLinePaint?.Dispose();
            _cursorLinePaint?.Dispose();
            _measureFillPaint?.Dispose();
            _measureBorderPaint?.Dispose();
            _eventDotPaint?.Dispose();
            _eventDotBorderPaint?.Dispose();

            _gridLinePaint = new SKPaint { Color = _gridColor.WithAlpha(80), IsAntialias = false, StrokeWidth = 1 };
            _axisLinePaint = new SKPaint { Color = _gridColor, IsAntialias = false, StrokeWidth = 1 };
            _textPaintLeft = new SKPaint { Color = _textColor, IsAntialias = true };
            _textPaintRight = new SKPaint { Color = _accentColor, IsAntialias = true };
            _stateTextPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
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

            // Rebuild thread color map when data changes (avoid per-frame allocations)
            _threadColorMap.Clear();
            int colorIdx = 0;
            foreach (var msg in _threadMessages)
            {
                if (!_threadColorMap.ContainsKey(msg.ThreadName))
                {
                    _threadColorMap[msg.ThreadName] = ThreadMarkerColors[colorIdx % ThreadMarkerColors.Length];
                    colorIdx++;
                }
            }

            // Cache dash effect once
            _threadDashEffect?.Dispose();
            _threadDashEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0);

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

        private void Series_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
    }
}
