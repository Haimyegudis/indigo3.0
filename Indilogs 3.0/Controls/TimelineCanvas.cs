using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading; // חובה עבור DispatcherTimer
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Controls
{
    public class TimelineCanvas : FrameworkElement
    {
        public static readonly DependencyProperty StatesProperty = DependencyProperty.Register("States", typeof(IEnumerable<TimelineState>), typeof(TimelineCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register("Markers", typeof(IEnumerable<TimelineMarker>), typeof(TimelineCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ViewScaleProperty = DependencyProperty.Register("ViewScale", typeof(double), typeof(TimelineCanvas), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ViewOffsetProperty = DependencyProperty.Register("ViewOffset", typeof(double), typeof(TimelineCanvas), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<TimelineState> States { get => (IEnumerable<TimelineState>)GetValue(StatesProperty); set => SetValue(StatesProperty, value); }
        public IEnumerable<TimelineMarker> Markers { get => (IEnumerable<TimelineMarker>)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
        public double ViewScale { get => (double)GetValue(ViewScaleProperty); set => SetValue(ViewScaleProperty, value); }
        public double ViewOffset { get => (double)GetValue(ViewOffsetProperty); set => SetValue(ViewOffsetProperty, value); }

        public event EventHandler<TimelineState> StateClicked;
        public event EventHandler<TimelineMarker> MarkerClicked;

        // אינטראקציה וציור
        private bool _isDragging = false;
        private bool _isZooming = false;
        private Point _dragStart;
        private double _dragStartOffset;
        private Point _zoomStartPoint;
        private Point _currentMousePos;

        // ניהול Tooltip חכם
        private DispatcherTimer _hoverTimer;
        private object _currentHoverObject;
        private ToolTip _currentToolTip;

        private const double TIMELINE_Y = 50;
        private const double BAR_HEIGHT = 40;

        public TimelineCanvas()
        {
            ClipToBounds = true;
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.MouseWheel += OnMouseWheel;

            // אתחול טיימר לריחוף (1.5 שניות)
            _hoverTimer = new DispatcherTimer();
            _hoverTimer.Interval = TimeSpan.FromSeconds(1.5);
            _hoverTimer.Tick += OnHoverTimerTick;
        }

        private void OnHoverTimerTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop(); // עצרנו, עכשיו מציגים
            ShowTooltipFor(_currentHoverObject);
        }

        private void ShowTooltipFor(object obj)
        {
            if (obj == null) return;

            // סגירת טולטיפ קודם אם פתוח
            HideTooltip();

            string content = "";
            if (obj is TimelineState s)
                content = $"{s.Name}\nDuration: {s.Duration.TotalSeconds:F2}s\nErrors: {s.ErrorCount}\nStatus: {s.Status}";
            else if (obj is TimelineMarker m)
                content = $"{m.Type}\n{m.Message}\nTime: {m.Time:HH:mm:ss.fff}";

            if (string.IsNullOrEmpty(content)) return;

            _currentToolTip = new ToolTip
            {
                Content = content,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Cyan,
                Placement = PlacementMode.Mouse
            };
            _currentToolTip.IsOpen = true;
        }

        private void HideTooltip()
        {
            if (_currentToolTip != null)
            {
                _currentToolTip.IsOpen = false;
                _currentToolTip = null;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (States == null || !States.Any()) return;

            DateTime minTime = States.Min(s => s.StartTime);
            DateTime maxTime = States.Max(s => s.EndTime);
            double totalSeconds = (maxTime - minTime).TotalSeconds;
            if (totalSeconds <= 0) totalSeconds = 1;

            foreach (var state in States)
            {
                double startX = TimeToX((state.StartTime - minTime).TotalSeconds, ActualWidth, totalSeconds);
                double endX = TimeToX((state.EndTime - minTime).TotalSeconds, ActualWidth, totalSeconds);
                double width = Math.Max(2, endX - startX);

                if (endX > 0 && startX < ActualWidth)
                    DrawStateBar(dc, startX, TIMELINE_Y, width, BAR_HEIGHT, state);
            }

            if (Markers != null)
            {
                foreach (var marker in Markers)
                {
                    double x = TimeToX((marker.Time - minTime).TotalSeconds, ActualWidth, totalSeconds);
                    if (x > 0 && x < ActualWidth)
                    {
                        if (marker.Type == TimelineMarkerType.Error)
                            DrawErrorIcon(dc, x, TIMELINE_Y - 20);
                        else
                            DrawEventIcon(dc, x, TIMELINE_Y + BAR_HEIGHT + 5, marker.Color);
                    }
                }
            }

            // עדכון: העברת זמן ההתחלה לפונקציית הציור של הציר
            DrawTimeAxis(dc, ActualHeight - 30, ActualWidth, totalSeconds, minTime);

            if (_isZooming)
            {
                double x = Math.Min(_zoomStartPoint.X, _currentMousePos.X);
                double w = Math.Abs(_zoomStartPoint.X - _currentMousePos.X);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)), new Pen(Brushes.LightBlue, 1), new Rect(x, 0, w, ActualHeight));
            }
        }

        private void DrawStateBar(DrawingContext dc, double x, double y, double w, double h, TimelineState state)
        {
            // 1. קביעת צבעים בסיסיים
            Brush fillBrush = new SolidColorBrush(state.Color) { Opacity = 0.8 };
            Brush borderBrush = Brushes.Black;
            double thickness = 1;

            // 2. טיפול מיוחד בסטייט שנכשל (Critical Failure)
            bool isCriticalFailure = state.Status == "FAILED";

            if (isCriticalFailure)
            {
                // רקע אדום כהה יותר
                fillBrush = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                borderBrush = Brushes.Red; // מסגרת אדומה בוהקת
                thickness = 2;
            }
            else if (state.Status == "SUCCESS")
            {
                borderBrush = Brushes.LimeGreen;
                thickness = 2;
            }

            // 3. ציור המלבן הבסיסי
            dc.DrawRectangle(fillBrush, new Pen(borderBrush, thickness), new Rect(x, y, w, h));

            // 4. *** התוספת הויזואלית: פסי אזהרה לכישלונות ***
            if (isCriticalFailure)
            {
                DrawHazardPattern(dc, x, y, w, h);
            }

            // 5. ציור הטקסט
            if (w > 30)
            {
                // הוספת כיתוב (FAILED) לשם הסטייט אם נכשל
                string displayText = state.Name;
                if (isCriticalFailure) displayText += " (FAILED!)";

                var text = new FormattedText(displayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    12,
                    Brushes.White,
                    1.25);

                text.MaxTextWidth = Math.Max(w - 10, 0);
                text.Trimming = TextTrimming.CharacterEllipsis;

                // מרכוז הטקסט בתוך הבר
                double textY = y + (h - text.Height) / 2;
                dc.DrawText(text, new Point(x + 5, textY));
            }
        }

        private void DrawHazardPattern(DrawingContext dc, double x, double y, double w, double h)
        {
            // יצירת קליפ (Clip) כדי שהפסים לא יצאו מהמלבן
            dc.PushClip(new RectangleGeometry(new Rect(x, y, w, h)));

            Pen stripePen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 4); // פסים לבנים חצי שקופים
            double step = 15; // המרחק בין הפסים

            // ציור אלכסונים לאורך כל הרוחב
            for (double i = -h; i < w; i += step)
            {
                // קו אלכסוני מ-(i,0) ל-(i+h, h)
                dc.DrawLine(stripePen, new Point(x + i, y), new Point(x + i + h, y + h));
            }

            dc.Pop(); // שחרור הקליפ

            // אופציונלי: ציור אייקון X או גולגולת במרכז
            StreamGeometry xMark = new StreamGeometry();
            using (var ctx = xMark.Open())
            {
                double cx = x + w / 2;
                double cy = y + h / 2;
                double s = 6; // גודל ה-X

                ctx.BeginFigure(new Point(cx - s, cy - s), true, true);
                ctx.LineTo(new Point(cx + s, cy + s), true, true);

                ctx.BeginFigure(new Point(cx + s, cy - s), true, true);
                ctx.LineTo(new Point(cx - s, cy + s), true, true);
            }
            dc.DrawGeometry(null, new Pen(Brushes.White, 3), xMark);
        }

        private void DrawErrorIcon(DrawingContext dc, double x, double y)
        {
            dc.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 1), new Point(x, y + 6), 8, 8);
            StreamGeometry xMark = new StreamGeometry();
            using (var ctx = xMark.Open())
            {
                ctx.BeginFigure(new Point(x - 3, y + 3), false, false);
                ctx.LineTo(new Point(x + 3, y + 9), true, false);
                ctx.BeginFigure(new Point(x + 3, y + 3), false, false);
                ctx.LineTo(new Point(x - 3, y + 9), true, false);
            }
            dc.DrawGeometry(null, new Pen(Brushes.White, 2), xMark);
        }

        private void DrawEventIcon(DrawingContext dc, double x, double y, Color c)
        {
            StreamGeometry diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(x, y - 5), true, true);
                ctx.LineTo(new Point(x + 5, y), true, false);
                ctx.LineTo(new Point(x, y + 5), true, false);
                ctx.LineTo(new Point(x - 5, y), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(c), new Pen(Brushes.White, 1), diamond);
        }

        // שינוי בחתימת הפונקציה: קבלת DateTime startTime
        private void DrawTimeAxis(DrawingContext dc, double y, double w, double totalSeconds, DateTime startTime)
        {
            dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(0, y), new Point(w, y));
            double pixelPerSecond = (w * ViewScale) / totalSeconds;
            double step = 100 / pixelPerSecond;
            if (step < 1) step = 1;
            if (step > 60) step = 60; else if (step > 30) step = 30; else if (step > 10) step = 10; else if (step > 5) step = 5;

            double startSec = XToSeconds(0, w, totalSeconds);
            double endSec = XToSeconds(w, w, totalSeconds);

            for (double t = Math.Floor(startSec / step) * step; t < endSec; t += step)
            {
                double x = TimeToX(t, w, totalSeconds);
                dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, y), new Point(x, y + 5));

                // חישוב הזמן האבסולוטי לתצוגה
                DateTime absoluteTime = startTime.AddSeconds(t);

                var text = new FormattedText($"{absoluteTime:HH:mm:ss}",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    11,
                    Brushes.White,
                    1.25);

                dc.DrawText(text, new Point(x - text.Width / 2, y + 8));
            }
        }

        private double TimeToX(double sec, double w, double total) => ((sec - ViewOffset) / total) * w * ViewScale;
        private double XToSeconds(double x, double w, double total) => (x / (w * ViewScale)) * total + ViewOffset;

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _isZooming = true;
                    _zoomStartPoint = e.GetPosition(this);
                    this.CaptureMouse();
                }
                else
                {
                    _isDragging = true;
                    _dragStart = e.GetPosition(this);
                    _dragStartOffset = ViewOffset;
                    this.CaptureMouse();
                    CheckObjectClick(_dragStart);
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _currentMousePos = e.GetPosition(this);

            var hit = GetHitObject(_currentMousePos);
            if (hit != _currentHoverObject)
            {
                _hoverTimer.Stop();
                HideTooltip();

                _currentHoverObject = hit;

                if (hit != null)
                {
                    _hoverTimer.Start();
                }
            }

            if (_isZooming) InvalidateVisual();
            else if (_isDragging && States != null && States.Any())
            {
                double dx = _currentMousePos.X - _dragStart.X;
                DateTime min = States.Min(s => s.StartTime);
                DateTime max = States.Max(s => s.EndTime);
                double totalSec = (max - min).TotalSeconds;
                double pixelsPerSecond = (ActualWidth * ViewScale) / totalSec;
                if (pixelsPerSecond > 0) ViewOffset = _dragStartOffset - (dx / pixelsPerSecond);
                InvalidateVisual();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isZooming && States != null && States.Any())
            {
                double x1 = Math.Min(_zoomStartPoint.X, _currentMousePos.X);
                double x2 = Math.Max(_zoomStartPoint.X, _currentMousePos.X);
                double width = x2 - x1;
                if (width > 10)
                {
                    DateTime min = States.Min(s => s.StartTime);
                    DateTime max = States.Max(s => s.EndTime);
                    double totalSec = (max - min).TotalSeconds;
                    double t1 = XToSeconds(x1, ActualWidth, totalSec);
                    double t2 = XToSeconds(x2, ActualWidth, totalSec);
                    double newRange = t2 - t1;
                    if (newRange > 0) { ViewScale = totalSec / newRange; ViewOffset = t1; }
                }
                _isZooming = false;
                InvalidateVisual();
            }
            _isDragging = false;
            this.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (States == null) return;
            Point mousePos = e.GetPosition(this);
            DateTime min = States.Min(s => s.StartTime);
            DateTime max = States.Max(s => s.EndTime);
            double totalSec = (max - min).TotalSeconds;
            double mouseTimeBefore = XToSeconds(mousePos.X, ActualWidth, totalSec);
            double zoomFactor = e.Delta > 0 ? 1.2 : 0.8;
            ViewScale *= zoomFactor;
            if (ViewScale < 0.1) ViewScale = 0.1;
            double mouseTimeAfter = (mousePos.X / (ActualWidth * ViewScale)) * totalSec;
            ViewOffset = mouseTimeBefore - mouseTimeAfter;
            InvalidateVisual();
        }
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            _isDragging = false;
            _isZooming = false;

            HideTooltip();
            _hoverTimer.Stop();

            InvalidateVisual();
        }
        private void CheckObjectClick(Point p)
        {
            var hit = GetHitObject(p);
            if (hit is TimelineState s) StateClicked?.Invoke(this, s);
            else if (hit is TimelineMarker m) MarkerClicked?.Invoke(this, m);
        }

        private object GetHitObject(Point p)
        {
            if (States == null || !States.Any()) return null;
            DateTime min = States.Min(s => s.StartTime);
            DateTime max = States.Max(s => s.EndTime);
            double totalSec = (max - min).TotalSeconds;

            if (Markers != null)
            {
                foreach (var m in Markers)
                {
                    double mx = TimeToX((m.Time - min).TotalSeconds, ActualWidth, totalSec);
                    double my = m.Type == TimelineMarkerType.Error ? TIMELINE_Y - 20 : TIMELINE_Y + BAR_HEIGHT + 5;
                    if (Math.Abs(p.X - mx) < 8 && Math.Abs(p.Y - (my + 4)) < 8) return m;
                }
            }

            if (p.Y >= TIMELINE_Y && p.Y <= TIMELINE_Y + BAR_HEIGHT)
            {
                double timeClicked = XToSeconds(p.X, ActualWidth, totalSec);
                return States.FirstOrDefault(s => timeClicked >= (s.StartTime - min).TotalSeconds && timeClicked <= (s.EndTime - min).TotalSeconds);
            }
            return null;
        }
    }
}