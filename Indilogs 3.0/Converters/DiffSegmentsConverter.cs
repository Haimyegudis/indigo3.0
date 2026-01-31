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
    /// When both panes show the same filtered data (e.g., same thread), compares by row index.
    /// Otherwise, compares by timestamp.
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
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: values null or < 3 elements");
                return null;
            }

            if (values[0] == DependencyProperty.UnsetValue ||
                values[1] == DependencyProperty.UnsetValue ||
                values[2] == DependencyProperty.UnsetValue)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: UnsetValue in bindings");
                return null;
            }

            var logEntry = values[0] as LogEntry;
            var viewModel = values[1] as LogComparisonViewModel;
            var paneIndicator = values[2] as string;
            int diffVersion = values.Length > 3 && values[3] is int ? (int)values[3] : 0;

            if (logEntry == null || viewModel == null || string.IsNullOrEmpty(paneIndicator))
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: logEntry={logEntry != null}, viewModel={viewModel != null}, pane={paneIndicator}");
                return null;
            }

            if (!viewModel.ShowDiffs)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: ShowDiffs is OFF");
                return null;
            }

            ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] Processing {paneIndicator} pane, DiffVersion={diffVersion}");
            ComparisonDebugLogger.Log("CONVERTER", $"  LogEntry.Date={logEntry.Date:HH:mm:ss.fff}, Message={Truncate(logEntry.Message, 50)}");

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

            ComparisonDebugLogger.Log("CONVERTER", $"  SourcePane: Type={sourcePane.SelectedSourceType}, Filter={sourcePane.SelectedFilter ?? "(none)"}, Count={sourcePane.FilteredLogs.Count}");
            ComparisonDebugLogger.Log("CONVERTER", $"  TargetPane: Type={targetPane.SelectedSourceType}, Filter={targetPane.SelectedFilter ?? "(none)"}, Count={targetPane.FilteredLogs.Count}");

            // Find the index of this log in the source pane
            int sourceIndex = -1;
            for (int i = 0; i < sourcePane.FilteredLogs.Count; i++)
            {
                if (ReferenceEquals(sourcePane.FilteredLogs[i], logEntry))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: Could not find logEntry in source pane");
                return null;
            }

            // Check if both panes have the same source type and filter
            bool sameSource = sourcePane.SelectedSourceType == targetPane.SelectedSourceType &&
                              sourcePane.SelectedFilter == targetPane.SelectedFilter;

            LogEntry correspondingLog;
            int correspondingIndex;

            if (sameSource && sourceIndex < targetPane.FilteredLogs.Count)
            {
                // Same source - compare by row index
                correspondingIndex = sourceIndex;
                correspondingLog = targetPane.GetLogAtIndex(correspondingIndex);
                ComparisonDebugLogger.Log("CONVERTER", $"  SAME SOURCE: Using index match. SourceIdx={sourceIndex}, TargetIdx={correspondingIndex}");
            }
            else
            {
                // Different sources - compare by timestamp
                correspondingIndex = targetPane.BinarySearchNearest(logEntry.Date);
                correspondingLog = targetPane.GetLogAtIndex(correspondingIndex);
                ComparisonDebugLogger.Log("CONVERTER", $"  DIFF SOURCE: Using time match. SourceIdx={sourceIndex}, TargetIdx={correspondingIndex}");
                if (correspondingLog != null)
                {
                    var delta = Math.Abs((logEntry.Date - correspondingLog.Date).TotalMilliseconds);
                    ComparisonDebugLogger.Log("CONVERTER", $"    Time delta: {delta:F0}ms");
                }
            }

            if (correspondingLog == null)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: No corresponding log found");
                return null;
            }

            ComparisonDebugLogger.Log("CONVERTER", $"  Corresponding: Date={correspondingLog.Date:HH:mm:ss.fff}, Message={Truncate(correspondingLog.Message, 50)}");

            // Get diff result
            var diffResult = viewModel.DiffEngine.Compare(logEntry.Message, correspondingLog.Message);

            if (diffResult == null)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: DiffResult is null");
                return null;
            }

            if (diffResult.AreEqual)
            {
                ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] SKIP: Messages are EQUAL (after masking)");
                return null;
            }

            var segments = paneIndicator == "Left" ? diffResult.LeftSegments : diffResult.RightSegments;
            ComparisonDebugLogger.Log("CONVERTER", $"[{_convertCallCount}] RESULT: {segments?.Count ?? 0} diff segments");

            if (segments != null)
            {
                foreach (var seg in segments.Take(5))
                {
                    ComparisonDebugLogger.Log("CONVERTER", $"    Segment: Type={seg.Type}, Text=\"{Truncate(seg.Text, 30)}\"");
                }
                if (segments.Count > 5)
                    ComparisonDebugLogger.Log("CONVERTER", $"    ... and {segments.Count - 5} more segments");
            }

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
