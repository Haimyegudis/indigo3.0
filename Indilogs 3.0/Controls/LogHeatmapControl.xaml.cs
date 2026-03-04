using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls
{
    /// <summary>
    /// Log Heatmap Scrollbar - provides a bird's-eye view of errors and state transitions
    /// </summary>
    public partial class LogHeatmapControl : UserControl
    {
        #region Constants
        private const int TICK_HEIGHT = 2;
        private const int UPDATE_DELAY_MS = 150;
        #endregion

        #region Colors
        private static readonly SolidColorBrush ErrorBrush           = new SolidColorBrush(Color.FromRgb(255, 69,  58));   // Red
        private static readonly SolidColorBrush MarkedBrush           = new SolidColorBrush(Color.FromRgb(144, 238, 144));  // Light Green (matches LogEntry.MarkedBrush)
        private static readonly SolidColorBrush StateTransitionBrush  = new SolidColorBrush(Color.FromRgb(173, 216, 230));  // Light Blue
        private static readonly SolidColorBrush BackgroundBrush       = new SolidColorBrush(Color.FromArgb(60, 20, 30, 50));

        static LogHeatmapControl()
        {
            ErrorBrush.Freeze();
            MarkedBrush.Freeze();
            StateTransitionBrush.Freeze();
            BackgroundBrush.Freeze();
        }
        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable<LogEntry>),
                typeof(LogHeatmapControl),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable<LogEntry>? ItemsSource
        {
            get => (IEnumerable<LogEntry>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty LinkedDataGridProperty =
            DependencyProperty.Register(
                nameof(LinkedDataGrid),
                typeof(DataGrid),
                typeof(LogHeatmapControl),
                new PropertyMetadata(null, OnLinkedDataGridChanged));

        public DataGrid? LinkedDataGrid
        {
            get => (DataGrid?)GetValue(LinkedDataGridProperty);
            set => SetValue(LinkedDataGridProperty, value);
        }

        /// <summary>
        /// When true, "==== STATE" messages are always shown as cyan regardless of their Level.
        /// Set this to match HasBinaryAppLogs on the parent view model.
        /// </summary>
        public static readonly DependencyProperty IsBinaryAppProperty =
            DependencyProperty.Register(
                nameof(IsBinaryApp), typeof(bool),
                typeof(LogHeatmapControl), new PropertyMetadata(false));

        public bool IsBinaryApp
        {
            get => (bool)GetValue(IsBinaryAppProperty);
            set => SetValue(IsBinaryAppProperty, value);
        }

        #endregion

        #region Events

        public event Action<LogEntry>? RequestScrollToLog;

        #endregion

        #region Fields

        private readonly Canvas _canvas;
        private readonly DispatcherTimer _updateTimer;
        private List<HeatmapTick> _tickCache = new List<HeatmapTick>();
        private INotifyCollectionChanged? _observableSource;
        private ScrollViewer? _dataGridScrollViewer;

        #endregion

        #region Constructor

        public LogHeatmapControl()
        {
            InitializeComponent();

            _canvas = new Canvas
            {
                Background = BackgroundBrush,
                ClipToBounds = true
            };
            Content = _canvas;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UPDATE_DELAY_MS)
            };
            _updateTimer.Tick += (s, e) =>
            {
                _updateTimer.Stop();
                RedrawHeatmap();
            };

            SizeChanged += (s, e) => ScheduleRedraw();
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            Unloaded += (s, e) => _updateTimer?.Stop();
        }

        #endregion

        #region Rendering

        private void RedrawHeatmap()
        {
            _canvas.Children.Clear();
            _tickCache.Clear();

            var width = ActualWidth;
            var height = ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            var items = ItemsSource?.ToList();
            if (items == null || items.Count == 0)
                return;

            var totalCount = items.Count;

            // Track used Y positions for pixel snapping
            var usedPixels = new Dictionary<int, HeatmapTickType>();

            for (int i = 0; i < totalCount; i++)
            {
                var log = items[i];
                var tickType = GetTickType(log);

                if (tickType == HeatmapTickType.None)
                    continue;

                // Calculate Y position
                double yPos = (double)i / totalCount * height;
                yPos = Math.Max(0, Math.Min(yPos, height - TICK_HEIGHT));

                int yPixel = (int)yPos;

                // Pixel snapping - higher priority wins
                if (usedPixels.TryGetValue(yPixel, out var existingType))
                {
                    if (tickType >= existingType)
                        continue;
                }

                usedPixels[yPixel] = tickType;

                _tickCache.Add(new HeatmapTick
                {
                    LogEntry = log,
                    Index = i,
                    YPosition = yPos,
                    Type = tickType
                });
            }

            // Draw ticks (lower priority first so higher priority draws on top)
            var sortedTicks = _tickCache.OrderByDescending(t => t.Type).ToList();

            foreach (var tick in sortedTicks)
            {
                var rect = new Rectangle
                {
                    Width = width,
                    Height = TICK_HEIGHT,
                    Fill = GetBrushForType(tick.Type)
                };

                Canvas.SetLeft(rect, 0);
                Canvas.SetTop(rect, tick.YPosition);

                _canvas.Children.Add(rect);
            }
        }

        private HeatmapTickType GetTickType(LogEntry log)
        {
            // 0. State transitions (highest visual priority — shown even if Level=Error)
            //    a. S4-5 binary: "=== state" pattern
            if (IsBinaryApp &&
                log.Message != null &&
                log.Message.IndexOf("=== state", StringComparison.OrdinalIgnoreCase) >= 0)
                return HeatmapTickType.StateTransition;
            //    b. S6: Manager thread + "PlcMngr:" + "->"
            if (StateTransitionHelper.IsS6StateTransition(log))
                return HeatmapTickType.StateTransition;
            //    c. CustomColor is Light Blue (173, 216, 230) — from coloring service
            if (log.CustomColor.HasValue)
            {
                var c = log.CustomColor.Value;
                if (c.R == 173 && c.G == 216 && c.B == 230)
                    return HeatmapTickType.StateTransition;
            }

            // 1. Marked lines (green — user-marked rows)
            if (log.IsMarked || log.IsCurrentMarked)
                return HeatmapTickType.Marked;

            // 2. Actual errors only (red) — only Level="Error", NOT Events thread
            if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                return HeatmapTickType.Error;

            return HeatmapTickType.None;
        }

        private SolidColorBrush GetBrushForType(HeatmapTickType type)
        {
            switch (type)
            {
                case HeatmapTickType.Error:          return ErrorBrush;
                case HeatmapTickType.Marked:         return MarkedBrush;
                case HeatmapTickType.StateTransition: return StateTransitionBrush;
                default: return Brushes.Transparent;
            }
        }

        public void ScheduleRedraw()
        {
            if (!_updateTimer.IsEnabled)
            {
                _updateTimer.Start();
            }
        }

        #endregion

        #region Property Changed Handlers

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LogHeatmapControl)d;

            // Unsubscribe from old collection
            if (control._observableSource != null)
                control._observableSource.CollectionChanged -= control.OnCollectionChanged;

            // Subscribe to new collection
            if (e.NewValue is INotifyCollectionChanged observable)
            {
                control._observableSource = observable;
                observable.CollectionChanged += control.OnCollectionChanged;
            }
            else
            {
                control._observableSource = null;
            }

            control.ScheduleRedraw();
        }

        private static void OnLinkedDataGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LogHeatmapControl)d;

            if (e.NewValue is DataGrid dataGrid)
            {
                dataGrid.Loaded += (s, args) =>
                {
                    control._dataGridScrollViewer = GetScrollViewer(dataGrid);
                };
            }
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer sv)
                return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleRedraw();
        }

        #endregion

        #region Mouse Interaction

        private void OnMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            var tick = FindTickAtPosition(pos.Y);

            if (tick != null)
            {
                RequestScrollToLog?.Invoke(tick.LogEntry);
            }
            else
            {
                // Click on empty area - scroll to proportional position
                var items = ItemsSource?.ToList();
                if (items != null && items.Count > 0)
                {
                    int index = (int)(pos.Y / ActualHeight * items.Count);
                    index = Math.Max(0, Math.Min(index, items.Count - 1));
                    RequestScrollToLog?.Invoke(items[index]);
                }
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            var tick = FindTickAtPosition(pos.Y);

            if (tick != null)
            {
                var typeStr = tick.Type == HeatmapTickType.Error           ? "Error" :
                              tick.Type == HeatmapTickType.Marked          ? "Marked" :
                              tick.Type == HeatmapTickType.StateTransition ? "State Change" : "?";
                var timeStr = tick.LogEntry.Date.ToString("HH:mm:ss.ffffff");
                ToolTip = $"Line {tick.Index + 1} | {typeStr} | {timeStr}\n{Truncate(tick.LogEntry.Message, 60)}";
            }
            else
            {
                var items = ItemsSource?.ToList();
                if (items != null && items.Count > 0)
                {
                    int index = (int)(pos.Y / ActualHeight * items.Count);
                    index = Math.Max(0, Math.Min(index, items.Count - 1));
                    ToolTip = $"Line {index + 1} of {items.Count}";
                }
                else
                {
                    ToolTip = null;
                }
            }
        }

        private HeatmapTick? FindTickAtPosition(double y)
        {
            const double tolerance = 5;

            return _tickCache
                .Where(t => Math.Abs(t.YPosition - y) <= tolerance)
                .OrderBy(t => t.Type)
                .FirstOrDefault();
        }

        private string? Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "...";
        }

        #endregion

        #region Nested Types

        private enum HeatmapTickType
        {
            None            = 0,
            StateTransition = 1,  // Light Blue  – highest priority (state changes always visible)
            Marked          = 2,  // Light Green – user-marked rows always visible
            Error           = 3   // Red         – actual Level="Error" only
        }

        private class HeatmapTick
        {
            public LogEntry LogEntry { get; set; } = null!;
            public int Index { get; set; }
            public double YPosition { get; set; }
            public HeatmapTickType Type { get; set; }
        }

        #endregion
    }
}
