using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0.Controls
{
    public partial class SearchPanelControl : UserControl
    {
        public SearchPanelControl()
        {
            InitializeComponent();
        }

        private void SearchTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (SearchTextBox.IsVisible)
            {
                // Use Dispatcher to ensure focus happens after UI is rendered
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
