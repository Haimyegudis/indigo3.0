using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Service for parsing boolean search queries into FilterNode trees.
    /// Supports: AND (default), OR, NOT (-), exact phrases (""), and grouping (())
    /// </summary>
    public class QueryParserService
    {
        private enum TokenType
        {
            Word,           // Regular word
            And,            // AND operator (or implicit space)
            Or,             // OR operator
            Not,            // NOT operator (-)
            LeftParen,      // (
            RightParen,     // )
            Phrase,         // "exact phrase"
            EndOfInput
        }

        private class Token
        {
            public TokenType Type { get; set; }
            public string Value { get; set; }
            public int Position { get; set; }

            public Token(TokenType type, string value, int position)
            {
                Type = type;
                Value = value;
                Position = position;
            }
        }

        private List<Token> _tokens;
        private int _currentTokenIndex;
        private string _originalQuery;

        /// <summary>
        /// Parses a boolean search query into a FilterNode tree.
        /// </summary>
        /// <param name="query">The search query string</param>
        /// <param name="errorMessage">Error message if parsing fails</param>
        /// <returns>FilterNode tree, or null if parsing fails</returns>
        public FilterNode Parse(string query, out string errorMessage)
        {
            errorMessage = null;
            _originalQuery = query;

            if (string.IsNullOrWhiteSpace(query))
            {
                errorMessage = "Query is empty";
                return null;
            }

            try
            {
                // Tokenize the input
                _tokens = Tokenize(query);
                _currentTokenIndex = 0;

                // Parse the expression
                var result = ParseExpression();

                // Check for unexpected tokens at the end
                if (CurrentToken.Type != TokenType.EndOfInput)
                {
                    errorMessage = $"Unexpected token at position {CurrentToken.Position}: '{CurrentToken.Value}'";
                    return null;
                }

                return result;
            }
            catch (Exception ex)
            {
                errorMessage = $"Parse error: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Checks if the query contains special boolean operators.
        /// Use this to determine if smart parsing is needed.
        /// </summary>
        public static bool HasBooleanOperators(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            return query.Contains("\"") ||
                   query.Contains("(") ||
                   query.Contains(")") ||
                   query.Contains("-") ||
                   query.IndexOf(" OR ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   query.IndexOf(" AND ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   query.IndexOf(" NOT ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   query.Contains(" | ");
        }

        private Token CurrentToken => _tokens[_currentTokenIndex];

        private void Advance()
        {
            if (_currentTokenIndex < _tokens.Count - 1)
                _currentTokenIndex++;
        }

        private List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < input.Length)
            {
                char c = input[i];

                // Skip whitespace
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // Handle quoted phrases
                if (c == '"')
                {
                    int start = i;
                    i++; // Skip opening quote
                    var phrase = new StringBuilder();

                    while (i < input.Length && input[i] != '"')
                    {
                        phrase.Append(input[i]);
                        i++;
                    }

                    if (i >= input.Length)
                    {
                        throw new Exception($"Unclosed quote at position {start}");
                    }

                    i++; // Skip closing quote
                    tokens.Add(new Token(TokenType.Phrase, phrase.ToString(), start));
                    continue;
                }

                // Handle parentheses
                if (c == '(')
                {
                    tokens.Add(new Token(TokenType.LeftParen, "(", i));
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    tokens.Add(new Token(TokenType.RightParen, ")", i));
                    i++;
                    continue;
                }

                // Handle NOT operator (-)
                if (c == '-')
                {
                    tokens.Add(new Token(TokenType.Not, "-", i));
                    i++;
                    continue;
                }

                // Handle OR operator (|)
                if (c == '|')
                {
                    tokens.Add(new Token(TokenType.Or, "|", i));
                    i++;
                    continue;
                }

                // Handle words and operators
                int wordStart = i;
                var word = new StringBuilder();

                while (i < input.Length && !char.IsWhiteSpace(input[i]) &&
                       input[i] != '(' && input[i] != ')' && input[i] != '"' &&
                       input[i] != '-' && input[i] != '|')
                {
                    word.Append(input[i]);
                    i++;
                }

                string wordStr = word.ToString();

                // Check if it's an operator keyword
                if (wordStr.Equals("OR", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token(TokenType.Or, wordStr, wordStart));
                }
                else if (wordStr.Equals("AND", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token(TokenType.And, wordStr, wordStart));
                }
                else if (wordStr.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token(TokenType.Not, wordStr, wordStart));
                }
                else
                {
                    tokens.Add(new Token(TokenType.Word, wordStr, wordStart));
                }
            }

            tokens.Add(new Token(TokenType.EndOfInput, "", input.Length));
            return tokens;
        }

        // Recursive descent parser methods

        /// <summary>
        /// Expression := OrExpression
        /// </summary>
        private FilterNode ParseExpression()
        {
            return ParseOrExpression();
        }

        /// <summary>
        /// OrExpression := AndExpression (OR AndExpression)*
        /// </summary>
        private FilterNode ParseOrExpression()
        {
            var left = ParseAndExpression();

            while (CurrentToken.Type == TokenType.Or)
            {
                Advance(); // Consume OR
                var right = ParseAndExpression();

                // Create OR group
                var orGroup = new FilterNode
                {
                    Type = NodeType.Group,
                    LogicalOperator = "OR"
                };
                orGroup.Children.Add(left);
                orGroup.Children.Add(right);
                left = orGroup;
            }

            return left;
        }

        /// <summary>
        /// AndExpression := NotExpression (AND? NotExpression)*
        /// </summary>
        private FilterNode ParseAndExpression()
        {
            var left = ParseNotExpression();

            while (CurrentToken.Type == TokenType.Word ||
                   CurrentToken.Type == TokenType.Phrase ||
                   CurrentToken.Type == TokenType.LeftParen ||
                   CurrentToken.Type == TokenType.Not ||
                   CurrentToken.Type == TokenType.And)
            {
                // Consume explicit AND if present
                if (CurrentToken.Type == TokenType.And)
                {
                    Advance();
                }

                // Check if we're at the end or a closing paren
                if (CurrentToken.Type == TokenType.RightParen ||
                    CurrentToken.Type == TokenType.EndOfInput)
                {
                    break;
                }

                var right = ParseNotExpression();

                // Create AND group
                var andGroup = new FilterNode
                {
                    Type = NodeType.Group,
                    LogicalOperator = "AND"
                };
                andGroup.Children.Add(left);
                andGroup.Children.Add(right);
                left = andGroup;
            }

            return left;
        }

        /// <summary>
        /// NotExpression := NOT? PrimaryExpression
        /// </summary>
        private FilterNode ParseNotExpression()
        {
            bool isNegated = false;

            if (CurrentToken.Type == TokenType.Not)
            {
                isNegated = true;
                Advance(); // Consume NOT or -
            }

            var node = ParsePrimaryExpression();

            if (isNegated)
            {
                // Wrap in NOT AND group
                var notGroup = new FilterNode
                {
                    Type = NodeType.Group,
                    LogicalOperator = "NOT AND"
                };
                notGroup.Children.Add(node);
                return notGroup;
            }

            return node;
        }

        /// <summary>
        /// PrimaryExpression := Word | Phrase | ( Expression )
        /// </summary>
        private FilterNode ParsePrimaryExpression()
        {
            var token = CurrentToken;

            if (token.Type == TokenType.Word || token.Type == TokenType.Phrase)
            {
                Advance();
                return new FilterNode
                {
                    Type = NodeType.Condition,
                    Field = "Message",
                    Operator = "Contains",
                    Value = token.Value
                };
            }

            if (token.Type == TokenType.LeftParen)
            {
                Advance(); // Consume (
                var expr = ParseExpression();

                if (CurrentToken.Type != TokenType.RightParen)
                {
                    throw new Exception($"Expected ')' at position {CurrentToken.Position}");
                }

                Advance(); // Consume )
                return expr;
            }

            throw new Exception($"Unexpected token at position {token.Position}: '{token.Value}'");
        }
    }
}
