#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0.Views
{
    /// <summary>
    /// Dialog that shows the contents of a ZIP archive and lets the user pick a file to open.
    /// Returns the selected entry name via <see cref="SelectedEntryName"/>.
    /// If the user clicks "Browse Files…", <see cref="BrowseExternalRequested"/> is set to true.
    /// </summary>
    public partial class ZipBrowserWindow : Window
    {
        private readonly List<string> _allEntries;

        /// <summary>The full entry name (path inside ZIP) of the selected file, or null.</summary>
        public string SelectedEntryName { get; private set; }

        /// <summary>If true, the caller should fall back to an OpenFileDialog.</summary>
        public bool BrowseExternalRequested { get; private set; }

        // File extensions considered relevant for log/config browsing
        private static readonly HashSet<string> RelevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".log", ".file", ".csv", ".txt", ".xml", ".json", ".cfg", ".ini", ".config", ".dat"
        };

        public ZipBrowserWindow(string zipPath)
        {
            InitializeComponent();

            ZipNameLabel.Text = Path.GetFileName(zipPath);
            _allEntries = new List<string>();

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // Skip directories (entries with empty Name or trailing slash)
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        string ext = Path.GetExtension(entry.Name);

                        // Show inner ZIP files as well (for reference)
                        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            _allEntries.Add(entry.FullName + " [nested ZIP]");
                            continue;
                        }

                        if (RelevantExtensions.Contains(ext) || string.IsNullOrEmpty(ext))
                        {
                            _allEntries.Add(entry.FullName);
                        }
                    }
                }

                _allEntries.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read ZIP:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ApplyFilter(string.Empty);
        }

        private void ApplyFilter(string filter)
        {
            IEnumerable<string> filtered = _allEntries;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                filtered = _allEntries.Where(e =>
                    e.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var list = filtered.ToList();
            FileList.ItemsSource = list;
            CountLabel.Text = $"{list.Count} / {_allEntries.Count} files";

            if (list.Count > 0)
                FileList.SelectedIndex = 0;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(SearchBox.Text);
        }

        private void FileList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AcceptSelection();
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void BrowseExternalButton_Click(object sender, RoutedEventArgs e)
        {
            BrowseExternalRequested = true;
            DialogResult = false;
            Close();
        }

        private void AcceptSelection()
        {
            if (FileList.SelectedItem is string selected)
            {
                SelectedEntryName = selected;
                DialogResult = true;
                Close();
            }
        }
    }
}
