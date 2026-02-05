using System;
using System.ComponentModel;
using IndiLogs_3._0.Controls.Charts;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// ViewModel for the Charts tab - provides connection between ChartTabControl and MainViewModel
    /// </summary>
    public class ChartTabViewModel : INotifyPropertyChanged
    {
        private MainViewModel _mainViewModel;
        private ChartTabControl _chartControl;

        public ChartTabViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        /// <summary>
        /// Set the chart control reference for bidirectional communication
        /// </summary>
        public void SetChartControl(ChartTabControl control)
        {
            if (_chartControl != null)
            {
                _chartControl.OnChartTimeClicked -= OnChartTimeClicked;
            }

            _chartControl = control;

            if (_chartControl != null)
            {
                _chartControl.OnChartTimeClicked += OnChartTimeClicked;
            }
        }

        /// <summary>
        /// Check if chart data is loaded
        /// </summary>
        public bool HasData => _chartControl?.HasData ?? false;

        /// <summary>
        /// Sync chart cursor to a log entry time
        /// </summary>
        public void SyncToLogTime(DateTime logTime)
        {
            _chartControl?.SyncToTime(logTime);
        }

        /// <summary>
        /// Called when user clicks on a point in the chart
        /// </summary>
        private void OnChartTimeClicked(DateTime time)
        {
            _mainViewModel?.NavigateToLogTime(time);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
