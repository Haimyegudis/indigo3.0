using System;
using System.Windows;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class ExportProgressWindow : Window
    {
        public bool IsCancelled { get; private set; }
        private bool _isCompleted = false;

        public ExportProgressWindow()
        {
            InitializeComponent();
            IsCancelled = false;
            _isCompleted = false;

            // Make sure window can be moved and minimized
            this.Topmost = false;
            this.ShowInTaskbar = true;
        }

        public void UpdateProgress(int percentage, string status, string details = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateProgress(percentage, status, details)));
                return;
            }

            ProgressBar.Value = percentage;
            PercentageText.Text = $"{percentage}%";
            StatusText.Text = status;
            DetailsText.Text = details;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompleted)
            {
                this.Close();
                return;
            }

            IsCancelled = true;
            StatusText.Text = "Cancelling...";
            CancelButton.IsEnabled = false;
        }

        public void Complete(bool success, string message = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Complete(success, message)));
                return;
            }

            _isCompleted = true;

            if (success)
            {
                ProgressBar.Value = 100;
                PercentageText.Text = "100%";
                StatusText.Text = "Export Complete!";
                DetailsText.Text = message;
                CancelButton.Content = "Close";
            }
            else
            {
                StatusText.Text = "Export Failed";
                DetailsText.Text = message;
                CancelButton.Content = "Close";
            }

            CancelButton.IsEnabled = true;

            // Auto-close after 2 seconds if successful
            if (success)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    this.Close();
                };
                timer.Start();
            }
        }
    }
}