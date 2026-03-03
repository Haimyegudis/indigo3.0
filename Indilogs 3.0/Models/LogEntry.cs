using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public class LogEntry : INotifyPropertyChanged
    {
        // Pre-created frozen brushes - shared across all instances to avoid GC pressure
        private static readonly Brush CurrentMarkedBrush = CreateFrozenBrush(200, 170, 255); // Light Purple
        private static readonly Brush MarkedBrush = CreateFrozenBrush(144, 238, 144);        // Light Green
        private static readonly Brush AnnotationBrush = CreateFrozenBrush(255, 255, 200);    // Yellow

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // ===== ANNOTATION PROPERTIES =====
        private bool _isAnnotationExpanded;
        public bool IsAnnotationExpanded
        {
            get => _isAnnotationExpanded;
            set
            {
                if (_isAnnotationExpanded != value)
                {
                    _isAnnotationExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AnnotationIcon));
                }
            }
        }
        private string _annotationContent = "";
        public string AnnotationContent
        {
            get => _annotationContent;
            set
            {
                if (_annotationContent != value)
                {
                    _annotationContent = value;
                    OnPropertyChanged();
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

        // ===== BASIC LOG PROPERTIES =====
        public string Level { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime? SyncedTime { get; set; }  // PLC time adjusted to APP clock
        public string ThreadName { get; set; } = "";
        public string Message { get; set; } = "";
        public string Logger { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string Method { get; set; } = "";

        // ===== PARSED FIELDS =====
        public string Pattern { get; set; } = "";
        public string Data { get; set; } = "";
        public string Exception { get; set; } = "";

        // ===== PLUGIN EXTRA FIELDS =====
        /// <summary>
        /// Arbitrary key-value pairs populated by plugin parsers via
        /// <see cref="IndiLogs.PluginAPI.LogEntryDto.ExtraFields"/>.
        /// Bound in the DataGrid as ExtraFields[key] when a plugin provides
        /// custom column definitions via ILogFilePlugin.GetColumns().
        /// </summary>
        public Dictionary<string, string>? ExtraFields { get; set; }

        // ===== MARKING & COLORING =====
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

        private bool _isCurrentMarked;
        public bool IsCurrentMarked
        {
            get => _isCurrentMarked;
            set
            {
                if (_isCurrentMarked != value)
                {
                    _isCurrentMarked = value;
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

        // Factory-default color — set only by ApplyFactoryDefaultColorsAsync / SetSystemDefaultColor.
        // Never overwritten by user actions. Used by the PLC FILTERED tab so it always
        // shows the hardcoded default appearance (e.g. state-transition rows stay light blue)
        // regardless of what the user has customised in the PLC tab.
        private Color? _systemDefaultColor;
        public Color? SystemDefaultColor
        {
            get => _systemDefaultColor;
            set
            {
                if (_systemDefaultColor != value)
                {
                    _systemDefaultColor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FilteredRowBackground));
                    OnPropertyChanged(nameof(HasSystemDefaultColor));
                }
            }
        }

        /// <summary>True when this log line has a factory-default background (e.g. state-transition light blue).</summary>
        public bool HasSystemDefaultColor => SystemDefaultColor.HasValue;

        // Cached brush for SystemDefaultColor
        private Brush? _cachedSystemBrush;
        private Color? _cachedSystemBrushColor;

        /// <summary>
        /// Row background used exclusively by the PLC FILTERED tab.
        /// Returns the factory-default colour when one is assigned, otherwise Transparent.
        /// Ignores user-set custom colours, marks, and annotations.
        /// </summary>
        public Brush FilteredRowBackground
        {
            get
            {
                if (!SystemDefaultColor.HasValue)
                    return Brushes.Transparent;

                if (_cachedSystemBrush == null || _cachedSystemBrushColor != SystemDefaultColor.Value)
                {
                    _cachedSystemBrushColor = SystemDefaultColor.Value;
                    var brush = new SolidColorBrush(SystemDefaultColor.Value);
                    brush.Freeze();
                    _cachedSystemBrush = brush;
                }
                return _cachedSystemBrush;
            }
        }

        // ===== ROW STYLING =====
        private bool _isErrorOrEvents;
        public bool IsErrorOrEvents
        {
            get => _isErrorOrEvents;
            set
            {
                if (_isErrorOrEvents != value)
                {
                    _isErrorOrEvents = value;
                    OnPropertyChanged();
                }
            }
        }

        // Legacy property for compatibility - will be removed
        private Brush? _rowForeground;
        public Brush? RowForeground
        {
            get => _rowForeground;
            set
            {
                if (_rowForeground != value)
                {
                    _rowForeground = value;
                    OnPropertyChanged();
                }
            }
        }

        // Cached brush for CustomColor to avoid creating new instances on every access
        private Brush? _cachedCustomBrush;
        private Color? _cachedCustomBrushColor;

        // ── Level-based row tinting (static frozen brushes — one allocation per process) ──
        private static Brush MakeLevelBrush(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }
        private static readonly Brush _brushLevelError   = MakeLevelBrush(0x14, 0xF4, 0x43, 0x36); // 8% red
        private static readonly Brush _brushLevelWarning = MakeLevelBrush(0x14, 0xFF, 0x98, 0x00); // 8% orange
        private static readonly Brush _brushLevelDebug   = MakeLevelBrush(0x10, 0x21, 0x96, 0xF3); // 6% blue

        public Brush RowBackground
        {
            get
            {
                // Priority: CurrentMarked > Marked > Annotation > CustomColor > Level tint > Transparent
                if (IsCurrentMarked)
                    return CurrentMarkedBrush;

                if (IsMarked)
                    return MarkedBrush;

                if (HasAnnotation)
                    return AnnotationBrush;

                if (CustomColor.HasValue)
                {
                    if (_cachedCustomBrush == null || _cachedCustomBrushColor != CustomColor.Value)
                    {
                        _cachedCustomBrushColor = CustomColor.Value;
                        var brush = new SolidColorBrush(CustomColor.Value);
                        brush.Freeze();
                        _cachedCustomBrush = brush;
                    }
                    return _cachedCustomBrush;
                }

                // Subtle level-based tint — lowest priority, only when no user color applies
                switch (Level?.ToUpperInvariant())
                {
                    case "WARNING":
                    case "WARN":
                        return _brushLevelWarning;
                }

                return Brushes.Transparent;
            }
        }

        // ===== INotifyPropertyChanged IMPLEMENTATION =====
        public void OnPropertyChanged([CallerMemberName] string? name = null)  // ✅ PUBLIC!
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}