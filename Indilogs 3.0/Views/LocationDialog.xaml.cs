using System.IO;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class LocationDialog : Window
    {
        public string LocationName => NameBox.Text.Trim();
        public string Address => AddressBox.Text.Trim();
        public string LocationPath => PathBox.Text.Trim();

        public LocationDialog(string title, string name, string address, string path)
        {
            InitializeComponent();
            Title = title;
            NameBox.Text = name ?? "";
            AddressBox.Text = address ?? "";
            PathBox.Text = path ?? "";
            NameBox.Focus();
        }

        private void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            using var fbd = new System.Windows.Forms.FolderBrowserDialog();
            fbd.Description = "Select the folder containing log files";
            if (!string.IsNullOrWhiteSpace(PathBox.Text) && Directory.Exists(PathBox.Text))
                fbd.SelectedPath = PathBox.Text;
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                PathBox.Text = fbd.SelectedPath;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
