using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class AnnotationWindow : Window
    {
        public string AnnotationText { get; private set; }

        public AnnotationWindow(string existingText = "")
        {
            InitializeComponent();
            AnnotationTextBox.Text = existingText;
            AnnotationTextBox.Focus();
            AnnotationTextBox.SelectAll();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AnnotationText = AnnotationTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
