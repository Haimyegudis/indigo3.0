using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class FileSelectionWindow : Window
    {
        public List<string> FilesToLoad { get; private set; }

        public FileSelectionWindow(List<(string FilePath, string SessionName)> allFiles, List<string> loadedFiles)
        {
            InitializeComponent();

            var fileInfoList = allFiles.Select(file => new FileInfo
            {
                FilePath = file.FilePath,
                FileName = file.SessionName, // Use SessionName instead of extracting from path
                IsLoaded = loadedFiles.Any(loaded => loaded == file.FilePath && Path.GetFileName(loaded) == file.SessionName),
                StatusText = loadedFiles.Any(loaded => loaded == file.FilePath && Path.GetFileName(loaded) == file.SessionName) ? "Already Loaded" : "Will Load",
                StatusIcon = loadedFiles.Any(loaded => loaded == file.FilePath && Path.GetFileName(loaded) == file.SessionName) ? "✅" : "📄"
            }).OrderBy(f => f.IsLoaded ? 0 : 1)
              .ThenBy(f => f.FileName)
              .ToList();

            FilesGrid.ItemsSource = fileInfoList;

            int loadedCount = fileInfoList.Count(f => f.IsLoaded);
            int toLoadCount = fileInfoList.Count - loadedCount;

            SummaryText.Text = $"Total: {fileInfoList.Count} files  |  " +
                              $"Already Loaded: {loadedCount}  |  " +
                              $"To Load: {toLoadCount}";

            // If all files are already loaded, change button text
            if (toLoadCount == 0)
            {
                LoadButton.Content = "Close";
            }

            FilesToLoad = new List<string>();
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var fileInfos = FilesGrid.ItemsSource as List<FileInfo>;
            if (fileInfos != null)
            {
                // Get unique ZIP/folder paths (not individual files)
                FilesToLoad = fileInfos
                    .Where(f => !f.IsLoaded)
                    .Select(f => f.FilePath)
                    .Distinct()
                    .ToList();
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public class FileInfo
        {
            public string FilePath { get; set; }
            public string FileName { get; set; }
            public bool IsLoaded { get; set; }
            public string StatusText { get; set; }
            public string StatusIcon { get; set; }
        }
    }
}
