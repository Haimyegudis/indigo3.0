using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SkiaSharp;
using System.Windows.Media;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Models.Charts
{
    public enum AxisType { Left, Right }
    public enum ReferenceLineType { Vertical, Horizontal }
    public enum ChartViewType { Signal, Gantt, Thread }

    /// <summary>
    /// Represents a reference line (horizontal or vertical) on a chart
    /// </summary>
    public class ReferenceLine : INotifyPropertyChanged
    {
        private string _name = "Line";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        private double _value = 0;
        public double Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        private ReferenceLineType _type = ReferenceLineType.Horizontal;
        public ReferenceLineType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(nameof(Type)); }
        }

        private SKColor _color = SKColors.Red;
        public SKColor Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(ColorString)); }
        }

        public string ColorString
        {
            get => _color.ToString();
            set
            {
                if (SKColor.TryParse(value, out SKColor c))
                {
                    Color = c;
                }
                else
                {
                    try
                    {
                        var wpfColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(value);
                        Color = new SKColor(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A);
                    }
                    catch { }
                }
            }
        }

        private float _thickness = 2.0f;
        public float Thickness
        {
            get => _thickness;
            set { _thickness = value; OnPropertyChanged(nameof(Thickness)); }
        }

        private bool _isDashed = true;
        public bool IsDashed
        {
            get => _isDashed;
            set { _isDashed = value; OnPropertyChanged(nameof(IsDashed)); }
        }

        private AxisType _yAxis = AxisType.Left;
        public AxisType YAxis
        {
            get => _yAxis;
            set { _yAxis = value; OnPropertyChanged(nameof(YAxis)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Represents a single data series on a chart
    /// </summary>
    public class SignalSeries : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public double[] Data { get; set; }
        public SKColor Color { get; set; }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        private AxisType _axisType = AxisType.Left;
        public AxisType YAxisType
        {
            get => _axisType;
            set { _axisType = value; OnPropertyChanged(nameof(YAxisType)); OnPropertyChanged(nameof(AxisDisplay)); }
        }

        public string AxisDisplay => YAxisType == AxisType.Left ? "L" : "R";

        private string _currentValueDisplay = "-";
        public string CurrentValueDisplay
        {
            get => _currentValueDisplay;
            set { _currentValueDisplay = value; OnPropertyChanged(nameof(CurrentValueDisplay)); }
        }

        private bool _isSmoothed = false;
        public bool IsSmoothed
        {
            get => _isSmoothed;
            set
            {
                _isSmoothed = value;
                if (_isSmoothed && SmoothedData == null)
                    CalculateSmoothing();
                OnPropertyChanged(nameof(IsSmoothed));
            }
        }

        public double[] SmoothedData { get; set; }

        /// <summary>
        /// Calculate Moving Average smoothing for noise reduction
        /// </summary>
        public void CalculateSmoothing(int windowSize = 10)
        {
            if (Data == null || Data.Length == 0) return;

            SmoothedData = new double[Data.Length];
            int halfWindow = windowSize / 2;

            for (int i = 0; i < Data.Length; i++)
            {
                if (double.IsNaN(Data[i]))
                {
                    SmoothedData[i] = double.NaN;
                    continue;
                }

                double sum = 0;
                int count = 0;

                int start = Math.Max(0, i - halfWindow);
                int end = Math.Min(Data.Length - 1, i + halfWindow);

                for (int j = start; j <= end; j++)
                {
                    if (!double.IsNaN(Data[j]))
                    {
                        sum += Data[j];
                        count++;
                    }
                }

                SmoothedData[i] = count > 0 ? sum / count : double.NaN;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// ViewModel for a single chart panel
    /// </summary>
    public class ChartViewModel : INotifyPropertyChanged
    {
        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        private ChartViewType _viewType = ChartViewType.Signal;
        public ChartViewType ViewType
        {
            get => _viewType;
            set { _viewType = value; OnPropertyChanged(nameof(ViewType)); OnPropertyChanged(nameof(IsSignalView)); OnPropertyChanged(nameof(IsGanttView)); OnPropertyChanged(nameof(IsThreadView)); }
        }

        public bool IsSignalView => ViewType == ChartViewType.Signal;
        public bool IsGanttView => ViewType == ChartViewType.Gantt;
        public bool IsThreadView => ViewType == ChartViewType.Thread;

        public ObservableCollection<SignalSeries> Series { get; set; } = new ObservableCollection<SignalSeries>();
        public ObservableCollection<ReferenceLine> ReferenceLines { get; set; } = new ObservableCollection<ReferenceLine>();
        public List<StateInterval> States { get; set; }
        public List<EventMarker> EventMarkers { get; set; }

        // For GANTT view - state data to display
        public List<StateData> GanttStates { get; set; }

        // For THREAD view - messages to display
        public List<ThreadMessageData> ThreadMessages { get; set; }
        public string ThreadName { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private bool _isDetached;
        public bool IsDetached
        {
            get => _isDetached;
            set { _isDetached = value; OnPropertyChanged(nameof(IsDetached)); OnPropertyChanged(nameof(IsNotDetached)); }
        }

        public bool IsNotDetached => !_isDetached;

        private double _chartHeight = 180;
        public double ChartHeight
        {
            get => _chartHeight;
            set { _chartHeight = value; OnPropertyChanged(nameof(ChartHeight)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Represents a state event for the timeline
    /// </summary>
    public class StateEventItem
    {
        public string Time { get; set; }
        public string StateName { get; set; }
        public int LineIndex { get; set; }
        public SKColor Color { get; set; }
    }

    /// <summary>
    /// Represents a state interval (time period with a specific state)
    /// </summary>
    public struct StateInterval
    {
        public int StartIndex;
        public int EndIndex;
        public int StateId;
        public string StateName;
        public string TooltipText;
    }

    /// <summary>
    /// Workspace model for saving/loading chart configurations
    /// </summary>
    public class WorkspaceModel
    {
        public string SourceCsvPath { get; set; }
        public List<ChartSaveData> Charts { get; set; } = new List<ChartSaveData>();
    }

    /// <summary>
    /// Serializable chart data for saving
    /// </summary>
    public class ChartSaveData
    {
        public string Title { get; set; }
        public List<SeriesSaveData> Series { get; set; } = new List<SeriesSaveData>();
        public List<ReferenceLine> ReferenceLines { get; set; } = new List<ReferenceLine>();
    }

    /// <summary>
    /// Serializable series data for saving
    /// </summary>
    public class SeriesSaveData
    {
        public string Name { get; set; }
        public string ColorHex { get; set; }
        public bool IsVisible { get; set; }
        public AxisType Axis { get; set; }
    }

    /// <summary>
    /// Represents an event marker on a chart (red dot with tooltip)
    /// </summary>
    public class EventMarker
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Signal category for filtering in the signal list
    /// </summary>
    public class SignalCategory
    {
        public string Name { get; set; }
        public string[] Keywords { get; set; }

        public static SignalCategory[] DefaultCategories = new[]
        {
            new SignalCategory { Name = "All Signals", Keywords = new string[0] },
            new SignalCategory { Name = "Axis / Motion", Keywords = new[] { "axis", "motion", "position", "velocity", "speed" } },
            new SignalCategory { Name = "IO / Sensors", Keywords = new[] { "io", "sensor", "input", "output", "digital" } },
            new SignalCategory { Name = "States / Logic", Keywords = new[] { "state", "status", "mode", "flag", "enable" } },
            new SignalCategory { Name = "Temperature", Keywords = new[] { "temp", "temperature", "heat" } },
            new SignalCategory { Name = "Pressure", Keywords = new[] { "pressure", "vacuum", "bar" } },
            new SignalCategory { Name = "Events", Keywords = new[] { "event" } }
        };
    }
}
