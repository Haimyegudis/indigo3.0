using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Views
{
    public partial class AnalysisReportWindow : Window
    {
        // The property that the left-side ListBox binds to
        public List<AnalysisResult> AllResults { get; set; } = null!;

        /// <summary>Called when the user double-clicks a log row — navigates the PLC tab.</summary>
        public Action<LogEntry>? JumpToLog { get; private set; }

        /// <summary>Bound by DataGrid MouseBinding in XAML to perform navigation.</summary>
        public ICommand JumpToLogFromFailuresCommand { get; private set; }

        public AnalysisReportWindow(List<AnalysisResult> results, Action<LogEntry>? jumpToLog = null)
        {
            InitializeComponent();
            AllResults = results;
            JumpToLog  = jumpToLog;

            JumpToLogFromFailuresCommand = new FailuresRelayCommand(obj =>
            {
                if (obj is LogEntry entry)
                    JumpToLog?.Invoke(entry);
            });

            // Bind data to the window itself
            this.DataContext = this;

            // Automatically select the first run (if it exists) so the user sees something immediately
            if (AllResults != null && AllResults.Any())
            {
                RunsList.SelectedIndex = 0;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>Local RelayCommand so the window doesn't need a reference to the ViewModels namespace.</summary>
    internal class FailuresRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public FailuresRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter)    => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
