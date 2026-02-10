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

            using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke })
            using (var path = new SKPath())
            {
                foreach (var s in series)
                {
                    if (s.XValues == null || s.YValues == null || s.XValues.Length == 0) continue;

                    paint.Color = s.Color;
                    paint.StrokeWidth = s.StrokeWidth;
                    paint.PathEffect = s.IsDashed ? SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) : null;

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

                    if (!first) canvas.DrawPath(path, paint);
                    paint.PathEffect = null;
                }
            }

            canvas.Restore();

            // Draw legend
            DrawLegend(canvas, chartRight, chartTop, series);

            // Draw zoom indicator
            if (_zoomXMin.HasValue)
            {
                using (var paint = new SKPaint { Color = _textColor.WithAlpha(100), TextSize = 9, IsAntialias = true })
                {
                    canvas.DrawText("Right-click to reset zoom", chartLeft + 4, h - 3, paint);
                }
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
            using (var barPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#3B82F6").WithAlpha(180) })
            {
                for (int i = 0; i < hist.BinCounts.Length; i++)
                {
                    float x1 = chartLeft + (float)((hist.BinEdges[i] - xMin) / xRange * chartW);
                    float x2 = chartLeft + (float)((hist.BinEdges[i + 1] - xMin) / xRange * chartW);
                    float yTop = chartBottom - (float)(hist.BinCounts[i] / yRange * chartH);

                    canvas.DrawRect(new SKRect(x1, yTop, x2, chartBottom), barPaint);
                }
            }

            // Draw normal curve
            if (hist.NormalX != null && hist.NormalY != null)
            {
                using (var curvePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Red, StrokeWidth = 2 })
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
                    canvas.DrawPath(path, curvePaint);
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

            // Subplot border
            using (var borderPaint = new SKPaint { Color = _gridColor, IsAntialias = false, StrokeWidth = 1, Style = SKPaintStyle.Stroke })
            {
                canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), borderPaint);
            }

            // Subplot title
            using (var titlePaint = new SKPaint { Color = _textColor, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                float titleW = titlePaint.MeasureText(subplot.Title);
                canvas.DrawText(subplot.Title, (chartLeft + chartRight - titleW) / 2, chartTop - 6, titlePaint);
            }

            // Y-axis labels (3 ticks)
            DrawSubplotYAxis(canvas, chartLeft, chartTop, chartBottom, yMin, yMax);

            // Draw scatter points
            canvas.Save();
            canvas.ClipRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom));

            using (var dotPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                foreach (var scatter in subplot.ScatterSeries)
                {
                    if (scatter.XValues == null || scatter.YValues == null) continue;
                    dotPaint.Color = scatter.Color;

                    for (int i = 0; i < scatter.XValues.Length; i++)
                    {
                        if (double.IsNaN(scatter.YValues[i])) continue;
                        float x = chartLeft + (float)((scatter.XValues[i] - xMin) / xRange * chartW);
                        float y = chartBottom - (float)((scatter.YValues[i] - yMin) / yRange * chartH);
                        canvas.DrawCircle(x, y, 2.5f, dotPaint);
                    }
                }
            }

            // Draw line series (regression + polynomial)
            using (var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke })
            using (var path = new SKPath())
            {
                foreach (var line in subplot.LineSeries)
                {
                    if (line.XValues == null || line.YValues == null || line.XValues.Length == 0) continue;
                    linePaint.Color = line.Color;
                    linePaint.StrokeWidth = line.StrokeWidth;
                    linePaint.PathEffect = line.IsDashed ? SKPathEffect.CreateDash(new float[] { 5, 3 }, 0) : null;

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
                    if (!first) canvas.DrawPath(path, linePaint);
                    linePaint.PathEffect = null;
                }
            }

            canvas.Restore();
        }

        private void DrawSubplotYAxis(SKCanvas canvas, float chartLeft, float chartTop, float chartBottom, double yMin, double yMax)
        {
            using (var textPaint = new SKPaint { Color = _textColor, TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                int nTicks = 3;
                for (int i = 0; i <= nTicks; i++)
                {
                    double val = yMin + (yMax - yMin) * i / nTicks;
                    float y = chartBottom - (chartBottom - chartTop) * i / nTicks;
                    string label = FormatTickLabel(val);
                    float tw = textPaint.MeasureText(label);
                    canvas.DrawText(label, chartLeft - tw - 3, y + 3, textPaint);
                }
            }
        }

        #endregion

        #region Drawing Helpers

        private void DrawNoData(SKCanvas canvas, float w, float h)
        {
            using (var paint = new SKPaint { Color = _textColor.WithAlpha(128), TextSize = 14, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                string msg = "No data to display";
                float tw = paint.MeasureText(msg);
                canvas.DrawText(msg, (w - tw) / 2, h / 2, paint);
            }
        }

        private void DrawTitle(SKCanvas canvas, float w, string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            using (var paint = new SKPaint { Color = _textColor, TextSize = 12, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
            {
                float tw = paint.MeasureText(title);
                // Truncate if too long
                if (tw > w - 20)
                {
                    while (title.Length > 10 && paint.MeasureText(title + "...") > w - 20)
                        title = title.Substring(0, title.Length - 1);
                    title += "...";
                    tw = paint.MeasureText(title);
                }
                canvas.DrawText(title, (w - tw) / 2, 16, paint);
            }
        }

        private void DrawGrid(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xMax, double yMin, double yMax)
        {
            using (var gridPaint = new SKPaint { Color = _gridColor.WithAlpha(60), IsAntialias = false, StrokeWidth = 1 })
            using (var axisPaint = new SKPaint { Color = _gridColor, IsAntialias = false, StrokeWidth = 1 })
            using (var textPaint = new SKPaint { Color = _textColor, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                // Border
                canvas.DrawRect(new SKRect(chartLeft, chartTop, chartRight, chartBottom), axisPaint);

                // Y grid + labels
                int nYTicks = 5;
                for (int i = 0; i <= nYTicks; i++)
                {
                    double val = yMin + (yMax - yMin) * i / nYTicks;
                    float y = Snap(chartBottom - (chartBottom - chartTop) * i / nYTicks);
                    canvas.DrawLine(chartLeft, y, chartRight, y, gridPaint);

                    string label = FormatTickLabel(val);
                    float tw = textPaint.MeasureText(label);
                    canvas.DrawText(label, chartLeft - tw - 4, y + 4, textPaint);
                }

                // X grid + labels
                int nXTicks = 6;
                for (int i = 0; i <= nXTicks; i++)
                {
                    double val = xMin + (xMax - xMin) * i / nXTicks;
                    float x = Snap(chartLeft + (chartRight - chartLeft) * i / nXTicks);
                    canvas.DrawLine(x, chartTop, x, chartBottom, gridPaint);

                    string label = FormatTickLabel(val);
                    float tw = textPaint.MeasureText(label);
                    canvas.DrawText(label, x - tw / 2, chartBottom + 14, textPaint);
                }

                // Y=0 reference line (X-axis) — helps see convergence
                double yRange = yMax - yMin;
                if (yRange > 0 && yMin <= 0 && yMax >= 0)
                {
                    float yZero = Snap(chartBottom - (float)((0 - yMin) / yRange * (chartBottom - chartTop)));
                    using (var zeroPaint = new SKPaint { Color = _textColor.WithAlpha(140), IsAntialias = false, StrokeWidth = 1.5f })
                    {
                        canvas.DrawLine(chartLeft, yZero, chartRight, yZero, zeroPaint);
                    }
                }
            }
        }

        private void DrawAxisLabels(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            float w, float h, string xLabel, string yLabel)
        {
            using (var labelPaint = new SKPaint { Color = _textColor.WithAlpha(180), TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                if (!string.IsNullOrEmpty(xLabel))
                {
                    float tw = labelPaint.MeasureText(xLabel);
                    canvas.DrawText(xLabel, (chartLeft + chartRight - tw) / 2, h - 2, labelPaint);
                }

                if (!string.IsNullOrEmpty(yLabel))
                {
                    canvas.Save();
                    canvas.RotateDegrees(-90, 10, (chartTop + chartBottom) / 2);
                    float tw = labelPaint.MeasureText(yLabel);
                    canvas.DrawText(yLabel, 10 - tw / 2, (chartTop + chartBottom) / 2 + 4, labelPaint);
                    canvas.Restore();
                }
            }
        }

        private void DrawDftMarkers(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange)
        {
            float chartW = chartRight - chartLeft;

            using (var markerPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1 })
            using (var labelPaint = new SKPaint { TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                foreach (var marker in _graphResult.DftMarkers)
                {
                    float x = chartLeft + (float)((marker.Frequency - xMin) / xRange * chartW);
                    if (x < chartLeft || x > chartRight) continue;

                    markerPaint.Color = marker.Color == SKColors.Black ? _textColor.WithAlpha(180) : marker.Color;
                    markerPaint.PathEffect = marker.IsDashed ? SKPathEffect.CreateDash(new float[] { 4, 3 }, 0) : null;

                    canvas.DrawLine(x, chartTop, x, chartBottom, markerPaint);

                    labelPaint.Color = markerPaint.Color;
                    canvas.DrawText(marker.Label, x + 2, chartTop + 12, labelPaint);

                    markerPaint.PathEffect = null;
                }
            }
        }

        private void DrawVerticalRefLines(SKCanvas canvas, float chartLeft, float chartTop, float chartRight, float chartBottom,
            double xMin, double xRange, float chartW)
        {
            using (var linePaint = new SKPaint { IsAntialias = false, StrokeWidth = 1.5f })
            using (var labelPaint = new SKPaint { TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            {
                float labelOffset = 0;
                foreach (var refLine in _graphResult.VerticalRefLines)
                {
                    float x = chartLeft + (float)((refLine.XValue - xMin) / xRange * chartW);
                    if (x < chartLeft || x > chartRight) continue;

                    // Adapt black color to theme
                    linePaint.Color = refLine.Color == SKColors.Black ? _textColor.WithAlpha(180) : refLine.Color;

                    // Set line style
                    switch (refLine.LineStyle)
                    {
                        case RefLineStyle.Solid:
                            linePaint.PathEffect = null;
                            break;
                        case RefLineStyle.Dashed:
                            linePaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0);
                            break;
                        case RefLineStyle.DashDot:
                            linePaint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3, 2, 3 }, 0);
                            break;
                        case RefLineStyle.Dotted:
                            linePaint.PathEffect = SKPathEffect.CreateDash(new float[] { 2, 3 }, 0);
                            break;
                    }

                    canvas.DrawLine(x, chartTop, x, chartBottom, linePaint);

                    labelPaint.Color = linePaint.Color;
                    canvas.DrawText(refLine.Label, x + 2, chartTop + 12 + labelOffset, labelPaint);
                    labelOffset += 10; // Stagger labels so they don't overlap

                    linePaint.PathEffect = null;
                }
            }
        }

        private void DrawLegend(SKCanvas canvas, float chartRight, float chartTop, List<CprSeriesData> series)
        {
            if (series == null || series.Count == 0) return;

            float legendX = chartRight + 4;
            float legendY = chartTop + 5;

            using (var textPaint = new SKPaint { Color = _textColor, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            using (var linePaint = new SKPaint { IsAntialias = false, StrokeWidth = 2 })
            {
                foreach (var s in series)
                {
                    linePaint.Color = s.Color;
                    canvas.DrawLine(legendX, legendY + 5, legendX + 14, legendY + 5, linePaint);
                    canvas.DrawText(s.Name, legendX + 17, legendY + 9, textPaint);
                    legendY += LEGEND_LINE_HEIGHT;
                }
            }
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
            using (var crosshairPaint = new SKPaint
            {
                Color = _textColor.WithAlpha(80),
                IsAntialias = false,
                StrokeWidth = 1,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
            })
            {
                canvas.DrawLine(mx, chartTop, mx, chartBottom, crosshairPaint);   // vertical
                canvas.DrawLine(chartLeft, my, chartRight, my, crosshairPaint);   // horizontal
            }

            // --- Draw Y value label on the left Y-axis ---
            using (var axisPaint = new SKPaint { Color = _textColor, TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            using (var axisBg = new SKPaint { Color = _bgColor.WithAlpha(220), Style = SKPaintStyle.Fill })
            {
                string yLabel = FormatTickLabel(dataY);
                float tw = axisPaint.MeasureText(yLabel);
                var yRect = new SKRect(chartLeft - tw - 7, my - 7, chartLeft - 1, my + 7);
                canvas.DrawRect(yRect, axisBg);
                canvas.DrawText(yLabel, chartLeft - tw - 4, my + 3, axisPaint);

                // X value label on the bottom X-axis
                string xLabel = FormatTickLabel(dataX);
                float xw = axisPaint.MeasureText(xLabel);
                var xRect = new SKRect(mx - xw / 2 - 3, chartBottom + 1, mx + xw / 2 + 3, chartBottom + 15);
                canvas.DrawRect(xRect, axisBg);
                canvas.DrawText(xLabel, mx - xw / 2, chartBottom + 12, axisPaint);
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
            using (var bgPaint = new SKPaint { Color = _bgColor.WithAlpha(230), Style = SKPaintStyle.Fill })
            using (var borderPaint = new SKPaint { Color = _gridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            using (var textPaint = new SKPaint { Color = _textColor, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") })
            using (var colorPaint = new SKPaint { IsAntialias = false, StrokeWidth = 2 })
            {
                // Header line
                string header = $"X: {FormatTickLabel(dataX)}";
                float lineHeight = 14;
                float maxW = textPaint.MeasureText(header);
                foreach (var line in tooltipLines)
                {
                    float lw = textPaint.MeasureText(line.text);
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
                canvas.DrawRoundRect(rect, 4, 4, bgPaint);
                canvas.DrawRoundRect(rect, 4, 4, borderPaint);

                // Draw header
                float textX = tipX + 6;
                float textY = tipY + lineHeight;
                canvas.DrawText(header, textX, textY, textPaint);

                // Separator line
                textY += 3;
                using (var sepPaint = new SKPaint { Color = _gridColor.WithAlpha(100), StrokeWidth = 1 })
                    canvas.DrawLine(tipX + 4, textY, tipX + boxW - 4, textY, sepPaint);

                // Draw series values
                foreach (var line in tooltipLines)
                {
                    textY += lineHeight;
                    // Color swatch
                    colorPaint.Color = line.color;
                    canvas.DrawLine(textX, textY - 4, textX + 10, textY - 4, colorPaint);
                    // Text
                    canvas.DrawText(line.text, textX + 14, textY, textPaint);
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
