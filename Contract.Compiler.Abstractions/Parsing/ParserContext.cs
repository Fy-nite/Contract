using System;
using System.Collections.Generic;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;

namespace Contract.Compiler.Parsing
{
    /// <summary>
    /// Wraps the mutable token-stream state and primitive parsing operations
    /// shared between the C# Parser and the F# ExpressionParser. The F# code
    /// programs against this class so it never needs to reference the Parser
    /// class directly.
    /// </summary>
    public class ParserContext
    {
        private readonly List<Token> _tokens;
        private int _current;
        private readonly DiagnosticBag _diagnostics;
        private readonly string? _sourceFile;

        public ParserContext(IEnumerable<Token> tokens, DiagnosticBag diagnostics, string? sourceFile = null)
        {
            _tokens = new List<Token>(tokens);
            _diagnostics = diagnostics;
            _sourceFile = sourceFile;
        }

        // ── Cursor ──────────────────────────────────────────────────

        public Token Current => _tokens[_current];

        public Token Previous => _tokens[_current - 1];

        public int Cursor
        {
            get => _current;
            set => _current = value;
        }

        public IReadOnlyList<Token> Tokens => _tokens;

        public bool IsAtEnd() => Current.Type == TokenType.EOF;

        // ── Primitives ──────────────────────────────────────────────

        public Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous;
        }

        public bool Check(TokenType type) => !IsAtEnd() && Current.Type == type;

        public bool CheckNext(TokenType type)
        {
            if (IsAtEnd() || _current + 1 >= _tokens.Count) return false;
            return _tokens[_current + 1].Type == type;
        }

        public bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        public Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            AddError(message, Current.Line, Current.Column);
            return new Token(TokenType.Identifier, "", Current.Line, Current.Column);
        }

        // ── Diagnostics ─────────────────────────────────────────────

        public void AddError(string message, int line, int column)
            => _diagnostics.AddError(message, line, column, _sourceFile);

        public void AddWarning(string message, int line, int column)
            => _diagnostics.AddWarning(message, line, column, _sourceFile);

        // ── Contextual keyword helpers ───────────────────────────────

        /// <summary>
        /// True when the current token is "fn" followed by <c>(</c> — the start
        /// of an anonymous lambda using the Construct-style <c>fn</c> keyword.
        /// </summary>
        public bool CheckFnLambda()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.LParen;

        /// <summary>
        /// True when the current token is an identifier with text "fn" followed
        /// by another identifier — the start of a function declaration.
        /// </summary>
        public bool CheckFn()
            => Current.Type == TokenType.Identifier && Current.Text == "fn"
               && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Identifier;

        /// <summary>Consumes the current token when it is a contextual "fn" keyword.</summary>
        public bool MatchFn()
        {
            if (CheckFn()) { Advance(); return true; }
            return false;
        }

        // ── Token list mutation ──────────────────────────────────────

        /// <summary>
        /// Splits the current <c>>></c> token in place into two <c>&gt;</c> tokens,
        /// so nested generic closes like <c>List&lt;List&lt;int&gt;&gt;</c> parse correctly.
        /// </summary>
        public void SplitGreaterGreater()
        {
            if (Current.Type != TokenType.GreaterGreater) return;
            var tok = Current;
            _tokens[_current] = new Token(TokenType.Greater, ">", tok.Line, tok.Column, 1);
            _tokens.Insert(_current + 1, new Token(TokenType.Greater, ">", tok.Line, tok.Column + 1, 1));
        }

        // ── For-in range suppression ─────────────────────────────────

        /// <summary>
        /// When positive, the postfix <c>..</c> range operator is left
        /// unconsumed — set by for-in header parsing so the header can
        /// claim <c>0..10</c> as its own range form.
        /// </summary>
        public int SuppressRangeDepth { get; set; }
    }
}
