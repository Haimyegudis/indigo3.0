using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class VisualTimelineViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TimelineState> States { get; set; } = new ObservableCollection<TimelineState>();
        public ObservableCollection<TimelineMarker> Markers { get; set; } = new ObservableCollection<TimelineMarker>();

        private TimelineState _selectedState;
        public TimelineState SelectedState
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
        public void LoadData(IEnumerable<LogEntry> logs, IEnumerable<EventEntry> events)
        {
            Clear();

            // לוגים כבר ממוינים מ-LoadSessionAsync, לא צריך למיין שוב
            var sortedLogs = logs is IList<LogEntry> list ? list : logs.ToList();
            // אם אין לוגים, עדיין נרצה לראות איוונטים אם יש

            // 1. טיפול בלוגים וסטייטים
            if (sortedLogs.Any())
            {
                TimelineState currentState = null;
                foreach (var log in sortedLogs)
                {
                    // זיהוי שגיאות כלליות
                    if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        Markers.Add(new TimelineMarker
                        {
                            Type = TimelineMarkerType.Error,
                            Time = log.Date,
                            Message = log.Message,
                            Color = Colors.Red,
                            Severity = "Error",
                            OriginalLog = log
                        });
                        if (currentState != null) currentState.ErrorCount++;
                    }

                    // --- לוגיקה חדשה: זיהוי נפילת OPCUA ---
                    if (log.Message.Contains("ERR-OPCUA is not operational. Go to Init state."))
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

                        // מסמנים את הסטייט הנוכחי כנכשל
                        if (currentState != null) currentState.Status = "FAILED";
                    }
                    // ----------------------------------------

                    // זיהוי כישלון סטייט קריטי (קיים)
                    if (log.ThreadName == "Events" && log.Message.Contains("PLC_FAILURE_STATE_CHANGE"))
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

                    // זיהוי מעברי סטייט
                    if (log.ThreadName == "Manager" && log.Message.StartsWith("PlcMngr:") && log.Message.Contains("->"))
                    {
                        var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                        string newStateName = parts[1].Trim();

                        if (currentState != null)
                        {
                            currentState.EndTime = log.Date;
                            // אם הסטייט כבר סומן כ-FAILED (בגלל OPCUA או אירוע אחר), לא דורסים את זה
                            if (currentState.Status != "FAILED")
                                currentState.Status = DetermineStatus(currentState.Name, newStateName, currentState.ErrorCount);
                            States.Add(currentState);
                        }

                        currentState = new TimelineState
                        {
                            Name = newStateName,
                            StartTime = log.Date,
                            Color = GetColorForState(newStateName),
                            Status = "RUNNING"
                        };
                    }

                    if (currentState != null) currentState.RelatedLogs.Add(log);
                }

                if (currentState != null)
                {
                    currentState.EndTime = sortedLogs.Last().Date;
                    if (currentState.Status == "RUNNING")
                        currentState.Status = currentState.ErrorCount > 0 ? "WARNING" : "FINISHED";
                    States.Add(currentState);
                }
            }

            // 2. הוספת Events - ללא סינון זמן! (מציג את כולם)
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

        private Color GetColorForState(string name)
        {
            if (name.Contains("READY")) return Colors.LightGreen;
            if (name.Contains("ERROR") || name.Contains("OFF")) return Colors.IndianRed;
            if (name.Contains("INIT")) return Colors.Gold;
            if (name.Contains("PRINT")) return Colors.CadetBlue;
            return Colors.CornflowerBlue;
        }

        private Color GetEventColor(string severity)
        {
            if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase)) return Colors.OrangeRed;
            if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase)) return Colors.Orange;
            return Colors.Cyan;
        }

        /// <summary>
        /// מתמקד על סטייט ספציפי על ידי זום וסלקט
        /// </summary>
        public void FocusOnState(string stateName)
        {
            if (States == null || !States.Any()) return;

            var targetState = States.FirstOrDefault(s => s.Name == stateName);
            if (targetState != null)
            {
                SelectedState = targetState;

                // חישוב זום ואופסט כדי להתמקד על הסטייט
                if (States.Count > 1)
                {
                    var firstState = States.First();
                    var lastState = States.Last();
                    var totalDuration = (lastState.EndTime - firstState.StartTime).TotalSeconds;
                    var stateDuration = (targetState.EndTime - targetState.StartTime).TotalSeconds;
                    var stateOffset = (targetState.StartTime - firstState.StartTime).TotalSeconds;

                    // זום כך שהסטייט יתפוס כ-50% מהמסך
                    ViewScale = Math.Max(1.0, totalDuration / (stateDuration * 2));

                    // אופסט כדי למרכז את הסטייט
                    ViewOffset = -(stateOffset / totalDuration) * ViewScale * 100;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}