using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using Xunit;

namespace IndiLogs.Tests
{
    public class TextFilterParserExtendedTests
    {
        [Fact]
        public void Parse_ThreadField_MapsToThreadName()
        {
            var node = TextFilterParser.Parse("Contains([Thread], 'v')");
            Assert.NotNull(node);
            Assert.Equal("ThreadName", node.Field);
        }

        [Fact]
        public void Parse_ProcessNameField_MapsToProcessName()
        {
            var node = TextFilterParser.Parse("Contains([ProcessName], 'v')");
            Assert.NotNull(node);
            Assert.Equal("ProcessName", node.Field);
        }

        [Fact]
        public void Parse_StartsWithOperator_MapsToBeginsWith()
        {
            var node = TextFilterParser.Parse("StartsWith([Message], 'x')");
            Assert.NotNull(node);
            Assert.Equal("Begins With", node.Operator);
        }

        [Fact]
        public void Parse_EndsWithOperator_MapsToEndsWith()
        {
            var node = TextFilterParser.Parse("EndsWith([Message], 'x')");
            Assert.NotNull(node);
            Assert.Equal("Ends With", node.Operator);
        }

        [Fact]
        public void Parse_EqualsOperator_MapsToEquals()
        {
            var node = TextFilterParser.Parse("Equals([Message], 'x')");
            Assert.NotNull(node);
            Assert.Equal("Equals", node.Operator);
        }

        [Theory]
        [InlineData("or")]
        [InlineData("OR")]
        [InlineData("Or")]
        public void Parse_OrKeyword_CaseInsensitive(string orKeyword)
        {
            var node = TextFilterParser.Parse(
                $"Contains([Message], 'a') {orKeyword} Contains([Message], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Theory]
        [InlineData("and")]
        [InlineData("AND")]
        [InlineData("And")]
        public void Parse_AndKeyword_CaseInsensitive(string andKeyword)
        {
            var node = TextFilterParser.Parse(
                $"Contains([Message], 'a') {andKeyword} Contains([Message], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Theory]
        [InlineData("contains")]
        [InlineData("CONTAINS")]
        [InlineData("Contains")]
        public void Parse_FunctionName_CaseInsensitive(string funcName)
        {
            var node = TextFilterParser.Parse($"{funcName}([Message], 'val')");
            Assert.NotNull(node);
            Assert.Equal("Contains", node.Operator);
        }

        [Theory]
        [InlineData("thread", "ThreadName")]
        [InlineData("THREAD", "ThreadName")]
        [InlineData("message", "Message")]
        [InlineData("MESSAGE", "Message")]
        [InlineData("level", "Level")]
        [InlineData("LOGGER", "Logger")]
        public void Parse_FieldName_CaseInsensitive(string fieldName, string expectedField)
        {
            var node = TextFilterParser.Parse($"Contains([{fieldName}], 'val')");
            Assert.NotNull(node);
            Assert.Equal(expectedField, node.Field);
        }

        [Fact]
        public void Parse_ThreeOrConditions()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') Or Contains([Message], 'c')");
            Assert.NotNull(node);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
        }

        [Fact]
        public void Parse_ThreeAndConditions()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Level], 'b') And Contains([Logger], 'c')");
            Assert.NotNull(node);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
        }

        [Fact]
        public void Parse_MixedAndOr_CorrectPrecedence()
        {
            // A Or B And C Or D => (A) Or (B And C) Or (D)
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') And Contains([Message], 'c') Or Contains([Message], 'd')");
            Assert.NotNull(node);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
            Assert.Equal(NodeType.Condition, node.Children[0].Type);
            Assert.Equal(NodeType.Group, node.Children[1].Type);
            Assert.Equal("AND", node.Children[1].LogicalOperator);
            Assert.Equal(NodeType.Condition, node.Children[2].Type);
        }

