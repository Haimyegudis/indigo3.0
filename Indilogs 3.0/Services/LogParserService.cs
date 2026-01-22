using IndiLogs_3._0.Models;
using System;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0.Services
{
    public static class LogParserService
    {
        // Pattern examples:
        // "Pattern: SomePattern" or "pattern=SomeValue"
        private static readonly Regex PatternRegex = new Regex(@"(?:Pattern[:=]\s*)([^\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Data examples:
        // "Data: {...}" or "data={...}" or JSON-like structures
        private static readonly Regex DataRegex = new Regex(@"(?:Data[:=]\s*)(\{[^}]*\}|[^\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Exception examples:
        // "Exception: ..." or "Error: ..." or full exception stack traces
        private static readonly Regex ExceptionRegex = new Regex(@"(?:Exception[:=]\s*|Error[:=]\s*)(.+?)(?=\s*(?:at |$))", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        // Alternative: Look for common exception class names
        private static readonly Regex ExceptionClassRegex = new Regex(@"\b(\w+Exception):", RegexOptions.Compiled);

        /// <summary>
        /// Parse a log entry message to extract Pattern, Data, and Exception fields
        /// </summary>
        public static void ParseLogEntry(LogEntry log)
        {
            if (log == null || string.IsNullOrEmpty(log.Message))
                return;

            string message = log.Message;

            // Parse Pattern
            var patternMatch = PatternRegex.Match(message);
            if (patternMatch.Success)
            {
                log.Pattern = patternMatch.Groups[1].Value.Trim();
            }

            // Parse Data
            var dataMatch = DataRegex.Match(message);
            if (dataMatch.Success)
            {
                log.Data = dataMatch.Groups[1].Value.Trim();
            }

            // Parse Exception
            // Try detailed exception pattern first
            var exceptionMatch = ExceptionRegex.Match(message);
            if (exceptionMatch.Success)
            {
                log.Exception = exceptionMatch.Groups[1].Value.Trim();
            }
            else
            {
                // Try to find exception class name
                var exceptionClassMatch = ExceptionClassRegex.Match(message);
                if (exceptionClassMatch.Success)
                {
                    // Extract from exception class to end of message or next log indicator
                    int startIndex = exceptionClassMatch.Index;
                    string exceptionPart = message.Substring(startIndex);

                    // Limit to reasonable length (e.g., 500 characters)
                    if (exceptionPart.Length > 500)
                        exceptionPart = exceptionPart.Substring(0, 500) + "...";

                    log.Exception = exceptionPart.Trim();
                }
            }

            // If this is an ERROR level log and we found no exception,
            // but the message looks like an error, capture a portion
            if (string.IsNullOrEmpty(log.Exception) &&
                !string.IsNullOrEmpty(log.Level) &&
                log.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                // Check if message contains common error indicators
                string lowerMessage = message.ToLowerInvariant();
                if (lowerMessage.Contains("failed") ||
                    lowerMessage.Contains("error") ||
                    lowerMessage.Contains("exception"))
                {
                    // Extract first 200 characters as error description
                    log.Exception = message.Length > 200
                        ? message.Substring(0, 200) + "..."
                        : message;
                }
            }
        }

        /// <summary>
        /// Parse multiple log entries in batch (async version for performance)
        /// </summary>
        public static async System.Threading.Tasks.Task ParseLogEntriesAsync(System.Collections.Generic.IEnumerable<LogEntry> logs)
        {
            if (logs == null)
                return;

            await System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.ForEach(logs, log =>
                {
                    ParseLogEntry(log);
                });
            });
        }

        /// <summary>
        /// Parse multiple log entries in batch (legacy synchronous version - use ParseLogEntriesAsync instead)
        /// </summary>
        [System.Obsolete("Use ParseLogEntriesAsync for better performance")]
        public static void ParseLogEntries(System.Collections.Generic.IEnumerable<LogEntry> logs)
        {
            if (logs == null)
                return;

            foreach (var log in logs)
            {
                ParseLogEntry(log);
            }
        }
    }
}
