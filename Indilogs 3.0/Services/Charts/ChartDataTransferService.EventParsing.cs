using System;
using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Services.Charts
{
    public partial class ChartDataTransferService
    {
        private List<ThreadMessageData> ParseThreadMessages(
            List<LogEntry> logs,
            List<string> selectedThreads,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var messages = new List<ThreadMessageData>();
            var selectedSet = new HashSet<string>(selectedThreads, StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.ThreadName)) continue;
                if (!selectedSet.Contains(log.ThreadName)) continue;

                if (timeIndexLookup.TryGetValue(log.Date, out int idx))
                {
                    messages.Add(new ThreadMessageData
                    {
                        TimeIndex = idx,
                        ThreadName = log.ThreadName,
                        Message = log.Message ?? "",
                        TimeStamp = log.Date
                    });
                }
            }

            return messages;
        }

        /// <summary>
        /// Parse Events from logs (ThreadName = "Events")
        /// Format: "Enqueue event EVENT_NAME from SUBSYSTEM ParamName=Value [Severity]"
        /// </summary>
        private List<EventMarkerData> ParseEvents(
            List<LogEntry> logs,
            Dictionary<DateTime, int> timeIndexLookup)
        {
            var events = new List<EventMarkerData>();

            foreach (var log in logs)
            {
                // Events are identified by ThreadName = "Events"
                if (!string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(log.Message)) continue;

                if (!timeIndexLookup.TryGetValue(log.Date, out int idx))
                    continue;

                // Parse event message: "Enqueue event EVENT_NAME from SUBSYSTEM ..."
                string eventName = "";
                string severity = "";
                string state = "";
                string description = "";
                string parameters = "";

                try
                {
                    string msg = log.Message;

                    // Try to extract event name
                    int eventStart = msg.IndexOf("event ", StringComparison.OrdinalIgnoreCase);
                    if (eventStart >= 0)
                    {
                        eventStart += 6; // "event ".Length
                        int eventEnd = msg.IndexOf(' ', eventStart);
                        if (eventEnd > eventStart)
                            eventName = msg.Substring(eventStart, eventEnd - eventStart);
                        else
                            eventName = msg.Substring(eventStart);
                    }
                    else
                    {
                        eventName = msg.Length > 50 ? msg.Substring(0, 50) + "..." : msg;
                    }

                    // Try to extract "from SUBSYSTEM"
                    int fromIdx = msg.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                    if (fromIdx > 0)
                    {
                        int subStart = fromIdx + 6;
                        int subEnd = msg.IndexOf(' ', subStart);
                        if (subEnd > subStart)
                            description = msg.Substring(subStart, subEnd - subStart);
                        else
                            description = msg.Substring(subStart);
                    }

                    // Try to extract severity [...]
                    int bracketStart = msg.LastIndexOf('[');
                    int bracketEnd = msg.LastIndexOf(']');
                    if (bracketStart > 0 && bracketEnd > bracketStart)
                    {
                        severity = msg.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                    }

                    // Extract state from event name if it contains STATE keywords
                    if (eventName.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        state = eventName;
                    }

                    // Everything after "from X" is parameters
                    if (fromIdx > 0)
                    {
                        int paramsStart = msg.IndexOf(' ', fromIdx + 6);
                        if (paramsStart > 0 && paramsStart < msg.Length - 1)
                        {
                            parameters = msg.Substring(paramsStart + 1).Trim();
                            // Remove the [Severity] part
                            if (bracketStart > paramsStart)
                                parameters = parameters.Substring(0, bracketStart - paramsStart - 1).Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ParseEvents message parsing failed", ex);
                }

                events.Add(new EventMarkerData
                {
                    TimeIndex = idx,
                    TimeStamp = log.Date,
                    Name = eventName,
                    State = state,
                    Severity = severity,
                    Description = description,
                    Parameters = parameters
                });
            }

            return events;
        }

        private void ForwardFillNaN(double[] data)
        {
            double lastValue = double.NaN;
            for (int i = 0; i < data.Length; i++)
            {
                if (double.IsNaN(data[i]))
                {
                    data[i] = lastValue;
                }
                else
                {
                    lastValue = data[i];
                }
            }
        }

        private void MergeStateIntervals(List<StateInterval> intervals)
        {
            if (intervals.Count < 2) return;

            intervals.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));

            var merged = new List<StateInterval>();
            var current = intervals[0];

            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].StateId == current.StateId &&
                    intervals[i].StartIndex <= current.EndIndex + 1)
                {
                    // Merge consecutive same-state intervals
                    current.EndIndex = Math.Max(current.EndIndex, intervals[i].EndIndex);
                    // Preserve tooltip from whichever interval has it
                    if (string.IsNullOrEmpty(current.TooltipText) && !string.IsNullOrEmpty(intervals[i].TooltipText))
                    {
                        current.TooltipText = intervals[i].TooltipText;
                        current.StateName = intervals[i].StateName;
                    }
                }
                else
                {
                    // Different state - fill gap and start new interval
                    current.EndIndex = intervals[i].StartIndex - 1;
                    merged.Add(current);
                    current = intervals[i];
                }
            }
            merged.Add(current);

            intervals.Clear();
            intervals.AddRange(merged);
        }
    }
}
