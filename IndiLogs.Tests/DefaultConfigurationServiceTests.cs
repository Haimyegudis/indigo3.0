using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class DefaultConfigurationServiceTests
    {
        [Fact]
        public void GetFactoryPlcFilter_ReturnsOrGroup()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            Assert.NotNull(filter);
            Assert.Equal(NodeType.Group, filter.Type);
            Assert.Equal("OR", filter.LogicalOperator);
        }

        [Fact]
        public void GetFactoryPlcFilter_HasFourConditions()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            Assert.Equal(4, filter.Children.Count);
        }

        [Fact]
        public void GetFactoryPlcFilter_ContainsPlcMngrCondition()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            var plcMngr = filter.Children.FirstOrDefault(c => c.Field == "Message" && c.Value == "PlcMngr:");
            Assert.NotNull(plcMngr);
            Assert.Equal("Begins With", plcMngr.Operator);
        }

        [Fact]
        public void GetFactoryPlcFilter_ContainsManagerThread()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            var manager = filter.Children.FirstOrDefault(c => c.Field == "ThreadName" && c.Value == "Manager");
            Assert.NotNull(manager);
            Assert.Equal("Begins With", manager.Operator);
        }

        [Fact]
        public void GetFactoryPlcFilter_ContainsErrorLevel()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            var error = filter.Children.FirstOrDefault(c => c.Field == "Level" && c.Value == "error");
            Assert.NotNull(error);
            Assert.Equal("Equals", error.Operator);
        }

        [Fact]
        public void GetFactoryPlcFilter_ContainsEventsThread()
        {
            var filter = DefaultConfigurationService.GetFactoryPlcFilter();

            var events = filter.Children.FirstOrDefault(c => c.Field == "ThreadName" && c.Value == "Events");
            Assert.NotNull(events);
            Assert.Equal("Equals", events.Operator);
        }

        [Fact]
        public void GetFactoryPlcFilter_IsCached()
        {
            var filter1 = DefaultConfigurationService.GetFactoryPlcFilter();
            var filter2 = DefaultConfigurationService.GetFactoryPlcFilter();

            Assert.Same(filter1, filter2);
        }

        [Fact]
        public void GetFactoryMainColoringRules_ReturnsEmpty()
        {
            var rules = DefaultConfigurationService.GetFactoryMainColoringRules();
            Assert.NotNull(rules);
            Assert.Empty(rules);
        }

        [Fact]
        public void GetFactoryAppColoringRules_ReturnsTwoRules()
        {
            var rules = DefaultConfigurationService.GetFactoryAppColoringRules();
            Assert.NotNull(rules);
            Assert.Equal(2, rules.Count);
        }

        [Fact]
        public void GetFactoryAppColoringRules_ContainsPipelineCancellation()
        {
            var rules = DefaultConfigurationService.GetFactoryAppColoringRules();

            var pipeline = rules.FirstOrDefault(r => r.Value.Contains("PipelineCancellationProvider"));
            Assert.NotNull(pipeline);
            Assert.Equal("Logger", pipeline.Field);
            Assert.Equal("Contains", pipeline.Operator);
        }

        [Fact]
        public void GetFactoryAppColoringRules_ContainsPressStateManager()
        {
            var rules = DefaultConfigurationService.GetFactoryAppColoringRules();

            var press = rules.FirstOrDefault(r => r.Value.Contains("PressStateManager"));
            Assert.NotNull(press);
            Assert.Equal("Logger", press.Field);
            Assert.Equal("Contains", press.Operator);
        }

        [Fact]
        public void GetFactoryBinaryAppPlcFilter_ReturnsOrGroup()
        {
            var filter = DefaultConfigurationService.GetFactoryBinaryAppPlcFilter();

            Assert.NotNull(filter);
            Assert.Equal(NodeType.Group, filter.Type);
            Assert.Equal("OR", filter.LogicalOperator);
            Assert.Equal(2, filter.Children.Count);
        }

        [Fact]
        public void GetFactoryBinaryAppPlcFilter_ContainsErrorAndState()
        {
            var filter = DefaultConfigurationService.GetFactoryBinaryAppPlcFilter();

            var error = filter.Children.FirstOrDefault(c => c.Field == "Level" && c.Value == "error");
            Assert.NotNull(error);

            var state = filter.Children.FirstOrDefault(c => c.Field == "Message" && c.Value == "=== state");
            Assert.NotNull(state);
        }

        [Fact]
        public void GetFactoryBinaryAppColoringRules_ReturnsEmpty()
        {
            var rules = DefaultConfigurationService.GetFactoryBinaryAppColoringRules();
            Assert.NotNull(rules);
            Assert.Empty(rules);
        }
    }
}
