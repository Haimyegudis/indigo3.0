using System;
using System.Threading.Tasks;
using Xunit;

namespace IndiLogs.Tests
{
    public class DispatcherTests
    {
        [Fact]
        public void TestDispatcher_Post_ExecutesAction()
        {
            var dispatcher = new TestDispatcher();
            int counter = 0;
            dispatcher.Post(() => counter++);
            Assert.Equal(1, counter);
        }

        [Fact]
        public void TestDispatcher_Post_TracksPostedActions()
        {
            var dispatcher = new TestDispatcher();
            dispatcher.Post(() => { });
            dispatcher.Post(() => { });
            Assert.Equal(2, dispatcher.PostedActions.Count);
        }

        [Fact]
        public void TestDispatcher_PostWithPriority_ExecutesAction()
        {
            var dispatcher = new TestDispatcher();
            bool executed = false;
            dispatcher.Post(() => executed = true, IndiLogs_3._0.Services.Interfaces.DispatchPriority.Background);
            Assert.True(executed);
        }

        [Fact]
        public async Task TestDispatcher_InvokeAsync_ExecutesAndCompletes()
        {
            var dispatcher = new TestDispatcher();
            string result = "";
            await dispatcher.InvokeAsync(() => result = "done");
            Assert.Equal("done", result);
        }

        [Fact]
        public void TestDispatcher_DeferredExecution_DoesNotExecuteImmediately()
        {
            var dispatcher = new TestDispatcher { ExecuteImmediately = false };
            int counter = 0;
            dispatcher.Post(() => counter++);
            Assert.Equal(0, counter);
            Assert.Single(dispatcher.PostedActions);
        }
    }
}
