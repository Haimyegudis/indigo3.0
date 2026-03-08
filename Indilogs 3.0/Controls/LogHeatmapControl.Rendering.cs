using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Controls
{
    public partial class LogHeatmapControl
    {
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
    }
}
