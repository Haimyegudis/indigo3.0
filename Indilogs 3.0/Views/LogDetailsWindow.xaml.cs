using IndiLogs_3._0.Models;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class LogDetailsWindow : Window
    {
        private readonly LogEntry _log;

        public LogDetailsWindow(LogEntry log)
        {
            InitializeComponent();
            _log = log;

            if (log != null)
            {
                // Header info
                TimeText.Text = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff");
                LevelText.Text = log.Level ?? "N/A";
                SetLevelColor(log.Level);

                // Tab contents
                MessageText.Text = log.Message ?? "(empty)";
                ThreadText.Text = log.ThreadName ?? "(empty)";
                PatternText.Text = log.Pattern ?? "(empty)";
                LoggerText.Text = log.Logger ?? "(empty)";
                ExceptionText.Text = log.Exception ?? "(empty)";
                DataText.Text = FormatData(log.Data);
                MethodText.Text = log.Method ?? "(empty)";
            }

            // ESC to close
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        }

        private void SetLevelColor(string level)
        {
            if (string.IsNullOrEmpty(level))
            {
                LevelBorder.Background = Brushes.Gray;
                LevelText.Foreground = Brushes.White;
                return;
            }

            switch (level.ToUpperInvariant())
            {
                case "ERROR":
                case "FATAL":
                    LevelBorder.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                    LevelText.Foreground = Brushes.White;
                    break;
                case "WARN":
                case "WARNING":
                    LevelBorder.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                    LevelText.Foreground = Brushes.Black;
                    break;
                case "INFO":
                    LevelBorder.Background = new SolidColorBrush(Color.FromRgb(23, 162, 184)); // Cyan
                    LevelText.Foreground = Brushes.White;
                    break;
                case "DEBUG":
                    LevelBorder.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray
                    LevelText.Foreground = Brushes.White;
                    break;
                default:
                    LevelBorder.Background = new SolidColorBrush(Color.FromRgb(0, 123, 255)); // Blue
                    LevelText.Foreground = Brushes.White;
                    break;
            }
        }

        private string FormatData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "(empty)";

            // Try to format JSON-like data
            if (data.TrimStart().StartsWith("{") || data.TrimStart().StartsWith("["))
            {
                try
                {
                    // Simple JSON formatting
                    return FormatJson(data);
                }
                catch
                {
                    return data;
                }
            }

            return data;
        }

        private string FormatJson(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false;

            foreach (char c in json)
            {
                if (c == '"' && (sb.Length == 0 || sb[sb.Length - 1] != '\\'))
                    inString = !inString;

                if (!inString)
                {
                    if (c == '{' || c == '[')
                    {
                        sb.Append(c);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == '}' || c == ']')
                    {
                        sb.AppendLine();
                        indent--;
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(c);
                    }
                    else if (c == ',')
                    {
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == ':')
                    {
                        sb.Append(c);
                        sb.Append(' ');
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_log == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {_log.Date:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Level: {_log.Level}");
            sb.AppendLine($"Thread: {_log.ThreadName}");
            sb.AppendLine($"Logger: {_log.Logger}");
            sb.AppendLine($"Method: {_log.Method}");
            sb.AppendLine($"Pattern: {_log.Pattern}");
            sb.AppendLine();
            sb.AppendLine("=== Message ===");
            sb.AppendLine(_log.Message);

            if (!string.IsNullOrEmpty(_log.Data))
            {
                sb.AppendLine();
                sb.AppendLine("=== Data ===");
                sb.AppendLine(_log.Data);
            }

            if (!string.IsNullOrEmpty(_log.Exception))
            {
                sb.AppendLine();
                sb.AppendLine("=== Exception ===");
                sb.AppendLine(_log.Exception);
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Log details copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
