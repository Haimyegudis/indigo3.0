using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class TimeRangeWindow : Window, INotifyPropertyChanged
    {
        private FrameworkElement _anchorElement;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        private string _startTimeText = "00:00:00";
        public string StartTimeText
        {
            get => _startTimeText;
            set { _startTimeText = value; OnPropertyChanged(); }
        }

        private string _endTimeText = "23:59:59";
        public string EndTimeText
        {
            get => _endTimeText;
            set { _endTimeText = value; OnPropertyChanged(); }
        }

        private DateTime _logStartTime;
        public DateTime LogStartTime
        {
            get => _logStartTime;
            set { _logStartTime = value; OnPropertyChanged(); }
        }

        private DateTime _logEndTime;
        public DateTime LogEndTime
        {
            get => _logEndTime;
            set { _logEndTime = value; OnPropertyChanged(); }
        }

        private string _totalDuration;
        public string TotalDuration
        {
            get => _totalDuration;
            set { _totalDuration = value; OnPropertyChanged(); }
        }

        public DateTime? ResultStartDateTime { get; private set; }
        public DateTime? ResultEndDateTime { get; private set; }
        public bool ShouldClear { get; private set; }

        public TimeRangeWindow(DateTime logStart, DateTime logEnd, DateTime? currentFilterStart = null, DateTime? currentFilterEnd = null)
        {
            InitializeComponent();
            DataContext = this;
            this.Loaded += TimeRangeWindow_Loaded;

            LogStartTime = logStart;
            LogEndTime = logEnd;

            var duration = logEnd - logStart;
            if (duration.TotalDays >= 1)
                TotalDuration = $"{(int)duration.TotalDays} days, {duration.Hours} hours, {duration.Minutes} minutes";
            else if (duration.TotalHours >= 1)
                TotalDuration = $"{(int)duration.TotalHours} hours, {duration.Minutes} minutes";
            else
                TotalDuration = $"{duration.Minutes} minutes, {duration.Seconds} seconds";

            // If there's an existing filter, use it as the default values
            // Otherwise, default to full log range
            if (currentFilterStart.HasValue && currentFilterEnd.HasValue)
            {
                StartDate = currentFilterStart.Value.Date;
                EndDate = currentFilterEnd.Value.Date;
                StartTimeText = currentFilterStart.Value.ToString("HH:mm:ss");
                EndTimeText = currentFilterEnd.Value.ToString("HH:mm:ss");
            }
            else
            {
                StartDate = logStart.Date;
                EndDate = logEnd.Date;
                StartTimeText = logStart.ToString("HH:mm:ss");
                EndTimeText = logEnd.ToString("HH:mm:ss");
            }
        }

        private void TimeRangeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position after loaded
            if (_anchorElement != null)
            {
                PositionWindowNearElement();
            }
        }

        /// <summary>
        /// Position the window near the specified button/element
        /// </summary>
        public void PositionNearElement(FrameworkElement element)
        {
            _anchorElement = element;
        }

        private void PositionWindowNearElement()
        {
            if (_anchorElement == null) return;

            try
            {
                // Find the main window
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null) return;

                // Get the position of the anchor element relative to the main window
                var transform = _anchorElement.TransformToVisual(mainWindow);
                var positionInMainWindow = transform.Transform(new Point(0, _anchorElement.ActualHeight));

                // Convert to screen coordinates
                var mainWindowPosition = mainWindow.PointToScreen(new Point(0, 0));

                double screenX = mainWindowPosition.X + positionInMainWindow.X;
                double screenY = mainWindowPosition.Y + positionInMainWindow.Y;

                // Set the window position
                this.Left = screenX;
                this.Top = screenY;

                // Make sure window doesn't go off screen
                var screen = System.Windows.SystemParameters.WorkArea;
                if (this.Left + this.ActualWidth > screen.Right)
                    this.Left = screen.Right - this.ActualWidth;
                if (this.Top + this.ActualHeight > screen.Bottom)
                    this.Top = screenY - _anchorElement.ActualHeight - this.ActualHeight; // Show above
                if (this.Left < screen.Left)
                    this.Left = screen.Left;
                if (this.Top < screen.Top)
                    this.Top = screen.Top;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TIME RANGE WINDOW] Position failed: {ex.Message}");
                // Fallback - center on owner
                if (this.Owner != null)
                {
                    this.Left = this.Owner.Left + (this.Owner.Width - this.Width) / 2;
                    this.Top = this.Owner.Top + (this.Owner.Height - this.Height) / 2;
                }
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndBuildDateTime())
                return;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldClear = true;
            DialogResult = true;
            Close();
        }

        private bool ValidateAndBuildDateTime()
        {
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                MessageBox.Show("Please select both start and end dates.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            TimeSpan startTime, endTime;
            if (!TimeSpan.TryParse(StartTimeText, out startTime))
            {
                MessageBox.Show("Invalid start time format. Please use HH:mm:ss format.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TimeSpan.TryParse(EndTimeText, out endTime))
            {
                MessageBox.Show("Invalid end time format. Please use HH:mm:ss format.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            ResultStartDateTime = StartDate.Value.Date.Add(startTime);
            ResultEndDateTime = EndDate.Value.Date.Add(endTime);

            if (ResultStartDateTime >= ResultEndDateTime)
            {
                MessageBox.Show("Start time must be before end time.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
