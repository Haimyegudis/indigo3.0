using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public class LogEntry : INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        private bool _isAnnotationExpanded;
        public bool IsAnnotationExpanded
        {
            get => _isAnnotationExpanded;
            set
            {
                _isAnnotationExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AnnotationIcon));
            }
        }
        public string AnnotationContent { get; set; }

        public string Level { get; set; }
        public DateTime Date { get; set; }
        public string ThreadName { get; set; }
        public string Message { get; set; }
        public string Logger { get; set; }
        public string ProcessName { get; set; }
        public string Method { get; set; }

        private bool _isMarked;
        public bool IsMarked
        {
            get => _isMarked;
            set
            {
                if (_isMarked != value)
                {
                    _isMarked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RowBackground));
                }
            }
        }

        private Color? _customColor;
        public Color? CustomColor
        {
            get => _customColor;
            set
            {
                if (_customColor != value)
                {
                    _customColor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RowBackground));
                }
            }
        }

        private bool _hasAnnotation;
        public bool HasAnnotation
        {
            get => _hasAnnotation;
            set
            {
                if (_hasAnnotation != value)
                {
                    _hasAnnotation = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RowBackground));
                    OnPropertyChanged(nameof(AnnotationIcon));
                }
            }
        }

        public string AnnotationIcon
        {
            get
            {
                if (!HasAnnotation) return "";
                return IsAnnotationExpanded ? "📌" : "📎"; // Different icon when expanded
            }
        }
        public Brush RowBackground
        {
            get
            {
                if (IsMarked)
                    return new SolidColorBrush(Color.FromRgb(204, 153, 255));

                if (HasAnnotation)
                    return new SolidColorBrush(Color.FromRgb(255, 255, 200)); // Yellow

                if (CustomColor.HasValue)
                    return new SolidColorBrush(CustomColor.Value);

                return Brushes.Transparent;
            }
        }

        public void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}