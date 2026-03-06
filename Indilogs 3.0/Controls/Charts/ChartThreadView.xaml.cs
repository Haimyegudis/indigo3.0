using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartThreadView : UserControl
    {
        // Cached SKTypeface instances — avoid re-creating on every render/tooltip
        private static readonly SKTypeface s_segoeNormal = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        private static readonly SKTypeface s_consolas = SKTypeface.FromFamilyName("Consolas");
        private static readonly SKTypeface s_consolasBold = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);

        public event Action<int>? OnTimeClicked;
        public event Action<int, int>? OnViewRangeChanged;
        public event Action<int>? OnCursorMoved;

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
        private bool _isSyncing = false;
        private Point _lastMousePos;

        // For X-axis labels
        public Func<int, string>? GetXAxisLabel { get; set; }

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

        // Cached colors used in render path (OnPaintSurface / Draw* helpers)
        private static readonly SKColor s_tooltipBgDark = SKColor.Parse("#DD1B2838");

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

        private SKPaint? _borderPaint;
        private SKPaint? _textPaint;
        private SKFont? _textFont;
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        private SKPaint? _gridPaint;

        // Cached paints for render-loop (avoid per-frame allocations)
        private readonly SKPaint _centerLinePaint = new SKPaint { StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _markerPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        private readonly SKFont _labelFont = new SKFont(s_consolasBold, 9);
        private readonly SKPaint _linePaint = new SKPaint { StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _highlightPaint = new SKPaint { Color = SKColors.Red.WithAlpha(60), Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Color = s_tooltipBgDark, Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        private readonly SKFont _tooltipTextFont = new SKFont(s_consolas, 11);
        private readonly SKPaint _axisPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _axisFont = new SKFont { Size = 9 };

        // Event marker support
        private List<EventMarker>? _chartEventMarkers;
        private SKPaint? _eventDotPaint;
        private SKPaint? _eventDotBorderPaint;
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

        public ChartThreadView()
        {
            InitializeComponent();
            UpdateThemeColors();
        }

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

        public void SetEventMarkers(List<EventMarker>? markers)
        {
            _chartEventMarkers = markers;
            SkiaCanvas.InvalidateVisual();
        }

        private void UpdateHeight()
        {
            if (_threadNames.Count > 0)
            {
                this.Height = (_threadNames.Count * ROW_HEIGHT) + X_AXIS_HEIGHT + 10;
            }
            else
            {
                this.Height = 50;
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
            _cursorIndex = index;
            SkiaCanvas.InvalidateVisual();
        }

        private class MessageHitArea
        {
            public ThreadMessageData Message { get; set; } = null!;
            public float X;
            public float Top;
            public float Bottom;
            public SKRect Rect;
        }
    }
}
