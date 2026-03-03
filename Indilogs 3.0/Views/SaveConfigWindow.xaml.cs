using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SaveConfigWindow : Window
    {
        public string ConfigName { get; private set; }
        private readonly HashSet<string> _existingNames;

        // This is the constructor that MainViewModel looks for (receives a list of names)
        public SaveConfigWindow(IEnumerable<string> existingNames = null)
        {
            InitializeComponent();
            NameTextBox.Focus();

            // Create HashSet for fast lookup (Case Insensitive)
            _existingNames = existingNames != null
                ? new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
        }

        // Default constructor (in case XAML requires it, even though in code we use the other one)
        public SaveConfigWindow() : this(null) { }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a configuration name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Duplicate check - here we use the list we received
            if (_existingNames.Contains(name))
            {
                MessageBox.Show($"A configuration named '{name}' already exists.\nPlease choose a different name.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Don't close the window so the user can correct the name
            }

            ConfigName = name;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}