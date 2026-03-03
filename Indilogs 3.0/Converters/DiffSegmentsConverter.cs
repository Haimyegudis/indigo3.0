using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// Converts a LogEntry to DiffSegments by comparing with the corresponding row in the other pane.
    /// FIX: Now enforces Time-Based comparison for all cases to handle shifted logs correctly.
    /// </summary>
    public class DiffSegmentsConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
            {
                return null;
            }

            if (values[0] == DependencyProperty.UnsetValue ||
                values[1] == DependencyProperty.UnsetValue ||
                values[2] == DependencyProperty.UnsetValue)
            {
                return null;
            }

            var logEntry = values[0] as LogEntry;
            var viewModel = values[1] as LogComparisonViewModel;
            var paneIndicator = values[2] as string;
            int diffVersion = values.Length > 3 && values[3] is int ? (int)values[3] : 0;

            if (logEntry == null || viewModel == null || string.IsNullOrEmpty(paneIndicator))
            {
                return null;
            }

            if (!viewModel.ShowDiffs)
            {
                return null;
            }

            ComparisonPaneViewModel sourcePane;
            ComparisonPaneViewModel targetPane;

            if (paneIndicator == "Left")
            {
                sourcePane = viewModel.LeftPane;
                targetPane = viewModel.RightPane;
            }
            else
            {
                sourcePane = viewModel.RightPane;
                targetPane = viewModel.LeftPane;
            }

            int correspondingIndex = targetPane.BinarySearchNearest(logEntry.Date);
            LogEntry? correspondingLog = targetPane.GetLogAtIndex(correspondingIndex);

            if (correspondingLog != null)
            {
                double deltaMs = Math.Abs((logEntry.Date - correspondingLog.Date).TotalMilliseconds);
                if (deltaMs > 2000)
                {
                    return null;
                }
            }

            if (correspondingLog == null)
            {
                return null;
            }

            var diffResult = viewModel.DiffEngine.Compare(logEntry.Message, correspondingLog.Message);

            if (diffResult == null)
            {
                return null;
            }

            if (diffResult.AreEqual)
            {
                return null;
            }

            var segments = paneIndicator == "Left" ? diffResult.LeftSegments : diffResult.RightSegments;

            return segments;
        }

        private static string Truncate(string? s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
