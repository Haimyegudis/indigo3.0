using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Parses Indigo stripe/slice JSON data from log entries
    /// </summary>
    public partial class StripeDataParserService
    {

        /// <summary>
        /// Parses stripe data from log entries - optimized version
        /// </summary>
        public List<IndigoStripeEntry> ParseFromLogs(IEnumerable<LogEntry> logs)
        {
            var results = new List<IndigoStripeEntry>();
            var logsList = logs.ToList();

            // Pre-filter: only logs that might contain stripe data
            var candidates = logsList.Where(log =>
                (!string.IsNullOrEmpty(log.Data) && log.Data.Contains("stripeDescriptor")) ||
                (!string.IsNullOrEmpty(log.Message) && log.Message.Contains("stripeDescriptor"))
            ).ToList();

            foreach (var log in candidates)
            {
                string? jsonString = ExtractValidJson(log);
                if (string.IsNullOrEmpty(jsonString))
                    continue;

                try
                {
                    var entries = ParseStripeJson(jsonString, log.Date);
                    results.AddRange(entries);
                }
                catch (JsonException)
                {
                    // Silently skip invalid JSON
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Parsing stripe entry failed", ex);
                }
            }

            return results;
        }

        /// <summary>
        /// Parses stripe data directly from JSON string
        /// </summary>
        public List<IndigoStripeEntry> ParseFromJson(string jsonString, DateTime? timestamp = null)
        {
            return ParseStripeJson(jsonString, timestamp ?? DateTime.Now);
        }

        /// <summary>
        /// Extract valid JSON that contains stripeDescriptor
        /// </summary>
        private string? ExtractValidJson(LogEntry log)
        {
            // First try the Data field - it's more likely to have clean JSON
            if (!string.IsNullOrEmpty(log.Data) && log.Data.Contains("stripeDescriptor"))
            {
                string? json = ExtractJsonObject(log.Data);
                if (json != null && IsValidJson(json))
                    return json;
            }

            // Then try the Message field
            if (!string.IsNullOrEmpty(log.Message) && log.Message.Contains("stripeDescriptor"))
            {
                string? json = ExtractJsonObject(log.Message);
                if (json != null && IsValidJson(json))
                    return json;
            }

            return null;
        }

        /// <summary>
        /// Extract a complete JSON object from text by matching braces
        /// </summary>
        private string? ExtractJsonObject(string text)
        {
            int startIndex = text.IndexOf('{');
            if (startIndex < 0)
                return null;

            int depth = 0;
            int endIndex = -1;

            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = i;
                        break;
                    }
                }
            }

            if (endIndex > startIndex)
            {
                return text.Substring(startIndex, endIndex - startIndex + 1);
            }

            return null;
        }

        /// <summary>
        /// Quick validation check for JSON
        /// </summary>
        private bool IsValidJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json, AppConstants.SafeJsonDocumentOptions);
                return doc.RootElement.TryGetProperty("stripeDescriptor", out _);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Stripe JSON validation failed: {ex.Message}");
                return false;
            }
        }

    }
}
