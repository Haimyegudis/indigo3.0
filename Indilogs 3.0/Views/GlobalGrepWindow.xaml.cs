using System;
using System.Windows;
using System.Windows.Forms;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    /// <summary>
    /// Code-behind for GlobalGrepWindow.
    /// Provides global search functionality across all loaded sessions or external files.
    /// </summary>
    public partial class GlobalGrepWindow : Window
    {
        private readonly GlobalGrepViewModel _viewModel;
        private readonly Action<GrepResult> _navigationCallback;

        /// <summary>
        /// Constructor for GlobalGrepWindow
        /// </summary>
        /// <param name="viewModel">The ViewModel containing search logic and data</param>
        /// <param name="navigationCallback">Callback to navigate to a selected result in the main window</param>
        public GlobalGrepWindow(GlobalGrepViewModel viewModel, Action<GrepResult> navigationCallback)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _navigationCallback = navigationCallback;

            DataContext = _viewModel;

            // Update Jump button state when selection changes
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedResult))
                {
                    JumpButton.IsEnabled = _viewModel.SelectedResult != null;
                }
            };

            // Set initial state
            JumpButton.IsEnabled = false;
        }

        /// <summary>
        /// Browse button click handler - opens file/folder picker
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Create an OpenFileDialog for ZIP files
            var zipDialog = new OpenFileDialog
            {
                Title = "Select ZIP Archive or Cancel to Choose Folder",
                Filter = "ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (zipDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.ExternalPath = zipDialog.FileName;
                return;
            }

            // If ZIP dialog was cancelled, show folder browser
            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder containing log files",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.ExternalPath = folderDialog.SelectedPath;
            }
        }

        /// <summary>
        /// Double-click handler for results grid - navigates to the selected log
        /// </summary>
        private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            JumpToSelectedLog();
        }

        /// <summary>
        /// Jump to Log button click handler
        /// </summary>
        private void JumpToLog_Click(object sender, RoutedEventArgs e)
        {
            JumpToSelectedLog();
        }

        /// <summary>
        /// Executes the navigation to the selected log entry
        /// </summary>
        private void JumpToSelectedLog()
        {
            if (_viewModel.SelectedResult == null)
            {
                System.Windows.MessageBox.Show(
                    "Please select a result to navigate to.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Check if this is an in-memory result (has ReferencedLogEntry)
            if (_viewModel.SelectedResult.SessionIndex < 0 && _viewModel.SelectedResult.ReferencedLogEntry == null)
            {
                System.Windows.MessageBox.Show(
                    "Cannot navigate to external file results. This feature only works for loaded sessions.",
                    "Navigation Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Invoke the callback to navigate in the main window
                _navigationCallback?.Invoke(_viewModel.SelectedResult);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to navigate to log: {ex.Message}",
                    "Navigation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
