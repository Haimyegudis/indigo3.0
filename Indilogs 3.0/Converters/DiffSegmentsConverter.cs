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
        private static int _convertCallCount = 0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            _convertCallCount++;

            // Basic validation
            if (values == null || values.Length < 3)
            {
                // ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: values null or < 3 elements");
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

            // ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] Processing {paneIndicator} pane");

            // Determine which pane this is
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

            // Find the index of this log in the source pane (needed mostly for debug/logging)
            // Note: In a high-performance scenario, we might want to skip this linear search if not needed.
            /*
            int sourceIndex = -1;
            for (int i = 0; i < sourcePane.FilteredLogs.Count; i++)
            {
                if (ReferenceEquals(sourcePane.FilteredLogs[i], logEntry))
                {
                    sourceIndex = i;
                    break;
                }
            }
            */

            // --- CRITICAL FIX: ALWAYS USE TIME MATCHING ---
            // Previously, there was logic here that checked 'if (sameSource)'.
            // That logic caused issues when comparing two runs of the same type where row counts differed.
            // We now force BinarySearchNearest based on Timestamp.

            int correspondingIndex = targetPane.BinarySearchNearest(logEntry.Date);
            LogEntry correspondingLog = targetPane.GetLogAtIndex(correspondingIndex);

            // Optional: Sanity check for time difference.
            // If the nearest log is too far away (e.g., > 2 seconds), treat it as "no match" to avoid showing misleading diffs.
            if (correspondingLog != null)
            {
                double deltaMs = Math.Abs((logEntry.Date - correspondingLog.Date).TotalMilliseconds);
                if (deltaMs > 2000) // 2 seconds threshold
                {
                    // Too far apart - likely not the corresponding line.
                    // ComparisonDebugLogger.Log("CONVERTER", $"SKIP: Time delta {deltaMs:F0}ms > 2000ms threshold");
                    return null;
                }
            }

            if (correspondingLog == null)
            {
                return null;
            }

            // Get diff result
            var diffResult = viewModel.DiffEngine.Compare(logEntry.Message, correspondingLog.Message);

            if (diffResult == null)
            {
                return null;
            }

            if (diffResult.AreEqual)
            {
                return null; // Return null to use default coloring (no diff highlight)
            }

            // Return the segments relevant to the current pane (Left or Right)
            var segments = paneIndicator == "Left" ? diffResult.LeftSegments : diffResult.RightSegments;

            return segments;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}