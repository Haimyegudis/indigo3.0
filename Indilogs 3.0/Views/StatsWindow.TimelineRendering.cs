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
        // ==========================================
        //  TIMELINE CHART (SkiaSharp)
        // ==========================================
        private void TimelineChartCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(_chartBg);

            if (_vm.TimelineBuckets == null || _vm.TimelineBucketCount == 0) return;

            int zStart = Math.Max(0, _vm.TimelineZoomStart);
            int zEnd = Math.Min(_vm.TimelineBucketCount, _vm.TimelineZoomEnd);
            int visibleCount = zEnd - zStart;
            if (visibleCount <= 0) visibleCount = _vm.TimelineBucketCount;

            float w = info.Width, h = info.Height;
            float leftM = 55, rightM = 15, topM = 15, bottomM = 35;
            float chartW = w - leftM - rightM;
            float chartH = h - topM - bottomM;
            int maxVal = 1;
            for (int i = zStart; i < zEnd; i++)
                if (_vm.TimelineBuckets[i] > maxVal) maxVal = _vm.TimelineBuckets[i];

            _hoveredTimelineBucket = -1;

            // State background coloring
            var stateEntries = _vm.TimelineStateEntries;
            if (stateEntries != null && stateEntries.Count > 0)
            {
                double zoomStartSec = zStart * _vm.TimelineBucketSize;
                double zoomEndSec = zEnd * _vm.TimelineBucketSize;
                var zoomStartTime = _vm.TimelineFirstTime.AddSeconds(zoomStartSec);
                var zoomEndTime = _vm.TimelineFirstTime.AddSeconds(zoomEndSec);
                double zoomTotalSec = zoomEndSec - zoomStartSec;
                if (zoomTotalSec <= 0) zoomTotalSec = 1;

                foreach (var state in stateEntries)
                {
                    if (state.EndTime == null) continue;
                    if (state.EndTime.Value < zoomStartTime || state.StartTime > zoomEndTime) continue;

                    double startSec = Math.Max(0, (state.StartTime - _vm.TimelineFirstTime).TotalSeconds - zoomStartSec);
                    double endSec = Math.Min(zoomTotalSec, (state.EndTime.Value - _vm.TimelineFirstTime).TotalSeconds - zoomStartSec);
                    float x1 = leftM + (float)(startSec / zoomTotalSec) * chartW;
                    float x2 = leftM + (float)(endSec / zoomTotalSec) * chartW;

                    int stateId = ChartStateConfig.GetId(state.StateName);
                    SKColor stateColor = ChartStateConfig.GetSolidColor(stateId).WithAlpha(35);

                    _cachedFillPaint.Color = stateColor;
                    canvas.DrawRect(x1, topM, x2 - x1, chartH, _cachedFillPaint);

                    float bandWidth = x2 - x1;
                    if (bandWidth > 40)
                    {
                        _cachedTextPaint9.Color = SKColors.Black;
                        float stateTextWidth = _cachedTextFont9.MeasureText(state.StateName);
                        float labelX = x1 + bandWidth / 2 - stateTextWidth / 2;
                        canvas.DrawText(state.StateName, labelX, topM + 11, _cachedTextFont9, _cachedTextPaint9);
                    }
                }
            }

            // Grid lines
            _cachedGridPaint.Color = _chartGrid;
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    canvas.DrawLine(leftM, y, w - rightM, y, _cachedGridPaint);
                }
            }

            // Area fill + line
            float stepW = chartW / visibleCount;
            var linePath = new SKPath();
            var areaPath = new SKPath();
            var accentColor = _vm.IsDarkMode ? s_accentDark : s_accentLight;

            areaPath.MoveTo(leftM, topM + chartH);
            for (int vi = 0; vi < visibleCount; vi++)
            {
                int i = zStart + vi;
                float x = leftM + vi * stepW + stepW / 2;
                float valH = (float)_vm.TimelineBuckets[i] / maxVal * chartH;
                float y = topM + chartH - valH;

                if (vi == 0) linePath.MoveTo(x, y); else linePath.LineTo(x, y);
                areaPath.LineTo(x, y);

                if (_timelineMouse.X >= leftM + vi * stepW && _timelineMouse.X < leftM + (vi + 1) * stepW &&
                    _timelineMouse.Y >= topM && _timelineMouse.Y <= topM + chartH)
                {
                    _hoveredTimelineBucket = i;
                }
            }
            areaPath.LineTo(leftM + (visibleCount - 1) * stepW + stepW / 2, topM + chartH);
            areaPath.Close();

            {
                var shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, topM), new SKPoint(0, topM + chartH),
                    new[] { accentColor.WithAlpha(120), accentColor.WithAlpha(15) },
                    null, SKShaderTileMode.Clamp);
                _cachedFillPaint.Shader = shader;
                canvas.DrawPath(areaPath, _cachedFillPaint);
                shader.Dispose();
                _cachedFillPaint.Shader = null;
            }

            _cachedStrokePaint.Color = accentColor;
            _cachedStrokePaint.StrokeWidth = 2.5f;
            canvas.DrawPath(linePath, _cachedStrokePaint);

            if (visibleCount <= 60)
            {
                _cachedFillPaint.Color = accentColor;
                for (int vi = 0; vi < visibleCount; vi++)
                {
                    int i = zStart + vi;
                    if (_vm.TimelineBuckets[i] > 0)
                    {
                        float x = leftM + vi * stepW + stepW / 2;
                        float valH = (float)_vm.TimelineBuckets[i] / maxVal * chartH;
                        float y = topM + chartH - valH;
                        canvas.DrawCircle(x, y, i == _hoveredTimelineBucket ? 5 : 3, _cachedFillPaint);
                    }
                }
            }

            // Hover vertical line
            if (_hoveredTimelineBucket >= zStart && _hoveredTimelineBucket < zEnd)
            {
                int hvi = _hoveredTimelineBucket - zStart;
                float hx = leftM + hvi * stepW + stepW / 2;
                var hoverLineColor = _vm.IsDarkMode ? SKColors.White.WithAlpha(80) : SKColors.Black.WithAlpha(60);
                _cachedStrokePaint.Color = hoverLineColor;
                _cachedStrokePaint.StrokeWidth = 1;
                var dashEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
                _cachedStrokePaint.PathEffect = dashEffect;
                canvas.DrawLine(hx, topM, hx, topM + chartH, _cachedStrokePaint);
                _cachedStrokePaint.PathEffect = null;
                dashEffect.Dispose();

                float hx1 = leftM + hvi * stepW;
                var highlightColor = _vm.IsDarkMode ? SKColors.White.WithAlpha(20) : SKColors.Black.WithAlpha(15);
                _cachedFillPaint.Color = highlightColor;
                canvas.DrawRect(hx1, topM, stepW, chartH, _cachedFillPaint);
            }

            // Y-axis labels
            _cachedTextPaint10.Color = _chartTextDim;
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    int val = (int)(maxVal * (1.0 - (double)i / gridLines));
                    string valStr = val.ToString();
                    float valWidth = _cachedTextFont10.MeasureText(valStr);
                    canvas.DrawText(valStr, leftM - 6 - valWidth, y + 4, _cachedTextFont10, _cachedTextPaint10);
                }
            }

            // X-axis labels
            {
                int labelCount = Math.Min(8, visibleCount);
                int labelStep = Math.Max(1, visibleCount / labelCount);
                for (int vi = 0; vi < visibleCount; vi += labelStep)
                {
                    int i = zStart + vi;
                    float x = leftM + vi * stepW + stepW / 2;
                    var time = _vm.TimelineFirstTime.AddSeconds(i * _vm.TimelineBucketSize);
                    string timeStr = time.ToString("HH:mm:ss");
                    float timeWidth = _cachedTextFont10.MeasureText(timeStr);
                    canvas.DrawText(timeStr, x - timeWidth / 2, topM + chartH + 18, _cachedTextFont10, _cachedTextPaint10);
                }
            }

            // Zoom hint
            if (visibleCount < _vm.TimelineBucketCount)
            {
                string zoomText = $"Zoom: {visibleCount}/{_vm.TimelineBucketCount} buckets  (Scroll to zoom, Shift+Scroll to pan)";
                _cachedTextPaint9.Color = _chartTextDim.WithAlpha(150);
                float zoomWidth = _cachedTextFont9.MeasureText(zoomText);
                canvas.DrawText(zoomText, w - rightM - zoomWidth, topM + chartH + 30, _cachedTextFont9, _cachedTextPaint9);
            }

            // Hover tooltip
            if (_hoveredTimelineBucket >= 0 && _hoveredTimelineBucket < _vm.TimelineBucketCount
                && _vm.TimelineBucketLogs != null)
            {
                var bucketStart = _vm.TimelineFirstTime.AddSeconds(_hoveredTimelineBucket * _vm.TimelineBucketSize);
                var bucketEnd = bucketStart.AddSeconds(_vm.TimelineBucketSize);
                int count = _vm.TimelineBuckets![_hoveredTimelineBucket];
                var logs = _vm.TimelineBucketLogs[_hoveredTimelineBucket];

                var sb = new StringBuilder();
                sb.AppendLine($"{bucketStart:HH:mm:ss} - {bucketEnd:HH:mm:ss}");

                if (stateEntries != null)
                {
                    var midTime = bucketStart.AddSeconds(_vm.TimelineBucketSize / 2);
                    foreach (var st in stateEntries)
                    {
                        if (midTime >= st.StartTime && st.EndTime.HasValue && midTime <= st.EndTime.Value)
                        {
                            sb.AppendLine($"State: {st.StateName}");
                            break;
                        }
                    }
                }

                sb.AppendLine($"{count} error(s)");
                var topMsgs = logs.Take(3).Select(l => StatsViewModel.TruncateMessage(l.Message, 50));
                foreach (var msg in topMsgs) sb.AppendLine($"  {msg}");
                if (count > 3) sb.AppendLine($"  +{count - 3} more...");
                sb.Append("Click to navigate");

                DrawTooltip(canvas, sb.ToString().TrimEnd(), _timelineMouse.X + 15, _timelineMouse.Y - 10, w, h);
            }
        }

        private void TimelineChartCanvas_MouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(TimelineChartCanvas);
            float dpi = (float)VisualTreeHelper.GetDpi(TimelineChartCanvas).DpiScaleX;
            _timelineMouse = new SKPoint((float)pos.X * dpi, (float)pos.Y * dpi);
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseLeave(object? sender, MouseEventArgs e)
        {
            _timelineMouse = new SKPoint(-1, -1);
            _hoveredTimelineBucket = -1;
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseWheel(object? sender, MouseWheelEventArgs e)
        {
            bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            _vm.ZoomTimeline(e.Delta, isShift, _hoveredTimelineBucket);
            e.Handled = true;
            TimelineChartCanvas.InvalidateVisual();
        }

        private void TimelineChartCanvas_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_hoveredTimelineBucket >= 0)
                _vm.NavigateTimelineBucket(_hoveredTimelineBucket);
        }

        // ==========================================
        //  SHARED TOOLTIP RENDERER
        // ==========================================
        private void DrawTooltip(SKCanvas canvas, string text, float x, float y, float canvasW, float canvasH)
        {
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Split('\n');
            float padding = 8, lineH = 16;
            float boxH = lines.Length * lineH + padding * 2;

            float maxW = 0;
            foreach (var line in lines)
            {
                float lw = _cachedTextFont11.MeasureText(line);
                if (lw > maxW) maxW = lw;
            }
            float boxW = maxW + padding * 2;

            if (x + boxW > canvasW - 5) x = canvasW - boxW - 5;
            if (y + boxH > canvasH - 5) y = canvasH - boxH - 5;
            if (x < 5) x = 5;
            if (y < 5) y = 5;

            var rect = new SKRect(x, y, x + boxW, y + boxH);

            canvas.DrawRoundRect(rect.Left + 2, rect.Top + 2, rect.Width, rect.Height, 6, 6, _cachedShadowBlurPaint);

            _cachedTooltipBgPaint.Color = _tooltipBg;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBgPaint);
            _cachedTooltipBorderPaint.Color = _tooltipBorder;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBorderPaint);

            _cachedTextPaint11.Color = _chartText;
            for (int i = 0; i < lines.Length; i++)
                canvas.DrawText(lines[i], x + padding, y + padding + (i + 1) * lineH - 3, _cachedTextFont11, _cachedTextPaint11);
        }
    }
}
