using System.Windows.Controls;

namespace IndiLogs_3._0.Controls
{
    public partial class PlcLogsTabControl : UserControl
    {
        public PlcLogsGridControl LogsGrid => MainLogsGrid;

        public PlcLogsTabControl()
        {
            InitializeComponent();
        }
    }
}
