using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace IndiLogs_3._0.Views
{
    public partial class StatsWindow : Window
    {
        private readonly StatsViewModel _vm;

        // SkiaSharp hover state (purely UI — not business logic)
        private SKPoint _barChartMouse = new SKPoint(-1, -1);
        private SKPoint _pieChartMouse = new SKPoint(-1, -1);
        private SKPoint _timelineMouse = new SKPoint(-1, -1);
        private int _hoveredBarIndex = -1;
        private int _hoveredPieIndex = -1;
        private int _hoveredTimelineBucket = -1;

        // Hit regions for click detection
        private List<SKRect> _barHitRegions = new List<SKRect>();
        private List<(float startAngle, float sweepAngle)> _pieHitAngles = new List<(float, float)>();
        private float _pieChartCenterX, _pieChartCenterY, _pieChartRadius;

        // Theme colors for SkiaSharp rendering
        private SKColor _chartBg, _chartText, _chartTextDim, _chartGrid;
        private SKColor _tooltipBg, _tooltipBorder;

        // Cached SKTypeface instances (native resources — allocate once)
        private static readonly SKTypeface s_segoeUI = SKTypeface.FromFamilyName("Segoe UI");
        private static readonly SKTypeface s_segoeUIBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

        // Cached SKColors
        private static readonly SKColor s_barNormal = SKColor.Parse("#E74C3C");
        private static readonly SKColor s_barHover = SKColor.Parse("#FF6B5A");
        private static readonly SKColor s_accentDark = SKColor.Parse("#3498DB");
        private static readonly SKColor s_accentLight = SKColor.Parse("#2196F3");

        private static readonly SKColor s_darkBg = SKColor.Parse("#2D2D30");
        private static readonly SKColor s_darkTextDim = SKColor.Parse("#C8C8C8");
        private static readonly SKColor s_darkGrid = SKColors.White.WithAlpha(25);
        private static readonly SKColor s_darkTooltipBg = SKColor.Parse("#1E3A5F").WithAlpha(245);
        private static readonly SKColor s_darkTooltipBorder = SKColor.Parse("#3498DB").WithAlpha(100);
        private static readonly SKColor s_lightBg = SKColor.Parse("#FAFAFA");
        private static readonly SKColor s_lightText = SKColor.Parse("#333333");
        private static readonly SKColor s_lightTextDim = SKColor.Parse("#666666");
        private static readonly SKColor s_lightGrid = SKColors.Black.WithAlpha(20);
        private static readonly SKColor s_lightTooltipBg = SKColors.White.WithAlpha(240);
        private static readonly SKColor s_lightTooltipBorder = SKColor.Parse("#2196F3").WithAlpha(120);

        private static readonly SKColor[] ChartColors = new[]
        {
            SKColor.Parse("#E74C3C"), SKColor.Parse("#F1C40F"), SKColor.Parse("#3498DB"),
            SKColor.Parse("#2ECC71"), SKColor.Parse("#9B59B6"), SKColor.Parse("#E67E22"),
            SKColor.Parse("#95A5A6"), SKColor.Parse("#16A085"), SKColor.Parse("#F39C12"),
            SKColor.Parse("#8E44AD")
        };

        // Cached SKPaint instances — created once, reused per frame
        private readonly SKPaint _cachedTextPaint11 = new SKPaint { IsAntialias = true };
        private readonly SKFont _cachedTextFont11 = new SKFont(s_segoeUI, 11);
        private readonly SKPaint _cachedTextPaint11Bold = new SKPaint { IsAntialias = true };
        private readonly SKFont _cachedTextFont11Bold = new SKFont(s_segoeUIBold, 11);
        private readonly SKPaint _cachedTextPaint10 = new SKPaint { IsAntialias = true };
        private readonly SKFont _cachedTextFont10 = new SKFont(s_segoeUI, 10);
        private readonly SKPaint _cachedTextPaint9 = new SKPaint { IsAntialias = true };
        private readonly SKFont _cachedTextFont9 = new SKFont(s_segoeUI, 9);
        private readonly SKPaint _cachedFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _cachedStrokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _cachedGridPaint = new SKPaint { StrokeWidth = 1, IsAntialias = false };
        private readonly SKPaint _cachedTooltipBgPaint = new SKPaint { IsAntialias = true };
        private readonly SKPaint _cachedTooltipBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

        public StatsWindow(IEnumerable<LogEntry> plcLogs, IEnumerable<LogEntry> appLogs,
            Action<string, string>? applyFilterCallback = null,
            Action<LogEntry>? navigateToLogCallback = null,
            bool isDarkMode = true, bool hasBinaryAppLogs = false)
        {
            InitializeComponent();

            _vm = new StatsViewModel(plcLogs, appLogs, applyFilterCallback, navigateToLogCallback, isDarkMode, hasBinaryAppLogs);
            ApplyChartTheme(isDarkMode);

            // In S4-5, hide Method panels
            if (hasBinaryAppLogs)
            {
                AppMethodErrorPanel.Visibility = Visibility.Collapsed;
                AppMethodErrorColumn.Width = new GridLength(0);
                AppMethodErrorSpacer.Width = new GridLength(0);
                AppMethodLoadPanel.Visibility = Visibility.Collapsed;
                AppMethodLoadColumn.Width = new GridLength(0);
                AppMethodLoadSpacer.Width = new GridLength(0);
            }

            Loaded += async (s, e) =>
            {
                await _vm.CalculateStatisticsAsync();
                ApplyViewModelToUI();
            };
        }

        /// <summary>Push ViewModel data into named UI elements that aren't data-bound.</summary>
        private void ApplyViewModelToUI()
        {
            SummaryText.Text = _vm.SummaryText;

            // PLC tab
            if (_vm.PlcHasLogs)
            {
                PlcSummaryText.Text = _vm.PlcSummaryText;
                PlcErrorHistogram.ItemsSource = _vm.PlcErrorStats;
                PlcErrorCountText.Text = _vm.PlcErrorCountText;
                PlcThreadHistogram.ItemsSource = _vm.PlcThreadStats;
                PlcThreadCountText.Text = _vm.PlcThreadCountText;
                if (_vm.PlcHasGaps)
                {
                    PlcGapSummaryText.Text = _vm.PlcGapSummaryText;
                    PlcGapDataGrid.ItemsSource = _vm.PlcGaps;
                    PlcGapDataGrid.Visibility = Visibility.Visible;
                    PlcNoGapsMessage.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PlcGapSummaryText.Text = _vm.PlcGapSummaryText;
                    PlcGapDataGrid.Visibility = Visibility.Collapsed;
                    PlcNoGapsMessage.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PlcSummaryText.Text = _vm.PlcSummaryText;
            }

            // APP tab
            if (_vm.AppHasLogs)
            {
                AppSummaryText.Text = _vm.AppSummaryText;
                AppThreadErrorHistogram.ItemsSource = _vm.AppThreadErrorStats;
                AppThreadErrorCountText.Text = _vm.AppThreadErrorCountText;
                AppThreadHistogram.ItemsSource = _vm.AppThreadStats;
                AppThreadCountText.Text = _vm.AppThreadCountText;
                if (!_vm.HasBinaryAppLogs && _vm.AppMethodErrorStats != null)
                {
                    AppMethodErrorHistogram.ItemsSource = _vm.AppMethodErrorStats;
                    AppMethodErrorCountText.Text = _vm.AppMethodErrorCountText;
                    AppMethodHistogram.ItemsSource = _vm.AppMethodStats;
                    AppMethodCountText.Text = _vm.AppMethodCountText;
                }
                if (_vm.AppHasGaps)
                {
                    AppGapSummaryText.Text = _vm.AppGapSummaryText;
                    AppGapDataGrid.ItemsSource = _vm.AppGaps;
                    AppGapDataGrid.Visibility = Visibility.Visible;
                    AppNoGapsMessage.Visibility = Visibility.Collapsed;
                }
                else
                {
                    AppGapSummaryText.Text = _vm.AppGapSummaryText;
                    AppGapDataGrid.Visibility = Visibility.Collapsed;
                    AppNoGapsMessage.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AppSummaryText.Text = _vm.AppSummaryText;
            }

            // Advanced Analytics tab
            AnalyticsSummaryText.Text = _vm.AnalyticsSummaryText;
            LoggerChartCountText.Text = _vm.LoggerChartCountText;
            StateChartCountText.Text = _vm.StateChartCountText;
            BarChartCanvas.InvalidateVisual();
            PieChartCanvas.InvalidateVisual();
            if (_vm.TimelineBuckets != null)
                TimelineChartCanvas.InvalidateVisual();
            if (!string.IsNullOrEmpty(_vm.TimelineChartInfoText))
                TimelineChartInfo.Text = _vm.TimelineChartInfoText;

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cachedTextPaint11?.Dispose();
            _cachedTextPaint11Bold?.Dispose();
            _cachedTextPaint10?.Dispose();
            _cachedTextPaint9?.Dispose();
            _cachedFillPaint?.Dispose();
            _cachedStrokePaint?.Dispose();
            _cachedGridPaint?.Dispose();
            _cachedTooltipBgPaint?.Dispose();
            _cachedTooltipBorderPaint?.Dispose();
            _vm?.Dispose();
        }

        private void ApplyChartTheme(bool isDarkMode)
        {
            if (isDarkMode)
            {
                _chartBg = s_darkBg;       _chartText = SKColors.White;
                _chartTextDim = s_darkTextDim; _chartGrid = s_darkGrid;
                _tooltipBg = s_darkTooltipBg;  _tooltipBorder = s_darkTooltipBorder;
            }
            else
            {
                _chartBg = s_lightBg;      _chartText = s_lightText;
                _chartTextDim = s_lightTextDim; _chartGrid = s_lightGrid;
                _tooltipBg = s_lightTooltipBg;  _tooltipBorder = s_lightTooltipBorder;
            }
        }

        // ==========================================
        //  BUTTON HANDLERS
        // ==========================================
        private void Export_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string report = _vm.BuildExportReport();
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"LogStats_Full_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };
                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, report);
                    MessageBox.Show("Report exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

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
            _cachedTextPaint11.TextAlign = SKTextAlign.Left;
            _cachedTextPaint11Bold.Color = _chartText;
            _cachedTextPaint11Bold.TextAlign = SKTextAlign.Left;

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
                    var path = new SKPath();
                    path.MoveTo(cx + exX, cy + exY);
                    path.ArcTo(new SKRect(cx - radius + exX, cy - radius + exY, cx + radius + exX, cy + radius + exY), startAngle, sweep, false);
                    path.Close();
                    canvas.DrawPath(path, _cachedFillPaint);
                    canvas.DrawPath(path, _cachedStrokePaint);
                    path.Dispose();
                }

                if (sweep > 18)
                {
                    float labelR = radius * 0.65f;
                    float lx = cx + exX + (float)(labelR * Math.Cos(midAngle * Math.PI / 180));
                    float ly = cy + exY + (float)(labelR * Math.Sin(midAngle * Math.PI / 180));
                    _cachedTextPaint11Bold.Color = SKColors.White;
                    _cachedTextPaint11Bold.TextAlign = SKTextAlign.Center;
                    canvas.DrawText($"{(float)item.Count / total * 100:F0}%", lx, ly + 4, _cachedTextFont11Bold, _cachedTextPaint11Bold);
                }

                startAngle += sweep;
            }

            // Legend
            float legendX = chartAreaW + 10;
            float legendY = 15;
            _cachedTextPaint10.Color = _chartTextDim.WithAlpha(180);
            _cachedTextPaint10.TextAlign = SKTextAlign.Left;

            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                var color = ChartColors[i % ChartColors.Length];
                bool isHov = i == _hoveredPieIndex;

                _cachedFillPaint.Color = isHov ? color : color.WithAlpha(200);
                canvas.DrawCircle(legendX + 6, legendY + 6, 6, _cachedFillPaint);

                string name = item.State.Length > 16 ? item.State.Substring(0, 13) + "..." : item.State;
                _cachedTextPaint11.Color = isHov ? _chartText : _chartTextDim;
                _cachedTextPaint11.TextAlign = SKTextAlign.Left;
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
                        _cachedTextPaint9.TextAlign = SKTextAlign.Center;
                        float labelX = x1 + bandWidth / 2;
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
            _cachedTextPaint10.TextAlign = SKTextAlign.Right;
            {
                int gridLines = 4;
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = topM + (chartH / gridLines) * i;
                    int val = (int)(maxVal * (1.0 - (double)i / gridLines));
                    canvas.DrawText(val.ToString(), leftM - 6, y + 4, _cachedTextFont10, _cachedTextPaint10);
                }
            }

            // X-axis labels
            _cachedTextPaint10.TextAlign = SKTextAlign.Center;
            {
                int labelCount = Math.Min(8, visibleCount);
                int labelStep = Math.Max(1, visibleCount / labelCount);
                for (int vi = 0; vi < visibleCount; vi += labelStep)
                {
                    int i = zStart + vi;
                    float x = leftM + vi * stepW + stepW / 2;
                    var time = _vm.TimelineFirstTime.AddSeconds(i * _vm.TimelineBucketSize);
                    canvas.DrawText(time.ToString("HH:mm:ss"), x, topM + chartH + 18, _cachedTextFont10, _cachedTextPaint10);
                }
            }

            // Zoom hint
            if (visibleCount < _vm.TimelineBucketCount)
            {
                string zoomText = $"Zoom: {visibleCount}/{_vm.TimelineBucketCount} buckets  (Scroll to zoom, Shift+Scroll to pan)";
                _cachedTextPaint9.Color = _chartTextDim.WithAlpha(150);
                _cachedTextPaint9.TextAlign = SKTextAlign.Right;
                canvas.DrawText(zoomText, w - rightM, topM + chartH + 30, _cachedTextFont9, _cachedTextPaint9);
            }

            // Hover tooltip
            if (_hoveredTimelineBucket >= 0 && _hoveredTimelineBucket < _vm.TimelineBucketCount)
            {
                var bucketStart = _vm.TimelineFirstTime.AddSeconds(_hoveredTimelineBucket * _vm.TimelineBucketSize);
                var bucketEnd = bucketStart.AddSeconds(_vm.TimelineBucketSize);
                int count = _vm.TimelineBuckets[_hoveredTimelineBucket];
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

            _cachedTextPaint11.TextAlign = SKTextAlign.Left;
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

            using (var shadowPaint = new SKPaint { Color = SKColors.Black.WithAlpha(100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4), IsAntialias = true })
                canvas.DrawRoundRect(rect.Left + 2, rect.Top + 2, rect.Width, rect.Height, 6, 6, shadowPaint);

            _cachedTooltipBgPaint.Color = _tooltipBg;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBgPaint);
            _cachedTooltipBorderPaint.Color = _tooltipBorder;
            canvas.DrawRoundRect(rect, 6, 6, _cachedTooltipBorderPaint);

            _cachedTextPaint11.Color = _chartText;
            for (int i = 0; i < lines.Length; i++)
                canvas.DrawText(lines[i], x + padding, y + padding + (i + 1) * lineH - 3, _cachedTextFont11, _cachedTextPaint11);
        }
    }

    public class LoadStat
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string DisplayText { get; set; } = "";
        public double BarWidth { get; set; }
    }

    public class ErrorStat
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Message { get; set; } = "";
        public int Count { get; set; }
        public string DisplayText { get; set; } = "";
        public double BarWidth { get; set; }
    }
}
