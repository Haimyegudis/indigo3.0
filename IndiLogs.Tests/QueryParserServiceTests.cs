using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class QueryParserServiceTests
    {
        private readonly QueryParserService _parser = new QueryParserService();

        [Fact]
        public void Parse_SingleWord_ReturnsConditionNode()
        {
            var result = _parser.Parse("error", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result.Type);
            Assert.Equal("Message", result.Field);
            Assert.Equal("Contains", result.Operator);
            Assert.Equal("error", result.Value);
        }

        [Fact]
        public void Parse_ExactPhrase_ReturnsConditionWithPhraseValue()
        {
            var result = _parser.Parse("\"disk full\"", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Condition, result.Type);
            Assert.Equal("disk full", result.Value);
        }

        [Fact]
        public void Parse_TwoWordsImplicitAnd_ReturnsAndGroup()
        {
            var result = _parser.Parse("error disk", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
            Assert.Equal("error", result.Children[0].Value);
            Assert.Equal("disk", result.Children[1].Value);
        }

        [Fact]
        public void Parse_OrOperator_ReturnsOrGroup()
        {
            var result = _parser.Parse("error OR warning", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void Parse_NotOperator_ReturnsNotAndGroup()
        {
            var result = _parser.Parse("-error", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("NOT AND", result.LogicalOperator);
            Assert.Single(result.Children);
            Assert.Equal("error", result.Children[0].Value);
        }

        [Fact]
        public void Parse_Parentheses_GroupsCorrectly()
        {
            var result = _parser.Parse("(error OR warning) crash", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
            // Left child should be the OR group
            Assert.Equal("OR", result.Children[0].LogicalOperator);
        }

        [Fact]
        public void Parse_EmptyQuery_ReturnsNullWithError()
        {
            var result = _parser.Parse("", out string error);

            Assert.Null(result);
            Assert.NotNull(error);
            Assert.Contains("empty", error, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_UnclosedQuote_ReturnsNullWithError()
        {
            var result = _parser.Parse("\"unclosed", out string error);

            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void Parse_PipeAsOr_ReturnsOrGroup()
        {
            var result = _parser.Parse("error | warning", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("OR", result.LogicalOperator);
        }

        [Fact]
        public void Parse_ExplicitAnd_ReturnsAndGroup()
        {
            var result = _parser.Parse("error AND warning", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Theory]
        [InlineData("hello world", false)]
        [InlineData("error OR warning", true)]
        [InlineData("\"exact match\"", true)]
        [InlineData("(grouped)", true)]
        [InlineData("-excluded", true)]
        [InlineData("simple", false)]
        public void HasBooleanOperators_DetectsOperators(string query, bool expected)
        {
            Assert.Equal(expected, QueryParserService.HasBooleanOperators(query));
        }

        [Fact]
        public void HasBooleanOperators_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(QueryParserService.HasBooleanOperators(null));
            Assert.False(QueryParserService.HasBooleanOperators(""));
            Assert.False(QueryParserService.HasBooleanOperators("   "));
        }

        [Fact]
        public void Parse_ComplexNested_ParsesCorrectly()
        {
            var result = _parser.Parse("(error OR warning) AND -\"ignore this\"", out string error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal(NodeType.Group, result.Type);
        }
    }
}
