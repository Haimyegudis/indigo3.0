using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SkiaSharp;
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
            _cachedShadowBlurPaint?.Dispose();
            _cachedPiePath?.Dispose();
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
