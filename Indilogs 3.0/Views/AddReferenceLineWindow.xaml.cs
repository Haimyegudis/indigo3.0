using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndiLogs_3._0.Models.Charts;
using SkiaSharp;

namespace IndiLogs_3._0.Views
{
    public partial class AddReferenceLineWindow : Window
    {
        public ReferenceLine ResultLine { get; private set; }
        public bool IsConfirmed { get; private set; }

        private double _currentCursorValue;
        private int _currentCursorIndex;

        public AddReferenceLineWindow(double currentValue, int currentIndex)
        {
            InitializeComponent();
            _currentCursorValue = currentValue;
            _currentCursorIndex = currentIndex;

            // Set default value based on cursor position
            ValueTextBox.Text = currentValue.ToString("F2");

            ColorCombo.SelectionChanged += ColorCombo_SelectionChanged;
        }

        private void LineType_Changed(object sender, RoutedEventArgs e)
        {
            if (HorizontalRadio.IsChecked == true)
            {
                ValueLabel.Text = "Value:";
                ValueTextBox.Text = _currentCursorValue.ToString("F2");
            }
            else
            {
                ValueLabel.Text = "Index:";
                ValueTextBox.Text = _currentCursorIndex.ToString();
            }
        }

        private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string colorHex)
            {
                try
                {
                    ColorPreview.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                }
                catch { }
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ValueTextBox.Text, out double value))
            {
                MessageBox.Show("Please enter a valid numeric value.", "Invalid Value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string colorHex = "#FFFF00"; // Default yellow
            if (ColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string hex)
            {
                colorHex = hex;
            }

            ResultLine = new ReferenceLine
            {
                Name = string.IsNullOrWhiteSpace(NameTextBox.Text) ? "Line" : NameTextBox.Text,
                Type = HorizontalRadio.IsChecked == true ? ReferenceLineType.Horizontal : ReferenceLineType.Vertical,
                Value = value,
                Color = SKColor.Parse(colorHex),
                Thickness = 1.5f,
                IsDashed = DashedCheckBox.IsChecked == true,
                YAxis = AxisType.Left
            };

            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
