using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs.Tests
{
    /// <summary>
    /// Test double for IDispatcher that executes actions synchronously.
    /// Tracks posted actions for assertion in unit tests.
    /// </summary>
    public class TestDispatcher : IDispatcher
    {
        public List<Action> PostedActions { get; } = new List<Action>();
        public bool ExecuteImmediately { get; set; } = true;

        public void Post(Action action)
        {
            PostedActions.Add(action);
            if (ExecuteImmediately) action();
        }

        public void Post(Action action, DispatcherPriority priority)
        {
            PostedActions.Add(action);
            if (ExecuteImmediately) action();
        }

        public Task InvokeAsync(Action action)
        {
            PostedActions.Add(action);
            if (ExecuteImmediately) action();
            return Task.CompletedTask;
        }
    }
}
