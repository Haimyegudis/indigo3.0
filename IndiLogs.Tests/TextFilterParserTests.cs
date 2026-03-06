using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using Xunit;

namespace IndiLogs.Tests
{
    public class TextFilterParserTests
    {
        [Fact]
        public void Parse_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(TextFilterParser.Parse(null));
            Assert.Null(TextFilterParser.Parse(""));
            Assert.Null(TextFilterParser.Parse("   "));
        }

        [Fact]
        public void Parse_SingleCondition_ReturnsConditionNode()
        {
            var node = TextFilterParser.Parse("StartsWith([Thread], 'main')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("ThreadName", node.Field);
            Assert.Equal("Begins With", node.Operator);
            Assert.Equal("main", node.Value);
        }

        [Fact]
        public void Parse_ContainsCondition_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'error')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("error", node.Value);
        }

        [Fact]
        public void Parse_EndsWithCondition_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("EndsWith([Level], 'Error')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("Level", node.Field);
            Assert.Equal("Ends With", node.Operator);
            Assert.Equal("Error", node.Value);
        }

        [Fact]
        public void Parse_EqualsCondition_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("Equals([Logger], 'MyClass')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("Logger", node.Field);
            Assert.Equal("Equals", node.Operator);
            Assert.Equal("MyClass", node.Value);
        }

        [Fact]
        public void Parse_ThreadFieldAlias_MapsToThreadName()
        {
            var node = TextFilterParser.Parse("Contains([Thread], 'worker')");
            Assert.NotNull(node);
            Assert.Equal("ThreadName", node.Field);
        }

        [Fact]
        public void Parse_UnknownField_PassedThrough()
        {
            var node = TextFilterParser.Parse("Contains([CustomField], 'val')");
            Assert.NotNull(node);
            Assert.Equal("CustomField", node.Field);
        }

        [Fact]
        public void Parse_UnknownFunction_DefaultsToContains()
        {
            var node = TextFilterParser.Parse("Like([Message], 'test')");
            Assert.NotNull(node);
            Assert.Equal("Contains", node.Operator);
        }

        [Fact]
        public void Parse_OrExpression_CreatesOrGroup()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void Parse_AndExpression_CreatesAndGroup()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Level], 'Error')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void Parse_AndBindsTighterThanOr()
        {
            // "A Or B And C" should parse as "A Or (B And C)"
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') And Contains([Message], 'c')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);

            // Second child should be the AND group
            var andChild = node.Children[1];
            Assert.Equal(NodeType.Group, andChild.Type);
            Assert.Equal("AND", andChild.LogicalOperator);
            Assert.Equal(2, andChild.Children.Count);
        }

        [Fact]
        public void Parse_ParenthesizedGroup_OverridesPrecedence()
        {
            // "(A Or B) And C" should have AND at top with OR child
            var node = TextFilterParser.Parse(
                "(Contains([Message], 'a') Or Contains([Message], 'b')) And Contains([Message], 'c')");

            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);

            // First child should be the OR group
            var orChild = node.Children[0];
            Assert.Equal(NodeType.Group, orChild.Type);
            Assert.Equal("OR", orChild.LogicalOperator);
        }

        [Fact]
        public void Parse_MissingClosingParen_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("(Contains([Message], 'a')"));
        }

        [Fact]
        public void Parse_UnexpectedToken_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Or"));
        }

        [Fact]
        public void Parse_TrailingTokens_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') Contains([Message], 'b')"));
        }
    }
}
