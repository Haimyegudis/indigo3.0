using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class FontsWindow : Window
    {
        private string _originalFont;
        private MainViewModel _viewModel;

        public FontsWindow()
        {
            InitializeComponent();

            // Save the original font when the window loads
            this.Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    _viewModel = vm;
                    _originalFont = vm.SelectedFont;
                }
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // User confirmed - keep the selection and close
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // User cancelled - if we have the original font, restore it
            if (_viewModel != null && _originalFont != null)
            {
                _viewModel.SelectedFont = _originalFont;
            }

            DialogResult = false;
            Close();
        }
    }
}