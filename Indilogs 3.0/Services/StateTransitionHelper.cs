using System;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Shared helper for detecting and parsing PLC state transitions in log entries.
    /// Centralises the pattern: ThreadName == "Manager" &amp;&amp; Message starts with "PlcMngr:" &amp;&amp; Message contains "->".
    /// </summary>
    public static class StateTransitionHelper
    {
        /// <summary>
        /// Returns <c>true</c> when the log entry represents an S6-style PLC state transition
        /// (Manager thread, "PlcMngr:" prefix, arrow "->").
        /// </summary>
        public static bool IsS6StateTransition(LogEntry log)
        {
            return log.ThreadName != null &&
                   string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) &&
                   log.Message != null &&
                   log.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                   log.Message.Contains("->");
        }

        /// <summary>
        /// Parses a state transition message ("PlcMngr: OldState -> NewState") into its from/to parts.
        /// Returns <c>false</c> when the message does not contain a valid arrow.
        /// </summary>
        public static bool TryParseTransition(string? message, out string? fromState, out string? toState)
        {
            fromState = null;
            toState = null;

            if (message == null) return false;

            var parts = message.Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return false;

            fromState = parts[0].Replace("PlcMngr:", "").Trim();
            toState = parts[1].Trim();
            return true;
        }
    }
}
