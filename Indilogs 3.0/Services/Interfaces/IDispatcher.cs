using System;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services.Interfaces
{
    /// <summary>
    /// Abstracts UI thread dispatching so ViewModels don't depend on WPF's Dispatcher.
    /// Enables unit testing with a synchronous test implementation.
    /// </summary>
    public interface IDispatcher
    {
        /// <summary>
        /// Posts an action to the UI thread (fire-and-forget, non-blocking).
        /// </summary>
        void Post(Action action);

        /// <summary>
        /// Posts an action to the UI thread with a specific priority.
        /// </summary>
        void Post(Action action, System.Windows.Threading.DispatcherPriority priority);

        /// <summary>
        /// Invokes an action on the UI thread and returns a Task that completes when done.
        /// </summary>
        Task InvokeAsync(Action action);
    }
}
