using System.IO;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Location Management

        private void AddLocation()
        {
            var result = ShowLocationDialog("Add Search Location", "", "", "");
            if (result == null) return;

            var loc = new SearchLocation { Name = result.Value.name, Address = result.Value.address, BasePath = result.Value.path };
            _locationService.Add(loc);
            Locations.Add(loc);
        }

        private void EditLocation()
        {
            if (SelectedLocation == null) return;
            var result = ShowLocationDialog("Edit Search Location", SelectedLocation.Name, SelectedLocation.Address, SelectedLocation.BasePath);
            if (result == null) return;

            SelectedLocation.Name = result.Value.name;
            SelectedLocation.Address = result.Value.address;
            SelectedLocation.BasePath = result.Value.path;
            _locationService.Update(SelectedLocation);
        }

        private void RemoveLocation()
        {
            if (SelectedLocation == null) return;
            if (_dialogService.ShowConfirm($"Remove location '{SelectedLocation.Name}'?", "Confirm") == DialogResult.Yes)
            {
                _locationService.Remove(SelectedLocation.Id);
                Locations.Remove(SelectedLocation);
            }
        }

        private async Task TestLocationAsync()
        {
            if (SelectedLocation == null) return;
            StatusMessage = $"Testing connectivity to {SelectedLocation.Name}...";
            var status = await _locationService.TestConnectivityAsync(SelectedLocation);
            StatusMessage = $"{SelectedLocation.Name}: {status}";
        }

        private (string name, string address, string path)? ShowLocationDialog(string title, string name, string address, string path)
        {
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var textSecondary = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");
            var primaryColor = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor");

            var dialog = new Window
            {
                Title = title,
                Width = 480,
                Height = 340,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = bgDark
            };

            var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

            // --- Name field ---
            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Friendly Name",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameHint = new System.Windows.Controls.TextBlock
            {
                Text = "A short label for this location (e.g. \"Simulator 1\")",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameBox = new System.Windows.Controls.TextBox
            {
                Text = name ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };

            // --- Address field ---
            var addrLabel = new System.Windows.Controls.TextBlock
            {
                Text = "IP Address / Hostname",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addrHint = new System.Windows.Controls.TextBlock
            {
                Text = "Machine IP or hostname (e.g. \"192.168.1.10\" or \"localhost\")",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addrBox = new System.Windows.Controls.TextBox
            {
                Text = address ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };

            // --- Path field with Browse ---
            var pathLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Log Folder Path",
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var pathHint = new System.Windows.Controls.TextBlock
            {
                Text = "Folder containing log files (local, mapped drive, or UNC path)",
                Foreground = textSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var pathRow = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var browseBtn = new System.Windows.Controls.Button
            {
                Content = "Browse...",
                Width = 75,
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(6, 0, 0, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(browseBtn, System.Windows.Controls.Dock.Right);
            var pathBox = new System.Windows.Controls.TextBox
            {
                Text = path ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = bgCard,
                Foreground = textPrimary,
                BorderBrush = borderBrush
            };
            browseBtn.Click += (s, e) =>
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Select the folder containing log files";
                    if (!string.IsNullOrWhiteSpace(pathBox.Text) && Directory.Exists(pathBox.Text))
                        fbd.SelectedPath = pathBox.Text;
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        pathBox.Text = fbd.SelectedPath;
                }
            };
            pathRow.Children.Add(browseBtn);
            pathRow.Children.Add(pathBox);

            // --- Buttons ---
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Padding = new Thickness(6, 6, 6, 6),
                Background = primaryColor,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(6, 6, 6, 6),
                Background = bgCard,
                Foreground = textPrimary,
                IsCancel = true
            };

            (string name, string address, string path)? result = null;
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    _dialogService.ShowWarning("Please enter a name.", title);
                    return;
                }
                result = (nameBox.Text.Trim(), addrBox.Text.Trim(), pathBox.Text.Trim());
                dialog.Close();
            };
            cancelBtn.Click += (s, e) => dialog.Close();

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);

            root.Children.Add(nameLabel);
            root.Children.Add(nameHint);
            root.Children.Add(nameBox);
            root.Children.Add(addrLabel);
            root.Children.Add(addrHint);
            root.Children.Add(addrBox);
            root.Children.Add(pathLabel);
            root.Children.Add(pathHint);
            root.Children.Add(pathRow);
            root.Children.Add(btnPanel);

            dialog.Content = root;
            dialog.ShowDialog();
            return result;
        }

        private string? PromptInput(string title, string prompt, string defaultValue)
        {
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");

            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = bgDark
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            var label = new System.Windows.Controls.TextBlock { Text = prompt, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 5) };
            var textBox = new System.Windows.Controls.TextBox { Text = defaultValue ?? "", Padding = new Thickness(6, 4, 6, 4), Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush };
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 5, 0), IsDefault = true };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 70, IsCancel = true };
            string? result = null;
            okBtn.Click += (s, e) => { result = textBox.Text; dialog.Close(); };
            cancelBtn.Click += (s, e) => dialog.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;
            dialog.ShowDialog();
            return result;
        }

        #endregion
    }
}
