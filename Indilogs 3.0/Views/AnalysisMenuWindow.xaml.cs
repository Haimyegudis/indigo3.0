using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class AnalysisMenuWindow : Window
    {
        public enum AnalysisChoice
        {
            None,
            Failures,
            Statistics
        }

        public AnalysisChoice SelectedChoice { get; private set; } = AnalysisChoice.None;

        public AnalysisMenuWindow()
        {
            InitializeComponent();
        }

        private void Failures_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = AnalysisChoice.Failures;
            DialogResult = true;
            Close();
        }

        private void Stats_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = AnalysisChoice.Statistics;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = AnalysisChoice.None;
            DialogResult = false;
            Close();
        }
    }
}
