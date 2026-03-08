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
            if (depObj is not Visual && depObj is not System.Windows.Media.Media3D.Visual3D)
                return null;

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
