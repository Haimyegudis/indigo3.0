using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.ViewModels;
using System;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace IndiLogs_3._0.Views
{
    public partial class StatsWindow
    {
        // Cached paints/paths for render loop (avoid per-frame allocations)
        private readonly SKPaint _cachedShadowBlurPaint = new SKPaint { Color = SKColors.Black.WithAlpha(100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4), IsAntialias = true };
        private readonly SKPath _cachedPiePath = new SKPath();

        // ==========================================
        //  BAR CHART (SkiaSharp)
        // ==========================================
        private void BarChartCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            var data = _vm.BarChartData;
            if (data == null || data.Count == 0) return;

            float w = info.Width, h = info.Height;
            float leftMargin = 160, rightMargin = 50, topMargin = 10, bottomMargin = 10;
            float chartW = w - leftMargin - rightMargin;
            float chartH = h - topMargin - bottomMargin;
            int count = data.Count;
            float barHeight = Math.Min(28, (chartH - (count - 1) * 4) / count);
            float gap = 4;
            int maxCount = data.Max(x => x.Count);

            _barHitRegions.Clear();
            _hoveredBarIndex = -1;

            _cachedTextPaint11.Color = _chartTextDim;
            _cachedTextPaint11Bold.Color = _chartText;

            for (int i = 0; i < count; i++)
            {
                var item = data[i];
                float y = topMargin + i * (barHeight + gap);
                float barW = maxCount > 0 ? (float)item.Count / maxCount * chartW : 0;
                var barRect = new SKRect(leftMargin, y, leftMargin + barW, y + barHeight);

                _barHitRegions.Add(new SKRect(0, y, w, y + barHeight));

                bool isHovered = _barChartMouse.Y >= y && _barChartMouse.Y <= y + barHeight && _barChartMouse.X >= 0;
                if (isHovered) _hoveredBarIndex = i;

                var barColor = isHovered ? s_barHover : s_barNormal;
                var shader = SKShader.CreateLinearGradient(
                    new SKPoint(barRect.Left, barRect.Top), new SKPoint(barRect.Right, barRect.Top),
                    new[] { barColor, barColor.WithAlpha(180) }, null, SKShaderTileMode.Clamp);
                _cachedFillPaint.Shader = shader;
                canvas.DrawRoundRect(barRect, 4, 4, _cachedFillPaint);
                shader.Dispose();
                _cachedFillPaint.Shader = null;

                if (isHovered)
                {
                    _cachedStrokePaint.Color = SKColors.White.WithAlpha(120);
                    _cachedStrokePaint.StrokeWidth = 1.5f;
                    canvas.DrawRoundRect(barRect, 4, 4, _cachedStrokePaint);
                }

                string label = item.Name.Length > 22 ? item.Name.Substring(0, 19) + "..." : item.Name;
                canvas.DrawText(label, 5, y + barHeight / 2 + 4, _cachedTextFont11, _cachedTextPaint11);

                string valueText = item.Count.ToString("N0");
                canvas.DrawText(valueText, leftMargin + barW + 6, y + barHeight / 2 + 4, _cachedTextFont11Bold, _cachedTextPaint11Bold);
            }

            if (_hoveredBarIndex >= 0 && _hoveredBarIndex < data.Count)
            {
                var item = data[_hoveredBarIndex];
                string tip = $"{item.Name}\n{item.Count:N0} errors — Click to navigate";
                DrawTooltip(canvas, tip, _barChartMouse.X + 15, _barChartMouse.Y - 10, w, h);
            }
        }

        private void BarChartCanvas_MouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(BarChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(BarChartCanvas).DpiScaleX;
            _barChartMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            BarChartCanvas.InvalidateVisual();
        }

        private void BarChartCanvas_MouseLeave(object? sender, MouseEventArgs e)
        {
            _barChartMouse = new SKPoint(-1, -1);
            _hoveredBarIndex = -1;
            BarChartCanvas.InvalidateVisual();
        }

        private void BarChartCanvas_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_hoveredBarIndex >= 0)
                _vm.NavigateBarChartItem(_hoveredBarIndex);
        }

        // ==========================================
        //  PIE CHART (SkiaSharp)
        // ==========================================
        private void PieChartCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            var data = _vm.PieChartData;
            if (data == null || data.Count == 0) return;

            float w = info.Width, h = info.Height;
            float legendWidth = w * 0.38f;
            float chartAreaW = w - legendWidth;
            float radius = Math.Min(chartAreaW, h) * 0.38f;
            float cx = chartAreaW / 2f;
            float cy = h / 2f;
            _pieChartCenterX = cx; _pieChartCenterY = cy; _pieChartRadius = radius;

            int total = data.Sum(x => x.Count);
            _pieHitAngles.Clear();
            _hoveredPieIndex = -1;

            float startAngle = -90;
            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                float sweep = (float)item.Count / total * 360f;

                bool isHovered = false;
                if (_pieChartMouse.X >= 0)
                {
                    float dx = _pieChartMouse.X - cx, dy = _pieChartMouse.Y - cy;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius)
                    {
                        float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
                        if (angle < -90) angle += 360;
                        float normalAngle = angle + 90;
                        if (normalAngle < 0) normalAngle += 360;
                        if (normalAngle >= 360) normalAngle -= 360;
                        float checkStart = startAngle + 90;
                        if (checkStart < 0) checkStart += 360;
                        float checkEnd = checkStart + sweep;
                        if (normalAngle >= checkStart && normalAngle < checkEnd)
                        {
                            isHovered = true;
                            _hoveredPieIndex = i;
                        }
                    }
                }

                _pieHitAngles.Add((startAngle, sweep));

                float explode = isHovered ? 6 : 0;
                float midAngle = startAngle + sweep / 2f;
                float exX = (float)(explode * Math.Cos(midAngle * Math.PI / 180));
                float exY = (float)(explode * Math.Sin(midAngle * Math.PI / 180));

                var color = ChartColors[i % ChartColors.Length];
                _cachedFillPaint.Color = isHovered ? color.WithAlpha(255) : color.WithAlpha(220);
                _cachedStrokePaint.Color = _chartBg;
                _cachedStrokePaint.StrokeWidth = 2;
                {
                    _cachedPiePath.Reset();
                    _cachedPiePath.MoveTo(cx + exX, cy + exY);
                    _cachedPiePath.ArcTo(new SKRect(cx - radius + exX, cy - radius + exY, cx + radius + exX, cy + radius + exY), startAngle, sweep, false);
                    _cachedPiePath.Close();
                    canvas.DrawPath(_cachedPiePath, _cachedFillPaint);
                    canvas.DrawPath(_cachedPiePath, _cachedStrokePaint);
                }

                if (sweep > 18)
                {
                    float labelR = radius * 0.65f;
                    float lx = cx + exX + (float)(labelR * Math.Cos(midAngle * Math.PI / 180));
                    float ly = cy + exY + (float)(labelR * Math.Sin(midAngle * Math.PI / 180));
                    _cachedTextPaint11Bold.Color = SKColors.White;
                    string pctText = $"{(float)item.Count / total * 100:F0}%";
                    float pctWidth = _cachedTextFont11Bold.MeasureText(pctText);
                    canvas.DrawText(pctText, lx - pctWidth / 2, ly + 4, _cachedTextFont11Bold, _cachedTextPaint11Bold);
                }

                startAngle += sweep;
            }

            // Legend
            float legendX = chartAreaW + 10;
            float legendY = 15;
            _cachedTextPaint10.Color = _chartTextDim.WithAlpha(180);

            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                var color = ChartColors[i % ChartColors.Length];
                bool isHov = i == _hoveredPieIndex;

                _cachedFillPaint.Color = isHov ? color : color.WithAlpha(200);
                canvas.DrawCircle(legendX + 6, legendY + 6, 6, _cachedFillPaint);

                string name = item.State.Length > 16 ? item.State.Substring(0, 13) + "..." : item.State;
                _cachedTextPaint11.Color = isHov ? _chartText : _chartTextDim;
                canvas.DrawText(name, legendX + 18, legendY + 11, _cachedTextFont11, _cachedTextPaint11);
                canvas.DrawText($"({item.Count})", legendX + 18, legendY + 24, _cachedTextFont10, _cachedTextPaint10);
                legendY += 30;
            }

            if (_hoveredPieIndex >= 0 && _hoveredPieIndex < data.Count)
            {
                var item = data[_hoveredPieIndex];
                float pct = (float)item.Count / total * 100;
                string tip = $"{item.State}\n{item.Count:N0} errors ({pct:F1}%)\nClick to navigate";
                DrawTooltip(canvas, tip, _pieChartMouse.X + 15, _pieChartMouse.Y - 10, w, h);
            }
        }

        private void PieChartCanvas_MouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(PieChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(PieChartCanvas).DpiScaleX;
            _pieChartMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            PieChartCanvas.InvalidateVisual();
        }

        private void PieChartCanvas_MouseLeave(object? sender, MouseEventArgs e)
        {
            _pieChartMouse = new SKPoint(-1, -1);
            _hoveredPieIndex = -1;
            PieChartCanvas.InvalidateVisual();
        }

        private void PieChartCanvas_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_hoveredPieIndex >= 0)
                _vm.NavigatePieChartItem(_hoveredPieIndex);
        }

    }
}
