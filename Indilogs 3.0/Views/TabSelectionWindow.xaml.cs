using IndiLogs_3._0.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class TabSelectionWindow : Window
    {
        /// <summary>
        /// The resulting configuration. Null if the user cancelled.
        /// </summary>
        public TabSelectionConfig ResultConfig { get; private set; }

        private readonly TabSelectionConfig _preScanConfig;

        // Time segment values (scroll-adjustable)
        private int _fromHour, _fromMin, _fromSec;
        private int _toHour, _toMin, _toSec;

        public TabSelectionWindow(TabSelectionConfig preScanConfig)
        {
            InitializeComponent();
            _preScanConfig = preScanConfig;

            // Config type badge
            ConfigTypeText.Text = preScanConfig.IsS6 ? "S6" : "S4-5";

            // Show/enable only checkboxes for components that exist in the ZIP
            ChkPlc.Visibility = preScanConfig.HasPlc ? Visibility.Visible : Visibility.Collapsed;
            ChkApp.Visibility = preScanConfig.HasApp ? Visibility.Visible : Visibility.Collapsed;
            ChkEvents.Visibility = preScanConfig.HasEvents ? Visibility.Visible : Visibility.Collapsed;
            ChkScreenshots.Visibility = preScanConfig.HasScreenshots ? Visibility.Visible : Visibility.Collapsed;
            ChkTerminalLogs.Visibility = preScanConfig.HasTerminalLogs ? Visibility.Visible : Visibility.Collapsed;
            ChkLrs.Visibility = preScanConfig.HasLrs ? Visibility.Visible : Visibility.Collapsed;
            ChkConfiguration.Visibility = preScanConfig.HasConfiguration ? Visibility.Visible : Visibility.Collapsed;
            ChkSetupInfo.Visibility = preScanConfig.HasSetupInfo ? Visibility.Visible : Visibility.Collapsed;
            ChkSystab.Visibility = preScanConfig.HasSystab ? Visibility.Visible : Visibility.Collapsed;
            ChkGlobals.Visibility = preScanConfig.HasGlobals ? Visibility.Visible : Visibility.Collapsed;
            ChkManagerThread.Visibility = (preScanConfig.IsS6 && preScanConfig.HasManagerThread)
                ? Visibility.Visible : Visibility.Collapsed;

            // Step Recorder is S4-5 only
            if (preScanConfig.IsS6)
            {
                ChkStepRecorder.Visibility = Visibility.Collapsed;
                ChkStepRecorder.IsChecked = false;
            }

            // Uncheck hidden checkboxes so their default true doesn't leak into tab visibility
            // (e.g., hidden LRS checkbox would keep TERMINALS tab visible even when user unchecks Terminal Logs)
            if (!preScanConfig.HasPlc) ChkPlc.IsChecked = false;
            if (!preScanConfig.HasApp) ChkApp.IsChecked = false;
            if (!preScanConfig.HasEvents) ChkEvents.IsChecked = false;
            if (!preScanConfig.HasScreenshots) ChkScreenshots.IsChecked = false;
            if (!preScanConfig.HasTerminalLogs) ChkTerminalLogs.IsChecked = false;
            if (!preScanConfig.HasLrs) ChkLrs.IsChecked = false;
            if (!preScanConfig.HasConfiguration) ChkConfiguration.IsChecked = false;
            if (!preScanConfig.HasSetupInfo) ChkSetupInfo.IsChecked = false;
            if (!preScanConfig.HasSystab) ChkSystab.IsChecked = false;
            if (!preScanConfig.HasGlobals) ChkGlobals.IsChecked = false;
            if (!(preScanConfig.IsS6 && preScanConfig.HasManagerThread)) ChkManagerThread.IsChecked = false;

            // Time range section
            if (preScanConfig.AvailableStartTime.HasValue && preScanConfig.AvailableEndTime.HasValue)
            {
                var start = preScanConfig.AvailableStartTime.Value;
                var end = preScanConfig.AvailableEndTime.Value;
                var duration = end - start;

                TimeRangeSection.Visibility = Visibility.Visible;
                TxtLogPeriod.Text = $"Log period: {start:HH:mm:ss} \u2014 {end:HH:mm:ss}";

                if (duration.TotalHours >= 1)
                    TxtDuration.Text = $"Duration: {(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
                else if (duration.TotalMinutes >= 1)
                    TxtDuration.Text = $"Duration: {(int)duration.TotalMinutes}m {duration.Seconds}s";
                else
                    TxtDuration.Text = $"Duration: {(int)duration.TotalSeconds}s";

                _fromHour = start.Hour; _fromMin = start.Minute; _fromSec = start.Second;
                _toHour = end.Hour; _toMin = end.Minute; _toSec = end.Second;
                UpdateTimeDisplay();
            }

            ResultConfig = null;
        }

        // ─── Window dragging ───
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch (InvalidOperationException) { /* DragMove can throw if mouse is released during drag */ }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ─── Time filter toggle ───
        private void ChkTimeFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (TimeSegmentsPanel == null) return;
            bool enabled = ChkTimeFilter.IsChecked == true;
            TimeSegmentsPanel.Opacity = enabled ? 1.0 : 0.35;
            TimeSegmentsPanel.IsHitTestVisible = enabled;
        }

        // ─── Time segment mouse scroll ───
        private void TimeSegment_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ChkTimeFilter.IsChecked != true) return;
            var border = sender as Border;
            if (border?.Tag == null) return;

            int delta = e.Delta > 0 ? 1 : -1;
            string tag = border.Tag.ToString();

            switch (tag)
            {
                case "FH": _fromHour = (_fromHour + delta + 24) % 24; break;
                case "FM": _fromMin = (_fromMin + delta + 60) % 60; break;
                case "FS": _fromSec = (_fromSec + delta + 60) % 60; break;
                case "TH": _toHour = (_toHour + delta + 24) % 24; break;
                case "TM": _toMin = (_toMin + delta + 60) % 60; break;
                case "TS": _toSec = (_toSec + delta + 60) % 60; break;
            }

            UpdateTimeDisplay();
            e.Handled = true;
        }

        private void UpdateTimeDisplay()
        {
            TxtFromHour.Text = _fromHour.ToString("D2");
            TxtFromMin.Text = _fromMin.ToString("D2");
            TxtFromSec.Text = _fromSec.ToString("D2");
            TxtToHour.Text = _toHour.ToString("D2");
            TxtToMin.Text = _toMin.ToString("D2");
            TxtToSec.Text = _toSec.ToString("D2");
        }

        // ─── Time segment hover glow ───
        private void TimeSegment_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b && ChkTimeFilter.IsChecked == true)
                b.BorderBrush = (Brush)FindResource("PrimaryColor");
        }

        private void TimeSegment_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.BorderBrush = Brushes.Transparent;
        }

        // ─── Select All / Deselect All ───
        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllCheckboxes(true);
        private void DeselectAll_Click(object sender, RoutedEventArgs e) => SetAllCheckboxes(false);

        private void SetAllCheckboxes(bool isChecked)
        {
            foreach (var child in CheckboxPanel.Children)
            {
                if (child is CheckBox cb && cb.Visibility == Visibility.Visible)
                    cb.IsChecked = isChecked;
            }
        }

        // ─── OK / Cancel ───
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ResultConfig = new TabSelectionConfig
            {
                // Data components
                LoadPlc = ChkPlc.IsChecked == true,
                LoadApp = ChkApp.IsChecked == true,
                LoadEvents = ChkEvents.IsChecked == true,
                LoadScreenshots = ChkScreenshots.IsChecked == true,
                LoadTerminalLogs = ChkTerminalLogs.IsChecked == true,
                LoadLrs = ChkLrs.IsChecked == true,
                LoadConfiguration = ChkConfiguration.IsChecked == true,
                LoadSetupInfo = ChkSetupInfo.IsChecked == true,
                LoadSystab = ChkSystab.IsChecked == true,
                LoadGlobals = ChkGlobals.IsChecked == true,
                LoadManagerThread = ChkManagerThread.IsChecked == true,

                // Tool tabs (visibility only)
                ShowCharts = ChkCharts.IsChecked == true,
                ShowCpr = ChkCpr.IsChecked == true,
                ShowStepRecorder = ChkStepRecorder.IsChecked == true,
                ShowDifferentLogs = ChkDifferentLogs.IsChecked == true
            };

            // Time filter
            if (ChkTimeFilter.IsChecked == true &&
                _preScanConfig.AvailableStartTime.HasValue &&
                _preScanConfig.AvailableEndTime.HasValue)
            {
                string fromStr = $"{_fromHour:D2}:{_fromMin:D2}:{_fromSec:D2}";
                string toStr = $"{_toHour:D2}:{_toMin:D2}:{_toSec:D2}";

                var filterStart = BuildFilterDateTime(fromStr, _preScanConfig.AvailableStartTime.Value);
                var filterEnd = BuildFilterDateTime(toStr, _preScanConfig.AvailableEndTime.Value);

                if (!filterStart.HasValue || !filterEnd.HasValue)
                {
                    MessageBox.Show("Invalid time format. Please use HH:mm:ss (e.g. 14:30:00).",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Handle cross-midnight: if end time-of-day < start time-of-day, assume end is next day
                if (filterEnd.Value < filterStart.Value)
                    filterEnd = filterEnd.Value.AddDays(1);

                ResultConfig.UseTimeFilter = true;
                ResultConfig.FilterStartTime = filterStart.Value;
                ResultConfig.FilterEndTime = filterEnd.Value;
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Parses a HH:mm:ss string and combines it with the date from a reference DateTime.
        /// </summary>
        private static DateTime? BuildFilterDateTime(string timeText, DateTime reference)
        {
            if (TimeSpan.TryParse(timeText, out var ts))
            {
                if (ts.TotalHours >= 24) return null;
                return reference.Date + ts;
            }
            return null;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

    }
}
