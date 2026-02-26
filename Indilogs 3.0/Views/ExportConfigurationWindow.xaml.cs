using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class ExportConfigurationWindow : Window
    {
        public ExportConfigurationWindow()
        {
            InitializeComponent();
            Loaded += ExportConfigurationWindow_Loaded;
        }

        private void ExportConfigurationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Wire up the CloseWindow action for In-Memory transfer
            if (DataContext is ExportConfigurationViewModel vm)
            {
                // For S4-5: collapse AXIS, CHStep, Thread columns — show only IO
                ApplyBinaryAppColumnLayout(vm.IsBinaryApp);

                vm.CloseWindow = () =>
                {
                    // Only set DialogResult if window was opened with ShowDialog()
                    // Check by seeing if we're in modal state
                    try
                    {
                        DialogResult = true;
                    }
                    catch (System.InvalidOperationException)
                    {
                        // Window was opened with Show(), not ShowDialog()
                        // Just close it
                    }
                    Close();
                };
            }
        }

        private void ApplyBinaryAppColumnLayout(bool isBinaryApp)
        {
            var hidden = new GridLength(0);
            var star   = new GridLength(1, GridUnitType.Star);
            AxisColumn.Width    = isBinaryApp ? hidden : star;
            ChStepColumn.Width  = isBinaryApp ? hidden : star;
            ThreadColumn.Width  = isBinaryApp ? hidden : star;
        }
    }
}
