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
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
            }
        }
    }
}
