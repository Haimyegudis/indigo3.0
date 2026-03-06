using IndiLogs.PluginAPI;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using IndiLogs_3._0.ViewModels.Components;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        // ── UI properties ──

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        private IReadOnlyList<PluginColumnDef>? _currentPluginColumns;
        public IReadOnlyList<PluginColumnDef>? CurrentPluginColumns
        {
            get => _currentPluginColumns;
            set { _currentPluginColumns = value; OnPropertyChanged(); }
        }

        private string? _setupInfo;
        public string? SetupInfo
        {
            get => _setupInfo;
            set { _setupInfo = value; OnPropertyChanged(); }
        }

        private string? _pressConfig;
        public string? PressConfig
        {
            get => _pressConfig;
            set { _pressConfig = value; OnPropertyChanged(); }
        }

        private string? _versionsInfo;
        public string? VersionsInfo
        {
            get => _versionsInfo;
            set { _versionsInfo = value; OnPropertyChanged(); }
        }

        private LogEntry? _selectedLog;
        public LogEntry? SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedLog)); }
        }

        public bool HasSelectedLog => _selectedLog != null;

        // Log Details panel pin/auto-hide state
        private bool _isLogDetailsPinned = true;
        public bool IsLogDetailsPinned
        {
            get => _isLogDetailsPinned;
            set { _isLogDetailsPinned = value; OnPropertyChanged(); }
        }

        // ── Font & appearance ──

        private string _selectedFont = "Segoe UI";
        public string SelectedFont
        {
            get => _selectedFont;
            set { if (_selectedFont != value) { _selectedFont = value; OnPropertyChanged(); UpdateContentFont(_selectedFont); } }
        }

        private bool _isBold;
        public bool IsBold
        {
            get => _isBold;
            set { if (_isBold != value) { _isBold = value; OnPropertyChanged(); UpdateContentFontWeight(value); } }
        }

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                _isDarkMode = value;
                ApplyTheme(value);
                OnPropertyChanged();
                Properties.Settings.Default.IsDarkMode = value;
                Properties.Settings.Default.Save();
            }
        }

        private double _gridFontSize = 12;
        public double GridFontSize
        {
            get => _gridFontSize;
            set { _gridFontSize = value; OnPropertyChanged(); }
        }

        private double _screenshotZoom = 1.0;
        public double ScreenshotZoom
        {
            get => _screenshotZoom;
            set { _screenshotZoom = value; OnPropertyChanged(); }
        }

        private int _contextSeconds = 10;
        public int ContextSeconds
        {
            get => _contextSeconds;
            set { if (_contextSeconds != value) { _contextSeconds = value; OnPropertyChanged(); } }
        }

        private string _selectedTimeUnit = "Seconds";
        public string SelectedTimeUnit
        {
            get => _selectedTimeUnit;
            set { _selectedTimeUnit = value; OnPropertyChanged(); }
        }

        // ── Panel visibility ──

        private bool _isLeftPanelVisible = true;
        public bool IsLeftPanelVisible
        {
            get => _isLeftPanelVisible;
            set { _isLeftPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isRightPanelVisible = true;
        public bool IsRightPanelVisible
        {
            get => _isRightPanelVisible;
            set { _isRightPanelVisible = value; OnPropertyChanged(); }
        }

        private bool _isBottomPanelVisible = true;
        public bool IsBottomPanelVisible
        {
            get => _isBottomPanelVisible;
            set { _isBottomPanelVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };

        // ── Scroll requests ──

        public event Action<LogEntry>? RequestScrollToLog;
        public event Action<LogEntry, bool>? RequestScrollToLogPreservePosition;
        public event Action<LogEntry>? RequestSaveScrollPosition;
        public event Action<string>? RequestScrollToBottom;

        public void ScrollToLog(LogEntry log) => RequestScrollToLog?.Invoke(log);
        public void ScrollToLogPreservePosition(LogEntry log) => RequestScrollToLogPreservePosition?.Invoke(log, true);
        public void SaveScrollPosition(LogEntry log) => RequestSaveScrollPosition?.Invoke(log);
        public void ScrollTabToBottom(string tabName) => RequestScrollToBottom?.Invoke(tabName);

        // ── Filter computed properties (delegated, bindings from XAML) ──

        public bool HasRangeStart => FilterVM?.HasRangeStart ?? false;
        public List<Models.ActiveFilterItem> ActiveFilters => FilterVM?.GetActiveFilters() ?? new List<Models.ActiveFilterItem>();
        public bool HasActiveFilters => ActiveFilters.Count > 0;
    }
}
