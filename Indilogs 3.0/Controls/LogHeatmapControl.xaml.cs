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
        private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 69, 58));       // Red for Errors/Events
        private static readonly SolidColorBrush StateTransitionBrush = new SolidColorBrush(Color.FromRgb(173, 216, 230)); // Light Blue for state transitions
        private static readonly SolidColorBrush BackgroundBrush = new SolidColorBrush(Color.FromArgb(60, 20, 30, 50));

        static LogHeatmapControl()
        {
            ErrorBrush.Freeze();
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

        public IEnumerable<LogEntry> ItemsSource
        {
            get => (IEnumerable<LogEntry>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty LinkedDataGridProperty =
            DependencyProperty.Register(
                nameof(LinkedDataGrid),
                typeof(DataGrid),
                typeof(LogHeatmapControl),
                new PropertyMetadata(null, OnLinkedDataGridChanged));

        public DataGrid LinkedDataGrid
        {
            get => (DataGrid)GetValue(LinkedDataGridProperty);
            set => SetValue(LinkedDataGridProperty, value);
        }

        #endregion

        #region Events

        public event Action<LogEntry> RequestScrollToLog;

        #endregion

        #region Fields

        private readonly Canvas _canvas;
        private readonly DispatcherTimer _updateTimer;
        private List<HeatmapTick> _tickCache = new List<HeatmapTick>();
        private INotifyCollectionChanged _observableSource;
        private ScrollViewer _dataGridScrollViewer;

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
            // Only Error/Events (red) and State Transitions (light blue)

            // Check for Errors and Events (including Thread="Events")
            if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase) ||
                log.IsErrorOrEvents ||
                string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                return HeatmapTickType.Error;

            // Check for state transitions:
            // 1. CustomColor is Light Blue (173, 216, 230)
            // 2. OR Thread=Manager AND Message starts with "PlcMngr:" AND contains "->"
            if (log.CustomColor.HasValue)
            {
                var c = log.CustomColor.Value;
                if (c.R == 173 && c.G == 216 && c.B == 230) // Light Blue
                    return HeatmapTickType.StateTransition;
            }

            // Also check by pattern (in case coloring hasn't been applied yet)
            if (string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) &&
                log.Message != null &&
                log.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                log.Message.Contains("->"))
            {
                return HeatmapTickType.StateTransition;
            }

            return HeatmapTickType.None;
        }

        private SolidColorBrush GetBrushForType(HeatmapTickType type)
        {
            switch (type)
            {
                case HeatmapTickType.Error: return ErrorBrush;
                case HeatmapTickType.StateTransition: return StateTransitionBrush;
                default: return Brushes.Transparent;
            }
        }

        private void ScheduleRedraw()
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

            if (control._observableSource != null)
            {
                control._observableSource.CollectionChanged -= control.OnCollectionChanged;
            }

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

        private static ScrollViewer GetScrollViewer(DependencyObject depObj)
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

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleRedraw();
        }

        #endregion

        #region Mouse Interaction

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            var tick = FindTickAtPosition(pos.Y);

            if (tick != null)
            {
                var typeStr = tick.Type == HeatmapTickType.Error ? "Error/Event" : "State Change";
                var timeStr = tick.LogEntry.Date.ToString("HH:mm:ss.fff");
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

        private HeatmapTick FindTickAtPosition(double y)
        {
            const double tolerance = 5;

            return _tickCache
                .Where(t => Math.Abs(t.YPosition - y) <= tolerance)
                .OrderBy(t => t.Type)
                .FirstOrDefault();
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "...";
        }

        #endregion

        #region Nested Types

        private enum HeatmapTickType
        {
            None = 0,
            Error = 1,           // Red - highest priority
            StateTransition = 2  // Light Blue
        }

        private class HeatmapTick
        {
            public LogEntry LogEntry { get; set; }
            public int Index { get; set; }
            public double YPosition { get; set; }
            public HeatmapTickType Type { get; set; }
        }

        #endregion
    }
}
