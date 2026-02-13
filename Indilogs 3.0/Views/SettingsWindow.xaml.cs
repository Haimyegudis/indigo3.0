using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _isChildDialogOpen = false;

        public SettingsWindow()
        {
            InitializeComponent();
            Deactivated += SettingsWindow_Deactivated;
        }

        private void SettingsWindow_Deactivated(object sender, System.EventArgs e)
        {
            // Don't close if a child dialog (like Fonts) is open
            if (!_isChildDialogOpen)
                Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            _isChildDialogOpen = true;
            WindowManager.OpenWindow(new HelpWindow());
            Close();
        }

        private void OpenFonts_Click(object sender, RoutedEventArgs e)
        {
            _isChildDialogOpen = true;
            WindowManager.ShowDialog(new FontsWindow { DataContext = this.DataContext });
            _isChildDialogOpen = false;
        }

        private void EditDefaultFilter_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            _isChildDialogOpen = true;

            var win = new FilterWindow();

            // Load current default filter (user's or factory)
            var currentFilter = vm.FilterVM.DefaultPlcFilter ?? DefaultConfigurationService.GetFactoryPlcFilter();
            win.ViewModel.RootNodes.Clear();
            win.ViewModel.RootNodes.Add(currentFilter.DeepClone());

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasFilter = newRoot != null && newRoot.Children.Count > 0;

                // Build or update the defaults
                var svc = vm.DefaultConfigService;
                var config = svc.CurrentDefaults ?? new DefaultConfiguration();

                if (hasFilter)
                {
                    config.PlcFilteredDefaultFilter = newRoot.DeepClone();
                    config.HasCustomPlcFilter = true;
                }
                else
                {
                    config.PlcFilteredDefaultFilter = null;
                    config.HasCustomPlcFilter = false;
                }

                svc.Save(config);
                vm.FilterVM.DefaultPlcFilter = config.PlcFilteredDefaultFilter;
                vm.SessionVM.StatusMessage = "PLC default filter saved.";
            }

            _isChildDialogOpen = false;
        }

        private void EditDefaultColoring_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            _isChildDialogOpen = true;

            var win = new ColoringWindow();
            var svc = vm.DefaultConfigService;
            var defaults = svc.CurrentDefaults;

            // Load current default coloring rules (user's or factory) for Main PLC
            var currentRules = defaults?.MainDefaultColoringRules;
            if (currentRules == null || currentRules.Count == 0)
            {
                currentRules = DefaultConfigurationService.GetFactoryMainColoringRules();
            }
            win.LoadSavedRules(currentRules.Select(r => r.Clone()).ToList());

            if (win.ShowDialog() == true)
            {
                var newRules = win.ResultConditions;
                var config = svc.CurrentDefaults ?? new DefaultConfiguration();

                if (newRules != null && newRules.Count > 0)
                {
                    config.MainDefaultColoringRules = newRules;
                    config.HasCustomMainColoring = true;
                }
                else
                {
                    config.MainDefaultColoringRules = null;
                    config.HasCustomMainColoring = false;
                }

                svc.Save(config);

                // Update live coloring service
                vm.ColoringService.UserDefaultMainRules = config.MainDefaultColoringRules;
                vm.SessionVM.StatusMessage = "Default coloring rules saved.";
            }

            _isChildDialogOpen = false;
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            vm.ResetDefaultsCommand.Execute(null);
        }
    }
}
