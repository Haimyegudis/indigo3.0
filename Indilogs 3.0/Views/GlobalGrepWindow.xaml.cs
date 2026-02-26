using System;
using System.Windows;
using System.Windows.Forms;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    public partial class GlobalGrepWindow : Window
    {
        private readonly GlobalGrepViewModel _viewModel;
        private readonly Action<GrepResult> _navigationCallback;
        private readonly Action<System.Collections.Generic.List<(string FilePath, string SessionName)>> _openAllFilesCallback;

        public GlobalGrepWindow(GlobalGrepViewModel viewModel, Action<GrepResult> navigationCallback, Action<System.Collections.Generic.List<(string FilePath, string SessionName)>> openAllFilesCallback = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _navigationCallback = navigationCallback;
            _openAllFilesCallback = openAllFilesCallback;
            DataContext = _viewModel;

            _viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(_viewModel.SelectedResult))
                    JumpButton.IsEnabled = _viewModel.SelectedResult != null;
            };
            JumpButton.IsEnabled = false;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var zipDialog = new OpenFileDialog { Title = "Select ZIP or Cancel for Folder", Filter = "ZIP (*.zip)|*.zip" };
            if (zipDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { _viewModel.ExternalPath = zipDialog.FileName; return; }

            var folderDialog = new FolderBrowserDialog { Description = "Select log folder" };
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) _viewModel.ExternalPath = folderDialog.SelectedPath;
        }

        private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => JumpToSelectedLog();
        private void JumpToLog_Click(object sender, RoutedEventArgs e) => JumpToSelectedLog();
        private void OpenAllFiles_Click(object sender, RoutedEventArgs e) => OpenAllFiles();

        private void JumpToSelectedLog()
        {
            if (_viewModel.SelectedResult == null) return;

            // ????? ?????: ??????? ????? ?? ?? ReferencedLogEntry ??? null (?????? ??????)
            // ?-MainViewModel ???? ?????? ??????? ????? ???????
            try
            {
                _navigationCallback?.Invoke(_viewModel.SelectedResult);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Navigation Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenAllFiles()
        {
            var uniqueFiles = _viewModel.GetUniqueFiles();

            if (uniqueFiles == null || uniqueFiles.Count == 0)
            {
                System.Windows.MessageBox.Show("No files to open.", "Open All Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Pass the full list of (FilePath, SessionName) tuples
                _openAllFilesCallback?.Invoke(uniqueFiles);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}