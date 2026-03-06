using System;
using System.Linq;
using System.Text;
using SkiaSharp;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services.Charts;


namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartGraphView
    {
        private readonly record struct ChartBounds(
            float Left, float Right, float Top, float Bottom,
            float Width, float Height, int Start, int End, int Count);

        private readonly record struct ScaleInfo(
            double LeftDisplayMin, double LeftRange,
            double RightDisplayMin, double RightRange,
            bool HasLeft, bool HasRight);

        private void DrawStates(SKCanvas canvas, in ChartBounds b)
        {
            if (!_showStates || _states == null) return;

            foreach (var st in _states)
            {
                if (st.EndIndex < b.Start || st.StartIndex > b.End) continue;
                float x1 = (float)((Math.Max(st.StartIndex, b.Start) - b.Start) / (double)b.Count * b.Width);
                float x2 = (float)((Math.Min(st.EndIndex, b.End) - b.Start) / (double)b.Count * b.Width);
                _stateFillPaint.Color = ChartStateConfig.GetColor(st.StateId);
                canvas.DrawRect(new SKRect(b.Left + x1, b.Top, b.Left + x2, b.Bottom), _stateFillPaint);

                string nm = ChartStateConfig.GetName(st.StateId);
                float tw = _stateTextFont.MeasureText(nm);
                if (tw < (x2 - x1) - 4)
                {
                    canvas.DrawText(nm, (float)Math.Round(b.Left + x1 + (x2 - x1) / 2 - tw / 2), (float)Math.Round(b.Top + 14), _stateTextFont, _stateTextPaint);
                }
            }
        }

        private void DrawTimeGaps(SKCanvas canvas, in ChartBounds b)
        {
            if (_timeGaps == null || _timeGaps.Count == 0) return;

            foreach (var gap in _timeGaps)
            {
                if (gap.EndIndex < b.Start || gap.StartIndex > b.End) continue;

                float gx1 = b.Left + (float)((Math.Max(gap.StartIndex, b.Start) - b.Start) / (double)b.Count * b.Width);
                float gx2 = b.Left + (float)((Math.Min(gap.EndIndex, b.End) - b.Start) / (double)b.Count * b.Width);

                if (gx2 - gx1 < 3) gx2 = gx1 + 3;

                var gapRect = new SKRect(gx1, b.Top, gx2, b.Bottom);
                canvas.DrawRect(gapRect, _gapFillPaint);
                canvas.DrawRect(gapRect, _gapBorderPaint);

                if (!string.IsNullOrEmpty(gap.Duration))
                {
                    string label = $"GAP {gap.Duration}";
                    float tw = _gapTextFont.MeasureText(label);
                    float labelX = gx1 + (gx2 - gx1) / 2 - tw / 2;
                    if (tw < (gx2 - gx1) - 4)
                    {
                        canvas.DrawText(label, labelX, b.Top + 12, _gapTextFont, _gapTextPaint);
                    }
                    else
                    {
                        canvas.DrawText("GAP", gx1 + 2, b.Top + 12, _gapTextFont, _gapTextPaint);
                    }
                }
            }
        }

        private void DrawReferenceLines(SKCanvas canvas, in ChartBounds b, in ScaleInfo scale)
        {
            if (_referenceLines == null) return;

            foreach (var line in _referenceLines)
            {
                _refLinePaint.Color = line.Color;
                _refLinePaint.StrokeWidth = line.Thickness;
                _refLinePaint.PathEffect = line.IsDashed ? _refDashEffect : null;

                if (line.Type == ReferenceLineType.Vertical)
                {
                    int idx = (int)line.Value;
                    if (idx >= b.Start && idx <= b.End)
                    {
                        float x = b.Left + (float)((idx - b.Start) / (double)b.Count * b.Width);
                        canvas.DrawLine(x, b.Top, x, b.Bottom, _refLinePaint);

                        if (!string.IsNullOrEmpty(line.Name))
                        {
                            _refLineTextPaint.Color = line.Color;
                            canvas.DrawText(line.Name, x + 4, b.Top + 12, _refLineTextFont, _refLineTextPaint);
                        }
                    }
                }
                else
                {
                    double range = (line.YAxis == AxisType.Left) ? scale.LeftRange : scale.RightRange;
                    double dMin = (line.YAxis == AxisType.Left) ? scale.LeftDisplayMin : scale.RightDisplayMin;

                    if (line.Value >= dMin && line.Value <= (dMin + range))
                    {
                        float y = b.Bottom - (float)((line.Value - dMin) / range * b.Height);
                        canvas.DrawLine(b.Left, y, b.Right, y, _refLinePaint);

                        if (!string.IsNullOrEmpty(line.Name))
                        {
                            _refLineTextPaint.Color = line.Color;
                            canvas.DrawText(line.Name, b.Left + 4, y - 4, _refLineTextFont, _refLineTextPaint);
                        }
                    }
                }
            }
        }

        private void DrawThreadMarkers(SKCanvas canvas, in ChartBounds b)
        {
            if (_threadMessages == null || _threadMessages.Count == 0) return;

            _threadLinePaint.PathEffect = _threadDashEffect;

            foreach (var msg in _threadMessages)
            {
                if (msg.TimeIndex < b.Start || msg.TimeIndex > b.End) continue;

                if (!_threadColorMap.TryGetValue(msg.ThreadName, out SKColor markerColor))
                    markerColor = ThreadMarkerColors[0];

                float x = b.Left + (float)((msg.TimeIndex - b.Start) / (double)b.Count * b.Width);

                _threadLinePaint.Color = markerColor;
                canvas.DrawLine(x, b.Top, x, b.Bottom, _threadLinePaint);

                _threadTrianglePaint.Color = markerColor;
                using (var trianglePath = new SKPath())
                {
                    trianglePath.MoveTo(x, b.Top);
                    trianglePath.LineTo(x - 5, b.Top - 8);
                    trianglePath.LineTo(x + 5, b.Top - 8);
                    trianglePath.Close();
                    canvas.DrawPath(trianglePath, _threadTrianglePaint);
                }
            }
        }

        private void DrawEventMarkers(SKCanvas canvas, in ChartBounds b)
        {
            _hoveredEventIndex = -1;
            if (_chartEventMarkers == null || _chartEventMarkers.Count == 0) return;

            float eventY = b.Bottom - 8;

            foreach (var evt in _chartEventMarkers)
            {
                if (evt.Index < b.Start || evt.Index > b.End) continue;

                float ex = b.Left + (float)((evt.Index - b.Start) / (double)b.Count * b.Width);

                canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotPaint);
                canvas.DrawCircle(ex, eventY, EVENT_DOT_RADIUS, _eventDotBorderPaint);

                {
                    float dx = (float)_hoverPos.X - ex;
                    float dy = (float)_hoverPos.Y - eventY;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist < EVENT_DOT_RADIUS * 4)
                    {
                        _hoveredEventIndex = evt.Index;
                    }
                }
            }

            if (_hoveredEventIndex >= 0)
            {
                var hoveredEvent = _chartEventMarkers.FirstOrDefault(e => e.Index == _hoveredEventIndex);
                if (hoveredEvent != null)
                {
                    float hx = b.Left + (float)((hoveredEvent.Index - b.Start) / (double)b.Count * b.Width);

                    canvas.DrawCircle(hx, eventY, EVENT_DOT_RADIUS + 3, _eventHighlightPaint);

                    var sb = new StringBuilder();
                    sb.AppendLine("=== EVENT ===");
                    if (!string.IsNullOrEmpty(hoveredEvent.Time))
                        sb.AppendLine($"Time: {hoveredEvent.Time}");
                    if (!string.IsNullOrEmpty(hoveredEvent.Name))
                        sb.AppendLine($"Name: {hoveredEvent.Name}");
                    if (!string.IsNullOrEmpty(hoveredEvent.Message))
                        sb.AppendLine($"Message: {hoveredEvent.Message}");
                    if (!string.IsNullOrEmpty(hoveredEvent.Severity))
                        sb.AppendLine($"Severity: {hoveredEvent.Severity}");
                    if (!string.IsNullOrEmpty(hoveredEvent.Description))
                        sb.AppendLine($"Source: {hoveredEvent.Description}");

                    float tooltipX = hx + 15;
                    float tooltipY = eventY - 40;
                    DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
                }
            }
        }

        private void DrawMeasureBox(SKCanvas canvas, in ChartBounds b, in ScaleInfo scale)
        {
            if (_measureStartIndex == -1 || _measureCurrentIndex == -1) return;

            int mS = Math.Max(Math.Min(_measureStartIndex, _measureCurrentIndex), b.Start);
            int mE = Math.Min(Math.Max(_measureStartIndex, _measureCurrentIndex), b.End);

            if (mE <= mS) return;

            float x1 = b.Left + (float)((mS - b.Start) / (double)b.Count * b.Width);
            float x2 = b.Left + (float)((mE - b.Start) / (double)b.Count * b.Width);
            var rect = new SKRect(x1, b.Top, x2, b.Bottom);
            canvas.DrawRect(rect, _measureFillPaint);
            canvas.DrawRect(rect, _measureBorderPaint);

            if (_isMeasuring) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== AREA MEASUREMENT ===");
            sb.AppendLine($"Index Range: {mS} -> {mE}");
            sb.AppendLine($"Points: {mE - mS + 1}");

            if (GetXAxisLabel != null)
            {
                string t1 = GetXAxisLabel(mS);
                string t2 = GetXAxisLabel(mE);
                if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                {
                    sb.AppendLine($"Time: {t1} -> {t2}");
                }
            }

            sb.AppendLine("-------------------");

            foreach (var s in _seriesList)
            {
                if (!s.IsVisible || s.Data == null) continue;
                var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;
                double sum = 0, mn = double.MaxValue, mx = double.MinValue;
                int c = 0;

                for (int i = mS; i <= mE; i++)
                {
                    if (i < dataToDraw.Length && !double.IsNaN(dataToDraw[i]))
                    {
                        double v = dataToDraw[i];
                        sum += v;
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                        c++;
                    }
                }

                if (c > 0)
                {
                    double avg = sum / c;
                    sb.AppendLine($"{s.Name}:");
                    sb.AppendLine($"  Avg: {avg:F3}");
                    sb.AppendLine($"  Min: {mn:F3}");
                    sb.AppendLine($"  Max: {mx:F3}");
                    sb.AppendLine($"  Delta: {(mx - mn):F3}");
                }
            }

            float tooltipX = (_measureCurrentIndex > _measureStartIndex) ? x2 + 15 : x1 - 170;
            float tooltipY = b.Top + 10;
            DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
        }

        private void DrawCtrlMeasurement(SKCanvas canvas, in ChartBounds b)
        {
            if (_ctrlPoint1 == -1 || _ctrlPoint1 < b.Start || _ctrlPoint1 > b.End) return;

            float x1 = b.Left + (float)((_ctrlPoint1 - b.Start) / (double)b.Count * b.Width);
            float y1 = (float)_ctrlPoint1Pos.Y;

            y1 = Math.Max(b.Top, Math.Min(b.Bottom, y1));

            canvas.DrawLine(x1, b.Top, x1, b.Bottom, _ctrlMeasurePaint);
            canvas.DrawLine(b.Left, y1, b.Right, y1, _ctrlMeasurePaint);
            canvas.DrawCircle(x1, y1, 5, _ctrlMeasurePaint);

            if (_ctrlPoint2 == -1 || _ctrlPoint2 < b.Start || _ctrlPoint2 > b.End) return;

            float x2 = b.Left + (float)((_ctrlPoint2 - b.Start) / (double)b.Count * b.Width);
            float y2 = (float)_ctrlPoint2Pos.Y;

            y2 = Math.Max(b.Top, Math.Min(b.Bottom, y2));

            canvas.DrawLine(x2, b.Top, x2, b.Bottom, _ctrlMeasurePaint);
            canvas.DrawLine(b.Left, y2, b.Right, y2, _ctrlMeasurePaint);
            canvas.DrawCircle(x2, y2, 5, _ctrlMeasurePaint);

            canvas.DrawLine(x1, y1, x2, y2, _ctrlMeasureDashPaint);

            if (_isCtrlMeasuring) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 2-POINT MEASUREMENT ===");
            sb.AppendLine($"Point 1 Index: {_ctrlPoint1}");
            sb.AppendLine($"Point 2 Index: {_ctrlPoint2}");
            sb.AppendLine($"X Distance: {Math.Abs(_ctrlPoint2 - _ctrlPoint1)} points");

            if (GetXAxisLabel != null)
            {
                string t1 = GetXAxisLabel(_ctrlPoint1);
                string t2 = GetXAxisLabel(_ctrlPoint2);
                if (!string.IsNullOrEmpty(t1) && !string.IsNullOrEmpty(t2))
                {
                    sb.AppendLine($"Time 1: {t1}");
                    sb.AppendLine($"Time 2: {t2}");
                }
            }

            sb.AppendLine("-------------------");

            foreach (var s in _seriesList)
            {
                if (!s.IsVisible || s.Data == null) continue;
                var dataToDraw = (s.IsSmoothed && s.SmoothedData != null) ? s.SmoothedData : s.Data;

                if (_ctrlPoint1 < dataToDraw.Length && _ctrlPoint2 < dataToDraw.Length)
                {
                    double v1 = dataToDraw[_ctrlPoint1];
                    double v2 = dataToDraw[_ctrlPoint2];

                    if (!double.IsNaN(v1) && !double.IsNaN(v2))
                    {
                        sb.AppendLine($"{s.Name}:");
                        sb.AppendLine($"  P1: {v1:F3}");
                        sb.AppendLine($"  P2: {v2:F3}");
                        sb.AppendLine($"  Delta: {(v2 - v1):F3}");
                    }
                }
            }

            float tooltipX = (_ctrlPoint2 > _ctrlPoint1) ? x2 + 15 : x2 - 170;
            float tooltipY = y2;
            DrawTooltip(canvas, sb.ToString(), tooltipX, tooltipY);
        }

    }
}
