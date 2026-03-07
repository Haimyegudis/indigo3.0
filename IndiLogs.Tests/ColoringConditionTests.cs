using IndiLogs_3._0.Models;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class ColoringConditionTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var cond = new ColoringCondition();

            Assert.Equal("", cond.Field);
            Assert.Equal("", cond.Operator);
            Assert.Equal("", cond.Value);
            Assert.True(cond.IsEnabled);
        }

        [Fact]
        public void Clone_CopiesAllProperties()
        {
            var original = new ColoringCondition
            {
                Field = "Message",
                Operator = "Contains",
                Value = "error",
                Color = Colors.Red
            };

            var clone = original.Clone();

            Assert.Equal("Message", clone.Field);
            Assert.Equal("Contains", clone.Operator);
            Assert.Equal("error", clone.Value);
            Assert.Equal(Colors.Red, clone.Color);
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var original = new ColoringCondition
            {
                Field = "Message",
                Operator = "Contains",
                Value = "error",
                Color = Colors.Red
            };

            var clone = original.Clone();
            clone.Field = "Level";
            clone.Value = "changed";

            Assert.Equal("Message", original.Field);
            Assert.Equal("error", original.Value);
        }

        [Fact]
        public void IsEnabled_DefaultTrue_JsonIgnored()
        {
            // IsEnabled defaults to true and is JsonIgnored
            var cond = new ColoringCondition();
            Assert.True(cond.IsEnabled);

            cond.IsEnabled = false;
            Assert.False(cond.IsEnabled);
        }
    }

    public class FilterConditionTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var cond = new FilterCondition();

            Assert.Equal("", cond.Field);
            Assert.Equal("", cond.Operator);
            Assert.Equal("", cond.Value);
            Assert.True(cond.IsActive);
        }

        [Fact]
        public void SetProperties_Works()
        {
            var cond = new FilterCondition
            {
                Field = "Level",
                Operator = "Equals",
                Value = "Error",
                IsActive = false
            };

            Assert.Equal("Level", cond.Field);
            Assert.Equal("Equals", cond.Operator);
            Assert.Equal("Error", cond.Value);
            Assert.False(cond.IsActive);
        }
    }
}
