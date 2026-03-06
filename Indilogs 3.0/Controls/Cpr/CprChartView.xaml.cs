using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndiLogs_3._0.Models.Cpr;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace IndiLogs_3._0.Controls.Cpr
{
    public partial class CprChartView : UserControl
    {
        private const float LEFT_MARGIN = 60;
        private const float RIGHT_MARGIN = 55;
        private const float TOP_MARGIN = 30;
        private const float BOTTOM_MARGIN = 25;
        private const float LEGEND_LINE_HEIGHT = 14;

        private CprGraphResult? _graphResult;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;
        private Point _hoverPos;
        private bool _showHover;

        // Theme colors
        private SKColor _bgColor = SKColor.Parse("#1B2838");
        private SKColor _gridColor = SKColor.Parse("#2D4A6F");
        private SKColor _textColor = SKColors.White;

        // Zoom state
        private double? _zoomXMin, _zoomXMax, _zoomYMin, _zoomYMax;
        private double _dataXMin, _dataXMax, _dataYMin, _dataYMax; // full data range

        // Cached typefaces (static — shared across all instances, never changes)
        private static readonly SKTypeface s_segoeNormal = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        private static readonly SKTypeface s_segoeBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

        // Cached colors for histogram
        private static readonly SKColor s_histBarColor = SKColor.Parse("#3B82F6").WithAlpha(180);

        // Cached paints for hot render path (DrawSingleSubplot + DrawSubplotYAxis — called N times per frame)
        private readonly SKPaint _subplotTitlePaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _subplotTitleFont = new SKFont();
        private readonly SKPaint _subplotBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
        private readonly SKPaint _subplotDotPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _subplotLinePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _subplotAxisTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _subplotAxisTextFont = new SKFont();

        // Cached paints for histogram and curve rendering
        private readonly SKPaint _histBarPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        private readonly SKPaint _histCurvePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Red, StrokeWidth = 2 };

        // Cached paints for grid, axis, labels, tooltips
        private readonly SKPaint _gridPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _axisPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _axisTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _axisTextFont = new SKFont { Size = 10 };
        private readonly SKPaint _axisLabelPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _axisLabelFont = new SKFont { Size = 10 };
        private readonly SKPaint _zeroLinePaint = new SKPaint { IsAntialias = false, StrokeWidth = 1.5f };
        private readonly SKPaint _dftMarkerPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _dftLabelPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _dftLabelFont = new SKFont { Size = 9 };

        // Cached paints for hover tooltip and crosshair
        private readonly SKPaint _crosshairPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _tooltipTextFont = new SKFont { Size = 10 };
        private readonly SKPaint _tooltipColorPaint = new SKPaint { IsAntialias = false, StrokeWidth = 2 };
        private readonly SKPaint _noDataPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _noDataFont = new SKFont { Size = 16 };

        // Zoom event for sync
        public event Action<double, double, double, double>? ZoomChanged;

        public CprChartView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            };
            SkiaCanvas.MouseMove += OnMouseMove;
            SkiaCanvas.MouseLeave += OnMouseLeave;
            SkiaCanvas.MouseWheel += OnMouseWheel;
            SkiaCanvas.MouseRightButtonDown += OnMouseRightButtonDown;
        }

        public void SetGraphResult(CprGraphResult? result)
        {
            _graphResult = result;
            // Reset zoom when new data arrives
            _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
            SkiaCanvas.InvalidateVisual();
        }

        public void Refresh()
        {
            SkiaCanvas.InvalidateVisual();
        }

        public void SetThemeColors(System.Windows.Media.Color bg, System.Windows.Media.Color grid, System.Windows.Media.Color text)
        {
            _bgColor = new SKColor(bg.R, bg.G, bg.B, bg.A);
            _gridColor = new SKColor(grid.R, grid.G, grid.B, grid.A);
            _textColor = new SKColor(text.R, text.G, text.B, text.A);
            SkiaCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Set zoom range from external source (for sync between charts)
        /// </summary>
        public void SetZoomRange(double xMin, double xMax, double yMin, double yMax)
        {
            _zoomXMin = xMin;
            _zoomXMax = xMax;
            _zoomYMin = yMin;
            _zoomYMax = yMax;
            SkiaCanvas.InvalidateVisual();
        }

        #region Mouse Events

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(SkiaCanvas);
            _hoverPos = new Point(pos.X * _dpiScaleX, pos.Y * _dpiScaleY);
            _showHover = true;
            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeave(object? sender, MouseEventArgs e)
        {
            _showHover = false;
            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (_graphResult == null) return;
            if (_graphResult.GraphType == CprGraphType.Skew || _graphResult.GraphType == CprGraphType.Histogram) return;

            var pos = e.GetPosition(SkiaCanvas);
            float mx = (float)(pos.X * _dpiScaleX);
            float my = (float)(pos.Y * _dpiScaleY);

            // Get current view bounds
            double xMin = _zoomXMin ?? _dataXMin;
            double xMax = _zoomXMax ?? _dataXMax;
            double yMin = _zoomYMin ?? _dataYMin;
            double yMax = _zoomYMax ?? _dataYMax;

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;

            if (xRange < 1e-10 || yRange < 1e-10) return;

            // Determine chart area from current render
            float w = (float)(SkiaCanvas.ActualWidth * _dpiScaleX);
            float h = (float)(SkiaCanvas.ActualHeight * _dpiScaleY);
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            // Mouse position in data coordinates
            double dataX = xMin + (mx - chartLeft) / chartW * xRange;
            double dataY = yMin + (chartBottom - my) / chartH * yRange;

            // Zoom factor
            double factor = e.Delta > 0 ? 0.8 : 1.25;

            // Zoom centered on mouse position
            double newXMin = dataX - (dataX - xMin) * factor;
            double newXMax = dataX + (xMax - dataX) * factor;
            double newYMin = dataY - (dataY - yMin) * factor;
            double newYMax = dataY + (yMax - dataY) * factor;

            // Clamp to full data range (don't zoom out beyond data)
            if (newXMax - newXMin > _dataXMax - _dataXMin)
            {
                newXMin = _dataXMin;
                newXMax = _dataXMax;
                newYMin = _dataYMin;
                newYMax = _dataYMax;
                _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
            }
            else
            {
                _zoomXMin = newXMin;
                _zoomXMax = newXMax;
                _zoomYMin = newYMin;
                _zoomYMax = newYMax;
            }

            SkiaCanvas.InvalidateVisual();

            // Notify for sync
            ZoomChanged?.Invoke(
                _zoomXMin ?? _dataXMin, _zoomXMax ?? _dataXMax,
                _zoomYMin ?? _dataYMin, _zoomYMax ?? _dataYMax);

            e.Handled = true;
        }

        private void OnMouseRightButtonDown(object? sender, MouseButtonEventArgs e)
        {
            // Right-click to reset zoom
            _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
            SkiaCanvas.InvalidateVisual();
            ZoomChanged?.Invoke(_dataXMin, _dataXMax, _dataYMin, _dataYMax);
        }

        #endregion

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_bgColor);

            if (_graphResult == null) return;

            if (_graphResult.GraphType == CprGraphType.Skew && _graphResult.Subplots != null)
            {
                DrawSubplots(canvas, info);
                return;
            }

            if (_graphResult.GraphType == CprGraphType.Histogram && _graphResult.HistogramData != null)
            {
                DrawHistogram(canvas, info);
                return;
            }

            DrawLinePlot(canvas, info);
        }
    }
}
