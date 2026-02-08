using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        public event Action OnAddReferenceLineRequested;
        public event Action<bool> OnTogglePanelRequested;
        public event Action<bool> OnLayoutChanged; // true = grid, false = stack
        public event Action<bool> OnSmoothChanged;
        public event Action<int> OnSmoothWindowChanged;

        private bool _isPlaying = false;
        private bool _isPanelVisible = true;

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

        private void AddRefLineButton_Click(object sender, RoutedEventArgs e)
        {
            OnAddReferenceLineRequested?.Invoke();
        }

        private void GridLayoutToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                OnLayoutChanged?.Invoke(toggle.IsChecked == true);
            }
        }

        private void TogglePanelButton_Click(object sender, RoutedEventArgs e)
        {
            _isPanelVisible = !_isPanelVisible;
            TogglePanelButton.Content = _isPanelVisible ? "◀" : "▶";
            OnTogglePanelRequested?.Invoke(_isPanelVisible);
        }

        private void SmoothCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            OnSmoothChanged?.Invoke(SmoothCheckBox.IsChecked == true);
        }

        private void SmoothWindowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int windowSize = (int)SmoothWindowSlider.Value;
            // Ensure odd window for symmetric smoothing
            if (windowSize % 2 == 0) windowSize++;
            if (SmoothWindowLabel != null)
                SmoothWindowLabel.Text = windowSize.ToString();
            if (SmoothCheckBox.IsChecked == true)
                OnSmoothWindowChanged?.Invoke(windowSize);
        }
    }
}
