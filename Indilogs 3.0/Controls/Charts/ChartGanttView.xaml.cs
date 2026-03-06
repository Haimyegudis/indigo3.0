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

        public event Action<int>? OnTimeClicked;
        public event Action<int, int>? OnViewRangeChanged;
        public event Action<int>? OnCursorMoved;

        private List<StateData> _stateDataList = new List<StateData>();
        private int _totalDataLength = 0;
        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _cursorIndex = -1;
        private bool _isLightTheme = false;

        // For X-axis labels
        public Func<int, string>? GetXAxisLabel { get; set; }

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

        private SKPaint _borderPaint = null!;
        private SKPaint _textPaint = null!;
        private SKFont _textFont = null!;
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private SKPaint _gridPaint = null!;

        // ── Cached paints for hot render loop (interval drawing) ──
        private readonly SKPaint _rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _gradientPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _glowPaint = new SKPaint { Color = SKColors.White.WithAlpha(40), Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _edgePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        private readonly SKPaint _highlightBorderPaint = new SKPaint { Color = SKColors.White.WithAlpha(200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        private readonly SKPaint _intervalLabelPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _intervalLabelFont = new SKFont(s_segoeBold, 9);

        // ── Cached paints for labels, tooltips, cursors ──
        private readonly SKPaint _labelPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _labelFont = new SKFont(s_segoeNormal, 10);
        private readonly SKPaint _axisPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _axisFont = new SKFont { Size = 9 };
        private readonly SKPaint _cursorGlowPaint = new SKPaint { Color = CursorColor.WithAlpha(40), StrokeWidth = 6, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _eventHighlightPaint = new SKPaint { Color = SKColors.Red.WithAlpha(60), Style = SKPaintStyle.Fill, IsAntialias = true };

        // Tooltip paints (shared between label tooltip, event tooltip, CHStep tooltip)
        private readonly SKPaint _tooltipShadowPaint = new SKPaint { Color = s_shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _tooltipTextFont = new SKFont(s_consolas, 11);
        private readonly SKPaint _tooltipAccentPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        // Label tooltip uses a separate text paint (Segoe Bold, size 11) and a separate border paint (accent blue)
        private readonly SKPaint _labelTooltipTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _labelTooltipTextFont = new SKFont(s_segoeBold, 11);
        private readonly SKPaint _labelTooltipBorderPaint = new SKPaint { Color = s_accentBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };

        // Drag/Pan support
        private bool _isDragging = false;
        private bool _isSyncing = false;
        private Point _lastMousePos;

        // Event marker support
        private List<EventMarker>? _chartEventMarkers;
        private SKPaint _eventDotPaint = null!;
        private SKPaint _eventDotBorderPaint = null!;
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
            _textFont?.Dispose();
            _gridPaint?.Dispose();
            _eventDotPaint?.Dispose();
            _eventDotBorderPaint?.Dispose();

            _borderPaint = new SKPaint { Color = _borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            _textPaint = new SKPaint { Color = _textColor, IsAntialias = true };
            _textFont = new SKFont(s_segoeNormal, 10);
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


    }
}
