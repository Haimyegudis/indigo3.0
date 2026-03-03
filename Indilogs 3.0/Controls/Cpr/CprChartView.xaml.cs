#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
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

        private CprGraphResult _graphResult;
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
        private readonly SKPaint _subplotBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
        private readonly SKPaint _subplotDotPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _subplotLinePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _subplotAxisTextPaint = new SKPaint { IsAntialias = true };

        // Cached paints for histogram and curve rendering
        private readonly SKPaint _histBarPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        private readonly SKPaint _histCurvePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Red, StrokeWidth = 2 };

        // Cached paints for grid, axis, labels, tooltips
        private readonly SKPaint _gridPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _axisPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _axisTextPaint = new SKPaint { TextSize = 10, IsAntialias = true };
        private readonly SKPaint _axisLabelPaint = new SKPaint { TextSize = 10, IsAntialias = true };
        private readonly SKPaint _zeroLinePaint = new SKPaint { IsAntialias = false, StrokeWidth = 1.5f };
        private readonly SKPaint _dftMarkerPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _dftLabelPaint = new SKPaint { TextSize = 9, IsAntialias = true };

        // Cached paints for hover tooltip and crosshair
        private readonly SKPaint _crosshairPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 };
        private readonly SKPaint _tooltipBgPaint = new SKPaint { Style = SKPaintStyle.Fill };
        private readonly SKPaint _tooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _tooltipTextPaint = new SKPaint { TextSize = 10, IsAntialias = true };
        private readonly SKPaint _tooltipColorPaint = new SKPaint { IsAntialias = false, StrokeWidth = 2 };
        private readonly SKPaint _noDataPaint = new SKPaint { TextSize = 16, IsAntialias = true };

        // Zoom event for sync
        public event Action<double, double, double, double> ZoomChanged;

        public CprChartView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                var source = PresentationSource.FromVisual(this);
                if (source != null)
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

        public void SetGraphResult(CprGraphResult result)
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

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(SkiaCanvas);
            _hoverPos = new Point(pos.X * _dpiScaleX, pos.Y * _dpiScaleY);
            _showHover = true;
            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _showHover = false;
            SkiaCanvas.InvalidateVisual();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
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

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click to reset zoom
            _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
            SkiaCanvas.InvalidateVisual();
            ZoomChanged?.Invoke(_dataXMin, _dataXMax, _dataYMin, _dataYMax);
        }

        #endregion

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
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

        #region Line Plot

        private void DrawLinePlot(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            var series = _graphResult.Series;
            if (series == null || series.Count == 0)
            {
                DrawNoData(canvas, w, h);
                return;
            }

            // Calculate full data ranges
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var s in series)
            {
                if (s.XValues == null || s.YValues == null) continue;
                for (int i = 0; i < s.XValues.Length; i++)
                {
                    if (double.IsNaN(s.XValues[i]) || double.IsNaN(s.YValues[i])) continue;
                    if (s.XValues[i] < xMin) xMin = s.XValues[i];
                    if (s.XValues[i] > xMax) xMax = s.XValues[i];
                    if (s.YValues[i] < yMin) yMin = s.YValues[i];
                    if (s.YValues[i] > yMax) yMax = s.YValues[i];
                }
            }

            if (xMin >= xMax || yMin >= yMax)
            {
                DrawNoData(canvas, w, h);
                return;
            }

            // Apply manual Y-axis if specified
            if (!_graphResult.AutoYAxis)
            {
                yMin = _graphResult.YAxisFrom;
                yMax = _graphResult.YAxisTo;
            }
            else
            {
                // Add 5% padding
                double yPad = (yMax - yMin) * 0.05;
                yMin -= yPad;
                yMax += yPad;
            }

            // Store full data ranges for zoom
            _dataXMin = xMin;
            _dataXMax = xMax;
            _dataYMin = yMin;
            _dataYMax = yMax;

            // Apply zoom if active
            if (_zoomXMin.HasValue)
            {
                xMin = _zoomXMin.Value;
                xMax = _zoomXMax.Value;
                yMin = _zoomYMin.Value;
                yMax = _zoomYMax.Value;
            }

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            // Draw title
            DrawTitle(canvas, w, _graphResult.Title);

            // Draw grid
            DrawGrid(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xMax, yMin, yMax);

            // Draw axis labels
            DrawAxisLabels(canvas, chartLeft, chartTop, chartRight, chartBottom, w, h, _graphResult.XLabel, _graphResult.YLabel);

            // Draw DFT markers if present
            if (_graphResult.DftMarkers != null)
            {
                DrawDftMarkers(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xRange);
            }

            // Draw vertical reference lines if present (Colors graph)
            if (_graphResult.VerticalRefLines != null)
            {
                DrawVerticalRefLines(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xRange, chartW);
            }

            // Draw series with clipping
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            _subplotLinePaint.IsAntialias = true;
            using (var path = new SKPath())
            {
                foreach (var s in series)
                {
                    if (s.XValues == null || s.YValues == null || s.XValues.Length == 0) continue;

                    _subplotLinePaint.Color = s.Color;
                    _subplotLinePaint.StrokeWidth = s.StrokeWidth;
                    _subplotLinePaint.PathEffect = s.IsDashed ? SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) : null;

                    path.Reset();
                    bool first = true;

                    for (int i = 0; i < s.XValues.Length; i++)
                    {
                        if (double.IsNaN(s.YValues[i]))
                        {
                            first = true;
                            continue;
                        }

                        float x = chartLeft + (float)((s.XValues[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)((s.YValues[i] - yMin) / yRange * chartH);

                        if (first)
                        {
                            path.MoveTo(x, y);
                            first = false;
                        }
                        else
                        {
                            path.LineTo(x, y);
                        }
                    }

                    if (!first) canvas.DrawPath(path, _subplotLinePaint);
                    _subplotLinePaint.PathEffect = null;
                }
            }

            canvas.Restore();

            // Draw legend
            DrawLegend(canvas, chartRight, chartTop, series);

            // Draw zoom indicator
            if (_zoomXMin.HasValue)
            {
                _dftLabelPaint.Color = _textColor.WithAlpha(100);
                _dftLabelPaint.TextSize = 9;
                canvas.DrawText("Right-click to reset zoom", chartLeft + 4, h - 3, _dftLabelPaint);
            }

            // Draw hover tooltip
            if (_showHover)
            {
                DrawHoverTooltip(canvas, chartLeft, chartTop, chartRight, chartBottom,
                    xMin, xRange, yMin, yRange, series);
            }
        }

        #endregion

        #region Histogram

        private void DrawHistogram(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float chartLeft = LEFT_MARGIN;
            float chartRight = w - RIGHT_MARGIN;
            float chartTop = TOP_MARGIN;
            float chartBottom = h - BOTTOM_MARGIN;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            var hist = _graphResult.HistogramData;
            if (hist.BinEdges == null || hist.BinCounts == null) return;

            double xMin = hist.BinEdges.First();
            double xMax = hist.BinEdges.Last();
            double yMin = 0;
            double yMax = Math.Max(hist.BinCounts.Max(), hist.NormalY != null && hist.NormalY.Length > 0 ? hist.NormalY.Max() : 0);
            yMax *= 1.1;

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            DrawTitle(canvas, w, _graphResult.Title);
            DrawGrid(canvas, chartLeft, chartTop, chartRight, chartBottom, xMin, xMax, yMin, yMax);
            DrawAxisLabels(canvas, chartLeft, chartTop, chartRight, chartBottom, w, h, _graphResult.XLabel, _graphResult.YLabel);

            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            // Draw bars
            _histBarPaint.Color = s_histBarColor;
            for (int i = 0; i < hist.BinCounts.Length; i++)
            {
                float x1 = chartLeft + (float)((hist.BinEdges[i] - xMin) / xRange * chartW);
                float x2 = chartLeft + (float)((hist.BinEdges[i + 1] - xMin) / xRange * chartW);
                float yTop = chartBottom - (float)(hist.BinCounts[i] / yRange * chartH);

                canvas.DrawRect(new SKRect(x1, yTop, x2, chartBottom), _histBarPaint);
            }

            // Draw normal curve
            if (hist.NormalX != null && hist.NormalY != null)
            {
                using (var path = new SKPath())
                {
                    bool first = true;
                    for (int i = 0; i < hist.NormalX.Length; i++)
                    {
                        float x = chartLeft + (float)((hist.NormalX[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)(hist.NormalY[i] / yRange * chartH);
                        if (first) { path.MoveTo(x, y); first = false; }
                        else path.LineTo(x, y);
                    }
                    canvas.DrawPath(path, _histCurvePaint);
                }
            }

            canvas.Restore();
        }

        #endregion

        #region Subplots (Skew)

        private void DrawSubplots(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            int rows = _graphResult.SubplotRows;
            int cols = _graphResult.SubplotCols;

            float cellW = w / cols;
            float cellH = h / rows;

            // Calculate shared Y range if needed
            double sharedYMin = double.MaxValue, sharedYMax = double.MinValue;
            if (_graphResult.SharedYAxis)
            {
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var sp = _graphResult.Subplots[r, c];
                    if (sp == null) continue;
                    foreach (var scatter in sp.ScatterSeries)
                    {
                        if (scatter.YValues == null) continue;
                        foreach (var v in scatter.YValues)
                        {
                            if (double.IsNaN(v)) continue;
                            if (v < sharedYMin) sharedYMin = v;
                            if (v > sharedYMax) sharedYMax = v;
                        }
                    }
                }
                double pad = (sharedYMax - sharedYMin) * 0.1;
                sharedYMin -= pad;
                sharedYMax += pad;
            }

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var sp = _graphResult.Subplots[r, c];
                if (sp == null) continue;

                float ox = c * cellW;
                float oy = r * cellH;

                canvas.Save();
                canvas.Translate(ox, oy);

                DrawSingleSubplot(canvas, cellW, cellH, sp,
                    _graphResult.SharedYAxis ? sharedYMin : double.NaN,
                    _graphResult.SharedYAxis ? sharedYMax : double.NaN);

                canvas.Restore();
            }
        }

        private void DrawSingleSubplot(SKCanvas canvas, float w, float h, CprSubplot subplot,
            double forceYMin, double forceYMax)
        {
            float chartLeft = 45;
            float chartRight = w - 10;
            float chartTop = 22;
            float chartBottom = h - 20;
            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            if (chartW <= 0 || chartH <= 0) return;

            // Calculate ranges from scatter data
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var scatter in subplot.ScatterSeries)
            {
                if (scatter.XValues == null || scatter.YValues == null) continue;
                for (int i = 0; i < scatter.XValues.Length; i++)
                {
                    if (double.IsNaN(scatter.XValues[i]) || double.IsNaN(scatter.YValues[i])) continue;
                    if (scatter.XValues[i] < xMin) xMin = scatter.XValues[i];
                    if (scatter.XValues[i] > xMax) xMax = scatter.XValues[i];
                    if (scatter.YValues[i] < yMin) yMin = scatter.YValues[i];
                    if (scatter.YValues[i] > yMax) yMax = scatter.YValues[i];
                }
            }

            if (xMin >= xMax) return;

            if (!double.IsNaN(forceYMin))
            {
                yMin = forceYMin;
                yMax = forceYMax;
            }
            else
            {
                double yPad = (yMax - yMin) * 0.1;
                yMin -= yPad;
                yMax += yPad;
            }

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            if (yRange < 1e-10) yRange = 1;

            // Subplot border (reuse cached paint)
            _subplotBorderPaint.Color = _gridColor;
            canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), _subplotBorderPaint);

            // Subplot title (reuse cached paint)
            _subplotTitlePaint.Color = _textColor;
            _subplotTitlePaint.TextSize = 11;
            _subplotTitlePaint.Typeface = s_segoeNormal;
            float titleW = _subplotTitlePaint.MeasureText(subplot.Title);
            canvas.DrawText(subplot.Title, (chartLeft + chartRight - titleW) / 2, chartTop - 6, _subplotTitlePaint);

            // Y-axis labels (3 ticks)
            DrawSubplotYAxis(canvas, chartLeft, chartTop, chartBottom, yMin, yMax);

            // Draw scatter points
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            foreach (var scatter in subplot.ScatterSeries)
            {
                if (scatter.XValues == null || scatter.YValues == null) continue;
                _subplotDotPaint.Color = scatter.Color;

                for (int i = 0; i < scatter.XValues.Length; i++)
                {
                    if (double.IsNaN(scatter.YValues[i])) continue;
                    float x = chartLeft + (float)((scatter.XValues[i] - xMin) / xRange * chartW);
                    float y = chartBottom - (float)((scatter.YValues[i] - yMin) / yRange * chartH);
                    canvas.DrawCircle(x, y, 2.5f, _subplotDotPaint);
                }
            }

            // Draw line series (regression + polynomial)
            using (var path = new SKPath())
            {
                foreach (var line in subplot.LineSeries)
                {
                    if (line.XValues == null || line.YValues == null || line.XValues.Length == 0) continue;
                    _subplotLinePaint.Color = line.Color;
                    _subplotLinePaint.StrokeWidth = line.StrokeWidth;
                    _subplotLinePaint.PathEffect = line.IsDashed ? SKPathEffect.CreateDash(new float[] { 5, 3 }, 0) : null;

                    path.Reset();
                    bool first = true;
                    for (int i = 0; i < line.XValues.Length; i++)
                    {
                        if (double.IsNaN(line.YValues[i])) { first = true; continue; }
                        float x = chartLeft + (float)((line.XValues[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)((line.YValues[i] - yMin) / yRange * chartH);
                        if (first) { path.MoveTo(x, y); first = false; }
                        else path.LineTo(x, y);
                    }
                    if (!first) canvas.DrawPath(path, _subplotLinePaint);
                    _subplotLinePaint.PathEffect = null;
                }
            }

            canvas.Restore();
        }

        private void DrawSubplotYAxis(SKCanvas canvas, float chartLeft, float chartTop, float chartBottom, double yMin, double yMax)
        {
            _subplotAxisTextPaint.Color = _textColor;
            _subplotAxisTextPaint.TextSize = 9;
            _subplotAxisTextPaint.Typeface = s_segoeNormal;

            int nTicks = 3;
            for (int i = 0; i <= nTicks; i++)
            {
                double val = yMin + (yMax - yMin) * i / nTicks;
                float y = chartBottom - (chartBottom - chartTop) * i / nTicks;
                string label = FormatTickLabel(val);
                float tw = _subplotAxisTextPaint.MeasureText(label);
                canvas.DrawText(label, chartLeft - tw - 3, y + 3, _subplotAxisTextPaint);
            }
        }

        #endregion

        #region Drawing Helpers

        private void DrawNoData(SKCanvas canvas, float w, float h)
        {
            _noDataPaint.Color = _textColor.WithAlpha(128);
            _noDataPaint.TextSize = 14;
            _noDataPaint.Typeface = s_segoeNormal;

            string msg = "No data to display";
            float tw = _noDataPaint.MeasureText(msg);
            canvas.DrawText(msg, (w - tw) / 2, h / 2, _noDataPaint);
        }

        private const string Ellipsis = "...";

        private void DrawTitle(SKCanvas canvas, float w, string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            _subplotTitlePaint.Color = _textColor;
            _subplotTitlePaint.TextSize = 12;
            _subplotTitlePaint.Typeface = s_segoeBold;

            float tw = _subplotTitlePaint.MeasureText(title);
            // Truncate if too long
            if (tw > w - 20)
            {
                float ellipsisW = _subplotTitlePaint.MeasureText(Ellipsis);
                while (title.Length > 10 && _subplotTitlePaint.MeasureText(title) + ellipsisW > w - 20)
                    title = title.Substring(0, title.Length - 1);
                title += Ellipsis;
                tw = _subplotTitlePaint.MeasureText(title);
            }
            canvas.DrawText(title, (w - tw) / 2, 16, _subplotTitlePaint);
        }

        private void DrawGrid(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xMax, double yMin, double yMax)
        {
            _gridPaint.Color = _gridColor.WithAlpha(60);
            _axisPaint.Color = _gridColor;
            _axisTextPaint.Color = _textColor;
            _axisTextPaint.Typeface = s_segoeNormal;

            // Border
            canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), _axisPaint);

            // Y grid + labels
            int nYTicks = 5;
            for (int i = 0; i <= nYTicks; i++)
            {
                double val = yMin + (yMax - yMin) * i / nYTicks;
                float y = Snap(chartBottom - (chartBottom - chartTop) * i / nYTicks);
                canvas.DrawLine(chartLeft, y, chartRight, y, _gridPaint);

                string label = FormatTickLabel(val);
                float tw = _axisTextPaint.MeasureText(label);
                canvas.DrawText(label, chartLeft - tw - 4, y + 4, _axisTextPaint);
            }

            // X grid + labels
            int nXTicks = 6;
            for (int i = 0; i <= nXTicks; i++)
            {
                double val = xMin + (xMax - xMin) * i / nXTicks;
                float x = Snap(chartLeft + (chartRight - chartLeft) * i / nXTicks);
                canvas.DrawLine(x, chartTop, x, chartBottom, _gridPaint);

                string label = FormatTickLabel(val);
                float tw = _axisTextPaint.MeasureText(label);
                canvas.DrawText(label, x - tw / 2, chartBottom + 14, _axisTextPaint);
            }

            // Y=0 reference line (X-axis) — helps see convergence
            double yRange = yMax - yMin;
            if (yRange > 0 && yMin <= 0 && yMax >= 0)
            {
                float yZero = Snap(chartBottom - (float)((0 - yMin) / yRange * (chartBottom - chartTop)));
                _zeroLinePaint.Color = _textColor.WithAlpha(140);
                canvas.DrawLine(chartLeft, yZero, chartRight, yZero, _zeroLinePaint);
            }
        }

        private void DrawAxisLabels(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            float w, float h, string xLabel, string yLabel)
        {
            _axisLabelPaint.Color = _textColor.WithAlpha(180);
            _axisLabelPaint.Typeface = s_segoeNormal;

            if (!string.IsNullOrEmpty(xLabel))
            {
                float tw = _axisLabelPaint.MeasureText(xLabel);
                canvas.DrawText(xLabel, (chartLeft + chartRight - tw) / 2, h - 2, _axisLabelPaint);
            }

            if (!string.IsNullOrEmpty(yLabel))
            {
                canvas.Save();
                canvas.RotateDegrees(-90, 10, (chartTop + chartBottom) / 2);
                float tw = _axisLabelPaint.MeasureText(yLabel);
                canvas.DrawText(yLabel, 10 - tw / 2, (chartTop + chartBottom) / 2 + 4, _axisLabelPaint);
                canvas.Restore();
            }
        }

        private void DrawDftMarkers(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange)
        {
            float chartW = chartRight - chartLeft;

            _dftMarkerPaint.StrokeWidth = 1;
            _dftLabelPaint.Typeface = s_segoeNormal;

            foreach (var marker in _graphResult.DftMarkers)
            {
                float x = chartLeft + (float)((marker.Frequency - xMin) / xRange * chartW);
                if (x < chartLeft || x > chartRight) continue;

                _dftMarkerPaint.Color = marker.Color == SKColors.Black ? _textColor.WithAlpha(180) : marker.Color;
                _dftMarkerPaint.PathEffect = marker.IsDashed ? SKPathEffect.CreateDash(new float[] { 4, 3 }, 0) : null;

                canvas.DrawLine(x, chartTop, x, chartBottom, _dftMarkerPaint);

                _dftLabelPaint.Color = _dftMarkerPaint.Color;
                canvas.DrawText(marker.Label, x + 2, chartTop + 12, _dftLabelPaint);

                _dftMarkerPaint.PathEffect = null;
            }
        }

        private void DrawVerticalRefLines(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange, float chartW)
        {
            _dftMarkerPaint.StrokeWidth = 1.5f;
            _dftLabelPaint.Typeface = s_segoeNormal;

            float labelOffset = 0;
            foreach (var refLine in _graphResult.VerticalRefLines)
            {
                float x = chartLeft + (float)((refLine.XValue - xMin) / xRange * chartW);
                if (x < chartLeft || x > chartRight) continue;

                // Adapt black color to theme
                _dftMarkerPaint.Color = refLine.Color == SKColors.Black ? _textColor.WithAlpha(180) : refLine.Color;

                // Set line style
                switch (refLine.LineStyle)
                {
                    case RefLineStyle.Solid:
                        _dftMarkerPaint.PathEffect = null;
                        break;
                    case RefLineStyle.Dashed:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0);
                        break;
                    case RefLineStyle.DashDot:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3, 2, 3 }, 0);
                        break;
                    case RefLineStyle.Dotted:
                        _dftMarkerPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 2, 3 }, 0);
                        break;
                }

                canvas.DrawLine(x, chartTop, x, chartBottom, _dftMarkerPaint);

                _dftLabelPaint.Color = _dftMarkerPaint.Color;
                canvas.DrawText(refLine.Label, x + 2, chartTop + 12 + labelOffset, _dftLabelPaint);
                labelOffset += 10; // Stagger labels so they don't overlap

                _dftMarkerPaint.PathEffect = null;
            }
        }

        private void DrawLegend(SKCanvas canvas, float chartRight, float chartTop, List<CprSeriesData> series)
        {
            if (series == null || series.Count == 0) return;

            float legendX = chartRight + 4;
            float legendY = chartTop + 5;

            _axisTextPaint.Color = _textColor;
            _axisTextPaint.Typeface = s_segoeNormal;
            _subplotLinePaint.StrokeWidth = 2;
            _subplotLinePaint.IsAntialias = false;

            foreach (var s in series)
            {
                _subplotLinePaint.Color = s.Color;
                canvas.DrawLine(legendX, legendY + 5, legendX + 14, legendY + 5, _subplotLinePaint);
                canvas.DrawText(s.Name, legendX + 17, legendY + 9, _axisTextPaint);
                legendY += LEGEND_LINE_HEIGHT;
            }
            _subplotLinePaint.IsAntialias = true; // restore default
        }

        private void DrawHoverTooltip(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange, double yMin, double yRange, List<CprSeriesData> series)
        {
            float mx = (float)_hoverPos.X;
            float my = (float)_hoverPos.Y;

            if (mx < chartLeft || mx > chartRight || my < chartTop || my > chartBottom) return;

            float chartW = chartRight - chartLeft;
            float chartH = chartBottom - chartTop;

            double dataX = xMin + (mx - chartLeft) / chartW * xRange;
            double dataY = yMin + (chartBottom - my) / chartH * yRange;

            // --- Draw crosshair lines ---
            _crosshairPaint.Color = _textColor.WithAlpha(80);
            _crosshairPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
            canvas.DrawLine(mx, chartTop, mx, chartBottom, _crosshairPaint);   // vertical
            canvas.DrawLine(chartLeft, my, chartRight, my, _crosshairPaint);   // horizontal
            _crosshairPaint.PathEffect = null;

            // --- Draw Y value label on the left Y-axis ---
            _dftLabelPaint.Color = _textColor;
            _dftLabelPaint.TextSize = 9;
            _dftLabelPaint.Typeface = s_segoeNormal;
            _tooltipBgPaint.Color = _bgColor.WithAlpha(220);
            {
                string yLabel = FormatTickLabel(dataY);
                float tw = _dftLabelPaint.MeasureText(yLabel);
                var yRect = new SKRect(chartLeft - tw - 7, my - 7, chartLeft - 1, my + 7);
                canvas.DrawRect(yRect, _tooltipBgPaint);
                canvas.DrawText(yLabel, chartLeft - tw - 4, my + 3, _dftLabelPaint);

                // X value label on the bottom X-axis
                string xLabel = FormatTickLabel(dataX);
                float xw = _dftLabelPaint.MeasureText(xLabel);
                var xRect = new SKRect(mx - xw / 2 - 3, chartBottom + 1, mx + xw / 2 + 3, chartBottom + 15);
                canvas.DrawRect(xRect, _tooltipBgPaint);
                canvas.DrawText(xLabel, mx - xw / 2, chartBottom + 12, _dftLabelPaint);
            }

            // --- Build tooltip with per-series Y values at cursor X ---
            var tooltipLines = new List<(string text, SKColor color)>();

            foreach (var s in series)
            {
                if (s.XValues == null || s.YValues == null || s.XValues.Length == 0) continue;

                // Find nearest X index to cursor dataX using binary search
                int bestIdx = -1;
                double bestDist = double.MaxValue;
                int lo = 0, hi = s.XValues.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    double d = Math.Abs(s.XValues[mid] - dataX);
                    if (d < bestDist) { bestDist = d; bestIdx = mid; }
                    if (s.XValues[mid] < dataX) lo = mid + 1;
                    else hi = mid - 1;
                }

                if (bestIdx >= 0 && !double.IsNaN(s.YValues[bestIdx]))
                {
                    string yVal = FormatTickLabel(s.YValues[bestIdx]);
                    tooltipLines.Add(($"{s.Name}: {yVal}", s.Color));
                }
            }

            if (tooltipLines.Count == 0) return;

            // --- Draw tooltip box ---
            _tooltipBgPaint.Color = _bgColor.WithAlpha(230);
            _tooltipBorderPaint.Color = _gridColor;
            _tooltipTextPaint.Color = _textColor;
            _tooltipTextPaint.Typeface = s_segoeNormal;
            {
                // Header line
                string header = $"X: {FormatTickLabel(dataX)}";
                float lineHeight = 14;
                float maxW = _tooltipTextPaint.MeasureText(header);
                foreach (var line in tooltipLines)
                {
                    float lw = _tooltipTextPaint.MeasureText(line.text);
                    if (lw + 18 > maxW) maxW = lw + 18; // 18 = color swatch + gap
                }

                float boxW = maxW + 12;
                float boxH = lineHeight * (tooltipLines.Count + 1) + 10; // +1 for header

                float tipX = mx + 14;
                float tipY = my - boxH / 2;
                // Keep within chart bounds
                if (tipX + boxW > chartRight) tipX = mx - boxW - 14;
                if (tipY < chartTop) tipY = chartTop;
                if (tipY + boxH > chartBottom) tipY = chartBottom - boxH;

                var rect = new SKRect(tipX, tipY, tipX + boxW, tipY + boxH);
                canvas.DrawRoundRect(rect, 4, 4, _tooltipBgPaint);
                canvas.DrawRoundRect(rect, 4, 4, _tooltipBorderPaint);

                // Draw header
                float textX = tipX + 6;
                float textY = tipY + lineHeight;
                canvas.DrawText(header, textX, textY, _tooltipTextPaint);

                // Separator line
                textY += 3;
                _gridPaint.Color = _gridColor.WithAlpha(100);
                canvas.DrawLine(tipX + 4, textY, tipX + boxW - 4, textY, _gridPaint);

                // Draw series values
                foreach (var line in tooltipLines)
                {
                    textY += lineHeight;
                    // Color swatch
                    _tooltipColorPaint.Color = line.color;
                    canvas.DrawLine(textX, textY - 4, textX + 10, textY - 4, _tooltipColorPaint);
                    // Text
                    canvas.DrawText(line.text, textX + 14, textY, _tooltipTextPaint);
                }
            }
        }

        private static string FormatTickLabel(double val)
        {
            double abs = Math.Abs(val);
            if (abs >= 10000 || (abs > 0 && abs < 0.01))
                return val.ToString("E1");
            if (abs >= 100)
                return val.ToString("F0");
            if (abs >= 1)
                return val.ToString("F1");
            return val.ToString("F3");
        }

        private static float Snap(float coord) => (float)Math.Floor(coord) + 0.5f;

        #endregion
    }
}
