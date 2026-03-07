using IndiLogs_3._0.ViewModels;
using System;
using Xunit;

namespace IndiLogs.Tests
{
    public class RelayCommandTests
    {
        [Fact]
        public void Execute_InvokesAction()
        {
            object? received = null;
            var cmd = new RelayCommand(p => received = p);

            cmd.Execute("hello");

            Assert.Equal("hello", received);
        }

        [Fact]
        public void Execute_NullParameter_InvokesAction()
        {
            bool invoked = false;
            var cmd = new RelayCommand(_ => invoked = true);

            cmd.Execute(null);

            Assert.True(invoked);
        }

        [Fact]
        public void CanExecute_NoPredicate_ReturnsTrue()
        {
            var cmd = new RelayCommand(_ => { });

            Assert.True(cmd.CanExecute(null));
            Assert.True(cmd.CanExecute("anything"));
        }

        [Fact]
        public void CanExecute_WithPredicate_ReturnsPredicateResult()
        {
            var cmd = new RelayCommand(_ => { }, p => p is string s && s == "yes");

            Assert.True(cmd.CanExecute("yes"));
            Assert.False(cmd.CanExecute("no"));
            Assert.False(cmd.CanExecute(null));
        }

        [Fact]
        public void Constructor_NullExecute_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }
    }
}
