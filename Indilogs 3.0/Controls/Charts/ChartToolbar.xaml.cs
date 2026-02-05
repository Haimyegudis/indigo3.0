using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartToolbar : UserControl
    {
        public event Action<string> OnLoadCsvRequested;
        public event Action OnPlayRequested;
        public event Action OnStopRequested;
        public event Action<double> OnSpeedChanged;
        public event Action OnAddChartRequested;
        public event Action OnRemoveChartRequested;
        public event Action<bool> OnShowStatesChanged;
        public event Action OnZoomFitRequested;

        private bool _isPlaying = false;

        public ChartToolbar()
        {
            InitializeComponent();
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                PlayButton.Content = _isPlaying ? "⏸" : "▶";
            }
        }

        private void LoadCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Open Chart Data File"
            };

            if (dialog.ShowDialog() == true)
            {
                OnLoadCsvRequested?.Invoke(dialog.FileName);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            OnPlayRequested?.Invoke();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            OnStopRequested?.Invoke();
        }

        private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedCombo.SelectedItem is ComboBoxItem item)
            {
                string speedText = item.Content.ToString().Replace("x", "");
                if (double.TryParse(speedText, out double speed))
                {
                    OnSpeedChanged?.Invoke(speed);
                }
            }
        }

        private void AddChartButton_Click(object sender, RoutedEventArgs e)
        {
            OnAddChartRequested?.Invoke();
        }

        private void RemoveChartButton_Click(object sender, RoutedEventArgs e)
        {
            OnRemoveChartRequested?.Invoke();
        }

        private void ShowStatesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            OnShowStatesChanged?.Invoke(ShowStatesCheckBox.IsChecked == true);
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            OnZoomFitRequested?.Invoke();
        }
    }
}
