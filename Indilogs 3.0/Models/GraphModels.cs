using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    // Simple data point for graph service
    public struct SimpleDataPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public SimpleDataPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    // LiveCharts DateTimePoint
    public struct DateTimePoint
    {
        public DateTime DateTime { get; set; }
        public double Value { get; set; }

        public DateTimePoint(DateTime dateTime, double value)
        {
            DateTime = dateTime;
            Value = value;
        }
    }

    // ייצוג של תוצאת ניתוח (חריגה/פיק)
    public class AnalysisEvent
    {
        public string ComponentName { get; set; }
        public string Type { get; set; }
        public double PeakValue { get; set; }
        public DateTime PeakTime { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationSeconds => (EndTime - StartTime).TotalSeconds;

        public string DisplayText => $"[{ComponentName}] {Type}: {PeakValue:F3} ({DurationSeconds:F3}s)";
        public string ColorCode => Type == "Upper" ? "#FF4444" : "#4444FF";
    }

    // ייצוג קטע זמן של סטייט - FIXED to use OxyPlot OADate format
    public class MachineStateSegment
    {
        public string Name { get; set; }
        public double Start { get; set; }  // OxyPlot OADate format (from DateTimeAxis.ToDouble)
        public double End { get; set; }    // OxyPlot OADate format (from DateTimeAxis.ToDouble)
        public OxyColor Color { get; set; }

        // המרה לזמן לצורך תצוגה ברשימה - using OxyPlot's DateTimeAxis
        public DateTime StartTimeValue => DateTimeAxis.ToDateTime(Start);
        public DateTime EndTimeValue => DateTimeAxis.ToDateTime(End);

        // מחרוזת לתצוגה ברשימה: "HH:mm:ss - HH:mm:ss"
        public string TimeRangeStr => $"{StartTimeValue:HH:mm:ss} - {EndTimeValue:HH:mm:ss}";
        public string DurationStr => $"{(EndTimeValue - StartTimeValue).TotalSeconds:F2}s";
    }

    // מודל לעץ הסיגנלים (היררכי)
    public class GraphNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsLeaf { get; set; }
        public ObservableCollection<GraphNode> Children { get; set; } = new ObservableCollection<GraphNode>();

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(); } }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}