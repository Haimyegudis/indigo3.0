using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartStateTimeline : UserControl
    {
        public event Action<int> OnTimelineClicked;
        public event Action<int, int> OnStateClicked; // start, end indices for state time window

        private List<StateInterval> _states = new List<StateInterval>();
        private int _totalDataLength = 0;
        private int _viewStartIndex = 0;
        private int _viewEndIndex = 0;
        private int _cursorIndex = -1;

        // Dark theme colors
        private static readonly SKColor BgColor = SKColor.Parse("#0D1B2A");
        private static readonly SKColor BorderColor = SKColor.Parse("#2D4A6F");
        private static readonly SKColor TextColor = SKColors.White;
        private static readonly SKColor CursorColor = SKColors.Red;

        private SKPaint _borderPaint = new SKPaint { Color = BorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private SKPaint _textPaint = new SKPaint { Color = TextColor, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) };
        private SKPaint _cursorPaint = new SKPaint { Color = CursorColor, StrokeWidth = 2, Style = SKPaintStyle.Stroke };

        public ChartStateTimeline()
        {
            InitializeComponent();
        }

        public void SetStates(List<StateInterval> states, int totalDataLength)
        {
            _states = states ?? new List<StateInterval>();
            _totalDataLength = totalDataLength;
            if (_viewEndIndex == 0 && _totalDataLength > 0)
            {
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;
            }
            SkiaCanvas.InvalidateVisual();
        }

        public void SyncViewRange(int start, int end)
        {
            _viewStartIndex = start;
            _viewEndIndex = end;
            SkiaCanvas.InvalidateVisual();
        }

        public void SyncCursor(int index)
        {
            _cursorIndex = index;
            SkiaCanvas.InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (_totalDataLength == 0) return;

            var pos = e.GetPosition(this);
            double ratio = pos.X / ActualWidth;
            int clickedIndex = (int)(ratio * _totalDataLength);
            clickedIndex = Math.Max(0, Math.Min(clickedIndex, _totalDataLength - 1));

            // Find the state at this index
            var state = _states?.FirstOrDefault(s =>
                clickedIndex >= s.StartIndex && clickedIndex <= s.EndIndex);

            if (state.HasValue && state.Value.StateId >= 0)
            {
                // Zoom to show the state time window with padding
                int stateLength = state.Value.EndIndex - state.Value.StartIndex;
                int padding = Math.Max(10, stateLength / 10);
                int startWithPadding = Math.Max(0, state.Value.StartIndex - padding);
                int endWithPadding = Math.Min(_totalDataLength - 1, state.Value.EndIndex + padding);

                OnStateClicked?.Invoke(startWithPadding, endWithPadding);
            }
            else
            {
                OnTimelineClicked?.Invoke(clickedIndex);
            }
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(BgColor);

            if (_totalDataLength == 0 || _states == null || _states.Count == 0) return;

            float w = info.Width;
            float h = info.Height;
            float padding = 2;
            float barHeight = h - (padding * 2);

            // Draw all states as colored rectangles (full timeline)
            foreach (var st in _states)
            {
                float x1 = (float)(st.StartIndex / (double)_totalDataLength * w);
                float x2 = (float)((st.EndIndex + 1) / (double)_totalDataLength * w);

                // Get color - use StateName to lookup ID if StateId is not set properly
                int effectiveStateId = st.StateId;
                if (!string.IsNullOrEmpty(st.StateName) && effectiveStateId <= 0)
                {
                    effectiveStateId = ChartStateConfig.GetId(st.StateName);
                }
                var color = ChartStateConfig.GetSolidColor(effectiveStateId);
                using (var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill })
                {
                    canvas.DrawRect(new SKRect(x1, padding, x2, h - padding), paint);
                }

                // Draw state name if there's enough space
                // Use StateName if available, otherwise fall back to StateId lookup
                string name = !string.IsNullOrEmpty(st.StateName) ? st.StateName : ChartStateConfig.GetName(st.StateId);
                float textWidth = _textPaint.MeasureText(name);
                if (textWidth < (x2 - x1) - 4)
                {
                    float textX = x1 + ((x2 - x1) - textWidth) / 2;
                    canvas.DrawText(name, textX, h / 2 + 4, _textPaint);
                }
            }

            // Draw view range indicator
            float viewX1 = (float)(_viewStartIndex / (double)_totalDataLength * w);
            float viewX2 = (float)((_viewEndIndex + 1) / (double)_totalDataLength * w);

            using (var viewPaint = new SKPaint { Color = SKColors.White.WithAlpha(60), Style = SKPaintStyle.Fill })
            {
                // Darken areas outside the view range
                if (viewX1 > 0)
                    canvas.DrawRect(new SKRect(0, 0, viewX1, h), new SKPaint { Color = SKColors.Black.WithAlpha(120), Style = SKPaintStyle.Fill });
                if (viewX2 < w)
                    canvas.DrawRect(new SKRect(viewX2, 0, w, h), new SKPaint { Color = SKColors.Black.WithAlpha(120), Style = SKPaintStyle.Fill });
            }

            // Draw view range borders
            using (var borderPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
            {
                canvas.DrawLine(viewX1, 0, viewX1, h, borderPaint);
                canvas.DrawLine(viewX2, 0, viewX2, h, borderPaint);
            }

            // Draw cursor position
            if (_cursorIndex >= 0 && _cursorIndex < _totalDataLength)
            {
                float cursorX = (float)(_cursorIndex / (double)_totalDataLength * w);
                canvas.DrawLine(cursorX, 0, cursorX, h, _cursorPaint);
            }

            // Draw border
            canvas.DrawRect(new SKRect(0, 0, w - 1, h - 1), _borderPaint);
        }
    }
}
