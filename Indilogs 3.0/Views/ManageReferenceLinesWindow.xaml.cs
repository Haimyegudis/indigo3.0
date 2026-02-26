using System.Collections.ObjectModel;
using System.Windows;
using IndiLogs_3._0.Models.Charts;

namespace IndiLogs_3._0.Views
{
    public partial class ManageReferenceLinesWindow : Window
    {
        private ObservableCollection<ReferenceLine> _referenceLines;
        private double _currentCursorValue;
        private int _currentCursorIndex;

        public ManageReferenceLinesWindow(
            ObservableCollection<ReferenceLine> referenceLines,
            double currentValue,
            int currentIndex)
        {
            InitializeComponent();
            _referenceLines = referenceLines;
            _currentCursorValue = currentValue;
            _currentCursorIndex = currentIndex;

            LinesGrid.ItemsSource = _referenceLines;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddReferenceLineWindow(_currentCursorValue, _currentCursorIndex);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultLine != null)
            {
                _referenceLines.Add(dialog.ResultLine);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is ReferenceLine line)
            {
                _referenceLines.Remove(line);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