        [Fact]
        public void Parse_MultipleAndGroups_SeparatedByOr()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Message], 'b') Or Contains([Message], 'c') And Contains([Message], 'd')");
            Assert.NotNull(node);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
            Assert.Equal("AND", node.Children[0].LogicalOperator);
            Assert.Equal("AND", node.Children[1].LogicalOperator);
        }

        [Fact]
        public void Parse_DeeplyNestedParentheses()
        {
            var node = TextFilterParser.Parse("((( Contains([Message], 'deep') )))");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("deep", node.Value);
        }

        [Fact]
        public void Parse_NestedGroupsWithinGroups()
        {
            var node = TextFilterParser.Parse(
                "((Contains([Message], 'a') Or Contains([Message], 'b')) And (Contains([Message], 'c') Or Contains([Message], 'd')))");
            Assert.NotNull(node);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
            Assert.Equal("OR", node.Children[0].LogicalOperator);
            Assert.Equal("OR", node.Children[1].LogicalOperator);
        }

        [Fact]
        public void Parse_EmptyQuotedValue()
        {
            var node = TextFilterParser.Parse("Contains([Message], '')");
            Assert.NotNull(node);
            Assert.Equal("", node.Value);
        }

        [Fact]
        public void Parse_ValueWithSpaces()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'hello world')");
            Assert.NotNull(node);
            Assert.Equal("hello world", node.Value);
        }

        [Fact]
        public void Parse_ValueWithSpecialCharacters()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'error: [code=42] @#$%')");
            Assert.NotNull(node);
            Assert.Equal("error: [code=42] @#$%", node.Value);
        }

        [Fact]
        public void Parse_UnexpectedEnd_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') And"));
        }

        [Fact]
        public void Parse_MissingField_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains('value')"));
        }

        [Fact]
        public void Parse_MissingValue_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], )"));
        }

        [Fact]
        public void Parse_MissingCloseParen_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'value'"));
        }

        [Fact]
        public void Parse_SingleCondition_ReturnsConditionNotGroup()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'only')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
        }

        [Fact]
        public void Parse_SingleConditionInParens_ReturnsCondition()
        {
            var node = TextFilterParser.Parse("(Contains([Message], 'wrapped'))");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node.Type);
            Assert.Equal("wrapped", node.Value);
        }

        [Fact]
        public void Parse_ComplexMixedFieldsAndOperators()
        {
            var node = TextFilterParser.Parse(
                "StartsWith([Thread], 'main') And (Contains([Message], 'error') Or EndsWith([Logger], 'Service')) And Equals([Level], 'ERROR')");
            Assert.NotNull(node);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(3, node.Children.Count);
            Assert.Equal("ThreadName", node.Children[0].Field);
            Assert.Equal("Begins With", node.Children[0].Operator);
            var orGroup = node.Children[1];
            Assert.Equal("OR", orGroup.LogicalOperator);
            Assert.Equal("Level", node.Children[2].Field);
            Assert.Equal("Equals", node.Children[2].Operator);
        }

        [Fact]
        public void Parse_UnknownField_PassedThrough()
        {
            var node = TextFilterParser.Parse("Contains([CustomField], 'val')");
            Assert.NotNull(node);
            Assert.Equal("CustomField", node.Field);
        }

        [Fact]
        public void Parse_ExtraWhitespace_Ignored()
        {
            var node = TextFilterParser.Parse("   Contains(  [Message]  ,  'val'  )   ");
            Assert.NotNull(node);
            Assert.Equal("Message", node.Field);
            Assert.Equal("val", node.Value);
        }

        [Fact]
        public void Parse_TrailingTokens_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') Contains([Message], 'b')"));
        }

        [Fact]
        public void Parse_FourOrConditions()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') Or Contains([Message], 'c') Or Contains([Message], 'd')");
            Assert.NotNull(node);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(4, node.Children.Count);
        }

        [Fact]
        public void Parse_FourAndConditions()
        {
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') And Contains([Level], 'b') And Contains([Logger], 'c') And Contains([Thread], 'd')");
            Assert.NotNull(node);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(4, node.Children.Count);
            Assert.Equal("ThreadName", node.Children[3].Field);
        }
    }
}
