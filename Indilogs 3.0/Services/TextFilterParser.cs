using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Parses DevExpress-style criteria strings into a FilterNode tree.
    ///
    /// Supported syntax:
    ///   StartsWith([Thread], 'value')   →  Field=ThreadName, Operator=Begins With
    ///   Contains([Message], 'value')    →  Field=Message,    Operator=Contains
    ///   EndsWith([Level], 'value')      →  Field=Level,      Operator=Ends With
    ///   Equals([Logger], 'value')       →  Field=Logger,     Operator=Equals
    ///   A Or B And C                   →  A Or (B And C)   — And has higher precedence
    ///   A And (B Or C)                 →  explicit grouping
    /// </summary>
    public static class TextFilterParser
    {
        #region Field / Operator Maps

        private static readonly Dictionary<string, string> FieldMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Thread",     "ThreadName" },
                { "ThreadName", "ThreadName" },
                { "Message",    "Message"    },
                { "Level",      "Level"      },
                { "Logger",     "Logger"     },
                { "ProcessName","ProcessName"},
            };

        private static readonly Dictionary<string, string> OperatorMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "StartsWith", "Begins With" },
                { "Contains",   "Contains"    },
                { "EndsWith",   "Ends With"   },
                { "Equals",     "Equals"      },
            };

        #endregion

        #region Token Types

        private enum TT { Func, Field, Value, Or, And, Not, LParen, RParen, Comma }

        private struct Token
        {
            public Token() { }
            public TT     Type;
            public string Text = "";
        }

        #endregion

        #region Public API

        /// <summary>
        /// Parse <paramref name="filterText"/> into a FilterNode tree.
        /// Returns null if the text is blank.
        /// Throws <see cref="FormatException"/> on syntax errors.
        /// </summary>
        public static FilterNode? Parse(string? filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return null;

            var tokens = Tokenize(filterText);
            int pos    = 0;
            var root   = ParseOr(tokens, ref pos);

            if (pos < tokens.Count)
                throw new FormatException($"Unexpected token after expression: '{tokens[pos].Text}'");

            return root;
        }

        #endregion

        #region Recursive-descent parser  (Or < And)

        // expr   = orExpr
        // orExpr = andExpr (Or andExpr)*   — Or has LOWER precedence
        private static FilterNode ParseOr(List<Token> tokens, ref int pos)
        {
            var children = new List<FilterNode> { ParseAnd(tokens, ref pos) };

            while (pos < tokens.Count && tokens[pos].Type == TT.Or)
            {
                pos++;   // consume 'Or'
                children.Add(ParseAnd(tokens, ref pos));
            }

            if (children.Count == 1) return children[0];

            var group = new FilterNode { Type = NodeType.Group, LogicalOperator = "OR" };
            foreach (var c in children) group.Children.Add(c);
            return group;
        }

        // andExpr = atom (And atom)*        — And has HIGHER precedence
        private static FilterNode ParseAnd(List<Token> tokens, ref int pos)
        {
            var children = new List<FilterNode> { ParseAtom(tokens, ref pos) };

            while (pos < tokens.Count && tokens[pos].Type == TT.And)
            {
                pos++;   // consume 'And'
                children.Add(ParseAtom(tokens, ref pos));
            }

            if (children.Count == 1) return children[0];

            var group = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" };
            foreach (var c in children) group.Children.Add(c);
            return group;
        }

        // atom = '(' expr ')' | condition
        private static FilterNode ParseAtom(List<Token> tokens, ref int pos)
        {
            if (pos >= tokens.Count)
                throw new FormatException("Unexpected end of filter expression.");

            if (tokens[pos].Type == TT.LParen)
            {
                pos++;   // consume '('
                var inner = ParseOr(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos].Type == TT.RParen)
                    pos++;   // consume ')'
                else
                    throw new FormatException("Missing closing ')'.");
                return inner;
            }

            if (tokens[pos].Type == TT.Func)
                return ParseCondition(tokens, ref pos);

            throw new FormatException($"Unexpected token: '{tokens[pos].Text}'");
        }

        // condition = FuncName '(' '[' field ']' ',' '\'' value '\'' ')'
        private static FilterNode ParseCondition(List<Token> tokens, ref int pos)
        {
            string funcName = tokens[pos++].Text;   // e.g. "StartsWith"

            Expect(tokens, ref pos, TT.LParen, "(");

            if (pos >= tokens.Count || tokens[pos].Type != TT.Field)
                throw new FormatException($"Expected [FieldName] after '{funcName}('.");
            string fieldRaw = tokens[pos++].Text;   // e.g. "Thread"

            Expect(tokens, ref pos, TT.Comma, ",");

            if (pos >= tokens.Count || tokens[pos].Type != TT.Value)
                throw new FormatException($"Expected 'value' in {funcName}([{fieldRaw}], ...).");
            string value = tokens[pos++].Text;

            Expect(tokens, ref pos, TT.RParen, ")");

            if (!FieldMap.TryGetValue(fieldRaw, out string field))
                field = fieldRaw;   // unknown field — pass through as-is

            if (!OperatorMap.TryGetValue(funcName, out string op))
                op = "Contains";    // unknown function → default to Contains

            return new FilterNode
            {
                Type            = NodeType.Condition,
                Field           = field,
                Operator        = op,
                Value           = value,
                LogicalOperator = "AND",
                Children        = new ObservableCollection<FilterNode>()
            };
        }

        private static void Expect(List<Token> tokens, ref int pos, TT expectedType, string expectedText)
        {
            if (pos >= tokens.Count || tokens[pos].Type != expectedType)
                throw new FormatException($"Expected '{expectedText}' but got '{(pos < tokens.Count ? tokens[pos].Text : "EOF")}'.");
            pos++;
        }

        #endregion

        #region Tokenizer

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int i      = 0;

            while (i < text.Length)
            {
                // Skip whitespace
                if (char.IsWhiteSpace(text[i])) { i++; continue; }

                // Parentheses
                if (text[i] == '(') { tokens.Add(Tok(TT.LParen, "(")); i++; continue; }
                if (text[i] == ')') { tokens.Add(Tok(TT.RParen, ")")); i++; continue; }
                if (text[i] == ',') { tokens.Add(Tok(TT.Comma,  ",")); i++; continue; }

                // Single-quoted string: 'value'
                if (text[i] == '\'')
                {
                    i++;   // skip opening '
                    int start = i;
                    while (i < text.Length && text[i] != '\'') i++;
                    tokens.Add(Tok(TT.Value, text.Substring(start, i - start)));
                    if (i < text.Length) i++;   // skip closing '
                    continue;
                }

                // Field identifier: [FieldName]
                if (text[i] == '[')
                {
                    i++;   // skip [
                    int start = i;
                    while (i < text.Length && text[i] != ']') i++;
                    tokens.Add(Tok(TT.Field, text.Substring(start, i - start)));
                    if (i < text.Length) i++;   // skip ]
                    continue;
                }

                // Keyword or function name
                if (char.IsLetter(text[i]) || text[i] == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string id = text.Substring(start, i - start);

                    if (string.Equals(id, "Or",  StringComparison.OrdinalIgnoreCase))
                        tokens.Add(Tok(TT.Or,  id));
                    else if (string.Equals(id, "And", StringComparison.OrdinalIgnoreCase))
                        tokens.Add(Tok(TT.And, id));
                    else if (string.Equals(id, "Not", StringComparison.OrdinalIgnoreCase))
                        tokens.Add(Tok(TT.Not, id));
                    else
                        tokens.Add(Tok(TT.Func, id));
                    continue;
                }

                // Unknown character — skip
                i++;
            }

            return tokens;
        }

        private static Token Tok(TT type, string text) => new Token { Type = type, Text = text };

        #endregion
    }
}
