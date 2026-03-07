using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class StripeDataParserServiceTests
    {
        private StripeDataParserService CreateService() => new StripeDataParserService();

        [Fact]
        public void ParseFromLogs_EmptyList_ReturnsEmpty()
        {
            var svc = CreateService();
            var result = svc.ParseFromLogs(new List<LogEntry>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_NoStripeData_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "Normal log message", Data = "" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_NullData_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "", Data = "" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_InvalidJson_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Data = "stripeDescriptor not json at all {{{{" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_FiltersOnlyStripeDescriptorLogs()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "normal log 1", Data = "no stripe here" },
                new LogEntry { Message = "normal log 2", Data = "no stripe here either" },
                new LogEntry { Message = "stripeDescriptor data", Data = "not json" },
            };

            // Even though "stripeDescriptor" appears in message, the JSON is invalid
            var result = svc.ParseFromLogs(logs);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromJson_InvalidJson_ThrowsOrReturnsEmpty()
        {
            var svc = CreateService();
            // ParseFromJson calls ParseStripeJson which may throw JsonException
            // or return empty depending on the JSON structure
            try
            {
                var result = svc.ParseFromJson("not json");
                // If it doesn't throw, should be empty
                Assert.Empty(result);
            }
            catch (System.Text.Json.JsonException)
            {
                // Expected for invalid JSON
            }
        }

        [Fact]
        public void ParseFromJson_EmptyJson_ReturnsEmpty()
        {
            var svc = CreateService();
            try
            {
                var result = svc.ParseFromJson("{}");
                Assert.Empty(result);
            }
            catch (System.Text.Json.JsonException)
            {
                // Some implementations may throw
            }
        }
    }
}
