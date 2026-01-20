using System.Windows.Controls;

namespace IndiLogs_3._0.Controls
{
    public partial class PlcFilteredTabControl : UserControl
    {
        public PlcLogsGridControl LogsGrid => FilteredLogsGrid;

        public PlcFilteredTabControl()
        {
            InitializeComponent();
        }
    }
}
