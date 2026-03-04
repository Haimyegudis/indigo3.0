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

        public void Post(Action action, DispatchPriority priority)
        {
            Application.Current.Dispatcher.BeginInvoke(MapPriority(priority), action);
        }

        public Task InvokeAsync(Action action)
        {
            return Application.Current.Dispatcher.InvokeAsync(action).Task;
        }

        private static DispatcherPriority MapPriority(DispatchPriority priority) => priority switch
        {
            DispatchPriority.Background => DispatcherPriority.Background,
            DispatchPriority.ContextIdle => DispatcherPriority.ContextIdle,
            DispatchPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
            DispatchPriority.DataBind => DispatcherPriority.DataBind,
            DispatchPriority.Loaded => DispatcherPriority.Loaded,
            _ => DispatcherPriority.Normal,
        };
    }
}
