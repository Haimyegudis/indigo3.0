using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Production implementation that delegates to WPF's Application.Current.Dispatcher.
    /// </summary>
    public class WpfDispatcher : IDispatcher
    {
        public void Post(Action action)
        {
            Application.Current.Dispatcher.BeginInvoke(action);
        }

        public void Post(Action action, DispatcherPriority priority)
        {
            Application.Current.Dispatcher.BeginInvoke(priority, action);
        }

        public Task InvokeAsync(Action action)
        {
            return Application.Current.Dispatcher.InvokeAsync(action).Task;
        }
    }
}
