using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using Xunit;

namespace IndiLogs.Tests
{
    public class StateTransitionHelperTests
    {
        // ── IsS6StateTransition ──

        [Fact]
        public void IsS6StateTransition_ReturnsTrueForValidTransition()
        {
            var log = new LogEntry
            {
                ThreadName = "Manager",
                Message = "PlcMngr: Idle -> Running"
            };
            Assert.True(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_ReturnsFalseForWrongThread()
        {
            var log = new LogEntry
            {
                ThreadName = "Worker",
                Message = "PlcMngr: Idle -> Running"
            };
            Assert.False(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_ReturnsFalseForMissingPrefix()
        {
            var log = new LogEntry
            {
                ThreadName = "Manager",
                Message = "Some other message with ->"
            };
            Assert.False(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_ReturnsFalseForMissingArrow()
        {
            var log = new LogEntry
            {
                ThreadName = "Manager",
                Message = "PlcMngr: Idle to Running"
            };
            Assert.False(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_IsCaseInsensitiveForThread()
        {
            var log = new LogEntry
            {
                ThreadName = "manager",
                Message = "PlcMngr: Idle -> Running"
            };
            Assert.True(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_HandlesNullThreadName()
        {
            var log = new LogEntry
            {
                ThreadName = null!,
                Message = "PlcMngr: Idle -> Running"
            };
            Assert.False(StateTransitionHelper.IsS6StateTransition(log));
        }

        [Fact]
        public void IsS6StateTransition_HandlesNullMessage()
        {
            var log = new LogEntry
            {
                ThreadName = "Manager",
                Message = null!
            };
            Assert.False(StateTransitionHelper.IsS6StateTransition(log));
        }

        // ── TryParseTransition ──

        [Fact]
        public void TryParseTransition_ParsesValidMessage()
        {
            bool result = StateTransitionHelper.TryParseTransition(
                "PlcMngr: Idle -> Running", out string? from, out string? to);

            Assert.True(result);
            Assert.Equal("Idle", from);
            Assert.Equal("Running", to);
        }

        [Fact]
        public void TryParseTransition_ReturnsFalseForNull()
        {
            bool result = StateTransitionHelper.TryParseTransition(
                null, out string? from, out string? to);

            Assert.False(result);
            Assert.Null(from);
            Assert.Null(to);
        }

        [Fact]
        public void TryParseTransition_ReturnsFalseForNoArrow()
        {
            bool result = StateTransitionHelper.TryParseTransition(
                "PlcMngr: Just a message", out string? from, out string? to);

            Assert.False(result);
        }

        [Fact]
        public void TryParseTransition_HandlesMultipleArrows()
        {
            bool result = StateTransitionHelper.TryParseTransition(
                "PlcMngr: A -> B -> C", out string? from, out string? to);

            Assert.True(result);
            Assert.Equal("A", from);
            Assert.Equal("B", to);
        }
    }
}
