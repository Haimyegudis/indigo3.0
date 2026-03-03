using IndiLogs_3._0.Models;
using Xunit;

namespace IndiLogs.Tests
{
    public class FilterNodeTests
    {
        [Fact]
        public void DeepClone_CopiesAllProperties()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                LogicalOperator = "OR",
                Field = "Logger",
                Operator = "Equals",
                Value = "TestLogger"
            };

            var clone = original.DeepClone();

            Assert.Equal(original.Type, clone.Type);
            Assert.Equal(original.LogicalOperator, clone.LogicalOperator);
            Assert.Equal(original.Field, clone.Field);
            Assert.Equal(original.Operator, clone.Operator);
            Assert.Equal(original.Value, clone.Value);
        }

        [Fact]
        public void DeepClone_ClonesChildren()
        {
            var parent = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "AND"
            };
            parent.Children.Add(new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Value = "child1"
            });
            parent.Children.Add(new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Value = "child2"
            });

            var clone = parent.DeepClone();

            Assert.Equal(2, clone.Children.Count);
            Assert.Equal("child1", clone.Children[0].Value);
            Assert.Equal("child2", clone.Children[1].Value);
        }

        [Fact]
        public void DeepClone_IsIndependent()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                Value = "original"
            };

            var clone = original.DeepClone();
            clone.Value = "modified";

            Assert.Equal("original", original.Value);
            Assert.Equal("modified", clone.Value);
        }

        [Fact]
        public void DeepClone_ChildrenAreIndependent()
        {
            var parent = new FilterNode { Type = NodeType.Group };
            parent.Children.Add(new FilterNode { Type = NodeType.Condition, Value = "child" });

            var clone = parent.DeepClone();
            clone.Children[0].Value = "modified";

            Assert.Equal("child", parent.Children[0].Value);
        }

        [Fact]
        public void PropertyChanged_Fires()
        {
            var node = new FilterNode();
            string changedProperty = null;
            node.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

            node.Value = "new value";

            Assert.Equal("Value", changedProperty);
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var node = new FilterNode();

            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("", node.Value);
            Assert.True(node.IsEnabled);
            Assert.Empty(node.Children);
        }
    }
}
