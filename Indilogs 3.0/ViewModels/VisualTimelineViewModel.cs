using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class VisualTimelineViewModel : ViewModelBase
    {
        public ObservableCollection<TimelineState> States { get; set; } = new ObservableCollection<TimelineState>();
        public ObservableCollection<TimelineMarker> Markers { get; set; } = new ObservableCollection<TimelineMarker>();

        private TimelineState? _selectedState;
        public TimelineState? SelectedState
        {
            get => _selectedState;
            set
            {
                _selectedState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedStateDuration));
                OnPropertyChanged(nameof(SelectedStateErrors));
                OnPropertyChanged(nameof(SelectedStateResult));
            }
        }

        public string SelectedStateDuration => _selectedState?.Duration.ToString(@"mm\:ss\.fff") ?? "-";
        public int SelectedStateErrors => _selectedState?.ErrorCount ?? 0;

        private double _viewScale = 1.0;
        public double ViewScale { get => _viewScale; set { _viewScale = value; OnPropertyChanged(); } }

        private double _viewOffset = 0;
        public double ViewOffset { get => _viewOffset; set { _viewOffset = value; OnPropertyChanged(); } }

        public int TotalStates => States.Count;
        public int TotalErrors => Markers.Count(m => m.Type == TimelineMarkerType.Error);
        public int TotalEvents => Markers.Count(m => m.Type == TimelineMarkerType.Event);

        public ICommand ResetZoomCommand { get; }

        public VisualTimelineViewModel()
        {
            ResetZoomCommand = new RelayCommand(o => { ViewScale = 1.0; ViewOffset = 0; });
        }

        public void Clear()
        {
            States.Clear();
            Markers.Clear();
            SelectedState = null;
            ViewScale = 1.0;
            ViewOffset = 0;
            OnPropertyChanged(nameof(TotalStates));
            OnPropertyChanged(nameof(TotalErrors));
            OnPropertyChanged(nameof(TotalEvents));
        }
        public string SelectedStateResult
        {
            get
            {
                if (_selectedState == null) return "-";
                if (_selectedState.Status == "FAILED" || _selectedState.ErrorCount > 0) return "FAILURE";
                return "PASSED";
            }
        }


        public void LoadData(IEnumerable<LogEntry> logs, IEnumerable<EventEntry>? events)
        {
            Clear();

            var sortedLogs = logs is IList<LogEntry> list ? list : logs.ToList();

            // 1. Process logs and build state timeline
            if (sortedLogs.Any())
            {
                TimelineState? currentState = null;
                foreach (var log in sortedLogs)
                {
                    // ── Error detection ──
                    if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        Markers.Add(new TimelineMarker
                        {
                            Type = TimelineMarkerType.Error,
                            Time = log.Date,
                            Message = log.Message ?? "",
                            Color = Colors.Red,
                            Severity = "Error",
                            OriginalLog = log
                        });
                        if (currentState != null) currentState.ErrorCount++;
                    }

                    // OPCUA critical failure
                    if (log.Message != null && log.Message.Contains("ERR-OPCUA is not operational. Go to Init state."))
                    {
                        Markers.Add(new TimelineMarker
                        {
                            Type = TimelineMarkerType.Error,
                            Time = log.Date,
                            Message = "OPCUA CRITICAL FAILURE",
                            Color = Colors.DarkRed,
                            Severity = "Critical",
                            OriginalLog = log
                        });
                        if (currentState != null) currentState.Status = "FAILED";
                    }

                    // PLC_FAILURE_STATE_CHANGE events
                    if (log.ThreadName == "Events" && log.Message != null && log.Message.Contains("PLC_FAILURE_STATE_CHANGE"))
                    {
                        Markers.Add(new TimelineMarker
                        {
                            Type = TimelineMarkerType.Error,
                            Time = log.Date,
                            Message = "CRITICAL FAILURE EVENT",
                            Color = Colors.DarkRed,
                            Severity = "Critical",
                            OriginalLog = log
                        });
                        if (currentState != null) currentState.Status = "FAILED";
                    }

                    // ── S6 state transitions: PlcMngr: OLD -> NEW ──
                    if (StateTransitionHelper.IsS6StateTransition(log) &&
                        StateTransitionHelper.TryParseTransition(log.Message, out _, out string? newStateName))
                    {

                        if (currentState != null)
                        {
                            currentState.EndTime = log.Date;
                            if (currentState.Status != "FAILED")
                                currentState.Status = DetermineStatus(currentState.Name, newStateName!, currentState.ErrorCount);
                            States.Add(currentState);
                        }

                        currentState = new TimelineState
                        {
                            Name = newStateName!,
                            StartTime = log.Date,
                            Color = GetColorForState(newStateName!),
                            Status = "RUNNING"
                        };
                    }
                    // ── S4-5 state transitions: STATE_XXX - Enter/Exit ====== ──
                    else if (log.Message != null && log.Message.Contains("==== STATE"))
                    {
                        var match = AppConstants.S4StateRegex().Match(log.Message);
                        if (match.Success)
                        {
                            string stateName = match.Groups[1].Value.ToUpperInvariant();
                            string direction = match.Groups[2].Value;

                            if (direction.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                            {
                                // Enter → close current state, start new state
                                if (currentState != null)
                                {
                                    currentState.EndTime = log.Date;
                                    if (currentState.Status != "FAILED")
                                        currentState.Status = currentState.ErrorCount > 0 ? "WARNING" : "OK";
                                    States.Add(currentState);
                                }

                                currentState = new TimelineState
                                {
                                    Name = stateName,
                                    StartTime = log.Date,
                                    Color = GetColorForState(stateName),
                                    Status = "RUNNING"
                                };
                            }
                            else if (direction.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                            {
                                // Exit → close current state (next Enter starts the new one)
                                if (currentState != null)
                                {
                                    currentState.EndTime = log.Date;
                                    if (currentState.Status != "FAILED")
                                        currentState.Status = currentState.ErrorCount > 0 ? "WARNING" : "OK";
                                    States.Add(currentState);
                                    currentState = null;
                                }
                            }
                        }
                    }

                    if (currentState != null) currentState.RelatedLogs.Add(log);
                }

                // Close the last state
                if (currentState != null)
                {
                    currentState.EndTime = sortedLogs.Last().Date;
                    if (currentState.Status == "RUNNING")
                        currentState.Status = currentState.ErrorCount > 0 ? "WARNING" : "FINISHED";
                    States.Add(currentState);
                }
            }

            // 2. Add Events (can be null to skip — e.g., for S4-5 where events are not shown)
            if (events != null)
            {
                foreach (var evt in events)
                {
                    Markers.Add(new TimelineMarker
                    {
                        Type = TimelineMarkerType.Event,
                        Time = evt.Time,
                        Message = $"{evt.Name}\n{evt.Description}",
                        Severity = evt.Severity,
                        Color = GetEventColor(evt.Severity),
                        OriginalLog = null
                    });
                }
            }

            OnPropertyChanged(nameof(TotalStates));
            OnPropertyChanged(nameof(TotalErrors));
            OnPropertyChanged(nameof(TotalEvents));
        }

        private string DetermineStatus(string currentName, string nextName, int errors)
        {
            if (currentName == "GET_READY") return nextName == "DYNAMIC_READY" ? "SUCCESS" : "FAILED";
            if (currentName == "MECH_INIT") return nextName == "STANDBY" ? "SUCCESS" : "FAILED";
            return errors > 0 ? "WARNING" : "OK";
        }

        /// <summary>State name → color mapping matching ChartStateConfig for consistency.</summary>
        private static readonly System.Collections.Frozen.FrozenDictionary<string, Color> _stateColorMap = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "INIT",           Color.FromRgb(0xFF, 0xE1, 0x35) },  // Yellow
            { "POWER_DISABLE",  Color.FromRgb(0xFF, 0x6B, 0x6B) },  // Red
            { "OFF",            Color.FromRgb(0x80, 0x80, 0x80) },  // Gray
            { "SERVICE",        Color.FromRgb(0x8B, 0x45, 0x13) },  // Brown
            { "MECH_INIT",      Color.FromRgb(0xFF, 0xA5, 0x00) },  // Orange
            { "STANDBY",        Color.FromRgb(0xFF, 0xFF, 0x00) },  // Bright Yellow
            { "GET_READY",      Color.FromRgb(0xFF, 0xA5, 0x00) },  // Orange
            { "READY",          Color.FromRgb(0x90, 0xEE, 0x90) },  // Light Green
            { "DYNAMIC_READY",  Color.FromRgb(0x32, 0xCD, 0x32) },  // Lime Green
            { "PRE_PRINT",      Color.FromRgb(0x26, 0xC6, 0xDA) },  // Cyan
            { "PRINT",          Color.FromRgb(0x22, 0x8B, 0x22) },  // Forest Green
            { "POST_PRINT",     Color.FromRgb(0x41, 0x69, 0xE1) },  // Royal Blue
            { "PAUSE",          Color.FromRgb(0xFF, 0xA7, 0x26) },  // Orange
            { "RECOVERY",       Color.FromRgb(0xEC, 0x40, 0x7A) },  // Pink
            { "GO_TO_OFF",      Color.FromRgb(0xA0, 0x52, 0x2D) },  // Sienna
            { "GO_TO_STANDBY",  Color.FromRgb(0xDA, 0xA5, 0x20) },  // Goldenrod
            { "GO_TO_SERVICE",  Color.FromRgb(0xCD, 0x85, 0x3F) },  // Peru
            { "SML_OFF",        Color.FromRgb(0xC6, 0x28, 0x28) },  // Dark Red
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private Color GetColorForState(string name)
        {
            // Exact match
            if (_stateColorMap.TryGetValue(name, out var color))
                return color;

            // Partial match fallback (e.g., "DYNAMIC_READY" contains "READY")
            foreach (var kvp in _stateColorMap)
            {
                if (name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kvp.Value;
            }

            return Colors.CornflowerBlue;
        }

        private Color GetEventColor(string severity)
        {
            if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase)) return Colors.OrangeRed;
            if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase)) return Colors.Orange;
            return Colors.Cyan;
        }

        /// <summary>
        /// Focuses on a specific state by zooming and selecting
        /// </summary>
        public void FocusOnState(string stateName)
        {
            if (States == null || !States.Any()) return;

            var targetState = States.FirstOrDefault(s => s.Name == stateName);
            if (targetState != null)
            {
                SelectedState = targetState;

                // Calculate zoom and offset to focus on the state
                if (States.Count > 1)
                {
                    var firstState = States.First();
                    var lastState = States.Last();
                    var totalDuration = (lastState.EndTime - firstState.StartTime).TotalSeconds;
                    var stateDuration = (targetState.EndTime - targetState.StartTime).TotalSeconds;
                    var stateOffset = (targetState.StartTime - firstState.StartTime).TotalSeconds;

                    // Zoom so the state occupies about 50% of the screen
                    ViewScale = Math.Max(1.0, totalDuration / (stateDuration * 2));

                    // Offset to center the state
                    ViewOffset = -(stateOffset / totalDuration) * ViewScale * 100;
                }
            }
        }

    }
}